using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

public sealed class UpdateRow
{
    public int Id { get; set; }
    public StringSet Codes { get; set; } = StringSet.Empty;
    public StringSet? MaybeCodes { get; set; }
    public UpdateDoc Doc { get; set; } = new();
}

public sealed class UpdateDoc
{
    public StringSet Tags { get; set; } = StringSet.Empty;
    public StringSet? MaybeTags { get; set; }
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
/// <c>ExecuteUpdate</c> setting a value set inside a JSON column is refused while the command is built —
/// no database needed, the refusal comes before a connection. What stays allowed is exercised against a
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

    // -- Null gets its own message (#57); what PostgreSQL does with each is measured in ExecuteUpdateJsonTests --

    private const string ArrayReason = "stores a JSON array";
    private const string NullReason  = "writes NULL as the whole document";

    [TestMethod]
    public void ANullConstant_IsRefusedForWhatNullDoes()
        => AssertNullRefusal(() => new UpdateTestContext().Rows.ExecuteUpdate(s => s.SetProperty(r => r.Doc.MaybeTags, (StringSet?)null)));

    [TestMethod]
    public void ANullParameter_IsRefusedForWhatNullDoes()
    {
        StringSet? none = null;
        AssertNullRefusal(() => new UpdateTestContext().Rows.ExecuteUpdate(s => s.SetProperty(r => r.Doc.MaybeTags, none)));
    }

    [TestMethod]
    public void AConditionalWithANullBranch_IsRefusedForBothOutcomes()
    {
        var some = StringSet.From("a");
        AssertMaybeNullRefusal(() => new UpdateTestContext().Rows.ExecuteUpdate(
            s => s.SetProperty(r => r.Doc.MaybeTags, r => r.Id > 0 ? null : some)));
    }

    [TestMethod]
    public void ANullableColumn_IsRefusedForBothOutcomes()
        => AssertMaybeNullRefusal(() => new UpdateTestContext().Rows.ExecuteUpdate(
            s => s.SetProperty(r => r.Doc.MaybeTags, r => r.MaybeCodes)));

    [TestMethod]
    public void ARequiredColumn_IsRefusedForTheJsonArrayOnly()
    {
        using var context = new UpdateTestContext();

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Rows.ExecuteUpdate(s => s.SetProperty(r => r.Doc.Tags, r => r.Codes)));

        StringAssert.Contains(refusal.Message, ArrayReason);
        Assert.IsFalse(refusal.Message.Contains(NullReason), refusal.Message);
    }

    private static void AssertNullRefusal(Action update)
    {
        var refusal = Assert.ThrowsExactly<InvalidOperationException>(update);

        StringAssert.Contains(refusal.Message, "'Doc.MaybeTags' inside a JSON column to null");
        StringAssert.Contains(refusal.Message, NullReason);
        StringAssert.Contains(refusal.Message, "SaveChanges, which writes JSON null");
        Assert.IsFalse(refusal.Message.Contains(ArrayReason), refusal.Message);
    }

    private static void AssertMaybeNullRefusal(Action update)
    {
        var refusal = Assert.ThrowsExactly<InvalidOperationException>(update);

        StringAssert.Contains(refusal.Message, "'Doc.MaybeTags' inside a JSON column.");
        StringAssert.Contains(refusal.Message, ArrayReason);
        StringAssert.Contains(refusal.Message, NullReason);
    }
}
