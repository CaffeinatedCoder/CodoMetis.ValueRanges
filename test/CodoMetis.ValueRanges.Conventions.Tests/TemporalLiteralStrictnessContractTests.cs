using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using NodaTime;
using CodoMetis.ValueRanges.Oracles;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every range and value set over a temporal element refuses a non-ISO element, through every entry
/// point that reads one: <c>Parse</c>, <c>TryParse</c>, the span overload, JSON and the
/// <c>TypeConverter</c>.
/// </summary>
/// <remarks>
/// <para>
/// Until 9.0.0 the BCL temporal types — and the wrapper arities, through their element's own
/// <c>Parse</c> — read element text with the lenient BCL parser, which reads the invariant culture
/// month first: <c>[01.02.2027,05.03.2027)</c> was January 2 to May 2, silently. The probes below are
/// all text that parser accepted. A type added later that parses leniently fails here by name; the
/// grammar itself is pinned in <c>TemporalLiteralGrammarTests</c>.
/// </para>
/// <para>
/// The probes are the culture forms the lenient parser read, and — for the offset-bearing types — an
/// ISO timestamp without an offset and a bare date, which System.Text.Json's own converter read in the
/// parsing machine's offset. A closed temporal set reads its JSON elements through that converter's
/// replacement, the strict literal grammar, unless a converter is registered for the element type.
/// </para>
/// </remarks>
[TestClass]
public sealed class TemporalLiteralStrictnessContractTests
{
    /// <summary>Non-ISO elements per temporal primitive, each one a lenient parser read — into a different value.</summary>
    private static readonly Dictionary<Type, string[]> NonIso = new()
    {
        [typeof(DateOnly)]       = ["01.02.2027"],
        // +0530 and +5:30 are offsets PostgreSQL reads and the strict grammar refuses; .NET's zzz read
        // them for DateTimeOffset alone until #234.
        [typeof(DateTime)]       = ["01.02.2027 09:30:00", "2027-02-01", "2027-02-01T09:30:00+0530", "2027-02-01T09:30:00+5:30"],
        [typeof(DateTimeOffset)] = ["01.02.2027 09:30:00 +01:00", "2027-02-01T09:30:00", "2027-02-01",
                                    "2027-02-01T09:30:00+0530", "2027-02-01 09:30:00-0530", "2027-02-01T09:30:00+5:30"],
        [typeof(TimeOnly)]       = ["9:30 AM", "9:30"],
        [typeof(LocalDate)]      = ["01.02.2027"],
        [typeof(LocalDateTime)]  = ["01.02.2027 09:30:00", "2027-02-01", "2027-02-01T9:30", "2027-02-01T09:30:00+0530",
                                    "2027-02-01T09:30:00+5:30"],
        [typeof(Instant)]        = ["01.02.2027 09:30:00 +01:00", "2027-02-01T09:30:00", "2027-02-01",
                                    "2027-02-01T09:30", "2027-02-01T09:30:00+0530", "2027-02-01T09:30:00+5:30"],
        [typeof(LocalTime)]      = ["9:30 AM", "9:30"],
        [typeof(YearMonth)]      = ["02.2027"]
    };

    /// <summary>
    /// <c>HH:mm</c> elements, paired with the full form each must read as. Valid ISO 8601, never
    /// misread, and what a hand-written <c>[09:00,17:00)</c> uses — accepted uniformly wherever a
    /// time of day appears. An instant still needs its offset or <c>Z</c> (see <see cref="NonIso"/>).
    /// </summary>
    /// <remarks>
    /// Until 9.0.0 the BCL types accepted it and the NodaTime ones did not: <c>TimeRange.Parse("[09:00,17:00)")</c>
    /// parsed while <c>LocalTimeSet.Parse("{09:00}")</c> threw.
    /// </remarks>
    private static readonly Dictionary<Type, (string Short, string Full)[]> HoursMinutes = new()
    {
        [typeof(TimeOnly)]       = [("09:30", "09:30:00")],
        [typeof(DateTime)]       = [("2027-02-01T09:30", "2027-02-01T09:30:00"), ("2027-02-01 09:30", "2027-02-01T09:30:00")],
        [typeof(DateTimeOffset)] = [("2027-02-01T09:30+01:00", "2027-02-01T09:30:00+01:00"),
                                    ("2027-02-01 09:30+01", "2027-02-01T09:30:00+01:00"),
                                    ("2027-02-01T09:30Z", "2027-02-01T09:30:00Z")],
        [typeof(LocalTime)]      = [("09:30", "09:30:00")],
        [typeof(LocalDateTime)]  = [("2027-02-01T09:30", "2027-02-01T09:30:00"), ("2027-02-01 09:30", "2027-02-01T09:30:00")],
        [typeof(Instant)]        = [("2027-02-01T09:30+01:00", "2027-02-01T09:30:00+01:00"),
                                    ("2027-02-01 09:30+01", "2027-02-01T09:30:00+01:00"),
                                    ("2027-02-01T09:30Z", "2027-02-01T09:30:00Z")]
    };

