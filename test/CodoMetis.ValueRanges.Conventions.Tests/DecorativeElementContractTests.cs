using System.Globalization;
using System.Text.Json;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// <c>docs/value-sets.md#the-text-form-contract</c>: a wrapper element whose text form is decorated
/// (<c>"CUST-{value}"</c>) rather than the backing primitive's "fails loudly at the persistence
/// boundary with an error naming the contract". Each boundary a set crosses on its way out — the JSON
/// write, the SQL literal and the bound parameter — is asked to write a set holding one decorated
/// element, and must refuse with a message that names the text form.
/// </summary>
/// <remarks>
/// <para>
/// The integer arity is the reference: its bridge parses the element's text as an <c>int</c>, so
/// <c>ORD-123</c> cannot get through. The string arity is the one the sentence is hardest to keep
/// for, because every text is a valid <c>string</c> — only a round trip through the element's own
/// <c>Parse</c> can tell <c>CUST-123</c> from content, and a decorated element that its <c>Parse</c>
/// refuses is written without complaint and fails only when the row is read back, with the
/// element's message rather than the contract's.
/// </para>
/// <para>
/// The temporal and NodaTime arities are held for the same sentence by <c>SqlLiteralTests</c>,
/// <c>SetElementBridgeTests</c> and <c>SetJsonConverterTests</c>, with elements that swallow the
/// format argument.
/// </para>
/// </remarks>
[TestClass]
public sealed class DecorativeElementContractTests
{
    private sealed class BclContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseNpgsql("Host=localhost;Database=conventions;Username=postgres", npgsql => npgsql.UseValueRanges());
    }

    private const string Header = "A decorated wrapper element was written instead of refused naming the text-form contract";

    /// <summary>
    /// The reference arity, asserted on its own rather than through the candidate registry: a test
    /// whose message carries a known candidate's label is read as that candidate by the seed check,
    /// so a seeded defect here must not share a message with the string arity's rows.
    /// </summary>
    [TestMethod]
    public void IntegerArity_RefusesADecoratedElement_AtEveryWriteBoundary()
    {
        var failures = new List<string>();

        Sweep(failures, Int32Set<DecoratedNumber>.From(DecoratedNumber.Parse("123", CultureInfo.InvariantCulture)));

        Assert.AreEqual(0, failures.Count, $"{Header}:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [TestMethod]
    public void StringArity_RefusesADecoratedElement_AtEveryWriteBoundary()
    {
        var failures = new List<string>();

        Sweep(failures, StringSet<DecoratedText>.From(DecoratedText.Parse("123", CultureInfo.InvariantCulture)));

        Candidates.Verdict(nameof(DecorativeElementContractTests), failures, Header);
    }

    private static void Sweep<TSet>(List<string> failures, TSet set) where TSet : notnull
    {
        using var context = new BclContext();
        var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(TSet))
                   ?? throw new InvalidOperationException($"{Name(typeof(TSet))} is not mapped.");

        Boundary("JSON write",      () => JsonSerializer.Serialize(set));
        Boundary("SQL literal",     () => mapping.GenerateSqlLiteral(set));
        Boundary("bound parameter", () => JsonSerializer.Serialize(mapping.Converter!.ConvertToProvider(set)));

        void Boundary(string boundary, Func<string> write)
        {
            try
            {
                failures.Add($"  {Name(typeof(TSet))}: {boundary} accepted the decorated element → {write()}");
            }
            catch (Exception refusal) when (!refusal.Message.Contains("text form", StringComparison.Ordinal))
            {
                failures.Add($"  {Name(typeof(TSet))}: {boundary} refused without naming the text-form contract → "
                           + $"{refusal.GetType().Name}: {refusal.Message}");
            }
            catch (Exception)
            {
                // Refused, naming the contract: the documented behaviour.
            }
        }
    }

    private static string Name(Type type) =>
        type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(",", type.GetGenericArguments().Select(Name))}>" : type.Name;

    /// <summary>A text key that decorates its value when formatted, and validates the raw value when parsed.</summary>
    public readonly record struct DecoratedText : IFormattable, IParsable<DecoratedText>
    {
        private readonly string _value;

        private DecoratedText(string value) => _value = value;

        public static DecoratedText Parse(string s, IFormatProvider? provider)
            => s.Length > 0 && s.All(char.IsAsciiDigit) ? new(s) : throw new FormatException($"'{s}' is not a customer number.");

        public static bool TryParse(string? s, IFormatProvider? provider, out DecoratedText result)
        {
            var ok = s is { Length: > 0 } && s.All(char.IsAsciiDigit);
            result = ok ? new(s!) : default;
            return ok;
        }

        public string ToString(string? format, IFormatProvider? formatProvider) => $"CUST-{_value}";

        public override string ToString() => ToString(null, null);
    }

    /// <summary>The integer counterpart: decorates when formatted, parses the raw integer.</summary>
    public readonly record struct DecoratedNumber : IFormattable, IParsable<DecoratedNumber>, IComparable<DecoratedNumber>
    {
        private readonly int _value;

        private DecoratedNumber(int value) => _value = value;

        public static DecoratedNumber Parse(string s, IFormatProvider? provider) => new(int.Parse(s, provider));

        public static bool TryParse(string? s, IFormatProvider? provider, out DecoratedNumber result)
        {
            var ok = int.TryParse(s, provider, out var value);
            result = new(value);
            return ok;
        }

        public int CompareTo(DecoratedNumber other) => _value.CompareTo(other._value);

        public string ToString(string? format, IFormatProvider? formatProvider) => $"ORD-{_value}";

        public override string ToString() => ToString(null, null);
    }
}
