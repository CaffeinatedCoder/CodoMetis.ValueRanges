using System.Collections.Immutable;

namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Marker interface for all value set types over an equatable element type — immutable,
/// canonical (deduplicated, sorted) sets whose PostgreSQL storage shape is a native array.
/// </summary>
/// <remarks>
/// The internal <see cref="Elements"/> member gives the generic set algebra structural access
/// to the canonical storage while keeping the interface closed to this package family — like
/// the private range constructors, this preserves the closed-world guarantee.
/// </remarks>
/// <typeparam name="T">The element type of the set.</typeparam>
public interface IValueSet<T> where T : IEquatable<T>
{
    /// <summary>The canonical elements: deduplicated, sorted, never containing <see langword="null"/>.</summary>
    internal ImmutableArray<T> Elements { get; }
}
