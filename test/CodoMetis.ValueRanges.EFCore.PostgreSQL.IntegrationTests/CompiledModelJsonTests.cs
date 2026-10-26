// DesignTimeServicesBuilder and OperationReporter are EF Core's internal design-time API: the services
// `dotnet ef dbcontext optimize` builds, which is exactly what this test has to run.
#pragma warning disable EF1001

using System.Reflection;
using System.Runtime.Loader;
using CodoMetis.ValueRanges.Conventions.Tests;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// A namespace of its own: the compiled model imports the namespace of every type it names, and the suite's
// own namespace declares an AuditStamp and a BillingMonth beside the conventions project's.
namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.CompiledModels;

/// <summary>One row per value: the value inside a jsonb document.</summary>
public sealed class CompiledJsonRow<TValue>
{
    public int Id { get; set; }

    public CompiledJsonDoc<TValue> Doc { get; set; } = new();
}

/// <summary>The jsonb document a <see cref="CompiledJsonRow{TValue}"/> holds.</summary>
public sealed class CompiledJsonDoc<TValue>
{
    public TValue Value { get; set; } = default!;
}

/// <summary>
/// Every discovered range, multirange and value set type inside a jsonb document, one table each. With
/// a model the context runs on it instead of building one: the compiled model
/// <c>dotnet ef dbcontext optimize</c> would write for it.
/// </summary>
public sealed class CompiledJsonContext(string connectionString, IModel? model = null) : DbContext
{
    internal static IEnumerable<Type> ValueTypes =>
        FuzzTypes.Ranges.SelectMany(pair => new[] { pair.Range, typeof(RangeSet<,>).MakeGenericType(pair.Range, pair.Element) })
                 .Concat(FuzzTypes.Sets.Select(triple => triple.Set));

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.UseValueRangesNodaTime()
                            .ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));
        if (model is not null) optionsBuilder.UseModel(model);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var value in ValueTypes)
        {
            var row = modelBuilder.Entity(typeof(CompiledJsonRow<>).MakeGenericType(value));
            row.ToTable($"compiled_{FuzzTypes.Name(value)}");
            row.Property(nameof(CompiledJsonRow<int>.Id)).ValueGeneratedNever();
            row.ComplexProperty(typeof(CompiledJsonDoc<>).MakeGenericType(value), nameof(CompiledJsonRow<int>.Doc), doc => doc.ToJson());
        }

        modelBuilder.HasPostgresRange("timerange", "time");
    }
}

/// <summary>
/// A compiled model (<c>dotnet ef dbcontext optimize</c>) of a context with every discovered range,
/// multirange and value set type inside a JSON column, generated, compiled and loaded in-process, then
/// held to the model EF Core builds at run time against a live server (#43).
/// </summary>
/// <remarks>
/// <para>
/// The generation is the one the <c>dbcontext optimize</c> command runs: <see cref="DesignTimeServicesBuilder"/>
/// over the context, which adds the provider's design-time services, then the C# compiled-model
/// generator. The files are compiled with Roslyn against the assemblies this process has loaded, and the
/// generated model's <c>Instance</c> is handed to <c>UseModel</c>. Each value is then written through each
/// model and read through both, and the document text each model writes is compared on the server. The
/// runtime model is the oracle; the expected values are its own, so the store's documented truncation of a
/// temporal value never reads as a disagreement.
/// </para>
/// <para>
/// What it holds: that the model generates and compiles with every type inside a JSON column, and that
/// every type saves and loads through it as through the runtime model. The standard compiled model carries
/// no type mappings; it resolves them through the plugin at run time, by CLR type, exactly as the runtime
/// model does, so a defect in the plugin's mappings fails both legs alike (a reader/writer that throws for
/// one type is reported as "saving failed" for that type). Only the Native AOT form
/// (<c>--nativeaot</c>) emits the mappings, and with them each JSON reader/writer's
/// <c>ConstructorExpression</c>. That form cannot be generated yet, because the plugin's mappings have no
/// static <c>Default</c> (#209).
/// </para>
/// </remarks>
[TestClass]
public sealed class CompiledModelJsonTests
{
    // 10 ranges, their 10 multiranges, 28 set types (FuzzTypes, LiteralCorpusProbeTests).
    private const int KnownTypes = 48;

