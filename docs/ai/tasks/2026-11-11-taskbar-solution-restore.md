# Restore the Taskbar companion through the active Visual Studio solution

- Status: complete
- Owner: Codex root; independent read-only Taskbar restore review
- Date: 2026-11-11
- Repository/branch: Mizon.Server.API, `codex/fix-taskbar-solution-restore`
- Base commit: `119746a6d4891f49f08c7080e6cad5e2d1c4df8f`
- Owned files: Mizon.Server.API.slnx, AI project/handoff/task notes
- Related repositories: Windows source unchanged at `799d8a4163ff0a392fe0f03483812c2f0ca5e83e`

## Request and evidence

The user's 07:47 Rebuild log contains one blocking error: NETSDK1005 for
`Taskbar/Mizon.Windows.Taskbar/obj/project.assets.json`. Its target was still
`net10.0-windows10.0.22621.0`, while the project now targets .NET 11. The API
successfully built, so this is separate from the preceding StaticWebAssets fixes.

The open Visual Studio process uses `Mizon.Server.API.slnx`, which omitted Taskbar.
The legacy `.sln` already contains it. The main WinUI project's existing
non-assembly ProjectReference and packaging target build the companion, but do
not restore it through that Visual Studio graph. Other companions are independent
solution entries.

## Implementation

Add the existing Taskbar csproj to `.slnx`, mapped to x64, matching the established
companion configuration. Keep the existing ProjectReference and packaging flow.
No duplicate project, forced per-build restore, target-framework rollback,
package change, cache deletion or installed SDK modification.

## Validation

Using Visual Studio 18 Insiders full MSBuild and SDK 11.0.100:

| Check | Result |
| --- | --- |
| Solution Restore, Debug/x64 | Passed; Taskbar restored and both .NET 11 / win-x64 targets exist in its assets |
| Solution Rebuild, Debug/x64 | Passed in 68.61 seconds; 0 errors, 274 existing warnings |
| Main WinUI build output | Contains Taskbar/Payload executable, DLL, PRI and runtime/dependency metadata |
| Independent source/configuration review | Confirmed missing `.slnx` entry and existing legacy `.sln` x64 mappings |

Commands were `/t:Restore /m:2 /nr:false /p:Configuration=Debug /p:Platform=x64`
and `/t:Rebuild` with the same configuration. Rebuild also set
`GenerateAppxPackageOnBuild=false`, `AppxPackageSigningEnabled=false` and an external
`CustomAfterMicrosoftCommonTargets` override of `ReleaseMizonExplorerCommandHost`
to avoid terminating the user's Explorer command host during validation.
That diagnostic override is not a source change.

Evidence: `D:/MizonTaskDiagnostics/TaskbarRestore/solution-restore.log` and
`solution-rebuild.log`. Logs/generated outputs remain outside Git.

No deployment, Start/debugger launch, UI automation, MCP behavior change or native
publish was tested. Build configuration changes require no new MCP contract.
Existing nullability, experimental API and EF/AOT warnings remain visible.

## Handoff and publication

Integrate the scoped commit into `main`, push and verify the remote SHA. If Visual
Studio requests a solution reload, accept it to display the added project.
Keep `.slnx` and `.sln` companion membership aligned in future additions.
