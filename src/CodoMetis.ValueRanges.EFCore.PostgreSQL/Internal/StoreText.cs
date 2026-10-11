namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// A type mapping that knows its value's <em>store text</em>: the text the plugin writes when it
/// writes the value as text rather than binding it — inside a JSON column, and in a SQL literal.
/// </summary>
/// <remarks>
/// By default that is the type's own invariant literal. A temporal type writes the value as Npgsql
/// would have bound it (<see cref="StoreTemporal"/>), so the document, the constant and the parameter
/// all mean the same value to the server.
/// </remarks>
/// <typeparam name="TValue">The range, multirange or value set type.</typeparam>
internal interface IStoreTextMapping<TValue>
{
    string FormatStoreText(TValue value);

    TValue ParseStoreText(string text);
}

/// <summary>
/// The store text of a type, for code that has the type but not its mapping — the JSON
/// reader/writer, which a compiled model re-creates from a static singleton.
/// </summary>
/// <remarks>
/// Resolved through the registries on first use rather than set by a definition when it is
/// constructed: a wrapper set family's definition is only built when something asks the registry for
/// that instantiation, which in a compiled model need not have happened before the first document is
/// written. Asking is what builds it. A type nothing registered — no <c>UseValueRanges()</c>, or a
/// NodaTime type without <c>UseValueRangesNodaTime()</c> — throws rather than falling back to a text
/// form that may not be the store's.
/// </remarks>
internal static class StoreText<TValue>
{
    private static IStoreTextMapping<TValue>? _mapping;

    internal static string Format(TValue value) => Mapping.FormatStoreText(value);

    internal static TValue Parse(string text) => Mapping.ParseStoreText(text);

    private static IStoreTextMapping<TValue> Mapping => _mapping ??= Resolve();

    private static IStoreTextMapping<TValue> Resolve()
    {
        object? mapping = null;

        if (RangeTypeRegistry.TryGetByClrType(typeof(TValue), out var range, out var isSet))
            mapping = isSet ? range.RangeSetTypeMapping : range.RangeTypeMapping;
        else if (SetTypeRegistry.TryGetByClrType(typeof(TValue), out var set))
            mapping = set.SetTypeMapping;

        return mapping as IStoreTextMapping<TValue>
            ?? throw new InvalidOperationException(
                   $"No CodoMetis.ValueRanges type mapping with a store text is registered for '{typeof(TValue)}'. "
                 + "Configure the context with UseValueRanges(), and UseValueRangesNodaTime() for the NodaTime types.");
    }
}
