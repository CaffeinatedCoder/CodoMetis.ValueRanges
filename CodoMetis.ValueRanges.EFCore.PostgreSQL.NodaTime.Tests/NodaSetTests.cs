using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// Model mapping and LINQ-to-SQL translation for the NodaTime value set types — the
/// definitions registered by <c>UseValueRangesNodaTime()</c> flow through the same registry,
/// mappings, and translators as the BCL set types.
/// </summary>
[TestClass]
public sealed class NodaSetTests
{
    private static readonly LocalDateSet SomeDays =
        LocalDateSet.From(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 24));

    private static string ColumnTypeOf(string propertyName)
    {
        using var context = new NodaTestDbContext();
        var property = context.Model.FindEntityType(typeof(Reservation))!.FindProperty(propertyName)!;
        return property.GetColumnType();
    }

    private static string Sql(Func<NodaTestDbContext, IQueryable<object?>> query)
    {
        using var context = new NodaTestDbContext();
        return query(context).ToQueryString();
    }

    // -------------------------------------------------------------------------
    // Model mapping
    // -------------------------------------------------------------------------

    [TestMethod]
    public void LocalDateSet_MapsTo_DateArray()
        => Assert.AreEqual("date[]", ColumnTypeOf(nameof(Reservation.Holidays)));

    [TestMethod]
    public void LocalDateTimeSet_MapsTo_TimestampArray()
        => Assert.AreEqual("timestamp without time zone[]", ColumnTypeOf(nameof(Reservation.WallClockMarks)));

    [TestMethod]
    public void InstantSet_MapsTo_TimestamptzArray()
        => Assert.AreEqual("timestamp with time zone[]", ColumnTypeOf(nameof(Reservation.Occurrences)));

    [TestMethod]
    public void LocalTimeSet_MapsTo_TimeArray()
        => Assert.AreEqual("time without time zone[]", ColumnTypeOf(nameof(Reservation.Slots)));

    [TestMethod]
    public void YearMonthSet_MapsTo_DateArray()
        => Assert.AreEqual("date[]", ColumnTypeOf(nameof(Reservation.BillingMonths)));

    [TestMethod]
    public void BclStringSet_CoexistsInTheSameModel()
        => Assert.AreEqual("text[]", ColumnTypeOf(nameof(Reservation.Tags)));

    // -------------------------------------------------------------------------
    // Translation
    // -------------------------------------------------------------------------

    [TestMethod]
    public void LocalDateSet_Contains_Constant()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Holidays.Contains(new LocalDate(2024, 6, 15))));
        StringAssert.Contains(sql, "r.\"Holidays\" @> ARRAY[DATE '2024-06-15']::date[]");
    }

    [TestMethod]
    public void LocalDateSet_Contains_Column()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Holidays.Contains(r.Day)));
        StringAssert.Contains(sql, "r.\"Holidays\" @> ARRAY[r.\"Day\"]::date[]");
    }

    [TestMethod]
    public void LocalDateSet_Overlaps_Constant()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Holidays.Overlaps(SomeDays)));
        StringAssert.Contains(sql, "r.\"Holidays\" && ARRAY['2024-01-01','2024-12-24']::date[]");
    }

    [TestMethod]
    public void LocalDateSet_IsSubsetOf_Parameter()
    {
        var other = LocalDateSet.From(new LocalDate(2024, 1, 1));
        var sql = Sql(db => db.Reservations.Where(r => r.Holidays.IsSubsetOf(other)));
        StringAssert.Contains(sql, "r.\"Holidays\" <@ @");
    }

    [TestMethod]
    public void LocalDateSet_Count_TranslatesToCardinality()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Holidays.Count > 1));
        StringAssert.Contains(sql, "cardinality(r.\"Holidays\") > 1");
    }

    [TestMethod]
    public void InstantSet_Contains_Parameter()
    {
        var at = Instant.FromUtc(2024, 6, 1, 12, 30);
        var sql = Sql(db => db.Reservations.Where(r => r.Occurrences.Contains(at)));
        StringAssert.Contains(sql, "r.\"Occurrences\" @> ARRAY[@");
    }

    [TestMethod]
    public void LocalTimeSet_IsEmpty_TranslatesToCardinalityZero()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Slots.IsEmpty));
        StringAssert.Contains(sql, "cardinality(r.\"Slots\") = 0");
    }

    // -------------------------------------------------------------------------
    // YearMonthSet — month elements travel as first-of-month dates
    // -------------------------------------------------------------------------

    [TestMethod]
    public void YearMonthSet_Contains_Constant_RendersFirstOfMonthDate()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.BillingMonths.Contains(new YearMonth(2024, 6))));
        StringAssert.Contains(sql, "r.\"BillingMonths\" @> ARRAY[DATE '2024-06-01']::date[]");
    }

    [TestMethod]
    public void YearMonthSet_Contains_Parameter()
    {
        var month = new YearMonth(2024, 6);
        var sql = Sql(db => db.Reservations.Where(r => r.BillingMonths.Contains(month)));
        StringAssert.Contains(sql, "r.\"BillingMonths\" @> ARRAY[@");
    }

    [TestMethod]
    public void YearMonthSet_ConstantSet_RendersDateArrayLiteral()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.BillingMonths.IsSupersetOf(Months)));
        StringAssert.Contains(sql, "r.\"BillingMonths\" @> ARRAY['2024-01-01','2024-06-01']::date[]");
    }

    private static readonly YearMonthSet Months = YearMonthSet.From(new YearMonth(2024, 1), new YearMonth(2024, 6));
}
