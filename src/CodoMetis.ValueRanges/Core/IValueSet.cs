using System.Collections.Immutable;

namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Marker interface for all value set types over an equatable element type — immutable,
/// canonical (deduplicated, sorted) sets whose PostgreSQL storage shape is a native array.
/// </summary>
/// <remarks>
/// The interface stays closed to this package family — implementing
/// <see cref="IValueSetFactory{TSet,T}"/> requires its internal members, which preserves the
/// closed-world guarantee the same way the private range constructors do.
/// </remarks>
/// <typeparam name="T">The element type of the set.</typeparam>
public interface IValueSet<T> where T : IEquatable<T>
{
    /// <summary>The canonical elements: deduplicated, sorted, never containing <see langword="null"/>.</summary>
    ImmutableArray<T> Values { get; }

    /// <summary>
    /// Applies the same element normalization the type's factory methods apply, so that the
    /// element-level operations (<c>Contains</c>, <c>Add</c>, <c>Remove</c>) agree with
    /// <c>From</c> on what counts as the same value.
    /// </summary>
    /// <remarks>
    /// The default is the identity — the element type is already its own canonical form.
    /// Types that normalize or validate elements at construction override it, which is what
    /// keeps a probe comparable with the stored elements: the NodaTime calendar-bearing sets
    /// store ISO elements, and <c>LocalDate.CompareTo</c> throws (while <c>Equals</c> silently
    /// returns <see langword="false"/>) across calendar systems.
    /// An element the type rejects at construction throws here too.
    /// </remarks>
    internal T NormalizeElement(T value) => value;

    /// <summary>
    /// The order <see cref="Values"/> is sorted by — the instance-side view of
    /// <c>IValueSetFactory&lt;TSet,T&gt;.CanonicalComparer</c>, which the element-level
    /// operations need but cannot reach statically. It lets membership binary-search the
    /// canonical array instead of scanning it, which matters for the large sets this package
    /// supports.
    /// </summary>
    /// <remarks>
    /// Must return the same order as the type's <c>CanonicalComparer</c>: a mismatch would make
    /// the binary search miss elements that are present. Only the string-backed families need
    /// to override the default.
    /// </remarks>
    internal IComparer<T> CanonicalOrder => Comparer<T>.Default;
}
