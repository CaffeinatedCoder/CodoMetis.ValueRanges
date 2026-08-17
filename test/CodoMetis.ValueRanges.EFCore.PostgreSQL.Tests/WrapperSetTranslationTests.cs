using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// LINQ-to-SQL translation for the ten core validated-wrapper arities, via
/// <see cref="EntityFrameworkQueryableExtensions.ToQueryString"/> — no database required. The
/// counterpart of the NodaTime satellite's <c>NodaWrapperSetTranslationTests</c>, which covered
/// its five arities while the core ten had only <see cref="SetModelMappingTests"/>'s column-type
/// assertion and one <c>StringSet&lt;TestKey&gt;</c> case.
/// </summary>
/// <remarks>
/// <para>
/// What these prove is narrower than the operator matrix, which
/// <see cref="SetQueryTranslationTests"/> already covers over the closed types: that the
/// element-to-primitive bridge emits the family's pinned text form on every operand path. That is
/// the code the arities add, and its failure mode is a literal that looks plausible and means
/// something coarser — <c>09:30</c> for a <c>time</c>, <c>06/15/2024 10:30:00</c> for a
/// <c>timestamp</c>.
/// </para>
/// <para>
/// The operands go through <see cref="EF.Constant{T}"/> deliberately. A captured variable is
/// parameterized, and a parameter binds the converted primitive natively — which hides the whole
/// literal path, and with it everything asserted here. The parameter shape is covered separately
/// by <see cref="WrapperSets_CapturedElement_BindsAsAParameter"/>.
/// </para>
/// <para>
/// Every literal below was executed against PostgreSQL 18.1 while these tests were written,
/// including the <c>array_remove</c> shape, where the element is a bare unknown literal outside
/// any array cast and has to resolve against <c>anyelement</c>. Valid SQL text is not the same
/// claim as executable SQL, and only the integration suite makes the second one continuously.
/// </para>
/// </remarks>
[TestClass]
public sealed class WrapperSetTranslationTests
{
    private static readonly TestKey       Key    = TestKey.Parse("users.read", CultureInfo.InvariantCulture);
    private static readonly TestUuid      Uuid   = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly TestSmallCode Small  = new(7);
    private static readonly TestCode      Code   = new(42);
    private static readonly TestBigCode   Big    = new(9_000_000_000L);
    private static readonly TestRate      Rate   = TestRate.Parse("12.50", CultureInfo.InvariantCulture);
    private static readonly TestDay       Day    = new(new DateOnly(2024, 6, 15));
    private static readonly TestSlot      Slot   = new(new TimeOnly(9, 30, 15, 250));
    private static readonly TestStamp     Stamp  = TestStamp.Parse("2024-06-15T10:30:00.1234567", CultureInfo.InvariantCulture);
    private static readonly TestInstant   Moment = new(new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(2)));

    /// <summary>The WHERE clause only — the projection lists every column and drowns the assertion.</summary>
    private static string Where(Func<TestDbContext, IQueryable<Booking>> query)
    {
        using var context = new TestDbContext();
        var       sql     = query(context).ToQueryString();
        var       index   = sql.IndexOf("WHERE", StringComparison.Ordinal);

        Assert.IsTrue(index >= 0, $"No WHERE clause in the generated SQL:\n{sql}");
        return sql[index..].ReplaceLineEndings(" ");
    }

    // -------------------------------------------------------------------------
    // Contains — the bare-element path, where the element mapping does the bridging
    // -------------------------------------------------------------------------

    [TestMethod]
    public void WrapperStringSet_Contains_RendersBackingText()
        => Assert.AreEqual(
            "WHERE b.\"Permissions\" @> ARRAY['users.read']::text[]",
            Where(db => db.Bookings.Where(b => b.Permissions.Contains(EF.Constant(Key)))));

    [TestMethod]
    public void WrapperGuidSet_Contains_RendersTheDForm()
        => Assert.AreEqual(
            "WHERE b.\"WrappedUuids\" @> ARRAY['11111111-1111-1111-1111-111111111111']::uuid[]",
            Where(db => db.Bookings.Where(b => b.WrappedUuids.Contains(EF.Constant(Uuid)))));

    [TestMethod]
    public void WrapperInt16Set_Contains_RendersTheInteger()
        => Assert.AreEqual(
            "WHERE b.\"WrappedSmallCodes\" @> ARRAY['7']::smallint[]",
            Where(db => db.Bookings.Where(b => b.WrappedSmallCodes.Contains(EF.Constant(Small)))));

    [TestMethod]
    public void WrapperInt32Set_Contains_RendersTheInteger()
        => Assert.AreEqual(
            "WHERE b.\"WrappedCodes\" @> ARRAY['42']::integer[]",
            Where(db => db.Bookings.Where(b => b.WrappedCodes.Contains(EF.Constant(Code)))));

    [TestMethod]
    public void WrapperInt64Set_Contains_RendersTheInteger()
        => Assert.AreEqual(
            "WHERE b.\"WrappedBigCodes\" @> ARRAY['9000000000']::bigint[]",
            Where(db => db.Bookings.Where(b => b.WrappedBigCodes.Contains(EF.Constant(Big)))));

    /// <summary>
    /// The scale is the assertion: <c>numeric</c> compares across scales, so this cannot be caught
    /// by an equality check against the server — only by looking at what was written.
    /// </summary>
    [TestMethod]
    public void WrapperDecimalSet_Contains_KeepsScale()
        => Assert.AreEqual(
            "WHERE b.\"WrappedRates\" @> ARRAY['12.50']::numeric[]",
            Where(db => db.Bookings.Where(b => b.WrappedRates.Contains(EF.Constant(Rate)))));

    [TestMethod]
    public void WrapperDateSet_Contains_RendersIsoDate()
        => Assert.AreEqual(
            "WHERE b.\"WrappedDays\" @> ARRAY['2024-06-15']::date[]",
            Where(db => db.Bookings.Where(b => b.WrappedDays.Contains(EF.Constant(Day)))));

    /// <summary>
    /// Sub-second digits survive. A <see cref="TimeOnly"/>'s null-format text is <c>09:30</c>, so
    /// an arity that took the element's default here would query for the wrong quarter-hour and
    /// never say so.
    /// </summary>
    [TestMethod]
    public void WrapperTimeSet_Contains_KeepsSubSecondPrecision()
        => Assert.AreEqual(
            "WHERE b.\"WrappedSlots\" @> ARRAY['09:30:15.2500000']::time without time zone[]",
            Where(db => db.Bookings.Where(b => b.WrappedSlots.Contains(EF.Constant(Slot)))));

    [TestMethod]
    public void WrapperDateTimeSet_Contains_KeepsSubSecondPrecision()
        => Assert.AreEqual(
            "WHERE b.\"Audits\" @> ARRAY['2024-06-15T10:30:00.1234567']::timestamp without time zone[]",
            Where(db => db.Bookings.Where(b => b.Audits.Contains(EF.Constant(Stamp)))));

    /// <summary>
    /// The probe normalizes to UTC on the way to the parameter, as the closed
    /// <see cref="DateTimeOffsetSet"/> does — a <c>+02:00</c> element is queried for at
    /// <c>+00:00</c>, same instant. Npgsql requires offset zero for <c>timestamptz</c>.
    /// </summary>
    [TestMethod]
    public void WrapperDateTimeOffsetSet_Contains_NormalizesToUtc()
        => Assert.AreEqual(
            "WHERE b.\"WrappedInstants\" @> ARRAY['2024-06-15T08:30:00.0000000+00:00']::timestamp with time zone[]",
            Where(db => db.Bookings.Where(b => b.WrappedInstants.Contains(EF.Constant(Moment)))));

    /// <summary>
    /// The sweep behind the per-arity assertions: the precision the element's default text form
    /// would have dropped must reach the SQL. Stated as survival of a value rather than absence of
    /// a shape, which is the only version that catches anything here.
    /// </summary>
    /// <remarks>
    /// A family that stops pinning its format does not emit a malformed literal — the store
    /// primitive is re-rendered on the way out, so the text stays ISO and only the value is
    /// coarser. Seeding the defect (<c>TimeSet&lt;&gt;</c> built with a <see langword="null"/>
    /// element format, the way <c>Int32Set&lt;&gt;</c> correctly is) produces
    /// <c>ARRAY['09:30:00.0000000']</c> — well-formed, seven fraction digits, and fifteen seconds
    /// short. An assertion looking for <c>'09:30'</c> in the clause passes on that; this one does
    /// not.
    ///
    /// <para>
    /// Only some arities can fail silently. <see cref="DateTime"/> and <see cref="DateOnly"/>
    /// defaults are <c>06/15/2024 10:30:00</c>, which the bridge's <c>ParseExact</c> rejects
    /// outright — <c>SqlLiteralTests.WrapperTimestampSet_ElementIgnoringTheFormat_IsRejected</c>
    /// covers that loud path. <see cref="TimeOnly"/> parses its own truncated default, and a
    /// <see cref="decimal"/> parses its own scale-shortened one, so those two are the cases with
    /// no exception to catch.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void WrapperSets_KeepThePrecisionTheElementsDefaultFormWouldDrop()
    {
        (string Clause, string MustSurvive, string DefaultFormWouldGive)[] cases =
        [
            (Where(db => db.Bookings.Where(b => b.WrappedSlots.Contains(EF.Constant(Slot)))),
             "09:30:15.2500000", "09:30 — truncated to the minute"),

            (Where(db => db.Bookings.Where(b => b.Audits.Contains(EF.Constant(Stamp)))),
             "2024-06-15T10:30:00.1234567", "06/15/2024 10:30:00 — sub-seconds and Kind dropped"),

            (Where(db => db.Bookings.Where(b => b.WrappedRates.Contains(EF.Constant(Rate)))),
             "12.50", "12.5 — the stored scale shortened"),

            (Where(db => db.Bookings.Where(b => b.WrappedDays.Contains(EF.Constant(Day)))),
             "2024-06-15", "06/15/2024 — the US-ordered default"),

            (Where(db => db.Bookings.Where(b => b.WrappedInstants.Contains(EF.Constant(Moment)))),
             "2024-06-15T08:30:00.0000000+00:00", "06/15/2024 10:30:00 +02:00 — un-normalized")
        ];

        foreach (var (clause, mustSurvive, wouldGive) in cases)
        {
            StringAssert.Contains(
                clause, $"'{mustSurvive}'",
                $"A wrapper arity lost precision its family pins a format to keep. Expected the "
              + $"element to reach SQL as '{mustSurvive}'; the element's own default text form "
              + $"would have given {wouldGive}.");
        }
    }

    // -------------------------------------------------------------------------
    // Remove — the other bare-element path, and the only one with no array cast
    // around the element to type it
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>array_remove</c>'s second argument sits outside any <c>ARRAY[…]::type[]</c>, so the
    /// element mapping's cast-free literal has to be resolvable on its own. PostgreSQL types the
    /// unknown literal from the array argument through <c>anyarray</c>/<c>anyelement</c>; that it
    /// does so for a non-text element type was never asserted anywhere before.
    /// </summary>
    [TestMethod]
    public void WrapperInt32Set_Remove_RendersABareElement()
        => Assert.AreEqual(
            "WHERE cardinality(array_remove(b.\"WrappedCodes\", '42')) = 0",
            Where(db => db.Bookings.Where(b => b.WrappedCodes.Remove(EF.Constant(Code)).IsEmpty)));

    [TestMethod]
    public void WrapperDateTimeSet_Remove_RendersABareElement()
        => Assert.AreEqual(
            "WHERE cardinality(array_remove(b.\"Audits\", '2024-06-15T10:30:00.1234567')) = 0",
            Where(db => db.Bookings.Where(b => b.Audits.Remove(EF.Constant(Stamp)).IsEmpty)));

    [TestMethod]
    public void WrapperDateSet_Remove_RendersABareElement()
        => Assert.AreEqual(
            "WHERE cardinality(array_remove(b.\"WrappedDays\", '2024-06-15')) = 0",
            Where(db => db.Bookings.Where(b => b.WrappedDays.Remove(EF.Constant(Day)).IsEmpty)));

    // -------------------------------------------------------------------------
    // Set-valued operands and the rest of the algebra
    // -------------------------------------------------------------------------

    [TestMethod]
    public void WrapperSets_Overlaps_TranslatesToAmpersand()
    {
        var other = Int32Set<TestCode>.From([new TestCode(1), new TestCode(2)]);

        Assert.AreEqual(
            "WHERE b.\"WrappedCodes\" && ARRAY['1','2']::integer[]",
            Where(db => db.Bookings.Where(b => b.WrappedCodes.Overlaps(EF.Constant(other)))));
    }

    [TestMethod]
    public void WrapperSets_IsSubsetOf_TranslatesToContainedBy()
    {
        var other = DecimalSet<TestRate>.From(
            [Rate, TestRate.Parse("13", CultureInfo.InvariantCulture)]);

        Assert.AreEqual(
            "WHERE b.\"WrappedRates\" <@ ARRAY['12.50','13']::numeric[]",
            Where(db => db.Bookings.Where(b => b.WrappedRates.IsSubsetOf(EF.Constant(other)))));
    }

    [TestMethod]
    public void WrapperSets_Equality_ComparesTheStoredArray()
    {
        var expected = Int32Set<TestCode>.From([Code]);

        Assert.AreEqual(
            "WHERE b.\"WrappedCodes\" = ARRAY['42']::integer[]",
            Where(db => db.Bookings.Where(b => b.WrappedCodes == EF.Constant(expected))));
    }

    [TestMethod]
    public void WrapperSets_Count_TranslatesToCardinality()
        => Assert.AreEqual(
            "WHERE cardinality(b.\"WrappedCodes\") > 2",
            Where(db => db.Bookings.Where(b => b.WrappedCodes.Count > 2)));

    [TestMethod]
    public void WrapperSets_IsEmpty_TranslatesToCardinalityZero()
        => Assert.AreEqual(
            "WHERE cardinality(b.\"WrappedSlots\") = 0",
            Where(db => db.Bookings.Where(b => b.WrappedSlots.IsEmpty)));

    /// <summary>
    /// The Union refusal reaches the arities too — the guard keys off <c>array_cat</c> in the SQL
    /// tree, not off the set type, but nothing pinned that for a family resolved through the
    /// registry's lazy open-generic path rather than a closed registration.
    /// </summary>
    [TestMethod]
    public void WrapperSets_Union_Count_FailsTranslation()
    {
        var other = Int32Set<TestCode>.From([Code]);

        using var context = new TestDbContext();
        var query = context.Bookings.Where(b => b.WrappedCodes.Union(other).Count > 1);

        Assert.ThrowsExactly<InvalidOperationException>(() => query.ToQueryString());
    }

    // -------------------------------------------------------------------------
    // Coexistence and the parameter path
    // -------------------------------------------------------------------------

    /// <summary>
    /// An arity and its closed sibling in one query — the same column type reached through two
    /// different definitions, neither of which may claim the other's property.
    /// </summary>
    [TestMethod]
    public void WrapperAndClosedSets_CoexistInOneQuery()
    {
        var clause = Where(db => db.Bookings.Where(
            b => b.Codes.Contains(EF.Constant(42))
              && b.WrappedCodes.Contains(EF.Constant(Code))
              && b.Tags.Contains("x")));

        StringAssert.Contains(clause, "b.\"Codes\" @> ARRAY[42]::integer[]");
        StringAssert.Contains(clause, "b.\"WrappedCodes\" @> ARRAY['42']::integer[]");
        StringAssert.Contains(clause, "b.\"Tags\" @> ARRAY['x']::text[]");
    }

    /// <summary>
    /// The parameter path, which is what a captured variable actually produces. The value binds as
    /// the converted primitive rather than being inlined, so the assertion is on the shape: a
    /// parameter inside the array, still cast to the column's element type.
    /// </summary>
    [TestMethod]
    public void WrapperSets_CapturedElement_BindsAsAParameter()
    {
        var stamp = Stamp;

        var clause = Where(db => db.Bookings.Where(b => b.Audits.Contains(stamp)));

        StringAssert.Contains(clause, "b.\"Audits\" @> ARRAY[@");
        StringAssert.Contains(clause, "]::timestamp without time zone[]");
    }
}
