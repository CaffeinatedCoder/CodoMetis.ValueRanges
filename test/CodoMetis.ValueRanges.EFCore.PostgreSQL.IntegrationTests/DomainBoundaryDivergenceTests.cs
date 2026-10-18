using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// At the very edge of a discrete domain the model and PostgreSQL do not agree, and this pins
/// exactly where and why so the difference is a documented property rather than a surprise.
/// </summary>
/// <remarks>
/// <para>
/// Everywhere else the two are held to agreement by <c>ShapeMatrixParityTests</c>. The boundary is
/// the one place a parity row would be wrong to write, and it is wrong even for <c>int4range</c> and
/// <c>int8range</c>, whose element domains match <see cref="int"/> and <see cref="long"/> exactly.
/// Matching domains are not enough, because the disagreement is not about the domain — it is about
/// what the *unbounded* sentinel means.
/// </para>
/// <para>
/// In this model <c>-∞</c> and <c>+∞</c> denote the absence of a bound over a domain that stops at
/// <c>int.MinValue</c> and <c>int.MaxValue</c>, so <c>(int.MaxValue, +∞)</c> has no first value and
/// is the empty range. In PostgreSQL they are sentinels that sit *outside* the element type, so the
/// same range still has room below and above the representable integers. Two consequences follow,
/// both asserted below:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>int4range(2147483647, NULL, '()')</c> raises <c>22003 integer out of range</c>: making it
///     canonical needs <c>2147483647 + 1</c>. The server cannot express the range at all, where the
///     model answers <c>Empty</c>.
///   </description></item>
///   <item><description>
///     <c>int4range(NULL, -2147483648, '()')</c> is <em>not</em> empty on the server, because its
///     lower sentinel is below <c>int.MinValue</c>. The model says empty, since no
///     <see cref="int"/> lies there.
///   </description></item>
/// </list>
/// <para>
/// Neither side is wrong; they answer over different domains. What matters is that a range at the
/// boundary is not round-trip-safe, and that this is known rather than discovered in production.
/// The two closed forms — <c>(max, max]</c> and <c>[min, min)</c> — do agree, and are asserted so
/// the divergence is bounded to the unbounded cases.
/// </para>
/// </remarks>
[TestClass]
public sealed class DomainBoundaryDivergenceTests
{
    [TestMethod]
    public async Task Int32Range_AtTheDomainEdge_DivergesOnlyWhereTheSentinelDiffers()
        => await AssertBoundary("int4range", "integer", int.MinValue.ToString(), int.MaxValue.ToString());

    [TestMethod]
    public async Task Int64Range_AtTheDomainEdge_DivergesOnlyWhereTheSentinelDiffers()
        => await AssertBoundary("int8range", "bigint", long.MinValue.ToString(), long.MaxValue.ToString());

    private static async Task AssertBoundary(string rangeType, string elementType, string min, string max)
    {
        ContainerLifecycle.RequireDatabase();

        // (max, +∞): the model is Empty; the server cannot canonicalize it and raises 22003.
        var overflow = await Evaluate($"isempty({rangeType}({max}, NULL, '()'))");
        Assert.AreEqual(
            "22003", overflow.SqlState,
            $"Expected {rangeType}({max}, NULL, '()') to raise '{elementType} out of range' — "
          + $"canonicalizing it needs {max} + 1. Got {overflow.Value?.ToString() ?? overflow.SqlState}. "
          + "If the server has gained a representation for this range, the model's Empty is no "
          + "longer the whole story and this divergence needs rewriting, not just re-pinning.");

        // (-∞, min): the model is Empty; the server's lower sentinel sits below the element type,
        // so it still considers the range inhabited.
        var belowMinimum = await Evaluate($"isempty({rangeType}(NULL, {min}, '()'))");
        Assert.AreEqual(
            false, belowMinimum.Value,
            $"Expected {rangeType}(NULL, {min}, '()') to be non-empty on the server, because its "
          + $"-∞ sentinel is below {elementType}'s minimum. The model answers Empty for the same "
          + "range, since no value lies there. A range at this boundary is not round-trip-safe.");

        // The closed forms at the same bounds agree, so the divergence is confined to the
        // unbounded sides rather than to the boundary generally.
        Assert.AreEqual(true,  (await Evaluate($"isempty({rangeType}({max}, {max}, '(]'))")).Value,
                        $"({max}, {max}] should be empty on both sides.");
        Assert.AreEqual(true,  (await Evaluate($"isempty({rangeType}({min}, {min}, '[)'))")).Value,
                        $"[{min}, {min}) should be empty on both sides.");
        Assert.AreEqual(false, (await Evaluate($"isempty({rangeType}({max}, NULL, '[)'))")).Value,
                        $"[{max}, +∞) holds {max} and should be non-empty on both sides.");
    }

