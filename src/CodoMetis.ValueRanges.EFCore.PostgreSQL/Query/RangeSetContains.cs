using System.Reflection;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// The three <c>RangeSet&lt;TRange, T&gt;.Contains</c> overloads as static calls, which is the form a
/// query holds them in from the moment <see cref="ValueRangesQueryExpressionInterceptor"/> has seen it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RangeSet{TRange,T}"/> is an <see cref="IReadOnlyList{T}"/> of its ranges. From EF Core
/// 11 on (and an EF Core 10 patch carrying dotnet/efcore#37081), a method named <c>Contains</c> on any
/// <see cref="IReadOnlyCollection{T}"/> is rewritten by EF Core, before translation, into
/// <c>Queryable.Contains</c> over the collection's <em>element</em> type — <c>TRange</c> — so
/// <c>set.Contains(value)</c> and <c>set.Contains(otherSet)</c>, whose operand is not a range, failed
/// inside EF Core with an <see cref="ArgumentException"/> (#287). The rewrite looks at the method's
/// declaring type, so the same call on a type that is not a collection is left alone: the interceptor
/// turns the instance call into the matching method here, and
/// <see cref="ValueRangesMethodCallTranslator"/> translates both forms to <c>@&gt;</c>.
/// </para>
/// <para>
/// Each method is the instance method it stands for, so a query that is evaluated on the client — a
/// captured set and a captured value — answers as the set does.
/// </para>
/// </remarks>
internal static class RangeSetContains
{
    /// <summary><see cref="RangeSet{TRange,T}.Contains(T)"/>.</summary>
    public static bool Value<TRange, T>(RangeSet<TRange, T> set, T value)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => set.Contains(value);

    /// <summary><see cref="RangeSet{TRange,T}.Contains(IRange{T})"/>.</summary>
    public static bool Range<TRange, T>(RangeSet<TRange, T> set, IRange<T> range)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => set.Contains(range);

    /// <summary><see cref="RangeSet{TRange,T}.Contains(RangeSet{TRange,T})"/>.</summary>
    public static bool Set<TRange, T>(RangeSet<TRange, T> set, RangeSet<TRange, T> other)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => set.Contains(other);

    private static readonly MethodInfo ValueMethod = typeof(RangeSetContains).GetMethod(nameof(Value))!;
    private static readonly MethodInfo RangeMethod = typeof(RangeSetContains).GetMethod(nameof(Range))!;
    private static readonly MethodInfo SetMethod   = typeof(RangeSetContains).GetMethod(nameof(Set))!;

    /// <summary>
    /// The static form of a <c>RangeSet&lt;TRange, T&gt;.Contains</c> instance method, closed over the
    /// set's type arguments, or <see langword="null"/> for any other method.
    /// </summary>
    /// <remarks>
    /// Each overload is matched by its operand type: the set, <c>IRange&lt;T&gt;</c> or <c>T</c>. Any other
    /// one-argument <c>Contains</c> has no static form here and is refused with <see cref="MissingArm"/>,
    /// naming it. Handed to the value form, as it was before #320, it failed in <c>Expression.Call</c> with an
    /// <see cref="ArgumentException"/> that named neither the overload nor the missing arm.
    /// </remarks>
    public static MethodInfo? Equivalent(MethodInfo instanceMethod)
    {
        if (instanceMethod is not { Name: nameof(RangeSet<,>.Contains), IsStatic: false, DeclaringType: { IsGenericType: true } declaringType }
         || declaringType.GetGenericTypeDefinition() != typeof(RangeSet<,>)
         || instanceMethod.GetParameters() is not [{ ParameterType: var operand }])
            return null;

        var arguments = declaringType.GetGenericArguments();
        var element   = arguments[1];

        var open = operand == declaringType                               ? SetMethod
                 : operand == typeof(IRange<>).MakeGenericType(element) ? RangeMethod
                 : operand == element                                   ? ValueMethod
                                                                        : throw MissingArm.For(instanceMethod, operand);

        return open.MakeGenericMethod(arguments);
    }
}