    [TestMethod]
    public async Task EveryType_RoundTripsThroughACompiledModel_AsThroughTheRuntimeModel()
    {
        ContainerLifecycle.RequireDatabase();

        var types = CompiledJsonContext.ValueTypes.ToList();
        Assert.IsGreaterThanOrEqualTo(KnownTypes, types.Count, $"Found {types.Count} types, fewer than {KnownTypes}.");

        var connectionString = new NpgsqlConnectionStringBuilder(ContainerLifecycle.ConnectionString)
        {
            Database = $"valueranges_compiled_{Guid.NewGuid():N}"
        }.ConnectionString;

        await using (var runtime = new CompiledJsonContext(connectionString))
            await runtime.Database.EnsureCreatedAsync();

        var compiledModel = CompileModel(connectionString);
        var failures      = new List<string>();
        var compared      = 0;

        foreach (var type in types)
        {
            var sweep = typeof(CompiledModelJsonTests).GetMethod(nameof(Sweep), BindingFlags.NonPublic | BindingFlags.Static)!
                                                      .MakeGenericMethod(type);
            compared += await (Task<int>)sweep.Invoke(null, [connectionString, compiledModel, failures])!;
        }

        Console.WriteLine($"{types.Count} types, {compared} values compared");
        Assert.IsGreaterThanOrEqualTo(types.Count * 2, compared, $"Only {compared} values were compared.");
        Assert.IsEmpty(failures, "The compiled model disagrees with the runtime model:\n" + string.Join("\n", failures.Take(40)));
    }

    /// <summary>One type: its reader/writer, then each value written through each model and read through both.</summary>
    private static async Task<int> Sweep<TValue>(string connectionString, IModel compiledModel, List<string> failures)
    {
        var name   = FuzzTypes.Name(typeof(TValue));
        var values = Values<TValue>();

        await using (var runtime = new CompiledJsonContext(connectionString))
        await using (var compiled = new CompiledJsonContext(connectionString, compiledModel))
        {
            var runtimeWriter  = ReaderWriterOf<TValue>(runtime);
            var compiledWriter = ReaderWriterOf<TValue>(compiled);
            if (compiledWriter?.GetType() != runtimeWriter?.GetType())
                failures.Add($"{name}: the compiled model reads and writes the document with {Show(compiledWriter)}, the runtime model with {Show(runtimeWriter)}");

            for (var i = 0; i < values.Count; i++)
            {
                runtime.Add(new CompiledJsonRow<TValue> { Id = i, Doc = new() { Value = values[i] } });
                compiled.Add(new CompiledJsonRow<TValue> { Id = 1000 + i, Doc = new() { Value = values[i] } });
            }

            try
            {
                await runtime.SaveChangesAsync();
                await compiled.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: saving failed: {ex.GetBaseException().Message}");
                return 0;
            }
        }

        var table = $"compiled_{name}";
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            try
            {
                var runtimeDocument  = await DocumentText(connection, table, i);
                var compiledDocument = await DocumentText(connection, table, 1000 + i);
                if (compiledDocument != runtimeDocument)
                    failures.Add($"{name} {value}: the compiled model writes {compiledDocument}, the runtime model {runtimeDocument}");

                var expected = await ReadAsync<TValue>(connectionString, null, i);
                foreach (var (id, label) in new[] { (i, "a row the runtime model wrote"), (1000 + i, "its own row") })
                {
                    var read = await ReadAsync<TValue>(connectionString, compiledModel, id);
                    if (!Equals(read, expected))
                        failures.Add($"{name} {value}: the compiled model reads {label} as {read}, the runtime model as {expected}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{name} {value}: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
            }
        }

        return values.Count;
    }

    private static async Task<TValue> ReadAsync<TValue>(string connectionString, IModel? model, int id)
    {
        await using var context = new CompiledJsonContext(connectionString, model);
        var row = await context.Set<CompiledJsonRow<TValue>>().AsNoTracking().SingleAsync(r => r.Id == id);
        return row.Doc.Value;
    }

    private static async Task<string?> DocumentText(NpgsqlConnection connection, string table, int id)
    {
        await using var command = new NpgsqlCommand($"SELECT \"Doc\"::text FROM \"{table}\" WHERE \"Id\" = {id}", connection);
        return (string?)await command.ExecuteScalarAsync();
    }

    /// <summary>The reader/writer EF Core uses for the value inside the document, as the model holds it.</summary>
    private static object? ReaderWriterOf<TValue>(DbContext context)
    {
        var property = context.Model.FindEntityType(typeof(CompiledJsonRow<TValue>))!
                              .FindComplexProperty(nameof(CompiledJsonRow<TValue>.Doc))!.ComplexType
                              .FindProperty(nameof(CompiledJsonDoc<TValue>.Value))!;
        return property.GetJsonValueReaderWriter() ?? property.GetTypeMapping().JsonValueReaderWriter;
    }

    private static string Show(object? writer) => writer?.GetType().Name ?? "none";

    // ── Values ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>A range's five shapes; a multirange's empty, five-shape and gapped sets; a set's empty and probe sets.</summary>
    private static IReadOnlyList<TValue> Values<TValue>()
    {
        var type  = typeof(TValue);
        var range = FuzzTypes.Ranges.FirstOrDefault(pair => pair.Range == type);
        var (method, arguments) =
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RangeSet<,>) ? (nameof(MultirangeValues), type.GetGenericArguments())
          : range.Range is not null                                                     ? (nameof(RangeValues), [range.Range, range.Element])
          :                                                                               (nameof(SetValues), new[] { type, FuzzTypes.Sets.Single(triple => triple.Set == type).Element });

        return (IReadOnlyList<TValue>)typeof(CompiledModelJsonTests).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
                                                                     .MakeGenericMethod(arguments).Invoke(null, null)!;
    }

