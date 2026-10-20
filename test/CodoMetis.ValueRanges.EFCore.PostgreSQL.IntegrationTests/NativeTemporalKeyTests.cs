using System.Reflection;
using CodoMetis.ValueRanges.Conventions.Tests;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping;
using NodaTime;
using Testcontainers.PostgreSql;

// DesignTimeServicesBuilder and OperationReporter are EF-internal (EF1001): they are what
// `dotnet ef migrations add` builds, and ScaffoldedMigration_CarriesTheTemporalKey runs the same path.
#pragma warning disable EF1001

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>docs/efcore.md#reject-overlapping-rows-with-npgsqls-temporal-keys-postgresql-18</c>: Npgsql 11's own
/// <c>HasKey(…).WithoutOverlaps()</c> and <c>WithPeriod()</c> foreign keys over the columns this package
/// maps — a <see cref="DateRange"/>, a <see cref="RangeSet{TRange,T}"/> of them (<c>datemultirange</c>), a
/// <see cref="TimeRange"/> (the custom <c>timerange</c>) and a <see cref="YearMonthRange"/>.
/// </summary>
/// <remarks>
/// <para>
/// Npgsql accepts a temporal key only over a type mapping that implements
/// <see cref="INpgsqlRangeTypeMapping"/>; it reads the mapping, not the store type name. Without the
/// interface every column here fails model validation with "requires the last column to be a PostgreSQL
/// range type" (#150). <see cref="EveryRangeAndMultirangeMapping_PassesNpgsqlsTemporalKeyCheck"/> holds
/// that for every discovered type, so a new range type needs no edit here.
/// </para>
/// <para>
/// <c>WITHOUT OVERLAPS</c> needs PostgreSQL 18, so these run on an 18 server of their own, whatever
/// <see cref="PostgresServer.Image"/> says.
/// </para>
/// </remarks>
[TestClass]
public class NativeTemporalKeyTests
{
    private const string Postgres18 = "postgres:18-alpine";

    private static readonly Lazy<Task<PostgreSqlContainer>> Server = new(async () =>
    {
        var container = new PostgreSqlBuilder(Postgres18).Build();
        await container.StartAsync();
        return container;
    });

    [ClassCleanup]
    public static async Task StopServer()
    {
        if (!Server.IsValueCreated) return;

        try { await (await Server.Value).DisposeAsync(); }
        catch { /* a server that never started has nothing to stop */ }
    }

    private static DateRange Days(int month, int first, int last)
        => DateRange.CreateFinite(new DateOnly(2024, month, first), new DateOnly(2024, month, last));

