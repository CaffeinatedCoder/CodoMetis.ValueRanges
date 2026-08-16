using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;
using NodaTime;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, canonical set of <see cref="YearMonth"/> values — the value-set counterpart of a
/// PostgreSQL <c>date[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// Canonical form — deduplicated, sorted by chronological order — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </para>
/// <para>
/// PostgreSQL has no month-granularity element type; the EF Core companion stores this type
/// as a month-aligned <c>date[]</c> (first-of-month dates) and reading a non-aligned date
/// throws. Elements must be in the ISO calendar: unlike a <see cref="LocalDate"/> — where a
/// non-ISO date names the same physical day and normalizes losslessly — a non-ISO year-month
/// spans parts of two ISO months, so construction throws <see cref="ArgumentException"/>
/// instead of reinterpreting the value.
/// </para>
/// <para>
/// System.Text.Json serialization produces ISO 8601 element strings with no setup beyond the
/// converter factory: System.Text.Json has no built-in converter for NodaTime types, so the
/// family supplies its own through
/// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>. Registering an element converter
/// yourself takes precedence — <c>AddNodaTimeRangeConverters()</c> does that, and additionally
/// covers bare elements outside a set.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(YearMonthSet), nameof(From))]
public sealed class YearMonthSet : IValueSet<YearMonth>, IValueSetFactory<YearMonthSet, YearMonth>, IEquatable<YearMonthSet>
{
    private readonly ImmutableArray<YearMonth> _elements;

    private YearMonthSet(ImmutableArray<YearMonth> elements) => _elements = elements;

    private static YearMonth RequireIso(YearMonth value)
        => value.Calendar == CalendarSystem.Iso
               ? value
               : throw new ArgumentException(
                     $"YearMonthSet elements must be in the ISO calendar; got {value} ({value.Calendar}). "
                   + "A non-ISO year-month spans parts of two ISO months and has no lossless ISO equivalent.");

    // Element-level operations (Contains, Add, Remove) validate their probe through the same
    // helper as From, so a non-ISO year-month is rejected at every entry point rather than
    // silently missing (Equals) or throwing from the comparer mid-operation.
    YearMonth IValueSet<YearMonth>.NormalizeElement(YearMonth value) => RequireIso(value);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static YearMonthSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static YearMonthSet From(IEnumerable<YearMonth> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromTrusted(ValueSetCore.Canonicalize(values.Select(RequireIso), Comparer<YearMonth>.Default));
    }

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static YearMonthSet From(params ReadOnlySpan<YearMonth> values)
    {
        var normalized = new List<YearMonth>(values.Length);
        foreach (var value in values) normalized.Add(RequireIso(value));
        return FromTrusted(ValueSetCore.Canonicalize(normalized, Comparer<YearMonth>.Default));
    }

    internal static YearMonthSet FromTrusted(ImmutableArray<YearMonth> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static YearMonthSet IValueSetFactory<YearMonthSet, YearMonth>.FromTrusted(ImmutableArray<YearMonth> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by chronological order.</summary>
    public ImmutableArray<YearMonth> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public YearMonth this[int index] => _elements[index];

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<YearMonth>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static YearMonth ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseYearMonth(s.ToString());

    /// <summary>
    /// Formats a <see cref="YearMonth"/> value using ISO 8601 (<c>uuuu-MM</c>) by default.
    /// </summary>
    public static string FormatValue(YearMonth value, string? format, IFormatProvider? provider)
        => format is null
               ? NodaPatterns.YearMonth.Format(value)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// The JSON fallback for <see cref="YearMonth"/> elements: the same ISO 8601 text form as
    /// <see cref="FormatValue"/>, so JSON, array literals and the wire form agree. Consulted only
    /// when no converter has claimed <see cref="YearMonth"/> — see
    /// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>.
    /// </summary>
    static JsonConverter<YearMonth>? IValueSetFactory<YearMonthSet, YearMonth>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<YearMonthSet, YearMonth>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{2024-01,2024-06}</c>, <c>{}</c>) into a
    /// <see cref="YearMonthSet"/>, normalizing to canonical form.
    /// </summary>
    public static YearMonthSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<YearMonthSet, YearMonth>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static YearMonthSet Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<YearMonthSet, YearMonth>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="YearMonthSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out YearMonthSet result)
        => SetFormat.TryParse<YearMonthSet, YearMonth>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out YearMonthSet result)
        => SetFormat.TryParse<YearMonthSet, YearMonth>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(YearMonthSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as YearMonthSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(YearMonthSet)"/>.</summary>
    public static bool operator ==(YearMonthSet? left, YearMonthSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(YearMonthSet? left, YearMonthSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}
