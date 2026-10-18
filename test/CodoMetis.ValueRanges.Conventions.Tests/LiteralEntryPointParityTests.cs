using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.Extensions.Configuration;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// One literal, every entry point that reads one: <c>Parse(string)</c>, <c>Parse(ReadOnlySpan&lt;char&gt;)</c>,
/// both <c>TryParse</c> overloads, a JSON string, the <c>TypeConverter</c> and <c>IConfiguration</c>
/// binding — for every discovered range union and each of its sealed variants, every multirange and
/// every value set. They produce an equal value, or they all refuse it.
/// </summary>
/// <remarks>
/// <para>
/// Every entry point is documented as "the literal", and each was wired separately: the span parser
/// is the one the string overload widens to, JSON goes through its own converter (and a variant's
/// converter defers to the union's), the <c>TypeConverter</c> is inherited by a variant unless it has
/// its own, and configuration binding adds the binder's own rules on top of the converter. Each has
/// been the one entry point that got a literal wrong before (#30–#32, #114). The sweep asserts no
/// value — only that the entry points agree, so a literal nobody thought to test is still checked.
/// </para>
/// <para>
/// A variant has no parser of its own; its expected answer is the union's, narrowed: the same value
/// when the literal has the variant's shape, a refusal otherwise. Refusals are compared as refusals,
/// whatever the exception (the binder wraps the converter's <see cref="FormatException"/> in an
/// <see cref="InvalidOperationException"/>); an exception that is not a refusal — a
/// <see cref="NullReferenceException"/>, an <see cref="IndexOutOfRangeException"/> — is its own outcome.
/// </para>
/// <para>
/// Two documented exceptions shape the grid. A value set's JSON form is the element array, not the
/// literal, so the literal reaches a set through the other entry points (its JSON array is
/// <see cref="SetWrapperParityTests"/>'s). And the <c>TypeConverter</c> converts an empty string to
/// <see langword="null"/>, an absent value, which configuration binding relies on
/// (<c>docs/serialization.md</c>, <c>ConfigurationBindingTests</c>), so the empty literal goes only
/// through the parsers and JSON.
/// </para>
/// </remarks>
[TestClass]
public sealed class LiteralEntryPointParityTests
{
    /// <summary>The property configuration binds the literal into.</summary>
    public sealed class Holder<T>
    {
        public T? Value { get; set; }
    }

    public sealed record LiteralRow(string Label, Func<List<string>, int> Sweep)
    {
        public override string ToString() => Label;
    }

    public static IEnumerable<object[]> Rows
    {
        get
        {
            foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
            {
                yield return [new LiteralRow(rangeType.Name, failures => Reflect.InvokeGeneric<int>(
                    typeof(LiteralEntryPointParityTests), nameof(SweepRange), [rangeType, elementType], failures))];
                yield return [new LiteralRow($"RangeSet<{rangeType.Name}>", failures => Reflect.InvokeGeneric<int>(
                    typeof(LiteralEntryPointParityTests), nameof(SweepRangeSet), [rangeType, elementType], failures))];
            }

            foreach (var (setType, elementType) in SetProbes.AllSetTypes())
                yield return [new LiteralRow(Display(setType), failures => Reflect.InvokeGeneric<int>(
                    typeof(LiteralEntryPointParityTests), nameof(SweepSet), [setType, elementType], failures))];
        }
    }

    [TestMethod]
    public void Discovery_FindsEveryRow()
    {
        // 11 ranges, their 11 multiranges, 30 set types.
        const int knownRows = 52;
        var rows = Rows.Count();
        Assert.IsTrue(rows >= knownRows, $"Found {rows} rows, fewer than {knownRows}.");
    }

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void OneLiteral_ReadsAlikeThroughEveryEntryPoint(LiteralRow row)
    {
        var failures = new List<string>();
        var checks   = row.Sweep(failures);

        Assert.IsTrue(checks >= 200, $"{row}: only {checks} checks ran.");

        Candidates.Verdict(nameof(LiteralEntryPointParityTests), failures,
            $"{row}: of {checks} checks, the entry points read one literal differently", shown: 30, rowPrefix: row.Label + " ");
    }

    // ---------------------------------------------------------------- ranges

    private static int SweepRange<TRange, T>(List<string> failures)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var label    = typeof(TRange).Name;
        var shapes   = RangeProbes.Shapes<TRange, T>();
        var variants = shapes.Select(shape => shape.GetType()).ToArray();
        var checks   = 0;

