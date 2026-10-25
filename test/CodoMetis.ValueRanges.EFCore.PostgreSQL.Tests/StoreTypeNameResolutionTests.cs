using CodoMetis.ValueRanges.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NpgsqlTypes;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// A range or multirange store type name on its own resolves to this package's type, and a value set never
/// takes an array name, which belongs to Npgsql's own array mapping (<c>docs/efcore.md</c>, #56). A
/// property's CLR type decides before any name: an <see cref="NpgsqlRange{T}"/> property keeps Npgsql's
/// mapping, and a range property given another range's column type is not mapped at all.
/// </summary>
[TestClass]
public sealed class StoreTypeNameResolutionTests
{
    public static IEnumerable<object[]> RangeNames =>
    [
        ["int4range", typeof(Int32Range)], ["int8range", typeof(Int64Range)], ["numrange", typeof(DecimalRange)],
        ["daterange", typeof(DateRange)], ["tsrange", typeof(DateTimeRange)], ["tstzrange", typeof(DateTimeOffsetRange)],
        ["timerange", typeof(TimeRange)], ["float8range", typeof(Float64Range)], ["float4range", typeof(Float32Range)],
        ["int4multirange", typeof(RangeSet<Int32Range, int>)], ["int8multirange", typeof(RangeSet<Int64Range, long>)],
        ["nummultirange", typeof(RangeSet<DecimalRange, decimal>)], ["datemultirange", typeof(RangeSet<DateRange, DateOnly>)],
        ["tsmultirange", typeof(RangeSet<DateTimeRange, DateTime>)], ["tstzmultirange", typeof(RangeSet<DateTimeOffsetRange, DateTimeOffset>)],
        ["timemultirange", typeof(RangeSet<TimeRange, TimeOnly>)], ["float8multirange", typeof(RangeSet<Float64Range, double>)],
        ["float4multirange", typeof(RangeSet<Float32Range, float>)]
    ];

    [TestMethod]
    [DynamicData(nameof(RangeNames))]
    public void RangeStoreTypeName_ResolvesToThisPackagesType(string storeType, Type expected)
    {
        using var context = new TestDbContext();

        Assert.AreEqual(expected, context.GetService<IRelationalTypeMappingSource>().FindMapping(storeType)?.ClrType, storeType);
    }

    [TestMethod]
    [DataRow("text[]"), DataRow("uuid[]"), DataRow("smallint[]"), DataRow("integer[]"), DataRow("bigint[]"), DataRow("numeric[]")]
    [DataRow("date[]"), DataRow("time without time zone[]"), DataRow("timestamp without time zone[]"), DataRow("timestamp with time zone[]")]
    [DataRow("double precision[]"), DataRow("real[]")]
    public void ArrayStoreTypeName_KeepsNpgsqlsMapping(string storeType)
    {
        using var context = new TestDbContext();

        var clrType = context.GetService<IRelationalTypeMappingSource>().FindMapping(storeType)?.ClrType;
        Assert.IsNotNull(clrType, storeType);
        Assert.IsFalse(clrType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueSet<>)),
                       $"{storeType} resolved to the value set {clrType}");
    }

    [TestMethod]
    public void NpgsqlRangeProperty_WithARangeColumnType_KeepsNpgsqlsMapping()
    {
        using var context = new ColumnTypeContext(seatsColumnType: null);

        var raw = context.Model.FindEntityType(typeof(ColumnTypeRow))!.FindProperty(nameof(ColumnTypeRow.Raw))!;
        Assert.AreEqual("int4range", raw.GetColumnType());
        Assert.AreEqual(typeof(NpgsqlRange<int>), raw.GetTypeMapping().ClrType);
    }

    [TestMethod]
    public void RangeProperty_WithAnotherRangesColumnType_FailsModelBuilding()
    {
        using var context = new ColumnTypeContext(seatsColumnType: "int8range");

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => context.Model);
        StringAssert.Contains(error.Message, "'int8range'");
    }

    public sealed class ColumnTypeRow
    {
        public int Id { get; set; }

        public Int32Range Seats { get; set; } = Int32Range.Empty;

        public NpgsqlRange<int> Raw { get; set; }
    }

    private sealed class ColumnTypeContext(string? seatsColumnType) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql("Host=localhost;Database=valueranges_tests;Username=postgres", npgsql => npgsql.UseValueRanges())
                             .ReplaceService<IModelCacheKeyFactory, ColumnTypeCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ColumnTypeRow>().Property(r => r.Raw).HasColumnType("int4range");
            if (seatsColumnType is not null) modelBuilder.Entity<ColumnTypeRow>().Property(r => r.Seats).HasColumnType(seatsColumnType);
        }

        public string? SeatsColumnType => seatsColumnType;
    }

    private sealed class ColumnTypeCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), ((ColumnTypeContext)context).SeatsColumnType, designTime);
    }
}
