using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;

/// <summary>
/// Resolves type mappings for all value set types, by CLR type only.
/// </summary>
/// <remarks>
/// Deliberately never matches by store type name alone: <c>text[]</c> and friends belong to
/// the provider's native array mappings for <c>string[]</c> etc., and claiming them would
/// hijack plain array properties and scaffolding. A scaffolded array column therefore stays a
/// plain CLR array — opting into a value set type is a model decision.
/// </remarks>
public sealed class ValueSetsTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    /// <inheritdoc />
    public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;

        if (clrType is null || !SetTypeRegistry.TryGetByClrType(clrType, out var definition))
            return null;

        var mapping   = definition.SetTypeMapping;
        var storeType = mappingInfo.StoreTypeName;

        // Honor an explicit store type only when it agrees with the CLR type;
        // a mismatch is left to other sources to resolve (or to fail meaningfully).
        return storeType is null || string.Equals(storeType, mapping.StoreType, StringComparison.OrdinalIgnoreCase)
                   ? mapping
                   : null;
    }
}
