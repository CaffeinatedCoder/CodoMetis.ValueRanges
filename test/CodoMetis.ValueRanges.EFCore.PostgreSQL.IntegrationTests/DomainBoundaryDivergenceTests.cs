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