        foreach (var literal in RangeLiterals<TRange, T>())
        {
            var parsed = Read(() => StringOverloads.Parse<TRange>(literal, CultureInfo.InvariantCulture));

            checks += Agree(failures, $"{label} {Show(literal)}", parsed, Entries<TRange>(literal,
                ("Parse(span)",      () => TRange.Parse(literal.AsSpan(), CultureInfo.InvariantCulture)),
                ("TryParse",         () => StringOverloads.TryParse<TRange>(literal, CultureInfo.InvariantCulture, out var r) ? r : throw new FormatException("TryParse: false")),
                ("TryParse(span)",   () => TRange.TryParse(literal.AsSpan(), CultureInfo.InvariantCulture, out var r) ? r : throw new FormatException("TryParse: false")),
                ("JSON",             () => JsonSerializer.Deserialize<TRange>(JsonSerializer.Serialize(literal))!)));

            // Each variant reads the literal as the union does, narrowed to its shape.
            foreach (var variant in variants)
            {
                var narrowed = parsed.Value is { } value && value.GetType() != variant
                                   ? Outcome.Refused
                                   : parsed;

                checks += Reflect.InvokeGeneric<int>(typeof(LiteralEntryPointParityTests), nameof(AgreeVariant),
                                                     [variant], failures, $"{label}.{variant.Name} {Show(literal)}", literal, narrowed);
            }
        }

