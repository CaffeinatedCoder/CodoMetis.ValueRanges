using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// Describes how one range type binds to PostgreSQL: its CLR types, its range and
/// multirange store types, and the corresponding type mappings. The non-generic view lets
/// the mapping source and translators work uniformly over all registered range types.
/// </summary>
internal interface IRangeTypeDefinition
{
    /// <summary>The range type, e.g. <c>DateRange</c>.</summary>
    Type RangeClrType { get; }

    /// <summary>The element type of the range, e.g. <see cref="DateOnly"/>.</summary>
    Type ElementClrType { get; }

    /// <summary>The corresponding set type, e.g. <c>RangeSet&lt;DateRange, DateOnly&gt;</c>.</summary>
    Type RangeSetClrType { get; }

    /// <summary>
    /// The PostgreSQL range type name, e.g. <c>daterange</c>. Also the name of the range
    /// constructor function.
    /// </summary>
    string RangeStoreType { get; }

    /// <summary>
    /// The PostgreSQL multirange type name, e.g. <c>datemultirange</c>. Also the name of the
    /// multirange constructor function.
    /// </summary>
    string MultirangeStoreType { get; }

    /// <summary>
    /// The PostgreSQL subtype of the range, e.g. <c>date</c> for <c>daterange</c>. Used to
    /// resolve the element type mapping explicitly — the provider's CLR-type default can
    /// differ (e.g. <see cref="DateTime"/> defaults to <c>timestamptz</c>, but the
    /// <c>tsrange</c> subtype is <c>timestamp</c>).
    /// </summary>
    string ElementStoreType { get; }

    /// <summary>The type mapping for the range type.</summary>
    RelationalTypeMapping RangeTypeMapping { get; }

    /// <summary>The type mapping for the set (multirange) type.</summary>
    RelationalTypeMapping RangeSetTypeMapping { get; }

    /// <summary>The <c>TRange.Empty</c> singleton, untyped.</summary>
    object EmptyRange { get; }

    /// <summary>The <c>RangeSet&lt;TRange, T&gt;.Infinite</c> singleton, untyped.</summary>
    object InfiniteRangeSet { get; }
}