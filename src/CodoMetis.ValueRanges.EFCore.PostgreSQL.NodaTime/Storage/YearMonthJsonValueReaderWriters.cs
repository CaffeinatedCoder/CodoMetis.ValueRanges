using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.NodaTime.Internal;
using Microsoft.EntityFrameworkCore.Storage.Json;
using NodaTime;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.NodaTime.Storage;

// EF1001: the core plugin's `EntityFrameworkCore.*.Internal` namespace, which this satellite builds on by design.
#pragma warning disable EF1001

// The YearMonth types are stored month-aligned (daterange, datemultirange, date[]), so inside an
// EF Core JSON column they are written in that same date form rather than their own "2026-01"
// literal: EF queries into a document by extracting the text and casting it to the store type,
// and PostgreSQL cannot read "2026-01" as a date. Reads validate month alignment through the same
// conversions the column mappings use. Public because a compiled model re-creates them through
// ConstructorExpression.

/// <summary>
/// Reads and writes a <see cref="YearMonthRange"/> inside an EF Core JSON column as the
/// month-aligned <c>daterange</c> literal its column mapping stores, e.g. <c>[2026-01-01,2026-07-01)</c>.
/// </summary>
public sealed class YearMonthRangeJsonValueReaderWriter : JsonValueReaderWriter<YearMonthRange>
{
    private static readonly PropertyInfo InstanceProperty =
        typeof(YearMonthRangeJsonValueReaderWriter).GetProperty(nameof(Instance))!;

    private YearMonthRangeJsonValueReaderWriter()
    {
    }

    /// <summary>The singleton instance.</summary>
    public static YearMonthRangeJsonValueReaderWriter Instance { get; } = new();

    /// <inheritdoc />
    public override YearMonthRange FromJsonTyped(ref Utf8JsonReaderManager manager, object? existingObject = null)
        => LocalDateRange.Parse(manager.CurrentReader.GetString()!, CultureInfo.InvariantCulture).ToYearMonthRange();

    /// <inheritdoc />
    public override void ToJsonTyped(Utf8JsonWriter writer, YearMonthRange value)
        => writer.WriteStringValue(StoreText<LocalDateRange>.Format(YearMonthRangeConversion.DateForm(value)));

    /// <inheritdoc />
    public override Expression ConstructorExpression => Expression.Property(null, InstanceProperty);
}

/// <summary>
/// Reads and writes a <see cref="RangeSet{TRange,T}"/> of <see cref="YearMonthRange"/> inside an
/// EF Core JSON column as the month-aligned <c>datemultirange</c> literal its column mapping stores.
/// </summary>
public sealed class YearMonthRangeSetJsonValueReaderWriter : JsonValueReaderWriter<RangeSet<YearMonthRange, YearMonth>>
{
    private static readonly PropertyInfo InstanceProperty =
        typeof(YearMonthRangeSetJsonValueReaderWriter).GetProperty(nameof(Instance))!;

    private YearMonthRangeSetJsonValueReaderWriter()
    {
    }

    /// <summary>The singleton instance.</summary>
    public static YearMonthRangeSetJsonValueReaderWriter Instance { get; } = new();

    /// <inheritdoc />
    public override RangeSet<YearMonthRange, YearMonth> FromJsonTyped(ref Utf8JsonReaderManager manager, object? existingObject = null)
        => RangeSet<YearMonthRange, YearMonth>.From(
               RangeSet<LocalDateRange, LocalDate>.Parse(manager.CurrentReader.GetString()!, CultureInfo.InvariantCulture)
                                                  .Select(range => range.ToYearMonthRange()));

    /// <inheritdoc />
    public override void ToJsonTyped(Utf8JsonWriter writer, RangeSet<YearMonthRange, YearMonth> value)
        => writer.WriteStringValue(StoreText<RangeSet<LocalDateRange, LocalDate>>.Format(YearMonthRangeConversion.DateForm(value)));

    /// <inheritdoc />
    public override Expression ConstructorExpression => Expression.Property(null, InstanceProperty);
}

/// <summary>
/// Reads and writes a <see cref="YearMonthSet"/> inside an EF Core JSON column as the
/// first-of-month <c>date[]</c> literal its column mapping stores, e.g. <c>{2026-01-01,2026-06-01}</c>.
/// </summary>
public sealed class YearMonthSetJsonValueReaderWriter : JsonValueReaderWriter<YearMonthSet>
{
    private static readonly PropertyInfo InstanceProperty =
        typeof(YearMonthSetJsonValueReaderWriter).GetProperty(nameof(Instance))!;

    private YearMonthSetJsonValueReaderWriter()
    {
    }

    /// <summary>The singleton instance.</summary>
    public static YearMonthSetJsonValueReaderWriter Instance { get; } = new();

    /// <inheritdoc />
    public override YearMonthSet FromJsonTyped(ref Utf8JsonReaderManager manager, object? existingObject = null)
        => YearMonthSet.From(
               LocalDateSet.Parse(manager.CurrentReader.GetString()!, CultureInfo.InvariantCulture)
                           .Values.Select(YearMonthSetTypeDefinition.FromPrimitive));

    /// <inheritdoc />
    public override void ToJsonTyped(Utf8JsonWriter writer, YearMonthSet value)
        => writer.WriteStringValue(
               StoreText<LocalDateSet>.Format(LocalDateSet.From(value.Values.Select(YearMonthSetTypeDefinition.ToPrimitive))));

    /// <inheritdoc />
    public override Expression ConstructorExpression => Expression.Property(null, InstanceProperty);
}
