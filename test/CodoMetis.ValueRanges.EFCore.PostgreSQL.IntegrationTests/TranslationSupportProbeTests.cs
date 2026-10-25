using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Conventions.Tests;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Every public query member (<see cref="QueryMembers"/>), over every closed type it applies to, run where
/// the server must evaluate it and in a final projection, and held against its classification in
/// <c>test/oracles/translation-support.json</c> and against memory over the rows as loaded (#384).
/// </summary>
/// <remarks>
/// <para>
/// A successful final <c>Select</c> proves nothing about translation: EF Core evaluates on the client
/// what it cannot translate there. So each call first runs where the server is required, in a filter for
/// a predicate and in a null test for a value (an aggregate in <c>HAVING</c>), with a column as an
/// operand so EF Core cannot evaluate the whole call before the query. If that runs, the member runs as
/// SQL, and its rows must be memory's; if EF Core refuses it ("could not be translated"), it runs in
/// memory only, and the projection must then answer what memory answers.
/// </para>
/// <para>
/// A <c>server</c> member that is refused, or answers differently, is a defect; a <c>client</c> or
/// <c>planned</c> member that now translates is a change the inventory has to follow. Both go through
/// <see cref="Candidates.Verdict"/>, each line starting with the member's key and the closed type.
/// </para>
/// </remarks>
[TestClass]
public sealed class TranslationSupportProbeTests
{
    [ClassInitialize]
    public static async Task CreateDatabase(TestContext _)
    {
        if (ContainerLifecycle.ConnectionString is null) return;

        await using (var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {InventoryDbContext.DatabaseName}", connection);
            await command.ExecuteNonQueryAsync();
        }

        await using var context = new InventoryDbContext();
        await context.Database.EnsureCreatedAsync();
        foreach (var (range, element) in FuzzTypes.Ranges)
            typeof(TranslationSupportProbeTests).GetMethod(nameof(SeedRanges), BindingFlags.NonPublic | BindingFlags.Static)!
                                                .MakeGenericMethod(range, element).Invoke(null, [context]);
        foreach (var (set, element, _) in FuzzTypes.Sets)
            typeof(TranslationSupportProbeTests).GetMethod(nameof(SeedSets), BindingFlags.NonPublic | BindingFlags.Static)!
                                                .MakeGenericMethod(set, element).Invoke(null, [context]);
        await context.SaveChangesAsync();
    }

    private static void SeedRanges<TRange, T>(InventoryDbContext context)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => context.AddRange(RangeProbes.Shapes<TRange, T>().Select((shape, index) => new InventoryRangeRow<TRange, T>
        {
            Id = index + 1, Range = shape, Set = RangeSet<TRange, T>.From(shape), Text = shape.ToString()!
        }));

