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

        if (s.Length < 2 || s[0] != '{' || s[^1] != '}')
            throw new FormatException($"The input string '{LiteralExcerpt.Of(s)}' is not a valid array literal.");

        // Not trimmed: a trailing escaped space ({a\ }) belongs to the last element.
        var inner = s[1..^1];
        if (LiteralWhiteSpace.Trim(inner).IsEmpty) return TSet.Empty;

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

        // Routing through From re-canonicalizes, so unsorted or duplicated literals — e.g. an
        // array written by hand or by another tool — normalize on parse.
        return TSet.From(values);
    }

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
