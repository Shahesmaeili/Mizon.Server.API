# Recover empty SDK static web assets caches before consumption

- Status: complete
- Owner: Codex root; independent read-only cache/source review
- Date: 2026-11-11
- Repository/branch: Mizon.Server.API, `codex/fix-empty-static-assets-cache`
- Base: `5aa02eac18096ae18c185603e48034f81c70715a`
- Owned files: API csproj, Build/Sdk11StaticWebAssets.targets, AI project/handoff notes
- Related repositories: no source changes

## Request and evidence

The user reported `DefineStaticWebAssets` failing with "The input does not contain
any JSON tokens" after the build dependency fix. Current original-checkout caches
were valid when inspected; new corruption there was not observed. In an isolated
worktree, a zero-byte `rpswa.dswa.cache.json` reproduced the exact reported error.

SDK 11.0.100's cache writer truncates the file before serialization; its reader
does not recover empty JSON on the next build. A failed/interrupted write can
therefore leave a build stuck. The preceding task's one-time cache deletion did
not provide ongoing recovery.

[SDK cache implementation](https://raw.githubusercontent.com/dotnet/sdk/v11.0.100/src/StaticWebAssetsSdk/Tasks/DefineStaticWebAssets.Cache.cs)

## Implementation

The version-scoped SDK extension imports a recovery target for VS and CLI.
Existing full-framework task-host overrides remain separately conditional.
Before any of the four cache-consuming targets runs, configuration is resolved,
then these exact names under the active `_StaticWebAssetsManifestBase` are checked:

- rpswa.dswa.cache.json
- rjimswa.dswa.cache.json
- rjsmrazor.dswa.cache.json
- rjsmcshtml.dswa.cache.json
- rbcswa.dswa.cache.json

Only empty/whitespace-only files are deleted for SDK regeneration. Missing files,
valid caches, nonempty malformed JSON, unrelated JSON and other configurations
are preserved. Recovery also handles design-time consumers and runs at most once
per project build. No recursive cleanup, global cache invalidation, package
addition, installed SDK change, Hot Reload disable or database operation.

## Validation

| Check | Observed result |
| --- | --- |
| Old code + synthetic zero-byte project cache | Exact JsonException reproduced |
| New code + same file + actual ResolveProjectStaticWebAssets | Passed; SDK regenerated valid JSON |
| Scoped fixtures under Full MSBuild, CLI and DesignTimeBuild | All passed: zero/whitespace removed, valid hash/time unchanged, nonempty malformed/unrelated/other-configuration files preserved |
| All five caches emptied, actual VS Debug/x64 Build | Passed; 0 errors, 2 existing obsolete API warnings; regenerated files parse as JSON |
| Whitespace project cache, actual CLI Debug/x64 Build | Passed; 0 warnings/errors |
| Zero-byte project cache, actual SDK target with DesignTimeBuild=true | Passed; regenerated valid JSON |
| Recovery against valid real caches | All hashes and last-write times preserved |
| Independent review | Target ordering correction incorporated; no remaining blocking findings |

Evidence is local under `D:/MizonTaskDiagnostics/AsyncInterfaces` (`empty-cache-*`,
`cache-recovery-*`, `recovery-*` logs), not committed. Synthetic corruption was
confined to the diagnostic worktree's generated outputs, never user/source data.

This build-only change requires no new MCP contract. No UI automation, real
account operation, app deployment, VS/Explorer restart or native publication was
performed. Nonempty corrupt JSON is intentionally not silently discarded.

## Handoff and publication

Integrated through a scoped commit into `main`; verify the remote SHA when handing
off. Reassess both workarounds on SDK updates. Test using actual VS MSBuild as well
as CLI, since their task loaders differ. Existing runtime/EF AOT limits are unchanged.