    /// <summary>
    /// The date extremes stand for PostgreSQL's infinities (#117): Npgsql writes <c>DateOnly.MaxValue</c>
    /// as <c>infinity</c> and reads <c>infinity</c> back as it, so the server's real 9999-12-31 has no
    /// value of its own here. <c>[x,infinity)</c>, which excludes the infinity, is therefore
    /// <c>[x,9999-12-30]</c> on every path: the column, the server's text through <c>Parse</c>, and
    /// the literal as written. The server's range also holds its real 9999-12-31, which is the
    /// divergence; reading the bound as the inclusive extreme instead was tried and made the column
    /// path disagree with the server's algebra (<c>DifferentialFuzzParityTests</c>: <c>(,) -
    /// (,-infinity]</c> is <c>(-infinity,)</c>, which excludes the minimum) and with the plugin's
    /// own <c>[x,9999-12-30]</c>, which PostgreSQL stores as <c>[x,9999-12-31)</c>.
    /// </summary>
    [TestMethod]
    [DataRow("daterange('2026-01-01', 'infinity')", "[2026-01-01,9999-12-30]")]
    [DataRow("daterange('-infinity', '2026-01-01', '(]')", "[0001-01-02,2026-01-01]")]
    [DataRow("daterange('-infinity', 'infinity')", "[0001-01-01,9999-12-30]")]
    [DataRow("daterange('-infinity', 'infinity', '()')", "[0001-01-02,9999-12-30]")]
    [DataRow("daterange(NULL, 'infinity')", "(,9999-12-30]")]
    [DataRow("daterange('-infinity', NULL, '()')", "[0001-01-02,)")]
    [DataRow("daterange('2026-01-01', 'infinity', '[]')", "[2026-01-01,9999-12-31]")]
    [DataRow("'[2026-01-01,9999-12-30]'::daterange", "[2026-01-01,9999-12-30]")]
    public async Task DateRange_AnInfiniteBound_ReadsAlikeFromTheColumnAndTheText(string expression, string expected)
    {
        ContainerLifecycle.RequireDatabase();
        await using var context = new IntegrationDbContext();

        // The expressions are this test's own constants, not input.
#pragma warning disable EF1003
        var value  = "SELECT " + expression + " AS \"Value\"";
        var column = await context.Database.SqlQueryRaw<DateRange>(value).SingleAsync();
        var text   = await context.Database.SqlQueryRaw<string>("SELECT (" + expression + ")::text AS \"Value\"").SingleAsync();

        Assert.AreEqual(expected, column.ToString(), $"{expression} from the column");
        Assert.AreEqual(column, DateRange.Parse(text, null), $"{expression} through its text {text}");
        Assert.AreEqual(RangeSet<DateRange, DateOnly>.From(column),
                        RangeSet<DateRange, DateOnly>.Parse($"{{{text}}}", null), $"{expression} in a multirange literal");

        var noda = await context.Database.SqlQueryRaw<LocalDateRange>(value).SingleAsync();
#pragma warning restore EF1003
        Assert.AreEqual(noda, LocalDateRange.Parse(text, null), $"{expression} as a LocalDateRange");
    }

