# Server project map

## Layers and dependencies

- Mizon.Server.API: ASP.NET Core host, endpoint mapping, authentication,
  client-context middleware, SignalR, generated JSON and response caching.
- Mizon.Server.Logic: request/response contracts and Operations; runtime database
  mutations and business decisions belong here, not in endpoints or SQL scripts.
- Mizon.Server: shared executor/envelope/operation infrastructure.
- Mizon.Database is a sibling repository containing EF models and migrations.
- Tools: audio analysis/import/simulation utilities and isolated diagnostics.
  Some utilities intentionally write data; inspect their entrypoint before running.

The API targets .NET 11. It is not a production NativeAOT application merely
because analyzers and source-generated JSON are enabled. See
[Mizon.Server.API/PERFORMANCE.md](../../Mizon.Server.API/PERFORMANCE.md) and
[Logic performance](../../Mizon.Server.Logic/PERFORMANCE.md).

## Current feature surface

Current operations include account login/registration/profile/mood, music detail,
play/view/like/reactions/comments/lyrics/waveform/stem metadata, playlists/library,
featured media, following activities, story widgets/views/likes and global search.
Use actual endpoint/operation registrations as the source of truth. Future film
and series features are not implemented merely because a UI placeholder exists.

Search uses NDJSON streaming; maintain IAsyncEnumerable behavior, producer
cancellation, incremental delivery and bounded work across layers. Shared client
DTOs/operations live in Mizon.API.DotNet; changing server DTOs alone is incomplete.

OutputCache has an explicit opt-in catalog and per-user/session isolation.
Writes, streaming search and unsuccessful responses must not become cached.
Read [cache policies and invalidation](../../Mizon.Server.API/Caching/README.md)
before changing a read/write operation. New writes may require invalidation even
when a response fails after the write committed.

Mood assets under App_Data/MoodEmojis are intentional source assets with provenance
and license files. They are not a database export. Do not replace them with private
download/session caches.

## Build and safe verification

From this repository root:

~~~powershell
dotnet build Mizon.Server.API/Mizon.Server.API.csproj -c Release -p:Platform=x64
dotnet run --project Tools/ApiDiagnostics/ApiDiagnostics.csproj -c Release -p:Platform=x64
~~~

The default diagnostic run exercises isolated auth, NDJSON, request diagnostics,
SignalR and response-cache scenarios. Its --http and --cache-benchmark options are
separate opt-in live checks; do not append them to a routine publication check.
The SQL-facing LogicPerformance runner is read-only but needs an appropriate local
database and fixtures; see its own README before use.

The solution currently includes absolute paths to Windows/SDK projects from the
original workstation. Building the API project directly does not require loading
the entire WinUI solution. On another machine resolve sibling roots first.

For SDK 11.0.100, the API imports `Build/Sdk11StaticWebAssets.targets`. It recovers
empty/whitespace-only copies of the SDK's five known asset caches in the active
intermediate directory before their consumers run. Valid caches are preserved.
Only full-framework VS MSBuild additionally imports `VisualStudioStaticWebAssets.targets`
to run JSON-dependent asset tasks in the .NET TaskHost; CLI retains normal task
selection. Read the [build compatibility note](tasks/2026-11-11-vs-static-web-assets.md)
and [empty-cache follow-up](tasks/2026-11-11-empty-static-assets-cache.md) before
changing these SDK-version-scoped workarounds.

Do not execute database update, seed/import tools, simulator writes or production
deployment as a side effect of building or documenting this repository.

## Cross-platform and MCP coordination

The Windows app and .NET SDK are separate repositories. Coordinate public IDs,
nullable fields, request shapes, generated JSON contexts and error codes.
The current MCP boundary lives in Windows, uses the same services/operations and
requires user consent; do not add a parallel unrestricted backend just for AI.

Read the sibling Windows Mcp/DEVELOPMENT.md. Its implemented domains currently
cover authentication, appearance, declared navigation and taskbar pinning, not
all music/profile business operations.

## More documentation

- [Shared collaboration/commit policy](COLLABORATION.md)
- [Current handoff](HANDOFF.md)
- [Task template](TASK_TEMPLATE.md)
- [API performance](../../Mizon.Server.API/PERFORMANCE.md)
- [Logic performance](../../Mizon.Server.Logic/PERFORMANCE.md)
- [Response caching](../../Mizon.Server.API/Caching/README.md)
- [Read-only performance runner](../../Tools/LogicPerformance/README.md)
