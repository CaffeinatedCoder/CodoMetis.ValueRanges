using System.Reflection;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// A Boolean predicate with a <see langword="null"/> range or range set on either side answers
/// <see langword="false"/>, the rule EF Core applies to a predicate over a NULL column (#350, rule (c)).
/// </summary>
/// <remarks>
/// <para>
/// Before, a null range answered some predicates and threw on others, and which one could depend on
/// the operand: <c>Contains(Empty)</c> and <c>IsContainedBy((,))</c> answered <see langword="true"/>,
/// <c>Contains(T)</c> threw <see cref="NullReferenceException"/>, and a null set operand threw. In a
/// query the same call over a NULL column is NULL, which a filter treats as <see langword="false"/>, so
/// every one of those answers disagreed with the database on the NULL rows.
/// </para>
/// <para>
/// Every public <see cref="bool"/> member of <see cref="RangeExtensions"/> and every Boolean instance
/// member of <see cref="RangeSet{TRange,T}"/> that takes a range or a set is discovered, for every
/// range type of both libraries, and called with <see langword="null"/> as the range it is called on,
/// as the operand, and as both, against every shape and set probe on the other side. A member added
/// later is covered with no edit here. <c>CompareTo</c> is not a predicate and keeps
/// <c>Comparer&lt;T&gt;.Default</c>'s rule (#261); a <see cref="RangeSet{TRange,T}"/> called on a null
/// set throws as any instance member does.
/// </para>
/// </remarks>
[TestClass]
public sealed class RangeNullPredicateContractTests
{
    [TestMethod]
    public void EveryPredicate_AnswersFalseForANullRangeOrSetOnEitherSide()
    {
        var failures = new List<string>();
        var calls    = 0;

        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
        {
            var (typeFailures, typeCalls) = Reflect.InvokeGeneric<(List<string>, int)>(
                typeof(RangeNullPredicateContractTests), nameof(NullAnswers), rangeType, elementType);
            failures.AddRange(typeFailures);
            calls += typeCalls;
        }

        // Thirteen range types, each with some thirty predicates called several times: far more than
        // this floor, which only catches a discovery that silently stopped matching.
        Assert.IsGreaterThanOrEqualTo(1000, calls, $"Only {calls} predicate calls were made; discovery stopped matching.");
        Assert.AreEqual(0, failures.Count,
                        $"{failures.Count} predicate calls with a null range or set did not answer false:\n"
                      + string.Join("\n", failures.Take(40))
                      + (failures.Count > 40 ? $"\n… and {failures.Count - 40} more" : ""));
    }

    private static (List<string> Failures, int Calls) NullAnswers<TRange, T>()
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var shapes   = RangeProbes.Shapes<TRange, T>();
        var finite   = shapes[2];
        RangeSet<TRange, T>[] sets = [RangeSet<TRange, T>.Empty, RangeSet<TRange, T>.From(finite), RangeSet<TRange, T>.Infinite];
        var element  = (T)RangeProbes.Bounds[typeof(T)].Low;
        var failures = new List<string>();
        var calls    = 0;
        var name     = typeof(TRange).Name;

        // What each parameter type can be given, other than null.
        object[] Probes(Type parameter) =>
            parameter == typeof(T)                   ? [element]
          : parameter == typeof(RangeSet<TRange, T>) ? [.. sets]
          :                                            [.. shapes];

        void Expect(string call, Func<object?> invoke)
        {
            calls++;
            string answer;
            try
            {
                answer = invoke() is true ? "true" : "false";
            }
            catch (TargetInvocationException e)
            {
                answer = $"throws {e.InnerException!.GetType().Name}";
            }

            if (answer != "false") failures.Add($"  {call}: {answer}");
        }

        // The range extensions: a null range they are called on, a null operand, and both.
        foreach (var method in typeof(RangeExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                                       .Where(method => method.ReturnType == typeof(bool)))
        {
            var closed = method.GetGenericArguments().Length switch
            {
                1 => method.MakeGenericMethod(typeof(T)),
                2 => method.MakeGenericMethod(typeof(TRange), typeof(T)),
                _ => null
            };
            if (closed is null || !closed.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(TRange))) continue;

            var parameters = closed.GetParameters();
            if (parameters.Length == 1)
            {
                Expect($"null.{method.Name}() on {name}", () => closed.Invoke(null, [null]));
                continue;
            }

            var operand = parameters[1].ParameterType;
            foreach (var probe in Probes(operand))
                Expect($"null.{method.Name}({probe}) on {name}", () => closed.Invoke(null, [null, probe]));

            if (operand.IsValueType) continue;

            foreach (var shape in shapes)
                Expect($"{shape}.{method.Name}(null {operand.Name}) on {name}", () => closed.Invoke(null, [shape, null]));
            Expect($"null.{method.Name}(null {operand.Name}) on {name}", () => closed.Invoke(null, [null, null]));
        }

        // The set's own predicates with a null range or set operand. A null set they are called on
        // throws, as any instance member does.
        foreach (var method in typeof(RangeSet<TRange, T>).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                                           .Where(method => method.ReturnType == typeof(bool)
                                                                         && method.GetParameters() is [{ ParameterType: { IsValueType: false } }]
                                                                         && method.Name != nameof(Equals)))
        {
            var operand = method.GetParameters()[0].ParameterType;
            foreach (var set in sets)
                Expect($"{set}.{method.Name}(null {operand.Name}) on RangeSet<{name}>", () => method.Invoke(set, [null]));
        }

        return (failures, calls);
    }
}
