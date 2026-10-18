using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using NodaTime;
using CodoMetis.ValueRanges.Oracles;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// A wrapper arity reads element text with its family's grammar, not its element's: for every
/// discovered wrapper family, closed over a deliberately lenient element, every probe text is
/// accepted or refused exactly as the primitive sibling accepts or refuses it, and an accepted one
/// reads the same value — through <c>Parse</c>, <c>TryParse</c>, the span overload, the
/// <c>TypeConverter</c> and JSON.
/// </summary>
/// <remarks>
/// <para>
/// Until 9.0.0 the numeric arities handed the raw element text to <c>TElement.Parse</c>. A generated
/// wrapper — <c>new(decimal.Parse(s, provider))</c>, which is <c>NumberStyles.Number</c> — accepts
/// group separators, so <c>DecimalSet.Parse("{\"1,5\"}")</c> threw while
/// <c>DecimalSet&lt;Price&gt;.Parse("{\"1,5\"}")</c> was <c>{15}</c>, silently, and the same through
/// configuration binding and a JSON string. In the other direction <c>{1e3}</c> — PostgreSQL numeric
/// grammar — was refused by the wrapper and read by the primitive. The temporal arities already
/// parsed with the family grammar first and handed the element its round-trip text; the numeric ones
/// now do the same.
/// </para>
/// <para>
/// Each numeric family is swept twice: over a deliberately lenient element (<c>NumberStyles.Any</c>),
/// which would widen the grammar, and over the representative one (<c>NumberStyles.Number</c> for
/// <see cref="Money"/>, which has no exponent), which would narrow it. An element that parses exactly
/// like the primitive would make the two agree by construction, and the sweep would pass with the
/// defect in place.
/// </para>
/// </remarks>
[TestClass]
public sealed class WrapperGrammarContractTests
{
    /// <summary>
    /// Probe element texts per primitive. Each one is placed in a quoted array literal (so leading
    /// and trailing whitespace reaches the element parser) and in a JSON string.
    /// </summary>
    private static readonly Dictionary<Type, string[]> TextProbes = new()
    {
        [typeof(decimal)] = [
            "1.5", "1.50", "-1.5", "+1.5", " 1.5", "1.5 ", ".5", "1e3", "1.5E-1", "12.500e1",
            "1,5", "1,000", "1,000.5", "1 000", "(1)", "1-", "¤1", "0x10", "NaN", "Infinity", ""
        ],
        [typeof(int)]   = Integers("2147483648"),
        [typeof(short)] = Integers("32768"),
        [typeof(long)]  = Integers("9223372036854775808"),
        [typeof(Guid)]  = [
            "6f9619ff-8b86-d011-b42d-00c04fc964ff", "6f9619ff8b86d011b42d00c04fc964ff",
            "{6f9619ff-8b86-d011-b42d-00c04fc964ff}", "(6f9619ff-8b86-d011-b42d-00c04fc964ff)",
            " 6f9619ff-8b86-d011-b42d-00c04fc964ff ", "6f9619ff-8b86-d011-b42d", ""
        ],
        [typeof(DateOnly)] = ["2024-06-15", "infinity", "01.02.2027", "06/15/2024", "2024-06-15T00:00:00", ""],
        [typeof(TimeOnly)] = ["09:30", "09:30:15.25", "23:59:59.9999999", "9:30 AM", "9:30", ""],
        [typeof(DateTime)] = [
            "2024-06-15T10:30:00", "2024-06-15 10:30:00.1234567", "2024-06-15T10:30:00Z", "-infinity",
            "01.02.2027 09:30:00", "2027-02-01", ""
        ],
        [typeof(DateTimeOffset)] = [
            "2024-06-15T10:30:00+02:00", "2024-06-15 10:30:00.123456+02", "2024-06-15T10:30:00Z",
            "2027-02-01T09:30:00", "01.02.2027 09:30:00 +01:00", ""
        ],
        [typeof(LocalDate)]     = ["2024-06-15", "01.02.2027", ""],
        [typeof(LocalDateTime)] = ["2024-06-15T10:30:00.123456789", "2024-06-15 10:30:00", "2027-02-01", ""],
        [typeof(Instant)]       = ["2024-06-15T10:30:00Z", "2024-06-15 10:30:00+02", "2027-02-01T09:30:00", ""],
        [typeof(LocalTime)]     = ["09:30:15.123456789", "23:59:59", "9:30 AM", ""],
        [typeof(YearMonth)]     = ["2024-06", "02.2027", ""]
    };

