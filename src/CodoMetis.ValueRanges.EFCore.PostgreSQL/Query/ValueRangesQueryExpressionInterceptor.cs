using System.Linq.Expressions;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Pre-translation rewrites of the query.
/// </summary>
/// <remarks>
/// <para>
/// The rewrite turns the <see cref="RangeSet{TRange,T}"/> operators (<c>|</c>, <c>&amp;</c>,
/// <c>-</c>) into their <c>Union</c> / <c>Intersect</c> / <c>Except</c> method-call equivalents.
/// EF Core translates user-defined operators as plain SQL binary operators without consulting
/// method call translators; the rewrite routes them through
/// <see cref="ValueRangesMethodCallTranslator"/> instead, producing valid multirange SQL. A second
/// rewrite gives a range constant EF Core inlined its union type — see
/// <see cref="RangeVariantConstantRetyper"/>. A third sends a captured collection of value sets or ranges
/// one parameter per element, because neither can be bound as one array parameter — see
/// <see cref="CollectionParameterExpander"/>. A fourth makes <c>CompareTo</c> with a null range on
/// either side answer in SQL as <c>Comparer&lt;T&gt;.Default</c> does — see <see cref="NullOperandCompareToRewriter"/>.
/// A fifth keeps <c>RangeSet.Contains</c> out of EF Core's own rewrite of <c>Contains</c> on a
/// read-only collection — see <see cref="RangeSetContainsRewriter"/>.
/// </para>
/// <para>
/// A sixth keeps an <see cref="IComparer{T}"/> over ranges or sets,
/// <see cref="RangeLowerBoundComparer{TRange,T}"/> among them, out of SQL, where EF Core would read it as the
/// natural order — see <see cref="RangeComparerRewriter"/>.
/// </para>
/// <para>
/// The value set refusals — a server-computed <c>Union</c> consumed by an order- or
/// multiplicity-sensitive operation, and <c>ExecuteUpdate</c> writing a value set into a JSON
/// column — are not made here but on the translated SQL, by <see cref="ValueSetSqlGuard"/>: at the
/// LINQ level they can only be recognized by how the query is written, and every pattern list
/// missed a shape that translated to the same SQL.
/// </para>
/// </remarks>
public sealed class ValueRangesQueryExpressionInterceptor : IQueryExpressionInterceptor
{
    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        => CollectionParameterExpander.Expand(
            RangeVariantConstantRetyper.Instance.Visit(
                NullOperandCompareToRewriter.Instance.Visit(
                    RangeComparerRewriter.Instance.Visit(
                        RangeSetContainsRewriter.Instance.Visit(RangeSetOperatorRewriter.Instance.Visit(queryExpression))))),
            DefaultCollectionMode(eventData.Context));

