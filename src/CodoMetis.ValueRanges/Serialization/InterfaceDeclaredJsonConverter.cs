using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Serialization;

/// <summary>
/// Serializes a value <em>declared</em> as one of the range or value set interfaces —
/// <see cref="IRange{T}"/>, its five shape interfaces, or <see cref="IValueSet{T}"/> — through the
/// converter of its runtime type, so it writes the same literal or array as when it is declared as
/// that type. Reading into such a declaration is refused with <see cref="NotSupportedException"/>.
/// </summary>
/// <remarks>
/// <para>
/// System.Text.Json resolves the declared type, and before 9.0.0 the interfaces carried no
/// converter: a property of type <c>IRange&lt;int&gt;</c> was written as <c>{}</c> and one of type
/// <c>IValueSet&lt;string&gt;</c> as <c>{"Values":["a"]}</c> — a dump of the interface's members,
/// with no error, and nothing that read back.
/// </para>
/// <para>
/// The write asks the options for the runtime type's contract, so whatever would serialize that
/// type — this package's converter, or one registered on the options — serializes it here too,
/// reflection-based or source-generated alike. Under a source-generated context the runtime type
/// must be listed there; if it is not, System.Text.Json throws a <see cref="NotSupportedException"/>
/// naming it.
/// </para>
/// <para>
/// A read cannot be served: an interface does not say which type to construct, and the same
/// literal is valid for more than one of them. The remedy is to declare the concrete type, which
/// reads the very JSON this converter writes. <see langword="null"/> is left to
/// System.Text.Json in both directions, as for any reference type.
/// </para>
/// </remarks>
public sealed class InterfaceDeclaredJsonConverter : JsonConverter<object>
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert is { IsInterface: true, IsGenericType: true }
        && IsHandled(typeToConvert.GetGenericTypeDefinition());

    /// <summary>Always throws: an interface declaration does not say which type to construct.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var declared = Display(typeToConvert);
        var concrete = typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(IValueSet<>)
                           ? "the value set type (StringSet, Int32Set<OrderId>, …)"
                           : "the range type (Int32Range, DateRange, …), one of its variants, or RangeSet<TRange, T>";

        throw new NotSupportedException(
            $"A value declared as {declared} cannot be read from JSON: the interface does not say which type to "
          + $"construct. Declare the property as the concrete type — {concrete} — which reads the JSON this "
          + "package writes for it.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options.GetTypeInfo(value.GetType()));

    private static bool IsHandled(Type definition)
        => definition == typeof(IRange<>)
        || definition == typeof(IFiniteRange<>)
        || definition == typeof(IUnboundedStartRange<>)
        || definition == typeof(IUnboundedEndRange<>)
        || definition == typeof(IEmptyRange<>)
        || definition == typeof(IInfinityRange<>)
        || definition == typeof(IValueSet<>);

    private static string Display(Type type)
        => type.IsGenericType
               ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>"
               : type.Name;
}
