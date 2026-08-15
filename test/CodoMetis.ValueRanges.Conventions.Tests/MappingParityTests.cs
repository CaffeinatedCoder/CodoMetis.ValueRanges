using CodoMetis.ValueRanges.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Parity between the types the packages define and the types the EF plugin maps. A range or
/// set type added to a shipping assembly without a registry entry compiles, tests and packs
/// clean; the consumer discovers it when a model build fails — or worse, when the property is
/// mapped as something else entirely.
/// </summary>
/// <remarks>
/// The types are discovered by reflection, so this test needs no edit when one is added — which
/// is the whole point. It asserts through <see cref="IRelationalTypeMappingSource"/>, the public
/// seam the provider itself resolves columns through, rather than reaching into the internal
/// registries.
/// </remarks>
[TestClass]
public sealed class MappingParityTests
{
    private sealed class BclContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseNpgsql(
                "Host=localhost;Database=conventions;Username=postgres",
                npgsql => npgsql.UseValueRanges());
    }

    private sealed class NodaContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseNpgsql(
                "Host=localhost;Database=conventions;Username=postgres",
                npgsql => npgsql.UseValueRangesNodaTime());
    }

    private static IRelationalTypeMappingSource MappingSourceOf(DbContext context) =>
        context.GetService<IRelationalTypeMappingSource>();

    private static IEnumerable<Type> ExportedTypesOf(Type marker) => marker.Assembly.GetExportedTypes();

    /// <summary>
    /// The range union types — the ones a consumer declares a property of. Each is an
    /// <em>abstract</em> record with five sealed variants nested inside it, so filtering to
    /// concrete types here would find nothing at all; the variants are excluded instead by
    /// their declaring type, since a property is declared as the union, never as a variant.
    /// </summary>
    private static IEnumerable<Type> RangeTypesIn(Type marker) =>
        ExportedTypesOf(marker)
           .Where(type => type is { IsClass: true, IsGenericTypeDefinition: false, DeclaringType: null })
           .Where(type => type.GetInterfaces().Any(@interface =>
                @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IRange<>)))
           .OrderBy(type => type.Name, StringComparer.Ordinal);

    /// <summary>Concrete, non-generic set types.</summary>
    private static IEnumerable<Type> SetTypesIn(Type marker) =>
        ExportedTypesOf(marker)
           .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
           .Where(type => type.GetInterfaces().Any(@interface =>
                @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IValueSet<>)))
           .OrderBy(type => type.Name, StringComparer.Ordinal);

    private static Type ElementTypeOf(Type rangeOrSet, Type openInterface) =>
        rangeOrSet.GetInterfaces()
                  .First(@interface => @interface.IsGenericType
                                    && @interface.GetGenericTypeDefinition() == openInterface)
                  .GetGenericArguments()[0];

    private static void AssertMapped(IRelationalTypeMappingSource source, Type clrType, string what)
    {
        var mapping = source.FindMapping(clrType);

        Assert.IsNotNull(
            mapping,
            $"{clrType.Name} is a public {what} that the EF plugin does not map. Every range and set "
          + "type must be wired through RangeTypeRegistry.Register / SetTypeRegistry.Register — a type "
          + "the registry does not know is not a compile error, it is a broken model at runtime.");

        Assert.IsFalse(
            string.IsNullOrWhiteSpace(mapping.StoreType),
            $"{clrType.Name} maps to an empty store type.");
    }

    /// <summary>
    /// Same guard as the value set contract tests: every assertion in this class iterates a
    /// reflection-discovered list, and a list that goes empty passes everything.
    /// </summary>
    [TestMethod]
    public void Discovery_FindsEveryRangeAndSetType()
    {
        // 7 BCL ranges + 4 NodaTime ranges; 10 closed BCL sets + 5 NodaTime sets, as of 6.1.0.
        AssertAtLeast(RangeTypesIn(typeof(Int32Range)),    7,  "BCL range types");
        AssertAtLeast(RangeTypesIn(typeof(LocalDateRange)), 4, "NodaTime range types");
        AssertAtLeast(SetTypesIn(typeof(StringSet)),      10,  "BCL set types");
        AssertAtLeast(SetTypesIn(typeof(LocalDateSet)),    5,  "NodaTime set types");

        static void AssertAtLeast(IEnumerable<Type> discovered, int floor, string what)
        {
            var found = discovered.Select(type => type.Name).ToList();

            Assert.IsTrue(
                found.Count >= floor,
                $"Discovery found {found.Count} {what}, fewer than the {floor} known to exist: "
              + $"{string.Join(", ", found)}.");
        }
    }

    [TestMethod]
    public void EveryCoreRangeType_IsMappedByThePlugin()
    {
        using var context = new BclContext();
        var       source  = MappingSourceOf(context);

        foreach (var rangeType in RangeTypesIn(typeof(Int32Range)))
        {
            AssertMapped(source, rangeType, "range type");

            var element = ElementTypeOf(rangeType, typeof(IRange<>));
            AssertMapped(source, typeof(RangeSet<,>).MakeGenericType(rangeType, element), "multirange");
        }
    }

    [TestMethod]
    public void EveryCoreSetType_IsMappedByThePlugin()
    {
        using var context = new BclContext();
        var       source  = MappingSourceOf(context);

        foreach (var setType in SetTypesIn(typeof(StringSet))) AssertMapped(source, setType, "value set type");
    }

    [TestMethod]
    public void EveryNodaTimeRangeType_IsMappedByTheSatellite()
    {
        using var context = new NodaContext();
        var       source  = MappingSourceOf(context);

        foreach (var rangeType in RangeTypesIn(typeof(LocalDateRange)))
        {
            AssertMapped(source, rangeType, "range type");

            var element = ElementTypeOf(rangeType, typeof(IRange<>));
            AssertMapped(source, typeof(RangeSet<,>).MakeGenericType(rangeType, element), "multirange");
        }
    }

    [TestMethod]
    public void EveryNodaTimeSetType_IsMappedByTheSatellite()
    {
        using var context = new NodaContext();
        var       source  = MappingSourceOf(context);

        foreach (var setType in SetTypesIn(typeof(LocalDateSet))) AssertMapped(source, setType, "value set type");
    }

    /// <summary>
    /// The satellite's entry point must imply the base one — the documented contract of
    /// <c>UseValueRangesNodaTime()</c>, and the reason a consumer can mix both families in one
    /// model without calling two methods.
    /// </summary>
    [TestMethod]
    public void TheNodaTimeSatellite_AlsoMapsTheBclTypes()
    {
        using var context = new NodaContext();
        var       source  = MappingSourceOf(context);

        foreach (var rangeType in RangeTypesIn(typeof(Int32Range))) AssertMapped(source, rangeType, "range type");
        foreach (var setType in SetTypesIn(typeof(StringSet))) AssertMapped(source, setType, "value set type");
    }

    /// <summary>
    /// A plain array must keep the provider's own mapping. The set types claim CLR types, never
    /// store type names — claiming <c>text[]</c> would hijack every <c>string[]</c> property in
    /// the model and everything scaffolding produces.
    /// </summary>
    [TestMethod]
    public void PlainArrays_KeepTheirNativeProviderMapping()
    {
        using var context = new BclContext();
        var       source  = MappingSourceOf(context);

        Assert.AreEqual(typeof(string[]), source.FindMapping(typeof(string[]))?.ClrType);
        Assert.AreEqual(typeof(Guid[]),   source.FindMapping(typeof(Guid[]))?.ClrType);

        Assert.AreNotEqual(
            typeof(StringSet), source.FindMapping("text[]")?.ClrType,
            "The store type name 'text[]' resolved to StringSet. It belongs to the provider's native "
          + "string[] mapping — value sets are resolved from the CLR type only.");
    }
}
