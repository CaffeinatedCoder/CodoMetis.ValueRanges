using Bookings;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BookingContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Bookings"),
    npgsql => npgsql.UseValueRanges()));

var app = builder.Build();

// Fine for a sample with one instance. With several, apply migrations before you deploy instead.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BookingContext>();
    await db.Database.MigrateAsync();
    await BookingContext.SeedAsync(db);
}

app.MapBookingApi();
app.Run();
