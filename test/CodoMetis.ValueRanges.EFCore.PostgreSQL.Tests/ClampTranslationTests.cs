using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// <c>Clamp</c> runs as SQL on every range type (#406, #486): <c>LEAST(GREATEST(v, lower(x)), upper)</c>, NULL
/// for the empty range, with the upper bound memory reads. A value whose time zone differs from the range's
/// elements stays in memory. That the server's answers are memory's is <c>ClampParityTests</c>' and
/// <c>ClampDomainParityTests</c>' (integration) to show.
/// </summary>
[TestClass]
public sealed class ClampTranslationTests
{
    [TestMethod]
    public void Int32RangeClamp_OfAConstant_IsLeastOfGreatest_WithTheInclusiveUpperBound()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Where(b => b.Seats.Clamp(15) == 10).Select(b => b.Id).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            WHERE CASE
                WHEN isempty(b."Seats") THEN NULL
                ELSE LEAST(GREATEST(15, lower(b."Seats")), upper(b."Seats") - 1)
            END = 10
            """));
    }

    [TestMethod]
    public void Int64RangeClamp_OfANullableValue_IsNullWhenTheValueIs()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Select(b => b.Tickets.Clamp(b.Tickets.LowerBound()!.Value)).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            SELECT CASE
                WHEN lower(b."Tickets") IS NULL OR isempty(b."Tickets") THEN NULL
                ELSE LEAST(GREATEST(lower(b."Tickets"), lower(b."Tickets")), upper(b."Tickets") - 1)
            END
            """));
    }

    [TestMethod]
    public void DecimalRangeClamp_OfAColumn_UsesTheUpperBoundAsStored()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Select(b => b.Price.Clamp(b.Amount)).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            SELECT CASE
                WHEN isempty(b."Price") THEN NULL
                ELSE LEAST(GREATEST(b."Amount", lower(b."Price")), upper(b."Price"))
            END
            """));
    }

    [TestMethod]
    public void DateRangeClamp_OfAColumn_StepsBackADayFromTheExclusiveUpperBound()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Select(b => b.Period.Clamp(b.Day)).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            SELECT CASE
                WHEN isempty(b."Period") THEN NULL
                ELSE LEAST(GREATEST(b."Day", lower(b."Period")), upper(b."Period") - 1)
            END
            """));
    }

    [TestMethod]
    public void TimeRangeClamp_OfAConstant_IsATimeLiteral_AndTheUpperBoundAsStored()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Where(b => b.OpeningHours.Clamp(new TimeOnly(9, 30)) == b.At).Select(b => b.Id).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            WHERE CASE
                WHEN isempty(b."OpeningHours") THEN NULL
                ELSE LEAST(GREATEST(TIME '09:30:00.0000000', lower(b."OpeningHours")), upper(b."OpeningHours"))
            END = b."At"
            """));
    }

    [TestMethod]
    public void DateTimeRangeClamp_OfAParameter_ComparesItWithTheBounds()
    {
        using var context = new TestDbContext();
        var at = new DateTime(2026, 1, 1, 9, 30, 0);

        var sql = context.Bookings.Select(b => b.LocalTime.Clamp(at)).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            ELSE LEAST(GREATEST(@at, lower(b."LocalTime")), upper(b."LocalTime"))
            """));
    }

    [TestMethod]
    public void DateTimeOffsetRangeClamp_OfATimestamptzValue_RunsAsSql()
    {
        using var context = new TestDbContext();
        var at = new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero);

        var sql = context.Bookings.Where(b => b.InstantTime.Clamp(at) == at).Select(b => b.Id).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize("ELSE LEAST(GREATEST(@at, lower(b.\"InstantTime\")), upper(b.\"InstantTime\"))"));
    }

    [TestMethod]
    public void DateTimeRangeClamp_OfATimestamptzColumn_FailsTranslation_InsteadOfConvertingThroughTheSessionZone()
    {
        using var context = new TestDbContext();

        var e = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Bookings.Where(b => b.LocalTime.Clamp(b.CreatedAt) > b.CreatedAt).ToQueryString());

        StringAssert.Contains(e.Message, "could not be translated");
    }

    private static string Normalize(string sql) => string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
