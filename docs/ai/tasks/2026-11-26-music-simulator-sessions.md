# 2026-11-26 — Music simulator authentication

- Owner: Codex Windows/server.
- Base: `cd3d54a23bd5d062aa6dfd53c9910d5cd037d598` and the device-session database migrations.
- Scope: the MusicPlaybackPresenceSimulator tool and its handoff.

The old simulator signed JWTs with randomly generated session IDs on each attempt.
The API requires a persisted active session, so the simulator repeatedly received
HTTP 401 from SignalR negotiation.

The simulator targets a local HTTPS API and a local SQL Server. It selects only the
story fixtures created by SeedStoryTestData. A matching seed story is required, and
any account with an active password, an active email, or a non-simulator session
is excluded. It creates or reuses a reserved device session for each selected fixture
and uses that same session ID whenever it signs a connection token.

A normal exit marks the selected simulator sessions signed out. If Visual Studio
or Windows forcibly stops the tool, the next start reuses its active session rows.
A rejected authentication or other terminal client error stops retries. Transient
startup failures back off, and reconnecting restores music group membership.
The server's session enforcement is unchanged.

The Release x64 simulator build passed. A 12-second sample of the old process
recorded 30 HTTP 401 responses. With only the simulator replaced, 10 fixtures
connected and published playback on the existing local API. SQL showed 10 active
simulator sessions on 10 fixture accounts. A 15-second sample of the replacement
had no HTTP 401 responses from the simulator. The Windows app and Kestrel kept
their original process IDs. Graceful shutdown and reconnect were not forced
against the live development session during this check.
