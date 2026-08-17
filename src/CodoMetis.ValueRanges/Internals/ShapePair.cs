using System.Diagnostics;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

/// Diagnostics for the engines' shape dispatch.
///
/// A binary range operation is a function of the *pair* of shapes, so the engines switch on
/// `(left, right)` with one arm per pair they accept. C# cannot prove a switch over interface
/// patterns exhaustive, so an arm for the discard pattern is mandatory — but that arm must
/// never produce a value. Returning something plausible from it is what shipped four bugs:
/// IsAdjacentTo (6.2.1), IsStrictlyLeftOf and Except (7.0.0), and the Infinity-operand
/// subtraction below it. In each case the arm nobody wrote was answered by a fallback that
/// looked right in a debugger and disagreed with PostgreSQL.
///
/// The engines therefore throw from the discard, naming the pair. `EngineDispatchConventionTests`
/// keeps it that way.
internal static class ShapePair
{
    internal static UnreachableException Unreachable<T>(string engine, IRange<T> left, IRange<T> right)
        where T : struct, IComparable<T>, IEquatable<T> =>
        new($"{engine} has no arm for the shape pair ({Shape(left)}, {Shape(right)}). " +
            "Either the caller's guards no longer hold, or the pair is genuinely reachable and " +
            "needs its own arm — never a fallback.");

    // Deliberately an if-chain rather than a switch expression: this is the one place that has to
    // cope with a shape it does not recognise, and a switch would need a value-returning discard
    // of exactly the kind the engines are not allowed to have.
    private static string Shape<T>(IRange<T> range)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        if (range is IEmptyRange<T>) return "Empty";
        if (range is IInfinityRange<T>) return "Infinity";
        if (range is IFiniteRange<T>) return "Finite";
        if (range is IUnboundedStartRange<T>) return "UnboundedStart";
        if (range is IUnboundedEndRange<T>) return "UnboundedEnd";
        return range.GetType().Name;
    }
}
