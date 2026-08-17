using CodoMetis.ValueRanges.Conventions.Tests;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// The twelve validated-wrapper arities that had never touched a database. Three of the fifteen
/// were covered — <c>StringSet&lt;TestKey&gt;</c>, <c>DateTimeSet&lt;AuditStamp&gt;</c> and
/// <c>YearMonthSet&lt;BillingMonth&gt;</c>, in <see cref="SetIntegrationTests"/> — and the rest had
/// only assertions about SQL text, which is a claim about PostgreSQL rather than a demonstration
/// of it.
/// </summary>
/// <remarks>
/// <para>
/// The element types are the ones the conventions suite closes each family over, linked rather
/// than redefined, so what round-trips here is what <c>ValueSetContractTests</c> checks the value
/// semantics of.
/// </para>
/// <para>
/// Each family is probed with a value that a bridge taking the element's default text form would
/// have coarsened — sub-second times, a decimal carrying its scale, an offset that has to
/// normalize — because a probe already equal to its own lossy rendering exercises nothing. The
/// wrapper arities are the only value set code whose write path differs from its closed sibling's,
/// and that difference is entirely in the element text bridge.
/// </para>
/// <para>Ids are in the 8100 block so these can run in parallel with the rest.</para>
/// </remarks>
[TestClass]
public class WrapperArityIntegrationTests
{
    private static async Task Seed(Reservation row)
    {
        await using var context = new IntegrationDbContext();
        context.Reservations.Add(row);
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
        return await command.ExecuteScalarAsync() is string text ? text : null;
    }

    // -----------------------------------------------------------------------
    // The probe values — deliberately awkward ones
    // -----------------------------------------------------------------------

    private static readonly TenantId     Tenant = TenantId.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff", null);
    private static readonly TinyCode     Tiny   = TinyCode.Parse("-7", null);
    private static readonly SmallCode    Small  = SmallCode.Parse("-2147483648", null);
    private static readonly LargeCode    Large  = LargeCode.Parse("9007199254740993", null);
    private static readonly Money        Rate   = Money.Parse("12.50", null);
    private static readonly BusinessDate Day    = BusinessDate.Parse("2024-06-15", null);
    private static readonly ShiftTime    Slot   = ShiftTime.Parse("09:30:15.25", null);
    private static readonly EventStamp   Moment = EventStamp.Parse("2024-06-15T10:30:00.1234560+02:00", null);

    private static readonly CalendarDay    NodaDay    = CalendarDay.Parse("2024-06-15", null);
    private static readonly WallClockStamp NodaMark   = WallClockStamp.Parse("2024-06-15T10:30:15.123456", null);
    private static readonly EventInstant   NodaMoment = EventInstant.Parse("2024-06-15T10:30:15.123456Z", null);
    private static readonly OpeningTime    NodaSlot   = OpeningTime.Parse("09:30:15.123456", null);

    private static Reservation Populated(int id) => new()
    {
        Id = id,
        WrappedTenants   = GuidSet<TenantId>.From([Tenant]),
        WrappedTinyCodes = Int16Set<TinyCode>.From([Tiny, TinyCode.Parse("32767", null)]),
        WrappedCodes     = Int32Set<SmallCode>.From([Small, SmallCode.Parse("42", null)]),
        WrappedBigCodes  = Int64Set<LargeCode>.From([Large]),
        WrappedRates     = DecimalSet<Money>.From([Rate, Money.Parse("0.1", null)]),
        WrappedDays      = DateSet<BusinessDate>.From([Day, BusinessDate.Parse("1970-01-01", null)]),
        WrappedSlots     = TimeSet<ShiftTime>.From([Slot]),
        WrappedStamps    = DateTimeOffsetSet<EventStamp>.From([Moment]),

        WrappedNodaDays    = LocalDateSet<CalendarDay>.From([NodaDay]),
        WrappedNodaMarks   = LocalDateTimeSet<WallClockStamp>.From([NodaMark]),
        WrappedNodaMoments = InstantSet<EventInstant>.From([NodaMoment]),
        WrappedNodaSlots   = LocalTimeSet<OpeningTime>.From([NodaSlot])
    };

