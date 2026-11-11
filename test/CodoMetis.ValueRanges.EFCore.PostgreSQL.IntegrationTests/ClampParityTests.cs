using System.Linq.Expressions;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>Clamp</c> on <see cref="Int32Range"/>, <see cref="Int64Range"/> and <see cref="DecimalRange"/> runs as
/// SQL and answers what memory answers over the loaded rows (#406), for every condition on the 10.3.0 parity
/// list (#408).
/// </summary>
/// <remarks>
/// <para>
/// <b>Shapes and extremes</b> are the rows: empty, finite, unbounded on either side, <c>(,)</c>, a single
/// value, an exclusive bound a discrete type closes (<c>(0,5]</c> is <c>[1,5]</c>), the widest ranges each
/// integer type can store with the element type's minimum and maximum as values, and <c>numrange</c> bounds
/// exclusive on either side, which <c>Clamp</c> returns although the range does not contain them. Decimals
/// are compared by value, so <c>2.500</c> as stored equals memory's <c>2.5</c>.
/// </para>
/// <para>
/// <b>Nullable operands and composition</b> go through <see cref="CompositionRunner.Judge"/>: a nullable
/// range column, a nullable value column read with <c>.Value</c>, and the child of an outer join on either
/// side, each NULL on some rows so rule (c) answers. Each value runs in <c>Where(== null)</c>,
/// <c>Where(!= null)</c> and a final <c>Select</c>, and each predicate built on it in <c>Where</c>,
/// <c>Where(!)</c> and <c>Select</c>, with the filters and the projection agreeing with each other (the
/// declared nullability). The composition sweeps run over the <see cref="DateRange"/> family, whose
/// <c>Clamp</c> stays in memory, so they do not reach these three.
/// </para>
/// <para>
/// <b>Ordering</b> puts NULL where PostgreSQL does (#359), as in <c>IntegerLengthParityTests</c>.
/// </para>
/// </remarks>
[TestClass]
public sealed class ClampParityTests
{
    private static readonly (Int32Range Range, int Value)[] Seats =
    [
        (Int32Range.Empty, 5),
        (Int32Range.CreateFinite(1, 10), 15),
        (Int32Range.CreateFinite(1, 10), -5),
        (Int32Range.CreateFinite(1, 10), 5),
        (Int32Range.CreateUnboundedStart(10, endInclusive: true), 15),
        (Int32Range.CreateUnboundedEnd(1), -5),
        (Int32Range.Infinite, 7),
        (Int32Range.CreateFinite(int.MinValue, int.MaxValue - 1), int.MaxValue),
        (Int32Range.CreateFinite(int.MinValue, int.MaxValue - 1), int.MinValue),
        (Int32Range.CreateFinite(0, 5, startInclusive: false, endInclusive: true), 0),
        (Int32Range.CreateFinite(int.MaxValue - 1, int.MaxValue - 1), 0),
        (Int32Range.CreateFinite(5, 5), 5),
    ];

    private static readonly (Int64Range Range, long Value)[] Tickets =
    [
        (Int64Range.CreateFinite(long.MinValue, long.MaxValue - 1), long.MaxValue),
        (Int64Range.CreateFinite(long.MinValue, long.MaxValue - 1), long.MinValue),
        (Int64Range.CreateFinite(1, 10), 15),
        (Int64Range.Infinite, -3),
        (Int64Range.Empty, 0),
        (Int64Range.CreateUnboundedStart(10, endInclusive: true), 5),
    ];

    private static readonly (DecimalRange Range, decimal Value)[] Prices =
    [
        (DecimalRange.CreateFinite(1.5m, 2.5m), 3m),
        (DecimalRange.CreateFinite(1.5m, 2.5m, startInclusive: false, endInclusive: true), 1m),
        (DecimalRange.CreateFinite(1.5m, 2.5m), 2m),
        (DecimalRange.Empty, 2m),
        (DecimalRange.CreateUnboundedStart(2.5m), 9m),
        (DecimalRange.CreateUnboundedEnd(1.50m), 0m),
        (DecimalRange.Infinite, 1.25m),
        (DecimalRange.CreateFinite(1.50m, 2.500m, startInclusive: true, endInclusive: true), 9.0m),
    ];

    [TestMethod]
    public async Task Clamp_InEveryQueryForm_AnswersWhatMemoryAnswers()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await Seed();

        await using var context = new ClampContext(connectionString);
        var loaded = await context.Rows.Include(r => r.Child).AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        Assert.IsTrue(loaded.Any(r => r.MaybeSeats is null) && loaded.Any(r => r.MaybeValue is null) && loaded.Any(r => r.MaybePrice is null)
                   && loaded.Any(r => r.MaybeAmount is null) && loaded.Any(r => r.Child is null),
                      "Some rows must hold a NULL column and some no child, or rule (c) is never reached.");

