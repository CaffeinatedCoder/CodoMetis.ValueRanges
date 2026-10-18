using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// <see cref="Float32Range"/> over the custom <c>float4range</c> and <see cref="Float32Set"/> over
/// <c>real[]</c>, without a database (#153): the column types, the migration the documented
/// <c>HasPostgresRange("float4range", "real")</c> produces — no <c>subtype_diff</c>, since
/// <c>float4mi</c> returns <c>real</c> and cannot be one — and the literals EF Core inlines, which are typed
/// (the range operators are polymorphic) and are the text <c>float4out</c> prints, never a float written
/// through <see cref="double"/>.
/// </summary>
[TestClass]
public sealed class Float32TranslationTests
{
    [TestMethod]
    public void Columns_AreFloat8Range_Float8Multirange_AndDoublePrecisionArray()
    {
        using var context = new ScoreContext();
        var entity = context.Model.FindEntityType(typeof(Scorecard))!;

        Assert.AreEqual("float4range", entity.FindProperty(nameof(Scorecard.Score))!.GetColumnType());
        Assert.AreEqual("float4multirange", entity.FindProperty(nameof(Scorecard.Bands))!.GetColumnType());
        Assert.AreEqual("real[]", entity.FindProperty(nameof(Scorecard.Weights))!.GetColumnType());
        Assert.AreEqual("real[]", entity.FindProperty(nameof(Scorecard.WrappedWeights))!.GetColumnType());
    }

    [TestMethod]
    public void Migration_CreatesFloat4Range_WithoutASubtypeDiff_AheadOfTheTable()
    {
        using var context = new ScoreContext();
        var services  = DesignTimeServices(context);
        var migration = services.GetRequiredService<IMigrationsScaffolder>().ScaffoldMigration("Initial", "Consumer.Migrations");

        // Npgsql's range annotation: "subtype,canonical,subtype opclass,collation,subtype diff".
        StringAssert.Contains(migration.MigrationCode, ".Annotation(\"Npgsql:Range:float4range\", \"real,,,,\")");

        var model      = context.GetService<IDesignTimeModel>().Model;
        var operations = services.GetRequiredService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var sql        = string.Join(Environment.NewLine,
                                     services.GetRequiredService<IMigrationsSqlGenerator>().Generate(operations, model).Select(c => c.CommandText));

        var createType = Regex.Match(sql, @"CREATE TYPE float4range AS RANGE \(\s*subtype = real\s*\)", RegexOptions.IgnoreCase);
        Assert.IsTrue(createType.Success, $"The migration SQL creates no float4range type over real alone:{Environment.NewLine}{sql}");
        Assert.IsGreaterThan(createType.Index, sql.IndexOf("CREATE TABLE", StringComparison.Ordinal));
        StringAssert.Contains(sql, "\"Score\" float4range NOT NULL");
        StringAssert.Contains(sql, "\"Bands\" float4multirange NOT NULL");
    }

    [TestMethod]
    public void ContainsConstant_IsATypedFloat4OutLiteral()
    {
        var sql = Sql(db => db.Scorecards.Where(s => s.Score.Contains(0.1f)));
        StringAssert.Contains(sql, "s.\"Score\" @> '0.1'::real");

        sql = Sql(db => db.Scorecards.Where(s => s.Score.Contains(1e6f)));
        StringAssert.Contains(sql, "s.\"Score\" @> '1e+06'::real");
    }

    [TestMethod]
    public void RangeConstant_IsTheFloat8OutLiteral()
    {
        var sql = Sql(db => db.Scorecards.Where(s => s.Score == Float32Range.CreateFinite(0.1f, 1e7f)));
        StringAssert.Contains(sql, "s.\"Score\" = '[0.1,1e+07)'::float4range");
    }

    [TestMethod]
    public void SetContainsConstant_IsTheExactElement()
    {
        var sql = Sql(db => db.Scorecards.Where(s => s.Weights.Contains(0.1f)));
        StringAssert.Contains(sql, "'0.1'");
        Assert.IsFalse(sql.Contains("0.10000000149011612", StringComparison.Ordinal), sql);
    }

    [TestMethod]
    public void SetConstant_IsTheFloat8OutLiteral()
    {
        var weights = Float32Set.From(1e6f, 0.1f);
        var sql     = Sql(db => db.Scorecards.Where(s => s.Weights == EF.Constant(weights)));
        StringAssert.Contains(sql, "s.\"Weights\" = ARRAY['0.1','1e+06']::real[]");
    }

    [TestMethod]
    public void NonFiniteParameter_IsRefusedWhenBound()
    {
        var nan = float.NaN;
        using var context = new ScoreContext();
        var query = context.Scorecards.Where(s => s.Score.Contains(nan));

        // ToQueryString binds the parameter through its mapping, which applies the finite guard.
        var error = Assert.Throws<Exception>(() => query.ToQueryString());
        Assert.IsTrue(Chain(error).Any(ex => ex is ArgumentException && ex.Message.Contains("finite numbers only")), error.ToString());
    }

    private static IEnumerable<Exception> Chain(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException) yield return exception;
    }

    private static string Sql<T>(Func<ScoreContext, IQueryable<T>> query)
    {
        using var context = new ScoreContext();
        return query(context).ToQueryString();
    }

    private static IServiceProvider DesignTimeServices(DbContext context)
    {
        var assembly = Assembly.GetExecutingAssembly();
        return new DesignTimeServicesBuilder(assembly, assembly, new OperationReporter(handler: null), []).Build(context);
    }

    private sealed class Scorecard
    {
        public int Id { get; set; }

        public Float32Range Score { get; set; } = Float32Range.Empty;

        public RangeSet<Float32Range, float> Bands { get; set; } = RangeSet<Float32Range, float>.Empty;

        public Float32Set Weights { get; set; } = Float32Set.Empty;

        public Float32Set<TestShare> WrappedWeights { get; set; } = Float32Set<TestShare>.Empty;
    }

    private sealed class ScoreContext : DbContext
    {
        public DbSet<Scorecard> Scorecards => Set<Scorecard>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql("Host=localhost;Database=design_time", npgsql => npgsql.UseValueRanges());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.HasPostgresRange("float4range", "real");
    }
}

/// <summary>A float-backed wrapper element, shaped like a generated one.</summary>
public readonly record struct TestShare : IFormattable, IParsable<TestShare>, IComparable<TestShare>
{
    private readonly float _value;

    public TestShare(float value) => _value = value;

    public static TestShare Parse(string s, IFormatProvider? provider)
        => new(float.Parse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestShare result)
    {
        var ok = float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value);
        result = new TestShare(value);
        return ok;
    }

    public int CompareTo(TestShare other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? System.Globalization.CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, System.Globalization.CultureInfo.InvariantCulture);
}
