using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;

/// <summary>
/// Reads and writes a range, multirange or value set inside an EF Core JSON column (a complex or
/// owned type mapped with <c>ToJson()</c>) as its PostgreSQL literal string — the same text the
/// type's <c>ToString()</c> produces and its <c>Parse</c> reads, always invariant.
/// </summary>
/// <remarks>
/// <para>
/// EF Core writes each property inside a JSON column through its type mapping's
/// <see cref="JsonValueReaderWriter"/>. The plugin's mappings had none, so
/// <c>SaveChanges</c> failed with a bare <see cref="NullReferenceException"/> from EF's own
/// update pipeline for any of these types inside a JSON column.
/// </para>
/// <para>
/// The literal is also what PostgreSQL reads for the column's store type, so a value extracted
/// from the document as text casts to <c>daterange</c>, <c>text[]</c> and so on.
/// </para>
/// <para>
/// A temporal type is written at the store's resolution, as Npgsql would bind it — microseconds,
/// truncated toward 2000-01-01, with the extremes as <c>-infinity</c> / <c>infinity</c> — so the
/// value a query casts out of the document is the value a parameter or a native column holds. The
/// per-type text comes from <see cref="Internal.StoreText{TValue}"/>, which is static for the reason
/// below.
/// </para>
/// <para>
/// Public because a compiled model (<c>dotnet ef dbcontext optimize</c>) re-creates the reader/writer
/// through <see cref="ConstructorExpression"/>, which generated code must be able to reference.
/// </para>
/// </remarks>
/// <typeparam name="TValue">The range, multirange or value set type.</typeparam>
public sealed class LiteralJsonValueReaderWriter<TValue> : JsonValueReaderWriter<TValue>
    where TValue : IFormattable, IParsable<TValue>
{
    private static readonly PropertyInfo InstanceProperty =
        typeof(LiteralJsonValueReaderWriter<TValue>).GetProperty(nameof(Instance))!;

    private LiteralJsonValueReaderWriter()
    {
    }

    /// <summary>The singleton instance.</summary>
    public static LiteralJsonValueReaderWriter<TValue> Instance { get; } = new();

    /// <inheritdoc />
    public override TValue FromJsonTyped(ref Utf8JsonReaderManager manager, object? existingObject = null)
        => Internal.StoreText<TValue>.Parse(manager.CurrentReader.GetString()!);

    /// <inheritdoc />
    public override void ToJsonTyped(Utf8JsonWriter writer, TValue value)
        => writer.WriteStringValue(Internal.StoreText<TValue>.Format(value));

    /// <inheritdoc />
    public override Expression ConstructorExpression => Expression.Property(null, InstanceProperty);
}
