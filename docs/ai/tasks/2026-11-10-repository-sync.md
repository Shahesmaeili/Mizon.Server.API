# 2026-11-10 coordinated repository synchronization

- Status: reviewed source and documentation complete; delivery is recorded by remote branch history.
- Owner: Codex, coordinated by the user.
- Repository: Shahesmaeili/Mizon.Server.API, branch main.
- Base: 5fbdbb3b0194fb038ad1af4d1b547fa6b628f84f.
- Request: publish all accumulated project code and establish shared instructions for concurrent AI development.

## Coordinated source set

| Repository | Commit | Scope |
| --- | --- | --- |
| Mizon.Database | c31909d2baf3b741aed604d0e4ab31f42e1dcdb5 | Newly tracked existing EF model/migrations/regression tests |
| Mizon.API.DotNet | b547c08aa7c968ca3fd2ac9a6f5df728d30e8154 | Typed SDK/contracts, streaming/media/socket improvements |
| Mizon.Windows | f92e7446b5455a22f5c58c1d96d4e9e3387750a8 | Accumulated WinUI, playback, shell, MCP and navigation work |
| Mizon.Server.API | This commit (git log for this file) | Operations/endpoints, caching/streaming/diagnostics and mood assets |

The .NET SDK remote now uses its canonical GitHub name Mizon.API.DotNet. The local
folder and namespaces remain Mizon.API. The database directory previously had no
Git repository; its outer directory now contains its own Git root and public remote.
ExplorerCommands, Palettes, MCP and taskbar projects remain in the Windows repository.

## Shared workflow

Each repository has AGENTS.md, CLAUDE.md, Copilot instructions and docs/ai guides.
COLLABORATION.md has identical contents/revision across all four repositories.
It records the user's standing scoped commit/push requirement, worktree/branch
ownership, explicit cross-repository dependencies, MCP consent, validation and
handoff rules. Markdown does not provide live session synchronization or locking.

## Validation

- Database Release/x64 regression runner: 40 checks passed, no SQL connections.
- Server API Release/x64 build: passed (0 errors); existing warnings remain.
- Default isolated API diagnostics: passed; no live HTTP/benchmark options used.
- SDK Release diagnostics: passed transport, operations, media and socket checks.
- Windows latest-source Debug/x64 build passed before this documentation-only step.
- Windows route/App Actions: 600 checks; pin lifecycle: 25 checks, both also NativeAOT x64.
- MCP protocol: 31 groups; production access/adapter integration: 110 checks.
- No deployment, live migration/data mutation, real account submission, native pin
  confirmation or Windows UI automation occurred during publication.

Historical benchmarks are not new measurements. Existing NuGet advisories and full
EF/WinUI application NativeAOT limitations remain documented, not silently suppressed.

## Files intentionally excluded

Generated bin/obj/AppPackages, debug MSIX packages, IDE state, local credentials and
diagnostic artifacts are excluded. Existing intentional mood/icon/model assets and
their provenance/license files remain source inputs. The already user-authorized
development JWT appsettings value is retained; no secret values appear in these docs.

## Next contributor

Fetch the needed repositories and read their task notes and PROJECT/HANDOFF files.
Use an independent worktree and claim file ownership before editing. Commit/push
only the next task's reviewed changes; this initial broad snapshot is not permission
to absorb another agent's unfinished work.
