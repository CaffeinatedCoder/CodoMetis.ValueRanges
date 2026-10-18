using System.Collections.Concurrent;
using EFCore.ComplexIndexes;
using EFCore.ComplexIndexes.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>EFCore.ComplexIndexes.PostgreSQL</c> over the columns this package maps: a <see cref="DateRange"/>,
/// a <see cref="RangeSet{TRange,T}"/> of them (<c>datemultirange</c>) and a <see cref="TimeRange"/> (the
/// custom <c>timerange</c>). Neither package references the other; that package accepts a period or an
/// overlap column by its store type name (anything ending in <c>range</c>), and every mapping here carries
/// one. The reference is test-only.
/// </summary>
/// <remarks>
/// <para>
/// Each test creates its schema with <c>EnsureCreated</c> in a database of its own, with
/// <c>UseNpgsqlComplexIndexes()</c> registering that package's differ for the runtime path. Without it,
/// <c>EnsureCreated</c> runs EF's stock differ and creates none of the constraints, which
/// <c>docs/efcore.md#reject-overlapping-rows-with-an-exclusion-constraint</c> states and
/// <see cref="ExclusionConstraint_IsSilentlyMissing_WithoutTheRuntimeDiffer"/> holds.
/// </para>
/// <para>
/// <c>UNIQUE … WITHOUT OVERLAPS</c> needs PostgreSQL 18, so the temporal constraints run on an 18 server;
/// exclusion constraints run on 16 and 18 (the shared 17 server holds the raw-SQL recipe in
/// <see cref="ExclusionConstraintTests"/>). Each server is a container of its own, started on first use.
/// </para>
/// </remarks>
[TestClass]
public class ComplexIndexesCrossTests
{
    private const string Postgres16 = "postgres:16-alpine";
    private const string Postgres18 = "postgres:18-alpine";

    private static readonly ConcurrentDictionary<string, Lazy<Task<PostgreSqlContainer>>> Servers = new();

