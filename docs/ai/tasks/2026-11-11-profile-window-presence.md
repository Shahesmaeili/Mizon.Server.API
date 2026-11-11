# 2026-11-11 — Authenticated main-window presence

- Owner: Codex (`music_home_contracts`); integration owner: root task.
- Repository/branch: Mizon.Server.API, `codex/profile-presence`.
- Base: `f939cb1ce2a964eef62a62a094c81ce95de42c20`.
- Scope: hub/DTO/generated JSON and isolated API diagnostics. No database changes.
- Related: matching Mizon.API.DotNet SDK and Windows header/taskbar integration.

## Behavior and contract

`MizonHub.SetWindowPresence(bool isActive, long revision)` acknowledges only the
authenticated caller. `ClientWindowPresenceResponse` contains `ClientPublicId`,
`ConnectionId`, `IsOnline` and `Revision`. The same typed payload is emitted as
`WindowPresenceChanged` to `Clients.Caller`, never other users/devices/groups.
Identity is read from the authenticated `sub` claim; no identity parameter exists.
Zero/missing subjects and nonpositive revisions are rejected.

Each connection owns one immutable response in `Context.Items`. Older or repeated
revisions return its latest response without publishing another event. Disconnect
removes that entry. New connections have independent revisions and no stored online
state. This is main-window focus presence, not cross-device online aggregation.
No new global dictionary, polling, table, SQL, history, or API route is introduced.
The SDK validates identity/connection/revision and restores current focus after
reconnect; the taskbar consumes the main app's state rather than opening a socket.

## Verification

`dotnet run --project Tools/ApiDiagnostics/ApiDiagnostics.csproj -c Release -p:Platform=x64`
passed with JSON reflection disabled:

- Real loopback Kestrel + production MizonHub + production generated JSON context:
  anonymous denial, authenticated DTO/event/completion, caller-only isolation with
  two same-account connections, invalid identity/revision, stale revision and reconnect.
- Direct production hub: canceled connection, 100 concurrent revision updates,
  independent device state and complete connection-owned cleanup.
- Presence fixtures deliberately supply no DbContext, proving these paths use no EF.
- Existing auth, NDJSON/backpressure, diagnostics, music-listener/group lifecycle
  (640 concurrent cycles), and OutputCache checks remain passing.

`ApiDiagnostics` now has friend access to the production JSON context. Its old
linked NDJSON source was removed so those checks also exercise the actual production
type rather than a duplicate. No source-generation fallback was enabled.

Existing EF NativeAOT/experimental and obsolete SQL API warnings remain unrelated
to this transport-only change. A full server NativeAOT publish is not claimed.
No live accounts, real database writes, UI automation or deployment was performed.

## Integration and publication

Coordinate the matching `codex/profile-presence` SDK and Windows commits; this
server alone does not update profile visuals. Root owns combined verification and
normal GitHub publication after integrating task-owned commits. The commit for this
note is identified by `git log` for this path.
