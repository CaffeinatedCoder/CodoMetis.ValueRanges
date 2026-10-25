using System.Reflection;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every public member a query can call on a range, a <see cref="RangeSet{TRange,T}"/> or a value set,
/// discovered from the public API of the two libraries, one key per overload:
/// <c>RangeExtensions.Overlaps(IRange&lt;T&gt;, IRange&lt;T&gt;)</c>, <c>RangeSet.Contains(T)</c>,
/// <c>StringSet.Count</c>, <c>DateRange.Month(DateOnly)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The input of the translation-support inventory (<c>test/oracles/translation-support.json</c>, #384).
/// It starts from the public API rather than from the registered translators, because the members no
/// translator knows are the ones the inventory exists to find.
/// </para>
/// <para>
/// A member is a query member when it is declared on a public, non-nested type of the core or the
/// NodaTime library and its receiver, one of its parameters or its result is a range, a multirange or a
/// value set. Static properties and fields (<c>Empty</c>, <c>Infinite</c>) are constants, which EF Core
/// evaluates on the client and sends as a parameter, so they are not listed. The sealed variants are not
/// listed either: a query sees a column as its union. Generic families keep their type parameters in the
/// key (<c>RangeSet.Union(RangeSet&lt;TRange, T&gt;)</c>, <c>StringSet&lt;T&gt;.Count</c>), so one entry
/// classifies the family and the probe runs it over every closed type.
/// </para>
/// </remarks>
internal static class QueryMembers
{
    private static readonly Type[] s_libraries = [typeof(Int32Range), typeof(LocalDateRange)];

    /// <summary>A query member: its key, the member, and the type that declares it.</summary>
    internal sealed record Member(string Key, MemberInfo Info, Type Declaring);

    /// <summary>Every query member, sorted by key.</summary>
    internal static IReadOnlyList<Member> All { get; } = Discover();

    private static List<Member> Discover()
    {
        var members = new Dictionary<string, Member>(StringComparer.Ordinal);

        foreach (var type in s_libraries.Select(marker => marker.Assembly).Distinct().SelectMany(assembly => assembly.GetExportedTypes()))
        {
            if (type.IsNested || type.IsInterface || type.Name.Contains('<') || type.GetCustomAttribute<CompilerGeneratedAttribute>() is not null) continue;
            if (IsConverter(type)) continue;

            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var key = member switch
                {
                    MethodInfo { IsSpecialName: true } m when !m.Name.StartsWith("op_", StringComparison.Ordinal) => null,
                    MethodInfo m when m.Name.Contains('.') || m.Name.Contains('<')                              => null,
                    MethodInfo m when !Writable(m)                                                                => null,
                    MethodInfo m when Touches(type, m)                                                            => $"{Name(type)}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => Name(p.ParameterType)))})",
                    PropertyInfo { GetMethod: { IsStatic: false, IsPublic: true } } p when IsValue(type) || IsValue(p.PropertyType)
                                                                                                                => $"{Name(type)}.{p.Name}{(p.GetIndexParameters() is { Length: > 0 } index ? $"[{string.Join(", ", index.Select(i => Name(i.ParameterType)))}]" : "")}",
                    _ => null
                };

                if (key is not null) members.TryAdd(key, new Member(key, member, type));
            }
        }

