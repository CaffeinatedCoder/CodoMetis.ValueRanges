using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Serialization;
namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Marker interface for the empty range — a range that contains no values.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
[JsonConverter(typeof(InterfaceDeclaredJsonConverter))]
public interface IEmptyRange<T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>;