namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// The part of a rejected input that goes into the exception message.
/// </summary>
/// <remarks>
/// Parse errors used to embed the whole input, and element parse errors chained the BCL's
/// exception, whose message embeds it again. A megabyte of hostile literal became a megabyte of
/// exception message — copied into every log sink and, in development, echoed to the client —
/// which is memory and log volume disproportionate to the request, the shape SECURITY.md lists as
/// in scope. Sixty-four characters is enough to recognise the input; the length says how much was
/// cut. The same bound is applied to the reason taken from an inner parser, so a validated
/// wrapper's own message survives (bounded) without the BCL's echo coming along.
/// </remarks>
internal static class LiteralExcerpt
{
    internal const int MaxLength = 64;

    internal static string Of(ReadOnlySpan<char> s)
        => s.Length <= MaxLength ? s.ToString() : $"{s[..MaxLength]}… ({s.Length} characters)";

    internal static string Of(string? s) => Of(s.AsSpan());
}
