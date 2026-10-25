using CodoMetis.ValueRanges;
using Microsoft.EntityFrameworkCore;

namespace Bookings;

public class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    // text[]: stored deduplicated and sorted, whatever order you add them in.
    public StringSet Features { get; set; } = StringSet.Empty;

    // daterange: the nights the room takes bookings. An open end means "until further notice".
    public DateRange Bookable { get; set; } = DateRange.Empty;
}

public class Booking
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public string Guest { get; set; } = "";

    // daterange: the nights of the stay. The exclusion constraint in the migration keeps two
    // bookings of one room from sharing a night.
    public DateRange Period { get; set; } = DateRange.Empty;

    // tstzrange: when the guest expects to arrive, if they said.
    public DateTimeOffsetRange? Arrival { get; set; }
}

public class BookingContext(DbContextOptions<BookingContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // The exclusion constraint compares RoomId with =, which GiST supports through btree_gist.
        model.HasPostgresExtension("btree_gist");
        model.Entity<Booking>().HasOne<Room>().WithMany().HasForeignKey(booking => booking.RoomId);
    }

    public static async Task SeedAsync(BookingContext db)
    {
        if (await db.Rooms.AnyAsync())
            return;

        var fromJanuary = new DateOnly(2026, 1, 1);
        db.Rooms.AddRange(
            new Room
            {
                Name = "Harbour",
                Features = ["sea-view", "balcony"],
                Bookable = DateRange.CreateUnboundedEnd(fromJanuary),
            },
            new Room
            {
                Name = "Garden",
                Features = ["step-free", "garden"],
                Bookable = DateRange.CreateFinite(new DateOnly(2026, 5, 1), new DateOnly(2026, 9, 30)),
            },
            new Room
            {
                Name = "Lighthouse",
                Features = ["sea-view", "bathtub", "balcony"],
                Bookable = DateRange.CreateUnboundedEnd(fromJanuary),
            });
        await db.SaveChangesAsync();
    }
}
