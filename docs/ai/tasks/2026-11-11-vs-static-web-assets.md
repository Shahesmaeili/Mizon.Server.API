# Fix Visual Studio SDK 11 StaticWebAssets dependency loading

- Status: complete
- Owner: Codex root; read-only SDK/Windows dependency reviews by delegated agents
- Date: 2026-11-11
- Repository/branch: Mizon.Server.API, `codex/fix-vs-net11-build`
- Base: `b69d6437882facad688bf9b10d7f1b8841feeb3c`
- Owned files: API project/build targets and this repository's AI handoff/project notes
- Related repositories: no source changes; database dependency tested at `d14260f`

## Request and cause

Fix the missing `Microsoft.Bcl.AsyncInterfaces, Version=10.0.0.10` error during
Build/Start in Visual Studio after the .NET 11 migration.

Reproduced with VS 18.9.1 Insiders' full-framework x64 MSBuild and SDK 11.0.100:
`EnsureHotReloadBrowserToolsKey` failed in `StaticWebAssets.HotReload.targets`.
SDK StaticWebAssets/Razor net472 System.Text.Json requests AsyncInterfaces
10.0.0.10, while the adjacent SDK DLL is 11.0.0.0 and VS's is 10.0.0.8.
The application's restored dependency graph does not require the missing package.

## Implementation

Use the SDK's `CustomAfterStaticWebAssetsSdkTargets` extension hook, scoped to
`MSBuildRuntimeType=Full` and SDK **11.0.100**. Fifteen task registrations run in
the supported .NET TaskHost. Besides key creation, asset manifests, cached asset
definitions and endpoint metadata use JSON and require the same isolation.
Unrelated tasks retain their SDK defaults.

`DefineStaticWebAssets` keeps the SDK's net472 assembly **but executes in the .NET
host**: its public FileInfo-based testing hook triggers a separate VS task-metadata
inspection failure when inspecting the net11 assembly. That failure was reproduced
during verification, and the net472 metadata resolves it without replacing the
task's implementation or targets.

- [MSBuild UsingTask / .NET TaskHost](https://learn.microsoft.com/en-us/visualstudio/msbuild/usingtask-element-msbuild)
- [Related task-property inspection issue #14680](https://github.com/dotnet/msbuild/issues/14680)

No SDK/VS installed files, binding redirects, registry entries, package versions,
business logic, database state, runtime dependencies or MCP permission contracts
changed. Hot Reload is not disabled. No arbitrary DLL copying or package addition
to the application is required. CLI builds do not import these overrides.

## Verification

Diagnostics are local under `D:/MizonTaskDiagnostics/AsyncInterfaces`, excluded
from Git because build logs may contain local paths/configuration or key material.
Only result summaries belong in this note; never publish generated private keys.

| Check | Result |
| --- | --- |
| Original API Debug/x64, full VS MSBuild | Exact missing-assembly error reproduced |
| Fixed API Debug/x64 build | Passed |
| Fixed API Debug/x64 Rebuild | Passed; 0 errors, 5 existing EF/AOT/obsolete API warnings |
| Fixed API incremental Debug/x64 | Passed; Hot Reload public key hash and write time unchanged; private key file present |
| Fixed API Release/x64, full VS MSBuild | Passed; 0 errors, 5 existing warnings |
| Fixed API Debug/x64, dotnet CLI | Passed; 0 warnings/errors on the incremental build |
| CLI property evaluation | Core runtime; workaround import and task assembly properties empty |
| API runtime dependency manifest | No Microsoft.Bcl.AsyncInterfaces runtime dependency introduced |
| Independent read-only review | Import order, version/runtime conditions and task scope reviewed; no blocking findings |
| Original developer checkout, VS Debug/x64 | Passed after the one-time failed-cache recovery below; 0 warnings/errors |

Integration into the original checkout exposed a zero-byte
`obj/x64/Debug/net11.0/rpswa.dswa.cache.json` left by the earlier failed task. The
SDK tried to deserialize it and raised a separate empty-JSON error. Only that
verified zero-byte generated file was removed, then the original checkout built
successfully. Other caches/output folders were not cleared. There is no permanent
cache-disable or cleanup workaround in the project.

Commands used (from the repository root; `$vsMsbuild` is the installed
VS 18 Insiders `MSBuild/Current/Bin/amd64/MSBuild.exe`):

```powershell
& $vsMsbuild Mizon.Server.API/Mizon.Server.API.csproj /restore /t:Build /m:2 /nr:false /p:Configuration=Debug /p:Platform=x64
& $vsMsbuild Mizon.Server.API/Mizon.Server.API.csproj /t:Rebuild /m:2 /nr:false /p:Configuration=Debug /p:Platform=x64
& $vsMsbuild Mizon.Server.API/Mizon.Server.API.csproj /t:Build /m:2 /nr:false /p:Configuration=Debug /p:Platform=x64
& $vsMsbuild Mizon.Server.API/Mizon.Server.API.csproj /restore /t:Build /m:2 /nr:false /p:Configuration=Release /p:Platform=x64
dotnet build Mizon.Server.API/Mizon.Server.API.csproj -c Debug -p:Platform=x64 --no-restore
```

No Windows UI automation, VS restart, Explorer/Search restart, app registration,
database migration/seed, real user operation or MCP self-approval was performed.
This is a build-tool fix, not a change to observable application behavior; no new
MCP tool or artificial behavioral test is warranted. Build checks do not establish
full API NativeAOT publication or end-to-end UI/startup behavior.

## Handoff

Remove/reassess the version-scoped workaround when updating the selected SDK after
verifying VS Debug Rebuild, incremental Debug and Release. A passing `dotnet build`
alone cannot reproduce the full-framework task loader. Existing EF/AOT warnings
are independent of this issue.

## Publication

Scoped commit on `codex/fix-vs-net11-build`, integrated into `main` without rewriting
history. The commit containing this note identifies the delivered change; verify
the remote branch SHA when handing off. No dependent repository needs a new commit.
