using System.Linq.Expressions;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>DateRange.Month</c>, <c>Quarter</c>, <c>Year</c> and <c>IsoWeek</c> over a column run as SQL and answer
/// what memory answers over the loaded rows (#191), for every condition on the 10.3.0 parity list (#408).
/// </summary>
/// <remarks>
/// <para>
/// <b>Shapes and extremes</b> are the rows. Every answer is a finite range, so the shapes are the periods'
/// edges: the days around each month, quarter, year and ISO week boundary, a leap day, the ISO weeks that
/// belong to the neighbouring year, and the first and last periods of <see cref="DateOnly"/>, with
/// <see cref="DateOnly.MinValue"/> and <see cref="DateOnly.MaxValue"/> stored as <c>-infinity</c> and
/// <c>infinity</c>. The integer forms run over years 1 and 9999 and each one's first and last month,
/// quarter and week, and over 52- and 53-week years. Equality with a stored range and <c>Contains</c> of the
/// extreme days hold the first and last periods to the value Npgsql binds for memory's answer.
/// </para>
/// <para>
/// <b>Nullable operands and composition</b> go through <see cref="CompositionRunner.Judge"/>: a nullable date
/// and year column read with <c>.Value</c> and the child of an outer join, each NULL on some rows so rule (c)
/// answers. Each range runs in <c>Where(== null)</c>, <c>Where(!= null)</c> and a final <c>Select</c>, and each
/// predicate built on it in <c>Where</c>, <c>Where(!)</c> and <c>Select</c>, with the filters and the
/// projection agreeing with each other (the declared nullability). The composition sweeps discover the
/// members of <see cref="RangeExtensions"/> and <see cref="RangeSet{TRange,T}"/>, not a range type's static
/// factories, so they do not reach these.
/// </para>
/// <para>
/// <b>An argument memory refuses</b> raises in PostgreSQL too (a week-year without that week, a month,
/// quarter or year out of range, the ISO week past 9999-12-31): <c>to_date</c> would roll week 53 of a
/// 52-week year over, and <c>make_date</c> accepts the year 10000.
/// </para>
/// </remarks>
[TestClass]
public sealed class CalendarFactoryParityTests
{
    private static readonly DateOnly[] Days =
    [
        DateOnly.MinValue, new(1, 1, 2), new(1, 1, 7), new(1, 1, 8), new(1, 1, 31), new(1, 2, 1), new(1, 3, 31), new(1, 4, 1),
        new(1, 12, 31), new(2, 1, 1), new(2024, 2, 29), new(2024, 12, 29), new(2024, 12, 30), new(2025, 1, 1), new(2025, 12, 28),
        new(2025, 12, 29), new(2026, 1, 1), new(2026, 3, 31), new(2026, 4, 1), new(2026, 6, 30), new(2026, 7, 1), new(2026, 9, 30),
        new(2026, 10, 1), new(2026, 12, 31), new(2027, 1, 3), new(2027, 1, 4), new(9999, 1, 1), new(9999, 9, 30), new(9999, 10, 1),
        new(9999, 11, 30), new(9999, 12, 1), new(9999, 12, 26), new(9999, 12, 27), new(9999, 12, 30), DateOnly.MaxValue
    ];

    /// <summary>The ISO week of these days ends after 9999-12-31, where memory throws.</summary>
    private static readonly DateOnly LastMonday = new(9999, 12, 27);

    private static readonly (int Year, int Month, int Quarter)[] Parts =
        [(1, 1, 1), (9999, 12, 4), (2024, 2, 1), (2026, 7, 3), (2000, 12, 4), (1, 12, 2), (9999, 1, 1), (1900, 3, 1)];

    private static readonly (int WeekYear, int Week)[] Weeks =
        [(1, 1), (2020, 53), (2026, 53), (2025, 52), (9999, 51), (2026, 1), (2027, 1), (9999, 1), (1, 52)];

    [TestMethod]
    public async Task CalendarFactories_InEveryQueryForm_AnswerWhatMemoryAnswers()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await Seed();