    private static void SeedSets<TSet, T>(InventoryDbContext context)
        where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var probes = SetProbes.For<T>();
        TSet[] sets = [TSet.Empty, TSet.From([probes[0]]), TSet.From(probes), TSet.From([probes[^1]])];
        context.AddRange(sets.Select((set, index) => new InventorySetRow<TSet, T> { Id = index + 1, Set = set, Text = set.ToString()! }));
    }

    [TestMethod]
    public async Task EveryQueryMember_RunsWhereTheInventorySays()
    {
        ContainerLifecycle.RequireDatabase();

        var tables   = await Tables();
        var failures = new List<string>();
        var observed = new List<string>();
        var cells    = 0;

        foreach (var member in QueryMembers.All)
        {
            var entry   = TranslationSupport.For(member.Key);
            var applies = false;
            foreach (var table in tables)
            {
                if (Close(member, table) is not { } closed) continue;
                applies = true;
                cells++;
                var outcome = await Observe(member, closed, table, failures);
                observed.Add($"{member.Key} on {table.Name}: {outcome}");

                if (entry is null)
                {
                    failures.Add($"{member.Key}: not in test/oracles/translation-support.json");
                    continue;
                }

                // A call no column can reach is evaluated before the query, so it runs in memory by nature.
                var support   = entry.SupportFor(table.Name);
                var runsAsSql = outcome == "server";
                if (outcome.StartsWith("error", StringComparison.Ordinal))
                    failures.Add($"{member.Key} on {table.Name}: {outcome}");
                if (support == "server" && !runsAsSql)
                    failures.Add($"{member.Key} on {table.Name}: classified server, but {outcome}");
                if (support is "client" or "planned" && runsAsSql)
                    failures.Add($"{member.Key} on {table.Name}: classified {support}, but runs as SQL now: reclassify");
            }

            if (!applies) failures.Add($"{member.Key}: applies to no discovered type, so nothing probes it");
        }

        // Some 2250 cells today, over 13 range and 34 value set types; the floor catches a discovery that stopped matching.
        Assert.IsGreaterThanOrEqualTo(2000, cells, $"Only {cells} member and type cells were probed");


        Candidates.Verdict(nameof(TranslationSupportProbeTests), failures,
                           "A query member does not run where test/oracles/translation-support.json says, or answers differently from memory",
                           method: nameof(EveryQueryMember_RunsWhereTheInventorySays));
    }

    // =========================================================================
    // Tables: one per discovered range and value set type
    // =========================================================================

    /// <summary>A table of one closed type: its rows as loaded and the types its operands take.</summary>
    private sealed record Table(string Name, Type Row, Type Value, Type Element, Type? RangeSet, IReadOnlyList<object> Rows, Func<InventoryDbContext, IQueryable> Source)
    {
        public bool IsRange => RangeSet is not null;
    }

    private static async Task<List<Table>> Tables()
    {
        await using var context = new InventoryDbContext();
        var tables = new List<Table>();
        await Task.Yield();
        foreach (var (range, element) in FuzzTypes.Ranges)
        {
            var row = typeof(InventoryRangeRow<,>).MakeGenericType(range, element);
            tables.Add(new Table(FuzzTypes.Name(range), row, range, element, typeof(RangeSet<,>).MakeGenericType(range, element),
                                 Load(context, row), c => Set(c, row)));
        }

        foreach (var (set, element, _) in FuzzTypes.Sets)
        {
            var row = typeof(InventorySetRow<,>).MakeGenericType(set, element);
            tables.Add(new Table(FuzzTypes.Name(set), row, set, element, null, Load(context, row), c => Set(c, row)));
        }

        return tables;
    }

    private static IQueryable Set(InventoryDbContext context, Type row) =>
        (IQueryable)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(row).Invoke(context, null)!;

    private static IReadOnlyList<object> Load(InventoryDbContext context, Type row) =>
        [.. ((IEnumerable)Set(context, row)).Cast<object>().OrderBy(Id)];

    // =========================================================================
    // Closing a member over a table
    // =========================================================================

    /// <summary>The member as it applies to <paramref name="table"/>'s type, or null where it does not.</summary>
    private static MemberInfo? Close(QueryMembers.Member member, Table table)
    {
        var declaring = member.Declaring;
        if (declaring.IsGenericTypeDefinition)
        {
            Type closedType;
            if (declaring == typeof(RangeSet<,>) || declaring == typeof(RangeLowerBoundComparer<,>))
            {
                if (!table.IsRange) return null;
                closedType = declaring.MakeGenericType(table.Value, table.Element);
            }
            else
            {
                if (!table.Value.IsGenericType || table.Value.GetGenericTypeDefinition() != declaring) return null;
                closedType = table.Value;
            }

            return closedType.GetMemberWithSameMetadataDefinitionAs(member.Info);
        }

        if (QueryMembers.IsValue(declaring)) return declaring == table.Value ? member.Info : null;

        if (member.Info is not MethodInfo method) return null;
        if (method.IsGenericMethodDefinition)
        {
            try
            {
                method = method.GetGenericArguments().Length switch
                {
                    1 => method.MakeGenericMethod(table.Element),
                    2 => method.MakeGenericMethod(table.Value, table.Element),
                    _ => throw new ArgumentException("arity")
                };
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        // A member whose operands are none of the library's (a conversion from a NodaTime interval) is the
        // table's by what it returns.
        return method.GetParameters().Any(p => Mentions(p.ParameterType, table))
            || (!method.GetParameters().Any(p => QueryMembers.IsValue(p.ParameterType)) && method.ReturnType == table.Value)
                   ? method
                   : null;
    }

    /// <summary>Whether a parameter of <paramref name="type"/> takes <paramref name="table"/>'s value, its multirange, a variant of it, or a sequence of one.</summary>
    private static bool Mentions(Type type, Table table)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) type = type.GetGenericArguments()[0];
        return type == table.Value || type == table.RangeSet || type.DeclaringType == table.Value
            || (type != typeof(object) && type.IsInterface && type.IsAssignableFrom(table.Value) && QueryMembers.IsValue(type));
    }

    // =========================================================================
    // Observing one member on one table
    // =========================================================================

    /// <summary>
    /// <c>server</c>, <c>refused</c>, <c>unprobed: …</c> or <c>error: …</c>; writes a line for every row
    /// whose answer differs from memory's.
    /// </summary>
    private static async Task<string> Observe(QueryMembers.Member member, MemberInfo closed, Table table, List<string> failures)
    {
        var row = Expression.Parameter(table.Row, "r");
        var aggregate = closed is MethodInfo { IsStatic: true } m && m.GetParameters() is [{ ParameterType: { IsGenericType: true } input }]
                     && input.GetGenericTypeDefinition() == typeof(IEnumerable<>) && Mentions(input, table) && m.Name.Contains("Agg");
        if (aggregate) return await ObserveAggregate(member, (MethodInfo)closed, table, failures);

        if (Call(closed, table, row) is not { } body) return $"unprobed: no operand for {Unprobed(closed, table)}";

        await using var context = new InventoryDbContext();
        var source  = table.Source(context);
        var memory  = Expression.Lambda(Expression.Convert(body, typeof(object)), row).Compile();
        var answers = new Dictionary<int, object?>();
        foreach (var item in table.Rows)
        {
            try { answers[Id(item)] = Materialize(memory.DynamicInvoke(item)); }
            catch (TargetInvocationException) { }
        }

        // Where the server must evaluate the call: a filter for a predicate, a null test for a value that can be
        // NULL, and an ordering for one that cannot, since EF Core folds (int?)x == null to FALSE untranslated.
        string outcome;
        try
        {
            if (body.Type.IsValueType && body.Type != typeof(bool) && Nullable.GetUnderlyingType(body.Type) is null)
            {
                var order    = await Ordered(source, Expression.Lambda(body, row));
                var expected = answers.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key).Select(pair => pair.Key).ToList();
                outcome = "server";
                if (!order.Where(answers.ContainsKey).SequenceEqual(expected))
                    failures.Add($"{member.Key} on {table.Name}: OrderBy puts the rows [{string.Join(",", order)}], memory [{string.Join(",", expected)}]");
            }
            else
            {
                var filter = body.Type == typeof(bool) ? body : CompositionRunner.IsNull(body);
                var ids    = await Ids(source, Expression.Lambda(filter, row));
                outcome = "server";
                foreach (var (id, answer) in answers)
                {
                    var expected = body.Type == typeof(bool) ? answer is true : answer is null;
                    if (ids.Contains(id) != expected)
                        failures.Add($"{member.Key} on {table.Name}, row {id}: the server {(ids.Contains(id) ? "selects" : "drops")} it where memory answers {Show(answer)}");
                }
            }
        }
        catch (InvalidOperationException e) when (e.Message.Contains("could not be translated", StringComparison.Ordinal))
        {
            outcome = "refused";
        }
        catch (Exception e) when (e is InvalidOperationException or DbException or InvalidCastException or NotSupportedException or ArgumentException)
        {
            return $"error: {e.GetType().Name}: {CompositionRunner.FirstLine(e)}";
        }

        // The final projection, where EF Core evaluates on the client what it cannot translate, over the
        // rows memory answers: one row whose call throws (an indexer past the end) fails the whole query.
        try
        {
            var answered  = answers.Keys.ToArray();
            var only      = Expression.Lambda(Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(int)],
                                                              Expression.Constant(answered), Expression.Property(row, "Id")), row);
            var projected = await Projected(source.Provider.CreateQuery(Expression.Call(typeof(Queryable), nameof(Queryable.Where), [table.Row],
                                                                                         source.Expression, Expression.Quote(only))), row, body);
            foreach (var (id, value) in projected)
            {
                if (answers.TryGetValue(id, out var answer) && !Same(Materialize(value), answer))
                    failures.Add($"{member.Key} on {table.Name}, row {id}: Select answers {Show(value)}, memory {Show(answer)}");
            }
        }
        catch (Exception e) when (e is not AssertFailedException)
        {
            failures.Add($"{member.Key} on {table.Name}: Select throws {e.GetType().Name}: {CompositionRunner.FirstLine(e)}");
        }

        return outcome;
    }

    private static async Task<string> ObserveAggregate(QueryMembers.Member member, MethodInfo aggregate, Table table, List<string> failures)
    {
        var input  = aggregate.GetParameters()[0].ParameterType.GetGenericArguments()[0];
        var column = input == table.Value ? nameof(InventoryRangeRow<Int32Range, int>.Range) : nameof(InventoryRangeRow<Int32Range, int>.Set);
        var row    = Expression.Parameter(table.Row, "r");
        var group  = Expression.Parameter(typeof(IGrouping<,>).MakeGenericType(typeof(int), table.Row), "g");
        var body   = Expression.Call(aggregate, Expression.Call(typeof(Enumerable), nameof(Enumerable.Select), [table.Row, input], group,
                                                                 Expression.Lambda(Expression.Property(row, column), row)));

        await using var context = new InventoryDbContext();
        var keyed   = Expression.Lambda(Expression.Constant(1), row);
        var grouped = Expression.Call(typeof(Queryable), nameof(Queryable.GroupBy), [table.Row, typeof(int)], table.Source(context).Expression, Expression.Quote(keyed));

        var memory   = aggregate.Invoke(null, [Cast(table.Rows.Select(r => r.GetType().GetProperty(column)!.GetValue(r)), input)]);
        try
        {
            var having = Expression.Call(typeof(Queryable), nameof(Queryable.Where), [group.Type], grouped,
                                         Expression.Quote(Expression.Lambda(CompositionRunner.IsNull(body), group)));
            var key    = Expression.Lambda(Expression.Property(group, "Key"), group);
            var count  = await table.Source(context).Provider
                                    .CreateQuery<int>(Expression.Call(typeof(Queryable), nameof(Queryable.Select), [group.Type, typeof(int)], having, Expression.Quote(key)))
                                    .CountAsync();
            if ((count == 1) != (memory is null))
                failures.Add($"{member.Key} on {table.Name}: HAVING (== null) keeps {count} groups where memory answers {Show(memory)}");

            var select = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [group.Type, typeof(object)], grouped,
                                         Expression.Quote(Expression.Lambda(Expression.Convert(body, typeof(object)), group)));
            var value  = await ((IQueryable<object>)table.Source(context).Provider.CreateQuery<object>(select)).SingleAsync();
            if (!Same(value, memory))
                failures.Add($"{member.Key} on {table.Name}: Select answers {Show(value)}, memory {Show(memory)}");
            return "server";
        }
        catch (InvalidOperationException e) when (e.Message.Contains("could not be translated", StringComparison.Ordinal))
        {
            return "refused";
        }
        catch (Exception e) when (e is InvalidOperationException or DbException)
        {
            return $"error: {e.GetType().Name}: {CompositionRunner.FirstLine(e)}";
        }
    }

    private static object Cast(IEnumerable<object?> items, Type element) =>
        typeof(Enumerable).GetMethod(nameof(Enumerable.ToList))!.MakeGenericMethod(element)
                          .Invoke(null, [typeof(Enumerable).GetMethod(nameof(Enumerable.Cast))!.MakeGenericMethod(element).Invoke(null, [items])])!;

    private static async Task<HashSet<int>> Ids(IQueryable source, LambdaExpression filter)
    {
        var where  = Expression.Call(typeof(Queryable), nameof(Queryable.Where), [source.ElementType], source.Expression, Expression.Quote(filter));
        var r      = Expression.Parameter(source.ElementType, "r");
        var select = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [source.ElementType, typeof(int)], where,
                                     Expression.Quote(Expression.Lambda(Expression.Property(r, "Id"), r)));
        return [.. await source.Provider.CreateQuery<int>(select).ToListAsync()];
    }

    private static async Task<List<int>> Ordered(IQueryable source, LambdaExpression key)
    {
        var ordered = Expression.Call(typeof(Queryable), nameof(Queryable.OrderBy), [source.ElementType, key.ReturnType], source.Expression, Expression.Quote(key));
        var r       = Expression.Parameter(source.ElementType, "r");
        var id      = Expression.Lambda(Expression.Property(r, "Id"), r);
        var then    = Expression.Call(typeof(Queryable), nameof(Queryable.ThenBy), [source.ElementType, typeof(int)], ordered, Expression.Quote(id));
        var select  = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [source.ElementType, typeof(int)], then, Expression.Quote(id));
        return await source.Provider.CreateQuery<int>(select).ToListAsync();
    }

    private static async Task<List<(int Id, object? Value)>> Projected(IQueryable source, ParameterExpression row, Expression body)
    {
        var projection = Expression.MemberInit(
            Expression.New(typeof(Projected)),
            Expression.Bind(typeof(Projected).GetProperty(nameof(Composition.Projected.Id))!, Expression.Property(row, "Id")),
            Expression.Bind(typeof(Projected).GetProperty(nameof(Composition.Projected.Value))!, Expression.Convert(body, typeof(object))));
        var select = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [source.ElementType, typeof(Projected)], source.Expression,
                                     Expression.Quote(Expression.Lambda(projection, row)));
        return [.. (await source.Provider.CreateQuery<Projected>(select).ToListAsync()).Select(p => (p.Id, p.Value))];
    }

    private static int Id(object row) => (int)row.GetType().GetProperty("Id")!.GetValue(row)!;

    /// <summary>A sequence answer as a list, so two answers compare by their elements.</summary>
    private static object? Materialize(object? value) => value switch
    {
        string                                                    => value,
        IEnumerable sequence when !QueryMembers.IsValue(value.GetType()) => sequence.Cast<object?>().ToList(),
        IEnumerator enumerator                                    => Drain(enumerator),
        _ when value?.GetType().GetMethod(nameof(IEnumerator.MoveNext)) is { } move => Drain(value, move),
        _                                                         => value
    };

    private static List<object?> Drain(IEnumerator enumerator)
    {
        var items = new List<object?>();
        while (enumerator.MoveNext()) items.Add(enumerator.Current);
        return items;
    }

    // A duck-typed struct enumerator (the value sets' GetEnumerator), read through reflection on its box.
    private static List<object?> Drain(object enumerator, MethodInfo move)
    {
        var items   = new List<object?>();
        var current = enumerator.GetType().GetProperty(nameof(IEnumerator.Current))!;
        while ((bool)move.Invoke(enumerator, null)!) items.Add(current.GetValue(enumerator));
        return items;
    }

    private static bool Same(object? a, object? b) =>
        a is List<object?> x && b is List<object?> y ? x.SequenceEqual(y) : Equals(a, b);

    private static string Show(object? value) => value is List<object?> list ? $"[{string.Join(", ", list)}]" : CompositionRunner.Show(value);

    // =========================================================================
    // Building the call: a column where one fits, a sample elsewhere
    // =========================================================================

    /// <summary>
    /// The call with a column in the first operand that can take one: the table's value, its multirange,
    /// its text (for <c>Parse</c>), an element read from the column (for a factory) or the row's id (for
    /// an integer); a captured sample for the rest. Null when an operand has no sample.
    /// </summary>
    private static Expression? Call(MemberInfo member, Table table, ParameterExpression row)
    {
        var columnUsed = false;
        var parameters = member switch
        {
            MethodInfo m   => m.GetParameters().Select(p => p.ParameterType).ToList(),
            PropertyInfo p => p.GetIndexParameters().Select(i => i.ParameterType).ToList(),
            _              => []
        };

        var isStatic = member is MethodInfo { IsStatic: true };
        Expression? receiver = null;
        if (!isStatic)
        {
            var declaring = member.DeclaringType!;
            receiver = declaring == table.Value ? Column(row, table, ref columnUsed)
                     : declaring == table.RangeSet ? SetColumn(row, ref columnUsed)
                     : declaring.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static) is { } instance ? Expression.Property(null, instance)
                     : null;
            if (receiver is null) return null;
        }

        var arguments = new List<Expression>();
        foreach (var parameter in parameters)
        {
            if (Operand(parameter, table, row, ref columnUsed, preferColumn: !columnUsed) is not { } operand) return null;
            arguments.Add(Convert(operand, parameter));
        }

        if (!columnUsed) return null;

        return member switch
        {
            MethodInfo { Name: var name } m when m.IsSpecialName && name.StartsWith("op_", StringComparison.Ordinal) => Operator(m, arguments),
            MethodInfo m when m.IsStatic                                                                          => Expression.Call(m, arguments),
            MethodInfo m                                                                                          => Expression.Call(receiver!, m, arguments),
            PropertyInfo p when arguments.Count > 0                                                               => Expression.Property(receiver!, p, arguments),
            PropertyInfo p                                                                                        => Expression.Property(receiver!, p),
            _                                                                                                     => null
        };
    }

    private static Expression Operator(MethodInfo method, IReadOnlyList<Expression> arguments) => method.Name switch
    {
        "op_Equality"    => Expression.Equal(arguments[0], arguments[1], false, method),
        "op_Inequality"  => Expression.NotEqual(arguments[0], arguments[1], false, method),
        "op_BitwiseOr"   => Expression.Or(arguments[0], arguments[1], method),
        "op_BitwiseAnd"  => Expression.And(arguments[0], arguments[1], method),
        "op_Subtraction" => Expression.Subtract(arguments[0], arguments[1], method),
        _                => Expression.Call(method, arguments)
    };

    private static Expression Column(ParameterExpression row, Table table, ref bool used)
    {
        used = true;
        return Expression.Property(row, table.IsRange ? "Range" : "Set");
    }

    private static Expression SetColumn(ParameterExpression row, ref bool used)
    {
        used = true;
        return Expression.Property(row, "Set");
    }

    private static Expression? Operand(Type parameter, Table table, ParameterExpression row, ref bool used, bool preferColumn)
    {
        var sequence = parameter.IsGenericType && parameter.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? parameter.GetGenericArguments()[0] : null;

        // The table's own value, as the receiver of an extension or the operand of a static member.
        if (parameter == table.Value || parameter.DeclaringType == table.Value
            || (parameter != typeof(object) && parameter.IsInterface && parameter.IsAssignableFrom(table.Value) && QueryMembers.IsValue(parameter)))
        {
            if (preferColumn) return Convert(Column(row, table, ref used), parameter);
            return Convert(Captured(Sample(table), table.Value), parameter);
        }

        if (parameter == table.RangeSet)
            return preferColumn ? SetColumn(row, ref used) : Captured(SampleSet(table), parameter);

        if (sequence is not null && sequence == table.Value && table.IsRange)
            return preferColumn ? Expression.NewArrayInit(sequence, Column(row, table, ref used)) : Captured(Array.CreateInstance(sequence, 0), parameter);

        if (sequence is not null && sequence == table.Element && !table.IsRange)
            return preferColumn ? Expression.Convert(Expression.Property(Column(row, table, ref used), "Values"), parameter) : Captured(Array.CreateInstance(sequence, 0), parameter);

        if (parameter == typeof(string) && preferColumn) return Text(row, ref used);

        if (parameter == table.Element)
        {
            if (preferColumn && table.IsRange)
            {
                // An element read from the column: lower(x), or the sample where the range has none.
                var lower = Expression.Call(typeof(RangeExtensions).GetMethod(nameof(RangeExtensions.LowerBound))!.MakeGenericMethod(table.Element),
                                            Column(row, table, ref used));
                return Expression.Coalesce(lower, Captured(SampleElement(table), table.Element));
            }

            return Captured(SampleElement(table), table.Element);
        }

        if (parameter == typeof(string))
            return preferColumn ? Text(row, ref used) : Expression.Constant(null, typeof(string));
        if (parameter == typeof(int))
            return preferColumn ? Id(row, ref used) : Captured(1, typeof(int));
        if (parameter == typeof(bool)) return Expression.Constant(true);
        if (parameter == typeof(IFormatProvider)) return Captured(CultureInfo.InvariantCulture, typeof(IFormatProvider));
        if (parameter == typeof(object)) return Expression.Convert(Captured(Sample(table), table.Value), typeof(object));

        object? sample = parameter switch
        {
            _ when parameter == typeof(long)     => 1L,
            _ when parameter == typeof(decimal)  => 1m,
            _ when parameter == typeof(double)   => 1d,
            _ when parameter == typeof(float)    => 1f,
            _ when parameter == typeof(TimeSpan) => TimeSpan.FromDays(1),
            _ when parameter == typeof(Period)   => Period.FromDays(1),
            _ when parameter == typeof(Duration) => Duration.FromDays(1),
            _ when parameter == typeof(Interval) => new Interval(Instant.FromUtc(2026, 1, 1, 0, 0), Instant.FromUtc(2026, 2, 1, 0, 0)),
            _ when parameter == typeof(DateInterval) => new DateInterval(new LocalDate(2026, 1, 1), new LocalDate(2026, 1, 31)),
            _ when parameter.IsGenericType && parameter.GetGenericTypeDefinition() == typeof(Func<,>) && table.IsRange
                => typeof(TranslationSupportProbeTests).GetMethod(nameof(Single), BindingFlags.NonPublic | BindingFlags.Static)!
                                                       .MakeGenericMethod(table.Value, table.Element).Invoke(null, null),
            _ => null
        };

        return sample is null ? null : Captured(sample, parameter);
    }

    private static string Unprobed(MemberInfo member, Table table) =>
        member is MethodInfo m
            ? string.Join(", ", m.GetParameters().Select(p => QueryMembers.Name(p.ParameterType)))
            : "a receiver";

    /// <summary>
    /// The operand as the parameter takes it, written as C# writes it: a reference conversion to an
    /// interface or a base type adds no node (a column passed to <c>IRange&lt;T&gt;</c> stays the column, which
    /// is what EF Core and the translators see from consumer code); only a value type is converted.
    /// </summary>
    private static Expression Convert(Expression expression, Type type) =>
        expression.Type == type || (!expression.Type.IsValueType && type.IsAssignableFrom(expression.Type))
            ? expression
            : Expression.Convert(expression, type);

    private static Expression Text(ParameterExpression row, ref bool used)
    {
        used = true;
        return Expression.Property(row, "Text");
    }

    private static Expression Id(ParameterExpression row, ref bool used)
    {
        used = true;
        return Expression.Property(row, "Id");
    }

    private static Func<T, TRange> Single<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => value => TRange.CreateFinite(value, value, true, true);

    private static object Sample(Table table) =>
        table.IsRange
            ? table.Rows.Select(r => r.GetType().GetProperty("Range")!.GetValue(r)!).Skip(2).First()
            : table.Rows.Select(r => r.GetType().GetProperty("Set")!.GetValue(r)!).Skip(1).First();

    private static object SampleSet(Table table) => table.Rows.Select(r => r.GetType().GetProperty("Set")!.GetValue(r)!).Skip(2).First();

    /// <summary>
    /// An element: a range probe's low bound, or the first element of a loaded set, as the store holds it
    /// (microsecond-truncated), since the store's truncation is <c>TemporalStoreParityTests</c>' to judge.
    /// </summary>
    private static object SampleElement(Table table) =>
        table.IsRange
            ? RangeProbes.Bounds[table.Element].Low
            : ((IEnumerable)table.Rows.Select(r => r.GetType().GetProperty("Set")!.GetValue(r)!).Skip(1).First()
                                     .GetType().GetProperty("Values")!.GetValue(table.Rows.Select(r => r.GetType().GetProperty("Set")!.GetValue(r)!).Skip(1).First())!)
              .Cast<object>().First();

    private static MemberExpression Captured(object? value, Type type) =>
        Expression.Field(Expression.Constant(Activator.CreateInstance(typeof(StrongBox<>).MakeGenericType(type), value)), nameof(StrongBox<>.Value));
}

