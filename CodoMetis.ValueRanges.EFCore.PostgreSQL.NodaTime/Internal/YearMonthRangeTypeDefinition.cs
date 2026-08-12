using System.Data.Common;
using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NodaTime;
using NodaTime.Text;
using Npgsql;
using NpgsqlTypes;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.NodaTime.Internal;

/// <summary>
/// Binds <see cref="YearMonthRange"/> to a month-aligned PostgreSQL <c>daterange</c>.
/// PostgreSQL has no month-granularity range type and <see cref="YearMonth"/> has no wire
/// representation, so every boundary converts through its days: a closed upper bound expands
/// to the last day of its month, which the server canonicalizes to an exclusive first-of-next-month.
/// The store types are shared with <c>DateRange</c>/<c>LocalDateRange</c> — lookups by CLR
/// type stay unambiguous, and store-name-only resolution remains with the first registration.
/// </summary>
/// <remarks>
/// The model is coarser than the <c>date</c> subtype, so <see cref="SupportsSqlConstruction"/>
/// is <see langword="false"/>: converting factory bounds elementwise cannot express a closed
/// month upper bound (the server would canonicalize by one day, not one month). Reads validate
/// month alignment — a <c>daterange</c> covering partial months fails loudly rather than
/// silently shifting boundaries.
/// </remarks>
internal sealed class YearMonthRangeTypeDefinition : IRangeTypeDefinition
{
    public Type RangeClrType => typeof(YearMonthRange);

    public Type ElementClrType => typeof(YearMonth);

    public Type RangeSetClrType => typeof(RangeSet<YearMonthRange, YearMonth>);

    public string RangeStoreType => "daterange";

    public string MultirangeStoreType => "datemultirange";

    public string ElementStoreType => "date";

    public bool IsDiscrete => true;

    public bool SupportsSqlConstruction => false;

    public RelationalTypeMapping? ElementTypeMapping { get; } = new YearMonthTypeMapping();

    public RelationalTypeMapping RangeTypeMapping { get; } = new YearMonthRangeTypeMapping();

    public RelationalTypeMapping RangeSetTypeMapping { get; } = new YearMonthRangeSetTypeMapping();

    public object EmptyRange => YearMonthRange.Empty;

    public object InfiniteRangeSet => RangeSet<YearMonthRange, YearMonth>.Infinite;
}

/// <summary>
/// The shared model↔provider conversions: a <see cref="YearMonthRange"/> travels as the
/// <see cref="NpgsqlRange{T}"/> of <see cref="LocalDate"/> covering exactly its months, and
/// its SQL literals print in the equivalent date form.
/// </summary>
internal static class YearMonthRangeConversion
{
    internal static NpgsqlRange<LocalDate> ToProvider(YearMonthRange model)
        => RangeProviderConversion.ToProvider(model.ToLocalDateRange(), normalizeValue: null);

    internal static YearMonthRange FromProvider(NpgsqlRange<LocalDate> provider)
        => RangeProviderConversion.FromProvider<LocalDateRange, LocalDate>(provider).ToYearMonthRange();

    internal static NpgsqlRange<LocalDate>[] ToProvider(RangeSet<YearMonthRange, YearMonth> set)
        => set.Select(ToProvider).ToArray();

    internal static RangeSet<YearMonthRange, YearMonth> SetFromProvider(NpgsqlRange<LocalDate>[] value)
        => RangeSet<YearMonthRange, YearMonth>.From(value.Select(FromProvider));

    internal static LocalDateRange DateForm(YearMonthRange model) => model.ToLocalDateRange();

    internal static RangeSet<LocalDateRange, LocalDate> DateForm(RangeSet<YearMonthRange, YearMonth> set)
        => RangeSet<LocalDateRange, LocalDate>.From(set.Select(range => range.ToLocalDateRange()));
}

/// <summary>
/// Maps <see cref="YearMonthRange"/> to a month-aligned <c>daterange</c> column.
/// </summary>
internal sealed class YearMonthRangeTypeMapping : RelationalTypeMapping
{
    internal YearMonthRangeTypeMapping()
        : base(new RelationalTypeMappingParameters(
                   new CoreTypeMappingParameters(
                       typeof(YearMonthRange),
                       new ValueConverter<YearMonthRange, NpgsqlRange<LocalDate>>(
                           model => YearMonthRangeConversion.ToProvider(model),
                           provider => YearMonthRangeConversion.FromProvider(provider)),
                       new ImmutableValueComparer<YearMonthRange>(),
                       new ImmutableValueComparer<YearMonthRange>()),
                   "daterange"))
    {
    }