    private static IReadOnlyList<TRange> RangeValues<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => RangeProbes.Shapes<TRange, T>();

    private static IReadOnlyList<RangeSet<TRange, T>> MultirangeValues<TRange, T>()
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (low, high) = ((T)RangeProbes.Bounds[typeof(T)].Low, (T)RangeProbes.Bounds[typeof(T)].High);

        return
        [
            RangeSet<TRange, T>.Empty,
            RangeSet<TRange, T>.From(RangeProbes.Shapes<TRange, T>()),
            RangeSet<TRange, T>.From([TRange.CreateUnboundedStart(low, false), TRange.CreateUnboundedEnd(high, false)])
        ];
    }

    private static IReadOnlyList<TSet> SetValues<TSet, T>()
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
        => [TSet.Empty, TSet.From(SetProbes.For<T>())];

    // ── The compiled model ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What <c>dotnet ef dbcontext optimize</c> writes for <see cref="CompiledJsonContext"/>, compiled and
    /// loaded: its <c>Instance</c>.
    /// </summary>
    private static IModel CompileModel(string connectionString)
    {
        using var context = new CompiledJsonContext(connectionString);
        return CompileModel(context, "CodoMetis.ValueRanges.CompiledJson");
    }

    /// <summary>
    /// The compiled model <c>dotnet ef dbcontext optimize</c> writes for <paramref name="context"/>, compiled and loaded.
    /// </summary>
    internal static IModel CompileModel(DbContext context, string modelNamespace)
    {
        var contextType = context.GetType();
        var assembly = contextType.Assembly;
        var services = new DesignTimeServicesBuilder(assembly, assembly, new OperationReporter(handler: null), [])
                      .Build(context);

        var options = new CompiledModelCodeGenerationOptions
        {
            ContextType               = contextType,
            ModelNamespace            = modelNamespace,
            Language                  = "C#",
            UseNullableReferenceTypes = true
        };
        var files = services.GetRequiredService<ICompiledModelCodeGeneratorSelector>()
                            .Select(options)
                            .GenerateModel(context.GetService<IDesignTimeModel>().Model, options);

        var compilation = CSharpCompilation.Create(
            $"CompiledModel_{Guid.NewGuid():N}",
            files.Select(file => CSharpSyntaxTree.ParseText(file.Code, path: file.Path)),
            AppDomain.CurrentDomain.GetAssemblies()
                     .Where(loaded => !loaded.IsDynamic && loaded.Location.Length > 0)
                     .Select(loaded => MetadataReference.CreateFromFile(loaded.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        var errors  = emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.IsTrue(emitted.Success,
                      $"The compiled model ({files.Count} generated files) does not compile ({errors.Count} errors):\n"
                    + string.Join("\n", errors.Take(20)));

        image.Position = 0;
        var model = AssemblyLoadContext.Default.LoadFromStream(image)
                                       .GetTypes()
                                       .Single(type => type.Name == $"{contextType.Name}Model")
                                       .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!
                                       .GetValue(null);
        return (IModel)model!;
    }
}
