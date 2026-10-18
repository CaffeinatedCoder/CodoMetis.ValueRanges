using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Oracles;
using NodaTime;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every primitive value set answers exactly as its wrapper arity closed over an identity element
/// (<c>Int32Set</c> and <c>Int32Set&lt;Same&lt;int&gt;&gt;</c>): every operation, every parse and format
/// entry point, JSON both ways and the <c>TypeConverter</c> give the same value, or throw the same
/// exception type on the same bad input. Nothing here says what the right answer is; the two siblings
/// only have to agree.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper arities have lagged their primitive set more than once — "as <c>DecimalSet</c> does" is a
/// recurring changelog phrase — and each time the divergence was found at one entry point and fixed
/// there. The claim-driven oracles check each family against its documents, so they cannot see a promise
/// the two siblings share implicitly. A differential sweep needs no ground truth: an identity element
/// (<see cref="Same{T}"/>, <see cref="SameText"/>) leaves the family's own code as the only difference,
/// so whichever side disagrees is the defect.
/// </para>
/// <para>
/// The input grid is the family's probes from <see cref="SetProbes"/> plus its extremes (the domain's
/// minimum and maximum, the strings that need escaping), every subset shape built from them, and array
/// literals and JSON documents derived from their text: quoted and whitespace-padded elements,
/// duplicates, out of order, empty, <c>NULL</c>, and the malformed neighbours of each. One
/// <c>[DynamicData]</c> row per family; a failure line starts with the family's name, which is what a
/// candidate row in <c>test/oracles/candidates.json</c> is scoped by.
/// </para>
/// <para>
/// A probe the primitive family <em>rewrites</em> (a NodaTime date in another calendar) is left out: a
/// wrapper family cannot build its element from the normalized primitive, so it refuses one instead —
/// the documented rule (<c>docs/architecture.md</c>), not a disagreement between siblings.
/// </para>
/// </remarks>
[TestClass]
public sealed class SetWrapperParityTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>The values at the edge of each element domain, and the strings an array literal must escape.</summary>
    private static readonly Dictionary<Type, object[]> Extremes = new()
    {
        [typeof(string)]         = ["", " padded ", "NULL", "null", "a,b", "{x}", "quo\"te", "back\\slash", "tab\there", "ü"],
        [typeof(Guid)]           = [Guid.Empty, Guid.AllBitsSet],
        [typeof(short)]          = [short.MinValue, short.MaxValue],
        [typeof(int)]            = [int.MinValue, int.MaxValue],
        [typeof(long)]           = [long.MinValue, long.MaxValue],
        [typeof(decimal)]        = [decimal.MinValue, decimal.MaxValue, 0.0000000000000000000000000001m, 1.10m],
        [typeof(float)]          = [float.MinValue, float.MaxValue, float.Epsilon, -0f, 0.1f, 1e6f, 16777217f],
        [typeof(double)]         = [double.MinValue, double.MaxValue, double.Epsilon, -0d, 2.9802322387695312e-08, 4.1045368012983762e-289, 1e23],
        [typeof(DateOnly)]       = [DateOnly.MinValue, DateOnly.MaxValue],
        [typeof(TimeOnly)]       = [TimeOnly.MinValue, TimeOnly.MaxValue],
        [typeof(DateTime)]       = [DateTime.MinValue, DateTime.MaxValue, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)],
        [typeof(DateTimeOffset)] = [DateTimeOffset.MinValue, DateTimeOffset.MaxValue,
                                    new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(14))],
        [typeof(LocalDate)]      = [LocalDate.MinIsoValue, LocalDate.MaxIsoValue],
        [typeof(LocalDateTime)]  = [LocalDateTime.MinIsoValue, LocalDateTime.MaxIsoValue],
        [typeof(Instant)]        = [Instant.MinValue, Instant.MaxValue],
        [typeof(LocalTime)]      = [LocalTime.Midnight, LocalTime.MaxValue],
        [typeof(YearMonth)]      = [new YearMonth(-9998, 1), new YearMonth(9999, 12)]
    };

    private static readonly string?[] Formats = [null, "", "G", "D", "O"];

    /// <summary>One row per primitive family; the label is the prefix of every failure line and candidate row.</summary>
    public sealed record ParityRow(string Label, Func<List<string>, int> Sweep)
    {
        public override string ToString() => Label;
    }

    public static IEnumerable<object[]> Rows =>
        Pairs().Select(pair => new object[]
        {
            new ParityRow(pair.Primitive.Name, failures => Reflect.InvokeGeneric<int>(
                typeof(SetWrapperParityTests), nameof(Sweep),
                [pair.Primitive, pair.Element, pair.Wrapper, IdentityWrappers.For(pair.Element)], failures))
        });

    private static IEnumerable<(Type Primitive, Type Element, Type Wrapper)> Pairs()
    {
        foreach (var (setType, element) in SetProbes.AllSetTypes().Where(pair => !pair.Set.IsGenericType))
        {
            var family = setType.Assembly.GetType(setType.FullName + "`1")
                      ?? throw new InvalidOperationException($"{setType.Name} has no wrapper arity {setType.Name}<TElement>.");

            yield return (setType, element, family.MakeGenericType(IdentityWrappers.For(element)));
        }
    }

    [TestMethod]
    public void Discovery_PairsEveryPrimitiveFamilyWithItsWrapperArity()
    {
        // 10 core families and 5 NodaTime ones, each with its wrapper arity, as of 9.0.0.
        const int knownFamilies = 15;
        var rows = Rows.Count();
        Assert.IsTrue(rows >= knownFamilies, $"Found {rows} primitive families, fewer than {knownFamilies}.");

        var wrapperFamilies = SetProbes.AllSetTypes().Count(pair => pair.Set.IsGenericType);
        Assert.AreEqual(wrapperFamilies, rows, "Every wrapper family must have a primitive sibling, and every primitive family a wrapper arity.");
    }

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void PrimitiveSet_AndItsIdentityWrapperArity_AnswerAlike(ParityRow row)
    {
        var failures = new List<string>();
        var checks   = row.Sweep(failures);

        Assert.IsTrue(checks >= 1_000, $"{row}: only {checks} checks ran.");

        Candidates.Verdict(nameof(SetWrapperParityTests), failures,
            $"{row}: of {checks} checks, the wrapper arity disagrees with its primitive set", shown: 30, rowPrefix: row.Label + " ");
    }

    // ---------------------------------------------------------------- the sweep

    private static int Sweep<TPrimSet, TPrim, TWrapSet, TWrap>(List<string> failures)
        where TPrimSet : class, IValueSetFactory<TPrimSet, TPrim>, IValueSet<TPrim>
        where TPrim : IEquatable<TPrim>
        where TWrapSet : class, IValueSetFactory<TWrapSet, TWrap>, IValueSet<TWrap>
        where TWrap : struct, IEquatable<TWrap>, IIdentity<TWrap, TPrim>
    {
        var label  = typeof(TPrimSet).Name;
        var wrap   = $"{typeof(TPrimSet).Name}<{typeof(TWrap).Name.Split('`')[0]}>";
        var checks = 0;

        // ---- the universe: probes the family keeps as given, plus the domain's extremes
        var universe = SetProbes.For<TPrim>()
                                .Concat(Extremes[typeof(TPrim)].Cast<TPrim>())
                                .Where(value => TPrimSet.From(value).Values.Single().Equals(value))
                                .DistinctBy(value => TPrimSet.FormatValue(value, null, CultureInfo.InvariantCulture))
                                .ToArray();

        var sets = SubsetShapes(universe).ToList();

        // ---- element format and parse
        var elementTexts = new List<string>();
        foreach (var value in universe)
        {
            var text = TPrimSet.FormatValue(value, null, CultureInfo.InvariantCulture);
            elementTexts.Add(text);

            foreach (var format in Formats)
            foreach (var provider in (CultureInfo[])[CultureInfo.InvariantCulture, German])
                Compare($"FormatValue({Show(text)}, {Show(format)}, {provider.Name})",
                        Outcome(() => TPrimSet.FormatValue(value, format, provider)),
                        Outcome(() => TWrapSet.FormatValue(TWrap.Wrap(value), format, provider)));
        }

        foreach (var text in ElementTextGrid(elementTexts))
            Compare($"ParseValue({Show(text)})",
                    Outcome(() => ElementText(TPrimSet.ParseValue(text.AsSpan(), CultureInfo.InvariantCulture))),
                    Outcome(() => ElementText(TWrapSet.ParseValue(text.AsSpan(), CultureInfo.InvariantCulture).Value)));

        // ---- construction, unary state and formatting, per set
        var primCount   = Property<TPrimSet>("Count");
        var primIsEmpty = Property<TPrimSet>("IsEmpty");
        var wrapCount   = Property<TWrapSet>("Count");
        var wrapIsEmpty = Property<TWrapSet>("IsEmpty");

        foreach (var elements in sets)
        {
            var name     = Name(elements);
            var prim     = TPrimSet.From(elements);
            var wrapped  = elements.Select(TWrap.Wrap).ToArray();

            Compare($"From({name})", Render(prim), Outcome(() => Render(TWrapSet.From(wrapped))));
            Compare($"From(reversed, duplicated {name})",
                    Outcome(() => Render(TPrimSet.From([.. Enumerable.Reverse(elements), .. elements]))),
                    Outcome(() => Render(TWrapSet.From([.. Enumerable.Reverse(wrapped), .. wrapped]))));
            Compare($"From(span {name})",
                    Outcome(() => Render(TPrimSet.From(elements.AsSpan()))),
                    Outcome(() => Render(TWrapSet.From(wrapped.AsSpan()))));
            Compare($"repeated Add {name}",
                    Outcome(() => Render(elements.Aggregate(TPrimSet.Empty, (set, value) => set.Add(value)))),
                    Outcome(() => Render(wrapped.Aggregate(TWrapSet.Empty, (set, value) => set.Add(value)))));

            var wrapSet = TWrapSet.From(wrapped);

            Compare($"{name}.Count",   primCount.GetValue(prim)!.ToString()!,   wrapCount.GetValue(wrapSet)!.ToString()!);
            Compare($"{name}.IsEmpty", primIsEmpty.GetValue(prim)!.ToString()!, wrapIsEmpty.GetValue(wrapSet)!.ToString()!);
            Compare($"{name}.ToString()", Outcome(prim.ToString), Outcome(wrapSet.ToString));

            foreach (var format in Formats)
            foreach (var provider in (CultureInfo[])[CultureInfo.InvariantCulture, German])
                Compare($"{name}.ToString({Show(format)}, {provider.Name})",
                        Outcome(() => prim.ToString(format, provider)),
                        Outcome(() => wrapSet.ToString(format, provider)));

            Compare($"TypeConverter.ConvertToInvariantString({name})",
                    Outcome(() => TypeDescriptor.GetConverter(typeof(TPrimSet)).ConvertToInvariantString(prim)),
                    Outcome(() => TypeDescriptor.GetConverter(typeof(TWrapSet)).ConvertToInvariantString(wrapSet)));

            // ---- JSON write: the same token kinds, and each side reads the other's document as the same value
            var primJson = JsonSerializer.Serialize(prim);
            var wrapJson = Outcome(() => JsonSerializer.Serialize(wrapSet));

            Compare($"JSON token kinds of {name}", TokenKinds(primJson), Outcome(() => TokenKinds(wrapJson)));
            Compare($"JSON write {name}, read as {label}", Render(prim),
                    Outcome(() => Render(JsonSerializer.Deserialize<TPrimSet>(wrapJson)!)));
            Compare($"JSON write {name}, read as {wrap}", Render(prim),
                    Outcome(() => Render(JsonSerializer.Deserialize<TWrapSet>(primJson)!)));

            // ---- element operations, with every element of the universe
            foreach (var value in universe)
            {
                var element = TWrap.Wrap(value);
                var shown   = Show(ElementText(value));

                Compare($"{name}.Contains({shown})", Outcome(() => prim.Contains(value).ToString()), Outcome(() => wrapSet.Contains(element).ToString()));
                Compare($"{name}.Add({shown})",      Outcome(() => Render(prim.Add(value))),         Outcome(() => Render(wrapSet.Add(element))));
                Compare($"{name}.Remove({shown})",   Outcome(() => Render(prim.Remove(value))),      Outcome(() => Render(wrapSet.Remove(element))));
            }
        }

        // ---- binary operations, over every ordered pair of set shapes
        var primEquality   = Operator<TPrimSet>("op_Equality");
        var primInequality = Operator<TPrimSet>("op_Inequality");
        var wrapEquality   = Operator<TWrapSet>("op_Equality");
        var wrapInequality = Operator<TWrapSet>("op_Inequality");

        foreach (var leftElements in sets)
        foreach (var rightElements in sets)
        {
            var pair = $"{Name(leftElements)} ? {Name(rightElements)}";
            var (pl, pr) = (TPrimSet.From(leftElements), TPrimSet.From(rightElements));
            var (wl, wr) = (TWrapSet.From(leftElements.Select(TWrap.Wrap)), TWrapSet.From(rightElements.Select(TWrap.Wrap)));

            Compare(pair.Replace("?", "Union"),     Outcome(() => Render(pl.Union(pr))),     Outcome(() => Render(wl.Union(wr))));
            Compare(pair.Replace("?", "Intersect"), Outcome(() => Render(pl.Intersect(pr))), Outcome(() => Render(wl.Intersect(wr))));
            Compare(pair.Replace("?", "Except"),    Outcome(() => Render(pl.Except(pr))),    Outcome(() => Render(wl.Except(wr))));

            Compare(pair.Replace("?", "Overlaps"),           Outcome(() => pl.Overlaps(pr).ToString()),           Outcome(() => wl.Overlaps(wr).ToString()));
            Compare(pair.Replace("?", "IsSubsetOf"),         Outcome(() => pl.IsSubsetOf(pr).ToString()),         Outcome(() => wl.IsSubsetOf(wr).ToString()));
            Compare(pair.Replace("?", "IsSupersetOf"),       Outcome(() => pl.IsSupersetOf(pr).ToString()),       Outcome(() => wl.IsSupersetOf(wr).ToString()));
            Compare(pair.Replace("?", "IsProperSubsetOf"),   Outcome(() => pl.IsProperSubsetOf(pr).ToString()),   Outcome(() => wl.IsProperSubsetOf(wr).ToString()));
            Compare(pair.Replace("?", "IsProperSupersetOf"), Outcome(() => pl.IsProperSupersetOf(pr).ToString()), Outcome(() => wl.IsProperSupersetOf(wr).ToString()));
            Compare(pair.Replace("?", "Equals"),             Outcome(() => pl.Equals(pr).ToString()),             Outcome(() => wl.Equals(wr).ToString()));
            Compare(pair.Replace("?", "=="), primEquality.Invoke(null, [pl, pr])!.ToString()!,   wrapEquality.Invoke(null, [wl, wr])!.ToString()!);
            Compare(pair.Replace("?", "!="), primInequality.Invoke(null, [pl, pr])!.ToString()!, wrapInequality.Invoke(null, [wl, wr])!.ToString()!);
        }

        // ---- literals through every text entry point
        var primConverter = TypeDescriptor.GetConverter(typeof(TPrimSet));
        var wrapConverter = TypeDescriptor.GetConverter(typeof(TWrapSet));

        foreach (var literal in LiteralGrid(elementTexts))
        {
            var shown = Show(literal);

            Compare($"Parse({shown})",
                    Outcome(() => Render(StringOverloads.Parse<TPrimSet>(literal, CultureInfo.InvariantCulture))),
                    Outcome(() => Render(StringOverloads.Parse<TWrapSet>(literal, CultureInfo.InvariantCulture))));
            Compare($"Parse({shown}, de-DE)",
                    Outcome(() => Render(StringOverloads.Parse<TPrimSet>(literal, German))),
                    Outcome(() => Render(StringOverloads.Parse<TWrapSet>(literal, German))));
            Compare($"Parse(span {shown})",
                    Outcome(() => Render(TPrimSet.Parse(literal.AsSpan(), CultureInfo.InvariantCulture))),
                    Outcome(() => Render(TWrapSet.Parse(literal.AsSpan(), CultureInfo.InvariantCulture))));
            Compare($"TryParse({shown})",
                    Outcome(() => StringOverloads.TryParse<TPrimSet>(literal, CultureInfo.InvariantCulture, out var p) ? Render(p) : $"false, {Render(p)}"),
                    Outcome(() => StringOverloads.TryParse<TWrapSet>(literal, CultureInfo.InvariantCulture, out var w) ? Render(w) : $"false, {Render(w)}"));
            Compare($"TryParse(span {shown})",
                    Outcome(() => TPrimSet.TryParse(literal.AsSpan(), CultureInfo.InvariantCulture, out var p) ? Render(p) : $"false, {Render(p)}"),
                    Outcome(() => TWrapSet.TryParse(literal.AsSpan(), CultureInfo.InvariantCulture, out var w) ? Render(w) : $"false, {Render(w)}"));
            Compare($"TypeConverter.ConvertFromInvariantString({shown})",
                    Outcome(() => Render((TPrimSet)primConverter.ConvertFromInvariantString(literal)!)),
                    Outcome(() => Render((TWrapSet)wrapConverter.ConvertFromInvariantString(literal)!)));
            Compare($"TypeConverter.ConvertFrom(de-DE, {shown})",
                    Outcome(() => Render((TPrimSet)primConverter.ConvertFrom(null, German, literal)!)),
                    Outcome(() => Render((TWrapSet)wrapConverter.ConvertFrom(null, German, literal)!)));
        }

        // ---- JSON documents read by both
        foreach (var json in JsonGrid(sets.Select(elements => JsonSerializer.Serialize(TPrimSet.From(elements)))))
            Compare($"JSON read {json}",
                    Outcome(() => Render(JsonSerializer.Deserialize<TPrimSet>(json))),
                    Outcome(() => Render(JsonSerializer.Deserialize<TWrapSet>(json))));

        return checks;

        void Compare(string operation, string primitive, string wrapper)
        {
            checks++;
            if (!string.Equals(primitive, wrapper, StringComparison.Ordinal))
                failures.Add($"{label} {operation}: {label} → {primitive}, {wrap} → {wrapper}");
        }

        string Render(object? set) => set switch
        {
            null                => "null",
            TPrimSet primitive  => Elements(primitive.Values),
            TWrapSet wrapperSet => Elements(wrapperSet.Values.Select(element => element.Value)),
            _                   => throw new UnreachableException()
        };

        // The primitive's own text form, in the set's order: a lost scale, kind or offset shows, and so
        // does an order the family did not sort by.
        string Elements(IEnumerable<TPrim> values) =>
            JsonSerializer.Serialize(values.Select(ElementText).ToArray());

        string ElementText(TPrim value) => TPrimSet.FormatValue(value, null, CultureInfo.InvariantCulture);

        string Name(TPrim[] elements) =>
            "{" + string.Join(",", elements.Select(value => ElementText(value))) + "}";
    }

    // ---------------------------------------------------------------- grids

    /// <summary>The empty set, every singleton, consecutive pairs, alternate elements and the whole universe.</summary>
    private static IEnumerable<TPrim[]> SubsetShapes<TPrim>(TPrim[] universe)
    {
        yield return [];
        foreach (var value in universe) yield return [value];
        for (var i = 0; i + 1 < universe.Length; i++) yield return [universe[i], universe[i + 1]];
        yield return [.. universe.Where((_, index) => index % 2 == 0)];
        yield return [.. universe.Where((_, index) => index % 2 == 1)];
        yield return universe;
    }

    /// <summary>Each element text, padded, quoted, and its malformed neighbours.</summary>
    private static IEnumerable<string> ElementTextGrid(List<string> texts)
    {
        foreach (var text in texts)
        {
            yield return text;
            yield return $" {text} ";
            yield return $"\t{text}";
            yield return $"\"{text}\"";
            yield return text.ToUpperInvariant();
            yield return text + "x";
        }

        yield return "";
        yield return " ";
        yield return "NULL";
        yield return "infinity";
        yield return "-infinity";
    }

    /// <summary>
    /// Array literals over the family's element texts: well-formed, padded, quoted, duplicated, out of
    /// order, empty, with <c>NULL</c>, and their malformed neighbours.
    /// </summary>
    internal static IEnumerable<string> LiteralGrid(List<string> texts)
    {
        var (t0, t1) = (texts[0], texts[Math.Min(1, texts.Count - 1)]);

        string[] fixedLiterals =
        [
            "{}", "{ }", " {} ", "", " ", "{", "}", "{{}}", "[1:1]={" + Quote(t0) + "}",
            $"{{{t0}}}", $"{{{Quote(t0)}}}", $"{{{Padded(t0)}}}", $"{{ {t0} }}",
            $"{{{t0},{t1}}}", $"{{{t1},{t0}}}", $"{{{t0},{t0}}}", $"{{{Quote(t0)},{Quote(t0)}}}",
            $"{{ {t0} , {t1} }}", $"{{{Padded(t1)},{Padded(t0)}}}",
            "{" + string.Join(",", texts.Select(Quote)) + "}",
            "{" + string.Join(",", texts.AsEnumerable().Reverse().Select(Quote)) + "}",
            "{" + string.Join(",", texts.Concat(texts).Select(Padded)) + "}",
            "{NULL}", $"{{{t0},NULL}}", "{\"NULL\"}", "{null}", "{ NULL }",
            $"{{{t0},}}", $"{{,{t0}}}", $"{{{t0}", $"{t0}}}", t0, $"{{{Quote(t0)}x}}", $"{{\"{t0}}}",
            $"{{{t0}\"}}", $"{{{{{t0}}}}}", $"{{{t0};{t1}}}", $"{{{Quote(t0)} {Quote(t1)}}}", $"{{\\{t0}}}"
        ];

        foreach (var literal in fixedLiterals) yield return literal;

        foreach (var text in texts)
        {
            yield return $"{{{text}}}";
            yield return $"{{{Quote(text)}}}";
            yield return $"{{{Padded(text)}}}";
        }

        static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        static string Padded(string text) => "\" " + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + " \"";
    }

    /// <summary>The documents the primitive writes, and their neighbours: reordered, duplicated, mistyped, malformed.</summary>
    private static IEnumerable<string> JsonGrid(IEnumerable<string> written)
    {
        var documents = written.ToList();
        foreach (var json in documents) yield return json;

        var tokens = JsonDocument.Parse(documents.OrderByDescending(json => json.Length).First())
                                 .RootElement.EnumerateArray().Select(element => element.GetRawText()).ToList();

        yield return "[" + string.Join(",", tokens.AsEnumerable().Reverse().Concat(tokens)) + "]";

        foreach (var token in tokens.Take(3))
        {
            yield return $"[{token},{token}]";
            yield return $"[{token},null]";
            yield return token.StartsWith('"') ? $"[{token.Trim('"')}]" : $"[\"{token}\"]";
            yield return $"[\" {token.Trim('"')} \"]";
            yield return $"[[{token}]]";
        }

        foreach (var json in (string[])["null", "[]", "[null]", "{}", "\"{}\"", "[true]", "[{}]", "[", "1", "\"\"", "[\"\"]"])
            yield return json;
    }

    // ---------------------------------------------------------------- helpers

    private static string Outcome(Func<string?> run)
    {
        try
        {
            return run() ?? "null";
        }
        catch (Exception ex)
        {
            return $"throws {ex.GetType().Name}";
        }
    }

    private static string TokenKinds(string json) =>
        string.Join(",", JsonDocument.Parse(json).RootElement.EnumerateArray().Select(element => element.ValueKind));

    private static string Show(string? text) => text is null ? "null" : JsonSerializer.Serialize(text);

    private static PropertyInfo Property<TSet>(string name) =>
        typeof(TSet).GetProperty(name) ?? throw new InvalidOperationException($"{typeof(TSet).Name} has no public {name}.");

    private static MethodInfo Operator<TSet>(string name) =>
        typeof(TSet).GetMethod(name, BindingFlags.Public | BindingFlags.Static, [typeof(TSet), typeof(TSet)])
     ?? throw new InvalidOperationException($"{typeof(TSet).Name} declares no {name}(TSet, TSet).");
}