/// <summary>One row of a range type's table: a range, its one-element multirange, and its literal.</summary>
public sealed class InventoryRangeRow<TRange, T>
    where TRange : class, IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    public int Id { get; set; }

    public TRange Range { get; set; } = TRange.Empty;

    public RangeSet<TRange, T> Set { get; set; } = RangeSet<TRange, T>.Empty;

    public string Text { get; set; } = "";
}

/// <summary>One row of a value set type's table: a set and its literal.</summary>
public sealed class InventorySetRow<TSet, T>
    where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
    where T : IEquatable<T>
{
    public int Id { get; set; }

    public TSet Set { get; set; } = TSet.Empty;

    public string Text { get; set; } = "";
}

/// <summary>A table per discovered range and value set type, in a database of its own.</summary>
public sealed class InventoryDbContext : DbContext
{
    internal const string DatabaseName = "valueranges_inventory";

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            new NpgsqlConnectionStringBuilder(ContainerLifecycle.ConnectionString) { Database = DatabaseName }.ConnectionString,
            npgsql => npgsql.UseValueRangesNodaTime()
                            .ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var (range, element) in FuzzTypes.Ranges)
        {
            var row = modelBuilder.Entity(typeof(InventoryRangeRow<,>).MakeGenericType(range, element));
            row.ToTable($"inventory_{FuzzTypes.Name(range)}");
            row.Property("Id").ValueGeneratedNever();
        }

        foreach (var (set, element, _) in FuzzTypes.Sets)
        {
            var row = modelBuilder.Entity(typeof(InventorySetRow<,>).MakeGenericType(set, element));
            row.ToTable($"inventory_{FuzzTypes.Name(set)}");
            row.Property("Id").ValueGeneratedNever();
        }

        modelBuilder.HasPostgresRange("timerange", "time");
        modelBuilder.HasPostgresRange("float8range", "double precision");
        modelBuilder.HasPostgresRange("float4range", "real");
    }
}
