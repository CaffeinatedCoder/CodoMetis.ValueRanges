using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;

/// <summary>
/// Resolves type mappings for all range types and their <see cref="RangeSet{TRange,T}"/>
/// counterparts, by CLR type (model building) or by store type name (migrations,
/// scaffolding, <c>HasColumnType</c>).
/// </summary>
public sealed class ValueRangesTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    /// <inheritdoc />
    public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var clrType   = mappingInfo.ClrType;
        var storeType = mappingInfo.StoreTypeName;

        if (clrType is not null)
        {
            if (!RangeTypeRegistry.TryGetByClrType(clrType, out var definition, out var isSet))
                return null;

            var mapping = isSet ? definition.RangeSetTypeMapping : definition.RangeTypeMapping;

            // Honor an explicit store type only when it agrees with the CLR type;
            // a mismatch is left to other sources to resolve (or to fail meaningfully).
            return storeType is null || string.Equals(storeType, mapping.StoreType, StringComparison.OrdinalIgnoreCase)
                       ? mapping
                       : null;
        }

        return storeType is not null && RangeTypeRegistry.TryGetByStoreType(storeType, out var byStore, out var isSetStore)
                   ? isSetStore ? byStore.RangeSetTypeMapping : byStore.RangeTypeMapping
                   : null;
    }
}