        await using var context = new CalendarContext(connectionString);
        var loaded = await context.Rows.Include(r => r.Child).AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        Assert.IsTrue(loaded.Any(r => r.MaybeDay is null) && loaded.Any(r => r.MaybeYear is null) && loaded.Any(r => r.Child is null),
                      "Some rows must hold a NULL column and some no child, or rule (c) is never reached.");
        Assert.IsTrue(loaded.Any(r => r.Day == DateOnly.MinValue) && loaded.Any(r => r.Day == DateOnly.MaxValue),
                      "The extremes must load as the days they stand for, or the infinities are never reached.");

        var failures = new List<string>();
        foreach (var (name, lambda) in Calls())
        {
            Outcome outcome;
            try
            {
                outcome = await CompositionRunner.Judge(
                    "calendar", new Call(name, lambda.Body), lambda.Parameters[0], context.Rows.AsNoTracking(), loaded,
                    r => r.Id, id => $"{id} ({Describe(loaded.Single(r => r.Id == id))})", failures);
            }
            catch (ArgumentOutOfRangeException e)
            {
                // A range PostgreSQL built past 9999-12-31 ([9999-12-01,10000-01-01) where memory's binds as infinity]) does not load.
                failures.Add($"{name} [calendar] Select: the server's range does not load: {CompositionRunner.FirstLine(e)}");
                continue;
            }

            if (outcome != Outcome.Compared) failures.Add($"{name}: {outcome}, where it must run as SQL and be compared");
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public async Task CalendarFactories_InAnOrdering_SortAsMemoryDoes()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await Seed();

        await using var context = new CalendarContext(connectionString);
        var loaded = await context.Rows.AsNoTracking().ToListAsync();

        async Task Agree<TKey>(string label, Expression<Func<CalendarRow, TKey>> key, bool descending)
        {
            var compiled = key.Compile();
            var server = descending
                ? await context.Rows.OrderByDescending(key).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync()
                : await context.Rows.OrderBy(key).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync();
            var memory = descending
                ? loaded.OrderByDescending(compiled).ThenBy(r => r.Id).Select(r => r.Id).ToList()
                : loaded.OrderBy(compiled).ThenBy(r => r.Id).Select(r => r.Id).ToList();

            CollectionAssert.AreEqual(memory, server, $"{label}: server {string.Join(",", server)}, memory {string.Join(",", memory)}");
        }

        await Agree("OrderBy(Month(Day))", r => DateRange.Month(r.Day), descending: false);
        await Agree("OrderByDescending(Year(Day))", r => DateRange.Year(r.Day), descending: true);
        await Agree("OrderBy(Quarter(Day).UpperBound())", r => DateRange.Quarter(r.Day).UpperBound(), descending: false);
        await Agree("OrderByDescending(IsoWeek(WeekDay).LowerBound())", r => DateRange.IsoWeek(r.WeekDay).LowerBound(), descending: true);
        await Agree("OrderBy(Month(Year, Month))", r => DateRange.Month(r.Year, r.Month), descending: false);
        await Agree("OrderBy(IsoWeek(WeekYear, Week))", r => DateRange.IsoWeek(r.WeekYear, r.Week), descending: false);
    }

    [TestMethod]
    public async Task CalendarFactories_OverAnArgumentMemoryRefuses_RaiseInPostgres()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await Seed();

        await using var context = new CalendarContext(connectionString);

        var loaded = await context.Rows.AsNoTracking().ToListAsync();
        int Row(Func<CalendarRow, bool> holds) => loaded.First(holds).Id;

        // The integer cases are written over the row holding (9999, 12, 4), so every argument is read from a column.
        var last    = Row(r => r is { Year: 9999, Month: 12, Quarter: 4 });
        var monday  = Row(r => r.Day == LastMonday);
        var maximum = Row(r => r.Day == DateOnly.MaxValue);

