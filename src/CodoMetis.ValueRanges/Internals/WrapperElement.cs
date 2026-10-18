using System.Globalization;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// The contract a wrapper arity's constraints cannot express, checked: an element's invariant
/// text form must read back, through the family's own element parser, as the same element. The
/// text form is what the array literal, JSON and the column hold, so an element that writes one
/// value and reads another is refused instead of stored — on the way out (<see cref="Formatted{TElement}"/>,
/// from every wrapper's <c>FormatValue</c>) and on the way in (<see cref="Parsed{TElement}"/>, from
/// its <c>ParseValue</c>), which also catches data written before the check existed.
/// </summary>
/// <remarks>
/// <para>
/// Until 10.0.0 nothing checked it (#53). <c>Int32Set&lt;T&gt;</c> over an element whose
/// <c>ToString</c> gave <c>V + 1</c> wrote <c>{124}</c> for 123 and read it back as 124;
/// <c>StringSet&lt;T&gt;</c> over an element that decorates on write and keeps any text on read
/// wrote <c>{CUST-123}</c> and read back <c>CUST-CUST-123</c>, growing on every round trip. Neither
/// threw. The numeric and temporal arities only refused decoration their primitive grammar could
/// not parse, which a wrong number passes.
/// </para>
/// <para>
/// The check is on the <em>element</em>, not the text: <c>Parse(Format(x))</c> must equal
/// <c>x</c>. A text-level check (<c>Format(Parse(text))</c> equal to <c>text</c>) would refuse an
/// element that normalizes on parse — one that trims, or lowercases, as the documented
/// <c>AccessRight</c> does — although it stores and reads a single value. On the read leg the
/// element is what the text parsed to, so a lenient parse that reads a decorated text as content
/// still fails: it formats as something else.
/// </para>
/// <para>
/// <c>parse</c> is the family's unchecked element parser (its grammar, then the element's
/// <c>Parse</c>), never the checked <c>ParseValue</c>: a mismatching element would otherwise be
/// re-checked without end. Writing throws <see cref="InvalidOperationException"/>, as the EF Core
/// bridge does for the same contract; reading throws <see cref="FormatException"/>, as any refused
/// element text does, so <c>TryParse</c> answers <see langword="false"/>.
/// </para>
/// </remarks>
internal static class WrapperElement
{
    private const string Contract =
        "Value set elements must format to exactly the backing primitive's text form, and read it back as the same element.";

    /// <summary>
    /// The element's text form for the wire — checked — and, for a non-invariant provider, the
    /// element's text under that provider, which is display text the families' closed siblings
    /// produce the same way.
    /// </summary>
    internal static string Formatted<TElement>(TElement value, string? format, Func<string, TElement> parse, IFormatProvider? provider)
        where TElement : IEquatable<TElement>, IFormattable
    {
        var text = value.ToString(format, CultureInfo.InvariantCulture);

        if (Mismatch(value, text, parse) is { } why)
            throw new InvalidOperationException($"{Describe<TElement>(text, why)} {Contract}");

        return provider is null || provider.Equals(CultureInfo.InvariantCulture) ? text : value.ToString(format, provider);
    }

    /// <summary>The element just parsed, if its own text form reads back as it.</summary>
    internal static TElement Parsed<TElement>(TElement value, string? format, Func<string, TElement> parse)
        where TElement : IEquatable<TElement>, IFormattable
    {
        var text = value.ToString(format, CultureInfo.InvariantCulture);

        if (Mismatch(value, text, parse) is { } why)
            throw new FormatException($"{Describe<TElement>(text, why)} {Contract}");

        return value;
    }

    private static string? Mismatch<TElement>(TElement value, string text, Func<string, TElement> parse)
        where TElement : IEquatable<TElement>
    {
        TElement back;
        try
        {
            back = parse(text);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            return $"cannot be read back: {LiteralExcerpt.Of(ex.Message)}";
        }

        return back.Equals(value) ? null : "reads back as a different element";
    }

    // The rule's name first: the array parser rebounds a read-leg message as a 64-character excerpt,
    // and a long element type name would otherwise cut it off.
    private static string Describe<TElement>(string text, string why)
        => $"The text form does not round-trip: '{typeof(TElement)}' formats as '{LiteralExcerpt.Of(text)}', which {why}.";
}
