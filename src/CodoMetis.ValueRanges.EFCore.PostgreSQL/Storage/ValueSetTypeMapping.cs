using System.Data.Common;
using System.Globalization;
using System.Text;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;

/// <summary>
/// Maps a value set type (e.g. <c>StringSet</c>) to its PostgreSQL array column
/// (e.g. <c>text[]</c>), converting through a primitive array at the provider boundary —
/// Npgsql binds primitive arrays natively, so no driver-level work is involved.
/// </summary>
/// <remarks>
/// The read path routes through <c>TSet.From</c>, so non-canonical rows (unsorted,
/// duplicated) normalize on materialization and <see langword="null"/> elements throw —
/// corrupt data by the type's contract. Canonical form makes plain <see cref="object.Equals(object)"/>
/// a correct and cheap value comparer, so change detection never produces false diffs.
/// </remarks>
/// <typeparam name="TSet">The set type being mapped.</typeparam>
/// <typeparam name="TElement">The element type of the set.</typeparam>
/// <typeparam name="TPrimitive">The primitive store representation of one element.</typeparam>
internal sealed class ValueSetTypeMapping<TSet, TElement, TPrimitive> : RelationalTypeMapping, IStoreTextMapping<TSet>
    where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
    where TElement : IEquatable<TElement>
{
    private readonly Func<TElement, TPrimitive>              _toPrimitive;
    private readonly Func<TPrimitive, TElement>              _fromPrimitive;
    private readonly Func<TPrimitive, string>                _literalText;
    private readonly bool                                    _storeText;
    private readonly Func<string, IEnumerable<TPrimitive>>?  _parseStoreText;

    /// <param name="storeType">The array store type, e.g. <c>date[]</c>.</param>
    /// <param name="toPrimitive">An element as the store holds it.</param>
    /// <param name="fromPrimitive">A stored primitive back to an element.</param>
    /// <param name="literalText">The primitive's SQL literal form; defaults to <see cref="SetProviderText.Of"/>.</param>
    /// <param name="jsonValueReaderWriter">A dedicated JSON reader/writer; defaults to the literal one.</param>
    /// <param name="storeText">
    /// Whether the set's store text is the array of its store primitives (<see cref="FormatStoreText"/>)
    /// rather than its own literal — for the temporal families and <c>YearMonthSet&lt;T&gt;</c>.
    /// </param>
    /// <param name="parseStoreText">The store primitives back from that text, where the set's own <c>Parse</c> cannot read it.</param>
    internal ValueSetTypeMapping(
        string                                  storeType,
        Func<TElement, TPrimitive>              toPrimitive,
        Func<TPrimitive, TElement>              fromPrimitive,
        Func<TPrimitive, string>?               literalText           = null,
        JsonValueReaderWriter?                  jsonValueReaderWriter = null,
        bool                                    storeText             = false,
        Func<string, IEnumerable<TPrimitive>>?  parseStoreText        = null
    )
        : base(new RelationalTypeMappingParameters(
                   new CoreTypeMappingParameters(
                       typeof(TSet),
                       new ValueConverter<TSet, TPrimitive[]>(
                           model => ToProvider(model, toPrimitive),
                           provider => FromProvider(provider, fromPrimitive)),
                       new ImmutableValueComparer<TSet>(),
                       new ImmutableValueComparer<TSet>(),
                       jsonValueReaderWriter: jsonValueReaderWriter ?? LiteralJsonValueReaderWriter<TSet>.Instance),
                   storeType))
    {
        _toPrimitive    = toPrimitive;
        _fromPrimitive  = fromPrimitive;
        _literalText    = literalText ?? SetProviderText.Of;
        _storeText      = storeText;
        _parseStoreText = parseStoreText;
    }

    private ValueSetTypeMapping(
        RelationalTypeMappingParameters         parameters,
        Func<TElement, TPrimitive>              toPrimitive,
        Func<TPrimitive, TElement>              fromPrimitive,
        Func<TPrimitive, string>                literalText,
        bool                                    storeText,
        Func<string, IEnumerable<TPrimitive>>?  parseStoreText
    )
        : base(parameters)
    {
        _toPrimitive    = toPrimitive;
        _fromPrimitive  = fromPrimitive;
        _literalText    = literalText;
        _storeText      = storeText;
        _parseStoreText = parseStoreText;
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new ValueSetTypeMapping<TSet, TElement, TPrimitive>(
            parameters, _toPrimitive, _fromPrimitive, _literalText, _storeText, _parseStoreText);

    private static TPrimitive[] ToProvider(TSet set, Func<TElement, TPrimitive> toPrimitive)
        => [.. Primitives(set, toPrimitive)];

    /// <summary>
    /// The canonical elements as the store holds them, without the duplicates the conversion can
    /// create. A temporal element is truncated to the store's microsecond, so two elements a few
    /// hundred nanoseconds apart become one value; the array the server stores — and its
    /// <c>cardinality</c> — would otherwise disagree with the set's <c>Count</c>. The first occurrence
    /// wins, so canonical order is kept: truncation never reorders, but a wrapper arity's canonical
    /// order is its element's, not necessarily the primitive's.
    /// </summary>
    private static IEnumerable<TPrimitive> Primitives(TSet set, Func<TElement, TPrimitive> toPrimitive)
    {
        var seen = new HashSet<TPrimitive>();
        foreach (var element in set.Values)
        {
            var primitive = toPrimitive(element);
            if (seen.Add(primitive)) yield return primitive;
        }
    }

    /// <summary>
    /// The set's store text: its own invariant literal, or — for the families whose primitive text
    /// differs from the model's literal, the temporal ones (truncated, <c>infinity</c>) and
    /// <c>YearMonthSet&lt;T&gt;</c> (a date) — the array literal of its store primitives, each in its
    /// SQL literal form: the same values a parameter binds, so the server reads a JSON document as it
    /// would read the column. Those texts never need quoting in an array literal.
    /// </summary>
    public string FormatStoreText(TSet set)
    {
        if (!_storeText) return set.ToString(null, CultureInfo.InvariantCulture);

        var texts = Primitives(set, _toPrimitive).Select(primitive =>
        {
            var text = _literalText(primitive);
            return text.Length > 0 && text.AsSpan().IndexOfAny("\"\\,{} ") < 0
                       ? text
                       : throw new InvalidOperationException(
                             $"The {typeof(TPrimitive).Name} element text '{text}' of {typeof(TSet).Name} would need quoting in an array literal.");
        });

        return "{" + string.Join(",", texts) + "}";
    }

    /// <inheritdoc />
    public TSet ParseStoreText(string text)
        => _parseStoreText is null
               ? TSet.Parse(text, CultureInfo.InvariantCulture)
               : TSet.From([.. _parseStoreText(text).Select(_fromPrimitive)]);

    private static TSet FromProvider(TPrimitive[] array, Func<TPrimitive, TElement> fromPrimitive)
    {
        var elements = new TElement[array.Length];
        for (var i = 0; i < array.Length; i++) elements[i] = fromPrimitive(array[i]);
        return TSet.From(elements);
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        if (parameter is not NpgsqlParameter npgsqlParameter) return;

        // EF normally applies the value converter before the parameter is configured;
        // converting here as well keeps direct usage (e.g. raw SQL) working.
        if (npgsqlParameter.Value is TSet model)
            npgsqlParameter.Value = ToProvider(model, _toPrimitive);

        npgsqlParameter.DataTypeName = StoreType;
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        // Every element renders as a quoted string literal and the whole array is cast to the
        // store type — PostgreSQL casts the string form of any element type ('1'::integer,
        // '2024-01-01'::date), which keeps literal generation uniform and invariant across
        // families. The cast is mandatory anyway: ARRAY[] without one is untyped.
        var sb = new StringBuilder("ARRAY[");

        var first = true;
        foreach (var text in ElementTexts(value))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('\'').Append(text.Replace("'", "''")).Append('\'');
        }

        return sb.Append("]::").Append(StoreType).ToString();
    }

    private IEnumerable<string> ElementTexts(object value)
    {
        switch (value)
        {
            case TSet model:
                // Elements format through their primitive store representation — the model's
                // own text form can be coarser than the store type (YearMonth vs date) — and
                // without the duplicates that conversion can create, as a parameter would be.
                foreach (var primitive in Primitives(model, _toPrimitive))
                    yield return _literalText(primitive);
                break;

            case TPrimitive[] provider:
                foreach (var primitive in provider)
                    yield return _literalText(primitive);
                break;

            default:
                throw new InvalidOperationException(
                    $"Cannot generate a '{StoreType}' SQL literal for a value of type '{value.GetType()}'.");
        }
    }
}
