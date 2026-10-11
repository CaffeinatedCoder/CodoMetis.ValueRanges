using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CodoMetis.ValueRanges.Serialization;

/// <summary>
/// Where the generic types — <see cref="RangeSet{TRange,T}"/> and the validated-wrapper value set
/// arities — hand over their closed converters, so that nothing has to build one by reflection.
/// </summary>
/// <remarks>
/// <para>
/// An attribute cannot name a converter closed over its type's own type arguments, so these types
/// name <see cref="RangeJsonConverterFactory"/> and <see cref="LiteralTypeConverter"/>, which are
/// handed only the <see cref="Type"/>. Building the converter from that with
/// <c>MakeGenericType</c> works under the JIT and fails under Native AOT: the instantiation over a
/// value type (<c>RangeSetJsonConverter&lt;DateRange, DateOnly&gt;</c>) is never compiled, and
/// <c>Activator.CreateInstance</c> throws. Whether it was compiled depended on unrelated code
/// elsewhere in the program, so an AOT build could pass a test by accident.
/// </para>
/// <para>
/// Instead each generic type registers delegates for its own closed converters from a static field
/// initializer. That code belongs to the type, so it is compiled for every instantiation the program
/// uses, and because it writes to this class it cannot be evaluated at build time and dropped.
/// <see cref="Get"/> runs the type's initializer before looking it up, so the entry exists however
/// the program reaches the type. The Native AOT consumer in <c>test/consumer-smoke-test.sh</c>
/// proves it for the hard case: range sets and a wrapper set the program only ever deserializes and
/// never constructs.
/// </para>
/// </remarks>
internal static class GenericConverters
{
    private static readonly ConcurrentDictionary<Type, Entry> Entries = new();

    internal sealed record Entry(Func<JsonConverter> Json, Func<TypeConverter> Text);

    /// <summary>Registers <paramref name="type"/>'s converters. Returns <see langword="true"/> so it can initialize a field.</summary>
    internal static bool Register(Type type, Func<JsonConverter> json, Func<TypeConverter> text)
    {
        Entries.TryAdd(type, new Entry(json, text));
        return true;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2059",
        Justification = "Runs the generic type's own registration. Should trimming ever remove that "
                      + "initializer, the lookup below throws a NotSupportedException naming the type: a loud "
                      + "failure, never a wrong value.")]
    internal static Entry Get(Type type)
    {
        RuntimeHelpers.RunClassConstructor(type.TypeHandle);

        return Entries.TryGetValue(type, out var entry)
                   ? entry
                   : throw new NotSupportedException(
                         $"No converter is registered for '{type}'. Every generic range set and value set type "
                       + "registers its own from a static initializer; if trimming removed it, register the closed "
                       + "converter yourself (see docs/serialization.md#native-aot).");
    }
}
