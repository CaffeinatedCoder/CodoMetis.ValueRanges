using System.Globalization;
using System.Text;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// PostgreSQL array literal formatting and parsing for value set types, e.g. <c>{a,b}</c>,
/// <c>{"quoted, element"}</c>, <c>{}</c>. Sibling of <see cref="RangeFormat"/>.
/// </summary>
internal static class SetFormat
{
    internal static string Format<TSet, T>(IValueSet<T> set, string? format, IFormatProvider? provider)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var elements = set.Values;
        if (elements.IsEmpty) return "{}";

        // No format means the literal, which is invariant whatever provider is passed; a provider
        // applies only alongside an explicit format, which asks for display text. An empty format
        // is no format — .NET reads "" as the general format, exactly like null.
        if (string.IsNullOrEmpty(format)) format = null;
        provider = format is null ? CultureInfo.InvariantCulture : provider ?? CultureInfo.InvariantCulture;

        var sb = new StringBuilder(elements.Length * 8).Append('{');
        for (var i = 0; i < elements.Length; i++)
        {
            if (i > 0) sb.Append(',');
            AppendElement(sb, TSet.FormatValue(elements[i], format, provider));
        }

        return sb.Append('}').ToString();
    }

    // `provider` arrives through the IParsable signatures and is deliberately unused: the literal is
    // invariant (see ParseElementValue). Do not thread it back to the element parser.
    internal static TSet Parse<TSet, T>(ReadOnlySpan<char> s, IFormatProvider? provider)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        s = LiteralWhiteSpace.Trim(s);
        var decorated = TryReadDimension(ref s, out var declared);

        if (s.Length < 2 || s[0] != '{' || s[^1] != '}')
            throw new FormatException($"The input string '{LiteralExcerpt.Of(s)}' is not a valid array literal.");

        // Not trimmed: a trailing escaped space ({a\ }) belongs to the last element.
        var inner = s[1..^1];
        if (LiteralWhiteSpace.Trim(inner).IsEmpty || !decorated && IsNestOfEmptyArrays(s))
        {
            // A decoration names at least one element, and array_in refuses it on {} and {{}} alike.
            if (decorated)
                throw new FormatException($"The input string '{LiteralExcerpt.Of(s)}' is empty but its dimension decoration names {declared} element(s).");
            return TSet.Empty;
        }

        var values   = new List<T>();
        var position = 0;

        while (true)
        {
            values.Add(ParseElement<TSet, T>(inner, ref position));

            while (position < inner.Length && LiteralWhiteSpace.Is(inner[position])) position++;

            if (position == inner.Length) break;

            if (inner[position] != ',')
                throw new FormatException($"The input string '{{{LiteralExcerpt.Of(inner)}}}' is not a valid array literal.");
            position++;
        }

        // array_in counts the elements as written, duplicates included, before the set deduplicates them.
        if (decorated && values.Count != declared)
            throw new FormatException(
                $"The input string '{LiteralExcerpt.Of(s)}' holds {values.Count} element(s) but its dimension decoration names {declared}.");

        // Routing through From re-canonicalizes, so unsorted or duplicated literals — e.g. an
        // array written by hand or by another tool — normalize on parse.
        return TSet.From(values);
    }

    /// <summary>
    /// Reads and removes the dimension decoration <c>array_in</c> accepts before a one-dimensional array,
    /// <c>[lower:upper]=</c> or <c>[upper]=</c> (lower 1), and returns the element count it names. Until
    /// 10.0.0 it was refused, so <c>[0:1]={1,2}</c>, which <c>array_out</c> prints for an array whose
    /// lower bound is not 1, could not be read back (#128). A set has no index, so the bounds are dropped.
    /// </summary>
    /// <remarks>
    /// Measured on PostgreSQL 16 and 18, which agree: each bound is an optionally signed decimal
    /// <see cref="int"/> with no whitespace inside the brackets; <c>upper + 1</c> must not overflow;
    /// whitespace may surround the <c>=</c>. A second bracket pair declares a second dimension, which a
    /// value set cannot hold.
    /// </remarks>
    private static bool TryReadDimension(ref ReadOnlySpan<char> s, out long declared)
    {
        declared = 0;
        if (s.IsEmpty || s[0] != '[') return false;

        var close = s.IndexOf(']');
        if (close < 0) throw Malformed(s, "an unterminated dimension decoration");

        var bounds = s[1..close];
        var colon  = bounds.IndexOf(':');
        var lower  = colon < 0 ? 1 : Bound(s, bounds[..colon]);
        var upper  = Bound(s, colon < 0 ? bounds : bounds[(colon + 1)..]);
        if (upper == int.MaxValue) throw Malformed(s, "an upper dimension bound past the largest index");

        var rest = LiteralWhiteSpace.Trim(s[(close + 1)..]);
        if (!rest.IsEmpty && rest[0] == '[')
            throw new FormatException($"The input string '{LiteralExcerpt.Of(s)}' is a multi-dimensional array literal; a value set is one-dimensional.");
        if (rest.IsEmpty || rest[0] != '=') throw Malformed(s, "a dimension decoration without '='");

        declared = (long)upper - lower + 1;
        s        = LiteralWhiteSpace.Trim(rest[1..]);
        return true;

        static int Bound(ReadOnlySpan<char> literal, ReadOnlySpan<char> text)
        {
            var digits = text.Length > 0 && text[0] is '+' or '-' ? text[1..] : text;
            return !digits.IsEmpty && !digits.ContainsAnyExceptInRange('0', '9')
                   && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bound)
                ? bound
                : throw Malformed(literal, "a dimension bound that is not a decimal integer");
        }
    }

    /// <summary>
    /// Whether the literal is a rectangular nest of empty arrays, <c>{{}}</c>, <c>{{},{}}</c> or
    /// <c>{{{},{}},{{},{}}}</c>, which PostgreSQL 17 and later read as the empty array <c>{}</c>
    /// (PostgreSQL 16 refuses them). Every sibling must have the same shape, the same count at every level
    /// below it, not only the same depth: <c>{{{},{}},{{}}}</c> is refused by every version, and until
    /// #231 it read as <c>{}</c> here. Mixed depths, <c>{{},{{}}}</c>, and any element make it a
    /// multi-dimensional literal instead.
    /// </summary>
    /// <remarks>
    /// The walk stops at <see cref="MaxDimensions"/> levels, PostgreSQL's MAXDIM: deeper nests are refused
    /// by every version (#250), and the cap bounds the recursion, so a literal of thousands of nested
    /// braces is refused instead of overflowing the stack, which no handler can catch (#248).
    /// </remarks>
    private static bool IsNestOfEmptyArrays(ReadOnlySpan<char> s)
    {
        var position = 0;
        return Shape(s, ref position, depth: 1) is { } shape && shape.Contains(',') && position == s.Length;

        // The nest's shape as a signature, "0" for {} and "n×child" for n siblings of one shape, or null
        // when it is not a rectangular nest of empty arrays.
        static string? Shape(ReadOnlySpan<char> s, ref int position, int depth)
        {
            if (depth > MaxDimensions || position == s.Length || s[position] != '{') return null;
            position++;
            Skip(s, ref position);
            if (position < s.Length && s[position] == '}')
            {
                position++;
                return "0";
            }

            var child = Shape(s, ref position, depth + 1);
            var count = 1;
            while (child is not null)
            {
                Skip(s, ref position);
                if (position == s.Length) return null;
                if (s[position] == '}')
                {
                    position++;
                    return $"{count},{child}";
                }

                if (s[position++] != ',') return null;
                Skip(s, ref position);
                if (Shape(s, ref position, depth + 1) != child) return null;
                count++;
            }

            return null;
        }

        static void Skip(ReadOnlySpan<char> s, ref int position)
        {
            while (position < s.Length && LiteralWhiteSpace.Is(s[position])) position++;
        }
    }

    /// <summary>PostgreSQL's MAXDIM: an array has at most six dimensions, so a literal nests at most six braces.</summary>
    private const int MaxDimensions = 6;

    private static FormatException Malformed(ReadOnlySpan<char> s, string what)
        => new($"The input string '{LiteralExcerpt.Of(s)}' is not a valid array literal: {what}.");

    internal static bool TryParse<TSet, T>(ReadOnlySpan<char> s, IFormatProvider? provider, out TSet result)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        try
        {
            result = Parse<TSet, T>(s, provider);
            return true;
        }
        catch
        {
            result = TSet.Empty;
            return false;
        }
    }

    private static T ParseElement<TSet, T>(ReadOnlySpan<char> inner, ref int position)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        while (position < inner.Length && LiteralWhiteSpace.Is(inner[position])) position++;

        if (position == inner.Length)
            throw new FormatException($"The input string '{{{LiteralExcerpt.Of(inner)}}}' is not a valid array literal.");

        if (inner[position] == '"')
        {
            // Quoted element: a backslash takes the next character literally; the text is taken
            // verbatim otherwise, so a quoted "NULL" is the four-character value, not the null
            // marker. Only whitespace may follow the closing quote (the caller then requires the
            // delimiter or the end), so {"a"b} is refused, as PostgreSQL refuses it.
            position++;
            var sb = new StringBuilder();
            while (true)
            {
                if (position == inner.Length)
                    throw new FormatException($"The input string '{{{LiteralExcerpt.Of(inner)}}}' has an unterminated quoted element.");

                var c = inner[position++];
                if (c == '\\')
                {
                    if (position == inner.Length)
                        throw new FormatException($"The input string '{{{LiteralExcerpt.Of(inner)}}}' has an unterminated escape sequence.");
                    sb.Append(inner[position++]);
                }
                else if (c == '"')
                {
                    return ParseElementValue<TSet, T>(sb.ToString(), inner);
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        if (inner[position] == '{')
            throw new FormatException(
                $"The input string '{{{LiteralExcerpt.Of(inner)}}}' is a multi-dimensional array literal; a value set is one-dimensional.");

        // Unquoted element, as PostgreSQL's array_in reads it: a backslash takes the next character
        // literally; an unescaped brace or quote is malformed; whitespace around the element is
        // dropped and whitespace inside it kept. Only a bare, unescaped NULL is the null marker.
        var text        = new StringBuilder();
        var significant = 0;
        var escaped     = false;

        while (position < inner.Length && inner[position] != ',')
        {
            var c = inner[position++];

            if (c == '\\')
            {
                if (position == inner.Length)
                    throw new FormatException($"The input string '{{{LiteralExcerpt.Of(inner)}}}' has an unterminated escape sequence.");

                text.Append(inner[position++]);
                significant = text.Length;
                escaped     = true;
            }
            else if (c is '{' or '}' or '"')
            {
                throw new FormatException(
                    $"The input string '{{{LiteralExcerpt.Of(inner)}}}' is not a valid array literal: unexpected '{c}' in an unquoted element.");
            }
            else
            {
                text.Append(c);
                if (!LiteralWhiteSpace.Is(c)) significant = text.Length;
            }
        }

        text.Length = significant;

        if (text.Length == 0)
            throw new FormatException($"The input string '{{{LiteralExcerpt.Of(inner)}}}' is not a valid array literal.");

        var value = text.ToString();

        if (!escaped && value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Value sets cannot contain null elements.");

        return ParseElementValue<TSet, T>(value, inner);
    }

    /// <summary>
    /// An element through the family's parser, with its failure re-thrown as a bounded message.
    /// Not chained: the BCL's format error embeds the whole offending text, which is what
    /// <see cref="LiteralExcerpt"/> exists to cut. A validated wrapper's own reason survives as
    /// an excerpt.
    /// </summary>
    private static T ParseElementValue<TSet, T>(ReadOnlySpan<char> text, ReadOnlySpan<char> inner)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        try
        {
            // Always invariant, whatever provider the caller handed Parse: the literal is a wire format.
            return TSet.ParseValue(text, CultureInfo.InvariantCulture);
        }
        catch (FormatException ex)
        {
            throw new FormatException(
                $"The element '{LiteralExcerpt.Of(text)}' in the array literal '{{{LiteralExcerpt.Of(inner)}}}' "
              + $"is not a valid {typeof(T).Name}: {LiteralExcerpt.Of(ex.Message)}");
        }
    }

    private static void AppendElement(StringBuilder sb, string text)
    {
        if (!NeedsQuoting(text))
        {
            sb.Append(text);
            return;
        }

        sb.Append('"');
        foreach (var c in text)
        {
            if (c is '"' or '\\') sb.Append('\\');
            sb.Append(c);
        }

        sb.Append('"');
    }

    private static bool NeedsQuoting(string text)
    {
        if (text.Length == 0) return true;
        if (text.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var c in text)
        {
            if (c is '{' or '}' or ',' or '"' or '\\' || char.IsWhiteSpace(c)) return true;
        }

        return false;
    }
}
