using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The single place where range types are wired to PostgreSQL. The six built-in range types
/// are registered up front; satellite packages (e.g. the NodaTime companion) contribute
/// additional <see cref="RangeTypeDefinition{TRange,T}"/>s via <see cref="Register"/> at
/// options-configuration time — mappings, multirange support, and all query translations
/// follow automatically.
/// </summary>
/// <remarks>
/// Definitions are global type wirings — a range CLR type binds to the same PostgreSQL type
/// in every context — so the registry is deliberately process-wide. Registration is additive,
/// idempotent per range CLR type, and thread-safe: lookups read an immutable snapshot that is
/// atomically replaced under a lock on registration.
/// </remarks>
internal static class RangeTypeRegistry
{
    private sealed record Snapshot(
        ImmutableArray<IRangeTypeDefinition>                            Definitions,
        FrozenDictionary<Type, (IRangeTypeDefinition Definition, bool IsSet)>   ByClrType,
        FrozenDictionary<string, (IRangeTypeDefinition Definition, bool IsSet)> ByStoreType,
        FrozenDictionary<Type, IRangeTypeDefinition>                    ByElementType,
        FrozenSet<Type>                                                 AggregateDeclaringTypes
    );

    private static readonly Lock RegistrationLock = new();

    private static volatile Snapshot Current = BuildSnapshot(
        [
            new RangeTypeDefinition<Int32Range, int>("int4range", "int4multirange", "integer"),
            new RangeTypeDefinition<Int64Range, long>("int8range", "int8multirange", "bigint"),
            new RangeTypeDefinition<DecimalRange, decimal>("numrange", "nummultirange", "numeric"),
            new RangeTypeDefinition<DateRange, DateOnly>("daterange", "datemultirange", "date"),

            // PostgreSQL `timestamp` has no time zone: Npgsql rejects UTC-kinded values, so
            // bounds are written as wall-clock time with DateTimeKind.Unspecified.
            new RangeTypeDefinition<DateTimeRange, DateTime>(
                "tsrange", "tsmultirange", "timestamp without time zone",
                value => DateTime.SpecifyKind(value, DateTimeKind.Unspecified)),

            // PostgreSQL `timestamptz` stores an instant: Npgsql requires offset zero, so bounds
            // are normalized to UTC. This preserves the instant (DateTimeOffset compares by UTC ticks).
            new RangeTypeDefinition<DateTimeOffsetRange, DateTimeOffset>(
                "tstzrange", "tstzmultirange", "timestamp with time zone",
                value => value.ToUniversalTime())
        ],
        [typeof(RangeAggregateExtensions)]);

    /// <summary>All registered range type definitions.</summary>
    public static IReadOnlyList<IRangeTypeDefinition> Definitions => Current.Definitions;

    /// <summary>
    /// Registers an additional range type definition. Safe to call repeatedly — a definition
    /// whose range CLR type is already registered is ignored, so satellite packages can
    /// register from their options-builder extension on every context configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A different range type is already registered for the same element CLR type — the
    /// element type is the fallback lookup for operands statically typed as
    /// <c>IRange&lt;T&gt;</c>, so it must resolve unambiguously.
    /// </exception>
    /// <remarks>
    /// Definitions may share PostgreSQL store type names with earlier registrations (the
    /// NodaTime <c>LocalDateRange</c> binds to <c>daterange</c> just like <c>DateRange</c>).
    /// Lookups by CLR type are always unambiguous; lookups by store type name alone keep the
    /// first-registered owner, so the built-in types remain the default for scaffolding-style
    /// resolution.
    /// </remarks>
    public static void Register(IRangeTypeDefinition definition)
    {
        if (Current.ByClrType.ContainsKey(definition.RangeClrType)) return;

        lock (RegistrationLock)
        {
            if (Current.ByClrType.ContainsKey(definition.RangeClrType)) return;

            if (Current.ByElementType.TryGetValue(definition.ElementClrType, out var existing))
            {
                throw new InvalidOperationException(
                    $"Cannot register range type '{definition.RangeClrType}' for element type "      +
                    $"'{definition.ElementClrType}': '{existing.RangeClrType}' is already registered " +
                    "for that element type.");
            }

            Current = BuildSnapshot(
                [.. Current.Definitions, definition],
                Current.AggregateDeclaringTypes);
        }
    }

