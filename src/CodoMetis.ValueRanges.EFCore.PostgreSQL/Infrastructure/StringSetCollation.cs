using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Infrastructure;

/// <summary>
/// A string set column compares its elements as memory does only under a collation that compares bytes (#483).
/// </summary>
/// <remarks>
/// <para>
/// <c>@&gt;</c>, <c>&lt;@</c>, <c>&amp;&amp;</c>, <c>=</c> and <c>array_remove</c> compare <c>text</c> elements with the
/// column's collation. Under a nondeterministic one, <c>A</c> equals <c>a</c>: on a case-insensitive column
/// <c>Contains("a")</c> is true for <c>{A}</c>, and <c>ExecuteUpdate</c> with <c>Remove("a")</c> stored <c>{}</c>,
/// where memory compares ordinally and keeps <c>{A}</c>. A deterministic collation breaks every tie by the bytes, so
/// it agrees; but a name doesn't say which kind it is, so only the byte-order collations PostgreSQL ships are allowed.
/// </para>
/// <para>
/// Writing <c>COLLATE "C"</c> into the operators instead was measured and rejected: the GIN index carries the
/// column's collation, and an operand collated otherwise turns every indexed <c>@&gt;</c> into a sequential scan,
/// on an ordinary <c>en_US</c> column too (PostgreSQL 16 and 18).
/// </para>
/// </remarks>
internal static class StringSetCollation
{
    /// <summary>The collations that order and compare text by its bytes, the database default among them.</summary>
    private static readonly HashSet<string> ByteOrder = ["C", "POSIX", "ucs_basic", "pg_c_utf8", "default"];

    public static bool ComparesBytes(string collation) => ByteOrder.Contains(collation);

    /// <summary><see cref="StringSet"/> and <see cref="StringSet{TElement}"/>, the set types over <c>text</c>.</summary>
    public static bool IsStringSet(Type type)
        => type == typeof(StringSet) || type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(StringSet<>);

    /// <summary>
    /// The annotation on a property whose collation may not compare bytes, naming it, for the query guard (#507). The
    /// read-optimized model a query runs on drops collations, which only migrations need.
    /// </summary>
    public const string ElementAnnotation = "CodoMetis:ValueRanges:Collation";

    /// <summary>The collation <see cref="ElementAnnotation"/> names on a property, or <see langword="null"/>.</summary>
    public static string? OfElement(IReadOnlyProperty property) => property.FindAnnotation(ElementAnnotation)?.Value as string;

    public static string Remedy
        => "Remove the collation, or use \"C\" (docs/efcore.md#store-a-value-set-in-an-array-column).";
}

/// <summary>Adds <see cref="StringSetCollationConvention"/>, beside whatever other plugins add.</summary>
internal sealed class ValueRangesConventionSetPlugin : IConventionSetPlugin
{
    /// <inheritdoc />
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.ModelFinalizingConventions.Add(new StringSetCollationConvention());
        return conventionSet;
    }
}

/// <summary>
/// Rejects, when the model is built, a string set column configured with a collation that may not compare bytes
/// (<see cref="StringSetCollation"/>). A compiled model is checked when it is generated.
/// </summary>
internal sealed class StringSetCollationConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            Check(entityType);
    }

    private static void Check(IConventionTypeBase type)
    {
        // Inside a JSON document a set is its literal, which no collation reads.
        if (type.IsMappedToJson()) return;

        foreach (var property in type.GetDeclaredProperties())
        {
            if (property.GetCollation() is not { } collation || StringSetCollation.ComparesBytes(collation))
                continue;

            // A value from this column compared with a string set compares under its collation too, and the query
            // guard has only the read-optimized model, which keeps no collation (#507).
            if (!StringSetCollation.IsStringSet(property.ClrType))
            {
                property.Builder.HasAnnotation(StringSetCollation.ElementAnnotation, collation);
                continue;
            }

            throw new InvalidOperationException(
                $"'{type.DisplayName()}.{property.Name}' is a {property.ClrType.ShortDisplayName()} column with the collation "
              + $"'{collation}'. A string set compares its elements ordinally in memory, and the database compares them "
              + "with the column's collation: under a case- or accent-insensitive one, Contains, the subset operators, == "
              + "and Remove would answer differently, and Remove in ExecuteUpdate would delete elements memory keeps. "
              + StringSetCollation.Remedy);
        }

        foreach (var complexProperty in type.GetDeclaredComplexProperties())
            Check(complexProperty.ComplexType);
    }
}
