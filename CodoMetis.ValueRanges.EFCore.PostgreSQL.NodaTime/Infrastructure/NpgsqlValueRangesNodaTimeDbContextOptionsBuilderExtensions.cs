using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.NodaTime.Internal;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

// ReSharper disable once CheckNamespace — conventional namespace for options builder extensions,
// so UseValueRangesNodaTime is discoverable without an extra using.
namespace Microsoft.EntityFrameworkCore;

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
            new SetTypeDefinition<LocalDateSet, LocalDate>(
                "date", literalText: NodaTime.Text.LocalDatePattern.Iso.Format),
            new SetTypeDefinition<LocalDateTimeSet, LocalDateTime>(
                "timestamp without time zone", literalText: NodaTime.Text.LocalDateTimePattern.ExtendedIso.Format),
            new SetTypeDefinition<InstantSet, Instant>(
                "timestamp with time zone", literalText: NodaTime.Text.InstantPattern.ExtendedIso.Format),
            new SetTypeDefinition<LocalTimeSet, LocalTime>(
                "time without time zone", literalText: NodaTime.Text.LocalTimePattern.ExtendedIso.Format),

            // No element normalization anywhere: the set types normalize calendars at
            // construction, a LocalDateTime is wall-clock time by construction, and an
            // Instant is an instant by construction.

            // YearMonth has no PostgreSQL representation: this definition stores the set
            // as a month-aligned date[], converting every element through its first day.
            new YearMonthSetTypeDefinition()
        ];

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

        RangeTypeRegistry.RegisterAggregateExtensions(typeof(NodaTimeRangeAggregateExtensions));

        return optionsBuilder.UseNodaTime().UseValueRanges();
    }
}
