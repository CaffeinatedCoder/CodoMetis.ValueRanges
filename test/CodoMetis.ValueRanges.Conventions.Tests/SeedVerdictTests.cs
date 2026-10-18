using CodoMetis.ValueRanges.Oracles;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The rule <c>dotnet run tools/oracles.cs seeds</c> judges a failed test by (<see cref="SeedVerdict"/>),
/// fed the messages <see cref="Candidates.Verdict"/> writes. A seed is caught when one row of a failed
/// test is not a known candidate, whatever else shares its message.
/// </summary>
/// <remarks>
/// Until 10.0.0 the tool counted a failed test as known whenever its message held
/// <c>[known candidate:</c> anywhere (#55). With one row of <c>RangeSetLiftParityTests</c> per type
/// registered as an unfiled candidate, <c>multirange-upper-inc-reads-lower-bound.patch</c> read as
/// "MISSED … all 12 tests pass with the defect planted" while every type still failed 21 or more other rows.
/// </remarks>
[TestClass]
public sealed class SeedVerdictTests
{
    private static readonly Candidates.Candidate Unfiled = new("AnyOracle", "row 1:", "a known disagreement", Issue: "");

    [TestMethod]
    public void AMessageWithAKnownRowAndAnotherRow_Catches()
        => Assert.IsTrue(SeedVerdict.Catches(MessageOf([Unfiled], ["row 1: memory 1, server 2", "row 2: memory 1, server 3"])));

    [TestMethod]
    public void AMessageWhoseEveryRowIsKnown_DoesNotCatch()
        => Assert.IsFalse(SeedVerdict.Catches(MessageOf([Unfiled], ["row 1: memory 1, server 2"])));

    /// <summary>The other rows come first, so a message cut short still shows one; known rows are what is cut.</summary>
    [TestMethod]
    public void AMessageCutShort_StillShowsTheRowThatCatches()
    {
        string[] rows = [.. Enumerable.Range(0, 30).Select(i => $"row 1: known variant {i}"), "row 2: memory 1, server 3"];
        var message = MessageOf([Unfiled], rows, shown: 5);

        StringAssert.Contains(message, "… and 26 more");
        Assert.IsTrue(SeedVerdict.Catches(message));
    }

    [TestMethod]
    public void AMessageCutShort_WhoseEveryRowIsKnown_DoesNotCatch()
    {
        var message = MessageOf([Unfiled], [.. Enumerable.Range(0, 30).Select(i => $"row 1: known variant {i}")], shown: 5);

        StringAssert.Contains(message, "… and 25 more");
        Assert.IsFalse(SeedVerdict.Catches(message));
    }

    /// <summary>
    /// A known row whose text spans lines is still one known row: until #235 its continuation line had no
    /// <c>[known candidate:</c> prefix and read as a caught row.
    /// </summary>
    [TestMethod]
    public void AKnownRowSpanningLines_DoesNotCatch()
    {
        var unfiled = new Candidates.Candidate("AnyOracle", "Int32Set whitespace {1,", "a known disagreement", Issue: "");
        var message = MessageOf([unfiled], ["Int32Set whitespace {1,\n2}: memory {1,2}, server refused"]);

        Assert.IsFalse(SeedVerdict.Catches(message), message);
        StringAssert.Contains(message, "{1,⏎2}");
    }

    [TestMethod]
    public void ARowSpanningLines_BesideAKnownRow_StillCatches()
    {
        var unfiled = new Candidates.Candidate("AnyOracle", "Int32Set whitespace {1,", "a known disagreement", Issue: "");

        Assert.IsTrue(SeedVerdict.Catches(MessageOf([unfiled], ["Int32Set whitespace {1,\n2}: memory {1,2}, server refused",
                                                                "Int32Set junk {1}\nx: memory {1}, server refused"])));
    }

    /// <summary>A seed that makes a known row agree changes what the sweep answers, which is a catch.</summary>
    [TestMethod]
    public void ACandidateFixed_Catches()
        => Assert.IsTrue(SeedVerdict.Catches(MessageOf([Unfiled], [])));

    [TestMethod]
    [DataRow("Assert.AreEqual failed. Expected:<1>. Actual:<2>.")]
    [DataRow("Assert.IsTrue failed. the text '[known candidate: x]' appears inside a line, not as a row")]
    public void AnyOtherAssertion_Catches(string message) => Assert.IsTrue(SeedVerdict.Catches(message));

    private static string MessageOf(IReadOnlyList<Candidates.Candidate> known, IReadOnlyList<string> rows, int shown = 25)
    {
        try
        {
            Candidates.Judge(known, rows, "AnyType: the sweep disagrees", shown);
        }
        catch (AssertFailedException failure)
        {
            return failure.Message;
        }

        Assert.Fail("Candidates.Judge passed, so there is no message to judge.");
        return "";
    }
}
