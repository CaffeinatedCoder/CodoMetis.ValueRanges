using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// <c>Length</c> on <see cref="Int32Range"/> and <see cref="Int64Range"/> runs as SQL (#405): the count of
/// integers, widened before subtracting, and for <c>int8range</c> NULL above <see cref="long.MaxValue"/>.
/// That the server's answers are memory's is <c>IntegerLengthParityTests</c>' (integration) to show.
/// </summary>
[TestClass]
public sealed class IntegerLengthTranslationTests
{
    private const string Int32Length =
        """
        CASE
            WHEN isempty(b."Seats") THEN 0
            ELSE upper(b."Seats")::bigint - lower(b."Seats")::bigint
        END
        """;

    private const string Int64Length =
        """
        CASE
            WHEN isempty(b."Tickets") THEN 0
            WHEN upper(b."Tickets")::numeric - lower(b."Tickets")::numeric <= 9223372036854775807.0 THEN CAST(upper(b."Tickets")::numeric - lower(b."Tickets")::numeric AS bigint)
        END
        """;

    [TestMethod]
    public void Int32RangeLength_InAFilter_IsTheWidenedCount()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Where(b => b.Seats.Length > 3).Select(b => b.Id).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize($"WHERE {Int32Length} > 3"));
    }

    [TestMethod]
    public void Int64RangeLength_InAProjection_IsTheNumericCountOrNull()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Select(b => b.Tickets.Length).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize($"SELECT {Int64Length}"));
    }

    [TestMethod]
    public void IntegerLength_InANullTest_TestsTheCount()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Where(b => b.Seats.Length == null || b.Tickets.Length == null).Select(b => b.Id).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize($"WHERE {Int32Length} IS NULL OR {Int64Length} IS NULL"));
    }

    [TestMethod]
    public void IntegerLength_InAnOrdering_OrdersByTheCount()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.OrderBy(b => b.Seats.Length).Select(b => b.Id).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize($"ORDER BY {Int32Length}"));
    }

    private static string Normalize(string sql) => string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
