# LogicPerformance

Read-only SQL Server regression and performance runner for Mizon.Server.Logic.
The normal path references the current project. Serialization uses a generated
System.Text.Json context; no new testing package is required.

Run from the server repository:

```powershell
dotnet build Tools/LogicPerformance/LogicPerformance.csproj -c Release -p:Platform=x64
dotnet Tools/LogicPerformance/bin/x64/Release/net10.0/LogicPerformance.dll D:/MizonDiagnostics/logic-results 30 --regressions --load
```

Arguments: output directory, measured iterations (1-1000), optional
`--regressions` and `--load`. The default connection matches the development
database using Windows authentication; override with the
`MIZON_BENCHMARK_CONNECTION` environment variable. Do not put credentials in
committed files.

Uses the existing alish account, existing active music and other existing rows.
Missing optional fixtures are reported as skipped. Run against stable development
data, with no concurrent edits if comparing response hashes/cursor stability.

The command interceptor permits only SELECT commands. Story regression checks
also intercept SaveChanges before persistence. Nothing is seeded or migrated.

Output:
- measurements.json: operation/command median and p95, command counts, approximate
  managed allocations, first streamed item time and response hashes.
- *.response.json: complete response snapshots, outside the measured interval.
- *.sql: distinct command texts, without parameter values.
- load.csv: optional 8-worker test results; each request owns its own context.

Command timings end when ExecuteReader returns. They do not include all row
consumption and are not server-only execution time. Operation timings include
consumption/assembly, excluding context creation, HTTP/auth and JSON serialization.
Allocation deltas use process-wide GC counters and should be interpreted as
approximate. Search snapshots are sorted by type/ID for comparison because
interleaving is intentionally nondeterministic.

For a before/after comparison with a saved compatible binary, build with
`-p:BaselineLogicDirectory=D:/path/to/baseline`; the directory must contain
Mizon.Server.Logic.dll, Mizon.Database.dll and Mizon.Server.dll from the same
baseline. Run before/after sequentially rather than simultaneously.

See ../../Mizon.Server.Logic/PERFORMANCE.md for the current findings and AOT limits.