    // -----------------------------------------------------------------------
    // Round trips
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task EveryCoreWrapperArity_RoundTripsCanonical()
    {
        ContainerLifecycle.RequireDatabase();

        var original = Populated(8100);

        await Seed(original);
        var loaded = await Load(8100);

        Assert.AreEqual(original.WrappedTenants,   loaded.WrappedTenants);
        Assert.AreEqual(original.WrappedTinyCodes, loaded.WrappedTinyCodes);
        Assert.AreEqual(original.WrappedCodes,     loaded.WrappedCodes);
        Assert.AreEqual(original.WrappedBigCodes,  loaded.WrappedBigCodes);
        Assert.AreEqual(original.WrappedRates,     loaded.WrappedRates);
        Assert.AreEqual(original.WrappedDays,      loaded.WrappedDays);
        Assert.AreEqual(original.WrappedSlots,     loaded.WrappedSlots);
        Assert.AreEqual(original.WrappedStamps,    loaded.WrappedStamps);
    }

    [TestMethod]
    public async Task EveryNodaTimeWrapperArity_RoundTripsCanonical()
    {
        ContainerLifecycle.RequireDatabase();

        var original = Populated(8101);

        await Seed(original);
        var loaded = await Load(8101);

        Assert.AreEqual(original.WrappedNodaDays,    loaded.WrappedNodaDays);
        Assert.AreEqual(original.WrappedNodaMarks,   loaded.WrappedNodaMarks);
        Assert.AreEqual(original.WrappedNodaMoments, loaded.WrappedNodaMoments);
        Assert.AreEqual(original.WrappedNodaSlots,   loaded.WrappedNodaSlots);
    }

    /// <summary>
    /// What is actually in the columns. CLR equality alone would not catch a bridge that coarsened
    /// consistently on both legs — it would round-trip perfectly and store the wrong thing, which
    /// is precisely how the format-pinning defect hides.
    /// </summary>
    [TestMethod]
    public async Task WrapperArities_StoreThePinnedTextForm()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(Populated(8102));

        // numeric keeps the scale the element formatted with; 0.1 is not representable in binary
        // floating point, so a bridge routed through double would show it here.
        Assert.AreEqual("{0.1,12.50}", await ColumnText("WrappedRates", 8102));

        // The sub-second component the element's own default text form would have dropped.
        Assert.AreEqual("{09:30:15.25}", await ColumnText("WrappedSlots", 8102));
        Assert.AreEqual("{09:30:15.123456}", await ColumnText("WrappedNodaSlots", 8102));
        Assert.AreEqual("{\"2024-06-15 10:30:15.123456\"}", await ColumnText("WrappedNodaMarks", 8102));

        // ISO dates, not the calendar-shaped or US-ordered defaults.
        Assert.AreEqual("{1970-01-01,2024-06-15}", await ColumnText("WrappedDays", 8102));
        Assert.AreEqual("{2024-06-15}", await ColumnText("WrappedNodaDays", 8102));

