using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>docs/efcore.md#indexing-a-range-column-and-preventing-overlaps</c>, executed: an exclusion
/// constraint over a mapped <see cref="DateRange"/> column rejects an overlapping row for the same
/// room with <c>23P01</c> surfacing as a <see cref="DbUpdateException"/> that wraps a
/// <see cref="PostgresException"/> naming the constraint, and accepts the adjacent pair the memory
/// side calls adjacent and not overlapping. The constraint is added with explicit SQL — the
/// document's alternative to a migration — so the test depends on no constraint package.
/// </summary>
[TestClass]
public class ExclusionConstraintTests
{
    private const string Constraint = "ex_room_booking_room_period";

    private static readonly DateRange First  = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 10));
    private static readonly DateRange Second = DateRange.CreateFinite(new DateOnly(2024, 1, 11), new DateOnly(2024, 1, 20));

    [TestMethod]
    public void AdjacentDays_AreAdjacentAndNotOverlapping_InMemory()
    {
        Assert.IsFalse(First.Overlaps(Second));
        Assert.IsTrue(First.IsAdjacentTo(Second));
    }

    [TestMethod]
    public async Task ExclusionConstraint_AcceptsTheAdjacentPair_AndRejectsAnOverlapWith23P01()
    {
        ContainerLifecycle.RequireDatabase();

        await using (var setup = new ExclusionDbContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                $"""
                 CREATE EXTENSION IF NOT EXISTS btree_gist;
                 DROP TABLE IF EXISTS "RoomBookings";
                 CREATE TABLE "RoomBookings" ("Id" integer PRIMARY KEY, "RoomId" integer NOT NULL, "Period" daterange NOT NULL);
                 ALTER TABLE "RoomBookings" ADD CONSTRAINT "{Constraint}"
                   EXCLUDE USING gist ("RoomId" WITH =, "Period" WITH &&);
                 """);
        }

        await using (var accepted = new ExclusionDbContext())
        {
            accepted.RoomBookings.AddRange(
                new RoomBooking { Id = 1, RoomId = 1, Period = First },
                new RoomBooking { Id = 2, RoomId = 1, Period = Second },
                new RoomBooking { Id = 3, RoomId = 2, Period = First });
            await accepted.SaveChangesAsync();
        }

        await using var rejected = new ExclusionDbContext();
        rejected.RoomBookings.Add(new RoomBooking
        {
            Id     = 4,
            RoomId = 1,
            Period = DateRange.CreateFinite(new DateOnly(2024, 1, 10), new DateOnly(2024, 1, 11)),
        });

        var error    = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => rejected.SaveChangesAsync());
        var postgres = error.InnerException as PostgresException;
        Assert.IsNotNull(postgres, $"Expected a PostgresException inside, got: {error.InnerException}");
        Assert.AreEqual(PostgresErrorCodes.ExclusionViolation, postgres.SqlState);
        Assert.AreEqual(Constraint, postgres.ConstraintName);

        await using var read = new ExclusionDbContext();
        Assert.AreEqual(3, await read.RoomBookings.CountAsync());
    }

    private sealed class RoomBooking
    {
        public int Id { get; set; }

        public int RoomId { get; set; }

        public DateRange Period { get; set; } = DateRange.Empty;
    }

    private sealed class ExclusionDbContext : DbContext
    {
        public DbSet<RoomBooking> RoomBookings => Set<RoomBooking>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(ContainerLifecycle.ConnectionString!, npgsql => npgsql.UseValueRanges());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<RoomBooking>().Property(b => b.Id).ValueGeneratedNever();
    }
}