    /// <summary>The divergence itself: the server's open range holds its real last date.</summary>
    [TestMethod]
    public async Task DateRange_ExclusiveInfinity_HoldsTheServersLastDate()
    {
        ContainerLifecycle.RequireDatabase();

        Assert.AreEqual(true, (await Evaluate("daterange('2026-01-01', 'infinity') @> '9999-12-31'::date")).Value,
                        "PostgreSQL's [x,infinity) holds its real 9999-12-31, which no DateOnly stands for apart from infinity.");
        Assert.AreEqual(false, DateRange.Parse("[2026-01-01,infinity)", null).Contains(DateOnly.MaxValue),
                        "DateOnly.MaxValue is infinity here, which [x,infinity) excludes.");
        Assert.AreEqual(false, LocalDateRange.Parse("[2026-01-01,infinity)", null).Contains(LocalDate.MaxIsoValue));
    }

    /// <summary>
    /// Precision (#126): the BCL types hold 100 ns and NodaTime 1 ns, the server a microsecond.
    /// PostgreSQL reads more than six fraction digits rounded half-to-even; the store writes the value
    /// truncated toward 2000-01-01, as Npgsql binds a parameter. So text with sub-microsecond digits
    /// holds three values: the library's (every digit it can keep), the server's reading of the text
    /// (rounded) and the stored one (truncated), at most 1 µs apart. Rounding on parse was rejected:
    /// a value with sub-microsecond ticks would stop round-tripping through its own literal (claim S08).
    /// </summary>
    [TestMethod]
    [DataRow("2026-01-01 10:00:00.1234567", "2026-01-01 10:00:00.123457", "2026-01-01 10:00:00.123456")]
    [DataRow("2026-01-01 10:00:00.0000015", "2026-01-01 10:00:00.000002", "2026-01-01 10:00:00.000001")]
    public async Task Timestamp_SubMicrosecondText_IsThreeValuesAtMostAMicrosecondApart(string text, string serverReads, string stored)
    {
        ContainerLifecycle.RequireDatabase();
        await using var context = new IntegrationDbContext();

        var literal = $"[\"{text}\",)";
        var parsed  = Assert.IsInstanceOfType<DateTimeRange.UnboundedEnd>(DateTimeRange.Parse(literal, null));
        Assert.AreEqual(DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture), parsed.Start, "the library keeps every 100 ns digit");

        Assert.AreEqual($"[\"{serverReads}\",)", (await Evaluate($"('{literal}'::tsrange)::text")).Value, "PostgreSQL rounds the text half-to-even");

        var constant = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(DateTimeRange))!.GenerateSqlLiteral(parsed);
        Assert.AreEqual($"[\"{stored}\",)", (await Evaluate($"({constant})::text")).Value, "the store truncates, as Npgsql binds");

        Assert.IsTrue(parsed.ToString().Contains(text[^7..]), "the library's own literal round-trips its digits");
        Assert.AreEqual(parsed, DateTimeRange.Parse(parsed.ToString(), null));
    }

    [TestMethod]
    public async Task LocalDateTime_NanosecondText_IsThreeValuesAtMostAMicrosecondApart()
    {
        ContainerLifecycle.RequireDatabase();
        await using var context = new IntegrationDbContext();

        const string literal = "[\"2026-01-01 10:00:00.123456789\",)";
        var parsed = Assert.IsInstanceOfType<LocalDateTimeRange.UnboundedEnd>(LocalDateTimeRange.Parse(literal, null));
        Assert.AreEqual(new LocalDateTime(2026, 1, 1, 10, 0, 0).PlusNanoseconds(123_456_789), parsed.Start);

        Assert.AreEqual("[\"2026-01-01 10:00:00.123457\",)", (await Evaluate($"('{literal}'::tsrange)::text")).Value);

        var constant = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(LocalDateTimeRange))!.GenerateSqlLiteral(parsed);
        Assert.AreEqual("[\"2026-01-01 10:00:00.123456\",)", (await Evaluate($"({constant})::text")).Value);
    }

    /// <summary>Evaluates one expression, returning either its value or the SQLSTATE it raised.</summary>
    private static async Task<(object? Value, string? SqlState)> Evaluate(string expression)
    {
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        try
        {
            await using var command = new NpgsqlCommand($"SELECT {expression}", connection);
            return (await command.ExecuteScalarAsync(), null);
        }
        catch (PostgresException raised)
        {
            return (null, raised.SqlState);
        }
    }
}