        var failures = new List<string>();
        foreach (var (name, lambda) in Calls())
        {
            var outcome = await CompositionRunner.Judge(
                "clamp", new Call(name, lambda.Body), lambda.Parameters[0], context.Rows.AsNoTracking(), loaded,
                r => r.Id, id => $"{id} ({Describe(loaded.Single(r => r.Id == id))})", failures);

            if (outcome != Outcome.Compared) failures.Add($"{name}: {outcome}, where it must run as SQL and be compared");
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public async Task Clamp_InAnOrdering_SortsAsMemoryDoes_WithNullWherePostgresPutsIt()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await Seed();

        await using var context = new ClampContext(connectionString);
        var loaded = await context.Rows.AsNoTracking().ToListAsync();

        async Task Agree<TKey>(string label, Expression<Func<ClampRow, TKey?>> key, bool descending) where TKey : struct
        {
            var compiled = key.Compile();
            var server = descending
                ? await context.Rows.OrderByDescending(key).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync()
                : await context.Rows.OrderBy(key).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync();
            var memory = descending
                ? loaded.OrderBy(r => compiled(r) is not null).ThenByDescending(compiled).ThenBy(r => r.Id).Select(r => r.Id).ToList()
                : loaded.OrderBy(r => compiled(r) is null).ThenBy(compiled).ThenBy(r => r.Id).Select(r => r.Id).ToList();

            CollectionAssert.AreEqual(memory, server, $"{label}: server {string.Join(",", server)}, memory {string.Join(",", memory)}");
        }

        await Agree("OrderBy(Seats.Clamp(Value))", r => r.Seats.Clamp(r.Value), descending: false);
        await Agree("OrderByDescending(Seats.Clamp(Value))", r => r.Seats.Clamp(r.Value), descending: true);
        await Agree("OrderBy(Tickets.Clamp(Big))", r => r.Tickets.Clamp(r.Big), descending: false);
        await Agree("OrderBy(Price.Clamp(Amount))", r => r.Price.Clamp(r.Amount), descending: false);
        await Agree("OrderByDescending(Price.Clamp(Amount))", r => r.Price.Clamp(r.Amount), descending: true);
    }

    private static IEnumerable<(string Name, LambdaExpression Lambda)> Calls()
    {
        (string, LambdaExpression) Int(string name, Expression<Func<ClampRow, int?>> lambda) => (name, lambda);
        (string, LambdaExpression) Long(string name, Expression<Func<ClampRow, long?>> lambda) => (name, lambda);
        (string, LambdaExpression) Dec(string name, Expression<Func<ClampRow, decimal?>> lambda) => (name, lambda);
        (string, LambdaExpression) Predicate(string name, Expression<Func<ClampRow, bool>> lambda) => (name, lambda);

        yield return Int("r.Seats.Clamp(r.Value)", r => r.Seats.Clamp(r.Value));
        yield return Int("r.Seats.Clamp(15)", r => r.Seats.Clamp(15));
        yield return Int("r.MaybeSeats.Clamp(r.Value)", r => r.MaybeSeats!.Clamp(r.Value));
        yield return Int("r.Seats.Clamp(r.MaybeValue.Value)", r => r.Seats.Clamp(r.MaybeValue!.Value));
        yield return Int("r.Child.Seats.Clamp(r.Value)", r => r.Child!.Seats.Clamp(r.Value));
        yield return Int("r.Seats.Clamp(r.Child.Value)", r => r.Seats.Clamp(r.Child!.Value));
        yield return Long("r.Tickets.Clamp(r.Big)", r => r.Tickets.Clamp(r.Big));
        yield return Long("r.Tickets.Clamp(long.MaxValue)", r => r.Tickets.Clamp(long.MaxValue));
        yield return Dec("r.Price.Clamp(r.Amount)", r => r.Price.Clamp(r.Amount));
        yield return Dec("r.Price.Clamp(2.0)", r => r.Price.Clamp(2.0m));
        yield return Dec("r.MaybePrice.Clamp(r.Amount)", r => r.MaybePrice!.Clamp(r.Amount));
        yield return Dec("r.Price.Clamp(r.MaybeAmount.Value)", r => r.Price.Clamp(r.MaybeAmount!.Value));
        yield return Dec("r.Child.Price.Clamp(r.Amount)", r => r.Child!.Price.Clamp(r.Amount));

        yield return Predicate("r.Seats.Clamp(r.Value) == r.Value", r => r.Seats.Clamp(r.Value) == r.Value);
        yield return Predicate("r.Seats.Clamp(r.Value) > 5", r => r.Seats.Clamp(r.Value) > 5);
        yield return Predicate("r.Seats.Clamp(r.Value) == 10 && r.Seats.Contains(5)", r => r.Seats.Clamp(r.Value) == 10 && r.Seats.Contains(5));
        yield return Predicate("r.MaybeSeats.Clamp(r.Value) < 0 || r.Seats.IsEmpty()", r => r.MaybeSeats!.Clamp(r.Value) < 0 || r.Seats.IsEmpty());
        yield return Predicate("r.Tickets.Clamp(r.Big) < 0", r => r.Tickets.Clamp(r.Big) < 0);
        yield return Predicate("(r.Price.Clamp(r.Amount) ?? -1) > 2", r => (r.Price.Clamp(r.Amount) ?? -1m) > 2m);
        yield return Predicate("r.Price.Clamp(r.Amount) == r.Amount", r => r.Price.Clamp(r.Amount) == r.Amount);
    }

