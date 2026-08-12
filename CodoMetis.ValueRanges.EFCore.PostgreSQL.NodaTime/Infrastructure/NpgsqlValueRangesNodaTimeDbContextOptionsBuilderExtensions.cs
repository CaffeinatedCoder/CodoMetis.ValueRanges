using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
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
                "tstzrange", "tstzmultirange", "timestamp with time zone")

            // No element normalization anywhere: a LocalDateTime is wall-clock time by
            // construction and an Instant is an instant by construction — the Kind and
            // offset reinterpretations of the BCL definitions have nothing to fix here.
        ];

    /// <summary>
    /// Enables mapping of the CodoMetis.ValueRanges NodaTime range types to the PostgreSQL
    /// range and multirange types — <c>LocalDateRange</c> to <c>daterange</c>,
    /// <c>LocalDateTimeRange</c> to <c>tsrange</c>, <c>InstantRange</c> to <c>tstzrange</c>,
    /// and their <see cref="CodoMetis.ValueRanges.RangeSet{TRange,T}"/> counterparts to the
    /// corresponding multiranges — including LINQ translation of the full range algebra:
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

        RangeTypeRegistry.RegisterAggregateExtensions(typeof(NodaTimeRangeAggregateExtensions));

        return optionsBuilder.UseNodaTime().UseValueRanges();
    }
}
