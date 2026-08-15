using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Persists every value set type to a live PostgreSQL instance and reads it back, and
/// executes the translated set algebra on the server — the authority layer for the array
/// mappings: canonical round-trips, empty-vs-NULL, non-canonical and corrupt rows, change
/// detection, GIN servicing, and the validated-wrapper hinge case.
/// Each test uses its own Ids (8xxx block) so tests can run in parallel.
/// </summary>
[TestClass]
public class SetIntegrationTests
{
    private static async Task Seed(params Reservation[] rows)
    {
        await using var context = new IntegrationDbContext();
        context.Reservations.AddRange(rows);
        await context.SaveChangesAsync();
    }

    private static async Task<Reservation> Load(int id)
    {
        await using var context = new IntegrationDbContext();
        return await context.Reservations.SingleAsync(r => r.Id == id);
    }

    private static async Task<string?> ColumnText(string column, int id)
    {
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT \"{column}\"::text FROM \"Reservations\" WHERE \"Id\" = {id}", connection);
        return await command.ExecuteScalarAsync() is string text ? text : null;   // DBNull → null
    }

    // -----------------------------------------------------------------------
    // Round trips
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task AllBclSetTypes_RoundTripCanonical()
    {
        ContainerLifecycle.RequireDatabase();

        var original = new Reservation
        {
            Id            = 8001,
            Tags          = StringSet.From("beta", "alpha", "beta"),
            Uuids         = GuidSet.From(Guid.NewGuid(), Guid.NewGuid()),
            SmallCodes    = Int16Set.From((short)7, (short)3),
            Codes         = Int32Set.From(10, 2),
            BigCodes      = Int64Set.From(long.MaxValue, 1L),
            Rates         = DecimalSet.From(2.25m, 1.5m),
            BlackoutDates = DateSet.From(new DateOnly(2024, 12, 24), new DateOnly(2024, 1, 1)),
            Slots         = TimeSet.From(new TimeOnly(17, 30), new TimeOnly(9, 0))
        };

        await Seed(original);
        var loaded = await Load(8001);

        Assert.AreEqual(original.Tags, loaded.Tags);
        Assert.AreEqual(original.Uuids, loaded.Uuids);
        Assert.AreEqual(original.SmallCodes, loaded.SmallCodes);
        Assert.AreEqual(original.Codes, loaded.Codes);
        Assert.AreEqual(original.BigCodes, loaded.BigCodes);
        Assert.AreEqual(original.Rates, loaded.Rates);
        Assert.AreEqual(original.BlackoutDates, loaded.BlackoutDates);
        Assert.AreEqual(original.Slots, loaded.Slots);

        // The stored array is the canonical form, not the construction order.
        Assert.AreEqual("{alpha,beta}", await ColumnText("Tags", 8001));
        Assert.AreEqual("{2,10}", await ColumnText("Codes", 8001));
    }

    [TestMethod]
    public async Task NodaTimeSetTypes_RoundTripCanonical()
    {
        ContainerLifecycle.RequireDatabase();

        var original = new Reservation
        {
            Id              = 8002,
            NodaHolidays    = LocalDateSet.From(new LocalDate(2024, 12, 24), new LocalDate(2024, 1, 1)),
            NodaMarks       = LocalDateTimeSet.From(new LocalDateTime(2024, 6, 1, 12, 30, 0), new LocalDateTime(2024, 6, 1, 8, 0, 0)),
            NodaOccurrences = InstantSet.From(Instant.FromUtc(2024, 6, 1, 12, 30), Instant.FromUtc(2024, 6, 1, 8, 0)),
            NodaSlots       = LocalTimeSet.From(new LocalTime(17, 30), new LocalTime(9, 0)),
            BillingMonths   = YearMonthSet.From(new YearMonth(2024, 6), new YearMonth(2024, 1))
        };

        await Seed(original);
        var loaded = await Load(8002);

        Assert.AreEqual(original.NodaHolidays, loaded.NodaHolidays);
        Assert.AreEqual(original.NodaMarks, loaded.NodaMarks);
        Assert.AreEqual(original.NodaOccurrences, loaded.NodaOccurrences);
        Assert.AreEqual(original.NodaSlots, loaded.NodaSlots);
        Assert.AreEqual(original.BillingMonths, loaded.BillingMonths);

        // Months persist as first-of-month dates — the month-aligned date[] contract.
        Assert.AreEqual("{2024-01-01,2024-06-01}", await ColumnText("BillingMonths", 8002));
    }

    [TestMethod]
    public async Task LargeSet_RoundTrips()
    {
        ContainerLifecycle.RequireDatabase();

        var original = Int32Set.From(Enumerable.Range(0, 1000).Select(i => (i * 37) % 5000));

        await Seed(new Reservation { Id = 8003, Codes = original });
        var loaded = await Load(8003);

        Assert.AreEqual(original, loaded.Codes);
    }

    [TestMethod]
    public async Task EmptySet_And_NullColumn_AreDistinct()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(
            new Reservation { Id = 8004, OptionalTags = StringSet.Empty },
            new Reservation { Id = 8005, OptionalTags = null });

        var withEmpty = await Load(8004);
        var withNull  = await Load(8005);

        Assert.AreSame(StringSet.Empty, withEmpty.OptionalTags);
        Assert.IsNull(withNull.OptionalTags);
        Assert.AreEqual("{}", await ColumnText("OptionalTags", 8004));
        Assert.IsNull(await ColumnText("OptionalTags", 8005));
    }

    [TestMethod]
    public async Task TimestampSets_ApplyTheRangeFamilyNormalizationRules()
    {
        ContainerLifecycle.RequireDatabase();

        // timestamp[]: UTC-kinded input is written as its wall-clock face; Kind reads back
        // Unspecified. timestamptz[]: a +02:00 element comes back at +00:00, same instant.
        var utcKinded  = new DateTime(2024, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var withOffset = new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.FromHours(2));

        await Seed(new Reservation
        {
            Id     = 8006,
            Marks  = DateTimeSet.From(utcKinded),
            Stamps = DateTimeOffsetSet.From(withOffset)
        });

        var loaded = await Load(8006);

        Assert.AreEqual(utcKinded.Ticks, loaded.Marks.Values[0].Ticks);
        Assert.AreEqual(DateTimeKind.Unspecified, loaded.Marks.Values[0].Kind);
        Assert.AreEqual(withOffset, loaded.Stamps.Values[0]);          // instant-based equality
        Assert.AreEqual(TimeSpan.Zero, loaded.Stamps.Values[0].Offset);
    }

    // -----------------------------------------------------------------------
    // Foreign rows: non-canonical and corrupt data
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task NonCanonicalRow_NormalizesOnRead_AndOperatorsStayCorrect()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(new Reservation { Id = 8010 });
        await using (var context = new IntegrationDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE \"Reservations\" SET \"Tags\" = ARRAY['b','a','b']::text[] WHERE \"Id\" = 8010");
        }

        var loaded = await Load(8010);
        Assert.AreEqual(StringSet.From("a", "b"), loaded.Tags);   // normalized on materialization

        var canonical = StringSet.From("a", "b");
        await using var query = new IntegrationDbContext();

        // The translated operators are order- and multiplicity-insensitive: they match the
        // non-canonical row. SQL `=` is sequence-sensitive: it does not — the documented
        // canonical-writers precondition on set equality (design decision D13).
        var byOperators = await query.Reservations
            .Where(r => r.Id == 8010 && r.Tags.Contains("a") && r.Tags.IsSupersetOf(canonical) && r.Tags.Overlaps(canonical))
            .CountAsync();
        Assert.AreEqual(1, byOperators);

        var byEquality = await query.Reservations
            .Where(r => r.Id == 8010 && r.Tags == canonical)
            .CountAsync();
        Assert.AreEqual(0, byEquality);

        // The row stays as written until actually modified — reads alone never rewrite it.
        Assert.AreEqual("{b,a,b}", await ColumnText("Tags", 8010));
    }

    [TestMethod]
    public async Task NullElementRow_ThrowsOnRead()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(new Reservation { Id = 8011 });
        await using (var context = new IntegrationDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE \"Reservations\" SET \"Tags\" = ARRAY['a',NULL]::text[] WHERE \"Id\" = 8011");
        }

        try
        {
            await Load(8011);
            Assert.Fail("Reading a NULL array element must throw — it is corrupt data by the type's contract.");
        }
        catch (Exception exception)
        {
            Assert.IsTrue(HasInChain<ArgumentException>(exception),
                $"Expected an ArgumentException in the chain, got: {exception}");
        }
    }

    [TestMethod]
    public async Task NonMonthAlignedDateRow_ThrowsOnRead()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(new Reservation { Id = 8012 });
        await using (var context = new IntegrationDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE \"Reservations\" SET \"BillingMonths\" = ARRAY['2024-06-15']::date[] WHERE \"Id\" = 8012");
        }

        try
        {
            await Load(8012);
            Assert.Fail("Reading a non-month-aligned date must throw for a YearMonthSet column.");
        }
        catch (Exception exception)
        {
            Assert.IsTrue(HasInChain<InvalidOperationException>(exception),
                $"Expected an InvalidOperationException in the chain, got: {exception}");
        }
    }

    private static bool HasInChain<TException>(Exception? exception) where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException) return true;
        }

        return false;
    }

    // -----------------------------------------------------------------------
    // Change detection
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task ChangeDetection_IdenticalSetWritesNothing_ModifiedSetUpdates()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(new Reservation { Id = 8020, Tags = StringSet.From("a", "b") });

        await using var context = new IntegrationDbContext();
        var row = await context.Reservations.SingleAsync(r => r.Id == 8020);

        // A different instance with the same canonical content — no diff, no statement.
        row.Tags = StringSet.From("b", "a");
        Assert.AreEqual(0, await context.SaveChangesAsync());

        row.Tags = row.Tags.Add("c");
        Assert.AreEqual(1, await context.SaveChangesAsync());

        Assert.AreEqual(StringSet.From("a", "b", "c"), (await Load(8020)).Tags);
    }

    // -----------------------------------------------------------------------
    // GIN servicing
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Contains_IsServedByAGinIndex()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(new Reservation { Id = 8030, Tags = StringSet.From("x") });

        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        await using (var create = new NpgsqlCommand(
                         "CREATE INDEX IF NOT EXISTS ix_reservations_tags_gin ON \"Reservations\" USING GIN (\"Tags\")",
                         connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        // The table is tiny, so force the planner off sequential scans to reveal whether the
        // @> predicate is index-servable at all.
        await using (var seqscanOff = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await seqscanOff.ExecuteNonQueryAsync();
        }

        var plan = new System.Text.StringBuilder();
        await using (var explain = new NpgsqlCommand(
                         "EXPLAIN SELECT \"Id\" FROM \"Reservations\" WHERE \"Tags\" @> ARRAY['x']::text[]",
                         connection))
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) plan.AppendLine(reader.GetString(0));
        }

        StringAssert.Contains(plan.ToString(), "ix_reservations_tags_gin");
    }

    // -----------------------------------------------------------------------
    // The validated-wrapper hinge: StringSet<TestKey> end to end
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task WrapperSet_RoundTripsAndTranslates()
    {
        ContainerLifecycle.RequireDatabase();

        var permissions = StringSet<TestKey>.From(
            TestKey.Parse("users.write", CultureInfo.InvariantCulture),
            TestKey.Parse("users.read", CultureInfo.InvariantCulture));

        await Seed(new Reservation { Id = 8040, Permissions = permissions });

        var loaded = await Load(8040);
        Assert.AreEqual(permissions, loaded.Permissions);
        Assert.AreEqual("{users.read,users.write}", await ColumnText("Permissions", 8040));

        // A bare wrapper parameter binds as text and the predicate runs on the server.
        var key = TestKey.Parse("users.read", CultureInfo.InvariantCulture);
        var required = StringSet<TestKey>.From(key);

        await using var context = new IntegrationDbContext();
        var matched = await context.Reservations
            .Where(r => r.Id == 8040 && r.Permissions.Contains(key) && r.Permissions.IsSupersetOf(required))
            .CountAsync();

        Assert.AreEqual(1, matched);
    }

    // -----------------------------------------------------------------------
    // Server-vs-in-memory parity
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task SetAlgebra_ServerAgreesWithInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var tags     = StringSet.From("a", "b", "c");
        var wanted   = StringSet.From("b", "z");
        var subset   = StringSet.From("a", "b");
        var superset = StringSet.From("a", "b", "c", "d");

        await Seed(new Reservation { Id = 8050, Tags = tags });

        await using var context = new IntegrationDbContext();
        var server = await context.Reservations
            .Where(r => r.Id == 8050)
            .Select(r => new
            {
                Contains   = r.Tags.Contains("b"),
                Overlaps   = r.Tags.Overlaps(wanted),
                IsSubset   = r.Tags.IsSubsetOf(wanted),
                IsSuperset = r.Tags.IsSupersetOf(wanted),
                Count      = r.Tags.Count,
                IsEmpty    = r.Tags.IsEmpty,
                Union      = r.Tags.Union(wanted),

                // Proper containment against a strict subset and against an equal set — the
                // second is where it has to differ from IsSubsetOf/IsSupersetOf.
                ProperSuperset      = r.Tags.IsProperSupersetOf(subset),
                ProperSupersetEqual = r.Tags.IsProperSupersetOf(tags),
                ProperSubset        = r.Tags.IsProperSubsetOf(superset),
                ProperSubsetEqual   = r.Tags.IsProperSubsetOf(tags),

                // array_remove keeps the array canonical, so the removal round-trips and its
                // cardinality is correct — neither holds for a server-side Union.
                Removed      = r.Tags.Remove("b"),
                RemovedCount = r.Tags.Remove("b").Count,
                RemovedAbsent = r.Tags.Remove("zzz")
            })
            .SingleAsync();

        Assert.AreEqual(tags.Contains("b"), server.Contains);
        Assert.AreEqual(tags.Overlaps(wanted), server.Overlaps);
        Assert.AreEqual(tags.IsSubsetOf(wanted), server.IsSubset);
        Assert.AreEqual(tags.IsSupersetOf(wanted), server.IsSuperset);
        Assert.AreEqual(tags.Count, server.Count);
        Assert.AreEqual(tags.IsEmpty, server.IsEmpty);

        Assert.AreEqual(tags.IsProperSupersetOf(subset), server.ProperSuperset);
        Assert.AreEqual(tags.IsProperSupersetOf(tags), server.ProperSupersetEqual);
        Assert.AreEqual(tags.IsProperSubsetOf(superset), server.ProperSubset);
        Assert.AreEqual(tags.IsProperSubsetOf(tags), server.ProperSubsetEqual);
        Assert.IsFalse(server.ProperSupersetEqual, "a set is not a proper superset of itself");
        Assert.IsFalse(server.ProperSubsetEqual, "a set is not a proper subset of itself");

        Assert.AreEqual(tags.Remove("b"), server.Removed);
        Assert.AreEqual(tags.Remove("b").Count, server.RemovedCount);
        Assert.AreEqual(tags.Remove("zzz"), server.RemovedAbsent);

        // array_cat's server value is unsorted/undeduplicated; materialization
        // re-canonicalizes, so it equals the in-memory union.
        Assert.AreEqual(tags.Union(wanted), server.Union);
    }

    [TestMethod]
    public async Task NonIsoProbe_MatchesTheSameRowOnTheServerAsInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        // A LocalDateSet stores ISO elements. A probe in another calendar names the same
        // physical day, so both the server and in-memory Contains must find it — the element
        // mapping normalizes it rather than binding the Coptic fields as if they were ISO.
        var iso     = new LocalDate(2024, 6, 15);
        var coptic  = iso.WithCalendar(CalendarSystem.Coptic);
        var holidays = LocalDateSet.From(iso);

        await Seed(new Reservation { Id = 8051, NodaHolidays = holidays });

        await using var context = new IntegrationDbContext();
        var matched = await context.Reservations
            .Where(r => r.Id == 8051 && r.NodaHolidays.Contains(coptic))
            .AnyAsync();

        Assert.IsTrue(holidays.Contains(coptic), "in-memory Contains missed the equivalent date");
        Assert.IsTrue(matched, "server-side Contains missed the equivalent date");
    }

    [TestMethod]
    public async Task CountOverARemovedUnion_IsRefusedRatherThanCountedTwice()
    {
        ContainerLifecycle.RequireDatabase();

        // array_cat concatenates and array_remove only preserves whatever canonical form its
        // input had, so cardinality over the pair counts shared elements twice. This asserts
        // the refusal against a live server together with the number it is refusing to return:
        // {a,c} unioned with {a,b} is {a,c,a,b} on the server — 4 — where the in-memory
        // expression is {a,b,c}, 3. Before the fix, only a bare Union was refused.
        var tags  = StringSet.From("a", "c");
        var other = StringSet.From("a", "b");

        await Seed(new Reservation { Id = 8052, Tags = tags, OptionalTags = other });

        await using var context = new IntegrationDbContext();

        // What the server computes for that expression, straight from PostgreSQL. This is the
        // number the translation used to return.
        var serverCount = await context.Database
            .SqlQuery<int>($"""
                SELECT cardinality(array_remove(array_cat("Tags", "OptionalTags"), 'x')) AS "Value"
                FROM "Reservations" WHERE "Id" = 8052
                """)
            .SingleAsync();

        Assert.AreEqual(4, serverCount, "the server-side count the refusal exists to prevent");
        Assert.AreEqual(3, tags.Union(other).Remove("x").Count, "the in-memory answer");

        // Refusing the translation hands the projection back to client evaluation, which
        // materializes the arrays and counts canonically — so the query now answers 3.
        var projected = await context.Reservations
            .Where(r => r.Id == 8052)
            .Select(r => r.Tags.Union(r.OptionalTags!).Remove("x").Count)
            .SingleAsync();

        Assert.AreEqual(3, projected,
            "the query must agree with the in-memory expression, not with cardinality over the concatenation");

        // In a predicate there is no client fallback, so the same expression is refused outright
        // rather than filtering on a number that is quietly too large.
        Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Reservations
                         .Where(r => r.Tags.Union(r.OptionalTags!).Remove("x").Count > 3)
                         .ToQueryString());
    }
}
