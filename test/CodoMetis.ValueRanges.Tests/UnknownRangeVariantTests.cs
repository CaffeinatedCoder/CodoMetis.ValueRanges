using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// An <see cref="IRange{T}"/> that is none of the five sealed shapes must be rejected, not guessed
/// at.
/// </summary>
/// <remarks>
/// <para>
/// The shape switches in the public surface are exhaustive over the five variants, so their
/// discard arms are unreachable as long as the sealed-variant rule holds. But <c>IRange&lt;T&gt;</c>
/// is a public interface: nothing in the type system stops an outside assembly implementing it,
/// which is exactly why the rule is a rule rather than a compiler guarantee. What the discard arm
/// answers is therefore observable, and it has to be a refusal.
/// </para>
/// <para>
/// <c>ToString</c> returned <c>"empty"</c> there until 7.0.1 — the worst available answer, since
/// that text is what <c>Parse</c> round-trips, what the EF literal sends to PostgreSQL and what
/// <c>ShapeMatrixParityTests</c> compares against the server. An unrecognised range would have been
/// stored, queried and asserted as the empty range with nothing raised anywhere.
/// </para>
/// </remarks>
[TestClass]
public class UnknownRangeVariantTests
{
    [TestMethod]
    public void Formatting_AnUnknownVariant_Throws()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => ((IFormattable)new RogueRange()).ToString(null, null));

        StringAssert.Contains(exception.Message, nameof(RogueRange),
                              "The refusal should name the offending type, so a consumer that hits "
                            + "it knows which implementation is at fault.");
    }

    /// <summary>
    /// Implements <see cref="IRange{T}"/> and <see cref="IRangeFactory{TRange,T}"/> without being
    /// any of <see cref="IFiniteRange{T}"/>, <see cref="IUnboundedStartRange{T}"/>,
    /// <see cref="IUnboundedEndRange{T}"/>, <see cref="IEmptyRange{T}"/> or
    /// <see cref="IInfinityRange{T}"/> — the shape no arm is written for.
    /// </summary>
    private sealed class RogueRange : IRange<int>, IRangeFactory<RogueRange, int>
    {
        public static RogueRange Empty    { get; } = new();
        public static RogueRange Infinite { get; } = new();

        public static RogueRange CreateFinite(int start, int end, bool startInclusive, bool endInclusive) => new();
        public static RogueRange CreateUnboundedEnd(int start, bool startInclusive) => new();
        public static RogueRange CreateUnboundedStart(int end, bool endInclusive) => new();

        public static int ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider) =>
            int.Parse(s, provider);
    }
}