        return checks;
    }

    private static int AgreeVariant<TVariant>(List<string> failures, string name, string literal, Outcome expected)
        where TVariant : class =>
        Agree(failures, name, expected, Entries<TVariant>(literal,
            ("JSON", () => JsonSerializer.Deserialize<TVariant>(JsonSerializer.Serialize(literal))!)));

    /// <summary>
    /// The five shapes' literals, the bounds' texts and the domain's extremes, and their neighbours:
    /// padded, re-cased, quoted, inverted, unterminated, decorated.
    /// </summary>
    private static IEnumerable<string> RangeLiterals<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        string Text(object value) => TRange.FormatValue((T)value, null, CultureInfo.InvariantCulture);

        var (lo, hi)   = (Text(RangeProbes.Bounds[typeof(T)].Low), Text(RangeProbes.Bounds[typeof(T)].High));
        var (min, max) = (Text(RangeSpecs.Extremes[typeof(T)].Min), Text(RangeSpecs.Extremes[typeof(T)].Max));

        foreach (var shape in RangeProbes.Shapes<TRange, T>())
        {
            var literal = ((IFormattable)shape).ToString(null, CultureInfo.InvariantCulture);
            yield return literal;
            yield return literal.ToUpperInvariant();
            yield return $" {literal} ";
            yield return $"\t{literal}\n";
            yield return literal + "x";
        }

        string[] grid =
        [
            "", " ", "[", "x", "EMPTY", "Empty", " empty ", "empty x", "(,)", "[,]", "(,]", "[,)", "( , )", "(\"\",\"\")",
            $"[{lo},{hi})", $"[{lo},{hi}]", $"({lo},{hi})", $"({lo},{hi}]", $"[ {lo} , {hi} )", $"[{lo} ,{hi})",
            $"[\"{lo}\",\"{hi}\")", $"[\" {lo} \",\" {hi} \")", $"[\"{lo}\" ,\"{hi}\" )", $"[{hi},{lo})", $"[{lo},{lo})",
            $"[{lo},{lo}]", $"({lo},{lo}]", $"[{lo},)", $"(,{hi}]", $"[{lo},{hi}", $"{lo},{hi}", $"[{lo},{hi})x",
            $"[{lo},{hi},{hi})", $"[{lo};{hi})", $"[\"\",{hi})", $"[{lo},\"\")", $"[\\{lo},{hi})", $"[{lo}\\,{hi})",
            "[-infinity,infinity]", "(-infinity,infinity)", $"[{lo},infinity)", $"[{lo},infinity]", $"(-infinity,{hi}]",
            $"[-infinity,{hi})", "[\"-infinity\",\"infinity\")", "[INFINITY,)",
            $"[{min},{max}]", $"[{min},{max})", $"({min},{max})", $"[{min},)", $"(,{max}]", $"[{max},)", $"(,{min})",
            $"[{min},{min}]", $"[{max},{max}]", $"({max},{max}]"
        ];

        foreach (var literal in grid) yield return literal;
    }

    // ---------------------------------------------------------------- multiranges

    private static int SweepRangeSet<TRange, T>(List<string> failures)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var label  = $"RangeSet<{typeof(TRange).Name}>";
        var checks = 0;

        var ranges = RangeLiterals<TRange, T>().Where(literal => TRange.TryParse(literal, CultureInfo.InvariantCulture, out _))
                                                .Select(literal => literal.Trim())
                                                .Distinct()
                                                .ToList();

        IEnumerable<string> Literals()
        {
            string[] fixedLiterals = ["{}", "{ }", " {} ", "", " ", "{", "}", "{{}}", "{empty}", "{EMPTY}", "{empty,empty}", "{(,)}", "{ (,) }", "x"];
            foreach (var literal in fixedLiterals) yield return literal;

            foreach (var range in ranges)
            {
                yield return $"{{{range}}}";
                yield return $"{{ {range} }}";
                yield return $"{{{range},{range}}}";
                yield return $"{{{range},}}";
                yield return $"{{,{range}}}";
                yield return $"{{{range}";
                yield return range;
                yield return $"{{{range}}}x";
                yield return $"{{{{{range}}}}}";
            }

            for (var i = 0; i + 1 < ranges.Count; i++)
            {
                yield return $"{{{ranges[i]},{ranges[i + 1]}}}";
                yield return $"{{{ranges[i + 1]}, {ranges[i]}}}";
                yield return $"{{{ranges[i]};{ranges[i + 1]}}}";
            }
        }

        foreach (var literal in Literals())
        {
            var parsed = Read(() => RangeSet<TRange, T>.Parse(literal, CultureInfo.InvariantCulture));

            checks += Agree(failures, $"{label} {Show(literal)}", parsed, Entries<RangeSet<TRange, T>>(literal,
                ("Parse(span)",    () => RangeSet<TRange, T>.Parse(literal.AsSpan(), CultureInfo.InvariantCulture)),
                ("TryParse",       () => RangeSet<TRange, T>.TryParse(literal, CultureInfo.InvariantCulture, out var r) ? r : throw new FormatException("TryParse: false")),
                ("IParsable.Parse",    () => StringOverloads.Parse<RangeSet<TRange, T>>(literal, CultureInfo.InvariantCulture)),
                ("IParsable.TryParse", () => StringOverloads.TryParse<RangeSet<TRange, T>>(literal, CultureInfo.InvariantCulture, out var r) ? r : throw new FormatException("TryParse: false")),
                ("TryParse(span)", () => RangeSet<TRange, T>.TryParse(literal.AsSpan(), CultureInfo.InvariantCulture, out var r) ? r : throw new FormatException("TryParse: false")),
                ("JSON",           () => JsonSerializer.Deserialize<RangeSet<TRange, T>>(JsonSerializer.Serialize(literal))!)));
        }

        return checks;
    }

    // ---------------------------------------------------------------- value sets

    private static int SweepSet<TSet, TElement>(List<string> failures)
        where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        var label  = Display(typeof(TSet));
        var checks = 0;
        var texts  = SetProbes.For<TElement>().Select(value => TSet.FormatValue(value, null, CultureInfo.InvariantCulture)).ToList();

        foreach (var literal in SetWrapperParityTests.LiteralGrid(texts))
        {
            var parsed = Read(() => StringOverloads.Parse<TSet>(literal, CultureInfo.InvariantCulture));

            checks += Agree(failures, $"{label} {Show(literal)}", parsed, Entries<TSet>(literal,
                ("Parse(span)",    () => TSet.Parse(literal.AsSpan(), CultureInfo.InvariantCulture)),
                ("TryParse",       () => StringOverloads.TryParse<TSet>(literal, CultureInfo.InvariantCulture, out var r) ? r : throw new FormatException("TryParse: false")),
                ("TryParse(span)", () => TSet.TryParse(literal.AsSpan(), CultureInfo.InvariantCulture, out var r) ? r : throw new FormatException("TryParse: false"))));
        }

        return checks;
    }

    // ---------------------------------------------------------------- the comparison

    /// <summary>What one entry point made of the literal: a value, a refusal, or an exception that is neither.</summary>
    private sealed record Outcome(object? Value, string Text)
    {
        internal static readonly Outcome Refused = new(null, "refused");
    }

    private static Outcome Read(Func<object> read)
    {
        try
        {
            var value = read();
            return value is null ? new(null, "null") : new(value, $"{value.GetType().Name} {value}");
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or ArgumentException
                                      or NotSupportedException or OverflowException)
        {
            return Outcome.Refused;
        }
        catch (Exception ex)
        {
            return new(null, $"throws {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// The entry points every type shares — the <c>TypeConverter</c> and configuration binding — after
    /// the type's own.
    /// </summary>
    private static IEnumerable<(string Name, Func<object> Read)> Entries<TValue>(string literal, params (string, Func<TValue>)[] own)
        where TValue : class
    {
        foreach (var (name, read) in own)
            yield return (name, () => read());

        // An empty string is an absent value to the TypeConverter and the binder, never a value or a
        // refusal: documented (docs/serialization.md, "An empty string converts to null").
        if (literal.Length == 0) yield break;

        yield return ("TypeConverter", () => TypeDescriptor.GetConverter(typeof(TValue)).ConvertFromInvariantString(literal)!);
        yield return ("configuration", () => new ConfigurationBuilder()
                                                .AddInMemoryCollection([new KeyValuePair<string, string?>("Value", literal)])
                                                .Build()
                                                .Get<Holder<TValue>>()?.Value
                                              ?? throw new InvalidOperationException("bound no value"));
    }

    private static int Agree(List<string> failures, string name, Outcome expected, IEnumerable<(string Name, Func<object> Read)> entries)
    {
        var checks = 0;
        foreach (var (entry, read) in entries)
        {
            checks++;
            var actual = Read(read);

            if (actual.Text != expected.Text)
                failures.Add($"{name} {entry}: {actual.Text}, but Parse → {expected.Text}");
            else if (actual.Value is not null && !actual.Value.Equals(expected.Value))
                failures.Add($"{name} {entry}: {actual.Text} prints alike but is not equal to what Parse read");
        }

        return checks;
    }

    private static string Show(string literal) => JsonSerializer.Serialize(literal);

    private static string Display(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>"
            : type.Name;
}