    private static string Describe(ClampRow row) =>
        $"Seats {row.Seats} / {row.Value}, MaybeSeats {row.MaybeSeats?.ToString() ?? "NULL"}, MaybeValue {row.MaybeValue?.ToString() ?? "NULL"}, "
      + $"Tickets {row.Tickets} / {row.Big}, Price {row.Price} / {row.Amount}, MaybePrice {row.MaybePrice?.ToString() ?? "NULL"}, "
      + $"MaybeAmount {row.MaybeAmount?.ToString() ?? "NULL"}, " + (row.Child is { } child ? $"child {child.Seats} / {child.Value}, {child.Price}" : "no child");

    /// <summary>A database of its own: one row per case, a NULL column every third row, a child every other row.</summary>
    private static async Task<string> Seed()
    {
        var connectionString = await CompositionDatabase.Create("clamp");
        await using var seed = new ClampContext(connectionString);
        await seed.Database.EnsureCreatedAsync();

        var count = Math.Max(Seats.Length, Math.Max(Tickets.Length, Prices.Length));
        for (var i = 0; i < count; i++)
        {
            var (seats, value)  = Seats[i % Seats.Length];
            var (tickets, big)  = Tickets[i % Tickets.Length];
            var (price, amount) = Prices[i % Prices.Length];
            seed.Rows.Add(new ClampRow
            {
                Id          = i + 1,
                Seats       = seats,
                Value       = value,
                MaybeSeats  = i % 3 == 0 ? null : Seats[(i + 1) % Seats.Length].Range,
                MaybeValue  = i % 3 == 1 ? null : Seats[(i + 2) % Seats.Length].Value,
                Tickets     = tickets,
                Big         = big,
                Price       = price,
                Amount      = amount,
                MaybePrice  = i % 3 == 2 ? null : Prices[(i + 1) % Prices.Length].Range,
                MaybeAmount = i % 3 == 0 ? null : Prices[(i + 2) % Prices.Length].Value,
                Child       = i % 2 == 0
                    ? new ClampChild { Id = 100 + i, Seats = Seats[(i + 3) % Seats.Length].Range, Value = Seats[(i + 4) % Seats.Length].Value, Price = Prices[(i + 3) % Prices.Length].Range }
                    : null
            });
        }

        await seed.SaveChangesAsync();
        return connectionString;
    }

    public sealed class ClampRow
    {
        public int Id { get; set; }

        public Int32Range Seats { get; set; } = Int32Range.Empty;

        public int Value { get; set; }

        public Int32Range? MaybeSeats { get; set; }

        public int? MaybeValue { get; set; }

        public Int64Range Tickets { get; set; } = Int64Range.Empty;

        public long Big { get; set; }

        public DecimalRange Price { get; set; } = DecimalRange.Empty;

        public decimal Amount { get; set; }

        public DecimalRange? MaybePrice { get; set; }

        public decimal? MaybeAmount { get; set; }

        public ClampChild? Child { get; set; }
    }

    public sealed class ClampChild
    {
        public int Id { get; set; }

        public int ParentId { get; set; }

        public Int32Range Seats { get; set; } = Int32Range.Empty;

        public int Value { get; set; }

        public DecimalRange Price { get; set; } = DecimalRange.Empty;
    }

    private sealed class ClampContext(string connectionString) : DbContext
    {
        public DbSet<ClampRow> Rows => Set<ClampRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ClampRow>().Property(r => r.Id).ValueGeneratedNever();
            modelBuilder.Entity<ClampChild>().Property(c => c.Id).ValueGeneratedNever();
            modelBuilder.Entity<ClampRow>().HasOne(r => r.Child).WithOne().HasForeignKey<ClampChild>(c => c.ParentId);
        }
    }
}
