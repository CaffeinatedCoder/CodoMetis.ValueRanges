namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// A UUID element as PostgreSQL's <c>uuid_in</c> reads it: 32 hexadecimal digits, optionally in
/// braces, with a hyphen allowed after any group of four digits except the last — and nothing
/// else. <c>6f9619ff-8b86-d011-b42d-00c04fc964ff</c>, <c>6f9619ff8b86d011b42d00c04fc964ff</c>,
/// <c>6f96-19ff-8b86-d011-b42d-00c0-4fc9-64ff</c> and <c>{6f9619ff-8b86-d011-b42d-00c04fc964ff}</c>
/// are all the same value; <c>(…)</c>, a hyphen after two digits, and whitespace at either end
/// are refused.
/// </summary>
/// <remarks>
/// Not <see cref="Guid.Parse(ReadOnlySpan{char})"/>: that reads the <c>N</c>, <c>D</c>, <c>B</c>,
/// <c>P</c> and <c>X</c> formats and trims every Unicode whitespace, so until 10.0.0 a
/// <c>GuidSet</c> literal parsed <c>{(6f9619ff-…)}</c> and <c>{" 6f9619ff-…"}</c>, which the
/// server refuses, and refused <c>{6f96-19ff-…}</c>, which it reads (#78, #129). The grammar is
/// <c>string_to_uuid</c> in <c>src/backend/utils/adt/uuid.c</c>, measured on PostgreSQL 17.
/// </remarks>
internal static class UuidLiteral
{
    // The rule first and the text last: the array parser rebounds this message as an excerpt, and a
    // long offending text would otherwise cut the rule off.
    private const string Rule =
        "Not a valid uuid: PostgreSQL reads 32 hexadecimal digits, optionally in braces, with a hyphen allowed after any group of four, and no whitespace.";

    internal static Guid Parse(ReadOnlySpan<char> s)
        => TryParse(s, out var value)
               ? value
               : throw new FormatException($"{Rule} The text was '{LiteralExcerpt.Of(s)}'.");

    internal static bool TryParse(ReadOnlySpan<char> s, out Guid value)
    {
        value = default;

        var braces   = s.Length > 0 && s[0] == '{';
        var position = braces ? 1 : 0;
        Span<byte> bytes = stackalloc byte[16];

        for (var i = 0; i < 16; i++)
        {
            if (position + 1 >= s.Length) return false;

            var high = HexDigit(s[position]);
            var low  = HexDigit(s[position + 1]);
            if (high < 0 || low < 0) return false;

            bytes[i]  = (byte)(high << 4 | low);
            position += 2;

            // A hyphen may follow a group of four digits, never the last one.
            if (i % 2 == 1 && i < 15 && position < s.Length && s[position] == '-') position++;
        }

        if (braces)
        {
            if (position >= s.Length || s[position] != '}') return false;
            position++;
        }

        if (position != s.Length) return false;

        value = new Guid(bytes, bigEndian: true);
        return true;
    }

    /// <summary>ASCII hexadecimal only, as C's <c>isxdigit</c>: no fullwidth or other Unicode digits.</summary>
    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _                 => -1
    };
}
