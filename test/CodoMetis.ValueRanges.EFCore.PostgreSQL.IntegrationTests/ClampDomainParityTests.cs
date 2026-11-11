using System.Globalization;
using System.Linq.Expressions;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>Clamp</c> runs as SQL on every range type and answers what memory answers over the loaded rows (#486),
/// as <see cref="ClampParityTests"/> holds it for the three types #406 translated.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rows</b> are every range the type's factories build from a handful of points, each paired with each
/// point as the value: empty, <c>(,)</c>, both unbounded forms with either inclusiveness, and every finite
/// range between two points with every inclusiveness. The points include the type's extremes, which Npgsql
/// writes as <c>±infinity</c> for the temporal types, so a bound that stands for an infinity is clamped to
/// as memory reads it (<c>docs/architecture.md#domain-width-and-the-boundary</c>).
/// </para>
/// <para>
/// <b>Query forms</b> go through <see cref="CompositionRunner.Judge"/>: a value column, every point as a
/// parameter and as an inlined constant, and a nullable range or value column NULL on some rows, each
/// tested against <see langword="null"/> both ways and projected, plus an equality predicate. The
/// composition over joins and the other predicates is type-independent and stays in
/// <see cref="ClampParityTests"/>.
/// </para>
/// <para>
/// <b>Values are compared exactly</b> as well, and in both orderings. <see cref="object.Equals(object)"/>
/// takes <c>-0</c> for <c>0</c>, a <see cref="DateTimeOffset"/> for any offset of the same instant and a
/// <see cref="DateTime"/> of any kind; <see cref="Exact"/> does not.
/// </para>
/// <para>
/// <b>Not in parallel.</b> Each test seeds a database of its own; run beside the rest of the suite, their
/// pools would add a dozen databases' connections to the shared server's limit at once.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ClampDomainParityTests
{
    [TestMethod]
    public Task Float64Range_ClampsAsMemoryDoes() => Run<Float64Range, double>(
        "float8", [double.MinValue, -1.5, -0.0, 0.0, 2.25, double.MaxValue]);

    [TestMethod]
    public Task Float32Range_ClampsAsMemoryDoes() => Run<Float32Range, float>(
        "float4", [float.MinValue, -1.5f, -0.0f, 0.0f, 2.25f, float.MaxValue]);

    [TestMethod]
    public Task DateRange_ClampsAsMemoryDoes() => Run<DateRange, DateOnly>(
        "date", [DateOnly.MinValue, new(1, 1, 2), new(2026, 1, 1), new(2026, 1, 15), new(9999, 12, 30), DateOnly.MaxValue]);

    [TestMethod]
    public Task DateTimeRange_ClampsAsMemoryDoes() => Run<DateTimeRange, DateTime>(
        "timestamp",
        [DateTime.MinValue, new(2026, 1, 1), new(2026, 1, 1, 12, 30, 0, 123, 456), new(9999, 12, 31, 23, 59, 59, 999, 999), DateTime.MaxValue]);

    [TestMethod]
    public Task DateTimeOffsetRange_ClampsAsMemoryDoes() => Run<DateTimeOffsetRange, DateTimeOffset>(
        "timestamptz",
        [DateTimeOffset.MinValue, new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 1, 1, 12, 30, 0, 123, 456, TimeSpan.Zero), DateTimeOffset.MaxValue]);

    [TestMethod]
    public Task TimeRange_ClampsAsMemoryDoes() => Run<TimeRange, TimeOnly>(
        "time", [TimeOnly.MinValue, new(8, 0), new(12, 30, 0, 500), new(23, 59, 59, 999, 999), TimeOnly.MaxValue]);

    [TestMethod]
    public Task LocalDateRange_ClampsAsMemoryDoes() => Run<LocalDateRange, LocalDate>(
        "localdate",
        [LocalDate.MinIsoValue, new(1, 1, 1), new(2026, 1, 1), new(2026, 1, 15), new(9999, 12, 30), LocalDate.MaxIsoValue],
        // A plain date column can't hold LocalDate.MinIsoValue (-9998-01-01): Npgsql writes it as a date.
        storable: value => value != LocalDate.MinIsoValue,
        // An exclusive bound at it starts on -9998-01-02, a real date below PostgreSQL's 4714 BC: the
        // domain boundary (docs/architecture.md#domain-width-and-the-boundary), not an infinity.
        storableRange: range => range.LowerBound() is not { } lower || lower == LocalDate.MinIsoValue || lower.Year > -4713);

    [TestMethod]
    public Task LocalDateTimeRange_ClampsAsMemoryDoes() => Run<LocalDateTimeRange, LocalDateTime>(
        "localdatetime",
        [LocalDateTime.MinIsoValue, new(2026, 1, 1, 0, 0), new LocalDateTime(2026, 1, 1, 12, 30).PlusNanoseconds(123_456_000), LocalDateTime.MaxIsoValue]);

    [TestMethod]
    public Task InstantRange_ClampsAsMemoryDoes() => Run<InstantRange, Instant>(
        "instant",
        [Instant.MinValue, Instant.FromUtc(2026, 1, 1, 0, 0), Instant.FromUtc(2026, 1, 1, 12, 30).PlusNanoseconds(123_456_000), Instant.MaxValue]);

    [TestMethod]
    public Task YearMonthRange_ClampsAsMemoryDoes() => Run<YearMonthRange, YearMonth>(
        "yearmonth",
        [new(1, 1), new(2026, 1), new(2026, 3), new(2026, 7), new(9999, 11), new(9999, 12)],
        // YearMonth has no column mapping of its own, only inside a range or set: values are operands only.
        storable: _ => false);

    /// <summary>
    /// Every range type is measured here, or by <see cref="ClampParityTests"/> for the three #406 translated, so a
    /// range type added later is not translated by <c>Clamp</c> without its parity being shown.
    /// </summary>
    [TestMethod]
    public void EveryRangeType_HasAClampParityTest()
    {
        Type[] byClampParityTests = [typeof(Int32Range), typeof(Int64Range), typeof(DecimalRange)];
        var rangeTypes = new[] { typeof(Int32Range).Assembly, typeof(LocalDateRange).Assembly }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsAbstract && type.GetInterfaces().Any(i => i.IsGenericType
                                                                         && i.GetGenericTypeDefinition() == typeof(IRangeFactory<,>)
                                                                         && i.GenericTypeArguments[0] == type))
            .ToList();
        Assert.IsTrue(rangeTypes.Count >= 13, $"Discovered only {rangeTypes.Count} range types: the discovery is broken.");

        var missing = rangeTypes
            .Where(type => !byClampParityTests.Contains(type)
                        && typeof(ClampDomainParityTests).GetMethod($"{type.Name}_ClampsAsMemoryDoes") is null)
            .Select(type => type.Name)
            .ToList();

        Assert.AreEqual(0, missing.Count, $"Clamp runs as SQL on every range type, but these have no parity test here: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// A <see cref="DateTime"/> property is <c>timestamp with time zone</c> by Npgsql's default, and a
    /// <see cref="DateTimeRange"/> a <c>tsrange</c>. <c>GREATEST</c>/<c>LEAST</c> would compare them through the
    /// session's time zone, so <c>Clamp</c> declines the pair: a filter fails translation, and a final projection
    /// runs in memory, with memory's answer whatever the session's zone.
    /// </summary>
    [TestMethod]
    public async Task DateTimeRange_OfATimestamptzValue_RunsInMemory_WhateverTheSessionZone()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await CompositionDatabase.Create("clamp_zoned");
        var stay = DateTimeRange.CreateFinite(new DateTime(2026, 1, 1, 8, 0, 0), new DateTime(2026, 1, 1, 18, 0, 0));
        var at = new DateTime(2026, 1, 1, 17, 30, 0, DateTimeKind.Utc);
        await using (var seed = new ZonedContext(connectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Rows.Add(new ZonedRow { Id = 1, Stay = stay, At = at });
            await seed.SaveChangesAsync();
        }

        foreach (var zone in new[] { "UTC", "Europe/Berlin", "America/Los_Angeles" })
        {
            var session = new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { Timezone = zone }.ConnectionString;
            await using var context = new ZonedContext(session);

            Assert.AreEqual(stay.Clamp(at), await context.Rows.Select(r => r.Stay.Clamp(r.At)).SingleAsync(), $"Select in a {zone} session");
            var e = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => context.Rows.Where(r => r.Stay.Clamp(r.At) == r.At).ToListAsync(), $"Where in a {zone} session");
            StringAssert.Contains(e.Message, "could not be translated");
            ClearPool(session);
        }

        ClearPool(connectionString);
    }

    /// <summary>
    /// A value with more precision than the column comes back at the store's microsecond, as any value sent to
    /// PostgreSQL does: memory returns the operand it was given, the server the operand it was bound.
    /// </summary>
    [TestMethod]
    public async Task Clamp_ReturnsAnOperandAtTheStoresMicrosecond()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await CompositionDatabase.Create("clamp_precision");
        await using var context = new ClampDomainContext<TimeRange, TimeOnly>(connectionString, valueColumn: true);
        await context.Database.EnsureCreatedAsync();
        context.Rows.Add(new ClampOf<TimeRange, TimeOnly> { Id = 1, Range = TimeRange.CreateUnboundedEnd(new TimeOnly(9, 0), true), Value = new TimeOnly(9, 0) });
        await context.SaveChangesAsync();

        var endOfDay = TimeOnly.MaxValue;
        var server = await context.Rows.Select(r => r.Range.Clamp(endOfDay)).SingleAsync();

        ClearPool(connectionString);
        Assert.AreEqual(new TimeOnly(23, 59, 59, 999, 999), server, "the bound parameter, truncated to the microsecond");
        Assert.AreEqual(TimeOnly.MaxValue, TimeRange.CreateUnboundedEnd(new TimeOnly(9, 0), true).Clamp(endOfDay), "memory's operand, every tick");
    }

    private static async Task Run<TRange, T>(string name, T[] points, Func<T, bool>? storable = null, Func<TRange, bool>? storableRange = null)
        where TRange : class, IRange<T>, IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await CompositionDatabase.Create($"clamp_{name}");
        var values = points.Where(storable ?? (_ => true)).ToArray();
        var valueColumn = values.Length > 0;
        var cases = Cases<TRange, T>(points, valueColumn ? values : [default]).Where(c => storableRange?.Invoke(c.Range) ?? true).ToList();

        await using (var seed = new ClampDomainContext<TRange, T>(connectionString, valueColumn))
        {
            await seed.Database.EnsureCreatedAsync();
            for (var i = 0; i < cases.Count; i++)
            {
                seed.Rows.Add(new ClampOf<TRange, T>
                {
                    Id         = i + 1,
                    Range      = cases[i].Range,
                    Value      = cases[i].Value,
                    MaybeRange = i % 3 == 0 ? null : cases[(i + 7) % cases.Count].Range,
                    MaybeValue = i % 3 == 1 ? null : cases[(i + 11) % cases.Count].Value
                });
            }

            await seed.SaveChangesAsync();
        }

        await using var context = new ClampDomainContext<TRange, T>(connectionString, valueColumn);
        var loaded = await context.Rows.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        Assert.AreEqual(cases.Count, loaded.Count);

        var failures = new List<string>();
        string Describe(int id)
        {
            var r = loaded.Single(row => row.Id == id);
            return valueColumn
                ? $"{id} ({r.Range} / {Show(r.Value)}, maybe {r.MaybeRange?.ToString() ?? "NULL"} / {Show(r.MaybeValue)})"
                : $"{id} ({r.Range}, maybe {r.MaybeRange?.ToString() ?? "NULL"})";
        }

        var calls = Calls<TRange, T>(points, valueColumn).ToList();
        foreach (var (call, lambda) in calls)
        {
            var outcome = await CompositionRunner.Judge(
                $"clamp {name}", new Call(call, lambda.Body), lambda.Parameters[0], context.Rows.AsNoTracking(), loaded,
                r => r.Id, Describe, failures);
            if (outcome != Outcome.Compared) failures.Add($"{call} [clamp {name}]: {outcome}, where it must run as SQL and be compared");
        }

        foreach (var (call, lambda) in calls)
        {
            if (lambda is not Expression<Func<ClampOf<TRange, T>, T?>> clamp || call.Contains("Maybe", StringComparison.Ordinal)) continue;

            var compiled = clamp.Compile();
            var row = clamp.Parameters[0];
            var projection = Expression.Lambda<Func<ClampOf<TRange, T>, Clamped<T>>>(
                Expression.MemberInit(
                    Expression.New(typeof(Clamped<T>)),
                    Expression.Bind(typeof(Clamped<T>).GetProperty(nameof(Clamped<T>.Id))!, Expression.Property(row, nameof(ClampOf<TRange, T>.Id))),
                    Expression.Bind(typeof(Clamped<T>).GetProperty(nameof(Clamped<T>.Value))!, clamp.Body)),
                row);

            foreach (var server in (await context.Rows.Select(projection).ToListAsync()).OrderBy(s => s.Id))
            {
                var memory = compiled(loaded.Single(r => r.Id == server.Id));
                if (!Exact(memory, server.Value))
                    failures.Add($"{call} [clamp {name}] exact, row {Describe(server.Id)}: server {Show(server.Value)}, memory {Show(memory)}");
            }

            foreach (var descending in new[] { false, true })
            {
                var sorted = descending
                    ? await context.Rows.OrderByDescending(clamp).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync()
                    : await context.Rows.OrderBy(clamp).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync();
                var expected = descending
                    ? loaded.OrderBy(r => compiled(r) is not null).ThenByDescending(compiled).ThenBy(r => r.Id).Select(r => r.Id).ToList()
                    : loaded.OrderBy(r => compiled(r) is null).ThenBy(compiled).ThenBy(r => r.Id).Select(r => r.Id).ToList();
                if (!sorted.SequenceEqual(expected))
                    failures.Add($"OrderBy{(descending ? "Descending" : "")}({call}) [clamp {name}]: server {string.Join(",", sorted)}, memory {string.Join(",", expected)}");
            }
        }

        // Each test has a database of its own, so its idle connections would otherwise stay open beside every other's.
        ClearPool(connectionString);
        Assert.AreEqual(0, failures.Count, $"{failures.Count} disagreements:{Environment.NewLine}" + string.Join(Environment.NewLine, failures.Take(80)));
    }

    private static void ClearPool(string connectionString)
    {
        using var connection = new Npgsql.NpgsqlConnection(connectionString);
        Npgsql.NpgsqlConnection.ClearPool(connection);
    }

    private static IEnumerable<(string Name, LambdaExpression Lambda)> Calls<TRange, T>(T[] points, bool valueColumn)
        where TRange : class, IRange<T>, IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        (string, LambdaExpression) Value(string name, Expression<Func<ClampOf<TRange, T>, T?>> lambda) => (name, lambda);

        if (valueColumn)
        {
            yield return Value("r.Range.Clamp(r.Value)", r => r.Range.Clamp(r.Value));
            yield return Value("r.MaybeRange.Clamp(r.Value)", r => r.MaybeRange!.Clamp(r.Value));
            yield return Value("r.Range.Clamp(r.MaybeValue.Value)", r => r.Range.Clamp(r.MaybeValue!.Value));

            // r.Range.Clamp(r.Value) == r.Value, through the element type's own operator.
            Expression<Func<ClampOf<TRange, T>, T?>> clamp = r => r.Range.Clamp(r.Value);
            var row = clamp.Parameters[0];
            yield return ("r.Range.Clamp(r.Value) == r.Value", Expression.Lambda<Func<ClampOf<TRange, T>, bool>>(
                Expression.Equal(clamp.Body, Expression.Convert(Expression.Property(row, nameof(ClampOf<TRange, T>.Value)), typeof(T?))), row));
        }

        // Each point as an operand, at the precision it reaches the server with (Clamp_ReturnsAnOperandAtTheStoresMicrosecond).
        foreach (var point in points.Select(Bound).Distinct())
        {
            Expression<Func<ClampOf<TRange, T>, T?>> parameter = r => r.Range.Clamp(point);
            yield return Value($"r.Range.Clamp(@{Show(point)})", parameter);
            yield return Value($"r.MaybeRange.Clamp(@{Show(point)})", r => r.MaybeRange!.Clamp(point));

            // EF Core caches a compiled query by its shape and compares an inlined constant with Equals, under
            // which -0 equals 0: whichever zero compiles first is reused for the other, sign included. That is
            // EF Core's, for any float constant; a parameter carries its own sign, so -0 is covered above.
            if (IsNegativeZero(point)) continue;
            yield return ($"r.Range.Clamp({Show(point)})", Expression.Lambda(new Inline().Visit(parameter.Body), parameter.Parameters));
        }

        // The clamped value compared with every point in a filter, where it meets a bound the way a point is bound,
        // not as read back: a YearMonthRange's upper bound read as the last day of its month projected as that
        // month, and compared unequal to it (#506).
        var bound = points.Select(Bound).Distinct().ToArray();
        foreach (var point in bound)
        foreach (var other in bound)
        {
            Expression<Func<ClampOf<TRange, T>, T?>> clamp = r => r.Range.Clamp(point);
            Expression<Func<T?>> operand = () => other;
            var row = clamp.Parameters[0];
            foreach (var (op, compare) in new (string, Func<Expression, Expression, BinaryExpression>)[]
                     { ("==", Expression.Equal), ("<", Expression.LessThan), (">", Expression.GreaterThan) })
            {
                yield return ($"r.Range.Clamp(@{Show(point)}) {op} @{Show(other)}",
                              Expression.Lambda<Func<ClampOf<TRange, T>, bool>>(compare(clamp.Body, operand.Body), row));
            }
        }
    }

    /// <summary>
    /// <paramref name="value"/> as Npgsql binds it: a temporal value at the store's microsecond, truncated toward
    /// 2000-01-01 (<c>docs/efcore.md</c>). Only <see cref="TimeOnly.MaxValue"/> among the points has a finer tick;
    /// the other extremes stand for an infinity and travel exactly.
    /// </summary>
    private static T Bound<T>(T value) where T : struct
        => value is TimeOnly time ? (T)(object)new TimeOnly(time.Ticks / 10 * 10) : value;

    private static bool IsNegativeZero<T>(T value) where T : struct
        => value is double d && d == 0 && double.IsNegative(d) || value is float f && f == 0 && float.IsNegative(f);

    /// <summary>Every range the factories build from <paramref name="points"/>, each paired with every value.</summary>
    private static IEnumerable<(TRange Range, T Value)> Cases<TRange, T>(T[] points, T[] values)
        where TRange : class, IRange<T>, IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var ranges = new List<TRange> { TRange.Empty, TRange.Infinite };
        foreach (var point in points)
        {
            foreach (var inclusive in new[] { true, false })
            {
                ranges.AddRange(Build(() => TRange.CreateUnboundedStart(point, inclusive)));
                ranges.AddRange(Build(() => TRange.CreateUnboundedEnd(point, inclusive)));
            }
        }

        for (var i = 0; i < points.Length; i++)
        {
            for (var j = i; j < points.Length; j++)
            {
                foreach (var (startInclusive, endInclusive) in new[] { (true, true), (true, false), (false, true), (false, false) })
                    ranges.AddRange(Build(() => TRange.CreateFinite(points[i], points[j], startInclusive, endInclusive)));
            }
        }

        return ranges.Distinct().SelectMany(range => values.Select(value => (range, value)));

        static IEnumerable<TRange> Build(Func<TRange> factory)
        {
            TRange? built = null;
            try { built = factory(); }
            catch (ArgumentException) { }
            if (built is not null) yield return built;
        }
    }

    /// <summary>Equal in every observable way: the sign of a zero, a <see cref="DateTime"/>'s ticks, a <see cref="DateTimeOffset"/>'s offset.</summary>
    private static bool Exact<T>(T? left, T? right) where T : struct => (left, right) switch
    {
        (null, null)                              => true,
        ({ } l, { } r) when l is double d         => BitConverter.DoubleToInt64Bits(d) == BitConverter.DoubleToInt64Bits((double)(object)r),
        ({ } l, { } r) when l is float f          => BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits((float)(object)r),
        ({ } l, { } r) when l is DateTimeOffset o => o.EqualsExact((DateTimeOffset)(object)r),
        ({ } l, { } r)                            => l.Equals(r),
        _                                         => false
    };

    private static string Show<T>(T? value) where T : struct => value switch
    {
        null             => "NULL",
        double d         => d.ToString("R", CultureInfo.InvariantCulture),
        float f          => f.ToString("R", CultureInfo.InvariantCulture),
        DateTime t       => t.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset o => o.ToString("O", CultureInfo.InvariantCulture),
        { } v            => v.ToString() ?? "NULL"
    };

    private static string Show<T>(T value) where T : struct => Show((T?)value);

    /// <summary>Turns a captured variable into a constant, which EF Core inlines rather than binds.</summary>
    private sealed class Inline : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
            => node.Expression is ConstantExpression { Value: { } closure } && node.Member is System.Reflection.FieldInfo field
                ? Expression.Constant(field.GetValue(closure), node.Type)
                : base.VisitMember(node);
    }

    public sealed class ZonedRow
    {
        public int Id { get; set; }

        public DateTimeRange Stay { get; set; } = DateTimeRange.Empty;

        public DateTime At { get; set; }
    }

    private sealed class ZonedContext(string connectionString) : DbContext
    {
        public DbSet<ZonedRow> Rows => Set<ZonedRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ZonedRow>().Property(r => r.Id).ValueGeneratedNever();
    }

    public sealed class Clamped<T> where T : struct
    {
        public int Id { get; set; }

        public T? Value { get; set; }
    }

    public sealed class ClampOf<TRange, T>
        where TRange : class
        where T : struct
    {
        public int Id { get; set; }

        public TRange Range { get; set; } = null!;

        public T Value { get; set; }

        public TRange? MaybeRange { get; set; }

        public T? MaybeValue { get; set; }
    }

    private sealed class ClampDomainContext<TRange, T>(string connectionString, bool valueColumn) : DbContext
        where TRange : class
        where T : struct
    {
        public DbSet<ClampOf<TRange, T>> Rows => Set<ClampOf<TRange, T>>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(
                connectionString,
                npgsql => npgsql.UseValueRangesNodaTime().ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresRange("timerange", "time");
            modelBuilder.HasPostgresRange("float8range", "double precision");
            modelBuilder.HasPostgresRange("float4range", "real");
            modelBuilder.Entity<ClampOf<TRange, T>>(row =>
            {
                row.ToTable("clamp_rows");
                row.Property(r => r.Id).ValueGeneratedNever();
                if (!valueColumn)
                {
                    row.Ignore(r => r.Value);
                    row.Ignore(r => r.MaybeValue);
                }
                else if (typeof(T) == typeof(DateTime))
                {
                    // A DateTimeRange is a tsrange, so its values are timestamps without a zone, not Npgsql's default timestamptz.
                    row.Property(r => r.Value).HasColumnType("timestamp without time zone");
                    row.Property(r => r.MaybeValue).HasColumnType("timestamp without time zone");
                }
            });
        }
    }
}
