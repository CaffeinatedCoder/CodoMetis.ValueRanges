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
}
