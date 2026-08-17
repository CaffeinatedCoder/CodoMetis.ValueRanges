using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// LINQ-to-SQL translation for the NodaTime validated-wrapper arities, via
/// <see cref="EntityFrameworkQueryableExtensions.ToQueryString"/> — no database required.
/// </summary>
/// <remarks>
/// <para>
/// These arities carry the most custom bridge code in the package and are reached through a
/// different registry seam from every other set type: the satellite registers them as open
/// generic families through <c>SetTypeRegistry.RegisterFamily</c>, and the closed instantiations
/// are built on demand. The operator matrix itself is covered by the base package's set
/// translation tests, so what these prove is narrower and more specific — that the bridge emits
/// the family's ISO text on every operand path, rather than NodaTime's culture form.
/// </para>
/// <para>
/// The operands go through <see cref="EF.Constant{T}"/> deliberately. A captured variable is
/// parameterized, and a parameter binds the converted primitive natively — which hides the whole
/// literal path, including the defect these tests were written to catch: before them,
/// <c>LocalDateSet&lt;T&gt;</c> rendered <c>ARRAY['Saturday, 15 June 2024']::date[]</c>.
/// </para>
/// </remarks>
[TestClass]
public sealed class NodaWrapperSetTranslationTests
{
    private static readonly CalendarDay Day     = new(new LocalDate(2024, 6, 15));
    private static readonly BillingMonth Month  = new(new YearMonth(2024, 6));
    private static readonly EventInstant Moment = new(Instant.FromUtc(2024, 6, 15, 10, 30));
    private static readonly OpeningTime Slot    = new(new LocalTime(9, 30, 15));
    private static readonly WallClockStamp Mark = new(new LocalDateTime(2024, 6, 15, 10, 30, 15));

    /// <summary>The WHERE clause only — the projection lists every column and drowns the assertion.</summary>
    private static string Where(Func<NodaTestDbContext, IQueryable<Reservation>> query)
    {
        using var context = new NodaTestDbContext();
        var       sql     = query(context).ToQueryString();
        var       index   = sql.IndexOf("WHERE", StringComparison.Ordinal);

        Assert.IsTrue(index >= 0, $"No WHERE clause in the generated SQL:\n{sql}");
        return sql[index..];
    }

    private static string Select(Func<NodaTestDbContext, IQueryable<object?>> query)
    {
        using var context = new NodaTestDbContext();
        return query(context).ToQueryString();
    }

    // -------------------------------------------------------------------------
    // Contains — the bare-element path, where the element mapping does the bridging
    // -------------------------------------------------------------------------

    [TestMethod]
    public void WrapperLocalDateSet_Contains_RendersIsoDate()
        => Assert.AreEqual(
            "WHERE r.\"WrappedHolidays\" @> ARRAY['2024-06-15']::date[]",
            Where(db => db.Reservations.Where(r => r.WrappedHolidays.Contains(EF.Constant(Day)))));

    [TestMethod]
    public void WrapperLocalDateTimeSet_Contains_RendersIsoTimestamp()
        => Assert.AreEqual(
            "WHERE r.\"WrappedMarks\" @> ARRAY['2024-06-15T10:30:15']::timestamp without time zone[]",
            Where(db => db.Reservations.Where(r => r.WrappedMarks.Contains(EF.Constant(Mark)))));

    [TestMethod]
    public void WrapperInstantSet_Contains_RendersIsoInstant()
        => Assert.AreEqual(
            "WHERE r.\"WrappedOccurrences\" @> ARRAY['2024-06-15T10:30:00Z']::timestamp with time zone[]",
            Where(db => db.Reservations.Where(r => r.WrappedOccurrences.Contains(EF.Constant(Moment)))));

    [TestMethod]
    public void WrapperLocalTimeSet_Contains_RendersIsoTime()
        => Assert.AreEqual(
            "WHERE r.\"WrappedSlots\" @> ARRAY['09:30:15']::time without time zone[]",
            Where(db => db.Reservations.Where(r => r.WrappedSlots.Contains(EF.Constant(Slot)))));

    /// <summary>
    /// The granularity hinge. The element's own text form is <c>2024-06</c>, which a
    /// <c>date[]</c> column cannot hold — the bridge converts it to the first of the month on the
    /// way out, and validates the alignment on the way back.
    /// </summary>
    [TestMethod]
    public void WrapperYearMonthSet_Contains_RendersFirstOfMonth()
        => Assert.AreEqual(
            "WHERE r.\"WrappedMonths\" @> ARRAY['2024-06-01']::date[]",
            Where(db => db.Reservations.Where(r => r.WrappedMonths.Contains(EF.Constant(Month)))));

