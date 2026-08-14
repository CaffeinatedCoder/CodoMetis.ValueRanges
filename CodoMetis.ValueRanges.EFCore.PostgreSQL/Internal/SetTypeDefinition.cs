using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The identity implementation of <see cref="ISetTypeDefinition"/> for set types whose element
/// CLR type is itself the primitive store representation (e.g. <c>StringSet</c>/<c>string</c>,
/// <c>DateSet</c>/<see cref="DateOnly"/>). Given only the element store type name (and an
/// optional element normalization for the way to the database), it derives everything else.
/// </summary>
/// <typeparam name="TSet">The set type, e.g. <c>StringSet</c>.</typeparam>
/// <typeparam name="T">The element type, which is also the primitive store representation.</typeparam>
internal sealed class SetTypeDefinition<TSet, T> : ISetTypeDefinition
    where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
    where T : IEquatable<T>
{
    internal SetTypeDefinition(
        string           elementStoreType,
        Func<T, T>?      normalizeValue = null,
        Func<T, string>? literalText    = null
    )
    {
        ElementStoreType = elementStoreType;
        ArrayStoreType   = elementStoreType + "[]";
        SetTypeMapping   = new ValueSetTypeMapping<TSet, T, T>(
            ArrayStoreType,
            normalizeValue ?? (static value => value),
            static value => value,
            literalText);
    }

    public Type SetClrType => typeof(TSet);

    public Type ElementClrType => typeof(T);

    public string ElementStoreType { get; }

    public string ArrayStoreType { get; }

    public RelationalTypeMapping SetTypeMapping { get; }

    public object EmptySet => TSet.Empty;
}

/// <summary>
/// The <see cref="ISetTypeDefinition"/> for string-backed validated-wrapper set instantiations
/// (<c>StringSet&lt;TElement&gt;</c>): elements bridge to <c>text</c> through their
/// <see cref="IFormattable"/>/<see cref="IParsable{TSelf}"/> surface, and reads re-run the
/// element's validation.
/// </summary>
internal sealed class StringBridgedSetTypeDefinition<TSet, TElement> : ISetTypeDefinition
    where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
    where TElement : struct, IEquatable<TElement>, IFormattable, IParsable<TElement>
{
    private static string ToPrimitive(TElement element)
        => element.ToString(null, CultureInfo.InvariantCulture);

    private static TElement FromPrimitive(string primitive)
        => TElement.Parse(primitive, CultureInfo.InvariantCulture);

    public Type SetClrType => typeof(TSet);

    public Type ElementClrType => typeof(TElement);

    public string ElementStoreType => "text";

    public string ArrayStoreType => "text[]";

    public RelationalTypeMapping SetTypeMapping { get; } = new ValueSetTypeMapping<TSet, TElement, string>(
        "text[]", ToPrimitive, FromPrimitive);

    public RelationalTypeMapping ElementTypeMapping { get; } = new BridgedElementTypeMapping<TElement, string>(
        "text", ToPrimitive, FromPrimitive);

    public object EmptySet => TSet.Empty;
}

/// <summary>
/// The <see cref="ISetTypeDefinition"/> for validated-wrapper set instantiations backed by a
/// struct primitive (<c>GuidSet&lt;TElement&gt;</c>, <c>Int32Set&lt;TElement&gt;</c>,
/// <c>Int64Set&lt;TElement&gt;</c>): the bridge goes through the invariant text form in both
/// directions, which is lossless exactly when the element's text form is the backing
/// primitive's text form — the documented contract. A decorative element format fails loudly
/// here with an error naming the type and the contract.
/// </summary>
internal sealed class BridgedSetTypeDefinition<TSet, TElement, TPrimitive> : ISetTypeDefinition
    where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
    where TPrimitive : struct, IFormattable, IParsable<TPrimitive>
{
    public BridgedSetTypeDefinition(string elementStoreType)
    {
        ElementStoreType = elementStoreType;
        ArrayStoreType   = elementStoreType + "[]";
        SetTypeMapping   = new ValueSetTypeMapping<TSet, TElement, TPrimitive>(
            ArrayStoreType, ToPrimitive, FromPrimitive);
        ElementTypeMapping = new BridgedElementTypeMapping<TElement, TPrimitive>(
            ElementStoreType, ToPrimitive, FromPrimitive);
    }

    private static TPrimitive ToPrimitive(TElement element)
    {
        var text = element.ToString(null, CultureInfo.InvariantCulture);
        try
        {
            return TPrimitive.Parse(text, CultureInfo.InvariantCulture);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"The invariant text form '{text}' of '{typeof(TElement)}' is not a valid "
              + $"'{typeof(TPrimitive)}' value. Value set elements must format to exactly the "
              + "backing primitive's text form.", ex);
        }
    }

    private static TElement FromPrimitive(TPrimitive primitive)
        => TElement.Parse(primitive.ToString(null, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    public Type SetClrType => typeof(TSet);

    public Type ElementClrType => typeof(TElement);

    public string ElementStoreType { get; }

    public string ArrayStoreType { get; }

    public RelationalTypeMapping SetTypeMapping { get; }

    public RelationalTypeMapping ElementTypeMapping { get; }

    public object EmptySet => TSet.Empty;
}
