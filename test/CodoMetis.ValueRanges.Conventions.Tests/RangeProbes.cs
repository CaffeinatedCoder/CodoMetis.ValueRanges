using CodoMetis.ValueRanges.Core;
using NodaTime;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every range type in the shipping assemblies, with one value of each of the five shapes — the
/// inputs of the discovery-driven wire-form sweeps (JSON and <c>TypeConverter</c>).
/// </summary>
internal static class RangeProbes
{
    /// <summary>Two ordered values per element type, for the bounded shapes.</summary>
    internal static readonly Dictionary<Type, (object Low, object High)> Bounds = new()
    {
        // Negative lows and a fractional decimal: the minus sign (U+2212 in sv-SE) and the decimal
        // separator are where a culture leaks into a literal.
        [typeof(int)]            = (-5, 10),
        [typeof(long)]           = (-5L, 10L),
        [typeof(decimal)]        = (-1.5m, 9.25m),
        [typeof(double)]         = (-1.5d, 9.25d),
        [typeof(DateOnly)]       = (new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)),
        [typeof(TimeOnly)]       = (new TimeOnly(9, 30), new TimeOnly(17, 0)),
        [typeof(DateTime)]       = (new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Unspecified),
                                    new DateTime(2026, 6, 30, 17, 0, 0, DateTimeKind.Unspecified)),
        [typeof(DateTimeOffset)] = (new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.FromHours(1)),
                                    new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.FromHours(2))),
        [typeof(LocalDate)]      = (new LocalDate(2026, 1, 1), new LocalDate(2026, 6, 30)),
        [typeof(LocalDateTime)]  = (new LocalDateTime(2026, 1, 1, 9, 30), new LocalDateTime(2026, 6, 30, 17, 0)),
        [typeof(Instant)]        = (Instant.FromUtc(2026, 1, 1, 9, 30), Instant.FromUtc(2026, 6, 30, 17, 0)),
        [typeof(YearMonth)]      = (new YearMonth(2026, 1), new YearMonth(2026, 6))
    };

    internal static IEnumerable<(Type Range, Type Element)> AllRangeTypes()
    {
        Type[] assemblyMarkers = [typeof(Int32Range), typeof(LocalDateRange)];

        foreach (var type in assemblyMarkers
                            .Select(marker => marker.Assembly)
                            .Distinct()
                            .SelectMany(assembly => assembly.GetExportedTypes())
                            .OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            var factory = type.GetInterfaces().FirstOrDefault(
                @interface => @interface.IsGenericType
                           && @interface.GetGenericTypeDefinition() == typeof(IRangeFactory<,>));

            // The union declares the factory over itself; its variants inherit that interface
            // and are reached through the union below, so they are not listed separately.
            if (factory is not null && factory.GetGenericArguments()[0] == type)
                yield return (type, factory.GetGenericArguments()[1]);
        }
    }

    /// <summary>One value of each shape: empty, infinite, finite, unbounded end, unbounded start.</summary>
    internal static TRange[] Shapes<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (low, high) = ((T)Bounds[typeof(T)].Low, (T)Bounds[typeof(T)].High);

        return
        [
            TRange.Empty,
            TRange.Infinite,
            TRange.CreateFinite(low, high, startInclusive: true, endInclusive: false),
            TRange.CreateUnboundedEnd(low, startInclusive: true),
            TRange.CreateUnboundedStart(high, endInclusive: false)
        ];
    }
}
