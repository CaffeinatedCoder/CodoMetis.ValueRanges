namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// For a discrete type whose element spans several values of its store type, how the upper bound readers step
/// back from the stored exclusive bound to the element's own value: by <see cref="Step"/> rather than by 1, and
/// to <see cref="Last"/> from an inclusive <c>infinity</c> (#506).
/// </summary>
/// <remarks>
/// A <c>YearMonthRange</c> is a month-aligned <c>daterange</c>, and a <c>YearMonth</c> operand binds as the first
/// day of its month. <c>upper(x) - 1</c> is the last day of the last month: it loads as that month, but compares
/// unequal to it, and greater, in a filter. <c>upper(x) - INTERVAL '1 month'</c> is the first day. The last month,
/// 9999-12, ends on the date Npgsql writes as <c>infinity</c>, which no interval steps back from, so the reader
/// answers <see cref="Last"/> there. With <c>Npgsql.DisableDateTimeInfinityConversions</c> set the bound is a
/// finite 10000-01-01 and the interval reaches 9999-12-01 on its own. Measured on PostgreSQL 16 and 18.
/// </remarks>
/// <param name="Step">The span of one element, as a PostgreSQL interval literal, e.g. <c>1 month</c>.</param>
/// <param name="Last">The element an inclusive <c>infinity</c> upper bound stands for.</param>
internal sealed record CoarseUpperBound(string Step, object Last);
