# Account window presence across devices

- Owner: Codex server task
- Date: 2026-11-25
- Repository: Mizon.Server.API
- Base: `9832f13d7b1cc17a11f08f200848acec4e1e9092`
- Related client work: Mizon.API.Swift, Mizon.Apple and Android
- Scope: SignalR presence messages, their generated JSON contract and the isolated API diagnostics

## Contract

The authenticated `WatchAccountPresence()` hub method subscribes the caller to
its own account and returns a snapshot containing `clientPublicId`, `isOnline`
and `revision`. `AccountPresenceChanged` carries the same payload when the
first active window appears or the last one becomes inactive or disconnects.
The revision resolves races between snapshot responses and delivered events.
Clients resubscribe after reconnecting.

Each client continues to report its own window through `SetWindowPresence`
and receives the existing caller-scoped acknowledgment. The account status
uses the active windows on the current server process. The general audience
count retains its existing connected-account semantics.

## Validation

The Release API diagnostics passed with .NET 11 on Apple Silicon and JSON
reflection disabled. The production hub was exercised over isolated
authenticated loopback WebSockets. The check covered two devices on one
account, an inactive subscriber, a different authenticated account,
invalid watch targets, reconnect and final disconnect cleanup.