    // The row every attempt is measured against, its adjacent neighbour in every column (accepted:
    // adjacent is not overlapping), and the same values for another room (accepted: another key).
    private static readonly Slot First = new()
    {
        Id     = 1,
        RoomId = 1,
        Period = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 10)),
        Days   = RangeSet<DateRange, DateOnly>.From([DateRange.CreateFinite(new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 5))]),
        Hours  = TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(10, 0)),
    };

    private static readonly Slot Adjacent = new()
    {
        Id     = 2,
        RoomId = 1,
        Period = DateRange.CreateFinite(new DateOnly(2024, 1, 11), new DateOnly(2024, 1, 20)),
        Days   = RangeSet<DateRange, DateOnly>.From([DateRange.CreateFinite(new DateOnly(2024, 2, 6), new DateOnly(2024, 2, 10))]),
        Hours  = TimeRange.CreateFinite(new TimeOnly(10, 0), new TimeOnly(11, 0)),
    };

    private static readonly Slot OtherRoom = First with { Id = 3, RoomId = 2 };

    /// <summary>Copies, so that parallel tests never track the same instance.</summary>
    private static Slot[] Rows() => [First with { }, Adjacent with { }, OtherRoom with { }];

    /// <summary>A row for room 1 that overlaps <see cref="First"/> in the named column only.</summary>
    private static Slot OverlappingIn(string column)
    {
        var far = new Slot
        {
            Id     = 10,
            RoomId = 1,
            Period = DateRange.CreateFinite(new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 10)),
            Days   = RangeSet<DateRange, DateOnly>.From([DateRange.CreateFinite(new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 5))]),
            Hours  = TimeRange.CreateFinite(new TimeOnly(15, 0), new TimeOnly(16, 0)),
        };

        return column switch
               {
                   nameof(Slot.Period) => far with { Period = DateRange.CreateFinite(new DateOnly(2024, 1, 10), new DateOnly(2024, 1, 11)) },
                   nameof(Slot.Days) => far with
                   {
                       Days = RangeSet<DateRange, DateOnly>.From([DateRange.CreateFinite(new DateOnly(2024, 2, 5), new DateOnly(2024, 2, 6))])
                   },
                   nameof(Slot.Hours) => far with { Hours = TimeRange.CreateFinite(new TimeOnly(9, 30), new TimeOnly(10, 30)) },
                   _                  => throw new ArgumentOutOfRangeException(nameof(column), column, null)
               };
    }

    private static readonly string[] Columns = [nameof(Slot.Period), nameof(Slot.Days), nameof(Slot.Hours)];

    [ClassCleanup]
    public static async Task StopServers()
    {
        foreach (var server in Servers.Values.Where(server => server.IsValueCreated))
        {
            try { await (await server.Value).DisposeAsync(); }
            catch { /* a server that never started has nothing to stop */ }
        }
    }

    [TestMethod]
    public async Task TemporalConstraint_RendersWithoutOverlaps_ForEachRangeColumn()
    {
        var connectionString = await FreshDatabase(Postgres18);

        await using var context = new TemporalContext(connectionString);
        var script = context.Database.GenerateCreateScript();
        await context.Database.EnsureCreatedAsync();

        foreach (var column in Columns)
        {
            var clause = $"UNIQUE (\"RoomId\", \"{column}\" WITHOUT OVERLAPS)";
            StringAssert.Contains(script, clause, $"The create script renders no temporal constraint over {column}:{Environment.NewLine}{script}");
            Assert.AreEqual(clause, await ConstraintDefinition(connectionString, TemporalContext.Name(column)));
        }

        Assert.IsTrue(await HasExtension(connectionString, "btree_gist"), "btree_gist was not created");
    }

    /// <summary>Before 18 the same schema does not create: <c>WITHOUT OVERLAPS</c> is a syntax error.</summary>
    [TestMethod]
    public async Task TemporalConstraint_IsASyntaxError_BeforePostgres18()
    {
        var connectionString = await FreshDatabase(Postgres16);

        await using var context = new TemporalContext(connectionString);
        var error = await Assert.ThrowsExactlyAsync<PostgresException>(() => context.Database.EnsureCreatedAsync());

        Assert.AreEqual(PostgresErrorCodes.SyntaxError, error.SqlState);
    }

    [TestMethod]
    public async Task TemporalConstraint_AcceptsAdjacentAndOtherKeys_AndRejectsAnOverlapInEachColumn()
    {
        var connectionString = await FreshDatabase(Postgres18);
        await RejectsOverlapsColumnByColumn(() => new TemporalContext(connectionString), TemporalContext.Name);
    }

    /// <summary>
    /// The temporal foreign key over a <see cref="DateRange"/>: <c>FOREIGN KEY (… PERIOD …)</c> on
    /// PostgreSQL 18, created by <c>EnsureCreated</c> with <c>btree_gist</c> injected. A dependent
    /// period covered by the principal's rows for the same key is saved, even when it takes two adjacent
    /// rows to cover it; one that runs past them, or names a key with no row, is rejected as
    /// <c>23503</c>.
    /// </summary>
    [TestMethod]
    public async Task TemporalForeignKey_AcceptsACoveredPeriod_AndRejectsOneThatIsNot()
    {
        var connectionString = await FreshDatabase(Postgres18);

        static DateRange Days(int from, int to) => DateRange.CreateFinite(new DateOnly(2026, 1, from), new DateOnly(2026, 1, to));

        await using (var context = new TemporalForeignKeyContext(connectionString))
        {
            var script = context.Database.GenerateCreateScript();
            StringAssert.Contains(script, "PERIOD \"Period\"", $"The create script renders no temporal foreign key:{Environment.NewLine}{script}");

            await context.Database.EnsureCreatedAsync();
            context.Availabilities.AddRange(new Availability { Id = 1, RoomId = 1, Period = Days(1, 10) },
                                            new Availability { Id = 2, RoomId = 1, Period = Days(11, 20) });
            await context.SaveChangesAsync();
        }

        Assert.AreEqual(
            "FOREIGN KEY (\"RoomId\", PERIOD \"Period\") REFERENCES \"Availabilities\"(\"RoomId\", PERIOD \"Period\")",
            await ConstraintDefinition(connectionString, TemporalForeignKeyContext.ForeignKeyName));
        Assert.IsTrue(await HasExtension(connectionString, "btree_gist"), "btree_gist was not created");

        await using (var covered = new TemporalForeignKeyContext(connectionString))
        {
            covered.Bookings.Add(new RoomBooking { Id = 1, RoomId = 1, Period = Days(5, 15) }); // covered by both rows together
            await covered.SaveChangesAsync();
        }

        foreach (var (label, booking) in new[]
                 {
                     ("past the last availability", new RoomBooking { Id = 2, RoomId = 1, Period = Days(15, 25) }),
                     ("a room with no availability", new RoomBooking { Id = 3, RoomId = 2, Period = Days(1, 2) })
                 })
        {
            await using var rejected = new TemporalForeignKeyContext(connectionString);
            rejected.Bookings.Add(booking);

            var error    = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => rejected.SaveChangesAsync(), $"A booking {label} was saved.");
            var postgres = error.InnerException as PostgresException;
            Assert.IsNotNull(postgres, $"Expected a PostgresException inside, got: {error.InnerException}");
            Assert.AreEqual(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState, label);
            Assert.AreEqual(TemporalForeignKeyContext.ForeignKeyName, postgres.ConstraintName, label);
        }
    }

    [TestMethod]
    [DataRow(Postgres16)]
    [DataRow(Postgres18)]
    public async Task ExclusionConstraint_AcceptsAdjacentAndOtherKeys_AndRejectsAnOverlapInEachColumn(string image)
    {
        var connectionString = await FreshDatabase(image);

        await using (var context = new ExclusionContext(connectionString))
        {
            var script = context.Database.GenerateCreateScript();
            foreach (var column in Columns)
                StringAssert.Contains(script, $"EXCLUDE USING gist (\"RoomId\" WITH =, \"{column}\" WITH &&)",
                                      $"The create script renders no exclusion constraint over {column}:{Environment.NewLine}{script}");
        }

        await RejectsOverlapsColumnByColumn(() => new ExclusionContext(connectionString), ExclusionContext.Name);
    }

    [TestMethod]
    public async Task ExclusionConstraint_IsSilentlyMissing_WithoutTheRuntimeDiffer()
    {
        var connectionString = await FreshDatabase(Postgres18);

        await using (var context = new UnwiredExclusionContext(connectionString))
        {
            await context.Database.EnsureCreatedAsync();
            context.Slots.AddRange(First with { }, OverlappingIn(nameof(Slot.Period)));
            await context.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand(
            "SELECT count(*) FROM pg_constraint WHERE conrelid = '\"Slots\"'::regclass AND contype = 'x'", connection);
        Assert.AreEqual(0L, await count.ExecuteScalarAsync());
    }

    [TestMethod]
    public async Task GistIndex_ServesEveryRangeOperator_AndAnOverlapsQueryRuns()
    {
        var connectionString = await FreshDatabase(Postgres18);

        await using (var context = new IndexContext(connectionString))
        {
            await context.Database.EnsureCreatedAsync();
            context.Slots.AddRange(Rows());
            await context.SaveChangesAsync();
        }

        var operands = new Dictionary<string, string>
        {
            [nameof(Slot.Period)] = "'[2024-01-03,2024-01-04)'::daterange",
            [nameof(Slot.Days)]   = "'{[2024-02-03,2024-02-04)}'::datemultirange",
            [nameof(Slot.Hours)]  = "'[09:30,09:45)'::timerange",
        };

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using (var noSeqScan = new NpgsqlCommand("SET enable_seqscan = off", connection))
                await noSeqScan.ExecuteNonQueryAsync();

            var unserved = new List<string>();
            foreach (var (column, operand) in operands)
            {
                var index = await IndexOn(connection, column);
                StringAssert.Contains(index.Definition, $"USING gist (\"{column}\")");

                foreach (var op in new[] { "&&", "@>", "<@", "<<", ">>", "&<", "&>", "-|-" })
                {
                    await using var explain = new NpgsqlCommand($"EXPLAIN SELECT \"Id\" FROM \"Slots\" WHERE \"{column}\" {op} {operand}", connection);
                    var plan = string.Join(Environment.NewLine, await ReadAll(explain));
                    if (!plan.Contains(index.Name, StringComparison.Ordinal)) unserved.Add($"{column} {op}: {plan}");
                }
            }

            Assert.AreEqual(0, unserved.Count, "The GiST index does not serve: " + string.Join(Environment.NewLine, unserved));
        }

        await using var read = new IndexContext(connectionString);
        var probe = DateRange.CreateFinite(new DateOnly(2024, 1, 10), new DateOnly(2024, 1, 11));
        var query = read.Slots.Where(slot => slot.Period.Overlaps(probe)).OrderBy(slot => slot.Id).Select(slot => slot.Id);

        StringAssert.Contains(query.ToQueryString(), "\"Period\" && @");
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, await query.ToListAsync());
    }

    private static async Task RejectsOverlapsColumnByColumn(Func<CrossContext> create, Func<string, string> constraintName)
    {
        await using (var setup = create())
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Slots.AddRange(Rows());
            await setup.SaveChangesAsync();
        }

        foreach (var column in Columns)
        {
            await using var rejected = create();
            rejected.Slots.Add(OverlappingIn(column));

            var error    = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => rejected.SaveChangesAsync(), $"An overlap in {column} was saved.");
            var postgres = error.InnerException as PostgresException;
            Assert.IsNotNull(postgres, $"Expected a PostgresException inside, got: {error.InnerException}");
            Assert.AreEqual(PostgresErrorCodes.ExclusionViolation, postgres.SqlState, column);
            Assert.AreEqual(constraintName(column), postgres.ConstraintName, column);
        }

        await using var read = create();
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, await read.Slots.OrderBy(slot => slot.Id).Select(slot => slot.Id).ToListAsync());
    }

    private static async Task<string> FreshDatabase(string image)
    {
        // Docker is the precondition every integration test shares: without it this skips (or, in CI,
        // fails) exactly like the rest of the suite.
        ContainerLifecycle.RequireServer();

        var server = await Servers.GetOrAdd(image, key => new Lazy<Task<PostgreSqlContainer>>(async () =>
        {
            var container = new PostgreSqlBuilder(key).Build();
            await container.StartAsync();
            return container;
        })).Value;

        return new NpgsqlConnectionStringBuilder(server.GetConnectionString())
        {
            Database = $"complex_indexes_{Guid.NewGuid():N}"
        }.ConnectionString;
    }

    private static async Task<string> ConstraintDefinition(string connectionString, string name)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = $1", connection)
        {
            Parameters = { new NpgsqlParameter { Value = name } }
        };
        return await command.ExecuteScalarAsync() as string ?? $"(no constraint named {name})";
    }

    private static async Task<bool> HasExtension(string connectionString, string name)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_extension WHERE extname = $1", connection)
        {
            Parameters = { new NpgsqlParameter { Value = name } }
        };
        return (long)(await command.ExecuteScalarAsync())! == 1;
    }

    private static async Task<(string Name, string Definition)> IndexOn(NpgsqlConnection connection, string column)
    {
        await using var command = new NpgsqlCommand(
            "SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'Slots' AND indexdef LIKE '%USING gist%' AND indexdef LIKE $1",
            connection)
        {
            Parameters = { new NpgsqlParameter { Value = $"%\"{column}\"%" } }
        };
        await using var reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync(), $"There is no GiST index over {column}.");
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<List<string>> ReadAll(NpgsqlCommand command)
    {
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
        return lines;
    }

    private sealed record Slot
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public DateRange Period { get; set; } = DateRange.Empty;

        public RangeSet<DateRange, DateOnly> Days { get; set; } = RangeSet<DateRange, DateOnly>.Empty;

        public TimeRange Hours { get; set; } = TimeRange.Empty;
    }

    /// <summary>One table of <see cref="Slot"/>s; each subclass declares its own constraints, so EF caches one model per subclass.</summary>
    private abstract class CrossContext(string connectionString, bool wired = true) : DbContext
    {
        public DbSet<Slot> Slots => Set<Slot>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges()
                                                                       .ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));

            // ComplexIndexes' differ for the runtime path, so EnsureCreated sees its declarations.
            if (wired) optionsBuilder.UseNpgsqlComplexIndexes();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresRange("timerange", "time");
            modelBuilder.Entity<Slot>(slot =>
            {
                slot.Property(s => s.Id).ValueGeneratedNever();
                Configure(slot);
            });
        }

        protected abstract void Configure(EntityTypeBuilder<Slot> slot);
    }

    private sealed class TemporalContext(string connectionString) : CrossContext(connectionString)
    {
        public static string Name(string column) => $"uq_slots_room_{column.ToLowerInvariant()}";

        protected override void Configure(EntityTypeBuilder<Slot> slot)
            => slot.HasTemporalConstraint(s => s.RoomId, s => s.Period, Name(nameof(Slot.Period)))
                   .HasTemporalConstraint(s => s.RoomId, s => s.Days, Name(nameof(Slot.Days)))
                   .HasTemporalConstraint(s => s.RoomId, s => s.Hours, Name(nameof(Slot.Hours)));
    }

    private sealed class ExclusionContext(string connectionString) : CrossContext(connectionString)
    {
        public static string Name(string column) => $"ex_slots_room_{column.ToLowerInvariant()}";

        // Both overloads: the builder recipe docs/efcore.md shows, and the scheduling shorthand.
        protected override void Configure(EntityTypeBuilder<Slot> slot)
            => slot.HasExclusionConstraint(ex => ex.WithEquality(s => s.RoomId).WithOverlaps(s => s.Period).HasName(Name(nameof(Slot.Period))))
                   .HasExclusionConstraint(ex => ex.WithEquality(s => s.RoomId).WithOverlaps(s => s.Days).HasName(Name(nameof(Slot.Days))))
                   .HasExclusionConstraint(s => s.RoomId, s => s.Hours, name: Name(nameof(Slot.Hours)));
    }

    private sealed class UnwiredExclusionContext(string connectionString) : CrossContext(connectionString, wired: false)
    {
        protected override void Configure(EntityTypeBuilder<Slot> slot)
            => slot.HasExclusionConstraint(ex => ex.WithEquality(s => s.RoomId).WithOverlaps(s => s.Period).HasName("ex_slots_unwired"));
    }

    private sealed class IndexContext(string connectionString) : CrossContext(connectionString)
    {
        // The doc's own recipe (Npgsql's HasMethod) on Period, ComplexIndexes' UseGist on the other two.
        protected override void Configure(EntityTypeBuilder<Slot> slot)
        {
            slot.HasIndex(s => s.Period).HasMethod("gist");
            slot.HasComplexIndex(s => s.Days, index => index.UseGist());
            slot.HasComplexIndex(s => s.Hours, index => index.UseGist());
        }
    }

    private sealed class Availability
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public DateRange Period { get; set; } = DateRange.Empty;
    }

    private sealed class RoomBooking
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public DateRange Period { get; set; } = DateRange.Empty;
    }

    /// <summary>A principal with a temporal key, and a dependent whose period must lie inside the principal's.</summary>
    private sealed class TemporalForeignKeyContext(string connectionString) : DbContext
    {
        public const string ForeignKeyName = "fk_bookings_room_period";

        public DbSet<Availability> Availabilities => Set<Availability>();

        public DbSet<RoomBooking> Bookings => Set<RoomBooking>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges())
                             .UseNpgsqlComplexIndexes();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Availability>(availability =>
            {
                availability.Property(a => a.Id).ValueGeneratedNever();
                // PostgreSQL requires the referenced columns to carry a WITHOUT OVERLAPS key.
                availability.HasTemporalConstraint(a => a.RoomId, a => a.Period, "uq_availabilities_room_period");
            });

            modelBuilder.Entity<RoomBooking>(booking =>
            {
                booking.ToTable("Bookings");
                booking.Property(b => b.Id).ValueGeneratedNever();
                booking.HasTemporalForeignKey<RoomBooking, Availability>(b => b.RoomId, b => b.Period, a => a.RoomId, a => a.Period, ForeignKeyName);
            });
        }
    }
}
