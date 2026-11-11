using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;

/// <summary>A row's id and one projected value, as the server returns them.</summary>
public sealed class Projected
{
    public int Id { get; set; }

    public object? Value { get; set; }
}

/// <summary>What became of one call: translated and compared with the reference on at least one row, translated
/// with no row the reference could answer (only the reference-free checks ran), or not translated.</summary>
internal enum Outcome
{
    Compared,
    Unanswered,
    Untranslated
}

/// <summary>
/// Runs one call in the query contexts a consumer puts it in, on the server and against
/// <see cref="NullRule"/> over the rows as loaded, and writes one line per row that disagrees.
/// </summary>
internal static class CompositionRunner
{
    /// <summary>
    /// A predicate in <c>Where(P)</c>, <c>Where(!P)</c> and <c>Select(P)</c>; a value tested against
    /// <see langword="null"/> both ways and projected. Each form is compared with the reference, and the
    /// three with each other, which needs no reference: the two filters partition the rows, and the
    /// projection says which side each row is on. Each line starts with the call's name, then
    /// <paramref name="context"/>, the query form and the row. A call EF Core cannot translate is counted
    /// rather than judged. Without <paramref name="reference"/> only the reference-free checks run.
    /// </summary>
    public static async Task<Outcome> Judge<TRow>(
        string context, Call call, ParameterExpression row, IQueryable<TRow> source, IReadOnlyList<TRow> loaded,
        Expression<Func<TRow, int>> id, Func<int, string> rowName, List<string> failures, bool reference = true)
    {
        var idOf = id.Compile();

        // A row whose reference throws has no answer to compare (a library member refusing a non-null
        // operand), and is left out of every form.
        var expected = new Dictionary<int, object?>();
        if (reference)
        {
            var answer = NullRule.Reference<TRow>(call.Body, row);
            foreach (var loadedRow in loaded)
            {
                try { expected[idOf(loadedRow)] = answer(loadedRow); }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException) { }
            }
        }

        var forms = call.Predicate
            ? new (string Form, Expression Filter, Func<object?, bool> Selected)[]
            {
                ("Where", call.Body, value => value is true),
                ("Where(!)", Expression.Not(call.Body), value => value is false)
            }
            : [
                ("Where(== null)", IsNull(call.Body), value => value is null),
                ("Where(!= null)", Expression.Not(IsNull(call.Body)), value => value is not null)
            ];

        try
        {
            var filtered = new List<HashSet<int>>();
            foreach (var (form, filter, selected) in forms)
            {
                var server = (await source.Where(Expression.Lambda<Func<TRow, bool>>(filter, row)).Select(id).ToListAsync()).ToHashSet();
                filtered.Add(server);
                foreach (var (rowId, value) in expected.OrderBy(pair => pair.Key))
                {
                    if (server.Contains(rowId) != selected(value))
                        failures.Add($"{call.Name} [{context}] {form}, row {rowName(rowId)}: server {Selects(server.Contains(rowId))} it, rule (c) {Selects(selected(value))} it");
                }
            }

            var projection = Expression.MemberInit(
                Expression.New(typeof(Projected)),
                Expression.Bind(typeof(Projected).GetProperty(nameof(Projected.Id))!, Splice(id, row)),
                Expression.Bind(typeof(Projected).GetProperty(nameof(Projected.Value))!,
                                Expression.Convert(Expression.Convert(call.Body, NullRule.Lift(call.Body.Type)), typeof(object))));
            var projected = await source.Select(Expression.Lambda<Func<TRow, Projected>>(projection, row)).ToListAsync();
            foreach (var item in projected.OrderBy(p => p.Id))
            {
                if (expected.TryGetValue(item.Id, out var value) && !Equals(item.Value, value))
                    failures.Add($"{call.Name} [{context}] Select, row {rowName(item.Id)}: server {Show(item.Value)}, rule (c) {Show(value)}");

                var (first, second) = (filtered[0].Contains(item.Id), filtered[1].Contains(item.Id));
                if (first == second || first != forms[0].Selected(item.Value))
                    failures.Add($"{call.Name} [{context}] partition, row {rowName(item.Id)}: {forms[0].Form} {Selects(first)} it, "
                               + $"{forms[1].Form} {Selects(second)} it, Select {Show(item.Value)}");
            }

            return expected.Count > 0 || !reference ? Outcome.Compared : Outcome.Unanswered;
        }
        catch (InvalidOperationException e) when (e.Message.Contains("could not be translated", StringComparison.Ordinal))
        {
            return Outcome.Untranslated;
        }
        catch (Exception e) when (e is InvalidOperationException or DbException or InvalidCastException)
        {
            failures.Add($"{call.Name} [{context}]: server threw {e.GetType().Name}: {FirstLine(e)}");
            return Outcome.Compared;
        }
    }

    /// <summary><paramref name="value"/> <c>== null</c>, lifting a value type so the test can be written.</summary>
    internal static Expression IsNull(Expression value)
    {
        var type = NullRule.Lift(value.Type);
        return Expression.Equal(type == value.Type ? value : Expression.Convert(value, type), Expression.Constant(null, type));
    }

    internal static Expression Splice<TRow, TOut>(Expression<Func<TRow, TOut>> lambda, ParameterExpression row) =>
        new Rebind(lambda.Parameters[0], row).Visit(lambda.Body);

    internal static string Show(object? value) => value switch
    {
        null   => "NULL",
        bool b => b ? "true" : "false",
        _      => value.ToString() ?? "NULL"
    };

    internal static string FirstLine(Exception e) => e.Message.Split('\n')[0];

    private static string Selects(bool selected) => selected ? "selects" : "drops";

    private sealed class Rebind(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
