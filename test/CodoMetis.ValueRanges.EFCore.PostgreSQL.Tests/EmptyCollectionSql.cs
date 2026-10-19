using System.Reflection;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// The SQL an empty captured collection of ranges or value sets reads no rows through, in the shape
/// the EF Core this test host resolved gives it.
/// </summary>
/// <remarks>
/// <para>
/// The plugin's <c>src/</c> projects compile against the 10.0.0 floor, and so does this project unless
/// the host is floated to a newer patch (the one-off and CI legs of #297). EF Core 10.0.0 and 10.0.1
/// hand the plugin the empty <c>VALUES</c>, which it replaces with one row of typed <c>NULL</c>s under
/// <c>WHERE FALSE</c> (#137). EF Core 10.0.2 and later replace it themselves with a table-less
/// <c>SELECT NULL AS "Value" WHERE FALSE</c> (dotnet/efcore#37216), and the plugin casts the bare
/// <c>NULL</c> to the column's store type, since PostgreSQL types it as <c>text</c> otherwise and
/// fails the operator on it (#302). The off switch <c>Microsoft.EntityFrameworkCore.Issue37216</c>
/// brings the first shape back on a later patch, so it is honoured here too.
/// </para>
/// <para>
/// Read from <c>Microsoft.EntityFrameworkCore.Relational</c>, the assembly that holds the change,
/// through its informational version: the assembly version stays <c>10.0.0.0</c> across patches.
/// </para>
/// </remarks>
internal static class EmptyCollectionSql
{
    /// <summary>The first EF Core patch that replaces an empty <c>VALUES</c> itself (dotnet/efcore#37248).</summary>
    private static readonly Version EfCoreReplacesEmptyValuesSince = new(10, 0, 2);

    /// <summary>The EF Core Relational version the test host resolved, as <c>major.minor.patch</c>.</summary>
    public static Version EfCoreVersion { get; } = ResolveEfCoreVersion();

    /// <summary>
    /// Whether EF Core itself writes the no-rows query root, so the plugin only types its <c>NULL</c>s.
    /// </summary>
    public static bool EfCoreReplacesTheEmptyValues
        => EfCoreVersion >= EfCoreReplacesEmptyValuesSince
        && !(AppContext.TryGetSwitch("Microsoft.EntityFrameworkCore.Issue37216", out var legacy) && legacy);

    /// <summary>
    /// The <c>FROM … WHERE …</c> of the subquery that reads no rows, as <c>ToQueryString</c> prints it with
    /// line breaks replaced by one space: the typed <c>NULL</c> is <c>NULL::<paramref name="storeType"/></c>
    /// and, where EF Core keeps the predicate, <paramref name="predicate"/> is what it reads.
    /// </summary>
    public static string FromNoRows(string storeType, string predicate)
        => EfCoreReplacesTheEmptyValues
            ? $"FROM (         SELECT NULL::{storeType} AS \"Value\"         WHERE FALSE     ) AS w     WHERE {predicate}"
            : $"FROM (VALUES (NULL::{storeType})) AS w(\"Value\")     WHERE FALSE";

    private static Version ResolveEfCoreVersion()
    {
        var relational    = typeof(SelectExpression).Assembly;
        var informational = relational.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? relational.GetName().Version?.ToString()
                         ?? throw new InvalidOperationException("Microsoft.EntityFrameworkCore.Relational carries no version.");

        // "10.0.13+<commit>", or "11.0.0-rc.1.…" for a prerelease: the numeric part alone.
        var numeric = informational.Split('+', '-')[0];
        return Version.Parse(numeric);
    }
}
