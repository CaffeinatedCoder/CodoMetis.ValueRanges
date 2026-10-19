using NodaTime;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// A <c>LocalDateRange</c> shifted by a period of months moves as PostgreSQL moves the same
/// <c>daterange</c>: both bounds of the half-open form, each by interval arithmetic. That includes the
/// short ranges a month or year shift empties, which the maintainer kept as PostgreSQL's answer (#259).
/// </summary>
[TestClass]
public sealed class CalendarShiftAgreementTests
{
    [TestMethod]
    [DataRow("2024-02-28", "2024-02-28", 0, 1)]
    [DataRow("2024-03-30", "2024-03-30", 1, 0)]
    [DataRow("2024-01-29", "2024-01-29", 1, 0)]
    [DataRow("2023-12-30", "2023-12-30", -1, 0)]
    [DataRow("2024-02-01", "2024-02-29", 1, 0)]
    [DataRow("2024-01-31", "2024-02-27", 1, 0)]
    [DataRow("2024-02-28", "2024-03-01", 0, 1)]
    public async Task LocalDateRange_ShiftByMonthsOrYears_AgreesWithPostgres(string first, string last, int months, int years)
    {
        ContainerLifecycle.RequireDatabase();

        var start = LocalDate.FromDateOnly(DateOnly.Parse(first, System.Globalization.CultureInfo.InvariantCulture));
        var end   = LocalDate.FromDateOnly(DateOnly.Parse(last, System.Globalization.CultureInfo.InvariantCulture));
        var model = LocalDateRange.CreateFinite(start, end).Shift(Period.FromMonths(months) + Period.FromYears(years));

        var interval = $"interval '{months} months {years} years'";
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT daterange(('{first}'::date + {interval})::date, ('{last}'::date + 1 + {interval})::date)::text", connection);
        var server = (string)(await command.ExecuteScalarAsync())!;

        Assert.AreEqual(LocalDateRange.Parse(server, null), model, $"[{first},{last}] + {months} months {years} years: PostgreSQL {server}");
    }
}