        return [.. members.Values.OrderBy(m => m.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// <paramref name="member"/> as it applies to one closed type, or null where it does not: a generic family
    /// closed over <paramref name="value"/>, a generic method closed over its element (and value) type, and a
    /// member of another type kept when an operand or its result is <paramref name="value"/>'s.
    /// <paramref name="rangeSet"/> is the range type's multirange, or null for a value set.
    /// </summary>
    internal static MemberInfo? Close(Member member, Type value, Type element, Type? rangeSet)
    {
        var declaring = member.Declaring;
        if (declaring.IsGenericTypeDefinition)
        {
            Type closedType;
            if (declaring == typeof(RangeSet<,>) || declaring == typeof(RangeLowerBoundComparer<,>))
            {
                if (rangeSet is null) return null;
                closedType = declaring.MakeGenericType(value, element);
            }
            else
            {
                if (!value.IsGenericType || value.GetGenericTypeDefinition() != declaring) return null;
                closedType = value;
            }

            return closedType.GetMemberWithSameMetadataDefinitionAs(member.Info);
        }

        if (IsValue(declaring)) return declaring == value ? member.Info : null;

        if (member.Info is not MethodInfo method) return null;
        if (method.IsGenericMethodDefinition)
        {
            try
            {
                method = method.GetGenericArguments().Length switch
                {
                    1 => method.MakeGenericMethod(element),
                    2 => method.MakeGenericMethod(value, element),
                    _ => throw new ArgumentException("arity")
                };
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        // A member whose operands are none of the library's (a conversion from a NodaTime interval) is the
        // type's by what it returns.
        return method.GetParameters().Any(p => Mentions(p.ParameterType, value, rangeSet))
            || (!method.GetParameters().Any(p => IsValue(p.ParameterType)) && method.ReturnType == value)
                   ? method
                   : null;
    }

    /// <summary>Whether a parameter of <paramref name="type"/> takes <paramref name="value"/>, its multirange, a variant of it, or a sequence of one.</summary>
    internal static bool Mentions(Type type, Type value, Type? rangeSet)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) type = type.GetGenericArguments()[0];
        return type == value || type == rangeSet || type.DeclaringType == value
            || (type != typeof(object) && type.IsInterface && type.IsAssignableFrom(value) && IsValue(type));
    }

    /// <summary>
    /// Whether C# can write a call of <paramref name="method"/> inside an expression tree: not with a
    /// <c>ref</c>, <c>out</c> or <c>in</c> parameter, nor with a ref struct such as
    /// <see cref="ReadOnlySpan{T}"/> (CS8640, CS8198), so the span and <c>TryParse</c> overloads can never
    /// reach a query.
    /// </summary>
    private static bool Writable(MethodInfo method) =>
        method.GetParameters().All(p => !p.ParameterType.IsByRef && !p.ParameterType.IsByRefLike) && !method.ReturnType.IsByRefLike;

    /// <summary>A JSON or type converter, which a query never calls.</summary>
    private static bool IsConverter(Type type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            if (t.FullName is "System.Text.Json.Serialization.JsonConverter" or "System.ComponentModel.TypeConverter"
                || t.Name.StartsWith("JsonConverter", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Whether a method's receiver, a parameter or its result is a range, a multirange or a value set.</summary>
    private static bool Touches(Type declaring, MethodInfo method) =>
        (!method.IsStatic && IsValue(declaring))
     || method.GetParameters().Any(p => IsValue(p.ParameterType))
     || IsValue(method.ReturnType);

    /// <summary>A range, a multirange or a value set, or a sequence or nullable of one.</summary>
    internal static bool IsValue(Type type)
    {
        if (type.IsByRef) type = type.GetElementType()!;
        if (Nullable.GetUnderlyingType(type) is { } underlying) type = underlying;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) type = type.GetGenericArguments()[0];
        if (type.IsGenericParameter) return type.GetGenericParameterConstraints().Any(IsValue);

        return Implements(type, typeof(IRange<>)) || Implements(type, typeof(IValueSet<>))
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RangeSet<,>))
            || (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
                && (definition == typeof(IRange<>) || definition == typeof(IValueSet<>) || definition == typeof(IValueSetFactory<,>) || definition == typeof(IRangeFactory<,>)));
    }

    private static bool Implements(Type type, Type open) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == open);

    /// <summary>A type as C# writes it, type parameters kept: <c>IRange&lt;T&gt;</c>, <c>RangeSet&lt;TRange, T&gt;</c>, <c>DateOnly?</c>.</summary>
    internal static string Name(Type type)
    {
        if (type.IsByRef) return $"ref {Name(type.GetElementType()!)}";
        if (Nullable.GetUnderlyingType(type) is { } underlying) return $"{Name(underlying)}?";
        if (type.IsArray) return $"{Name(type.GetElementType()!)}[]";
        if (!type.IsGenericType) return type.Name;

        var bare = type.Name[..type.Name.IndexOf('`')];
        return $"{bare}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
    }
}