    /// <summary>
    /// Probe JSON number tokens per numeric primitive, read by the wrapper's element converter and
    /// compared with what System.Text.Json reads for the primitive sibling.
    /// </summary>
    private static readonly Dictionary<Type, string[]> NumberProbes = new()
    {
        [typeof(decimal)] = ["1.5", "1.50", "-0.1", "1e3", "1.5e1", "12.50E-1"],
        [typeof(int)]     = ["42", "-7", "1e3", "42.0", "2147483648"],
        [typeof(short)]   = ["42", "-7", "1e3", "42.0", "32768"],
        [typeof(long)]    = ["42", "-7", "1e3", "42.0", "9223372036854775808"]
    };

    /// <summary>
    /// Families with no grammar to widen: a string element's text is its value, and a wrapper that
    /// trims or validates it (<see cref="TextKey"/> does) is exercising its own validation.
    /// </summary>
    private static readonly HashSet<Type> NoGrammar = [typeof(string)];

    /// <summary>15 wrapper families as of 9.0.0, less the string one, plus a lenient element for each of the four numeric ones.</summary>
    private const int KnownClosures = 18;

    private static string[] Integers(string overflow) =>
        ["42", "-7", "+42", " 42", "42 ", "1,000", "1e3", "42.0", "(1)", "1-", "¤1", "0x2A", "4 2", overflow, ""];

    [TestMethod]
    public void EveryWrapperArity_ReadsElementsWithItsFamilyGrammar()
    {
        var swept    = new List<string>();
        var failures = new List<string>();

        foreach (var (closed, _) in SetProbes.AllSetTypes().Where(pair => pair.Set.IsGenericType))
        {
            var family  = closed.GetGenericTypeDefinition();
            var sibling = family.Assembly.GetType(family.FullName!.Split('`')[0])
                       ?? throw new InvalidOperationException($"{family.Name} has no primitive sibling.");
            var primitive = ElementTypeOf(sibling);

            if (NoGrammar.Contains(primitive)) continue;

            // Both directions: the lenient element would widen the grammar, the representative one
            // (Money is NumberStyles.Number, which has no exponent) would narrow it.
            foreach (var element in new[] { LenientElementFor(primitive), WrapperElements.For(family) }.OfType<Type>())
            {
                var wrapper = family.MakeGenericType(element);

                failures.AddRange(Reflect.InvokeGeneric<IEnumerable<string>>(
                    typeof(WrapperGrammarContractTests), nameof(Disagreements), wrapper, element, sibling, primitive));
                swept.Add($"{family.Name.Split('`')[0]}<{element.Name}>");
            }
        }

        Assert.IsTrue(swept.Count >= KnownClosures,
            $"Only {swept.Count} wrapper closures were swept, fewer than the {KnownClosures} expected: "
          + string.Join(", ", swept));
        Assert.IsEmpty(failures,
            "A wrapper arity reads element text differently from its primitive sibling:\n" + string.Join("\n", failures));
    }

    private static Type? LenientElementFor(Type primitive) => primitive.Name switch
    {
        nameof(Decimal) => typeof(LenientAmount),
        nameof(Int32)   => typeof(LenientCount),
        nameof(Int16)   => typeof(LenientTinyCount),
        nameof(Int64)   => typeof(LenientLargeCount),
        _               => null
    };

