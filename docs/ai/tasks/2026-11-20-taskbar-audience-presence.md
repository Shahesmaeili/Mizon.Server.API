# SignalR aggregate audience presence

The authenticated hub counts unique connected accounts. Extra connections for
the same account do not add to the count. The first connection and final
disconnect publish a revisioned aggregate. Counts are in memory on the current
server process, so a multi-server deployment needs a shared presence registry.

The caller can request its online follower count. The shared Logic operation
streams distinct accepted active follower IDs from EF and intersects them with
the current online accounts. No account list is sent. No schema changes or
database writes are required.

The isolated production SignalR checks used real WebSockets, generated JSON,
two connections for one identity, a second identity, and disconnect cleanup.
The Windows and .NET SDK tasks consume the same contract.