        (string Name, int Id, Expression<Func<CalendarRow, DateRange>> Call, Func<DateRange> Memory)[] cases =
        [
            ("IsoWeek(9999-12-27)", monday, r => DateRange.IsoWeek(r.Day), () => DateRange.IsoWeek(LastMonday)),
            ("IsoWeek(DateOnly.MaxValue)", maximum, r => DateRange.IsoWeek(r.Day), () => DateRange.IsoWeek(DateOnly.MaxValue)),
            ("IsoWeek(2025, 53)", last, r => DateRange.IsoWeek(r.Year - 7974, r.Month + 41), () => DateRange.IsoWeek(2025, 53)),
            ("IsoWeek(2026, 0)", last, r => DateRange.IsoWeek(r.Year - 7973, r.Month - 12), () => DateRange.IsoWeek(2026, 0)),
            ("IsoWeek(9999, 52)", last, r => DateRange.IsoWeek(r.Year, r.Month + 40), () => DateRange.IsoWeek(9999, 52)),
            ("IsoWeek(10000, 1)", last, r => DateRange.IsoWeek(r.Year + 1, 1), () => DateRange.IsoWeek(10000, 1)),
            ("Month(9999, 13)", last, r => DateRange.Month(r.Year, r.Month + 1), () => DateRange.Month(9999, 13)),
            ("Month(10000, 12)", last, r => DateRange.Month(r.Year + 1, r.Month), () => DateRange.Month(10000, 12)),
            ("Month(0, 12)", last, r => DateRange.Month(r.Year - 9999, r.Month), () => DateRange.Month(0, 12)),
            ("Quarter(9999, 5)", last, r => DateRange.Quarter(r.Year, r.Quarter + 1), () => DateRange.Quarter(9999, 5)),
            ("Quarter(9999, 0)", last, r => DateRange.Quarter(r.Year, r.Quarter - 4), () => DateRange.Quarter(9999, 0)),
            ("Year(-1)", last, r => DateRange.Year(r.Year - 10000), () => DateRange.Year(-1)),
            ("Year(10000)", last, r => DateRange.Year(r.Year + 1), () => DateRange.Year(10000))
        ];