    /// <summary>5 range types and 12 set types (6 closed, 6 wrapper arities) with a time of day, as of 9.0.0.</summary>
    private const int KnownTimeOfDayTypes = 17;

    [TestMethod]
    public void EveryTimeOfDayRangeAndSet_AcceptsHoursAndMinutes_OnEveryEntryPoint()
    {
        var checkedTypes = new List<string>();
        var failures     = new List<string>();

        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
        {
            if (!HoursMinutes.ContainsKey(elementType)) continue;

            failures.AddRange(Reflect.InvokeGeneric<IEnumerable<string>>(
                typeof(TemporalLiteralStrictnessContractTests), nameof(RangeHoursMinutesFailures), rangeType, elementType));
            checkedTypes.Add(rangeType.Name);
        }

        var primitives = SetProbes.AllSetTypes()
                                  .Where(pair => !pair.Set.IsGenericType)
                                  .ToDictionary(pair => pair.Set.Name, pair => pair.Element);

        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
        {
            var primitive = primitives.GetValueOrDefault(setType.Name.Split('`')[0]) ?? elementType;
            if (!HoursMinutes.ContainsKey(primitive)) continue;

            failures.AddRange(Reflect.InvokeGeneric<IEnumerable<string>>(
                typeof(TemporalLiteralStrictnessContractTests), nameof(SetHoursMinutesFailures), setType, elementType, primitive));
            checkedTypes.Add(setType.IsGenericType ? $"{setType.Name.Split('`')[0]}<{elementType.Name}>" : setType.Name);
        }

        Assert.IsTrue(
            checkedTypes.Count >= KnownTimeOfDayTypes,
            $"Only {checkedTypes.Count} time-of-day range and set types were checked, fewer than the "
          + $"{KnownTimeOfDayTypes} known to exist: {string.Join(", ", checkedTypes)}.");
        Assert.IsEmpty(failures, "HH:mm is not accepted uniformly:\n" + string.Join("\n", failures));
    }

    private static IEnumerable<string> RangeHoursMinutesFailures<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var failures = new List<string>();

        foreach (var (shortForm, fullForm) in HoursMinutes[typeof(T)])
        {
            var literal  = $"[\"{shortForm}\",\"{shortForm}\"]";
            var expected = StringOverloads.Parse<TRange>($"[\"{fullForm}\",\"{fullForm}\"]", CultureInfo.InvariantCulture);
            var label    = $"{typeof(TRange).Name} {literal}";

            Check(failures, label, "Parse", expected, () => StringOverloads.Parse<TRange>(literal, CultureInfo.InvariantCulture));
            Check(failures, label, "Parse(span)", expected, () => TRange.Parse(literal.AsSpan(), CultureInfo.InvariantCulture));
            Check(failures, label, "TryParse", expected,
                () => StringOverloads.TryParse<TRange>(literal, CultureInfo.InvariantCulture, out var tried) ? tried : throw new FormatException("TryParse returned false."));
            Check(failures, label, "JSON", expected, () => JsonSerializer.Deserialize<TRange>(JsonSerializer.Serialize(literal))!);
            Check(failures, label, "TypeConverter", expected,
                () => (TRange)TypeDescriptor.GetConverter(typeof(TRange)).ConvertFromInvariantString(literal)!);
            Check(failures, label, "RangeSet.Parse", RangeSet<TRange, T>.From([expected]),
                () => RangeSet<TRange, T>.Parse($"{{{literal}}}", CultureInfo.InvariantCulture));
        }

