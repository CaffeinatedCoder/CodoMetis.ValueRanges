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

/// <param name="postgresMajor">The PostgreSQL version Npgsql is told it targets; unset when omitted.</param>
public sealed class UpdateTestContext(int? postgresMajor = null) : DbContext
{
    public DbSet<UpdateRow> Rows => Set<UpdateRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            "Host=localhost;Database=valueranges_tests;Username=postgres",
            npgsql =>
            {
                npgsql.UseValueRanges();
                if (postgresMajor is { } major) npgsql.SetPostgresVersion(major, 0);
            });

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
/// <para>
/// EF Core writes such a setter with <c>jsonb_set(doc, path, to_jsonb(@p))</c>, binding the set's
/// PostgreSQL array: the document then holds a JSON array where the reader expects the set's literal
/// string, the update reports success, and the row no longer loads.
/// </para>
/// <para>
/// Every test runs once per shape Npgsql writes the setter in, and the refusal must name the property
/// path in both (#303). With the version unset (<c>null</c>), which Npgsql EF 10 takes for PostgreSQL 14,
/// every 10.x patch writes <c>jsonb_set</c>; Npgsql EF 10.0.3 wraps a value that may be null in
/// <c>COALESCE(…, 'null'::jsonb)</c>. With PostgreSQL 16 configured (<c>16</c>), Npgsql EF 10.0.3 writes
/// <c>jsonb_set_lax</c> (npgsql/efcore.pg#3855) — but 10.0.0–10.0.2 still write <c>jsonb_set</c>, so on
/// the 10.0.0 floor this repository pins, the second leg repeats the first. It exercises
/// <c>jsonb_set_lax</c> only where the test host resolves Npgsql EF 10.0.3 or later.
/// </para>
/// </remarks>
[TestClass]
public sealed class ExecuteUpdateGuardTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow(16)]
    public void AValueSetInsideAJsonColumn_IsRefused(int? postgresMajor)
    {
        using var context = new UpdateTestContext(postgresMajor);

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Rows.ExecuteUpdate(s => s.SetProperty(r => r.Doc.Tags, StringSet.From("a"))));

        StringAssert.Contains(refusal.Message, "'Doc.Tags'");
        StringAssert.Contains(refusal.Message, "SaveChanges");
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(16)]
    public void AValueSetInANestedComplexTypeOfAJsonColumn_IsRefused(int? postgresMajor)
    {
        using var context = new UpdateTestContext(postgresMajor);

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Rows.ExecuteUpdate(s => s.SetProperty(r => r.Doc.Nested.Flags, StringSet.From("a"))));

        StringAssert.Contains(refusal.Message, "'Doc.Nested.Flags'");
    }

    // -- Null gets its own message (#57); what PostgreSQL does with each is measured in ExecuteUpdateJsonTests --
    //
    // Until 10.1.0 a value that may be null on some row was refused for a second reason, "writes NULL as
    // the whole document": jsonb_set returns NULL for a NULL value. That holds before Npgsql EF 10.0.3 only,
    // which writes such a value through jsonb_set_lax or coalesces it to JSON null, so only the JSON array
    // is refused (#303).

    private const string ArrayReason = "stores a JSON array";
    private const string NullReason  = "cannot type the untyped NULL (42804)";

    [TestMethod]
    [DataRow(null)]
    [DataRow(16)]
    public void ANullConstant_IsRefusedForWhatNullDoes(int? postgresMajor)
        => AssertNullRefusal(() => new UpdateTestContext(postgresMajor).Rows.ExecuteUpdate(s => s.SetProperty(r => r.Doc.MaybeTags, (StringSet?)null)));

    [TestMethod]
    [DataRow(null)]
    [DataRow(16)]
    public void ANullParameter_IsRefusedForWhatNullDoes(int? postgresMajor)
    {
        StringSet? none = null;
        AssertNullRefusal(() => new UpdateTestContext(postgresMajor).Rows.ExecuteUpdate(s => s.SetProperty(r => r.Doc.MaybeTags, none)));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(16)]
    public void AConditionalWithANullBranch_IsRefusedForTheJsonArrayOnly(int? postgresMajor)
    {
        var some = StringSet.From("a");
        AssertMaybeNullRefusal(() => new UpdateTestContext(postgresMajor).Rows.ExecuteUpdate(
            s => s.SetProperty(r => r.Doc.MaybeTags, r => r.Id > 0 ? null : some)));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(16)]
    public void ANullableColumn_IsRefusedForTheJsonArrayOnly(int? postgresMajor)
        => AssertMaybeNullRefusal(() => new UpdateTestContext(postgresMajor).Rows.ExecuteUpdate(
            s => s.SetProperty(r => r.Doc.MaybeTags, r => r.MaybeCodes)));

    [TestMethod]
    [DataRow(null)]
    [DataRow(16)]
    public void ARequiredColumn_IsRefusedForTheJsonArrayOnly(int? postgresMajor)
    {
        using var context = new UpdateTestContext(postgresMajor);

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
        Assert.IsFalse(refusal.Message.Contains(NullReason), refusal.Message);
    }
}
