using CodoMetis.ValueRanges.Core;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CodoMetis.ValueRanges.AspNetCore.Tests;

// docs/serialization.md carries the class below verbatim, from its <summary> to its closing brace;
// OpenApiSchemaTests.DocumentedSnippet_IsThisFile fails when the two drift apart.

/// <summary>
/// Describes CodoMetis.ValueRanges types in ASP.NET Core's OpenAPI document as they appear on the
/// wire. Without it a range, multirange or value set is described as <c>{}</c>: the generator
/// cannot see through a custom JSON converter. Register it with
/// <c>builder.Services.AddOpenApi(o => o.AddSchemaTransformer&lt;ValueRangesSchemaTransformer&gt;());</c>
/// </summary>
public sealed class ValueRangesSchemaTransformer : IOpenApiSchemaTransformer
{
    // Mirrors what the parsers accept: surrounding whitespace, "empty" in any case, quoted bounds.
    private const string RangePattern      = @"^\s*([Ee][Mm][Pp][Tt][Yy]|[\[(][\s\S]*,[\s\S]*[\])])\s*$";
    private const string MultirangePattern = @"^\s*\{[\s\S]*\}\s*$";

    public async Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RangeSet<,>))
        {
            var element = type.GetGenericArguments()[1];
            schema.Type        = JsonSchemaType.String;
            schema.Pattern     = MultirangePattern;
            schema.Description = $"A PostgreSQL multirange literal over {element.Name}: ranges in braces, "
                               + "e.g. {[1,5],[7,10]}, or {} for none.";
        }
        else if (FactoryElement(type, typeof(IRangeFactory<,>)) is { } rangeElement)
        {
            schema.Type        = JsonSchemaType.String;
            schema.Pattern     = RangePattern;
            schema.Description = $"A PostgreSQL range literal over {rangeElement.Name}: [ ] include a bound, "
                               + "( ) exclude it, an omitted bound is unbounded, e.g. [1,10) or [5,); "
                               + "or 'empty'.";
        }
        else if (FactoryElement(type, typeof(IValueSetFactory<,>)) is { } setElement)
        {
            schema.Type        = JsonSchemaType.Array;
            schema.UniqueItems = true;
            schema.Items       = await context.GetOrCreateSchemaAsync(setElement, cancellationToken: cancellationToken);
            schema.Description = $"A set of distinct {setElement.Name} values.";
        }
    }

    // A range variant (DateRange.Finite, …) inherits its union's IRangeFactory, so it is described too.
    private static Type? FactoryElement(Type type, Type openFactory)
        => Array.Find(type.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == openFactory)
               ?.GetGenericArguments()[1];
}