    /// <summary>
    /// NodaTime's culture text form must never reach SQL. This is the assertion that fails if a
    /// family stops pinning its ISO pattern — for a <see cref="LocalDate"/> the default is
    /// <c>Saturday, 15 June 2024</c>, and for a <see cref="LocalDateTime"/> it is the US
    /// <c>06/15/2024 10:30:15</c>. Both are wrong for their column, and neither throws.
    /// </summary>
    [TestMethod]
    public void WrapperSets_NeverEmitTheCultureTextForm()
    {
        string[] clauses =
        [
            Where(db => db.Reservations.Where(r => r.WrappedHolidays.Contains(EF.Constant(Day)))),
            Where(db => db.Reservations.Where(r => r.WrappedMarks.Contains(EF.Constant(Mark)))),
            Where(db => db.Reservations.Where(r => r.WrappedOccurrences.Contains(EF.Constant(Moment)))),
            Where(db => db.Reservations.Where(r => r.WrappedSlots.Contains(EF.Constant(Slot)))),
            Where(db => db.Reservations.Where(r => r.WrappedMonths.Contains(EF.Constant(Month))))
        ];

        foreach (var clause in clauses)
        {
            foreach (var fragment in new[] { "June", "Saturday", "06/15/2024" })
            {
                Assert.IsFalse(
                    clause.Contains(fragment, StringComparison.Ordinal),
                    $"NodaTime's culture text form leaked into SQL: {clause}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Set-valued operands and the rest of the algebra
    // -------------------------------------------------------------------------

    [TestMethod]
    public void WrapperSets_Overlaps_TranslatesToAmpersand()
    {
        var other = LocalDateSet<CalendarDay>.From(Day);

        Assert.AreEqual(
            "WHERE r.\"WrappedHolidays\" && ARRAY['2024-06-15']::date[]",
            Where(db => db.Reservations.Where(r => r.WrappedHolidays.Overlaps(EF.Constant(other)))));
    }

    [TestMethod]
    public void WrapperSets_IsSubsetOf_TranslatesToContainedBy()
    {
        var other = LocalDateSet<CalendarDay>.From(new CalendarDay(new LocalDate(2024, 1, 1)), Day);

        Assert.AreEqual(
            "WHERE r.\"WrappedHolidays\" <@ ARRAY['2024-01-01','2024-06-15']::date[]",
            Where(db => db.Reservations.Where(r => r.WrappedHolidays.IsSubsetOf(EF.Constant(other)))));
    }

    [TestMethod]
    public void WrapperYearMonthSet_SetOperand_RendersMonthAlignedDates()
    {
        var other = YearMonthSet<BillingMonth>.From(Month, new BillingMonth(new YearMonth(2024, 1)));

        Assert.AreEqual(
            "WHERE r.\"WrappedMonths\" && ARRAY['2024-01-01','2024-06-01']::date[]",
            Where(db => db.Reservations.Where(r => r.WrappedMonths.Overlaps(EF.Constant(other)))));
    }

    [TestMethod]
    public void WrapperSets_Equality_ComparesTheStoredArray()
    {
        var expected = LocalDateSet<CalendarDay>.From(Day);

        Assert.AreEqual(
            "WHERE r.\"WrappedHolidays\" = ARRAY['2024-06-15']::date[]",
            Where(db => db.Reservations.Where(r => r.WrappedHolidays == EF.Constant(expected))));
    }

    [TestMethod]
    public void WrapperSets_Count_TranslatesToCardinality()
        => StringAssert.Contains(
            Select(db => db.Reservations.Select(r => (object?)r.WrappedHolidays.Count)),
            "cardinality(r.\"WrappedHolidays\")");

    [TestMethod]
    public void WrapperSets_IsEmpty_TranslatesToCardinalityZero()
        => Assert.AreEqual(
            "WHERE cardinality(r.\"WrappedMonths\") = 0",
            Where(db => db.Reservations.Where(r => r.WrappedMonths.IsEmpty)));

    // -------------------------------------------------------------------------
    // Coexistence
    // -------------------------------------------------------------------------

    /// <summary>
    /// An arity, its closed sibling and a BCL set in one query — the same column type reached
    /// through three different definitions, none of which may claim the others' properties.
    /// </summary>
    [TestMethod]
    public void WrapperAndClosedSets_CoexistInOneQuery()
    {
        var clause = Where(db => db.Reservations.Where(
            r => r.Holidays.Contains(EF.Constant(new LocalDate(2024, 6, 15)))
              && r.WrappedHolidays.Contains(EF.Constant(Day))
              && r.Tags.Contains("x")));

        StringAssert.Contains(clause, "r.\"Holidays\" @> ARRAY['2024-06-15']::date[]");
        StringAssert.Contains(clause, "r.\"WrappedHolidays\" @> ARRAY['2024-06-15']::date[]");
        StringAssert.Contains(clause, "r.\"Tags\" @> ARRAY['x']::text[]");
    }

    /// <summary>
    /// The parameter path, which is what a captured variable actually produces. The value binds
    /// as the converted primitive rather than being inlined, so the assertion is on the shape:
    /// a parameter inside the array, still cast to the column's element type.
    /// </summary>
    [TestMethod]
    public void WrapperSets_CapturedElement_BindsAsAParameter()
    {
        var day = new CalendarDay(new LocalDate(2024, 6, 15));

        var clause = Where(db => db.Reservations.Where(r => r.WrappedHolidays.Contains(day)));

        StringAssert.Contains(clause, "r.\"WrappedHolidays\" @> ARRAY[@");
        StringAssert.Contains(clause, "]::date[]");
    }
}
