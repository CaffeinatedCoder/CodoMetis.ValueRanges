using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// A wrapper element that adds nothing to the primitive it wraps: it parses exactly what the primitive
/// parses, formats exactly as the primitive formats, and compares as the primitive compares. A wrapper
/// arity closed over it must therefore answer every question exactly as its primitive set does — which
/// is what <see cref="SetWrapperParityTests"/> asserts, with no ground truth needed.
/// </summary>
/// <remarks>
/// The representative wrappers in <see cref="WrapperElements"/> are deliberately <em>not</em> identities
/// (<see cref="TextKey"/> trims and compares by culture, <see cref="Money"/> narrows the grammar), because
/// they exist to prove that a family overrides what its element gets wrong. A parity sweep needs the
/// opposite: an element that gets nothing wrong, so every disagreement is the family's.
/// </remarks>
public interface IIdentity<TSelf, TPrimitive>
    where TSelf : IIdentity<TSelf, TPrimitive>
{
    /// <summary>The wrapped primitive.</summary>
    TPrimitive Value { get; }

    /// <summary>Wraps <paramref name="value"/> unchanged.</summary>
    static abstract TSelf Wrap(TPrimitive value);
}

/// <summary>
/// The identity wrapper over a struct primitive: every BCL element type of a value set family and the
/// five NodaTime ones. Parsing reads the round-trip form each family hands its element, without the
/// shifts a bare <c>Parse</c> would apply (a <c>DateTime</c> read with <see cref="DateTimeStyles.RoundtripKind"/>,
/// a NodaTime value with its ISO pattern).
/// </summary>
public readonly record struct Same<T> : IIdentity<Same<T>, T>, IFormattable, IParsable<Same<T>>, IComparable<Same<T>>
    where T : struct, IEquatable<T>, IComparable<T>, IFormattable
{
    private static readonly Func<string, T> s_parse = IdentityParse.For<T>();

    private Same(T value) => Value = value;

    /// <inheritdoc />
    public T Value { get; }

    /// <inheritdoc />
    public static Same<T> Wrap(T value) => new(value);

    public static Same<T> Parse(string s, IFormatProvider? provider) => new(s_parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out Same<T> result)
    {
        try
        {
            result = new(s_parse(s!));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or UnparsableValueException)
        {
            result = default;
            return false;
        }
    }

    public int CompareTo(Same<T> other) => Value.CompareTo(other.Value);

    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => Value.ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// The identity wrapper over <see cref="string"/>: the text is the value, untrimmed and unvalidated, and
/// the comparison is <see cref="string"/>'s own (culture-sensitive) one — which the string family must
/// override with ordinal exactly as <c>StringSet</c> sorts.
/// </summary>
public readonly record struct SameText : IIdentity<SameText, string>, IFormattable, IParsable<SameText>, IComparable<SameText>
{
    private readonly string? _value;

    private SameText(string value) => _value = value;

    /// <inheritdoc />
    public string Value => _value ?? "";

    /// <inheritdoc />
    public static SameText Wrap(string value) => new(value);

    public static SameText Parse(string s, IFormatProvider? provider) => new(s ?? throw new ArgumentNullException(nameof(s)));

    public static bool TryParse(string? s, IFormatProvider? provider, out SameText result)
    {
        result = s is null ? default : new(s);
        return s is not null;
    }

    public int CompareTo(SameText other) => string.Compare(Value, other.Value, StringComparison.CurrentCulture);

    public string ToString(string? format, IFormatProvider? formatProvider) => Value;

    public override string ToString() => Value;
}

/// <summary>The identity wrapper for each primitive element type; unknown ones throw so a new family cannot go unswept.</summary>
internal static class IdentityWrappers
{
    internal static Type For(Type primitive) =>
        primitive == typeof(string)
            ? typeof(SameText)
            : IdentityParse.Supports(primitive)
                ? typeof(Same<>).MakeGenericType(primitive)
                : throw new InvalidOperationException(
                      $"No identity wrapper for the element type '{primitive.Name}'. Add its parse to IdentityParse so the "
                    + "new family is covered by SetWrapperParityTests.");
}

/// <summary>The exact inverse of each primitive's invariant text form.</summary>
internal static class IdentityParse
{
    private static readonly Dictionary<Type, Delegate> s_parsers = new()
    {
        [typeof(short)]          = (Func<string, short>)(s => short.Parse(s, CultureInfo.InvariantCulture)),
        [typeof(int)]            = (Func<string, int>)(s => int.Parse(s, CultureInfo.InvariantCulture)),
        [typeof(long)]           = (Func<string, long>)(s => long.Parse(s, CultureInfo.InvariantCulture)),
        [typeof(decimal)]        = (Func<string, decimal>)(s => decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture)),
        [typeof(Guid)]           = (Func<string, Guid>)(s => Guid.Parse(s, CultureInfo.InvariantCulture)),
        [typeof(DateOnly)]       = (Func<string, DateOnly>)(s => DateOnly.Parse(s, CultureInfo.InvariantCulture)),
        [typeof(TimeOnly)]       = (Func<string, TimeOnly>)(s => TimeOnly.Parse(s, CultureInfo.InvariantCulture)),
        [typeof(DateTime)]       = (Func<string, DateTime>)(s => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
        [typeof(DateTimeOffset)] = (Func<string, DateTimeOffset>)(s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
        [typeof(LocalDate)]      = (Func<string, LocalDate>)(s => LocalDatePattern.Iso.Parse(s).GetValueOrThrow()),
        [typeof(LocalDateTime)]  = (Func<string, LocalDateTime>)(s => LocalDateTimePattern.ExtendedIso.Parse(s).GetValueOrThrow()),
        [typeof(Instant)]        = (Func<string, Instant>)(s => InstantPattern.ExtendedIso.Parse(s).GetValueOrThrow()),
        [typeof(LocalTime)]      = (Func<string, LocalTime>)(s => LocalTimePattern.ExtendedIso.Parse(s).GetValueOrThrow()),
        [typeof(YearMonth)]      = (Func<string, YearMonth>)(s => YearMonthPattern.Iso.Parse(s).GetValueOrThrow())
    };

    internal static bool Supports(Type primitive) => s_parsers.ContainsKey(primitive);

    internal static Func<string, T> For<T>() =>
        s_parsers.TryGetValue(typeof(T), out var parse)
            ? (Func<string, T>)parse
            : throw new InvalidOperationException($"No identity parse for '{typeof(T).Name}'.");
}
