using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The single place where value set types are wired to PostgreSQL, sibling of
/// <see cref="RangeTypeRegistry"/>. The core closed types are registered up front; the
/// validated-wrapper families (<c>StringSet&lt;&gt;</c>, <c>GuidSet&lt;&gt;</c>, the integer
/// and <c>DecimalSet&lt;&gt;</c> arities, and the four temporal ones) are matched by open
/// generic definition and their closed instantiations built lazily and cached — no per-element
/// registration exists, so there is nothing to misconfigure. Satellite packages (e.g. the
/// NodaTime companion) contribute additional closed definitions via <see cref="Register"/> and
/// additional families via <see cref="RegisterFamily"/> at options-configuration time.
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

    private static volatile FrozenDictionary<Type, Func<Type, ISetTypeDefinition>> Families =
        new Dictionary<Type, Func<Type, ISetTypeDefinition>>
        {
            // The string family has no primitive conversion to configure: the element's text
            // form is the store value, so it carries its own two-parameter definition.
            [typeof(StringSet<>)] = static closed => (ISetTypeDefinition)Activator.CreateInstance(
                typeof(StringBridgedSetTypeDefinition<,>)
                   .MakeGenericType(closed, closed.GetGenericArguments()[0]))!,

            // The element format is null wherever the element's own default text form already
            // round-trips through the primitive. That is true of Guid and the integers, and of
            // decimal — and false of every temporal below, whose default form silently drops
            // sub-seconds (and, for DateTime, the Kind).
            [typeof(GuidSet<>)] = Bridged<Guid>(
                "uuid", null, static text => Guid.Parse(text, Invariant), Text),
            [typeof(Int16Set<>)] = Bridged<short>(
                "smallint", null, static text => short.Parse(text, Invariant), Text),
            [typeof(Int32Set<>)] = Bridged<int>(
                "integer", null, static text => int.Parse(text, Invariant), Text),
            [typeof(Int64Set<>)] = Bridged<long>(
                "bigint", null, static text => long.Parse(text, Invariant), Text),
            [typeof(DecimalSet<>)] = Bridged<decimal>(
                "numeric", null, static text => decimal.Parse(text, NumberStyles.Number, Invariant), Text),

            // ParseExact, not Parse: an element that ignores the format specifier and hands back
            // its own default form is a contract violation, and the strict parse turns it into
            // the loud InvalidOperationException the bridge raises rather than a value quietly
            // truncated to whole minutes or seconds.
            [typeof(DateSet<>)] = Bridged<DateOnly>(
                "date", "yyyy-MM-dd",
                static text => DateOnly.ParseExact(text, "yyyy-MM-dd", Invariant),
                static value => value.ToString("yyyy-MM-dd", Invariant)),
            [typeof(TimeSet<>)] = Bridged<TimeOnly>(
                "time without time zone", "O",
                static text => TimeOnly.ParseExact(text, "O", Invariant),
                static value => value.ToString("O", Invariant)),

            // The same store-side normalization the closed siblings apply: PostgreSQL
            // `timestamp` has no time zone, so elements are written as wall-clock time with
            // DateTimeKind.Unspecified; `timestamptz` stores an instant, so they normalize to
            // UTC. RoundtripKind is what makes the first one reproduce SpecifyKind on the
            // original value instead of reinterpreting a UTC element as local.
            [typeof(DateTimeSet<>)] = Bridged<DateTime>(
                "timestamp without time zone", "O",
                static text => DateTime.SpecifyKind(
                    DateTime.ParseExact(text, "O", Invariant, DateTimeStyles.RoundtripKind),
                    DateTimeKind.Unspecified),
                static value => value.ToString("O", Invariant)),
            [typeof(DateTimeOffsetSet<>)] = Bridged<DateTimeOffset>(
                "timestamp with time zone", "O",
                static text => DateTimeOffset
                              .ParseExact(text, "O", Invariant, DateTimeStyles.AssumeUniversal)
                              .ToUniversalTime(),
                static value => value.ToString("O", Invariant))
        }.ToFrozenDictionary();

    private static CultureInfo Invariant => CultureInfo.InvariantCulture;

    /// <summary>The invariant text form of a primitive whose default already round-trips.</summary>
    private static string Text<TPrimitive>(TPrimitive value) where TPrimitive : IFormattable
        => value.ToString(null, Invariant);

    /// <summary>
    /// Builds the factory for one wrapper family: given the closed set type, it closes
    /// <see cref="BridgedSetTypeDefinition{TSet,TElement,TPrimitive}"/> over the element type
    /// and hands it the family's text bridge. Also the seam satellites use — see
    /// <see cref="RegisterFamily"/>.
    /// </summary>
    /// <param name="elementStoreType">The PostgreSQL type of one element.</param>
    /// <param name="elementFormat">
    /// The format specifier handed to the element's <see cref="IFormattable"/>, or
    /// <see langword="null"/> for its default.
    /// </param>
    /// <param name="parsePrimitive">The element's text form to the store primitive.</param>
    /// <param name="formatPrimitive">The store primitive back to text the element can parse.</param>
    /// <param name="literalText">The primitive's SQL literal form; defaults to the invariant one.</param>
    internal static Func<Type, ISetTypeDefinition> Bridged<TPrimitive>(
        string                    elementStoreType,
        string?                   elementFormat,
        Func<string, TPrimitive>  parsePrimitive,
        Func<TPrimitive, string>  formatPrimitive,
        Func<TPrimitive, string>? literalText = null
    )
        where TPrimitive : struct
        => closed =>
        {
            var definitionType = typeof(BridgedSetTypeDefinition<,,>)
               .MakeGenericType(closed, closed.GetGenericArguments()[0], typeof(TPrimitive));

            return (ISetTypeDefinition)Activator.CreateInstance(
                definitionType,
                [elementStoreType, elementFormat, parsePrimitive, formatPrimitive, literalText])!;
        };

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
    /// A different set type is already registered for the same element CLR type. Nothing resolves
    /// a definition by element type any more, so this is a registration sanity check rather than a
    /// correctness requirement: two set types over one element type is almost always a satellite
    /// registering the same family twice under different names, and the specific message beats the
    /// silence. <see cref="RangeTypeRegistry"/> carries the identical check.
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

    /// <summary>
    /// Registers an additional validated-wrapper family — an open generic set type whose closed
    /// instantiations are built on demand by <paramref name="factory"/>. The counterpart of
    /// <see cref="Register"/> for the arities: a family cannot be registered as closed
    /// definitions because its element type is whatever the consumer supplies.
    /// </summary>
    /// <remarks>
    /// Safe to call repeatedly — an already-registered family is ignored, so a satellite can
    /// register from its options-builder extension on every context configuration. There is no
    /// element-type collision check as in <see cref="Register"/>: a family claims no element
    /// type of its own, and the closed instantiations it produces are reachable by element type
    /// only once they have been built.
    /// </remarks>
    /// <param name="openGenericSetType">The family, e.g. <c>typeof(LocalDateSet&lt;&gt;)</c>.</param>
    /// <param name="factory">Builds the definition for one closed instantiation.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="openGenericSetType"/> is not an open generic type of arity one.
    /// </exception>
    public static void RegisterFamily(Type openGenericSetType, Func<Type, ISetTypeDefinition> factory)
    {
        ArgumentNullException.ThrowIfNull(openGenericSetType);
        ArgumentNullException.ThrowIfNull(factory);

        if (!openGenericSetType.IsGenericTypeDefinition || openGenericSetType.GetGenericArguments().Length != 1)
        {
            throw new ArgumentException(
                $"'{openGenericSetType}' is not an open generic set type of arity one. A wrapper "
              + "family is registered as e.g. typeof(LocalDateSet<>), and its closed "
              + "instantiations are built on demand.", nameof(openGenericSetType));
        }

        if (Families.ContainsKey(openGenericSetType)) return;

        lock (RegistrationLock)
        {
            if (Families.ContainsKey(openGenericSetType)) return;

            Families = new Dictionary<Type, Func<Type, ISetTypeDefinition>>(Families)
                       {
                           [openGenericSetType] = factory
                       }.ToFrozenDictionary();
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
}
