# Oracles

The machinery that turns bug-finding from one-off sweeps into a system: the claim catalogue
(`claims.json`), the registry of oracle test classes (`oracles.json`), the seeds that prove each
oracle can fail (`seeds/`), the public-surface waivers (`surface-waivers.json`), the known
disagreements (`candidates.json`) and the hand-written list of what a consumer varies (`axes.md`),
which the `*ProbeTests` sweeps take their products from. The convention tests in
`test/CodoMetis.ValueRanges.Conventions.Tests/Oracle*.cs` hold all of it consistent; `dotnet run
tools/oracles.cs` proves the seeds locally. [docs/oracles.md](../../docs/oracles.md) explains it.
