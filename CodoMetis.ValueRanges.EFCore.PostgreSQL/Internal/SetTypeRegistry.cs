using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The single place where value set types are wired to PostgreSQL, sibling of
/// <see cref="RangeTypeRegistry"/>. The core closed types are registered up front; the four
/// validated-wrapper families (<c>StringSet&lt;&gt;</c>, <c>GuidSet&lt;&gt;</c>,
/// <c>Int32Set&lt;&gt;</c>, <c>Int64Set&lt;&gt;</c>) are matched by open generic definition
/// and their closed instantiations built lazily and cached — no per-element registration
/// exists, so there is nothing to misconfigure. Satellite packages (e.g. the NodaTime
/// companion) contribute additional closed definitions via <see cref="Register"/> at
/// options-configuration time.
/// </summary>
/// <remarks>
/// Unlike <see cref="RangeTypeRegistry"/>, there is deliberately no lookup by store type
/// name: <c>text[]</c> and friends belong to the provider's native array mappings for
/// <c>string[]</c> etc., and claiming them would hijack plain array properties and
/// scaffolding. Value set columns are always resolved from the CLR type.
/// </remarks>
internal static class SetTypeRegistry
{
    private sealed record Snapshot(
        ImmutableArray<ISetTypeDefinition>         Definitions,
        FrozenDictionary<Type, ISetTypeDefinition> ByClrType,
        FrozenDictionary<Type, ISetTypeDefinition> ByElementType
    );

    private static readonly Lock RegistrationLock = new();

    /// <summary>Lazily built definitions for closed wrapper-family instantiations.</summary>
    private static readonly ConcurrentDictionary<Type, ISetTypeDefinition> FamilyCache = new();

    private static readonly FrozenDictionary<Type, Func<Type, ISetTypeDefinition>> Families =
        new Dictionary<Type, Func<Type, ISetTypeDefinition>>
        {
            [typeof(StringSet<>)] = static closed => BuildFamilyDefinition(
                typeof(StringBridgedSetTypeDefinition<,>), closed, primitive: null, elementStoreType: null),
            [typeof(GuidSet<>)] = static closed => BuildFamilyDefinition(
                typeof(BridgedSetTypeDefinition<,,>), closed, typeof(Guid), "uuid"),
            [typeof(Int32Set<>)] = static closed => BuildFamilyDefinition(
                typeof(BridgedSetTypeDefinition<,,>), closed, typeof(int), "integer"),
            [typeof(Int64Set<>)] = static closed => BuildFamilyDefinition(
                typeof(BridgedSetTypeDefinition<,,>), closed, typeof(long), "bigint")
        }.ToFrozenDictionary();

    private static volatile Snapshot Current = BuildSnapshot(
        [
            new SetTypeDefinition<StringSet, string>("text"),
            new SetTypeDefinition<GuidSet, Guid>("uuid"),
            new SetTypeDefinition<Int16Set, short>("smallint"),
            new SetTypeDefinition<Int32Set, int>("integer"),
            new SetTypeDefinition<Int64Set, long>("bigint"),
            new SetTypeDefinition<DecimalSet, decimal>("numeric"),
            new SetTypeDefinition<DateSet, DateOnly>("date"),
            new SetTypeDefinition<TimeSet, TimeOnly>("time without time zone"),

            // PostgreSQL `timestamp` has no time zone: Npgsql rejects UTC-kinded values, so
            // elements are written as wall-clock time with DateTimeKind.Unspecified.
            new SetTypeDefinition<DateTimeSet, DateTime>(
                "timestamp without time zone",
                static value => DateTime.SpecifyKind(value, DateTimeKind.Unspecified)),

            // PostgreSQL `timestamptz` stores an instant: Npgsql requires offset zero, so
            // elements are normalized to UTC. This preserves the instant (equality and
            // canonical order are instant-based).
            new SetTypeDefinition<DateTimeOffsetSet, DateTimeOffset>(
                "timestamp with time zone",
                static value => value.ToUniversalTime())
        ]);

    /// <summary>All registered closed set type definitions (excluding cached family instantiations).</summary>
    public static IReadOnlyList<ISetTypeDefinition> Definitions => Current.Definitions;

    /// <summary>
    /// Registers an additional closed set type definition. Safe to call repeatedly — a
    /// definition whose set CLR type is already registered is ignored, so satellite packages
    /// can register from their options-builder extension on every context configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A different set type is already registered for the same element CLR type — the element
    /// type is the fallback lookup for interface-typed operands, so it must resolve
    /// unambiguously.
    /// </exception>
    public static void Register(ISetTypeDefinition definition)
    {
        if (Current.ByClrType.ContainsKey(definition.SetClrType)) return;

        lock (RegistrationLock)
        {
            if (Current.ByClrType.ContainsKey(definition.SetClrType)) return;

            if (Current.ByElementType.TryGetValue(definition.ElementClrType, out var existing))
            {
                throw new InvalidOperationException(
                    $"Cannot register set type '{definition.SetClrType}' for element type "        +
                    $"'{definition.ElementClrType}': '{existing.SetClrType}' is already registered " +
                    "for that element type.");
            }

            Current = BuildSnapshot([.. Current.Definitions, definition]);
        }
    }

    private static Snapshot BuildSnapshot(ImmutableArray<ISetTypeDefinition> definitions)
        => new(
            definitions,
            definitions.ToFrozenDictionary(definition => definition.SetClrType),
            definitions.ToFrozenDictionary(definition => definition.ElementClrType));

    /// <summary>
    /// Looks up a definition by set CLR type — closed registrations first, then the wrapper
    /// families by open generic definition (built lazily and cached per instantiation).
    /// </summary>
    public static bool TryGetByClrType(Type clrType, [NotNullWhen(true)] out ISetTypeDefinition? definition)
    {
        if (Current.ByClrType.TryGetValue(clrType, out definition)) return true;
        if (FamilyCache.TryGetValue(clrType, out definition)) return true;

        if (clrType.IsGenericType
            && !clrType.IsGenericTypeDefinition
            && Families.TryGetValue(clrType.GetGenericTypeDefinition(), out var factory))
        {
            definition = FamilyCache.GetOrAdd(clrType, factory);
            return true;
        }

        definition = null;
        return false;
    }

    /// <summary>
    /// Looks up a definition by element type — the fallback when an expression is statically
    /// typed as <c>IValueSet&lt;T&gt;</c> and only <c>T</c> is known. Searches closed
    /// registrations and already-instantiated family definitions.
    /// </summary>
    public static bool TryGetByElementType(Type elementType, [NotNullWhen(true)] out ISetTypeDefinition? definition)
    {
        if (Current.ByElementType.TryGetValue(elementType, out definition)) return true;

        foreach (var cached in FamilyCache.Values)
        {
            if (cached.ElementClrType == elementType)
            {
                definition = cached;
                return true;
            }
        }

        definition = null;
        return false;
    }

    private static ISetTypeDefinition BuildFamilyDefinition(
        Type    openDefinitionType,
        Type    closedSetType,
        Type?   primitive,
        string? elementStoreType
    )
    {
        var element = closedSetType.GetGenericArguments()[0];

        var definitionType = primitive is null
                                 ? openDefinitionType.MakeGenericType(closedSetType, element)
                                 : openDefinitionType.MakeGenericType(closedSetType, element, primitive);

        var arguments = elementStoreType is null ? null : new object[] { elementStoreType };
        return (ISetTypeDefinition)Activator.CreateInstance(definitionType, arguments)!;
    }
}