        var failures = new List<string>();
        foreach (var (name, id, call, memory) in cases)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => memory(), $"{name}: memory");

            foreach (var (form, query) in new (string, Func<Task>)[]
                     {
                         ("Select", () => context.Rows.Where(r => r.Id == id).Select(call).ToListAsync()),
                         ("Where", () => context.Rows.Where(r => r.Id == id).Where(Contains(call)).Select(r => r.Id).ToListAsync())
                     })
            {
                try
                {
                    await query();
                    failures.Add($"{name} {form}: the server answered, where memory throws");
                }
                catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.InvalidTextRepresentation or PostgresErrorCodes.InvalidDatetimeFormat
                                                                   or PostgresErrorCodes.DatetimeFieldOverflow)
                {
                }
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    private static Expression<Func<CalendarRow, bool>> Contains(Expression<Func<CalendarRow, DateRange>> call)
        => Expression.Lambda<Func<CalendarRow, bool>>(
            Expression.Call(typeof(RangeExtensions), nameof(RangeExtensions.Contains), [typeof(DateOnly)],
                            call.Body, Expression.Property(call.Parameters[0], nameof(CalendarRow.WeekDay))),
            call.Parameters);

    private static IEnumerable<(string Name, LambdaExpression Lambda)> Calls()
    {
        (string, LambdaExpression) Range(string name, Expression<Func<CalendarRow, DateRange>> lambda) => (name, lambda);
        (string, LambdaExpression) Predicate(string name, Expression<Func<CalendarRow, bool>> lambda) => (name, lambda);

        yield return Range("Month(r.Day)", r => DateRange.Month(r.Day));
        yield return Range("Quarter(r.Day)", r => DateRange.Quarter(r.Day));
        yield return Range("Year(r.Day)", r => DateRange.Year(r.Day));
        yield return Range("IsoWeek(r.WeekDay)", r => DateRange.IsoWeek(r.WeekDay));
        yield return Range("Month(r.Year, r.Month)", r => DateRange.Month(r.Year, r.Month));
        yield return Range("Quarter(r.Year, r.Quarter)", r => DateRange.Quarter(r.Year, r.Quarter));
        yield return Range("Year(r.Year)", r => DateRange.Year(r.Year));
        yield return Range("IsoWeek(r.WeekYear, r.Week)", r => DateRange.IsoWeek(r.WeekYear, r.Week));
        yield return Range("Month(2026, r.Month)", r => DateRange.Month(2026, r.Month));
        yield return Range("Quarter(r.Year, 4)", r => DateRange.Quarter(r.Year, 4));
        yield return Range("IsoWeek(2026, r.Week)", r => DateRange.IsoWeek(2026, r.Week));

        yield return Range("Month(r.MaybeDay.Value)", r => DateRange.Month(r.MaybeDay!.Value));
        yield return Range("IsoWeek(r.MaybeDay.Value)", r => DateRange.IsoWeek(r.MaybeDay!.Value));
        yield return Range("Year(r.MaybeYear.Value)", r => DateRange.Year(r.MaybeYear!.Value));
        yield return Range("Month(r.MaybeYear.Value, r.Month)", r => DateRange.Month(r.MaybeYear!.Value, r.Month));
        yield return Range("Quarter(r.Child.Day)", r => DateRange.Quarter(r.Child!.Day));
        yield return Range("IsoWeek(r.Child.WeekDay)", r => DateRange.IsoWeek(r.Child!.WeekDay));
        yield return Range("Year(r.Child.Year)", r => DateRange.Year(r.Child!.Year));
        yield return Range("Quarter(r.Child.Year, r.Quarter)", r => DateRange.Quarter(r.Child!.Year, r.Quarter));

        yield return Predicate("Month(r.Day).Contains(r.WeekDay)", r => DateRange.Month(r.Day).Contains(r.WeekDay));
        yield return Predicate("Month(r.Day) == r.Period", r => DateRange.Month(r.Day) == r.Period);
        yield return Predicate("Year(r.Day) == r.Period", r => DateRange.Year(r.Day) == r.Period);
        yield return Predicate("Month(r.Year, r.Month) == r.Period", r => DateRange.Month(r.Year, r.Month) == r.Period);
        yield return Predicate("Month(r.Day).Contains(DateOnly.MaxValue)", r => DateRange.Month(r.Day).Contains(DateOnly.MaxValue));
        yield return Predicate("Year(r.Day).Contains(DateOnly.MinValue)", r => DateRange.Year(r.Day).Contains(DateOnly.MinValue));
        yield return Predicate("IsoWeek(r.WeekDay).Contains(DateOnly.MinValue)", r => DateRange.IsoWeek(r.WeekDay).Contains(DateOnly.MinValue));
        yield return Predicate("IsoWeek(r.WeekDay).Overlaps(r.Period)", r => DateRange.IsoWeek(r.WeekDay).Overlaps(r.Period));
        yield return Predicate("Quarter(r.Day).UpperBound() == r.Day", r => DateRange.Quarter(r.Day).UpperBound() == r.Day);
        yield return Predicate("Month(r.Day).LowerBound() == r.Day", r => DateRange.Month(r.Day).LowerBound() == r.Day);
        yield return Predicate("Year(r.Year).IsContainedBy(Year(r.Day))", r => DateRange.Year(r.Year).IsContainedBy(DateRange.Year(r.Day)));
        yield return Predicate("Quarter(r.Year, r.Quarter).Contains(r.Day) && r.Year > 2000",
                               r => DateRange.Quarter(r.Year, r.Quarter).Contains(r.Day) && r.Year > 2000);
        yield return Predicate("Month(r.MaybeDay.Value).Contains(r.Day) || r.Year == 1",
                               r => DateRange.Month(r.MaybeDay!.Value).Contains(r.Day) || r.Year == 1);
        yield return Predicate("Year(r.Child.Year).Overlaps(r.Period)", r => DateRange.Year(r.Child!.Year).Overlaps(r.Period));
        yield return Predicate("IsoWeek(r.WeekYear, r.Week).Contains(r.WeekDay)", r => DateRange.IsoWeek(r.WeekYear, r.Week).Contains(r.WeekDay));
        yield return Predicate("IsoWeek(r.WeekYear, r.Week).UpperBound() > r.WeekDay", r => DateRange.IsoWeek(r.WeekYear, r.Week).UpperBound() > r.WeekDay);
    }

    private static string Describe(CalendarRow row) =>
        $"Day {row.Day:yyyy-MM-dd}, WeekDay {row.WeekDay:yyyy-MM-dd}, ({row.Year}, {row.Month}, {row.Quarter}), week {row.WeekYear}-W{row.Week}, "
      + $"Period {row.Period}, MaybeDay {row.MaybeDay?.ToString("yyyy-MM-dd") ?? "NULL"}, MaybeYear {row.MaybeYear?.ToString() ?? "NULL"}, "
      + (row.Child is { } child ? $"child {child.Day:yyyy-MM-dd} / {child.WeekDay:yyyy-MM-dd} / {child.Year}" : "no child");

    /// <summary>
    /// A database of its own: one row per day, the integer columns cycling through their own lists, a NULL
    /// column every third row, a child every other row. A week column never holds a day whose ISO week memory
    /// refuses; the refusal is the third test's.
    /// </summary>
    private static async Task<string> Seed()
    {
        var connectionString = await CompositionDatabase.Create("calendar");
        await using var seed = new CalendarContext(connectionString);
        await seed.Database.EnsureCreatedAsync();

        static DateOnly WeekDay(DateOnly day) => day >= LastMonday ? LastMonday.AddDays(-1) : day;

        for (var i = 0; i < Days.Length; i++)
        {
            var day                     = Days[i];
            var (year, month, quarter)  = Parts[i % Parts.Length];
            var (weekYear, week)        = Weeks[i % Weeks.Length];
            seed.Rows.Add(new CalendarRow
            {
                Id        = i + 1,
                Day       = day,
                WeekDay   = WeekDay(day),
                Year      = year,
                Month     = month,
                Quarter   = quarter,
                WeekYear  = weekYear,
                Week      = week,
                Period    = i % 3 == 0 ? DateRange.Month(day) : i % 3 == 1 ? DateRange.Year(day) : DateRange.Month(year, month),
                MaybeDay  = i % 3 == 0 ? null : WeekDay(Days[(i + 1) % Days.Length]),
                MaybeYear = i % 3 == 1 ? null : Parts[(i + 2) % Parts.Length].Year,
                Child     = i % 2 == 0
                    ? new CalendarChild { Id = 100 + i, Day = Days[(i + 3) % Days.Length], WeekDay = WeekDay(Days[(i + 4) % Days.Length]), Year = Parts[(i + 3) % Parts.Length].Year }
                    : null
            });
        }

        await seed.SaveChangesAsync();
        return connectionString;
    }

    public sealed class CalendarRow
    {
        public int Id { get; set; }

        public DateOnly Day { get; set; }

        public DateOnly WeekDay { get; set; }

        public int Year { get; set; }

        public int Month { get; set; }

        public int Quarter { get; set; }

        public int WeekYear { get; set; }

        public int Week { get; set; }

        public DateRange Period { get; set; } = DateRange.Empty;

        public DateOnly? MaybeDay { get; set; }

        public int? MaybeYear { get; set; }

        public CalendarChild? Child { get; set; }
    }

    public sealed class CalendarChild
    {
        public int Id { get; set; }

        public int ParentId { get; set; }

        public DateOnly Day { get; set; }

        public DateOnly WeekDay { get; set; }

        public int Year { get; set; }
    }

    private sealed class CalendarContext(string connectionString) : DbContext
    {
        public DbSet<CalendarRow> Rows => Set<CalendarRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CalendarRow>().Property(r => r.Id).ValueGeneratedNever();
            modelBuilder.Entity<CalendarChild>().Property(c => c.Id).ValueGeneratedNever();
            modelBuilder.Entity<CalendarRow>().HasOne(r => r.Child).WithOne().HasForeignKey<CalendarChild>(c => c.ParentId);
        }
    }
}
