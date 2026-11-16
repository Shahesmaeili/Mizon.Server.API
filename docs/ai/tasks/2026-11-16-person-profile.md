# Artist profile API and follow lifecycle

- Owner: Codex
- Branch: `codex/person-profile`, base `805a468`
- Database dependency: `a377c8a` (AddPersonProfileMediaAndClientLink)
- Owns: Persons routes, logic and diagnostics, generated JSON, error and cache registration

Authenticated `GET /Persons/PersonProfile?PersonPublicId=...` returns artist
identity, three media IDs and active follower/track counts. Personalized follow
state requires a live session. Outgoing count is nullable and derives from an
official linked Client; a private account's count is visible only to its owner.
`GET /Persons/PersonMusics` adds an optional `BeforeMusicPublicId` cursor and
returns up to 24 distinct active tracks per call. `POST /Persons/PersonFollow`
expects `content.personPublicId` and an explicit boolean `content.following`.

The write checks the current session and uses the existing relationship and
history models. Repeating the same desired state is a no-op. First-use reference
data, state and history use EF and one transaction. A bounded retry covers
SQL Server deadlocks, duplicate keys and row version conflicts. Relationship
cache generations are invalidated, and the new endpoints do not cache personal
follow state. Persian and English message strings follow the existing transport.

Verification: API diagnostics with JSON reflection disabled and
`--person-profile-sql` passed. The latter uses a disposable SQL database and
checks new migration Up/Down/Up, 24+3 pagination, private following counts,
4 concurrent follows, idempotent unfollow/refollow, history and signed-out
session rejection. Existing historical migrations start from an older schema,
so the scratch fixture uses the preceding model as its baseline. Live data and
schema were never written to. This is not a full server NativeAOT publish.
