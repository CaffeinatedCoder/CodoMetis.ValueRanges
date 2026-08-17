using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// Hammers both registries from many threads at once and requires that nothing registered is
/// lost and nothing already registered disappears.
/// </summary>
/// <remarks>
/// <para>
/// The registries are the only mutable process-wide state in the packages. Everything they hold is
/// immutable and every lookup reads a snapshot, so the design is sound — but "sound by inspection"
/// is what the four receiver-shape dispatch bugs were too. What makes this worth a test rather than
/// a comment is the shape of the write: registration is copy-on-write over a whole snapshot
/// (<c>Current = BuildSnapshot([.. Current.Definitions, definition])</c>), which is a read-modify-write
/// and therefore loses an update the moment two of them interleave. The <c>volatile</c> field alone
/// would not prevent that; the lock is what does, and the lock is the part a later edit can drop
/// without any test noticing.
/// </para>
/// <para>
/// The interleaving is not hypothetical in the way it first looks. Registration happens from the
/// options-builder extension, on every context configuration — so it runs on whichever thread built
/// the context, and an application that constructs its first contexts from a warm-up loop, a
/// pooling factory, or several request threads at once has genuinely concurrent first calls.
/// </para>
/// <para>
/// Each registry is exercised through both of its mutating entry points <em>simultaneously</em>,
/// because that is the interesting case: they take the same lock and each rebuilds the snapshot
/// from fields the other one writes, so one clobbering the other is a distinct failure from either
/// one losing its own update. Readers run alongside, checking that the built-in registrations stay
/// visible — a rebuild that dropped them would otherwise show up only as a mapping failure in some
/// unrelated test.
/// </para>
/// <para>
/// The definitions and families registered here are marker types that no model can name, so they
/// stay in the process-wide registries for the rest of the run without being reachable: every
/// lookup is by CLR type, store type name or aggregate declaring type, and none of those can
/// arrive at a marker. Their type mappings throw, which is the loud version of that claim.
/// </para>
/// </remarks>
[TestClass]
public sealed class RegistryConcurrencyTests
{
    private const int Writers = 16;

    /// <summary>
    /// Rounds of the whole experiment, each with a fresh block of markers. A lost update is a race
    /// and one round can win it: with the lock removed, a single round of the set registry came
    /// through clean roughly one time in three. Four rounds is what makes the seeded defect fail
    /// reliably rather than usually — and, with the lock in place, no round can lose anything, so
    /// the repetition costs correctness nothing.
    /// </summary>
    private const int Rounds = 4;

    /// <summary>
    /// A type nobody can name in a model. Closing it over successively deeper array types
    /// manufactures as many distinct markers as the sweep needs — <c>Marker&lt;int[]&gt;</c>,
    /// <c>Marker&lt;int[][]&gt;</c>, … — from one declaration.
    /// </summary>
    private sealed class Marker<T>;

    private static Type MarkerType(Type seed, int index)
    {
        var type = seed;
        for (var level = 0; level <= index; level++) type = type.MakeArrayType();

        return typeof(Marker<>).MakeGenericType(type);
    }

    // A wrapper family is keyed by its open generic type definition, and generic type definitions
    // cannot be manufactured at run time — so concurrent family writers need one declaration each.
    private sealed class Family0<T>;

    private sealed class Family1<T>;

    private sealed class Family2<T>;

    private sealed class Family3<T>;

    private sealed class Family4<T>;

    private sealed class Family5<T>;

    private sealed class Family6<T>;

    private sealed class Family7<T>;

    private static readonly Type[] Families =
    [
        typeof(Family0<>), typeof(Family1<>), typeof(Family2<>), typeof(Family3<>),
        typeof(Family4<>), typeof(Family5<>), typeof(Family6<>), typeof(Family7<>)
    ];

