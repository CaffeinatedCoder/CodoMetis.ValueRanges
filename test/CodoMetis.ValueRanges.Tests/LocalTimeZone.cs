namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Sets this process's local time zone for one test, so a test of a zone rule runs in a zone that has
/// one whatever machine it runs on. The CI runner's zone is UTC, which skips and repeats no wall clock,
/// and a test that relied on <see cref="TimeZoneInfo.Local"/> passed there without testing anything
/// (#119).
/// </summary>
/// <remarks>
/// On Linux and macOS .NET reads the zone from the <c>TZ</c> variable, again after
/// <see cref="TimeZoneInfo.ClearCachedData"/>. The zone is process-wide, so a test that uses this must
/// be <c>[DoNotParallelize]</c>. Windows reads the zone from the registry and ignores <c>TZ</c>; there the
/// test is Inconclusive, and everywhere else a zone that did not take is a failure, never a skip.
/// </remarks>
internal sealed class LocalTimeZone : IDisposable
{
    /// <summary>Skips 02:00–03:00 on <see cref="Skipped"/>'s day and repeats it on <see cref="Repeated"/>'s.</summary>
    public const string Berlin = "Europe/Berlin";

    /// <summary>A wall clock Berlin skips: clocks go from 02:00 to 03:00 on 29 March 2026.</summary>
    public static readonly DateTime Skipped = new(2026, 3, 29, 2, 30, 0, DateTimeKind.Local);

    /// <summary>A wall clock Berlin repeats: clocks go from 03:00 back to 02:00 on 25 October 2026.</summary>
    public static readonly DateTime Repeated = new(2026, 10, 25, 2, 30, 0, DateTimeKind.Local);

    private readonly string? _original = Environment.GetEnvironmentVariable("TZ");

    private LocalTimeZone(string id)
    {
        Environment.SetEnvironmentVariable("TZ", id);
        TimeZoneInfo.ClearCachedData();
    }

    /// <summary>Berlin as the local zone until disposed, with its 2026 gap and overlap checked.</summary>
    public static LocalTimeZone UseBerlin()
    {
        var zone = new LocalTimeZone(Berlin);
        if (TimeZoneInfo.Local.IsInvalidTime(Skipped) && TimeZoneInfo.Local.IsAmbiguousTime(Repeated)) return zone;

        var local = TimeZoneInfo.Local.Id;
        zone.Dispose();
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive($"Windows ignores TZ; the local zone stayed {local}, which this test cannot change.");
        Assert.Fail($"Setting TZ={Berlin} left the local zone at {local} without the 2026 gap and overlap; the test would check nothing.");
        return zone;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TZ", _original);
        TimeZoneInfo.ClearCachedData();
    }
}