        return failures;
    }

    private static IEnumerable<string> SetHoursMinutesFailures<TSet, T, TPrimitive>()
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var failures = new List<string>();

        foreach (var (shortForm, fullForm) in HoursMinutes[typeof(TPrimitive)])
        {
            var literal  = $"{{\"{shortForm}\"}}";
            var expected = StringOverloads.Parse<TSet>($"{{\"{fullForm}\"}}", CultureInfo.InvariantCulture);
            var label    = $"{(typeof(TSet).IsGenericType ? $"{typeof(TSet).Name.Split('`')[0]}<{typeof(T).Name}>" : typeof(TSet).Name)} {literal}";

            Check(failures, label, "Parse", expected, () => StringOverloads.Parse<TSet>(literal, CultureInfo.InvariantCulture));
            Check(failures, label, "TryParse", expected,
                () => StringOverloads.TryParse<TSet>(literal, CultureInfo.InvariantCulture, out var tried) ? tried : throw new FormatException("TryParse returned false."));
            Check(failures, label, "JSON", expected, () => JsonSerializer.Deserialize<TSet>($"[{JsonSerializer.Serialize(shortForm)}]")!);
            Check(failures, label, "TypeConverter", expected,
                () => (TSet)TypeDescriptor.GetConverter(typeof(TSet)).ConvertFromInvariantString(literal)!);
        }

        return failures;
    }

    private static void Check<TValue>(List<string> failures, string label, string path, TValue expected, Func<TValue> read)
    {
        try
        {
            var actual = read();
            if (!Equals(actual, expected))
                failures.Add($"  {label}: {path} read {actual}, expected {expected}.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException or NotSupportedException or ArgumentException)
        {
            failures.Add($"  {label}: {path} refused it ({ex.GetType().Name}).");
        }
    }

    /// <summary>8 temporal range types and 18 temporal set types (9 closed, 9 wrapper arities), as of 9.0.0.</summary>
    private const int KnownTemporalTypes = 26;

    [TestMethod]
    public void EveryTemporalRangeAndSet_RefusesANonIsoElement_OnEveryEntryPoint()
    {
        var checkedTypes = new List<string>();

        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
        {
            if (!NonIso.ContainsKey(elementType)) continue;

            Reflect.InvokeGeneric<object?>(typeof(TemporalLiteralStrictnessContractTests), nameof(AssertRangeRefuses), rangeType, elementType);
            checkedTypes.Add(rangeType.Name);
        }

        var primitives = SetProbes.AllSetTypes()
                                  .Where(pair => !pair.Set.IsGenericType)
                                  .ToDictionary(pair => pair.Set.Name, pair => pair.Element);

        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
        {
            // A wrapper arity is backed by the primitive of its closed sibling: DateSet<T> by DateOnly.
            var primitive = primitives.GetValueOrDefault(setType.Name.Split('`')[0]) ?? elementType;
            if (!NonIso.ContainsKey(primitive)) continue;

            Reflect.InvokeGeneric<object?>(typeof(TemporalLiteralStrictnessContractTests), nameof(AssertSetRefuses), setType, elementType, primitive);
            checkedTypes.Add(setType.IsGenericType ? $"{setType.Name.Split('`')[0]}<{elementType.Name}>" : setType.Name);
        }

        Assert.IsTrue(
            checkedTypes.Count >= KnownTemporalTypes,
            $"Only {checkedTypes.Count} temporal range and set types were checked, fewer than the "
          + $"{KnownTemporalTypes} known to exist: {string.Join(", ", checkedTypes)}.");
    }

    private static object? AssertRangeRefuses<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        foreach (var element in NonIso[typeof(T)])
            RefusesElement<TRange, T>(element);
        return null;
    }

    private static void RefusesElement<TRange, T>(string element)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var literal = $"[\"{element}\",\"{element}\"]";
        var label   = $"{typeof(TRange).Name} {literal}";

        Assert.ThrowsExactly<FormatException>(() => StringOverloads.Parse<TRange>(literal, CultureInfo.InvariantCulture), $"{label}: Parse accepted it.");
        Assert.ThrowsExactly<FormatException>(() => TRange.Parse(literal.AsSpan(), CultureInfo.InvariantCulture), $"{label}: Parse(span) accepted it.");
        Assert.IsFalse(StringOverloads.TryParse<TRange>(literal, CultureInfo.InvariantCulture, out _), $"{label}: TryParse accepted it.");
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<TRange>(JsonSerializer.Serialize(literal)), $"{label}: JSON accepted it.");
        Assert.ThrowsExactly<FormatException>(
            () => TypeDescriptor.GetConverter(typeof(TRange)).ConvertFromInvariantString(literal), $"{label}: the TypeConverter accepted it.");

        var multirange = $"{{{literal}}}";
        Assert.ThrowsExactly<FormatException>(
            () => RangeSet<TRange, T>.Parse(multirange, CultureInfo.InvariantCulture), $"{label}: RangeSet.Parse accepted it.");
    }

    private static object? AssertSetRefuses<TSet, T, TPrimitive>()
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        foreach (var element in NonIso[typeof(TPrimitive)])
            SetRefusesElement<TSet, T>(element);
        return null;
    }

    private static void SetRefusesElement<TSet, T>(string element)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var literal = $"{{\"{element}\"}}";
        var label   = $"{(typeof(TSet).IsGenericType ? $"{typeof(TSet).Name.Split('`')[0]}<{typeof(T).Name}>" : typeof(TSet).Name)} {literal}";

        Assert.ThrowsExactly<FormatException>(() => StringOverloads.Parse<TSet>(literal, CultureInfo.InvariantCulture), $"{label}: Parse accepted it.");
        Assert.IsFalse(StringOverloads.TryParse<TSet>(literal, CultureInfo.InvariantCulture, out _), $"{label}: TryParse accepted it.");
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<TSet>($"[{JsonSerializer.Serialize(element)}]"), $"{label}: JSON accepted it.");
        Assert.ThrowsExactly<FormatException>(
            () => TypeDescriptor.GetConverter(typeof(TSet)).ConvertFromInvariantString(literal), $"{label}: the TypeConverter accepted it.");
    }
}