    /// <summary>
    /// <see cref="RangeTypeRegistry"/> under both of its writers at once: sixteen range
    /// definitions and sixteen aggregate declaring types, released together.
    /// </summary>
    [TestMethod]
    public void RangeTypeRegistry_ConcurrentRegistration_LosesNothing()
    {
        var lost = new List<string>();

        for (var round = 0; round < Rounds; round++)
        {
            var block       = round * Writers;
            var definitions = Enumerable.Range(block, Writers).Select(index => new StubRangeDefinition(index)).ToArray();
            var aggregates  = Enumerable.Range(block, Writers).Select(index => MarkerType(typeof(uint), index)).ToArray();

            RunTogether(
            [
                .. definitions.Select<StubRangeDefinition, Action>(
                    definition => () => RangeTypeRegistry.Register(definition)),
                .. aggregates.Select<Type, Action>(
                    declaringType => () => RangeTypeRegistry.RegisterAggregateExtensions(declaringType))
            ],
            () =>
            {
                // The built-in registrations must stay resolvable throughout: a snapshot rebuilt
                // from a stale read would drop them, and nothing else in the suite would say so.
                Assert.IsTrue(RangeTypeRegistry.TryGetByClrType(typeof(Int32Range), out _, out _));
                Assert.IsTrue(RangeTypeRegistry.TryGetByStoreType("daterange", out _, out _));
                Assert.IsTrue(RangeTypeRegistry.TryGetByElementType(typeof(decimal), out _));
                Assert.IsTrue(RangeTypeRegistry.IsAggregateDeclaringType(typeof(RangeAggregateExtensions)));
            });

            lost.AddRange(definitions
                         .Where(definition => !RangeTypeRegistry.TryGetByClrType(definition.RangeClrType, out _, out _))
                         .Select(definition => definition.RangeStoreType));

            lost.AddRange(aggregates
                         .Where(declaringType => !RangeTypeRegistry.IsAggregateDeclaringType(declaringType))
                         .Select(declaringType => $"aggregate marker {declaringType.GetGenericArguments()[0].Name}"));
        }

        Assert.AreEqual(
            0, lost.Count,
            $"{lost.Count} of {Rounds * Writers * 2} concurrent registrations were lost: "
          + $"{string.Join(", ", lost)}. Registration is a read-modify-write over the whole snapshot, "
          + "so two of them interleaving drops one — the lock around it is load-bearing.");

        // Registration is additive and idempotent, so the range types the rest of the suite maps
        // are still there afterwards, not only during.
        Assert.IsTrue(RangeTypeRegistry.TryGetByClrType(typeof(DateRange), out _, out _));
        Assert.IsTrue(RangeTypeRegistry.TryGetByClrType(typeof(RangeSet<Int32Range, int>), out _, out var isSet));
        Assert.IsTrue(isSet);
    }

    /// <summary>
    /// <see cref="SetTypeRegistry"/> under both of its writers at once: sixteen closed set
    /// definitions and eight wrapper families. The families write a different field from the
    /// definitions under the same lock, which is the pairing worth running together.
    /// </summary>
    [TestMethod]
    public void SetTypeRegistry_ConcurrentRegistration_LosesNothing()
    {
        var lost = new List<string>();

        for (var round = 0; round < Rounds; round++)
        {
            var definitions = Enumerable.Range(round * Writers, Writers)
                                        .Select(index => new StubSetDefinition(index))
                                        .ToArray();

            // The families are declared, not manufactured, so the same eight are offered every
            // round — after the first they take the already-registered fast path, which is the
            // other half of the entry point and worth running under contention too.
            RunTogether(
            [
                .. definitions.Select<StubSetDefinition, Action>(
                    definition => () => SetTypeRegistry.Register(definition)),
                .. Families.Select<Type, Action>(
                    family => () => SetTypeRegistry.RegisterFamily(family, static _ => new StubSetDefinition(-1)))
            ],
            () =>
            {
                Assert.IsTrue(SetTypeRegistry.TryGetByClrType(typeof(StringSet), out _));
                Assert.IsTrue(SetTypeRegistry.TryGetByClrType(typeof(Int64Set), out _));

                // A wrapper family resolves through the lazy per-instantiation cache, which is a
                // second piece of shared state the writers touch.
                Assert.IsTrue(SetTypeRegistry.TryGetByClrType(typeof(GuidSet<TestGuidKey>), out _));
            });

            lost.AddRange(definitions
                         .Where(definition => !SetTypeRegistry.TryGetByClrType(definition.SetClrType, out _))
                         .Select(definition => definition.ArrayStoreType));

            lost.AddRange(Families
                         .Where(family => !SetTypeRegistry.TryGetByClrType(family.MakeGenericType(typeof(int)), out _))
                         .Select(family => family.Name));
        }

        Assert.AreEqual(
            0, lost.Count,
            $"{lost.Count} of {Rounds * (Writers + Families.Length)} concurrent registrations were lost: "
          + $"{string.Join(", ", lost)}. Both entry points replace a frozen collection with a copy of "
          + "itself plus one entry, which loses an update whenever two of them interleave.");

        Assert.IsTrue(SetTypeRegistry.TryGetByClrType(typeof(DecimalSet), out _));
    }