    private static IEnumerable<string> Disagreements<TWrapperSet, TElement, TPrimitiveSet, TPrimitive>()
        where TWrapperSet : IValueSetFactory<TWrapperSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
        where TPrimitiveSet : IValueSetFactory<TPrimitiveSet, TPrimitive>, IValueSet<TPrimitive>
        where TPrimitive : IEquatable<TPrimitive>
    {
        var label    = $"{typeof(TWrapperSet).Name.Split('`')[0]}<{typeof(TElement).Name}>";
        var failures = new List<string>();

        if (!TextProbes.TryGetValue(typeof(TPrimitive), out var texts))
        {
            failures.Add($"  {label}: no probe texts for {typeof(TPrimitive).Name} — add them to TextProbes.");
            return failures;
        }

        foreach (var text in texts)
        {
            var literal  = $"{{\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}";
            var expected = Outcome(() => StringOverloads.Parse<TPrimitiveSet>(literal, CultureInfo.InvariantCulture));

            Compare("Parse", Outcome(() => StringOverloads.Parse<TWrapperSet>(literal, CultureInfo.InvariantCulture)));
            Compare("Parse(span)", Outcome(() => TWrapperSet.Parse(literal.AsSpan(), CultureInfo.InvariantCulture)));
            Compare("TryParse", StringOverloads.TryParse<TWrapperSet>(literal, CultureInfo.InvariantCulture, out var tried)
                                    ? tried.ToString(null, CultureInfo.InvariantCulture)
                                    : Refused);
            Compare("TypeConverter", Outcome(() => (TWrapperSet)TypeDescriptor.GetConverter(typeof(TWrapperSet))
                                                                              .ConvertFromInvariantString(literal)!));
            Compare("JSON string", Outcome(() => JsonSerializer.Deserialize<TWrapperSet>(
                                                     $"[{JsonSerializer.Serialize(text)}]")!));

            void Compare(string path, string actual)
            {
                if (actual != expected)
                    failures.Add($"  {label} {path} of {literal}: {actual}, but {typeof(TPrimitiveSet).Name} reads {expected}");
            }
        }

        foreach (var number in NumberProbes.GetValueOrDefault(typeof(TPrimitive)) ?? [])
        {
            var json     = $"[{number}]";
            var expected = Outcome(() => JsonSerializer.Deserialize<TPrimitiveSet>(json)!);
            var actual   = Outcome(() => JsonSerializer.Deserialize<TWrapperSet>(json)!);

            if (actual != expected)
                failures.Add($"  {label} JSON number {json}: {actual}, but {typeof(TPrimitiveSet).Name} reads {expected}");
        }

        return failures;
    }

    private const string Refused = "refused";

    private static string Outcome<TSet>(Func<TSet> read) where TSet : IFormattable
    {
        try
        {
            return read().ToString(null, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or OverflowException or ArgumentException
                                      or NotSupportedException)
        {
            return Refused;
        }
    }

    private static Type ElementTypeOf(Type setType) =>
        setType.GetInterfaces()
               .First(@interface => @interface.IsGenericType
                                 && @interface.GetGenericTypeDefinition() == typeof(IValueSet<>))
               .GetGenericArguments()[0];
}

// Deliberately lenient numeric wrappers: NumberStyles.Any accepts group separators, parentheses,
// a trailing sign, a currency symbol, an exponent and an integral value with a zero fraction. A
// generated wrapper is usually NumberStyles.Number or the BCL default; Any is the widest it could be,
// which is the point — the family grammar must win whatever the element accepts.

public readonly record struct LenientAmount : IFormattable, IParsable<LenientAmount>, IComparable<LenientAmount>
{
    private readonly decimal _value;

    private LenientAmount(decimal value) => _value = value;

    public static LenientAmount Parse(string s, IFormatProvider? provider)
        => new(decimal.Parse(s, NumberStyles.Any, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out LenientAmount result)
    {
        var parsed = decimal.TryParse(s, NumberStyles.Any, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new LenientAmount(value) : default;
        return parsed;
    }

    public int CompareTo(LenientAmount other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct LenientCount : IFormattable, IParsable<LenientCount>, IComparable<LenientCount>
{
    private readonly int _value;

    private LenientCount(int value) => _value = value;

    public static LenientCount Parse(string s, IFormatProvider? provider)
        => new(int.Parse(s, NumberStyles.Any, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out LenientCount result)
    {
        var parsed = int.TryParse(s, NumberStyles.Any, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new LenientCount(value) : default;
        return parsed;
    }

    public int CompareTo(LenientCount other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct LenientTinyCount : IFormattable, IParsable<LenientTinyCount>, IComparable<LenientTinyCount>
{
    private readonly short _value;

    private LenientTinyCount(short value) => _value = value;

    public static LenientTinyCount Parse(string s, IFormatProvider? provider)
        => new(short.Parse(s, NumberStyles.Any, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out LenientTinyCount result)
    {
        var parsed = short.TryParse(s, NumberStyles.Any, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new LenientTinyCount(value) : default;
        return parsed;
    }

    public int CompareTo(LenientTinyCount other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct LenientLargeCount : IFormattable, IParsable<LenientLargeCount>, IComparable<LenientLargeCount>
{
    private readonly long _value;

    private LenientLargeCount(long value) => _value = value;

    public static LenientLargeCount Parse(string s, IFormatProvider? provider)
        => new(long.Parse(s, NumberStyles.Any, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out LenientLargeCount result)
    {
        var parsed = long.TryParse(s, NumberStyles.Any, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new LenientLargeCount(value) : default;
        return parsed;
    }

    public int CompareTo(LenientLargeCount other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}
