using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The representative element type each wrapper family is exercised with. Shared, because two
/// suites need it for the same reason: a family is only reachable once it is closed over
/// something, and both the value semantics and the EF mapping have to be checked on the closed
/// type. Unknown families throw rather than being skipped — a silently uncovered family is the
/// failure mode these discovery-driven tests exist to avoid.
/// </summary>
internal static class WrapperElements
{
    internal static Type For(Type family) => family.Name switch
    {
        "StringSet`1"         => typeof(TextKey),
        "GuidSet`1"           => typeof(TenantId),
        "Int16Set`1"          => typeof(TinyCode),
        "Int32Set`1"          => typeof(SmallCode),
        "Int64Set`1"          => typeof(LargeCode),
        "DecimalSet`1"        => typeof(Money),
        "DateSet`1"           => typeof(BusinessDate),
        "TimeSet`1"           => typeof(ShiftTime),
        "DateTimeSet`1"       => typeof(AuditStamp),
        "DateTimeOffsetSet`1" => typeof(EventStamp),
        "LocalDateSet`1"      => typeof(CalendarDay),
        "LocalDateTimeSet`1"  => typeof(WallClockStamp),
        "InstantSet`1"        => typeof(EventInstant),
        "LocalTimeSet`1"      => typeof(OpeningTime),
        "YearMonthSet`1"      => typeof(BillingMonth),
        _                     => throw new InvalidOperationException(
            $"No representative wrapper element type registered for the set family '{family.Name}'. "
          + "Add one to WrapperElements.For (and probes for it in ValueSetContractTests) so the new "
          + "family is covered.")
    };
}

// Representative validated wrapper elements — the shape Vogen, Metalama and StronglyTypedId
// generate, and the one the wrapper set arities exist for. Each satisfies the family's
// constraint (struct, IEquatable, IComparable where required, IFormattable, IParsable) and
// upholds the contract constraints cannot express: the invariant text form is exactly the
// backing primitive's text form.
//
// TextKey validates and trims in Parse, and — deliberately — implements IComparable with a
// culture-sensitive comparison, which is what the generators actually emit and what
// StringSet<TElement>'s ordinal CanonicalComparer exists to override. An ordinal CompareTo
// here would make the type agree with the canonical order by accident and hide a real defect:
// with one, removing StringSet's CanonicalOrder override left the contract tests passing.

public readonly record struct TextKey : IFormattable, IParsable<TextKey>, IComparable<TextKey>
{
    private readonly string? _value;

    private TextKey(string value) => _value = value;

    public static TextKey Parse(string s, IFormatProvider? provider)
        => string.IsNullOrWhiteSpace(s)
               ? throw new FormatException($"'{s}' is not a valid key.")
               : new TextKey(s.Trim());

    public static bool TryParse(string? s, IFormatProvider? provider, out TextKey result)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            result = default;
            return false;
        }

        result = new TextKey(s.Trim());
        return true;
    }

    public int CompareTo(TextKey other) => string.Compare(_value, other._value, StringComparison.CurrentCulture);

    public string ToString(string? format, IFormatProvider? formatProvider) => _value ?? "";

    public override string ToString() => _value ?? "";
}

public readonly record struct TenantId : IFormattable, IParsable<TenantId>, IComparable<TenantId>
{
    private readonly Guid _value;

    private TenantId(Guid value) => _value = value;

    public static TenantId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out TenantId result)
    {
        var parsed = Guid.TryParse(s, out var value);
        result = parsed ? new TenantId(value) : default;
        return parsed;
    }

    public int CompareTo(TenantId other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider);

    public override string ToString() => _value.ToString();
}

