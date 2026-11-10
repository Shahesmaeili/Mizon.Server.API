# Server handoff — .NET 11 upgrade, 2026-11-10

All 13 server, Logic, shared infrastructure and utility projects target .NET 11.
SDK11.0.100 is selected by global.json with patch roll-forward. ASP.NET Core,
EF Core and related framework packages are aligned to 11.0.0. The two tracked
Visual Studio folder publish profiles also use net11.0.

Database, .NET SDK and Windows upgrades are coordinated in the same task.
Baseline executables were retained before changing production targets.
No schema, migration, stored data, API contract, authorization rule or UI design changed.

## Validation

- All 13 projects: Release/x64 build passed.
- API isolated checks passed: authentication, generated NDJSON/backpressure,
  diagnostics, SignalR lifecycle/concurrent membership, real OutputCache
  isolation/expiry/invalidation/failure/cancellation.
- Baseline/candidate SQL smoke runs: all 53 read-only operation cases passed;
  response hashes and query counts match. Repeated measurements are separate.
- SQL checks use a SELECT-only interceptor; no seeds/imports/migrations ran.

See [the task record](tasks/2026-11-10-net11-upgrade.md) and
[benchmark workflow](../../Tools/RuntimeUpgradeBenchmarks/README.md).
Older PERFORMANCE.md results are historical, not .NET10-versus11 measurements.

## Boundaries

- Full EF-consuming API NativeAOT support is not established. Model/component
  NativeAOT checks do not prove all dynamically composed queries support AOT.
- EF/SqlClient AOT and trimming warnings remain visible where applicable.
- Allocation traffic is not total WinUI working set, GPU memory or frame rate.
- No Windows registration, deployment, application restart or UI automation was
  performed by this server migration.
- The existing development JWT value previously approved by the user is unchanged.

Read the performance report and cross-repository task notes before further work.

## Actual WinUI memory follow-up —2026-11-11

[Real packaged app measurements](../performance/net11/WINUI_MEMORY.md) now cover
Login and MusicHome, separately from the earlier headless allocations/SDK peaks.
Twelve valid Release/x64 runs produced360 samples. Private resident medians were
59.28→59.91MiB and181.46→182.07MiB; ranges overlap, so no meaningful reduction is
claimed. One .NET10native crash was excluded and documented. Original package
registration/state was restored. Reproducible external tools and sanitized evidence
are included; no product code or API behavior changed in this follow-up.
