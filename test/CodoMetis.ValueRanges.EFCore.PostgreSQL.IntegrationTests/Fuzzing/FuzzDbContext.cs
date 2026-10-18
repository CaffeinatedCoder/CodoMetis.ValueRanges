using CodoMetis.ValueRanges.Conventions.Tests;
using CodoMetis.ValueRanges.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;

/// <summary>One fuzz row of a range type: two ranges and two multiranges, all drawn for the same row.</summary>
public sealed class FuzzRangeRow<TRange, T>
    where TRange : class, IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    public int Id { get; set; }

    public TRange Left { get; set; } = TRange.Empty;

    public TRange Right { get; set; } = TRange.Empty;

    public RangeSet<TRange, T> LeftSet { get; set; } = RangeSet<TRange, T>.Empty;

    public RangeSet<TRange, T> RightSet { get; set; } = RangeSet<TRange, T>.Empty;
}

/// <summary>One fuzz row of a value set type.</summary>
public sealed class FuzzSetRow<TSet, T>
    where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
    where T : IEquatable<T>
{
    public int Id { get; set; }

    public TSet Left { get; set; } = TSet.Empty;

    public TSet Right { get; set; } = TSet.Empty;
}

/// <summary>Every operation of one query, projected per row.</summary>
public sealed class FuzzOut
{
    public int Id { get; set; }

    public object?[] Values { get; set; } = [];
}

/// <summary>
/// The types the sweep covers, discovered by reflection from the shipping assemblies exactly as
/// <c>MappingParityTests</c> discovers them — so a range or set type added to a package is fuzzed
/// without an edit here, and one the sweep cannot draw fails loudly in <see cref="FuzzDomains"/>.
/// </summary>
internal static class FuzzTypes
{
    private static IEnumerable<Type> Exported => new[] { typeof(Int32Range), typeof(LocalDateRange) }
       .SelectMany(marker => marker.Assembly.GetExportedTypes());

    /// <summary>Every range union type, with its element type.</summary>
    public static IReadOnlyList<(Type Range, Type Element)> Ranges { get; } =
    [
        .. Exported
           .Where(type => type is { IsClass: true, IsGenericTypeDefinition: false, DeclaringType: null })
           .Select(type => (Range: type, Element: ElementOf(type, typeof(IRange<>))))
           .Where(pair => pair.Element is not null)
           .Select(pair => (pair.Range, pair.Element!))
           .OrderBy(pair => pair.Range.Name, StringComparer.Ordinal)
    ];

    /// <summary>Every value set type: the closed ones, and each wrapper arity over its representative element.</summary>
    public static IReadOnlyList<(Type Set, Type Element, Type Primitive)> Sets { get; } =
    [
        .. Exported
           .Where(type => type is { IsClass: true, IsAbstract: false } && ElementOf(type, typeof(IValueSet<>)) is not null)
           .Select(type => type.IsGenericTypeDefinition ? type.MakeGenericType(WrapperElements.For(type)) : type)
           .Select(type => (Set: type, Element: ElementOf(type, typeof(IValueSet<>))))
           .Where(pair => pair.Element is not null)
           .Select(pair => (pair.Set, pair.Element!, PrimitiveOf(pair.Set, pair.Element!)))
           .OrderBy(triple => Name(triple.Set), StringComparer.Ordinal)
    ];

    private static Type? ElementOf(Type type, Type openInterface) =>
        type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface)
           ?.GetGenericArguments()[0];

    // A wrapper arity's primitive is the element type of its closed sibling (StringSet<T> → StringSet → string).
    private static Type PrimitiveOf(Type set, Type element)
    {
        if (!set.IsGenericType) return element;
        var closedName = set.GetGenericTypeDefinition().Name.Split('`')[0];
        var sibling    = set.Assembly.GetExportedTypes().Single(t => t.Name == closedName && !t.IsGenericType);
        return ElementOf(sibling, typeof(IValueSet<>))!;
    }

    /// <summary>A readable, table-safe name: <c>StringSet_TextKey</c>.</summary>
    public static string Name(Type type) =>
        type.IsGenericType
            ? $"{type.Name.Split('`')[0]}_{string.Join("_", type.GetGenericArguments().Select(Name))}"
            : type.Name;
}

/// <summary>
/// The fuzz tables, one per discovered type, in a database of their own — so its schema is created
/// whole by <c>EnsureCreated</c> next to the suite's, and its rows never meet another test's.
/// </summary>
public sealed class FuzzDbContext : DbContext
{
    internal const string DatabaseName = "valueranges_fuzz";

    internal static string ConnectionString =>
        new NpgsqlConnectionStringBuilder(ContainerLifecycle.ConnectionString) { Database = DatabaseName }.ConnectionString;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            ConnectionString,
            npgsql => npgsql.UseValueRangesNodaTime()
                            .ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var (range, element) in FuzzTypes.Ranges)
        {
            var row = modelBuilder.Entity(typeof(FuzzRangeRow<,>).MakeGenericType(range, element));
            row.ToTable($"fuzz_{FuzzTypes.Name(range)}");
            row.Property(nameof(FuzzRangeRow<Int32Range, int>.Id)).ValueGeneratedNever();
        }

        foreach (var (set, element, _) in FuzzTypes.Sets)
        {
            var row = modelBuilder.Entity(typeof(FuzzSetRow<,>).MakeGenericType(set, element));
            row.ToTable($"fuzz_{FuzzTypes.Name(set)}");
            row.Property(nameof(FuzzSetRow<StringSet, string>.Id)).ValueGeneratedNever();
        }

        modelBuilder.HasPostgresRange("timerange", "time");
        modelBuilder.HasPostgresRange("float8range", "double precision");
        modelBuilder.HasPostgresRange("float4range", "real");
    }
}