        // Canonical order is the element's own, and the integers span both signs.
        Assert.AreEqual("{-7,32767}", await ColumnText("WrappedTinyCodes", 8102));
        Assert.AreEqual("{-2147483648,42}", await ColumnText("WrappedCodes", 8102));
        Assert.AreEqual("{9007199254740993}", await ColumnText("WrappedBigCodes", 8102));
    }

    /// <summary>
    /// <c>timestamptz</c> stores an instant, so a <c>+02:00</c> element normalizes to UTC on the
    /// way out and reads back at offset zero — the same rule the closed
    /// <see cref="DateTimeOffsetSet"/> follows, which
    /// <c>SetIntegrationTests.TimestampSets_ApplyTheRangeFamilyNormalizationRules</c> pins.
    /// Equality is instant-based, so the round trip above cannot show this on its own.
    /// </summary>
    [TestMethod]
    public async Task WrapperTimestamptzArity_NormalizesToUtc()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(Populated(8103));
        var loaded = await Load(8103);

        Assert.AreEqual(
            EventStamp.Parse("2024-06-15T08:30:00.1234560+00:00", null), loaded.WrappedStamps[0],
            "the element must come back at offset zero, naming the same instant");
    }

    // -----------------------------------------------------------------------
    // The server-side algebra, per arity
    // -----------------------------------------------------------------------

    /// <summary>
    /// A bare wrapper element as a query parameter, for every arity: the element mapping converts
    /// it to the backing primitive and the predicate runs on the server. This is the leg the
    /// translation tests cannot reach — they assert the SQL, not that PostgreSQL agrees the
    /// bound value names the row that was written.
    /// </summary>
    [TestMethod]
    public async Task EveryWrapperArity_ContainsMatchesOnTheServer()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(Populated(8104));

        await using var context = new IntegrationDbContext();

        var matched = await context.Reservations
            .Where(r => r.Id == 8104
                     && r.WrappedTenants.Contains(Tenant)
                     && r.WrappedTinyCodes.Contains(Tiny)
                     && r.WrappedCodes.Contains(Small)
                     && r.WrappedBigCodes.Contains(Large)
                     && r.WrappedRates.Contains(Rate)
                     && r.WrappedDays.Contains(Day)
                     && r.WrappedSlots.Contains(Slot)
                     && r.WrappedStamps.Contains(Moment)
                     && r.WrappedNodaDays.Contains(NodaDay)
                     && r.WrappedNodaMarks.Contains(NodaMark)
                     && r.WrappedNodaMoments.Contains(NodaMoment)
                     && r.WrappedNodaSlots.Contains(NodaSlot))
            .CountAsync();

        Assert.AreEqual(
            1, matched,
            "a server-side Contains missed an element the row was written with — the element "
          + "mapping bound something the column does not hold.");
    }

    /// <summary>
    /// The negative half: a value the set does not hold must not match. A bridge that coarsened
    /// the bound parameter enough would make neighbouring values collide, and the positive test
    /// above cannot see that.
    /// </summary>
    [TestMethod]
    public async Task WrapperArities_ContainsRejectsANearMiss()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(Populated(8105));

        // Each differs from a stored element only below the precision the family pins.
        var nearRate   = Money.Parse("12.51", null);
        var nearSlot   = ShiftTime.Parse("09:30:15.26", null);
        var nearNoda   = OpeningTime.Parse("09:30:15.123457", null);
        var nearBig    = LargeCode.Parse("9007199254740992", null);

        await using var context = new IntegrationDbContext();

        var matched = await context.Reservations
            .Where(r => r.Id == 8105
                     && (r.WrappedRates.Contains(nearRate)
                      || r.WrappedSlots.Contains(nearSlot)
                      || r.WrappedNodaSlots.Contains(nearNoda)
                      || r.WrappedBigCodes.Contains(nearBig)))
            .CountAsync();

        Assert.AreEqual(
            0, matched,
            "a server-side Contains matched a value one unit of precision away from a stored "
          + "element — the bridge is coarsening the parameter, the column, or both.");
    }

    /// <summary>
    /// The rest of the translated algebra over an arity, checked against the in-memory answer
    /// rather than a hard-coded one, so the two definitions of the operators stay tied together.
    /// </summary>
    [TestMethod]
    public async Task WrapperArity_SetAlgebra_ServerAgreesWithInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var rates    = DecimalSet<Money>.From([Rate, Money.Parse("0.1", null)]);
        var wanted   = DecimalSet<Money>.From([Rate, Money.Parse("99.99", null)]);
        var superset = DecimalSet<Money>.From([Rate, Money.Parse("0.1", null), Money.Parse("7", null)]);

        await Seed(new Reservation { Id = 8106, WrappedRates = rates });

        await using var context = new IntegrationDbContext();
        var server = await context.Reservations
            .Where(r => r.Id == 8106)
            .Select(r => new
            {
                Contains     = r.WrappedRates.Contains(Rate),
                Overlaps     = r.WrappedRates.Overlaps(wanted),
                IsSubset     = r.WrappedRates.IsSubsetOf(superset),
                IsSuperset   = r.WrappedRates.IsSupersetOf(wanted),
                ProperSubset = r.WrappedRates.IsProperSubsetOf(superset),
                Count        = r.WrappedRates.Count,
                IsEmpty      = r.WrappedRates.IsEmpty,
                Removed      = r.WrappedRates.Remove(Rate),
                Union        = r.WrappedRates.Union(wanted)
            })
            .SingleAsync();

        Assert.AreEqual(rates.Contains(Rate),               server.Contains);
        Assert.AreEqual(rates.Overlaps(wanted),             server.Overlaps);
        Assert.AreEqual(rates.IsSubsetOf(superset),         server.IsSubset);
        Assert.AreEqual(rates.IsSupersetOf(wanted),         server.IsSuperset);
        Assert.AreEqual(rates.IsProperSubsetOf(superset),   server.ProperSubset);
        Assert.AreEqual(rates.Count,                        server.Count);
        Assert.AreEqual(rates.IsEmpty,                      server.IsEmpty);

        // array_remove keeps the array canonical, so the removal materializes equal.
        Assert.AreEqual(rates.Remove(Rate), server.Removed);

        // array_cat does not, but materialization re-canonicalizes through From.
        Assert.AreEqual(rates.Union(wanted), server.Union);
    }

    /// <summary>
    /// A NULL element written by another client must throw on read rather than materialize a
    /// default-valued wrapper — the same contract the closed families hold to, which
    /// <c>SetIntegrationTests.NullElementRow_ThrowsOnRead</c> pins for <c>text[]</c>.
    /// </summary>
    /// <remarks>
    /// The exception type differs by element kind, and not because of anything this package does.
    /// For a reference-typed element the driver hands back a <c>string?[]</c> containing the null
    /// and the set's own <c>From</c> rejects it — <see cref="ArgumentException"/>. For a
    /// value-typed one, which is every arity here, Npgsql refuses to build the array at all and
    /// throws <see cref="InvalidCastException"/> before this package sees a thing. Both are the
    /// required outcome, so the assertion is on the outcome; pinning either type alone would make
    /// this a test of the driver.
    /// </remarks>
    [TestMethod]
    public async Task NullElementInAWrapperColumn_ThrowsOnRead()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(new Reservation { Id = 8107 });
        await using (var context = new IntegrationDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE \"Reservations\" SET \"WrappedTenants\" = ARRAY[NULL]::uuid[] WHERE \"Id\" = 8107");
        }

        try
        {
            var loaded = await Load(8107);

            Assert.Fail(
                "a NULL element in a wrapper arity's column must throw — it is corrupt data by "
              + $"the type's contract. Materialized {loaded.WrappedTenants} instead.");
        }
        catch (Exception exception) when (exception is not AssertFailedException)
        {
            var rejected = false;
            for (var current = exception; current is not null; current = current.InnerException)
                rejected |= current is ArgumentException or InvalidCastException;

            Assert.IsTrue(
                rejected,
                $"Expected the read to be refused by either the set's own null guard or the "
              + $"driver's, got: {exception}");
        }
    }
}

