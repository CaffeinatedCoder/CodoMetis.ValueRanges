using System.Diagnostics;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using NodaTime;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The shape matrix the range sweeps enumerate: every shape, every bound position on a small grid and
/// every inclusivity, embedded into each discovered element type in order. Shared by
/// <see cref="DomainParityOracleTests"/>, which checks each pair against set theory, and
/// <see cref="RangeSetLiftParityTests"/>, which checks each pair against its one-element multiranges.
/// </summary>
internal static class RangeSpecs
{
    internal enum Shape { Empty, Infinity, Finite, UnboundedStart, UnboundedEnd }

    internal sealed record Spec(Shape Shape, int Start, int End, bool StartInclusive, bool EndInclusive);

    /// <summary>The grid positions that stand for the element domain's minimum and maximum.</summary>
    internal const int DomainMin = int.MinValue, DomainMax = int.MaxValue;

    private static readonly DateTime      s_dateTimeOrigin = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly LocalDateTime s_localOrigin    = new(2026, 1, 1, 0, 0);
    private static readonly Instant       s_instantOrigin  = Instant.FromUtc(2026, 1, 1, 0, 0);

    /// <summary>Grid index to element, order-preserving; a discrete type's step is one grid point.</summary>
    internal static readonly Dictionary<Type, Delegate> Embeddings = new()
    {
        [typeof(int)]            = (Func<int, int>)(k => k),
        [typeof(long)]           = (Func<int, long>)(k => k),
        [typeof(decimal)]        = (Func<int, decimal>)(k => k * 0.5m),
        [typeof(double)]         = (Func<int, double>)(k => k * 0.5d),
        [typeof(DateOnly)]       = (Func<int, DateOnly>)(k => new DateOnly(2026, 1, 1).AddDays(k)),
        [typeof(TimeOnly)]       = (Func<int, TimeOnly>)(k => new TimeOnly(9, 0).AddMinutes(30 * k)),
        [typeof(DateTime)]       = (Func<int, DateTime>)(k => s_dateTimeOrigin.AddMinutes(30 * k)),
        [typeof(DateTimeOffset)] = (Func<int, DateTimeOffset>)(k => new DateTimeOffset(s_dateTimeOrigin.AddMinutes(30 * k), TimeSpan.FromHours(1))),
        [typeof(LocalDate)]      = (Func<int, LocalDate>)(k => new LocalDate(2026, 1, 1).PlusDays(k)),
        [typeof(LocalDateTime)]  = (Func<int, LocalDateTime>)(k => s_localOrigin.PlusMinutes(30 * k)),
        [typeof(Instant)]        = (Func<int, Instant>)(k => s_instantOrigin.Plus(Duration.FromMinutes(30 * k))),
        [typeof(YearMonth)]      = (Func<int, YearMonth>)(k => new YearMonth(2026, 1).PlusMonths(k))
    };

    /// <summary>The least and greatest value of each element domain, which no grid position reaches.</summary>
    internal static readonly Dictionary<Type, (object Min, object Max)> Extremes = new()
    {
        [typeof(int)]            = (int.MinValue, int.MaxValue),
        [typeof(long)]           = (long.MinValue, long.MaxValue),
        [typeof(decimal)]        = (decimal.MinValue, decimal.MaxValue),
        [typeof(double)]         = (double.MinValue, double.MaxValue),
        [typeof(DateOnly)]       = (DateOnly.MinValue, DateOnly.MaxValue),
        [typeof(TimeOnly)]       = (TimeOnly.MinValue, TimeOnly.MaxValue),
        [typeof(DateTime)]       = (DateTime.MinValue, DateTime.MaxValue),
        [typeof(DateTimeOffset)] = (DateTimeOffset.MinValue, DateTimeOffset.MaxValue),
        [typeof(LocalDate)]      = (LocalDate.MinIsoValue, LocalDate.MaxIsoValue),
        [typeof(LocalDateTime)]  = (LocalDateTime.MinIsoValue, LocalDateTime.MaxIsoValue),
        [typeof(Instant)]        = (Instant.MinValue, Instant.MaxValue),
        [typeof(YearMonth)]      = (new YearMonth(-9998, 1), new YearMonth(9999, 12))
    };

    /// <summary>
    /// The embedding of <typeparamref name="T"/>, with <see cref="DomainMin"/> and <see cref="DomainMax"/>
    /// mapped to the domain's extremes.
    /// </summary>
    internal static Func<int, T> ValueOf<T>()
    {
        var embed = (Func<int, T>)Embeddings[typeof(T)];
        var (min, max) = Extremes[typeof(T)];

        return k => k switch
        {
            DomainMin => (T)min,
            DomainMax => (T)max,
            _         => embed(k)
        };
    }

    /// <summary>Both unbounded shapes at every bound, and every finite range between two bounds, with every inclusivity.</summary>
    internal static IEnumerable<Spec> EnumerateSpecs(int[] bounds)
    {
        yield return new(Shape.Empty, 0, 0, false, false);
        yield return new(Shape.Infinity, 0, 0, false, false);

        foreach (var bound in bounds)
        foreach (var inclusive in (bool[]) [true, false])
        {
            yield return new(Shape.UnboundedStart, 0, bound, false, inclusive);
            yield return new(Shape.UnboundedEnd, bound, 0, inclusive, false);
        }

        foreach (var start in bounds)
        foreach (var end in bounds.Where(end => end >= start))
        foreach (var startInclusive in (bool[]) [true, false])
        foreach (var endInclusive in (bool[]) [true, false])
            yield return new(Shape.Finite, start, end, startInclusive, endInclusive);
    }

    internal static TRange Build<TRange, T>(Spec spec, Func<int, T> valueOf)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T> =>
        spec.Shape switch
        {
            Shape.Empty          => TRange.Empty,
            Shape.Infinity       => TRange.Infinite,
            Shape.Finite         => TRange.CreateFinite(valueOf(spec.Start), valueOf(spec.End), spec.StartInclusive, spec.EndInclusive),
            Shape.UnboundedStart => TRange.CreateUnboundedStart(valueOf(spec.End), spec.EndInclusive),
            Shape.UnboundedEnd   => TRange.CreateUnboundedEnd(valueOf(spec.Start), spec.StartInclusive),
            _                    => throw new UnreachableException()
        };

    /// <summary>The specification as a literal, with each bound written by the type's own <c>FormatValue</c>.</summary>
    internal static string Describe<TRange, T>(Spec spec, Func<int, T> valueOf)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        string Text(int k) => TRange.FormatValue(valueOf(k), null, CultureInfo.InvariantCulture);

        return spec.Shape switch
        {
            Shape.Empty          => "empty",
            Shape.Infinity       => "(,)",
            Shape.Finite         => $"{(spec.StartInclusive ? '[' : '(')}{Text(spec.Start)},{Text(spec.End)}{(spec.EndInclusive ? ']' : ')')}",
            Shape.UnboundedStart => $"(,{Text(spec.End)}{(spec.EndInclusive ? ']' : ')')}",
            Shape.UnboundedEnd   => $"{(spec.StartInclusive ? '[' : '(')}{Text(spec.Start)},)",
            _                    => throw new UnreachableException()
        };
    }
}
