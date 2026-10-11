using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

public sealed class UpdateRow
{
    public int Id { get; set; }
    public StringSet Codes { get; set; } = StringSet.Empty;
    public UpdateDoc Doc { get; set; } = new();
}

public sealed class UpdateDoc
{
    public StringSet Tags { get; set; } = StringSet.Empty;
    public DateTimeRange Period { get; set; } = DateTimeRange.Empty;
    public UpdateNested Nested { get; set; } = new();
}

public sealed class UpdateNested
{
    public StringSet Flags { get; set; } = StringSet.Empty;
}

public sealed class UpdateTestContext : DbContext
{
    public DbSet<UpdateRow> Rows => Set<UpdateRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            "Host=localhost;Database=valueranges_tests;Username=postgres",
            npgsql => npgsql.UseValueRanges());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<UpdateRow>().ComplexProperty(r => r.Doc, doc =>
        {
            doc.ToJson();
            doc.ComplexProperty(d => d.Nested);
        });
}

/// <summary>
/// <c>ExecuteUpdate</c> setting a value set inside a JSON column is refused at query compilation — no
/// database needed, the refusal comes before a connection. What stays allowed is exercised against a
/// live server in <c>ExecuteUpdateJsonTests</c> (integration).
/// </summary>
/// <remarks>
/// EF Core writes such a setter with <c>jsonb_set(doc, path, to_jsonb(@p))</c>, binding the set's
/// PostgreSQL array: the document then holds a JSON array where the reader expects the set's literal
/// string, the update reports success, and the row no longer loads.
/// </remarks>
[TestClass]
public sealed class ExecuteUpdateGuardTests
{
    [TestMethod]
    public void AValueSetInsideAJsonColumn_IsRefused()
    {
        using var context = new UpdateTestContext();

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Rows.ExecuteUpdate(s => s.SetProperty(r => r.Doc.Tags, StringSet.From("a"))));

        StringAssert.Contains(refusal.Message, "'Doc.Tags'");
        StringAssert.Contains(refusal.Message, "SaveChanges");
    }

    [TestMethod]
    public void AValueSetInANestedComplexTypeOfAJsonColumn_IsRefused()
    {
        using var context = new UpdateTestContext();

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Rows.ExecuteUpdate(s => s.SetProperty(r => r.Doc.Nested.Flags, StringSet.From("a"))));

        StringAssert.Contains(refusal.Message, "'Doc.Nested.Flags'");
    }

    /// <summary>
    /// The guard matches EF Core's private lowering of <c>ExecuteUpdate</c> by its shape. If an EF
    /// upgrade changes it, the guard would silently stop recognising the call; this fails instead.
    /// </summary>
    [TestMethod]
    public void EfCoresExecuteUpdateLowering_HasTheShapeTheGuardMatches()
    {
        var lowering = typeof(EntityFrameworkQueryableExtensions)
                      .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                      .Where(m => m is { Name: "ExecuteUpdate", IsGenericMethodDefinition: true })
                      .Where(m => m.GetParameters() is [_, { ParameterType: var setters }] && setters == typeof(IReadOnlyList<ITuple>))
                      .ToList();

        Assert.AreEqual(
            1, lowering.Count,
            "EF Core no longer lowers ExecuteUpdate to ExecuteUpdate<TSource>(IQueryable<TSource>, IReadOnlyList<ITuple>). "
          + "Update JsonValueSetUpdateGuard in ValueRangesQueryExpressionInterceptor to the new shape.");
    }
}
