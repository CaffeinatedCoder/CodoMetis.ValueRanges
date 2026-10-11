using System.Globalization;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// The text a temporal value is written as — a SQL literal, or inside a JSON document — is the value
/// Npgsql would bind: microseconds truncated toward 2000-01-01, the extremes as <c>infinity</c>, the
/// range rebuilt so it collapses and merges as the server would. The live-server authority is
/// <c>TemporalStoreParityTests</c> (integration); this pins the text itself.
/// </summary>
[TestClass]
public sealed class TemporalStoreTextTests
{
    private static readonly DateTime After2000  = new DateTime(2026, 3, 15, 10, 20, 30).AddTicks(1_234_567);
    private static readonly DateTime Before2000 = new DateTime(1999, 12, 31, 23, 59, 59).AddTicks(1_234_561);

    private static RelationalTypeMapping MappingOf<TValue>()
    {
        using var context = new TestDbContext();
        return context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(TValue))!;
    }

    private static string LiteralOf<TValue>(TValue value) where TValue : notnull
        => MappingOf<TValue>().GenerateSqlLiteral(value);

    private static string JsonOf<TValue>(TValue value) where TValue : notnull
        => MappingOf<TValue>().JsonValueReaderWriter!.ToJsonString(value);

    [TestMethod]
    public void RangeBounds_AreTruncatedTowardThePostgresEpoch()
    {
        // After 2000 the cut rounds down; before it, toward the epoch, it rounds up — as Npgsql's does.
        // The second range starts before 2000 and ends after it, so its bounds go opposite ways.
        Assert.AreEqual(
            "'[2026-03-15T10:20:30.1234560,2026-03-15T11:20:30.1234560)'::tsrange",
            LiteralOf(DateTimeRange.CreateFinite(After2000, After2000.AddHours(1))));
        Assert.AreEqual(
            "'[1999-12-31T23:59:59.1234570,2000-01-01T00:59:59.1234560)'::tsrange",
            LiteralOf(DateTimeRange.CreateFinite(Before2000, Before2000.AddHours(1))));
    }

    [TestMethod]
    public void TheJsonDocument_HoldsTheSameText_AsTheLiteral()
    {
        var range = DateTimeRange.CreateFinite(After2000, After2000.AddHours(1));

        Assert.AreEqual("\"[2026-03-15T10:20:30.1234560,2026-03-15T11:20:30.1234560)\"", JsonOf(range));
        Assert.AreEqual(
            "\"{[2026-03-15T10:20:30.1234560,2026-03-15T11:20:30.1234560)}\"",
            JsonOf(RangeSet<DateTimeRange, DateTime>.From(range)));
    }

    [TestMethod]
    public void TheExtremes_AreInfinity_AsNpgsqlSendsThem()
    {
        Assert.AreEqual(
            "'[-infinity,2026-03-15T10:20:30.0000000)'::tsrange",
            LiteralOf(DateTimeRange.CreateFinite(DateTime.MinValue, new DateTime(2026, 3, 15, 10, 20, 30))));
        Assert.AreEqual(
            "'[2026-03-15,infinity]'::daterange",
            LiteralOf(DateRange.CreateFinite(new DateOnly(2026, 3, 15), DateOnly.MaxValue, endInclusive: true)));
        Assert.AreEqual(
            "ARRAY['-infinity','infinity']::timestamp with time zone[]",
            LiteralOf(DateTimeOffsetSet.From(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)));
        Assert.AreEqual("\"{-infinity,infinity}\"", JsonOf(DateSet.From(DateOnly.MinValue, DateOnly.MaxValue)));
    }

    /// <summary>A time of day has no infinity; its maximum is truncated like any other value.</summary>
    [TestMethod]
    public void TimeOfDay_MaxValue_IsTruncated_NotRoundedToMidnight()
        => Assert.AreEqual(
            "'[00:00:00.0000000,23:59:59.9999990]'::timerange",
            LiteralOf(TimeRange.CreateFinite(TimeOnly.MinValue, TimeOnly.MaxValue, endInclusive: true)));

    [TestMethod]
    public void ARangeNarrowerThanAMicrosecond_IsEmpty_AsTheServerStoresIt()
        => Assert.AreEqual(
            "'empty'::tsrange",
            LiteralOf(DateTimeRange.CreateFinite(After2000.AddTicks(-6), After2000.AddTicks(-3))));

    [TestMethod]
    public void AMultirange_MergesWhatTruncationMakesTouch()
        => Assert.AreEqual(
            "'{[2026-03-15T10:20:30.1234560,2026-03-15T10:20:30.1234580)}'::tsmultirange",
            LiteralOf(RangeSet<DateTimeRange, DateTime>.From(
                DateTimeRange.CreateFinite(After2000.AddTicks(-7), After2000.AddTicks(6)),
                DateTimeRange.CreateFinite(After2000.AddTicks(7), After2000.AddTicks(13)))));

    /// <summary>
    /// Two elements a few hundred nanoseconds apart are one store value: the array — literal, document
    /// and parameter alike — holds it once, so <c>cardinality</c> on the server is the set's <c>Count</c>.
    /// </summary>
    [TestMethod]
    public void SetElements_ThatTruncateToOneValue_AreStoredOnce()
    {
        var set = DateTimeSet.From(After2000, After2000.AddTicks(1));

        Assert.AreEqual("ARRAY['2026-03-15T10:20:30.1234560']::timestamp without time zone[]", LiteralOf(set));
        Assert.AreEqual("\"{2026-03-15T10:20:30.1234560}\"", JsonOf(set));
        Assert.AreEqual(1, ((DateTime[])MappingOf<DateTimeSet>().Converter!.ConvertToProvider(set)!).Length);
    }

    /// <summary>
    /// The JSON writer finds the store text through the registries on first use — no model built, no
    /// mapping looked up — which is the situation of a compiled model: a wrapper set family's
    /// definition is only built when something asks for it. Before, the hook had to have been set by
    /// a definition's constructor, and an unresolved family silently fell back to its own literal.
    /// </summary>
    [TestMethod]
    public void TheJsonWriter_ResolvesAWrapperSetThatNothingResolvedBefore()
    {
        var set = DateTimeSet<NeverMappedStamp>.From(new NeverMappedStamp(After2000), new NeverMappedStamp(After2000.AddTicks(1)));

        Assert.AreEqual(
            "\"{2026-03-15T10:20:30.1234560}\"",
            LiteralJsonValueReaderWriter<DateTimeSet<NeverMappedStamp>>.Instance.ToJsonString(set));
    }

    private static string Where(Func<TestDbContext, IQueryable<Booking>> query)
    {
        using var db = new TestDbContext();
        var sql = query(db).ToQueryString();
        return sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..].Trim();
    }

    /// <summary>
    /// A range's element constant is the range's own: normalized (UTC for <c>timestamptz</c>, no Kind for
    /// <c>timestamp</c>), truncated, the extremes as infinity — and still self-describing.
    /// </summary>
    [TestMethod]
    public void ARangesElementConstant_IsNormalizedAndTruncated()
    {
        var offset = new DateTimeOffset(After2000, TimeSpan.FromMinutes(330));

        Assert.AreEqual(
            "WHERE b.\"InstantTime\" @> TIMESTAMPTZ '2026-03-15T04:50:30.1234560+00:00'",
            Where(db => db.Bookings.Where(b => b.InstantTime.Contains(EF.Constant(offset)))));
        Assert.AreEqual(
            "WHERE b.\"LocalTime\" @> TIMESTAMP '2026-03-15T10:20:30.1234560'",
            Where(db => db.Bookings.Where(b => b.LocalTime.Contains(EF.Constant(DateTime.SpecifyKind(After2000, DateTimeKind.Utc))))));
        Assert.AreEqual(
            "WHERE b.\"LocalTime\" @> TIMESTAMP '-infinity'",
            Where(db => db.Bookings.Where(b => b.LocalTime.Contains(EF.Constant(DateTime.MinValue)))));
    }

    /// <summary>A wrapper element no model in this project maps — see the test above.</summary>
    private readonly record struct NeverMappedStamp(DateTime Value) : IFormattable, IParsable<NeverMappedStamp>, IComparable<NeverMappedStamp>
    {
        public static NeverMappedStamp Parse(string s, IFormatProvider? provider)
            => new(DateTime.ParseExact(s, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

        public static bool TryParse(string? s, IFormatProvider? provider, out NeverMappedStamp result)
        {
            var ok = DateTime.TryParseExact(s, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value);
            result = new(value);
            return ok;
        }

        public int CompareTo(NeverMappedStamp other) => Value.CompareTo(other.Value);

        public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);
    }
}
