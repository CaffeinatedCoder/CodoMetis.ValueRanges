using System.Diagnostics;
using System.Reflection;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// The refusal a translator, or the query interceptor's <see cref="RangeSetContains.Equivalent"/>, throws for
/// an overload it recognizes by name but has no arm for.
/// </summary>
/// <remarks>
/// <para>
/// The translators recognize an overload by its name and map each operand as the type the overload
/// declares for it. An operand type an arm was not written for must be fatal, never a guess. Mapping it
/// as the nearest known type is the receiver-shaped dispatch trap the engines guard against
/// (<c>ShapePair</c>), one layer up: a <c>RangeSet.Contains</c> over a value set would have been
/// translated as <c>@&gt;</c> over an operand mapped as a range, without an error (#295).
/// </para>
/// <para>
/// Declining (returning <see langword="null"/>) would not be enough. EF Core's own translators also
/// match by name, so one of them could take the call, as its collection <c>Contains</c> took
/// <c>RangeSet.Contains</c> (#288). <c>TranslatorDispatchSweepTests</c> hands every public overload to
/// the translators, so an overload without an arm fails there before a consumer meets this.
/// </para>
/// </remarks>
internal static class MissingArm
{
    internal static UnreachableException For(MethodInfo method, Type? operand = null) =>
        new($"{Describe(method)} has no translation{(operand is null ? "" : $" for an operand of type {Name(operand)}")}. "
          + "Give the overload its own arm, never a fallback to a type it is not.");

    private static string Describe(MethodInfo method) =>
        $"{(method.DeclaringType is { } type ? Name(type) + "." : "")}{method.Name}({string.Join(", ", method.GetParameters().Select(p => Name(p.ParameterType)))})";

    private static string Name(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>"
            : type.Name;
}
