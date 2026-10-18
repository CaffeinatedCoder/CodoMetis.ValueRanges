namespace CodoMetis.ValueRanges.Oracles;

/// <summary>
/// Calls the <see cref="string"/> overloads of <c>Parse</c> and <c>TryParse</c> from generic code.
/// </summary>
/// <remarks>
/// Under a constraint that derives from <see cref="ISpanParsable{TSelf}"/> (<c>IRangeFactory</c>,
/// <c>IValueSetFactory</c>), <c>TSet.Parse(text, provider)</c> with a <see cref="string"/> argument does
/// <em>not</em> call the string overload: overload resolution removes a base interface's candidate when a
/// derived interface's is applicable, and <see cref="ISpanParsable{TSelf}.Parse(ReadOnlySpan{char}, IFormatProvider?)"/>
/// is applicable through the implicit string-to-span conversion. A sweep that means to compare the two
/// overloads would compare the span overload with itself. Constraining on <see cref="IParsable{TSelf}"/>
/// alone leaves only the string candidates, which dispatch to the type's own string methods.
/// <para>
/// Linked into every test project that parses generically, as <c>Candidates.cs</c> is; <c>StringOverloadConventionTests</c>
/// fails on a generic call that reaches the span overload with a string.
/// </para>
/// </remarks>
public static class StringOverloads
{
    public static TSelf Parse<TSelf>(string s, IFormatProvider? provider) where TSelf : IParsable<TSelf>
        => TSelf.Parse(s, provider);

    public static bool TryParse<TSelf>(string? s, IFormatProvider? provider, out TSelf result) where TSelf : IParsable<TSelf>
        => TSelf.TryParse(s, provider, out result!);
}
