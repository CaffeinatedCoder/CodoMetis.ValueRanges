using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.NodaTime.Internal;
using NodaTime;
using NodaTime.Text;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

// ReSharper disable once CheckNamespace — conventional namespace for options builder extensions,
// so UseValueRangesNodaTime is discoverable without an extra using.
namespace Microsoft.EntityFrameworkCore;
// EF1001 here is EF's analyzer reading CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal
// as an internal namespace — it keys on the `EntityFrameworkCore.*.Internal` shape, not on an
// attribute. That warning is aimed at consumers of the plugin; this satellite is the same codebase
// and builds on those types by design.
#pragma warning disable EF1001


/// <summary>
/// CodoMetis.ValueRanges NodaTime extension method for <see cref="NpgsqlDbContextOptionsBuilder"/>.
/// </summary>
public static class NpgsqlValueRangesNodaTimeDbContextOptionsBuilderExtensions
{
    // Constructed once: definitions build their type mappings eagerly, and Register is an
    // idempotent no-op for already-known range CLR types on every later options build.
    private static readonly IRangeTypeDefinition[] Definitions =
        [
            new RangeTypeDefinition<LocalDateRange, LocalDate>("daterange", "datemultirange", "date"),
            new RangeTypeDefinition<LocalDateTimeRange, LocalDateTime>(
                "tsrange", "tsmultirange", "timestamp without time zone"),
            new RangeTypeDefinition<InstantRange, Instant>(
                "tstzrange", "tstzmultirange", "timestamp with time zone"),

            // No element normalization anywhere: a LocalDateTime is wall-clock time by
            // construction and an Instant is an instant by construction — the Kind and
            // offset reinterpretations of the BCL definitions have nothing to fix here.

            // YearMonth has no PostgreSQL representation: this definition stores the range
            // as a month-aligned daterange, converting every boundary through its days.
            new YearMonthRangeTypeDefinition()
        ];

    private static readonly ISetTypeDefinition[] SetDefinitions =
        [
            // Literal texts use the ISO patterns explicitly: NodaTime's IFormattable with a
            // null format produces the culture's long form, not ISO.
            //
            // The calendar-bearing types normalize to ISO, mirroring what their From methods
            // do. Stored elements are already ISO — a set can only be built through From — but
            // the normalization also backs the element mapping used for a bare probe in
            // `column @> ARRAY[@p]`, where a non-ISO value would otherwise bind its own
            // calendar's year/month/day as if they were ISO.
            new SetTypeDefinition<LocalDateSet, LocalDate>(
                "date",
                static value => value.Calendar == CalendarSystem.Iso ? value : value.WithCalendar(CalendarSystem.Iso),
                NodaTime.Text.LocalDatePattern.Iso.Format),
            new SetTypeDefinition<LocalDateTimeSet, LocalDateTime>(
                "timestamp without time zone",
                static value => value.Calendar == CalendarSystem.Iso ? value : value.WithCalendar(CalendarSystem.Iso),
                NodaTime.Text.LocalDateTimePattern.ExtendedIso.Format),

            // No normalization: an Instant is an instant and a LocalTime a time of day by
            // construction — neither carries a calendar.
            new SetTypeDefinition<InstantSet, Instant>(
                "timestamp with time zone", literalText: NodaTime.Text.InstantPattern.ExtendedIso.Format),
            new SetTypeDefinition<LocalTimeSet, LocalTime>(
                "time without time zone", literalText: NodaTime.Text.LocalTimePattern.ExtendedIso.Format),

            // YearMonth has no PostgreSQL representation: this definition stores the set
            // as a month-aligned date[], converting every element through its first day.
            new YearMonthSetTypeDefinition()
        ];

