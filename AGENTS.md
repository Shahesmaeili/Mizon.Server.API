# Mizon development requirements

- Preserve the existing naming/layering; business behavior belongs in `Mizon.Server.Logic/Operations`, API routes in `Mizon.Server.API`, and EF/code-first structures in `Mizon.Database`.
- Standing user requirement: new or changed user-facing features/pages also need scoped MCP read-state/actions and meaningful MCP integration checks, even when this repository supplies only their backend changes.
- Visual-only styling changes reuse existing MCP contracts/checks; do not add artificial tools or tests mirroring colors/layout. New behavior or observable state requires meaningful MCP coverage.
- Coordinate the shared API/SDK and Windows MCP adapter; reuse the same business operations. Do not add a parallel public backend or arbitrary SQL/invocation tool for MCP.
- Follow the sibling Windows repository's `Mcp/DEVELOPMENT.md` ([GitHub](https://github.com/Shahesmaeili/Mizon.Windows/blob/main/Mcp/DEVELOPMENT.md)). Current implemented MCP coverage is **authentication pages, appearance, declared navigation and taskbar pinning**; broader domain data/actions must be explicitly implemented, not assumed.
- Preserve typed self-describing schemas, source-generated JSON/NativeAOT compatibility, cancellation, sanitized errors and cleanup. Access is granted by the user inside Mizon; tools/tests cannot approve themselves.
- New feature tools require a separate in-app permission group; old authentication read/control grants must not authorize music/settings/other domains. Tool discovery can be public while protected data/actions remain gated.
- Include meaningful MCP permission, failure/cancellation and lifecycle checks in the feature's completion criteria. Use synthetic data/test doubles for writes unless the task already authorizes the real change; do not submit real accounts/forms just for testing.
- Headless MCP tests do not replace visual, animation or UI performance checks. Do not run Windows UI automation unless the user requests it; report any unverified cases.

## Shared collaboration and publication

- Read [COLLABORATION.md](docs/ai/COLLABORATION.md), [PROJECT.md](docs/ai/PROJECT.md) and [HANDOFF.md](docs/ai/HANDOFF.md) before editing. The shared guide is authoritative here and copied identically into the related repositories.
- Standing user instruction: after each completed task, commit the reviewed task-owned changes with a precise message and push them to GitHub without asking again for routine authorization. Verify and report the remote commit; never claim a failed push succeeded.
- Concurrent Codex/Claude/other agents use separate worktrees/branches and explicit file ownership. Do not stage another agent's unfinished work, reset a shared checkout or force-push shared history. Record task handoffs using the shared template.
