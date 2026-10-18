using System.Globalization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The dimension decoration <c>array_in</c> reads before a one-dimensional array, <c>[0:1]={1,2}</c>,
/// and the nested empty arrays PostgreSQL 17 and later read as <c>{}</c>. Until 10.0.0 both threw, so a
/// column whose lower bound is not 1, which <c>array_out</c> prints with the decoration, could not be
/// read back (#128).
/// </summary>
/// <remarks>
/// Every row is PostgreSQL's answer for <c>int4[]</c>, measured on 16 and 18 with
/// <c>pg_input_is_valid</c>: they agree on every decoration row, and 16 refuses the nested empties.
/// <c>LiteralCorpusProbeTests</c> (integration) asks a live server about the same literals for every
/// value set type.
/// </remarks>
[TestClass]
public class SetDimensionDecorationTests
{
    [TestMethod]
    [DataRow("[1:1]={1}", "{1}")]
    [DataRow("[0:1]={1,2}", "{1,2}")]
    [DataRow("[-5:-4]={2,1}", "{1,2}")]
    [DataRow("[3:5]={1,2,3}", "{1,2,3}")]
    [DataRow("[1]={1}", "{1}")]
    [DataRow("[+1:+1]={1}", "{1}")]
    [DataRow("[01:01]={1}", "{1}")]
    [DataRow("[-0:0]={1}", "{1}")]
    [DataRow("[1:1]  =  {1}", "{1}")]
    [DataRow(" [1:1]={1} ", "{1}")]
    [DataRow("\t[1:1]=\t{1}", "{1}")]
    [DataRow("[1:2]={1,1}", "{1}")]
    [DataRow("[2147483645:2147483646]={1,2}", "{1,2}")]
    [DataRow("[-2147483648:-2147483648]={1}", "{1}")]
    [DataRow("{{}}", "{}")]
    [DataRow("{{},{}}", "{}")]
    [DataRow("{{{}}}", "{}")]
    [DataRow("{ {} }", "{}")]
    [DataRow("{{ }}", "{}")]
    [DataRow("{{} ,{}}", "{}")]
    [DataRow("{{{}},{{}}}", "{}")]
    [DataRow("{{{},{}}}", "{}")]
    [DataRow("{{{},{}},{{},{}}}", "{}")]       // rectangular: every sibling has the same shape (#231)
    [DataRow("{{{{}}},{{{}}}}", "{}")]
    [DataRow("{{{},{}},{{},{}},{{},{}}}", "{}")]
    [DataRow("{{{{}},{{}}},{{{}},{{}}}}", "{}")]
    public void Reads_AsPostgresReadsIt(string literal, string expected)
        => Assert.AreEqual(expected, Int32Set.Parse(literal, CultureInfo.InvariantCulture).ToString());

    [TestMethod]
    [DataRow("[1:2]={1}")]                     // the decoration names two elements
    [DataRow("[1:1]={1,1}")]                   // counted as written, duplicates included
    [DataRow("[2]={1}")]
    [DataRow("[1:0]={}")]                      // a decoration names at least one element
    [DataRow("[1:1]={}")]
    [DataRow("[1:1]={{}}")]
    [DataRow("[ 1 : 1 ]={1}")]
    [DataRow("[ 1:1]={1}")]
    [DataRow("[1:1 ]={1}")]
    [DataRow("[0]={1}")]
    [DataRow("[-1]={1}")]
    [DataRow("[2147483647:2147483647]={1}")]   // upper + 1 overflows
    [DataRow("[-2147483649:-2147483649]={1}")]
    [DataRow("[0x1:0x1]={1}")]
    [DataRow("[1_0:1_0]={1}")]
    [DataRow("[a:b]={1}")]
    [DataRow("[:1]={1}")]
    [DataRow("[1:]={1}")]
    [DataRow("[]={1}")]
    [DataRow("[1:1]{1}")]
    [DataRow("[1:1]=1")]
    [DataRow("[1:1]=={1}")]
    [DataRow("[1:1]={1}x")]
    [DataRow("[1:1]=\"{1}\"")]
    [DataRow("[1:1]= {1}")]
    [DataRow("[1:1]={{1}}")]
    [DataRow("[1:1][1:1]={{1}}")]              // two dimensions, which a set cannot hold
    [DataRow("[1:2][1:1]={{1},{2}}")]
    [DataRow("{{},{{}}}")]                     // mixed depths
    [DataRow("{{{},{}},{{}}}")]                // ragged: same depth, different counts (#231)
    [DataRow("{{{}},{{},{}}}")]
    [DataRow("{{{},{},{}},{{},{}}}")]
    [DataRow("{{{{}},{{}}},{{{}},{{},{}}}}")]
    [DataRow("{{{{}},{{}}},{{{}}}}")]
    [DataRow("{{},}")]
    [DataRow("{{}}x")]
    [DataRow("{{1},{}}")]
    [DataRow("{{},{1}}")]
    public void Rejects_WhatPostgresRejects(string literal)
    {
        Assert.ThrowsExactly<FormatException>(() => Int32Set.Parse(literal, CultureInfo.InvariantCulture));
        Assert.IsFalse(Int32Set.TryParse(literal, CultureInfo.InvariantCulture, out _));
    }

    /// <summary>
    /// The decoration reaches every family: the text a column of another type prints with a lower bound
    /// other than 1 reads back as the same set.
    /// </summary>
    [TestMethod]
    public void EveryFamily_ReadsTheDecoration()
    {
        Assert.AreEqual(StringSet.From("a", "b,c"), StringSet.Parse("[0:1]={a,\"b,c\"}", CultureInfo.InvariantCulture));
        Assert.AreEqual(DateSet.From(new DateOnly(2026, 1, 1)), DateSet.Parse("[0:0]={2026-01-01}", CultureInfo.InvariantCulture));
        Assert.AreEqual(GuidSet.Empty, GuidSet.Parse("{{}}", CultureInfo.InvariantCulture));
    }
}