    private static TimeRange Hours(int startHour, int startMinute, int endHour, int endMinute)
        => TimeRange.CreateFinite(new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute));

    [TestMethod]
    public Task DateRangeKey_RejectsAnOverlap_AndAcceptsAdjacentAndOtherKeys()
        => KeyHolds("daterange", Days(1, 1, 10), Days(1, 11, 20), Days(1, 10, 11));

    [TestMethod]
    public Task MultirangeKey_RejectsAnOverlap_AndAcceptsAdjacentAndOtherKeys()
        => KeyHolds("datemultirange",
                    RangeSet<DateRange, DateOnly>.From([Days(2, 1, 5), Days(2, 20, 25)]),
                    RangeSet<DateRange, DateOnly>.From([Days(2, 6, 10)]),
                    RangeSet<DateRange, DateOnly>.From([Days(2, 5, 6)]));

    [TestMethod]
    public Task TimeRangeKey_RejectsAnOverlap_AndAcceptsAdjacentAndOtherKeys()
        => KeyHolds("timerange", Hours(9, 0, 10, 0), Hours(10, 0, 11, 0), Hours(9, 30, 10, 30));

    [TestMethod]
    public Task YearMonthRangeKey_RejectsAnOverlap_AndAcceptsAdjacentAndOtherKeys()
        => KeyHolds("daterange",
                    YearMonthRange.CreateFinite(new YearMonth(2024, 1), new YearMonth(2024, 3)),
                    YearMonthRange.CreateFinite(new YearMonth(2024, 4), new YearMonth(2024, 6)),
                    YearMonthRange.CreateFinite(new YearMonth(2024, 3), new YearMonth(2024, 4)));

    /// <summary>
    /// The create script and the catalogue both carry <c>PRIMARY KEY (… WITHOUT OVERLAPS)</c> with
    /// <c>btree_gist</c>; an adjacent period and the same period under another key are accepted, and an
    /// overlap fails with <c>23P01</c> on the key.
    /// </summary>
    private static async Task KeyHolds<TPeriod>(string storeType, TPeriod first, TPeriod adjacent, TPeriod overlapping)
        where TPeriod : class
    {
        var connectionString = await FreshDatabase();

        await using (var context = new KeyContext<TPeriod>(connectionString))
        {
            var script = context.Database.GenerateCreateScript();
            StringAssert.Contains(script, $"\"Period\" {storeType} NOT NULL");
            StringAssert.Contains(script, KeyContext<TPeriod>.Clause, $"The create script renders no temporal key:{Environment.NewLine}{script}");
            StringAssert.Contains(script, "CREATE EXTENSION IF NOT EXISTS btree_gist");

            await context.Database.EnsureCreatedAsync();
        }

        Assert.AreEqual(KeyContext<TPeriod>.Clause, await Scalar(connectionString,
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'PK_Slots'"));

        await using (var context = new KeyContext<TPeriod>(connectionString))
        {
            context.Slots.AddRange(
                new Slot<TPeriod> { RoomId = 1, Period = first },
                new Slot<TPeriod> { RoomId = 1, Period = adjacent },
                new Slot<TPeriod> { RoomId = 2, Period = first });
            await context.SaveChangesAsync();
        }

        await using (var context = new KeyContext<TPeriod>(connectionString))
        {
            context.Slots.Add(new Slot<TPeriod> { RoomId = 1, Period = overlapping });
            var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => context.SaveChangesAsync());
            var postgres = error.InnerException as PostgresException;

            Assert.AreEqual(PostgresErrorCodes.ExclusionViolation, postgres?.SqlState, error.ToString());
            Assert.AreEqual("PK_Slots", postgres!.ConstraintName);
        }

        await using (var context = new KeyContext<TPeriod>(connectionString))
            Assert.AreEqual(3, await context.Slots.CountAsync());
    }

    /// <summary>
    /// <c>FOREIGN KEY (… PERIOD …)</c>: a booking whose period two adjacent availabilities cover together is
    /// accepted, and one that reaches past them fails with <c>23503</c>.
    /// </summary>
    [TestMethod]
    public async Task PeriodForeignKey_AcceptsACoveredPeriod_AndRejectsAnUncoveredOne()
    {
        var connectionString = await FreshDatabase();

        await using (var context = new PeriodContext(connectionString))
        {
            var script = context.Database.GenerateCreateScript();
            StringAssert.Contains(script, PeriodContext.Clause, $"The create script renders no PERIOD foreign key:{Environment.NewLine}{script}");
            await context.Database.EnsureCreatedAsync();

            context.Availabilities.AddRange(
                new Availability { RoomId = 1, Period = Days(1, 1, 10) },
                new Availability { RoomId = 1, Period = Days(1, 11, 20) });
            context.Bookings.Add(new Booking { Id = 1, RoomId = 1, Period = Days(1, 5, 15) });
            await context.SaveChangesAsync();
        }

        await using (var context = new PeriodContext(connectionString))
        {
            context.Bookings.Add(new Booking { Id = 2, RoomId = 1, Period = Days(1, 15, 25) });
            var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => context.SaveChangesAsync());

            Assert.AreEqual(PostgresErrorCodes.ForeignKeyViolation, (error.InnerException as PostgresException)?.SqlState, error.ToString());
        }
    }

    /// <summary>
    /// Npgsql validates against the PostgreSQL version it targets, not the server it reaches: without
    /// <c>SetPostgresVersion(18, 0)</c> the model fails before any SQL is sent.
    /// </summary>
    [TestMethod]
    public void WithoutOverlaps_NeedsTheProviderTargetingPostgres18()
    {
        using var context = new KeyContext<DateRange>("Host=localhost;Database=unused", targetPostgres18: false);

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => context.Database.GenerateCreateScript());
        StringAssert.Contains(error.Message, "SetPostgresVersion(18, 0)");
    }

    /// <summary>
    /// The migration a consumer checks in carries the key, and applying it creates the clause.
    /// </summary>
    [TestMethod]
    public void ScaffoldedMigration_CarriesTheTemporalKey()
    {
        using var context = new KeyContext<DateRange>("Host=localhost;Database=design_time");
        var assembly = Assembly.GetExecutingAssembly();
        var services = new DesignTimeServicesBuilder(assembly, assembly, new OperationReporter(handler: null), []).Build(context);

        var migration = services.GetRequiredService<IMigrationsScaffolder>().ScaffoldMigration("Initial", "Consumer.Migrations");

        StringAssert.Contains(migration.MigrationCode, "\"Npgsql:WithoutOverlaps\", true",
            $"The scaffolded migration does not carry WITHOUT OVERLAPS:{Environment.NewLine}{migration.MigrationCode}");
    }

    /// <summary>
    /// Every range type and its <see cref="RangeSet{TRange,T}"/>: the mapping the plugin resolves implements
    /// <see cref="INpgsqlRangeTypeMapping"/>, and a <c>WITHOUT OVERLAPS</c> key over it passes Npgsql's model
    /// validation. Discovered, so a new range type is covered with no edit here.
    /// </summary>
    [TestMethod]
    public void EveryRangeAndMultirangeMapping_PassesNpgsqlsTemporalKeyCheck()
    {
        var refused = new List<string>();

        foreach (var (range, element) in FuzzTypes.Ranges)
        {
            foreach (var period in new[] { range, typeof(RangeSet<,>).MakeGenericType(range, element) })
            {
                using var context = (DbContext)Activator.CreateInstance(
                    typeof(KeyContext<>).MakeGenericType(period), "Host=localhost;Database=unused", true)!;

                // Resolving a service builds the model, which is where Npgsql validates the key.
                try
                {
                    var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(period);
                    if (mapping is not INpgsqlRangeTypeMapping)
                        refused.Add($"{Name(period)}: {mapping?.GetType().Name ?? "no mapping"} does not implement INpgsqlRangeTypeMapping");
                    else
                        _ = context.Database.GenerateCreateScript();
                }
                catch (InvalidOperationException error) { refused.Add($"{Name(period)}: {error.Message}"); }
            }
        }

        RangeCensus.AssertReachedEveryUnion(FuzzTypes.Ranges.Select(type => type.Range), nameof(EveryRangeAndMultirangeMapping_PassesNpgsqlsTemporalKeyCheck));
        Assert.IsEmpty(refused, string.Join(Environment.NewLine, refused));
    }

    private static string Name(Type type)
        => type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>" : type.Name;

    private static async Task<string> FreshDatabase()
    {
        // Docker is the precondition every integration test shares: without it this skips (or, in CI,
        // fails) exactly like the rest of the suite.
        ContainerLifecycle.RequireServer();

        var server = await Server.Value;
        return new NpgsqlConnectionStringBuilder(server.GetConnectionString())
        {
            Database = $"native_temporal_{Guid.NewGuid():N}"
        }.ConnectionString;
    }

    private static async Task<string?> Scalar(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync() as string;
    }

    public sealed class Slot<TPeriod>
    {
        public int RoomId { get; set; }

        public TPeriod Period { get; set; } = default!;
    }

    /// <summary>One table of slots keyed by room and period, <c>WITHOUT OVERLAPS</c>.</summary>
    private sealed class KeyContext<TPeriod>(string connectionString, bool targetPostgres18 = true) : DbContext
    {
        public const string Clause = "PRIMARY KEY (\"RoomId\", \"Period\" WITHOUT OVERLAPS)";

        public DbSet<Slot<TPeriod>> Slots => Set<Slot<TPeriod>>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(connectionString, npgsql =>
            {
                // UseValueRangesNodaTime implies UseValueRanges; EnableUnmappedTypes puts the custom
                // timerange on the wire.
                npgsql.UseValueRangesNodaTime().ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes());
                if (targetPostgres18) npgsql.SetPostgresVersion(18, 0);
            });

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresRange("timerange", "time");
            modelBuilder.Entity<Slot<TPeriod>>(slot =>
            {
                slot.ToTable("Slots");
                slot.HasKey(s => new { s.RoomId, s.Period }).WithoutOverlaps();
            });
        }
    }

    public sealed class Availability
    {
        public int RoomId { get; set; }

        public DateRange Period { get; set; } = DateRange.Empty;
    }

    public sealed class Booking
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public DateRange Period { get; set; } = DateRange.Empty;

        public Availability? Availability { get; set; }
    }

    /// <summary>Availabilities keyed <c>WITHOUT OVERLAPS</c>, and bookings whose period they must cover.</summary>
    private sealed class PeriodContext(string connectionString) : DbContext
    {
        public const string Clause =
            "FOREIGN KEY (\"RoomId\", PERIOD \"Period\") REFERENCES \"Availabilities\" (\"RoomId\", PERIOD \"Period\")";

        public DbSet<Availability> Availabilities => Set<Availability>();

        public DbSet<Booking> Bookings => Set<Booking>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges().SetPostgresVersion(18, 0));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Availability>(availability => availability.HasKey(a => new { a.RoomId, a.Period }).WithoutOverlaps());
            modelBuilder.Entity<Booking>(booking =>
            {
                booking.Property(b => b.Id).ValueGeneratedNever();
                booking.HasOne(b => b.Availability)
                       .WithMany()
                       .HasForeignKey(b => new { b.RoomId, b.Period })
                       .WithPeriod();
            });
        }
    }
}
