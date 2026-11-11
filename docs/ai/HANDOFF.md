# Server handoff — .NET 11 upgrade, 2026-11-10

## Main-window profile presence — 2026-11-11

The authenticated hub now acknowledges main-window focus through the existing
connection, with caller-only events, connection identity and monotonic intent
revisions. State is ephemeral and cleared on disconnect; no database or global
presence broadcast was added. Real loopback production SignalR/generated JSON and
the existing API diagnostics pass. See the [contract and checks](tasks/2026-11-11-profile-window-presence.md).
Coordinate the matching SDK/Windows changes before deploying this feature.

## Taskbar companion solution restore — 2026-11-11

The latest VS Rebuild log passed the server but failed with NETSDK1005 for
`Mizon.Windows.Taskbar`: its assets still targeted .NET 10. The open `.slnx`
omitted the companion, although the legacy `.sln` included it. It is now included
with an x64 mapping so solution restore refreshes its .NET 11 dependencies.
VS MSBuild Debug/x64 solution Restore and Rebuild passed (0 errors; existing
warnings remain). Main-app output contains the companion payload. No Windows
source, runtime behavior or deployment changed. See the
[task record](tasks/2026-11-11-taskbar-solution-restore.md) for validation limits.

## Visual Studio build compatibility — 2026-11-11

The API's SDK 11.0.100 StaticWebAssets tasks could not load
`Microsoft.Bcl.AsyncInterfaces, Version=10.0.0.10` in Visual Studio's
full-framework MSBuild. The project now imports scoped .NET TaskHost overrides
through the SDK's `CustomAfterStaticWebAssetsSdkTargets` hook. Hot Reload and
static assets remain enabled; no application packages or SDK installation files
were changed. The override applies only to Full MSBuild with SDK 11.0.100.

The [empty-cache follow-up](tasks/2026-11-11-empty-static-assets-cache.md) adds
automatic recovery for the SDK's five known empty/whitespace-only asset caches.
It runs before consumers in normal and design-time builds, under either VS MSBuild
or CLI with SDK 11.0.100. It preserves valid caches and other configuration/output
files; there is no recursive cleanup or cache-disable flag.

See [the reproduction and verification note](tasks/2026-11-11-vs-static-web-assets.md).
Do not replace this with a runtime NuGet reference or remove it based only on a
successful CLI build: CLI uses a different task runtime. When upgrading the SDK,
verify fresh Debug, incremental Debug and Release builds using VS MSBuild.

## Upgrade checkpoint

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

## Music home demo artwork — 2026-11-11

The twelve known sample stories and four featured hero slides now use licensed
music photography. An explicit .NET Logic operation inserted eighteen dedicated
Media rows and repointed only those fixture content/preview relations. Existing
avatars, music covers, original media, authors and widgets are preserved. Video
fixtures use silent twelve-second photo-motion loops with matching frame previews.

Assets and attribution are committed under `Mizon.Server.API/App_Data/MusicHomeArtwork`.
The tool's artwork mode is plan-only unless `--apply` is passed; it is not an API
endpoint or startup seed. Exact scope, readback/idempotency and preservation results:
[artwork task record](tasks/2026-11-11-music-home-artwork.md).
No schema/UI changes or Windows automation were required.
