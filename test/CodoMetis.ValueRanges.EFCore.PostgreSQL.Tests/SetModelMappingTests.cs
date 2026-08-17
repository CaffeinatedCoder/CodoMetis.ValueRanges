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

    // -- Validated wrapper arities --
    //
    // Each family is registered by open generic definition and its closed instantiations built
    // on demand, so a family missing from the registry's table is not a compile error: the
    // property simply resolves to no mapping and the model build fails at the consumer. One
    // assertion per arity, so that failure lands here instead.

    [TestMethod]
    public void WrapperStringSet_MapsTo_TextArray() => Assert.AreEqual("text[]", ColumnTypeOf(nameof(Booking.Permissions)));

    [TestMethod]
    public void WrapperGuidSet_MapsTo_UuidArray()
        => Assert.AreEqual("uuid[]", ColumnTypeOf(nameof(Booking.WrappedUuids)));

    [TestMethod]
    public void WrapperInt16Set_MapsTo_SmallintArray()
        => Assert.AreEqual("smallint[]", ColumnTypeOf(nameof(Booking.WrappedSmallCodes)));

    [TestMethod]
    public void WrapperInt32Set_MapsTo_IntegerArray()
        => Assert.AreEqual("integer[]", ColumnTypeOf(nameof(Booking.WrappedCodes)));

    [TestMethod]
    public void WrapperInt64Set_MapsTo_BigintArray()
        => Assert.AreEqual("bigint[]", ColumnTypeOf(nameof(Booking.WrappedBigCodes)));

    [TestMethod]
    public void WrapperDecimalSet_MapsTo_NumericArray()
        => Assert.AreEqual("numeric[]", ColumnTypeOf(nameof(Booking.WrappedRates)));

    [TestMethod]
    public void WrapperDateSet_MapsTo_DateArray()
        => Assert.AreEqual("date[]", ColumnTypeOf(nameof(Booking.WrappedDays)));

    [TestMethod]
    public void WrapperTimeSet_MapsTo_TimeArray()
        => Assert.AreEqual("time without time zone[]", ColumnTypeOf(nameof(Booking.WrappedSlots)));

    [TestMethod]
    public void WrapperDateTimeSet_MapsTo_TimestampArray()
        => Assert.AreEqual("timestamp without time zone[]", ColumnTypeOf(nameof(Booking.Audits)));

    [TestMethod]
    public void WrapperDateTimeOffsetSet_MapsTo_TimestamptzArray()
        => Assert.AreEqual("timestamp with time zone[]", ColumnTypeOf(nameof(Booking.WrappedInstants)));

    /// <summary>
    /// Every arity maps to the same column type as the closed sibling it parallels — the point
    /// of the arities is that the storage shape does not change when a domain type replaces the
    /// primitive.
    /// </summary>
    [TestMethod]
    public void EveryWrapperArity_MapsToTheSameColumnTypeAsItsClosedSibling()
    {
        (string Closed, string Wrapper)[] pairs =
        [
            (nameof(Booking.Tags),         nameof(Booking.Permissions)),
            (nameof(Booking.Uuids),        nameof(Booking.WrappedUuids)),
            (nameof(Booking.SmallCodes),   nameof(Booking.WrappedSmallCodes)),
            (nameof(Booking.Codes),        nameof(Booking.WrappedCodes)),
            (nameof(Booking.BigCodes),     nameof(Booking.WrappedBigCodes)),
            (nameof(Booking.Rates),        nameof(Booking.WrappedRates)),
            (nameof(Booking.BlackoutDays), nameof(Booking.WrappedDays)),
            (nameof(Booking.Slots),        nameof(Booking.WrappedSlots)),
            (nameof(Booking.WallClocks),   nameof(Booking.Audits)),
            (nameof(Booking.Instants),     nameof(Booking.WrappedInstants))
        ];

        foreach (var (closed, wrapper) in pairs)
        {
            Assert.AreEqual(
                ColumnTypeOf(closed), ColumnTypeOf(wrapper),
                $"{wrapper} does not map to the same column type as {closed}.");
        }
    }

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