    private YearMonthRangeTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new YearMonthRangeTypeMapping(parameters);

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        if (parameter is not NpgsqlParameter npgsqlParameter) return;

        // EF normally applies the value converter before the parameter is configured;
        // converting here as well keeps direct usage (e.g. raw SQL) working.
        if (npgsqlParameter.Value is YearMonthRange model)
            npgsqlParameter.Value = YearMonthRangeConversion.ToProvider(model);

        npgsqlParameter.DataTypeName = StoreType;
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        // A month literal must print in date form — [2024-01,2024-03] is not a daterange.
        var range = value switch
                    {
                        YearMonthRange model => YearMonthRangeConversion.DateForm(model),
                        NpgsqlRange<LocalDate> provider =>
                            RangeProviderConversion.FromProvider<LocalDateRange, LocalDate>(provider),
                        _ => throw new InvalidOperationException(
                                 $"Cannot generate a '{StoreType}' SQL literal for a value of type '{value.GetType()}'.")
                    };

        // ToString() formats with the invariant culture; string interpolation would route
        // through IFormattable with the current culture instead.
        return $"'{range.ToString()}'::{StoreType}";
    }
}

/// <summary>
/// Maps <see cref="RangeSet{TRange,T}"/> of <see cref="YearMonthRange"/> to a month-aligned
/// <c>datemultirange</c> column.
/// </summary>
internal sealed class YearMonthRangeSetTypeMapping : RelationalTypeMapping
{
    internal YearMonthRangeSetTypeMapping()
        : base(new RelationalTypeMappingParameters(
                   new CoreTypeMappingParameters(
                       typeof(RangeSet<YearMonthRange, YearMonth>),
                       new ValueConverter<RangeSet<YearMonthRange, YearMonth>, NpgsqlRange<LocalDate>[]>(
                           model => YearMonthRangeConversion.ToProvider(model),
                           provider => YearMonthRangeConversion.SetFromProvider(provider)),
                       new ImmutableValueComparer<RangeSet<YearMonthRange, YearMonth>>(),
                       new ImmutableValueComparer<RangeSet<YearMonthRange, YearMonth>>()),
                   "datemultirange"))
    {
    }

    private YearMonthRangeSetTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new YearMonthRangeSetTypeMapping(parameters);

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        if (parameter is not NpgsqlParameter npgsqlParameter) return;

        if (npgsqlParameter.Value is RangeSet<YearMonthRange, YearMonth> model)
            npgsqlParameter.Value = YearMonthRangeConversion.ToProvider(model);

        npgsqlParameter.DataTypeName = StoreType;
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var set = value switch
                  {
                      RangeSet<YearMonthRange, YearMonth> model => YearMonthRangeConversion.DateForm(model),
                      NpgsqlRange<LocalDate>[] provider =>
                          RangeSet<LocalDateRange, LocalDate>.From(
                              provider.Select(RangeProviderConversion.FromProvider<LocalDateRange, LocalDate>)),
                      _ => throw new InvalidOperationException(
                               $"Cannot generate a '{StoreType}' SQL literal for a value of type '{value.GetType()}'.")
                  };

        return $"'{set.ToString()}'::{StoreType}";
    }
}

/// <summary>
/// Maps a <see cref="YearMonth"/> range element to a <c>date</c> — the first day of its month.
/// Used for bound values in translated SQL: parameters and constants convert to first-of-month
/// dates, and bound extractions (<c>lower</c>, <c>upper() - 1</c>) read back as the date's month.
/// </summary>
internal sealed class YearMonthTypeMapping : RelationalTypeMapping
{
    internal YearMonthTypeMapping()
        : base(new RelationalTypeMappingParameters(
                   new CoreTypeMappingParameters(
                       typeof(YearMonth),
                       new ValueConverter<YearMonth, LocalDate>(
                           model => model.OnDayOfMonth(1),
                           provider => provider.ToYearMonth())),
                   "date"))
    {
    }

    private YearMonthTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new YearMonthTypeMapping(parameters);

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        if (parameter is not NpgsqlParameter npgsqlParameter) return;

        if (npgsqlParameter.Value is YearMonth model)
            npgsqlParameter.Value = model.OnDayOfMonth(1);

        npgsqlParameter.DataTypeName = StoreType;
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var date = value switch
                   {
                       YearMonth model    => model.OnDayOfMonth(1),
                       LocalDate provider => provider,
                       _ => throw new InvalidOperationException(
                                $"Cannot generate a '{StoreType}' SQL literal for a value of type '{value.GetType()}'.")
                   };

        return $"DATE '{LocalDatePattern.Iso.Format(date)}'";
    }
}