    private static ParameterTranslationMode DefaultCollectionMode(DbContext? context)
        => context is null
               ? ParameterTranslationMode.Parameter
               : RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>()).ParameterizedCollectionMode;

    /// <summary>
    /// Binds a captured collection of value sets or ranges one parameter per element —
    /// <c>IN (@p1, @p2, …)</c> — where EF Core would bind it as a single array parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Neither collection can go out as one parameter. A value set is itself a PostgreSQL array, so a
    /// collection of them is an array of arrays (<c>text[][]</c>), which PostgreSQL does not have:
    /// Npgsql refused to write it (<c>InvalidCastException</c>). A collection of ranges reaches Npgsql
    /// as an untyped <c>NpgsqlRange&lt;T&gt;[]</c>, which Npgsql infers as a <em>multirange</em>, so
    /// <c>r.Seats = ANY (@p)</c> failed on the server (<c>42809</c>), and with an
    /// <c>InvalidCastException</c> for the custom <c>timerange</c>, which has no inferred type at all.
    /// Typing that one parameter <c>int4range[]</c> would need Npgsql's internal array mapping, and
    /// still could not name the custom type.
    /// </para>
    /// <para>
    /// EF Core's <see cref="ParameterTranslationMode.MultipleParameters"/> mode binds each element as
    /// its own parameter, through the element's own mapping, which names the store type. So
    /// <c>Contains</c> becomes <c>IN</c> — equality against each — and <c>Any</c> or <c>Count</c> over
    /// the collection read it from <c>VALUES</c>. This is the SQL a constant or inline collection
    /// already produced. Equality of two canonical arrays is set equality, and range equality is the
    /// range <c>==</c> translation, so the answer is the one the same expression gives in memory. A
    /// collection of <see cref="RangeSet{TRange,T}"/> is left alone: <c>NpgsqlRange&lt;T&gt;[][]</c>
    /// infers as a multirange array, which is what it is.
    /// </para>
    /// <para>
    /// Both collections are switched wherever they are used. Until 10.0.0 a range collection was
    /// switched only when a <c>Contains</c> in the query read it, because <c>Contains</c> is what had
    /// failed: on its own, <c>Any</c> or <c>Count</c> over it ran, as <c>unnest</c> of the parameter
    /// Npgsql had typed as a multirange — which merged overlapping and adjacent ranges and dropped
    /// empty ones, so <c>Count(w =&gt; w.Overlaps(c))</c> over <c>[1,5]</c> and <c>[3,8]</c> was 1
    /// where memory says 2, without an error (issue #76). Read from <c>VALUES</c> the list is the
    /// list. An empty collection then reads no rows (<see cref="EmptyCollectionRewriter"/>).
    /// </para>
    /// <para>
    /// Only a collection that would have gone out as one array parameter is changed. A context
    /// configured with <see cref="ParameterTranslationMode.Constant"/> or
    /// <see cref="ParameterTranslationMode.MultipleParameters"/>, and a collection wrapped in
    /// <see cref="EF.Constant{T}"/>, already translate, so they keep their mode. A collection wrapped
    /// in <see cref="EF.Parameter{T}"/> asks for the one array parameter that cannot be bound, so it is
    /// refused here, with what to write instead.
    /// </para>
    /// <para>
    /// A value set <c>Union</c> computed on the server stays refused under <c>IN</c>: that is decided on
    /// the SQL tree, by <see cref="ValueSetSqlGuard"/>.
    /// </para>
    /// </remarks>
    private sealed class CollectionParameterExpander(ParameterTranslationMode defaultMode) : ExpressionVisitor
    {
        public static Expression Expand(Expression query, ParameterTranslationMode defaultMode)
            => new CollectionParameterExpander(defaultMode).Visit(query);

        protected override Expression VisitExtension(Expression node)
            => node is QueryParameterExpression parameter
               && (parameter.TranslationMode ?? defaultMode) == ParameterTranslationMode.Parameter
               && Element(parameter.Type) is not null
                   ? new QueryParameterExpression(parameter.Name, parameter.Type, ParameterTranslationMode.MultipleParameters)
                   : base.VisitExtension(node);

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Every use of either collection as one parameter is wrong: a value set collection cannot be
            // bound, and a range collection binds as a multirange, which is not the list.
            if (EFParameterOf(node) is var (type, isSet)) throw EFParameterRefused(type, isSet);

            return base.VisitMethodCall(node);
        }

        private static InvalidOperationException EFParameterRefused(Type type, bool isSet)
            => new($"A collection of {type.Name} cannot be sent as a single parameter ('EF.Parameter'). "
                 + (isSet
                        ? $"Each {type.Name} is a PostgreSQL array, and PostgreSQL has no array of arrays, so there is "
                        + "no type to bind the collection as. "
                        : "Npgsql would send it as an array it reads as a multirange rather than an array of ranges, "
                        + "which merges overlapping and adjacent ranges and drops empty ones. ")
                 + $"Remove 'EF.Parameter': without it the collection is sent one parameter per {(isSet ? "set" : "range")}, "
                 + "Contains translates to IN (@p1, @p2, …), and Any or Count read it from VALUES.");

        /// <summary>The value set or range type of an <c>EF.Parameter(collection)</c> call.</summary>
        private static (Type Type, bool IsSet)? EFParameterOf(Expression? node)
            => node is MethodCallExpression { Method: { Name: nameof(EF.Parameter), IsGenericMethod: true } method } call
               && method.DeclaringType == typeof(EF)
                   ? Element(call.Type)
                   : null;

        /// <summary>
        /// The registered value set or range type a collection type holds, or <see langword="null"/>.
        /// </summary>
        private static (Type Type, bool IsSet)? Element(Type type)
        {
            var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                                 ? type
                                 : type.GetInterfaces().FirstOrDefault(
                                     i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            if (enumerable?.GetGenericArguments()[0] is not { } element) return null;
            if (SetTypeRegistry.TryGetByClrType(element, out _)) return (element, true);

            return RangeTypeRegistry.TryGetByClrType(element, out _, out var isRangeSet) && !isRangeSet
                       ? (element, false)
                       : null;
        }
    }

    /// <summary>
    /// Retypes a range constant that EF Core inlined from the runtime type of its value — the sealed
    /// variant — to the range type itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EF Core evaluates an expression that depends on nothing in the query — a range built inline,
    /// <c>r.Period == DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1))</c>,
    /// <c>Int32Range.Parse("[1,5]", null)</c> — on the client before translation, and inlines the
    /// result as a constant typed by its value: <c>DateRange.Finite</c>, under a conversion back to
    /// <c>DateRange</c>. No type mapping exists for a variant, and EF Core does not carry the column's
    /// mapping across to an operand of another CLR type, so <c>==</c>, <c>!=</c>, <c>Equals</c> and a
    /// conditional failed with "does not have a type mapping assigned". The method translators
    /// (<c>Contains</c>, <c>Overlaps</c>, …) were unaffected — they type their operands themselves —
    /// and so were a captured variable and <c>EF.Constant</c>, which EF types by the declared type.
    /// </para>
    /// <para>
    /// A factory call that depends on a column never reaches here as a constant; the method call
    /// translator turns it into the range constructor function.
    /// </para>
    /// </remarks>
    private sealed class RangeVariantConstantRetyper : ExpressionVisitor
    {
        public static readonly RangeVariantConstantRetyper Instance = new();

        protected override Expression VisitConstant(ConstantExpression node)
            => node.Value is not null
               && node.Type.IsSealed
               && node.Type.BaseType is { IsAbstract: true } union
               && RangeTypeRegistry.TryGetByClrType(union, out _, out var isSet) && !isSet
                   ? Expression.Constant(node.Value, union)
                   : node;
    }

    /// <summary>
    /// Gives a range or multirange <c>a.CompareTo(b)</c> compared with a number the answer
    /// <see cref="Comparer{T}.Default"/> gives when <c>a</c> or <c>b</c> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EF Core rewrites <c>a.CompareTo(b) &gt; 0</c> to <c>a &gt; b</c>, and likewise <c>== 1</c>,
    /// <c>&gt;= 1</c>, <c>&gt; -1</c>, <c>!= -1</c> and the other forms whose answer it can read off
    /// the three results. In SQL, <c>a &gt; NULL</c> is <c>NULL</c>, so a null operand returned no
    /// rows for any of them where memory, in which <c>null</c> sorts first, returns every row for
    /// <c>&gt; 0</c> (#220, #256). A NULL column has no answer in memory at all: the call throws.
    /// The order here is <see cref="Comparer{T}.Default"/>'s (#261): two nulls compare equal, a null
    /// before any range.
    /// </para>
    /// <para>
    /// So the comparison <c>a.CompareTo(b) op k</c> is split by nullness, each case decided as
    /// <see cref="Comparer{T}.Default"/> decides it: both null, <c>0 op k</c>; only <c>a</c>,
    /// <c>-1 op k</c>; only <c>b</c>, <c>1 op k</c>; neither, the comparison itself, which EF Core still
    /// turns into the btree operator. <c>CompareTo</c> answers -1, 0 or 1, so the three constants are
    /// exact for any <c>k</c>. The null tests are reference comparisons, since a range record's
    /// <c>==</c> is value equality; a constant's is decided here, and EF Core folds a parameter's by
    /// its value and a non-nullable column's away, so the SQL for two non-null operands is the bare
    /// operator.
    /// </para>
    /// </remarks>
    private sealed class NullOperandCompareToRewriter : ExpressionVisitor
    {
        public static readonly NullOperandCompareToRewriter Instance = new();

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (!IsComparison(node.NodeType)) return base.VisitBinary(node);

            var (call, onLeft) = (CompareToCall(node.Left), CompareToCall(node.Right)) switch
                                 {
                                     ({ } left, _)  => (left, true),
                                     (_, { } right) => (right, false),
                                     _              => (null, false)
                                 };

            if (call is null) return base.VisitBinary(node);

            var receiver = Visit(call.Object)!;
            var operand  = Visit(call.Arguments[0]);
            var other    = Visit(onLeft ? node.Right : node.Left);

            Expression Compare(Expression result)
                => onLeft
                       ? Expression.MakeBinary(node.NodeType, result, other)
                       : Expression.MakeBinary(node.NodeType, other, result);

            var comparison = Compare(call.Update(receiver, [operand]));

            var receiverNull = IsNull(receiver);
            var operandNull  = IsNull(operand);

            // Comparer<T>.Default: (null, null) is 0, (null, x) is -1, (x, null) is 1.
            return Or(Or(And(And(receiverNull, operandNull), Decide(Compare(Expression.Constant(0)))),
                         And(And(receiverNull, Not(operandNull)), Decide(Compare(Expression.Constant(-1))))),
                      Or(And(And(Not(receiverNull), operandNull), Decide(Compare(Expression.Constant(1)))),
                         And(And(Not(receiverNull), Not(operandNull)), comparison)));
        }

        /// <summary>
        /// A <c>CompareTo</c> that is not compared with a number — projected, ordered by, added to —
        /// answers the number itself. EF Core writes it as <c>CASE WHEN a = b THEN 0 WHEN a &gt; b THEN 1
        /// WHEN a &lt; b THEN -1 END</c>, which is <c>NULL</c> for a null operand, and reading that NULL into
        /// an <see cref="int"/> threw where memory answers 1 (#253). So it becomes the same split by
        /// nullness as a comparison, as one conditional: EF Core writes that as one <c>CASE</c>, and the
        /// branches a constant or a non-nullable column rules out fold away.
        /// </summary>
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (CompareToCall(node) is not { } call) return base.VisitMethodCall(node);

            var receiver = Visit(call.Object)!;
            var operand  = Visit(call.Arguments[0]);
            var compared = call.Update(receiver, [operand]);

            var receiverNull = IsNull(receiver);
            var operandNull  = IsNull(operand);

            // Comparer<T>.Default: (null, null) is 0, (null, x) is -1, (x, null) is 1.
            return Choose(receiverNull,
                          Choose(operandNull, Expression.Constant(0), Expression.Constant(-1)),
                          Choose(operandNull, Expression.Constant(1), compared));
        }

        private static Expression Choose(Expression test, Expression whenTrue, Expression whenFalse)
            => test is ConstantExpression { Value: bool value }
                   ? value ? whenTrue : whenFalse
                   : Expression.Condition(test, whenTrue, whenFalse);

        private static bool IsComparison(ExpressionType type)
            => type is ExpressionType.Equal or ExpressionType.NotEqual
                    or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
                    or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual;

        private static MethodCallExpression? CompareToCall(Expression expression)
            => expression is MethodCallExpression
               {
                   Method: { Name: nameof(IComparable<int>.CompareTo), IsStatic: false, DeclaringType: { } declaringType } method,
                   Object: not null,
                   Arguments: [{ Type.IsValueType: false } argument]
               } call
               && method.ReturnType == typeof(int)
               && argument.Type != typeof(object)
               && RangeTypeRegistry.TryGetByClrType(declaringType, out _, out _)
                   ? call
                   : null;

        private static Expression IsNull(Expression expression)
            => expression is ConstantExpression constant
                   ? Expression.Constant(constant.Value is null)
                   : Expression.ReferenceEqual(expression, Expression.Constant(null, expression.Type));

        // A comparison of two constants (-1 op 0) is decided now; one with a parameter or a column
        // as the number is left for EF Core.
        private static Expression Decide(Expression comparison)
            => comparison is BinaryExpression { Left: ConstantExpression, Right: ConstantExpression }
                   ? Expression.Constant(Expression.Lambda<Func<bool>>(comparison).Compile(preferInterpretation: true)())
                   : comparison;

        private static Expression Not(Expression expression)
            => expression is ConstantExpression { Value: bool value } ? Expression.Constant(!value) : Expression.Not(expression);

        private static Expression And(Expression left, Expression right)
            => (left, right) switch
               {
                   (ConstantExpression { Value: false }, _) or (_, ConstantExpression { Value: false }) => Expression.Constant(false),
                   (ConstantExpression { Value: true }, _) => right,
                   (_, ConstantExpression { Value: true }) => left,
                   _                                       => Expression.AndAlso(left, right)
               };

        private static Expression Or(Expression left, Expression right)
            => (left, right) switch
               {
                   (ConstantExpression { Value: true }, _) or (_, ConstantExpression { Value: true }) => Expression.Constant(true),
                   (ConstantExpression { Value: false }, _) => right,
                   (_, ConstantExpression { Value: false }) => left,
                   _                                        => Expression.OrElse(left, right)
               };
    }

    /// <summary>
    /// Turns <c>set.Contains(x)</c> on a <see cref="RangeSet{TRange,T}"/> into the static
    /// <see cref="RangeSetContains"/> call that stands for it.
    /// </summary>
    /// <remarks>
    /// EF Core 11 (and an EF Core 10 patch, dotnet/efcore#37081) rewrites every method named
    /// <c>Contains</c> on an <see cref="IReadOnlyCollection{T}"/> into <c>Queryable.Contains</c> over
    /// the element type before translation. A <see cref="RangeSet{TRange,T}"/> is a read-only list of
    /// its ranges, so <c>Contains(T)</c>, <c>Contains(IRange&lt;T&gt;)</c> and <c>Contains(RangeSet)</c>
    /// all matched, and the two whose operand is not a range failed inside EF Core with an
    /// <see cref="ArgumentException"/> before any translator ran (#287). The static form has a
    /// declaring type that is not a collection, which EF Core's rewrite does not touch;
    /// <see cref="ValueRangesMethodCallTranslator"/> translates it to <c>@&gt;</c> as before.
    /// </remarks>
    private sealed class RangeSetContainsRewriter : ExpressionVisitor
    {
        public static readonly RangeSetContainsRewriter Instance = new();

        protected override Expression VisitMethodCall(MethodCallExpression node)
            => node is { Object: { } set, Arguments: [var operand] } && RangeSetContains.Equivalent(node.Method) is { } equivalent
                   ? Expression.Call(equivalent, Visit(set), Visit(operand))
                   : base.VisitMethodCall(node);
    }

    /// <summary>
    /// Turns <c>comparer.Compare(x, y)</c> over two ranges or two sets into
    /// <see cref="RangeComparerCall.RunInMemory{TRange}"/>, which EF Core cannot translate: a filter or an
    /// ordering then fails translation, and a final projection runs the comparer in memory (#385).
    /// </summary>
    /// <remarks>
    /// <para>
    /// EF Core translates any method named <c>Compare</c> with two operands as the natural comparison of
    /// the operands, whatever the comparer (dotnet/efcore#39402). For a range that is the range order, the
    /// one <c>CompareTo</c> runs in. <see cref="RangeLowerBoundComparer{TRange,T}"/> orders by the lower
    /// bound alone, so <c>[2026-03-01,)</c> and <c>[2026-03-01,2026-03-03]</c> compare equal to
    /// <c>[2026-03-01,2026-03-10]</c> in memory and as 1 and -1 in SQL, and
    /// <c>Where(… Compare(r.Period, p) == 0)</c> selected one row of the three without an error.
    /// </para>
    /// <para>
    /// By the time a query compiles, the comparer is a query parameter. Typed as
    /// <see cref="IComparer{T}"/>, as <see cref="RangeSet{TRange,T}.LowerBoundComparer"/> is, its value is
    /// unknown, and one compiled query serves every comparer the same code passes. So every comparer over a
    /// range or set type is kept out of SQL, <see cref="Comparer{T}.Default"/> included: <c>CompareTo</c>
    /// is the call that runs in SQL in the range order.
    /// </para>
    /// </remarks>
    private sealed class RangeComparerRewriter : ExpressionVisitor
    {
        public static readonly RangeComparerRewriter Instance = new();

        protected override Expression VisitMethodCall(MethodCallExpression node)
            => node is { Object: { } comparer, Method: { Name: nameof(IComparer<>.Compare) } method, Arguments: [var x, var y] }
               && method.ReturnType == typeof(int)
               && method.GetParameters() is [{ ParameterType: var compared }, { ParameterType: var other }]
               && compared == other
               && RangeTypeRegistry.TryGetByClrType(compared, out _, out _)
               && typeof(IComparer<>).MakeGenericType(compared).IsAssignableFrom(comparer.Type)
                   ? Expression.Call(RangeComparerCall.RunInMemoryMethod.MakeGenericMethod(compared), Visit(comparer), Visit(x), Visit(y))
                   : base.VisitMethodCall(node);
    }

    private sealed class RangeSetOperatorRewriter : ExpressionVisitor
    {
        public static readonly RangeSetOperatorRewriter Instance = new();

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.Method is { DeclaringType: { IsGenericType: true } declaringType } operatorMethod
             && declaringType.GetGenericTypeDefinition() == typeof(RangeSet<,>)
             && OperatorEquivalent(operatorMethod.Name) is { } methodName)
            {
                // Both Union/Intersect/Except overloads (range and set operand) exist;
                // the operator's second parameter type picks the matching one.
                var equivalent = declaringType.GetMethod(
                    methodName, [operatorMethod.GetParameters()[1].ParameterType])!;

                return Expression.Call(Visit(node.Left), equivalent, Visit(node.Right));
            }

            return base.VisitBinary(node);
        }

        private static string? OperatorEquivalent(string operatorMethodName)
            => operatorMethodName switch
               {
                   "op_BitwiseOr"   => nameof(RangeSet<,>.Union),
                   "op_BitwiseAnd"  => nameof(RangeSet<,>.Intersect),
                   "op_Subtraction" => nameof(RangeSet<,>.Except),
                   _                => null
               };
    }
}