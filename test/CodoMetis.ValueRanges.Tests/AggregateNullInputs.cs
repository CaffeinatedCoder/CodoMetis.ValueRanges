using System.Reflection;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Calls every per-type <c>RangeAgg</c> and <c>RangeIntersectAgg</c> overload a class declares with a
/// <see langword="null"/> range among its inputs, and compares the answer with the same inputs without
/// it. PostgreSQL's <c>range_agg</c> and <c>range_intersect_agg</c> skip a <c>NULL</c> input, so the two
/// answers must be equal wherever the null stands (#361).
/// </summary>
/// <remarks>
/// The overloads are found by reflection, so one added for a new range type is covered with no edit
/// here. The inputs are the infinite and the empty range, which every range type has without an element
/// value. A null after another input is the case that mattered: <c>RangeIntersectAgg</c> folded it in as
/// <c>Intersect(null)</c>, which is <c>Empty</c>. Linked into the NodaTime tests for that package's
/// overloads.
/// </remarks>
internal static class AggregateNullInputs
{
    public static List<string> Disagreements(Type declaringType, int minimumOverloads)
    {
        var overloads = declaringType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                     .Where(method => !method.IsGenericMethodDefinition
                                                   && method.Name is "RangeAgg" or "RangeIntersectAgg")
                                     .ToList();

        Assert.IsGreaterThanOrEqualTo(minimumOverloads, overloads.Count,
                                      $"{declaringType.Name} declares fewer per-type aggregate overloads than expected");

        var failures = new List<string>();

        foreach (var method in overloads)
        {
            var rangeType = method.GetParameters()[0].ParameterType.GetGenericArguments()[0];
            var infinite  = Static(rangeType, "Infinite");
            var empty     = Static(rangeType, "Empty");

            foreach (var (name, withNull, without) in new (string, object?[], object?[])[]
                     {
                         ("[null]",             [null],                  []),
                         ("[(,), null]",        [infinite, null],        [infinite]),
                         ("[null, (,)]",        [null, infinite],        [infinite]),
                         ("[(,), null, empty]", [infinite, null, empty], [infinite, empty])
                     })
            {
                var answer   = Call(method, rangeType, withNull);
                var expected = Call(method, rangeType, without);

                // Compared as literals, which are canonical for a range and for a set.
                if (answer != expected)
                    failures.Add($"{method.Name}({rangeType.Name}) over {name}: {answer}, without the null {expected}");
            }
        }

        return failures;
    }

    private static object Static(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    /// <summary>The answer as text: the value's literal, <c>null</c>, or the exception it threw.</summary>
    private static string Call(MethodInfo method, Type rangeType, object?[] items)
    {
        var source = Array.CreateInstance(rangeType, items.Length);
        for (var index = 0; index < items.Length; index++) source.SetValue(items[index], index);

        try
        {
            return method.Invoke(null, [source])?.ToString() ?? "null";
        }
        catch (TargetInvocationException e)
        {
            return $"throws {e.InnerException!.GetType().Name}";
        }
        catch (NullReferenceException)
        {
            // A set built around a null element throws when it prints itself.
            return "a set holding null";
        }
    }
}
