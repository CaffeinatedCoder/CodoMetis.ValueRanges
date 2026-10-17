using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Oracles;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The first probe sweep of route B (#105, `test/oracles/axes.md`): every discovered range, multirange
/// and value set type × how a consumer declares it × the <see cref="JsonSerializerOptions"/> a
/// consumer sets. The oracle is the document's promise: the wire form is the type's literal (a set's
/// array), the round trip is the same value, and a read into an interface is the refusal
/// <c>docs/serialization.md</c> names.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="JsonDefaultContractTests"/> holds the same promise under default options for the
/// declarations System.Text.Json resolves differently (union, variant, interface, <c>object</c>).
/// What it does not vary is the options. #114, #116 and #117 were each found by one combination of
/// consumer settings that no test kept, so this class keeps the product: six declarations × six
/// option sets per value. One <c>[DynamicData]</c> row per type, so a failure names the type in the
/// test explorer and a candidate row (<c>test/oracles/candidates.json</c>) is scoped to it by prefix.
/// (<c>[DataTestMethod]</c> is obsolete in MSTest 4; <c>[TestMethod]</c> with <c>[DynamicData]</c> is the same thing.)
/// </para>
/// <para>
/// The source-generated option set lists every type of this package in <see cref="JsonOptionsProbeContext"/>
/// and lets the holders (a record, a dictionary, an interface declaration) fall back to reflection,
/// since a context can only list closed types known at compile time; <c>Discovery_ContextListsEveryType</c>
/// fails naming the attribute to add when a type is missing. A context missing an element or runtime
/// type is the refusal <c>SetSourceGenerationTests</c> and <c>InterfaceDeclaredJsonTests</c> hold.
/// </para>
/// <para>
/// Under <c>NumberHandling.AllowReadingFromString</c> — part of ASP.NET Core's default web options — the
/// sweep also reads the wire form with every number quoted, since that is what the option promises a
/// client may send, and a closed integer set and its wrapper arity must agree on it.
/// </para>
/// </remarks>
[TestClass]
public sealed class JsonOptionsProbeTests
{
    private static readonly JsonSerializerOptions Default = new();

    /// <summary>The options axis: what a consumer sets, named as the failure line names it.</summary>
    private static readonly (string Name, JsonSerializerOptions Options)[] OptionsAxis =
    [
        ("default", Default),
        ("camelCase", new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
        ("snake_case", new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower }),
        ("ReferenceHandler.Preserve", new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve }),
        ("source-generated context", new JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(JsonOptionsProbeContext.Default, new DefaultJsonTypeInfoResolver())
        }),
        ("NumberHandling.AllowReadingFromString", new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString })
    ];

    /// <summary>One row per discovered type; the row's label is the prefix every failure line and candidate row starts with.</summary>
    public sealed record ProbeRow(string Label, Func<List<string>, int> Sweep)
    {
        public override string ToString() => Label;
    }

    public static IEnumerable<object[]> Rows
    {
        get
        {
            foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
            {
                yield return [new ProbeRow(rangeType.Name, failures => Reflect.InvokeGeneric<int>(typeof(JsonOptionsProbeTests), nameof(SweepRange), [rangeType, elementType], failures))];
                yield return [new ProbeRow($"RangeSet<{rangeType.Name}>", failures => Reflect.InvokeGeneric<int>(typeof(JsonOptionsProbeTests), nameof(SweepRangeSet), [rangeType, elementType], failures))];
            }

            foreach (var (setType, elementType) in SetProbes.AllSetTypes())
                yield return [new ProbeRow(Display(setType), failures => Reflect.InvokeGeneric<int>(typeof(JsonOptionsProbeTests), nameof(SweepSet), [setType, elementType], failures))];
        }
    }

    /// <summary>The floor that keeps the row list from passing by finding nothing.</summary>
    [TestMethod]
    public void Discovery_FindsEveryRow()
    {
        // 11 ranges, their 11 multiranges, 30 set types.
        const int knownRows = 52;
        var rows = Rows.Count();
        Assert.IsTrue(rows >= knownRows, $"Found {rows} rows, fewer than {knownRows}.");
    }

    /// <summary>
    /// The source-generated context lists every union, variant, multirange and set type, so the
    /// source-generated option set reaches each through generated metadata rather than the fallback.
    /// </summary>
    [TestMethod]
    public void Discovery_ContextListsEveryType()
    {
        var types = new List<Type>();
        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
        {
            types.Add(rangeType);
            types.AddRange(rangeType.GetNestedTypes().Where(nested => nested.IsSubclassOf(rangeType)));
            types.Add(typeof(RangeSet<,>).MakeGenericType(rangeType, elementType));
        }

        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
        {
            types.Add(setType);
            types.Add(elementType);
        }

        var missing = types.Where(type => JsonOptionsProbeContext.Default.GetTypeInfo(type) is null)
                           .Select(type => $"[JsonSerializable(typeof({CSharp(type)}))]")
                           .ToList();

        Assert.AreEqual(0, missing.Count,
            "JsonOptionsProbeContext does not list every type; add to JsonOptionsProbeContext.cs:" + Environment.NewLine
          + string.Join(Environment.NewLine, missing));
    }

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void EveryDeclaration_WritesTheLiteralAndReadsBack_UnderEveryOptions(ProbeRow row)
    {
        var failures = new List<string>();
        var checks   = row.Sweep(failures);

        // Six option sets × at least five declarations × at least two values.
        Assert.IsTrue(checks >= 60, $"{row}: only {checks} checks ran.");

        Candidates.Verdict(nameof(JsonOptionsProbeTests), failures,
            $"{row}: of {checks} checks, these disagree with the documented wire form", rowPrefix: row.Label + " ");
    }

    // --- ranges -----------------------------------------------------------------------------------

    private static int SweepRange<TRange, T>(List<string> failures)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var checks = 0;
        foreach (var range in RangeProbes.Shapes<TRange, T>())
        {
            var variant  = range.GetType();
            var expected = JsonSerializer.Serialize(((IFormattable)range).ToString(null, CultureInfo.InvariantCulture));

            foreach (var (name, options) in OptionsAxis)
            {
                var label = $"{typeof(TRange).Name} {range} under {name}";
                checks += ProbeDeclared(failures, label, range, typeof(TRange), expected, options);
                checks += ProbeDeclared(failures, label, range, variant, expected, options);
                foreach (var declared in DeclarableInterfacesOf(variant))
                    checks += ProbeInterface(failures, label, range, declared, expected, options);
                checks += ProbeObject(failures, label, range, expected, options);
                checks += ProbeHolder(failures, label, range, typeof(TRange), expected, options);
                checks += ProbeHolder(failures, label, range, variant, expected, options);
                checks += ProbeDictionaryValue(failures, label, range, typeof(TRange), expected, options);
                checks += ProbeDictionaryValue(failures, label, range, variant, expected, options);
                checks += ProbeNull(failures, $"{typeof(TRange).Name} null under {name}", typeof(TRange), options);
                checks += ProbeNull(failures, $"{typeof(TRange).Name} null under {name}", variant, options);
            }
        }

        return checks;
    }

    private static int SweepRangeSet<TRange, T>(List<string> failures)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var shapes = RangeProbes.Shapes<TRange, T>();
        RangeSet<TRange, T>[] sets =
        [
            RangeSet<TRange, T>.Empty,
            RangeSet<TRange, T>.From([shapes[2]]),
            RangeSet<TRange, T>.From([TRange.Infinite])
        ];

        var checks = 0;
        foreach (var set in sets)
        {
            var expected = JsonSerializer.Serialize(set.ToString(null, CultureInfo.InvariantCulture));
            foreach (var (name, options) in OptionsAxis)
            {
                var label = $"RangeSet<{typeof(TRange).Name}> {set} under {name}";
                checks += ProbeDeclared(failures, label, set, set.GetType(), expected, options);
                checks += ProbeObject(failures, label, set, expected, options);
                checks += ProbeHolder(failures, label, set, set.GetType(), expected, options);
                checks += ProbeDictionaryValue(failures, label, set, set.GetType(), expected, options);
                checks += ProbeNull(failures, $"RangeSet<{typeof(TRange).Name}> null under {name}", set.GetType(), options);
            }
        }

        return checks;
    }

    // --- value sets -------------------------------------------------------------------------------

    private static int SweepSet<TSet, T>(List<string> failures)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var checks = 0;
        foreach (var set in new[] { TSet.Empty, TSet.From(SetProbes.For<T>()) })
        {
            // The documented wire form is a JSON array of the elements; the default-options union
            // declaration is the reference the other declarations and options are held to.
            var expected = JsonSerializer.Serialize(set, typeof(TSet), Default);
            var display  = Display(typeof(TSet));
            var elements = ((IValueSet<T>)set).Values.Length;
            if (!expected.StartsWith('[') || Count(expected) != elements)
                failures.Add($"{display} {set} under default as {display}: wrote {expected}, not an array of its {elements} elements");

            foreach (var (name, options) in OptionsAxis)
            {
                var label = $"{display} {set} under {name}";
                checks += ProbeDeclared(failures, label, set, typeof(TSet), expected, options);
                checks += ProbeInterface(failures, label, set, typeof(IValueSet<T>), expected, options);
                checks += ProbeObject(failures, label, set, expected, options);
                checks += ProbeHolder(failures, label, set, typeof(TSet), expected, options);
                checks += ProbeDictionaryValue(failures, label, set, typeof(TSet), expected, options);
                checks += ProbeNull(failures, $"{display} null under {name}", typeof(TSet), options);

                if ((options.NumberHandling & JsonNumberHandling.AllowReadingFromString) != 0 && HasNumber(expected))
                    checks += ProbeQuotedNumbers(failures, label, set, typeof(TSet), expected, options);
            }
        }

        return checks;
    }

    // --- the declarations -------------------------------------------------------------------------

    /// <summary>Declared as the type itself: writes the wire form and reads back equal.</summary>
    private static int ProbeDeclared(List<string> failures, string label, object value, Type declared, string expected, JsonSerializerOptions options)
    {
        var where = $"{label} as {Display(declared)}";
        if (Write(failures, where, value, declared, options) is not { } json) return 1;
        if (json != expected)
        {
            failures.Add($"{where}: wrote {json}, expected {expected}");
            return 1;
        }

        ReadBack(failures, where, json, declared, value, options);
        return 2;
    }

    /// <summary>Declared as a package interface: writes the runtime type's wire form; a read is refused.</summary>
    private static int ProbeInterface(List<string> failures, string label, object value, Type declared, string expected, JsonSerializerOptions options)
    {
        var where = $"{label} as {Display(declared)}";
        if (Write(failures, where, value, declared, options) is not { } json) return 1;
        if (json != expected)
        {
            failures.Add($"{where}: wrote {json}, expected {expected}");
            return 1;
        }

        try
        {
            var back = JsonSerializer.Deserialize(json, declared, options);
            failures.Add($"{where}: a read was not refused; got {back ?? "null"} (docs: NotSupportedException, declare the concrete type)");
        }
        catch (NotSupportedException)
        {
            // The documented refusal.
        }
        catch (Exception e)
        {
            failures.Add($"{where}: a read threw {e.GetType().Name}, not the documented NotSupportedException: {e.Message}");
        }

        return 2;
    }

    /// <summary>Reached through <see langword="object"/> and inside a <c>List&lt;object&gt;</c>: the runtime type's wire form.</summary>
    private static int ProbeObject(List<string> failures, string label, object value, string expected, JsonSerializerOptions options)
    {
        var where = $"{label} as object";
        if (Write(failures, where, value, typeof(object), options) is { } json && json != expected)
            failures.Add($"{where}: wrote {json}, expected {expected}");

        // ReferenceHandler.Preserve wraps the list itself in {"$id","$values"}: that is the list's form, not the value's.
        if (Write(failures, $"{label} in a List<object>", new List<object> { value }, typeof(List<object>), options) is { } list && Unwrapped(list) != $"[{expected}]")
            failures.Add($"{label} in a List<object>: wrote {list}, expected [{expected}]");

        return 2;
    }

    /// <summary>A property of a record, typed as <paramref name="declared"/>: the property's value is the wire form, and the record reads back.</summary>
    private static int ProbeHolder(List<string> failures, string label, object value, Type declared, string expected, JsonSerializerOptions options)
    {
        var where      = $"{label} as a {Display(declared)} record property";
        var holderType = typeof(Holder<>).MakeGenericType(declared);
        var holder     = Activator.CreateInstance(holderType, value)!;

        if (Write(failures, where, holder, holderType, options) is not { } json) return 1;
        if (SingleValue(json) != expected)
        {
            failures.Add($"{where}: wrote {json}, expected the property to hold {expected}");
            return 1;
        }

        try
        {
            var back = JsonSerializer.Deserialize(json, holderType, options);
            var read = back is null ? null : holderType.GetProperty(nameof(Holder<object>.Value))!.GetValue(back);
            if (!value.Equals(read))
                failures.Add($"{where}: read back {read ?? "null"}, not the same value");
        }
        catch (Exception e)
        {
            failures.Add($"{where}: read threw {e.GetType().Name}: {e.Message}");
        }

        return 2;
    }

    /// <summary>A value of a <c>Dictionary&lt;string, T&gt;</c>: the entry's value is the wire form, and the dictionary reads back.</summary>
    private static int ProbeDictionaryValue(List<string> failures, string label, object value, Type declared, string expected, JsonSerializerOptions options)
    {
        var where          = $"{label} as a Dictionary<string, {Display(declared)}> value";
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), declared);
        var dictionary     = (IDictionary)Activator.CreateInstance(dictionaryType)!;
        dictionary.Add("k", value);

        if (Write(failures, where, dictionary, dictionaryType, options) is not { } json) return 1;
        if (SingleValue(json) != expected)
        {
            failures.Add($"{where}: wrote {json}, expected the entry to hold {expected}");
            return 1;
        }

        try
        {
            var back = (IDictionary?)JsonSerializer.Deserialize(json, dictionaryType, options);
            var read = back?["k"];
            if (!value.Equals(read))
                failures.Add($"{where}: read back {read ?? "null"}, not the same value");
        }
        catch (Exception e)
        {
            failures.Add($"{where}: read threw {e.GetType().Name}: {e.Message}");
        }

        return 2;
    }

    /// <summary>A null property writes <c>null</c> and reads back null, never the empty value (J18).</summary>
    private static int ProbeNull(List<string> failures, string label, Type declared, JsonSerializerOptions options)
    {
        var where      = $"{label} as a {Display(declared)} record property";
        var holderType = typeof(Holder<>).MakeGenericType(declared);
        var holder     = Activator.CreateInstance(holderType, [null])!;

        if (Write(failures, where, holder, holderType, options) is not { } json) return 1;
        if (SingleValue(json) != "null")
        {
            failures.Add($"{where}: wrote {json}, expected the property to hold null");
            return 1;
        }

        try
        {
            var back = JsonSerializer.Deserialize(json, holderType, options);
            var read = back is null ? null : holderType.GetProperty(nameof(Holder<object>.Value))!.GetValue(back);
            if (read is not null)
                failures.Add($"{where}: null read back as {read}");
        }
        catch (Exception e)
        {
            failures.Add($"{where}: read threw {e.GetType().Name}: {e.Message}");
        }

        return 2;
    }

    /// <summary>
    /// Under <c>AllowReadingFromString</c> every number in the array may arrive quoted; the set must
    /// read it to the same value, whichever arity it is.
    /// </summary>
    private static int ProbeQuotedNumbers(List<string> failures, string label, object value, Type declared, string expected, JsonSerializerOptions options)
    {
        var quoted = JsonNode.Parse(expected)!.AsArray();
        for (var i = 0; i < quoted.Count; i++)
        {
            if (quoted[i] is JsonValue number && number.GetValueKind() == JsonValueKind.Number)
                quoted[i] = JsonValue.Create(number.ToJsonString());
        }

        var json  = quoted.ToJsonString();
        var where = $"{label} as {Display(declared)} from quoted numbers {json}";
        ReadBack(failures, where, json, declared, value, options);
        return 1;
    }

    // --- plumbing ---------------------------------------------------------------------------------

    private static string? Write(List<string> failures, string where, object value, Type declared, JsonSerializerOptions options)
    {
        try
        {
            return JsonSerializer.Serialize(value, declared, options);
        }
        catch (Exception e)
        {
            failures.Add($"{where}: write threw {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static void ReadBack(List<string> failures, string where, string json, Type declared, object value, JsonSerializerOptions options)
    {
        try
        {
            var back = JsonSerializer.Deserialize(json, declared, options);
            if (!value.Equals(back))
                failures.Add($"{where}: read back {back ?? "null"}, not the same value");
        }
        catch (Exception e)
        {
            failures.Add($"{where}: read threw {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>The raw text of the one non-metadata property of a JSON object (<c>$id</c> under <c>ReferenceHandler.Preserve</c> is skipped).</summary>
    private static string SingleValue(string objectJson)
    {
        using var document = JsonDocument.Parse(objectJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return objectJson;
        var properties = document.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith('$')).ToList();
        return properties.Count == 1 ? properties[0].Value.GetRawText() : objectJson;
    }

    /// <summary>A collection under <c>ReferenceHandler.Preserve</c> is <c>{"$id":…,"$values":[…]}</c>; the array inside is what the elements wrote.</summary>
    private static string Unwrapped(string collectionJson)
    {
        using var document = JsonDocument.Parse(collectionJson);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("$values", out var values)
                   ? values.GetRawText()
                   : collectionJson;
    }

    private static int Count(string arrayJson)
    {
        using var document = JsonDocument.Parse(arrayJson);
        return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : -1;
    }

    private static bool HasNumber(string arrayJson)
    {
        using var document = JsonDocument.Parse(arrayJson);
        return document.RootElement.ValueKind == JsonValueKind.Array
            && document.RootElement.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Number);
    }

    /// <summary>
    /// The package's interfaces a value can be declared as: those in the core namespace a type
    /// implements, minus the factory interfaces, whose static abstract members rule them out.
    /// </summary>
    private static List<Type> DeclarableInterfacesOf(Type type)
        => [.. type.GetInterfaces()
                   .Where(i => i.Namespace == typeof(IRange<>).Namespace
                            && !i.GetMethods().Any(m => m is { IsStatic: true, IsAbstract: true }))];

    private static string Display(Type type)
    {
        if (type.IsGenericType)
            return $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>";
        return type.IsNested ? $"{Display(type.DeclaringType!)}.{type.Name}" : type.Name;
    }

    /// <summary>The type as C# source names it, for the attribute line a missing context entry needs.</summary>
    private static string CSharp(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(short)) return "short";
        if (type == typeof(decimal)) return "decimal";
        return Display(type);
    }

    /// <summary>A record with one property of the declared type: the shape a consumer's DTO has.</summary>
    public sealed record Holder<T>(T? Value);
}