    // The validated-wrapper arities. A family cannot be registered as a closed definition —
    // its element type is whatever the consumer supplies — so each is registered as an open
    // generic whose instantiations the core registry builds on demand.
    //
    // Every one pins the ISO pattern as the format handed to the element's IFormattable, for
    // the same reason the closed definitions above pass a literalText: NodaTime's null-format
    // output is the culture's form. The element's own ToString(format, provider) is what
    // produces it, so a wrapper that forwards its format argument — which is what the
    // generators emit — needs no configuration.
    private static readonly (Type Family, Func<Type, ISetTypeDefinition> Factory)[] SetFamilies =
        [
            (typeof(LocalDateSet<>), SetTypeRegistry.Bridged(
                 "date", LocalDatePattern.Iso.PatternText,
                 ParseWith(LocalDatePattern.Iso), LocalDatePattern.Iso.Format)),

            (typeof(LocalDateTimeSet<>), SetTypeRegistry.Bridged(
                 "timestamp without time zone", LocalDateTimePattern.ExtendedIso.PatternText,
                 ParseWith(LocalDateTimePattern.ExtendedIso), LocalDateTimePattern.ExtendedIso.Format)),

            (typeof(InstantSet<>), SetTypeRegistry.Bridged(
                 "timestamp with time zone", InstantPattern.ExtendedIso.PatternText,
                 ParseWith(InstantPattern.ExtendedIso), InstantPattern.ExtendedIso.Format)),

            (typeof(LocalTimeSet<>), SetTypeRegistry.Bridged(
                 "time without time zone", LocalTimePattern.ExtendedIso.PatternText,
                 ParseWith(LocalTimePattern.ExtendedIso), LocalTimePattern.ExtendedIso.Format)),

            // The one family whose element text form and store text form differ: the element
            // speaks 2024-06 and the column holds 2024-06-01. formatPrimitive therefore feeds
            // the element its own granularity back, while literalText renders the date.
            (typeof(YearMonthSet<>), SetTypeRegistry.Bridged<LocalDate>(
                 "date", YearMonthPattern.Iso.PatternText,
                 static text => YearMonthPattern.Iso.Parse(text).GetValueOrThrow().OnDayOfMonth(1),
                 static date => YearMonthPattern.Iso.Format(YearMonthOf(date)),
                 LocalDatePattern.Iso.Format))
        ];

    /// <summary>
    /// A pattern as the bridge's parse leg. <c>GetValueOrThrow</c> raises
    /// <c>UnparsableValueException</c>, which derives from <see cref="FormatException"/> — the
    /// exception the bridge translates into the message naming the wrapper text-form contract.
    /// </summary>
    private static Func<string, T> ParseWith<T>(IPattern<T> pattern)
        => text => pattern.Parse(text).GetValueOrThrow();

    private static YearMonth YearMonthOf(LocalDate date)
        => date.Day == 1
               ? date.ToYearMonth()
               : throw new InvalidOperationException(
                     $"A YearMonthSet<T> column must hold first-of-month dates; got {date}. "
                   + "The stored array is corrupt for this mapping.");

    /// <summary>
    /// Enables mapping of the CodoMetis.ValueRanges NodaTime range types to the PostgreSQL
    /// range and multirange types — <c>LocalDateRange</c> to <c>daterange</c>,
    /// <c>LocalDateTimeRange</c> to <c>tsrange</c>, <c>InstantRange</c> to <c>tstzrange</c>,
    /// and their <see cref="CodoMetis.ValueRanges.RangeSet{TRange,T}"/> counterparts to the
    /// corresponding multiranges — as well as the NodaTime value set types to native arrays
    /// (<c>LocalDateSet</c> to <c>date[]</c>, <c>InstantSet</c> to <c>timestamptz[]</c>,
    /// <c>YearMonthSet</c> to a month-aligned <c>date[]</c>, …) — including LINQ translation
    /// of the full range and set algebra:
    /// <code>
    /// options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRangesNodaTime());
    /// </code>
    /// This implies both <c>UseNodaTime()</c> (the Npgsql NodaTime plugin, which maps the
    /// element types) and <c>UseValueRanges()</c> (the base plugin for the BCL-based range
    /// types) — neither needs to be called separately.
    /// </summary>
    /// <remarks>
    /// When the application builds its own <c>NpgsqlDataSource</c> instead of letting EF
    /// create one, <c>UseNodaTime()</c> must also be called on that data source builder —
    /// the same requirement Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime documents.
    /// </remarks>
    /// <param name="optionsBuilder">The Npgsql options builder.</param>
    /// <returns>The same options builder, for chaining.</returns>
    public static NpgsqlDbContextOptionsBuilder UseValueRangesNodaTime(this NpgsqlDbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        foreach (var definition in Definitions)
            RangeTypeRegistry.Register(definition);

        foreach (var definition in SetDefinitions)
            SetTypeRegistry.Register(definition);

        foreach (var (family, factory) in SetFamilies)
            SetTypeRegistry.RegisterFamily(family, factory);

        RangeTypeRegistry.RegisterAggregateExtensions(typeof(NodaTimeRangeAggregateExtensions));

        return optionsBuilder.UseNodaTime().UseValueRanges();
    }
}
