using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// Describes how one value set type binds to PostgreSQL: its CLR types, its element and array
/// store types, and the corresponding type mapping. The non-generic view lets the mapping
/// source and translators work uniformly over all registered set types. Sibling of
/// <see cref="IRangeTypeDefinition"/>.
/// </summary>
internal interface ISetTypeDefinition
{
    /// <summary>The set type, e.g. <c>StringSet</c> or <c>StringSet&lt;PermissionKey&gt;</c>.</summary>
    Type SetClrType { get; }

    /// <summary>The element type of the set, e.g. <see cref="string"/> or a validated wrapper.</summary>
    Type ElementClrType { get; }

    /// <summary>
    /// The PostgreSQL element type, e.g. <c>text</c>. Used to resolve the element type mapping
    /// explicitly — the provider's CLR-type default can differ from the array's element type.
    /// </summary>
    string ElementStoreType { get; }

    /// <summary>The PostgreSQL array type name, e.g. <c>text[]</c>.</summary>
    string ArrayStoreType { get; }

    /// <summary>The type mapping for the set type.</summary>
    RelationalTypeMapping SetTypeMapping { get; }

    /// <summary>
    /// The type mapping for elements of the set, or <see langword="null"/> to resolve it from
    /// the type mapping source via <see cref="ElementClrType"/>/<see cref="ElementStoreType"/>.
    /// A definition whose element CLR type is unknown to the provider (a validated wrapper)
    /// supplies its own converting mapping here, so that a bare element parameter in
    /// <c>column @&gt; ARRAY[@p]</c> binds as the primitive store type.
    /// </summary>
    RelationalTypeMapping? ElementTypeMapping => null;

    /// <summary>The <c>TSet.Empty</c> singleton, untyped.</summary>
    object EmptySet { get; }
}