/// <summary>
/// The twelve arities that had no column. Declared here rather than in
/// <see cref="IntegrationDbContext"/> so the shared wrapper element types can be imported without
/// colliding with that file's own <c>AuditStamp</c> and <c>BillingMonth</c>.
/// </summary>
public partial class Reservation
{
    public GuidSet<TenantId> WrappedTenants { get; set; } = GuidSet<TenantId>.Empty;

    public Int16Set<TinyCode> WrappedTinyCodes { get; set; } = Int16Set<TinyCode>.Empty;

    public Int32Set<SmallCode> WrappedCodes { get; set; } = Int32Set<SmallCode>.Empty;

    public Int64Set<LargeCode> WrappedBigCodes { get; set; } = Int64Set<LargeCode>.Empty;

    public DecimalSet<Money> WrappedRates { get; set; } = DecimalSet<Money>.Empty;

    public DateSet<BusinessDate> WrappedDays { get; set; } = DateSet<BusinessDate>.Empty;

    public TimeSet<ShiftTime> WrappedSlots { get; set; } = TimeSet<ShiftTime>.Empty;

    public DateTimeOffsetSet<EventStamp> WrappedStamps { get; set; } = DateTimeOffsetSet<EventStamp>.Empty;

    public LocalDateSet<CalendarDay> WrappedNodaDays { get; set; } = LocalDateSet<CalendarDay>.Empty;

    public LocalDateTimeSet<WallClockStamp> WrappedNodaMarks { get; set; } = LocalDateTimeSet<WallClockStamp>.Empty;

    public InstantSet<EventInstant> WrappedNodaMoments { get; set; } = InstantSet<EventInstant>.Empty;

    public LocalTimeSet<OpeningTime> WrappedNodaSlots { get; set; } = LocalTimeSet<OpeningTime>.Empty;
}
