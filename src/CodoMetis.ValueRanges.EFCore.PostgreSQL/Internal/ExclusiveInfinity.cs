namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The extremes of a discrete type that Npgsql writes as <c>-infinity</c> / <c>infinity</c>, and the bound memory
/// loads for an exclusive bound at each: one step inside, as the closed form puts it (#489).
/// </summary>
/// <remarks>
/// PostgreSQL canonicalizes a finite discrete bound to <c>[x</c> and <c>y)</c> but leaves an infinite one as it is,
/// so <c>[2026-01-01,infinity)</c> and <c>(-infinity,2026-01-01)</c> exist, from another tool or from the server's own
/// algebra (<c>(,) - (,-infinity]</c> is <c>(-infinity,)</c>). Memory loads them as <c>[2026-01-01,9999-12-30]</c> and
/// <c>[0001-01-02,2025-12-31]</c> (<c>docs/architecture.md#domain-width-and-the-boundary</c>), while
/// <c>upper(x) - 1</c> is still <c>infinity</c> and <c>lower(x)</c> is <c>-infinity</c>. The bound readers test for
/// the sentinel and answer <see cref="Lower"/> or <see cref="Upper"/> there. A bound PostgreSQL's <c>date</c> can't
/// hold is <see langword="null"/>, and the reader keeps the extreme on that side.
/// </remarks>
/// <param name="Min">The value Npgsql writes as <c>-infinity</c>.</param>
/// <param name="Lower">What memory loads for an exclusive <c>-infinity</c> lower bound, or <see langword="null"/>.</param>
/// <param name="Max">The value Npgsql writes as <c>infinity</c>.</param>
/// <param name="Upper">What memory loads for an exclusive <c>infinity</c> upper bound, or <see langword="null"/>.</param>
internal sealed record ExclusiveInfinity(object Min, object? Lower, object Max, object? Upper);
