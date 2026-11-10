# Server handoff — 2026-11-10

## Current publication

This task publishes the accumulated local source since the initial GitHub import,
including music discovery/social APIs, playlists/library/detail, following activity,
moods, story visibility, streaming, response caching and diagnostics.
It adds shared AI collaboration instructions; it does not introduce a new API
behavior, migrate a database or deploy a server during publication.

A previously unversioned sibling database source is now published separately as
Shahesmaeili/Mizon.Database. The SDK remote is canonically Mizon.API.DotNet while
its local directory stays Mizon.API. Windows/ExplorerCommands/Palettes/MCP/taskbar
source remains in Mizon.Windows.

## Verification in this publication task

- Database Release/x64 console regression checks: 40 passed, no SQL connections.
- API Release/x64 build: passed, 0 errors; existing warnings remain.
- Default ApiDiagnostics Release/x64: passed with JSON reflection disabled.
  No --http, live benchmark, production request or database mutation was used.
- Build/test logs are local diagnostics, not repository source:
  D:/MizonTaskDiagnostics/publish-*.log.

Older benchmark values and live checks in PERFORMANCE.md are historical evidence;
they are not fresh before/after measurements for this publication.

## Remaining boundaries

- EF query precompilation limitations prevent claiming full API NativeAOT support.
- Existing dependency advisory warnings include transitive Pkcs 9.0.4 and the
  installed ILCompiler 10.0.9. This task does not silently update dependencies.
- OutputCache is process-local; multiple server instances need coordinated storage
  and invalidation. Direct database edits are reflected at TTL expiry.
- No migration application, seed/import run or data export was performed.
- Source appsettings retains the development JWT value the user explicitly approved
  for public publication earlier; no credential value is copied into these docs.

Before the next feature: fetch, read the task notes and inspect actual source.
Do not assume all Windows features have a public backend or complete MCP coverage.