    /// <summary>
    /// Releases every writer at once and runs <paramref name="read"/> on two further threads for
    /// the duration, so lookups and registrations genuinely overlap rather than merely both
    /// happening.
    /// </summary>
    private static void RunTogether(Action[] writers, Action read)
    {
        using var start = new ManualResetEventSlim(false);
        using var done  = new ManualResetEventSlim(false);

        var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            start.Wait();
            while (!done.IsSet) read();
        })).ToArray();

        var writes = writers.Select(write => Task.Run(() =>
        {
            start.Wait();
            write();
        })).ToArray();

        start.Set();
        Task.WaitAll(writes);
        done.Set();
        Task.WaitAll(readers);

        // One final read on this thread, after every writer has finished.
        read();
    }

    // -------------------------------------------------------------------------
    // Registration-only stubs
    // -------------------------------------------------------------------------

    /// <summary>
    /// A definition that exists to be registered and looked up, nothing else. Everything the
    /// mapping source or a translator would ask for throws, so a marker that somehow became
    /// reachable would fail loudly instead of mapping a column to nonsense.
    /// </summary>
    private sealed class StubRangeDefinition(int index) : IRangeTypeDefinition
    {
        public Type RangeClrType { get; } = MarkerType(typeof(int), index);

        public Type ElementClrType { get; } = MarkerType(typeof(string), index);

        public Type RangeSetClrType { get; } = MarkerType(typeof(long), index);

        public string RangeStoreType { get; } = $"__marker_range_{index}";

        public string MultirangeStoreType { get; } = $"__marker_multirange_{index}";

        public string ElementStoreType { get; } = $"__marker_element_{index}";

        public bool IsDiscrete => false;

        public RelationalTypeMapping RangeTypeMapping => throw Unreachable();

        public RelationalTypeMapping RangeSetTypeMapping => throw Unreachable();

        public object EmptyRange => throw Unreachable();

        public object InfiniteRangeSet => throw Unreachable();
    }

    /// <inheritdoc cref="StubRangeDefinition"/>
    private sealed class StubSetDefinition(int index) : ISetTypeDefinition
    {
        public Type SetClrType { get; } = MarkerType(typeof(short), index);

        public Type ElementClrType { get; } = MarkerType(typeof(byte), index);

        public string ElementStoreType { get; } = $"__marker_set_element_{index}";

        public string ArrayStoreType { get; } = $"__marker_set_array_{index}";

        public RelationalTypeMapping SetTypeMapping => throw Unreachable();

        public object EmptySet => throw Unreachable();
    }

    private static NotSupportedException Unreachable() =>
        new("This definition exists only to be registered by RegistryConcurrencyTests. Nothing can "
          + "resolve it — its CLR types are private markers and its store type names are not real "
          + "PostgreSQL types — so reaching a type mapping through it is a defect in the registry "
          + "lookups, not in this test.");

    /// <summary>An element type for the pre-existing wrapper family the readers resolve.</summary>
    private readonly record struct TestGuidKey(Guid Value) : IFormattable, IParsable<TestGuidKey>, IComparable<TestGuidKey>
    {
        public static TestGuidKey Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

        public static bool TryParse(string? s, IFormatProvider? provider, out TestGuidKey result)
        {
            var parsed = Guid.TryParse(s, out var value);
            result = parsed ? new TestGuidKey(value) : default;
            return parsed;
        }

        public int CompareTo(TestGuidKey other) => Value.CompareTo(other.Value);

        public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);

        public override string ToString() => Value.ToString();
    }
}
