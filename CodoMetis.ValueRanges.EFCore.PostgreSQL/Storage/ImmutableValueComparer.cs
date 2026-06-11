using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;

/// <summary>
/// A <see cref="ValueComparer{T}"/> for immutable reference types with value equality:
/// equality and hashing delegate to the type's own implementation, and — because instances
/// can never be mutated — the snapshot is the instance itself.
/// </summary>
/// <typeparam name="TValue">The immutable model type.</typeparam>
internal sealed class ImmutableValueComparer<TValue>() : ValueComparer<TValue>(
    (left, right) => object.Equals(left, right),
    value => value == null ? 0 : value.GetHashCode(),
    value => value
) where TValue : class;