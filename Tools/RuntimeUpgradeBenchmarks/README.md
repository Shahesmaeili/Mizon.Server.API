# Runtime upgrade comparison

Reproducible comparison of the Mizon .NET 10 baseline and .NET 11 upgrade.
Build and retain both versions before running; never compare a stale debug binary
with a fresh release binary. `global.json` in the upgraded repositories pins 11.0.100.
Use a detached checkout of the baseline commit and its recorded SDK for .NET 10.

## Required retained artifacts

Under each of `baseline/` and `candidate/`:

- `database/`: Release/x64 Mizon.Database.Tests, with `--benchmark` mode.
- `sdk/`: Release/x64 Tools/SdkBenchmarks from Mizon.API.DotNet.
- `windows/`: Release/x64 Mcp/Mizon.Windows.PerformanceBenchmarks.
- `api/`: Release/x64 Tools/ApiDiagnostics with `--component-benchmark` mode.
- `logic/`: Release/x64 Tools/LogicPerformance, including all dependencies.

The same benchmark source and fixture payloads must be compiled in both versions.
Baseline source hashes and candidate changes are recorded with the report.
Do not upload LogicPerformance response snapshots, SQL parameters or connection strings.

```powershell
./Tools/RuntimeUpgradeBenchmarks/Run-Comparison.ps1 `
    -ArtifactRoot D:/MizonTaskDiagnostics/Net11 `
    -OutputDirectory D:/MizonTaskDiagnostics/Net11/results `
    -Rounds 5 -SqlIterations 80
```

## Interpretation

- Run benchmarks sequentially, without simultaneous builds. Versions alternate
  order, with a fresh process for each category/round. Normal desktop services
  remain running, so small timing differences may be noise.
- `DOTNET_TieredCompilation=0` is set identically for steady-state measurements,
  then restored. This is a benchmark setting, not a production configuration change.
- This is a complete SDK/runtime/framework dependency upgrade comparison; it does
  not isolate the JIT from EF, SqlClient, ASP.NET Core or Extensions improvements.
- SDK uses synthetic HTTP responses through the real Mizon transport and generated
  JSON. Windows cases use real route parsing and MCP framing without UI or pipe IPC.
- API cases use the real NDJSON writer/generated metadata and real Kestrel/Mizon
  OutputCache middleware. HTTP cases include both loopback client and server, with
  synthetic identities/payloads and no SQL. Cache hit and bypass are validated.
- Database model/context cases do not open SQL connections. LogicPerformance runs
  actual operations against the local development database with a SELECT-only
  interceptor. It never seeds or persists state.
- Allocation bytes per operation are allocation traffic, not retained memory or
  total WinUI working set. Do not infer application RAM reduction from them.
- Report medians across process medians and the run ranges. Compare query counts,
  payload checksums and result shapes as well as speed. Fast errors are not wins.
- Keep .NET 10/11 build and functional test evidence separate from timings.
