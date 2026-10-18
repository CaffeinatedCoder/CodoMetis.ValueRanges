using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.Extensions.Configuration;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Route B, step 2 (#105, #124, <c>test/oracles/axes.md</c>): every discovered range, multirange and
/// value set type × every entry point that needs no host × the current culture. The oracle is the
/// document's promise that the literal is a wire format with no culture (J04, J05, J13, J16): whatever
/// culture the process runs under, a literal reads to the value the invariant culture reads, and a value
/// writes the invariant literal and the invariant JSON.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LiteralCultureContractTests"/> varies the culture for <c>ToString</c> only, and
/// <see cref="LiteralProviderContractTests"/> varies the provider handed to <c>Parse</c> with the current
/// culture left alone. Neither sees an entry point that reaches for <see cref="CultureInfo.CurrentCulture"/>
/// itself — a <c>Parse</c> with a null provider, a <see cref="TypeConverter"/> handed a null culture,
/// configuration binding, a JSON converter. This class keeps the product: twelve read and five write entry
/// points, through the union and the sealed variant, under four cultures that each break a different
/// assumption (comma decimal separator, U+2212 minus, a Persian and a Buddhist calendar). The host entry
/// points (MVC, minimal APIs, <c>RequestLocalization</c>) are <c>HostCultureProbeTests</c> in the
/// AspNetCore test project.
/// </para>
/// <para>
/// The reference is computed under the invariant culture before any culture is switched, so a machine
/// whose own culture is <c>de-DE</c> does not move it. One <c>[DynamicData]</c> row per type; each failure
/// line starts with the row label and names the entry point and declaration before the value and culture,
/// so a candidate row (<c>test/oracles/candidates.json</c>) covers one entry point of one type.
/// </para>
/// </remarks>
[TestClass]
public sealed class CultureEntryPointProbeTests
{
    /// <summary>The culture axis: comma decimal separator; U+2212 minus; Persian calendar and Arabic separators; Buddhist calendar.</summary>
    private static readonly string[] Cultures = ["de-DE", "sv-SE", "fa-IR", "th-TH"];

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
                yield return [new ProbeRow(rangeType.Name, failures => Reflect.InvokeGeneric<int>(typeof(CultureEntryPointProbeTests), nameof(SweepRange), [rangeType, elementType], failures))];
                yield return [new ProbeRow($"RangeSet<{rangeType.Name}>", failures => Reflect.InvokeGeneric<int>(typeof(CultureEntryPointProbeTests), nameof(SweepRangeSet), [rangeType, elementType], failures))];
            }

            foreach (var (setType, elementType) in SetProbes.AllSetTypes())
                yield return [new ProbeRow(Display(setType), failures => Reflect.InvokeGeneric<int>(typeof(CultureEntryPointProbeTests), nameof(SweepSet), [setType, elementType], failures))];
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

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void EveryEntryPoint_ReadsAndWritesTheInvariantLiteral_UnderEveryCurrentCulture(ProbeRow row)
    {
        var failures = new List<string>();
        var checks   = row.Sweep(failures);

        // Four cultures × at least two values × at least twelve entry points.
        Assert.IsTrue(checks >= 96, $"{row}: only {checks} checks ran.");

        Candidates.Verdict(nameof(CultureEntryPointProbeTests), failures,
            $"{row}: of {checks} checks, these disagree with the invariant literal", rowPrefix: row.Label + " ");
    }

    // --- the types --------------------------------------------------------------------------------

    private static int SweepRange<TRange, T>(List<string> failures)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var checks = 0;
        foreach (var range in RangeProbes.Shapes<TRange, T>())
            checks += Probe<TRange>(failures, typeof(TRange).Name, range, [typeof(TRange), range.GetType()], asKey: true);
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
            RangeSet<TRange, T>.From([shapes[4], TRange.CreateUnboundedEnd((T)RangeProbes.Bounds[typeof(T)].High, startInclusive: false)])
        ];

        var checks = 0;
        foreach (var set in sets)
            checks += Probe<RangeSet<TRange, T>>(failures, $"RangeSet<{typeof(TRange).Name}>", set, [typeof(RangeSet<TRange, T>)], asKey: false);
        return checks;
    }

    private static int SweepSet<TSet, T>(List<string> failures)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var checks = 0;
        foreach (var set in new[] { TSet.Empty, TSet.From(SetProbes.For<T>()) })
            checks += Probe<TSet>(failures, Display(typeof(TSet)), set, [typeof(TSet)], asKey: false);
        return checks;
    }

    // --- the entry points -------------------------------------------------------------------------

    /// <summary>
    /// Every entry point for one value under every culture. <typeparamref name="TValue"/> is the type
    /// that declares <c>Parse</c> (the union, the multirange, the set); <paramref name="declarations"/>
    /// adds the sealed variant for a range, which reaches the literal only through its
    /// <see cref="TypeConverter"/>, configuration and JSON.
    /// </summary>
    private static int Probe<TValue>(List<string> failures, string row, object value, Type[] declarations, bool asKey)
        where TValue : ISpanParsable<TValue>, IFormattable
    {
        // The reference: what the invariant culture reads and writes, computed before any switch.
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        string literal;
        Dictionary<Type, string> json;
        string? keyJson = null;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            literal = ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            json    = declarations.ToDictionary(declared => declared, declared => JsonSerializer.Serialize(value, declared));
            if (asKey) keyJson = JsonSerializer.Serialize(KeyedBy(value), KeyedType(typeof(TValue)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        var checks = 0;
        foreach (var name in Cultures)
        {
            var culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentCulture   = culture;
            CultureInfo.CurrentUICulture = culture;
            try
            {
                var at = $"under {name} for {literal}";
                var parsable = typeof(TValue);

                // Read: the static parsers, with no provider and with the current culture handed over.
                checks += Read(failures, row, "Parse(string, null)", parsable, at, value, () => StringOverloads.Parse<TValue>(literal, null));
                checks += Read(failures, row, "Parse(string, CurrentCulture)", parsable, at, value, () => StringOverloads.Parse<TValue>(literal, CultureInfo.CurrentCulture));
                checks += Read(failures, row, "Parse(span, null)", parsable, at, value, () => TValue.Parse(literal.AsSpan(), null));
                checks += Read(failures, row, "TryParse(string, null)", parsable, at, value,
                               () => StringOverloads.TryParse<TValue>(literal, null, out var result) ? result : throw new FormatException("TryParse returned false"));
                checks += Read(failures, row, "TryParse(span, CurrentCulture)", parsable, at, value,
                               () => TValue.TryParse(literal.AsSpan(), CultureInfo.CurrentCulture, out var result) ? result : throw new FormatException("TryParse returned false"));

                foreach (var declared in declarations)
                {
                    var converter = TypeDescriptor.GetConverter(declared);

                    // Read: TypeDescriptor with a null culture (it means the current one) and the current one handed over.
                    checks += Read(failures, row, "TypeConverter.ConvertFromString", declared, at, value, () => converter.ConvertFromString(literal));
                    checks += Read(failures, row, "TypeConverter.ConvertFrom(CurrentCulture)", declared, at, value,
                                   () => converter.ConvertFrom(null, CultureInfo.CurrentCulture, literal));

                    // Read: Microsoft.Extensions.Configuration, a property of the declared type.
                    checks += Read(failures, row, "IConfiguration binding", declared, at, value, () => Bind(declared, literal));

                    // Read: System.Text.Json, the invariant JSON back.
                    checks += Read(failures, row, "JsonSerializer.Deserialize", declared, at, value,
                                   () => JsonSerializer.Deserialize(json[declared], declared));

                    // Write: the literal through TypeDescriptor, the JSON through the serializer.
                    checks += Write(failures, row, "TypeConverter.ConvertToString", declared, at, literal, () => converter.ConvertToString(value));
                    checks += Write(failures, row, "TypeConverter.ConvertTo(CurrentCulture)", declared, at, literal,
                                    () => (string?)converter.ConvertTo(null, CultureInfo.CurrentCulture, value, typeof(string)));
                    checks += Write(failures, row, "JsonSerializer.Serialize", declared, at, json[declared], () => JsonSerializer.Serialize(value, declared));
                }

                // Write: what interpolation, string.Format and a logging template call.
                checks += Write(failures, row, "interpolation", parsable, at, literal, () => $"{value}");
                checks += Write(failures, row, "string.Format(CurrentCulture)", parsable, at, literal, () => string.Format(CultureInfo.CurrentCulture, "{0}", value));

                if (keyJson is not null)
                {
                    // A range as a dictionary key: the property name is the literal, both ways.
                    var keyed = KeyedType(parsable);
                    checks += Write(failures, row, "JSON dictionary key write", parsable, at, keyJson, () => JsonSerializer.Serialize(KeyedBy(value), keyed));
                    checks += Read(failures, row, "JSON dictionary key read", parsable, at, value,
                                   () => ((IDictionary?)JsonSerializer.Deserialize(keyJson, keyed))?.Keys.Cast<object>().Single());
                }
            }
            finally
            {
                CultureInfo.CurrentCulture   = original;
                CultureInfo.CurrentUICulture = originalUi;
            }
        }

        return checks;
    }

    /// <summary>A read leg: the entry point returns a value equal to the reference, of the declared type, or the line says what it did instead.</summary>
    private static int Read(List<string> failures, string row, string entryPoint, Type declared, string at, object expected, Func<object?> read)
    {
        var where = $"{row} {entryPoint} as {Display(declared)}";
        try
        {
            var actual = read();
            if (actual is null)
                failures.Add($"{where}: read null {at}");
            else if (!expected.Equals(actual))
                failures.Add($"{where}: read {Literal(actual)}, not the invariant value, {at}");
            else if (!declared.IsInstanceOfType(actual))
                failures.Add($"{where}: read a {actual.GetType().Name}, not a {Display(declared)}, {at}");
        }
        catch (Exception e)
        {
            failures.Add($"{where}: threw {Unwrap(e).GetType().Name} {at}: {Unwrap(e).Message}");
        }

        return 1;
    }

    /// <summary>A write leg: the entry point returns exactly the invariant text, or the line says what it wrote instead.</summary>
    private static int Write(List<string> failures, string row, string entryPoint, Type declared, string at, string expected, Func<string?> write)
    {
        var where = $"{row} {entryPoint} as {Display(declared)}";
        try
        {
            var actual = write();
            if (actual != expected)
                failures.Add($"{where}: wrote {actual ?? "null"}, expected {expected}, {at}");
        }
        catch (Exception e)
        {
            failures.Add($"{where}: threw {Unwrap(e).GetType().Name} {at}: {Unwrap(e).Message}");
        }

        return 1;
    }

    // --- plumbing ---------------------------------------------------------------------------------

    /// <summary>Binds <c>Value</c> of a <see cref="Settings{T}"/> closed over the declared type from an in-memory source.</summary>
    private static object? Bind(Type declared, string literal)
    {
        var settingsType = typeof(Settings<>).MakeGenericType(declared);
        var settings = new ConfigurationBuilder()
                      .AddInMemoryCollection([new KeyValuePair<string, string?>("Value", literal)])
                      .Build()
                      .Get(settingsType);
        return settings is null ? null : settingsType.GetProperty(nameof(Settings<object>.Value))!.GetValue(settings);
    }

    private static Type KeyedType(Type key) => typeof(Dictionary<,>).MakeGenericType(key, typeof(int));

    private static object KeyedBy(object key)
    {
        var dictionary = (IDictionary)Activator.CreateInstance(KeyedType(KeyTypeOf(key)))!;
        dictionary.Add(key, 1);
        return dictionary;
    }

    /// <summary>The union a variant belongs to: the dictionary is keyed by the union, as a consumer declares it.</summary>
    private static Type KeyTypeOf(object key) => key.GetType().IsNested ? key.GetType().BaseType! : key.GetType();

    private static string Literal(object value) => value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? "";

    private static Exception Unwrap(Exception e) => e is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : e;

    private static string Display(Type type)
    {
        if (type.IsGenericType)
            return $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>";
        return type.IsNested ? $"{Display(type.DeclaringType!)}.{type.Name}" : type.Name;
    }

    /// <summary>The options class a consumer binds: one settable property of the declared type.</summary>
    public sealed class Settings<T>
    {
        public T? Value { get; set; }
    }
}