public readonly record struct SmallCode : IFormattable, IParsable<SmallCode>, IComparable<SmallCode>
{
    private readonly int _value;

    private SmallCode(int value) => _value = value;

    public static SmallCode Parse(string s, IFormatProvider? provider)
        => new(int.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out SmallCode result)
    {
        var parsed = int.TryParse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new SmallCode(value) : default;
        return parsed;
    }

    public int CompareTo(SmallCode other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct LargeCode : IFormattable, IParsable<LargeCode>, IComparable<LargeCode>
{
    private readonly long _value;

    private LargeCode(long value) => _value = value;

    public static LargeCode Parse(string s, IFormatProvider? provider)
        => new(long.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out LargeCode result)
    {
        var parsed = long.TryParse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new LargeCode(value) : default;
        return parsed;
    }

    public int CompareTo(LargeCode other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct TinyCode : IFormattable, IParsable<TinyCode>, IComparable<TinyCode>
{
    private readonly short _value;

    private TinyCode(short value) => _value = value;

    public static TinyCode Parse(string s, IFormatProvider? provider)
        => new(short.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TinyCode result)
    {
        var parsed = short.TryParse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new TinyCode(value) : default;
        return parsed;
    }

    public int CompareTo(TinyCode other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

// Money keeps its scale: 12.50 stays 12.50 through Parse and ToString, which is what makes the
// decimal element converter's "same text form as the primitive" claim testable.
public readonly record struct Money : IFormattable, IParsable<Money>, IComparable<Money>
{
    private readonly decimal _value;

    private Money(decimal value) => _value = value;

    public static Money Parse(string s, IFormatProvider? provider)
        => new(decimal.Parse(s, NumberStyles.Number, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out Money result)
    {
        var parsed = decimal.TryParse(s, NumberStyles.Number, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new Money(value) : default;
        return parsed;
    }

    public int CompareTo(Money other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

// The temporal wrappers forward the format specifier rather than pinning one of their own —
// the shape the generators emit, and the shape the temporal arities' contract requires. A
// wrapper that swallowed the argument would be the defect those families exist to reject.

public readonly record struct BusinessDate : IFormattable, IParsable<BusinessDate>, IComparable<BusinessDate>
{
    private readonly DateOnly _value;

    private BusinessDate(DateOnly value) => _value = value;

    public static BusinessDate Parse(string s, IFormatProvider? provider)
        => new(DateOnly.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out BusinessDate result)
    {
        var parsed = DateOnly.TryParse(s, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new BusinessDate(value) : default;
        return parsed;
    }

    public int CompareTo(BusinessDate other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public readonly record struct ShiftTime : IFormattable, IParsable<ShiftTime>, IComparable<ShiftTime>
{
    private readonly TimeOnly _value;

    private ShiftTime(TimeOnly value) => _value = value;

    public static ShiftTime Parse(string s, IFormatProvider? provider)
        => new(TimeOnly.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out ShiftTime result)
    {
        var parsed = TimeOnly.TryParse(s, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new ShiftTime(value) : default;
        return parsed;
    }

    public int CompareTo(ShiftTime other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString("O", CultureInfo.InvariantCulture);
}

public readonly record struct AuditStamp : IFormattable, IParsable<AuditStamp>, IComparable<AuditStamp>
{
    private readonly DateTime _value;

    private AuditStamp(DateTime value) => _value = value;

    // RoundtripKind, so a "…Z" payload comes back as UTC rather than being shifted to local.
    public static AuditStamp Parse(string s, IFormatProvider? provider)
        => new(DateTime.Parse(s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    public static bool TryParse(string? s, IFormatProvider? provider, out AuditStamp result)
    {
        var parsed = DateTime.TryParse(
            s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value);
        result = parsed ? new AuditStamp(value) : default;
        return parsed;
    }

    public int CompareTo(AuditStamp other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString("O", CultureInfo.InvariantCulture);
}

public readonly record struct EventStamp : IFormattable, IParsable<EventStamp>, IComparable<EventStamp>
{
    private readonly DateTimeOffset _value;

    private EventStamp(DateTimeOffset value) => _value = value;

    public static EventStamp Parse(string s, IFormatProvider? provider)
        => new(DateTimeOffset.Parse(s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));

    public static bool TryParse(string? s, IFormatProvider? provider, out EventStamp result)
    {
        var parsed = DateTimeOffset.TryParse(
            s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value);
        result = parsed ? new EventStamp(value) : default;
        return parsed;
    }

    public int CompareTo(EventStamp other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString("O", CultureInfo.InvariantCulture);
}

// The NodaTime wrappers parse ISO explicitly. NodaTime's own types have no IParsable, and their
// null-format ToString is the culture's form — the reason those arities pin a pattern.

public readonly record struct CalendarDay : IFormattable, IParsable<CalendarDay>, IComparable<CalendarDay>
{
    private readonly LocalDate _value;

    private CalendarDay(LocalDate value) => _value = value;

    public static CalendarDay Parse(string s, IFormatProvider? provider)
        => new(LocalDatePattern.Iso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out CalendarDay result)
    {
        var parsed = s is not null && LocalDatePattern.Iso.Parse(s).Success;
        result = parsed ? new CalendarDay(LocalDatePattern.Iso.Parse(s!).Value) : default;
        return parsed;
    }

    public int CompareTo(CalendarDay other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => LocalDatePattern.Iso.Format(_value);
}

public readonly record struct WallClockStamp
    : IFormattable, IParsable<WallClockStamp>, IComparable<WallClockStamp>
{
    private readonly LocalDateTime _value;

    private WallClockStamp(LocalDateTime value) => _value = value;

    public static WallClockStamp Parse(string s, IFormatProvider? provider)
        => new(LocalDateTimePattern.ExtendedIso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out WallClockStamp result)
    {
        var parsed = s is not null && LocalDateTimePattern.ExtendedIso.Parse(s).Success;
        result = parsed ? new WallClockStamp(LocalDateTimePattern.ExtendedIso.Parse(s!).Value) : default;
        return parsed;
    }

    public int CompareTo(WallClockStamp other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => LocalDateTimePattern.ExtendedIso.Format(_value);
}

public readonly record struct EventInstant : IFormattable, IParsable<EventInstant>, IComparable<EventInstant>
{
    private readonly Instant _value;

    private EventInstant(Instant value) => _value = value;

    public static EventInstant Parse(string s, IFormatProvider? provider)
        => new(InstantPattern.ExtendedIso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out EventInstant result)
    {
        var parsed = s is not null && InstantPattern.ExtendedIso.Parse(s).Success;
        result = parsed ? new EventInstant(InstantPattern.ExtendedIso.Parse(s!).Value) : default;
        return parsed;
    }

    public int CompareTo(EventInstant other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => InstantPattern.ExtendedIso.Format(_value);
}

public readonly record struct OpeningTime : IFormattable, IParsable<OpeningTime>, IComparable<OpeningTime>
{
    private readonly LocalTime _value;

    private OpeningTime(LocalTime value) => _value = value;

    public static OpeningTime Parse(string s, IFormatProvider? provider)
        => new(LocalTimePattern.ExtendedIso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out OpeningTime result)
    {
        var parsed = s is not null && LocalTimePattern.ExtendedIso.Parse(s).Success;
        result = parsed ? new OpeningTime(LocalTimePattern.ExtendedIso.Parse(s!).Value) : default;
        return parsed;
    }

    public int CompareTo(OpeningTime other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => LocalTimePattern.ExtendedIso.Format(_value);
}

public readonly record struct BillingMonth : IFormattable, IParsable<BillingMonth>, IComparable<BillingMonth>
{
    private readonly YearMonth _value;

    private BillingMonth(YearMonth value) => _value = value;

    public static BillingMonth Parse(string s, IFormatProvider? provider)
        => new(YearMonthPattern.Iso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out BillingMonth result)
    {
        var parsed = s is not null && YearMonthPattern.Iso.Parse(s).Success;
        result = parsed ? new BillingMonth(YearMonthPattern.Iso.Parse(s!).Value) : default;
        return parsed;
    }

    public int CompareTo(BillingMonth other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => YearMonthPattern.Iso.Format(_value);
}
