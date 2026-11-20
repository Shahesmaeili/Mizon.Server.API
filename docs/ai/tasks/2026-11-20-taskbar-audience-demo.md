# Development demo for taskbar audience counts

- Owner: Codex
- Base: `43d9f8c`
- Owns: the optional SignalR audience demo and local API launch profiles.

Visual Studio launch profiles enable `TaskbarAudience__UseDemoCounts` when the
API runs in Development. It sends both counts through the existing authenticated
hub protocol every 2.4–6 seconds. Each update changes the global total (18–65)
and the caller’s sample follower count (1–12). Samples live in memory and do not
access or modify the database. The hosted service stops with the API. Without
the opt-in setting, and in environments other than Development, the real
account and follower results are used.

The existing API diagnostics exercised the real-count and demo paths over
authenticated loopback WebSockets. The demo used the same generated JSON and
revisioned event as the normal client. Anonymous connections were rejected.
