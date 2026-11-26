# 2026-11-25 — Account device session management

- Status: server implementation verified in an isolated SQL Server database and a live loopback Kestrel instance. Client rollout remains separate.
- Owner: Codex Windows/server. Branch: codex/device-sessions.
- Owned scope: client session operations and SQL diagnostics, JWT session enforcement, and session-specific SignalR membership and notifications.
- Related repositories: Mizon.Database, Mizon.API.DotNet and Mizon.Windows. Apple and Android task threads received the same contract.

## Contract

| Method and route | Request content | Response content |
| --- | --- | --- |
| GET /Clients/Sessions | None | sessions, inactivityDays |
| POST /Clients/TerminateSession | sessionPublicId | revokedCount |
| POST /Clients/TerminateOtherSessions | Empty object | revokedCount |
| POST /Clients/UpdateSessionInactivity | inactivityDays | inactivityDays |
| POST /Clients/ConfirmSession | sessionPublicId | confirmed |

The routes use authenticated Mizon API envelopes. A row includes sessionPublicId,
platform, appVersion, osVersion, deviceName, deviceModel, createdAtUtc,
lastActiveAtUtc, isCurrent, isOnline, isUnconfirmed, ipAddress, and location.
Location is currently null. IP is the address seen by the transport at sign-in.
Dates are UTC. New-device recognition expires after 24 hours. The supported
inactivity choices are 0 (never), 7, 30, 90, 180, and 365 days. The current session
must be at least 24 hours old to end another session; error 4603 identifies this case.
The existing Logout route ends the current session.

SessionsChanged, SessionRevoked and NewSessionUnconfirmed are zero-argument hub
messages. A request using a cryptographically valid access token for an inactive
session receives HTTP 401 with Mizon-Session-Revoked: 1. Clients must only clear
the login that used that token. Presence is scoped to live SignalR connections
on the API instance. Last activity is persisted at most about once per minute per
active session. The API does not send a client's session metadata to other accounts.

## Validation and handoff

- SQL diagnostics validate account ownership, recent-session protection, recognition,
  inactivity, live presence, signed JWT enforcement, and WebSocket revocation.
- Deploy the two Mizon.Database migrations before the API. No live database migration,
  app deployment, or restart was performed during development.
- The [development music simulator](2026-11-26-music-simulator-sessions.md)
  provisions scoped sessions for the existing story fixtures before connecting.
- QR sign-in, external website authorizations, Telegram-specific call/secret-chat
  session capabilities, and geo-IP lookup require their own contracts.
