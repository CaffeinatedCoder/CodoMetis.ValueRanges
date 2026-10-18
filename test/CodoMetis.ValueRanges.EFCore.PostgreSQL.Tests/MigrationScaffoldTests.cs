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
/// <c>docs/efcore.md#timerange-and-the-custom-timerange-type</c>: <c>HasPostgresRange("timerange",
/// "time")</c> generates <c>CREATE TYPE timerange AS RANGE (SUBTYPE = time)</c> in your migrations,
/// and the <c>TimeRange</c> and <c>RangeSet&lt;TimeRange, TimeOnly&gt;</c> columns are declared over
/// that type and the multirange PostgreSQL creates beside it.
/// </summary>
/// <remarks>
/// The services are the ones <c>dotnet ef migrations add</c> builds: <see cref="DesignTimeServicesBuilder"/>
/// over the context, which adds the provider's design-time services from its
/// <c>DesignTimeProviderServices</c> attribute. The scaffolder writes the migration file a consumer
/// checks in, and the differ and SQL generator of the same provider turn the model into the statements
/// that migration applies. No database is involved; the integration suite's
/// <c>CustomRangeCatalogueTests</c> reads the catalogue after the same SQL has run.
/// </remarks>
[TestClass]
public sealed class MigrationScaffoldTests
{
    [TestMethod]
    public void ScaffoldedMigration_DeclaresTheTimerangeType()
    {
        using var context = new TimeRangeMigrationContext();
        var migration = DesignTimeServices(context).GetRequiredService<IMigrationsScaffolder>()
                                                    .ScaffoldMigration("Initial", "Consumer.Migrations");

        // Npgsql's range annotation: "subtype,canonical,subtype opclass,collation,subtype diff".
        StringAssert.Contains(migration.MigrationCode, ".Annotation(\"Npgsql:Range:timerange\", \"time,,,,\")");
        StringAssert.Contains(migration.MigrationCode, "type: \"timerange\"");
        StringAssert.Contains(migration.MigrationCode, "type: \"timemultirange\"");
    }

    [TestMethod]
    public void MigrationSql_CreatesTheTimerangeType_AheadOfTheTableThatUsesIt()
    {
        var sql = MigrationSql();

        var createType  = Regex.Match(sql, @"CREATE TYPE timerange AS RANGE \(\s*subtype = time\b", RegexOptions.IgnoreCase);
        var createTable = sql.IndexOf("CREATE TABLE", StringComparison.Ordinal);

        Assert.IsTrue(createType.Success, $"The migration SQL creates no timerange type:{Environment.NewLine}{sql}");
        Assert.IsTrue(createTable > createType.Index, $"The timerange type is created after the table that uses it:{Environment.NewLine}{sql}");
    }

    [TestMethod]
    public void MigrationSql_DeclaresTheColumnsOverTheCreatedTypeAndItsMultirange()
    {
        var sql = MigrationSql();

        // PostgreSQL names the multirange of a range type by replacing its trailing "range" with
        // "multirange", so CREATE TYPE timerange is what makes timemultirange exist.
        StringAssert.Contains(sql, "\"OpeningHours\" timerange NOT NULL");
        StringAssert.Contains(sql, "\"OpeningWindows\" timemultirange NOT NULL");
    }

    private static string MigrationSql()
    {
        using var context = new TimeRangeMigrationContext();
        var services   = DesignTimeServices(context);
        var model      = context.GetService<IDesignTimeModel>().Model;
        var operations = services.GetRequiredService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var commands   = services.GetRequiredService<IMigrationsSqlGenerator>().Generate(operations, model);

        return string.Join(Environment.NewLine, commands.Select(command => command.CommandText));
    }

    private static IServiceProvider DesignTimeServices(DbContext context)
    {
        var assembly = Assembly.GetExecutingAssembly();
        return new DesignTimeServicesBuilder(assembly, assembly, new OperationReporter(handler: null), [])
              .Build(context);
    }

    private sealed class OpeningSchedule
    {
        public int Id { get; set; }

        public TimeRange OpeningHours { get; set; } = TimeRange.Empty;

        public RangeSet<TimeRange, TimeOnly> OpeningWindows { get; set; } = RangeSet<TimeRange, TimeOnly>.Empty;
    }

    private sealed class TimeRangeMigrationContext : DbContext
    {
        public DbSet<OpeningSchedule> Schedules => Set<OpeningSchedule>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql("Host=localhost;Database=design_time", npgsql => npgsql.UseValueRanges());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.HasPostgresRange("timerange", "time");
    }
}
