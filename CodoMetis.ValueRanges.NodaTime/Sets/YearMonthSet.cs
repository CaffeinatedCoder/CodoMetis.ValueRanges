using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
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
/// System.Text.Json serialization delegates elements to the configured serializer — register
/// NodaTime.Serialization.SystemTextJson's converters for ISO 8601 element output.
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

    ImmutableArray<YearMonth> IValueSet<YearMonth>.Elements => _elements;

    /// <summary>The canonical elements: deduplicated, sorted by chronological order.</summary>
    public ImmutableArray<YearMonth> Values => _elements;

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
    /// Parses a PostgreSQL array literal (e.g. <c>{2024-01,2024-06}</c>, <c>{}</c>) into a
    /// <see cref="YearMonthSet"/>, normalizing to canonical form.
    /// </summary>
    public static YearMonthSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<YearMonthSet, YearMonth>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="YearMonthSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out YearMonthSet result)
        => SetFormat.TryParse<YearMonthSet, YearMonth>(s.AsSpan(), provider, out result);

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
