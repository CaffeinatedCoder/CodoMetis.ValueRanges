using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The single place where range types are wired to PostgreSQL. Supporting a future range
/// type added to <c>CodoMetis.ValueRanges</c> means adding one
/// <see cref="RangeTypeDefinition{TRange,T}"/> line here — mappings, multirange support,
/// and all query translations follow automatically.
/// </summary>
internal static class RangeTypeRegistry
{
    /// <summary>All registered range type definitions.</summary>
    public static IReadOnlyList<IRangeTypeDefinition> Definitions { get; } =
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
        ];

    private static readonly FrozenDictionary<Type, (IRangeTypeDefinition Definition, bool IsSet)> ByClrType =
        Definitions
           .SelectMany(definition => new[]
                                     {
                                         KeyValuePair.Create(definition.RangeClrType,    (definition, IsSet: false)),
                                         KeyValuePair.Create(definition.RangeSetClrType, (definition, IsSet: true))
                                     })
           .ToFrozenDictionary();

    private static readonly FrozenDictionary<string, (IRangeTypeDefinition Definition, bool IsSet)> ByStoreType =
        Definitions
           .SelectMany(definition => new[]
                                     {
                                         KeyValuePair.Create(definition.RangeStoreType,      (definition, IsSet: false)),
                                         KeyValuePair.Create(definition.MultirangeStoreType, (definition, IsSet: true))
                                     })
           .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<Type, IRangeTypeDefinition> ByElementType =
        Definitions.ToFrozenDictionary(definition => definition.ElementClrType);

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
        if (ByClrType.TryGetValue(clrType, out var entry))
        {
            (definition, isSet) = entry;
            return true;
        }

        (definition, isSet) = (null, false);
        return false;
    }

    /// <summary>
    /// Looks up a definition by PostgreSQL store type name (range or multirange);
    /// <paramref name="isSet"/> tells which of the two matched.
    /// </summary>
    public static bool TryGetByStoreType(
        string                                        storeType,
        [NotNullWhen(true)] out IRangeTypeDefinition? definition,
        out                     bool                  isSet
    )
    {
        if (ByStoreType.TryGetValue(storeType, out var entry))
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
        => ByElementType.TryGetValue(elementType, out definition);
}