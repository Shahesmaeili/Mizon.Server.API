# Music playback simulator SignalR event binding

- Owner: Codex Windows workstream
- Base: `fd64e0f0032964be74551626a79fcbe94b343a0a`
- Scope: `Tools/MusicPlaybackPresenceSimulator/Program.cs`

The simulator registers handlers for broadcasts it receives from the production
hub before starting each connection. It uses the hub's existing response types
and discards the values. The subscriptions remain active during reconnects and
are disposed when the listener loop ends.

Release and Debug x64 simulator builds passed. A 10-second runtime exception
sample on the old simulator counted 371 argument-binding exceptions. A new
10-second sample after restarting only the simulator counted zero while the
simulator logged playback actions. The API and WinUI process IDs and start times
were preserved. The API returned the expected unauthenticated HTTP 401 on 5128.
