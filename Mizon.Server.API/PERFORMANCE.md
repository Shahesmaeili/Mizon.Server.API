# API performance and NativeAOT review — 2026-11-08

Update 2026-11-09: explicit read-only server OutputCache policies are now enabled.
See [Caching/README.md](Caching/README.md) for endpoint lifetimes, invalidation,
privacy isolation and the measured cache-bypass versus cache-hit comparison.
The statements below describe the earlier API optimization baseline.

## Changes

- Keep the existing endpoint contracts and separate business/EF queries. Fifteen
  forwarding handlers return their operation's Task without an extra async state
  machine. Login/register retain postprocessing.
- Search serializes each item with generated JSON metadata directly into the
  response PipeWriter. Every complete NDJSON row is flushed; backpressure,
  cancellation and upstream disposal are preserved. The host owns the pipe.
- JWT uses spans, static HMAC operations, a precomputed header and pooled large
  buffers. Signing keys, claims, lifetimes, validation errors and wire format
  remain compatible. Rented buffers are cleared on return.
- Client-context lookup uses one cache read and propagates RequestAborted on a
  cache miss. The existing public client-ID cache contract is unchanged.
- SignalR uses per-entity synchronization, removes empty groups and releases
  connection state before transport awaits. Identity data lives with its
  connection instead of accumulating permanently by user. Multiple connections
  belonging to the same user can register/unregister independently.
- Request diagnostics use LoggerMessage source generation and timestamp values.
  Disabled information logging forwards directly without a timing state machine.
  Unmatched routes no longer dereference a missing endpoint.
- Removed unused output/distributed/memory-cache registrations. No endpoint used
  those cache policies, and no personalized-response caching was introduced.
- Removed the legacy SignalR 1.2.10 package; the .NET 10 ASP.NET shared framework
  supplies the server. OpenAPI is registered only in Development, matching its
  existing route availability. Detailed hub errors are Development-only.
- Enabled explicit request delegate generation, AOT analyzers and reflection-free
  default JSON serialization. Added explicit SignalR scalar/request metadata.
- Updated OpenAPI to 10.0.14 and pinned the patched transitive Pkcs dependency to
  10.0.14 for this API host. EF remains 10.0.5.
- GlobalUsings was reviewed and retained: its domain imports are used; Web SDK
  implicit usings already supply common framework types.

## Verification

Visual Studio MSBuild Debug/x64 build passed. The diagnostic runner at
`Tools/ApiDiagnostics` passed with the JSON reflection fallback disabled:

- Authentication contracts, malformed tokens, claims, cache hits/misses and
  request cancellation. An independent old/new comparison passed 3,111 cases.
- Streaming JSON escaping, incremental delivery, backpressure, cancellation,
  completion, producer failures and resource ownership.
- SignalR lifecycle/transport failure cases and 640 concurrent join/leave cycles.
- Actual HTTPS API reads for clients, music, media metadata, waveform, lyrics,
  contributors, reactions, playlists, features, activity, stories and details.
- Exact HTTP/2 and HTTP/3 negotiation, 401/400/404 behavior, gzip request/response
  handling, OpenAPI, real NDJSON delivery/cancellation/empty results.
- Actual SignalR WebSockets: handshake, groups, presence JSON, play/pause/seek,
  multiple same-user connections and listener removal.

The chosen music has no lyric-line fixture, so that HTTP detail case was skipped.
Write operations were not executed; no records, schema, JWT settings or existing
Visual Studio process were changed by the tests.

The isolated synchronous JWT benchmark measured validation allocation of
3,360 → 488 bytes/token and generation of 3,992 → 2,128 bytes/token. These are
component allocations, not total request memory. Timing varied with JIT warmup;
no universal latency improvement or sub-millisecond API guarantee is claimed.

Local logs and isolated native component project:
`D:/MizonTaskDiagnostics/ApiOptimization/`

- `api-final-vs-build.log`
- `diagnostics-final-run.log`
- `JwtProbe/differential.log`, `before.log`, `after.log`
- `NativeCore/publish.log`, `run.log`, `run.ps1`

## NativeAOT boundary and remaining environment warnings

The actual native component executable passed JWT, NDJSON and diagnostic tests,
asserting that dynamic code and JSON reflection were disabled. This does **not**
certify the entire EF-backed API for NativeAOT. The earlier Logic query
precompilation audit rejected dynamic/captured-context queries. `PublishAot`
therefore remains false for the application; `IsAotCompatible` enables analysis.
No blanket trimming/AOT warning suppression or JSON reflection fallback was added.

The installed preview SDK/runtime still uses 10.0.9. Upgrade the installed SDK and
ASP.NET runtime to the current supported .NET 10 security patch before deployment;
updating NuGet packages does not patch the installed runtime. Project-reference
restore can still report the lower-level Database/Logic Pkcs version and the
shared Server project's ILCompiler version; the API's resolved Pkcs is 10.0.14.
Warnings remain visible. Full trimmed/native database deployment requires its
own complete query validation.

Official references:

- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries
- https://github.com/microsoft/OpenAPI.NET/security/advisories/GHSA-v5pm-xwqc-g5wc
- https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx
- https://github.com/dotnet/runtime/security/advisories/GHSA-p3jp-9v83-76w9

## Re-running diagnostics

Start a separate development API on an unused loopback port. Defaults remain
port 5128 and normal asset initialization; these overrides are for diagnostics:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project Mizon.Server.API --no-launch-profile -- --Server:Port=55128 --AssetInitialization:Enabled=false
```

In another terminal, from the solution directory:

```powershell
dotnet run --project Tools/ApiDiagnostics -p:Platform=x64 -- --http https://localhost:55128 --api-path C:/Users/Ali/source/repos/Mizon.Server.API/Mizon.Server.API
```

Omit both HTTP options to run only component checks without a server or database.
HTTP checks require existing `alish`, an active session and music fixtures. They
read the local JWT settings privately and are restricted to HTTPS loopback.
No credentials are printed. Stop only the diagnostic server when finished.
