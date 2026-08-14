using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

[TestClass]
public sealed class SetModelMappingTests
{
    private static string ColumnTypeOf(string propertyName)
    {
        using var context = new TestDbContext();
        var property = context.Model.FindEntityType(typeof(Booking))!.FindProperty(propertyName)!;
        return property.GetColumnType();
    }

    [TestMethod]
    public void StringSet_MapsTo_TextArray() => Assert.AreEqual("text[]", ColumnTypeOf(nameof(Booking.Tags)));

    [TestMethod]
    public void WrapperStringSet_MapsTo_TextArray() => Assert.AreEqual("text[]", ColumnTypeOf(nameof(Booking.Permissions)));

    [TestMethod]
    public void GuidSet_MapsTo_UuidArray() => Assert.AreEqual("uuid[]", ColumnTypeOf(nameof(Booking.Uuids)));

    [TestMethod]
    public void Int16Set_MapsTo_SmallintArray() => Assert.AreEqual("smallint[]", ColumnTypeOf(nameof(Booking.SmallCodes)));

    [TestMethod]
    public void Int32Set_MapsTo_IntegerArray() => Assert.AreEqual("integer[]", ColumnTypeOf(nameof(Booking.Codes)));

    [TestMethod]
    public void Int64Set_MapsTo_BigintArray() => Assert.AreEqual("bigint[]", ColumnTypeOf(nameof(Booking.BigCodes)));

    [TestMethod]
    public void DecimalSet_MapsTo_NumericArray() => Assert.AreEqual("numeric[]", ColumnTypeOf(nameof(Booking.Rates)));

    [TestMethod]
    public void DateSet_MapsTo_DateArray() => Assert.AreEqual("date[]", ColumnTypeOf(nameof(Booking.BlackoutDays)));

    [TestMethod]
    public void TimeSet_MapsTo_TimeArray()
        => Assert.AreEqual("time without time zone[]", ColumnTypeOf(nameof(Booking.Slots)));

    [TestMethod]
    public void DateTimeSet_MapsTo_TimestampArray()
        => Assert.AreEqual("timestamp without time zone[]", ColumnTypeOf(nameof(Booking.WallClocks)));

    [TestMethod]
    public void DateTimeOffsetSet_MapsTo_TimestamptzArray()
        => Assert.AreEqual("timestamp with time zone[]", ColumnTypeOf(nameof(Booking.Instants)));

    [TestMethod]
    public void PlainStringArray_KeepsNativeProviderMapping()
        => Assert.AreEqual("text[]", ColumnTypeOf(nameof(Booking.PlainTags)));

    [TestMethod]
    public void StoreTypeNameAlone_NeverResolvesToASetType()
    {
        // "text[]" belongs to the provider's native array mappings — the set plugin must not
        // claim store names, or plain string[] properties and scaffolding would be hijacked.
        using var context = new TestDbContext();
        var mappingSource = context.GetService<IRelationalTypeMappingSource>();

        var byStoreName = mappingSource.FindMapping("text[]");

        Assert.IsNotNull(byStoreName);
        Assert.AreNotEqual(typeof(StringSet), byStoreName.ClrType);
    }

    [TestMethod]
    public void WithoutUseValueRanges_SetProperty_FailsModelBuild()
    {
        using var context = new PlainNpgsqlContext();

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = context.Model);
    }

    private sealed class PlainNpgsqlContext : DbContext
    {
        public DbSet<Booking> Bookings => Set<Booking>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql("Host=localhost;Database=valueranges_tests;Username=postgres");
    }
}
