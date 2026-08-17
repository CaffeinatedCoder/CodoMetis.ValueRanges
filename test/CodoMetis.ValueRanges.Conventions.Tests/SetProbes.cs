using CodoMetis.ValueRanges.Core;
using NodaTime;
using NodaTime.Calendars;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The single source of truth for which set types exist and which elements to probe them with.
/// </summary>
/// <remarks>
/// Shared by <see cref="ValueSetContractTests"/>, which checks the CLAUDE.md contract rules, and
/// <see cref="SmallModelSetOracleTests"/>, which checks every operation against set theory. Two
/// probe tables would drift, and a family that lost its probes in one of them would go
/// unexercised there while the other stayed green — the failure mode a discovery-driven suite has
/// to defend against hardest.
/// </remarks>
internal static class SetProbes
{
    internal static bool HasProbes(Type elementType) => Probes.ContainsKey(elementType);

    internal static TElement[] For<TElement>() => [.. Probes[typeof(TElement)].Cast<TElement>()];

    /// <summary>
    /// Probe elements per element type. Values that a normalizing set type would rewrite are
    /// deliberately included — an ISO-normalizing set is only exercised by a non-ISO probe.
    /// </summary>
    private static readonly Dictionary<Type, object[]> Probes = new()
    {
        // "Zebra" and "apple" are load-bearing: ordinal puts 'Z' (90) before 'a' (97), a culture
        // comparison puts apple first. Probes that both orders agree on would let the
        // CanonicalOrder rule pass while broken — they did, until a seeded defect showed it.
        [typeof(string)]         = ["beta", "Alpha", "gamma delta", "Zebra", "apple"],
        [typeof(Guid)]           = [Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), Guid.Empty],
        [typeof(short)]          = [(short)-7, (short)0, (short)32767],
        [typeof(int)]            = [-7, 0, 42],
        [typeof(long)]           = [-7L, 0L, 9_000_000_000L],
        [typeof(decimal)]        = [-1.5m, 0m, 12.75m],
        [typeof(DateOnly)]       = [new DateOnly(2024, 6, 15), new DateOnly(1970, 1, 1)],
        [typeof(TimeOnly)]       = [new TimeOnly(9, 30), new TimeOnly(23, 59, 59)],
        [typeof(DateTime)]       = [
            new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Unspecified),
            new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Local)
        ],
        [typeof(DateTimeOffset)] = [
            new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(2))
        ],

        // NodaTime: the calendar-bearing types normalize to ISO at construction, so a probe in
        // another calendar is the one that exercises NormalizeElement.
        [typeof(LocalDate)]      = [
            new LocalDate(2024, 6, 15),
            new LocalDate(1740, 10, 8, CalendarSystem.Coptic)
        ],
        [typeof(LocalDateTime)]  = [
            new LocalDateTime(2024, 6, 15, 10, 30),
            new LocalDateTime(1740, 10, 8, 10, 30, CalendarSystem.Coptic)
        ],
        [typeof(Instant)]        = [Instant.FromUtc(2024, 6, 15, 10, 30), Instant.FromUnixTimeSeconds(0)],
        [typeof(LocalTime)]      = [new LocalTime(9, 30), new LocalTime(23, 59, 59)],

        // YearMonth rejects non-ISO calendars outright rather than normalizing, so every probe
        // must be ISO — a non-ISO probe would assert the throw, not the round trip.
        [typeof(YearMonth)]      = [new YearMonth(2024, 6), new YearMonth(1970, 1)],

        // Validated wrapper elements, one per family arity.
        // Same ordinal-vs-culture split as the plain string probes, for the wrapper arity.
        [typeof(TextKey)]        = [
            TextKey.Parse("users.read", null),
            TextKey.Parse("  Admin  ", null),
            TextKey.Parse("Zebra", null),
            TextKey.Parse("apple", null)
        ],
        [typeof(TenantId)]       = [TenantId.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff", null)],
        [typeof(SmallCode)]      = [SmallCode.Parse("42", null), SmallCode.Parse("-7", null)],
        [typeof(LargeCode)]      = [LargeCode.Parse("9000000000", null), LargeCode.Parse("0", null)],
        [typeof(TinyCode)]       = [TinyCode.Parse("32767", null), TinyCode.Parse("-7", null)],

        // Two scales and a value that is not representable in binary floating point: a decimal
        // element that round-tripped through double would come back as 0.1000000000000000055.
        [typeof(Money)]          = [Money.Parse("12.50", null), Money.Parse("0.1", null),
                                    Money.Parse("-1.5", null)],

        // The sub-second probes are the ones that fail if a temporal arity formats its elements
        // with their default form instead of the round-trip one — that is the whole reason
        // those families pin a format.
        [typeof(BusinessDate)]   = [BusinessDate.Parse("2024-06-15", null),
                                    BusinessDate.Parse("1970-01-01", null)],
        [typeof(ShiftTime)]      = [ShiftTime.Parse("09:30:15.25", null),
                                    ShiftTime.Parse("23:59:59.9999999", null)],
        [typeof(AuditStamp)]     = [AuditStamp.Parse("2024-06-15T10:30:00.1234567", null),
                                    AuditStamp.Parse("1970-01-01T00:00:00", null)],
        [typeof(EventStamp)]     = [EventStamp.Parse("2024-06-15T10:30:00.1234567+02:00", null),
                                    EventStamp.Parse("2024-06-15T10:30:00.1234567Z", null)],

        [typeof(CalendarDay)]    = [CalendarDay.Parse("2024-06-15", null),
                                    CalendarDay.Parse("1970-01-01", null)],
        [typeof(WallClockStamp)] = [WallClockStamp.Parse("2024-06-15T10:30:00.123456789", null),
                                    WallClockStamp.Parse("1970-01-01T00:00:00", null)],
        [typeof(EventInstant)]   = [EventInstant.Parse("2024-06-15T10:30:00.123456789Z", null),
                                    EventInstant.Parse("1970-01-01T00:00:00Z", null)],
        [typeof(OpeningTime)]    = [OpeningTime.Parse("09:30:15.123456789", null),
                                    OpeningTime.Parse("23:59:59", null)],
        [typeof(BillingMonth)]   = [BillingMonth.Parse("2024-06", null), BillingMonth.Parse("1970-01", null)]
    };

    /// <summary>
    /// Every set type in the shipping assemblies, with the wrapper families closed over a
    /// representative validated element type.
    /// </summary>
    internal static IEnumerable<(Type Set, Type Element)> AllSetTypes()
    {
        Type[] assemblyMarkers = [typeof(StringSet), typeof(LocalDateSet)];

        foreach (var setType in assemblyMarkers
                               .Select(marker => marker.Assembly)
                               .Distinct()
                               .SelectMany(assembly => assembly.GetExportedTypes())
                               .Where(IsValueSetType)
                               .OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            if (!setType.IsGenericTypeDefinition)
            {
                yield return (setType, ElementTypeOf(setType));
                continue;
            }

            // A wrapper family: close it over the element type its arity is meant for.
            var closed = setType.MakeGenericType(WrapperElements.For(setType));
            yield return (closed, ElementTypeOf(closed));
        }
    }

    private static bool IsValueSetType(Type type) =>
        type is { IsClass: true, IsAbstract: false }
     && type.GetInterfaces().Any(@interface =>
            @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IValueSet<>));

    private static Type ElementTypeOf(Type setType) =>
        setType.GetInterfaces()
               .First(@interface => @interface.IsGenericType
                                 && @interface.GetGenericTypeDefinition() == typeof(IValueSet<>))
               .GetGenericArguments()[0];
}
