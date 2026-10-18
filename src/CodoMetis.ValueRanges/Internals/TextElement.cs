namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// The one rule a <c>text</c> element has: PostgreSQL's <c>text</c> cannot hold a NUL character
/// (U+0000) — <c>chr(0)</c> is "null character not permitted", and a parameter holding one is
/// "invalid byte sequence for encoding UTF8: 0x00". Everything else <c>textin</c> keeps as it is.
/// </summary>
/// <remarks>
/// Until 10.0.0 <c>StringSet.Parse("{a\0b}")</c> and <c>StringSet.From(["a\0b"])</c> accepted the
/// element, and the set failed at save time, away from where the text came in (#133). The check is
/// here once and applied on every construction path: <c>From</c>, <c>Add</c> (through
/// <c>NormalizeElement</c>), <c>Parse</c>, <c>TryParse</c>, JSON and the <c>TypeConverter</c>
/// (through <c>ParseValue</c>), for <c>StringSet</c> and <c>StringSet&lt;T&gt;</c>.
/// </remarks>
internal static class TextElement
{
    private const string Rule = "PostgreSQL text cannot hold a NUL character (U+0000)";

    internal static bool HoldsNul(ReadOnlySpan<char> s) => s.IndexOf('\0') >= 0;

    /// <summary>A value being stored: <see cref="ArgumentException"/>, as a null element is.</summary>
    internal static string Require(string value)
        => value is not null && HoldsNul(value)
               ? throw new ArgumentException($"{Rule}: '{LiteralExcerpt.Of(value)}'.", nameof(value))
               : value!; // a null reaches ValueSetCore.Canonicalize, which refuses it as a null element

    /// <summary>Text being parsed: <see cref="FormatException"/>, as any other refused element text.</summary>
    internal static ReadOnlySpan<char> RequireParsable(ReadOnlySpan<char> s)
        => HoldsNul(s)
               ? throw new FormatException($"{Rule}: '{LiteralExcerpt.Of(s)}'.")
               : s;
}
