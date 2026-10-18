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
/// <see cref="Float64Range"/> over the custom <c>float8range</c> and <see cref="Float64Set"/> over
/// <c>double precision[]</c>, without a database (#153): the column types, the migration the documented
/// <c>HasPostgresRange("float8range", "double precision")</c> produces — no <c>subtype_diff</c>, since
/// <c>float8mi</c> overflows building a GiST index over extreme values — and the literals EF Core inlines,
/// which are typed (the range operators are polymorphic) and exact (the text <c>float8out</c> prints, not
/// .NET's shortest form, which is a different double for 2^-25).
/// </summary>
[TestClass]
public sealed class Float64TranslationTests
{
    [TestMethod]
    public void Columns_AreFloat8Range_Float8Multirange_AndDoublePrecisionArray()
    {
        using var context = new ScoreContext();
        var entity = context.Model.FindEntityType(typeof(Scorecard))!;

        Assert.AreEqual("float8range", entity.FindProperty(nameof(Scorecard.Score))!.GetColumnType());
        Assert.AreEqual("float8multirange", entity.FindProperty(nameof(Scorecard.Bands))!.GetColumnType());
        Assert.AreEqual("double precision[]", entity.FindProperty(nameof(Scorecard.Weights))!.GetColumnType());
        Assert.AreEqual("double precision[]", entity.FindProperty(nameof(Scorecard.WrappedWeights))!.GetColumnType());
    }

    [TestMethod]
    public void Migration_CreatesFloat8Range_WithoutASubtypeDiff_AheadOfTheTable()
    {
        using var context = new ScoreContext();
        var services  = DesignTimeServices(context);
        var migration = services.GetRequiredService<IMigrationsScaffolder>().ScaffoldMigration("Initial", "Consumer.Migrations");

        // Npgsql's range annotation: "subtype,canonical,subtype opclass,collation,subtype diff".
        StringAssert.Contains(migration.MigrationCode, ".Annotation(\"Npgsql:Range:float8range\", \"double precision,,,,\")");

        var model      = context.GetService<IDesignTimeModel>().Model;
        var operations = services.GetRequiredService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var sql        = string.Join(Environment.NewLine,
                                     services.GetRequiredService<IMigrationsSqlGenerator>().Generate(operations, model).Select(c => c.CommandText));

        var createType = Regex.Match(sql, @"CREATE TYPE float8range AS RANGE \(\s*subtype = double precision\s*\)", RegexOptions.IgnoreCase);
        Assert.IsTrue(createType.Success, $"The migration SQL creates no float8range type over double precision alone:{Environment.NewLine}{sql}");
        Assert.IsGreaterThan(createType.Index, sql.IndexOf("CREATE TABLE", StringComparison.Ordinal));
        StringAssert.Contains(sql, "\"Score\" float8range NOT NULL");
        StringAssert.Contains(sql, "\"Bands\" float8multirange NOT NULL");
    }

    [TestMethod]
    public void ContainsConstant_IsATypedExactLiteral()
    {
        var sql = Sql(db => db.Scorecards.Where(s => s.Score.Contains(2.9802322387695312e-08)));
        StringAssert.Contains(sql, "s.\"Score\" @> '2.9802322387695312e-08'::double precision");

        sql = Sql(db => db.Scorecards.Where(s => s.Score.Contains(1e23)));
        StringAssert.Contains(sql, "s.\"Score\" @> '9.999999999999999e+22'::double precision");
    }

    [TestMethod]
    public void RangeConstant_IsTheFloat8OutLiteral()
    {
        var sql = Sql(db => db.Scorecards.Where(s => s.Score == Float64Range.CreateFinite(1.5, 1e15)));
        StringAssert.Contains(sql, "s.\"Score\" = '[1.5,1e+15)'::float8range");
    }

    [TestMethod]
    public void SetContainsConstant_IsTheExactElement()
    {
        var sql = Sql(db => db.Scorecards.Where(s => s.Weights.Contains(2.9802322387695312e-08)));
        StringAssert.Contains(sql, "2.9802322387695312e-08");
        Assert.IsFalse(sql.Contains("2.980232238769531E-08", StringComparison.OrdinalIgnoreCase), sql);
    }

    [TestMethod]
    public void SetConstant_IsTheFloat8OutLiteral()
    {
        var weights = Float64Set.From(1e23, 2.9802322387695312e-08);
        var sql     = Sql(db => db.Scorecards.Where(s => s.Weights == EF.Constant(weights)));
        StringAssert.Contains(sql, "s.\"Weights\" = ARRAY['2.9802322387695312e-08','9.999999999999999e+22']::double precision[]");
    }

    [TestMethod]
    public void NonFiniteParameter_IsRefusedWhenBound()
    {
        var nan = double.NaN;
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

        public Float64Range Score { get; set; } = Float64Range.Empty;

        public RangeSet<Float64Range, double> Bands { get; set; } = RangeSet<Float64Range, double>.Empty;

        public Float64Set Weights { get; set; } = Float64Set.Empty;

        public Float64Set<TestRatio> WrappedWeights { get; set; } = Float64Set<TestRatio>.Empty;
    }

    private sealed class ScoreContext : DbContext
    {
        public DbSet<Scorecard> Scorecards => Set<Scorecard>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql("Host=localhost;Database=design_time", npgsql => npgsql.UseValueRanges());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.HasPostgresRange("float8range", "double precision");
    }
}

/// <summary>A double-backed wrapper element, shaped like a generated one.</summary>
public readonly record struct TestRatio : IFormattable, IParsable<TestRatio>, IComparable<TestRatio>
{
    private readonly double _value;

    public TestRatio(double value) => _value = value;

    public static TestRatio Parse(string s, IFormatProvider? provider)
        => new(double.Parse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestRatio result)
    {
        var ok = double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value);
        result = new TestRatio(value);
        return ok;
    }

    public int CompareTo(TestRatio other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? System.Globalization.CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, System.Globalization.CultureInfo.InvariantCulture);
}
