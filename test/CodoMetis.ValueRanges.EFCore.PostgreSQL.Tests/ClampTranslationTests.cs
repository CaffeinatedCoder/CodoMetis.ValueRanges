using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// <c>Clamp</c> on <see cref="Int32Range"/>, <see cref="Int64Range"/> and <see cref="DecimalRange"/> runs as
/// SQL (#406): <c>LEAST(GREATEST(v, lower(x)), upper)</c>, NULL for the empty range, with the upper bound
/// memory reads. That the server's answers are memory's is <c>ClampParityTests</c>' (integration) to show.
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

    private static string Normalize(string sql) => string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
