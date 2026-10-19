# Oracles

The machinery that turns bug-finding from one-off sweeps into a system: the claim catalogue
(`claims.json`), the registry of oracle test classes (`oracles.json`), the seeds that prove each
oracle can fail (`seeds/`), the public-surface waivers (`surface-waivers.json`), the known
disagreements (`candidates.json`), the test classes compiled out on this line
(`compiled-out.json`), the hand-written list of what a consumer varies (`axes.md`), which the `*ProbeTests` sweeps take their products from, and the clone-divergence sweep over the
hand-copied type families (`CloneSweep.cs`) with the waivers that explain each difference between
siblings that is right as it is (`clone-waivers.json`). The convention tests in
`test/CodoMetis.ValueRanges.Conventions.Tests/Oracle*.cs` and `CloneDivergenceTests.cs` hold all of it
consistent; `dotnet run tools/oracles.cs` proves the seeds locally and prints the sibling divergences.
[docs/oracles.md](../../docs/oracles.md) explains it.