    /// <summary>
    /// Registers a static class whose <c>RangeAgg</c>/<c>RangeIntersectAgg</c> overloads the
    /// aggregate translator should recognize. The core <see cref="RangeAggregateExtensions"/>
    /// is pre-registered; satellite packages register their own per-type overload classes.
    /// Safe to call repeatedly.
    /// </summary>
    public static void RegisterAggregateExtensions(Type declaringType)
    {
        if (Current.AggregateDeclaringTypes.Contains(declaringType)) return;

        lock (RegistrationLock)
        {
            if (Current.AggregateDeclaringTypes.Contains(declaringType)) return;

            Current = BuildSnapshot(
                Current.Definitions,
                [.. Current.AggregateDeclaringTypes, declaringType]);
        }
    }

    /// <summary>
    /// Whether <paramref name="declaringType"/> hosts registered
    /// <c>RangeAgg</c>/<c>RangeIntersectAgg</c> aggregate overloads.
    /// </summary>
    public static bool IsAggregateDeclaringType(Type? declaringType)
        => declaringType is not null && Current.AggregateDeclaringTypes.Contains(declaringType);

    private static Snapshot BuildSnapshot(
        ImmutableArray<IRangeTypeDefinition> definitions,
        IEnumerable<Type>                    aggregateDeclaringTypes
    )
    {
        var byClrType = definitions
           .SelectMany(definition => new[]
                                     {
                                         KeyValuePair.Create(definition.RangeClrType,    (definition, IsSet: false)),
                                         KeyValuePair.Create(definition.RangeSetClrType, (definition, IsSet: true))
                                     })
           .ToFrozenDictionary();

        // Store type names may be shared across definitions (BCL and NodaTime types bind to
        // the same PostgreSQL types) — the first-registered definition owns the name.
        var byStoreType = definitions
           .SelectMany(definition => new[]
                                     {
                                         KeyValuePair.Create(definition.RangeStoreType,      (definition, IsSet: false)),
                                         KeyValuePair.Create(definition.MultirangeStoreType, (definition, IsSet: true))
                                     })
           .DistinctBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
           .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        var byElementType = definitions.ToFrozenDictionary(definition => definition.ElementClrType);

        return new Snapshot(
            definitions,
            byClrType,
            byStoreType,
            byElementType,
            aggregateDeclaringTypes.ToFrozenSet());
    }

    /// <summary>
    /// Looks up a definition by range or set CLR type;
    /// <paramref name="isSet"/> tells which of the two matched.
    /// </summary>
    public static bool TryGetByClrType(
        Type                                          clrType,
        [NotNullWhen(true)] out IRangeTypeDefinition? definition,
        out                     bool                  isSet
    )
    {
        if (Current.ByClrType.TryGetValue(clrType, out var entry))
        {
            (definition, isSet) = entry;
            return true;
        }

        (definition, isSet) = (null, false);
        return false;
    }

    /// <summary>
    /// Looks up a definition by PostgreSQL store type name (range or multirange);
    /// <paramref name="isSet"/> tells which of the two matched. When several definitions
    /// share a store type name, the first-registered one is returned.
    /// </summary>
    public static bool TryGetByStoreType(
        string                                        storeType,
        [NotNullWhen(true)] out IRangeTypeDefinition? definition,
        out                     bool                  isSet
    )
    {
        if (Current.ByStoreType.TryGetValue(storeType, out var entry))
        {
            (definition, isSet) = entry;
            return true;
        }

        (definition, isSet) = (null, false);
        return false;
    }

    /// <summary>
    /// Looks up a definition by range element type — the fallback when an expression is
    /// statically typed as <c>IRange&lt;T&gt;</c> and only <c>T</c> is known.
    /// </summary>
    public static bool TryGetByElementType(Type elementType, [NotNullWhen(true)] out IRangeTypeDefinition? definition)
        => Current.ByElementType.TryGetValue(elementType, out definition);
}
