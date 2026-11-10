# Server response caching

Uses the ASP.NET Core 10 OutputCache middleware and its bounded memory store.
This is server-controlled output caching, not ResponseCaching/browser caching.
Authentication, authorization and client-context resolution run on every request
before the cache. No new package, schema migration or database writes are needed.

## Endpoint policies

Only the exact method/route pairs in ResponseCacheCatalog opt in. New endpoints
are uncached until explicitly reviewed. Five read-only operations use POST; their
body is hashed and rewound before normal request binding.

| Route (all names relative to their group) | Method | Maximum lifetime | Scope |
| --- | --- | --- | --- |
| Clients/Me | GET | 15 seconds | User + session |
| Clients/ClientProfile | GET | 30 seconds | User + session |
| Clients/ClientMoodTypes | GET | 30 minutes | Shared, authenticated |
| Clients/FollowingActivities | POST | 5 seconds | User + session |
| Musics/Music | GET | 10 seconds | User + session |
| Musics/MusicContributors | GET | 5 minutes | Shared, authenticated |
| Musics/MusicFile | GET | 60 seconds | Shared, authenticated |
| Musics/MusicStemMix | GET | 5 minutes | Shared, authenticated |
| Musics/MusicFileWaveform | GET | 1 hour | Shared, authenticated |
| Musics/MusicLyric, MusicLyricLine | GET | 1 hour | Shared, authenticated |
| Musics/MusicReactions | GET | 5 seconds | User + session |
| Musics/MusicReactionTypes | GET | 1 hour | Shared, authenticated |
| Musics/MusicGenreTypes | GET | 5 minutes | Shared, authenticated |
| Musics/MusicAlbumMusics, MusicContributorMusics | GET | 30 seconds | User + session |
| Musics/MusicComments | GET | 10 seconds | User + session |
| Musics/MusicPlaylists, MusicLibrary, MusicPlaylistDetails | POST | 10 seconds | User + session |
| Musics/MusicFeatures | POST | 10 seconds | User + session |
| Stories/Stories, MusicStories | GET | 5 seconds | User + session |
| Stories/StoryDetails | GET | 3 seconds | User + session |
| Medias/Media | GET | 60 seconds | Shared, authenticated |

The 14 write operations (login/register/logout/mood; music like/view/play/reaction/
comment/create-playlist/membership; story create/view/like) are never cached.
Search/Global remains NDJSON streaming and uncached. HTTP errors, unsuccessful
HTTP-200 business envelopes, cancelled responses, cookies, unknown endpoints,
ranged requests and missing identity metadata are not stored. Media/file metadata
with HTTP(S) query-string URLs bypass caching because an independent signed-link
expiration is not known. Request Cache-Control:no-store bypasses the cache;
no-cache refreshes it. Responses use private, no-store to prevent downstream
browsers/proxies from retaining personal data.

Every query, POST body and relevant representation/device header varies the key.
Query strings are hashed (including query tokens); bearer tokens are not placed
in cache keys. User/session-scoped entries do not mix liked/viewed state, owned
playlists or privacy-sensitive lists. Only audited non-personal metadata is shared.

## Invalidation and expiration

The API executor invalidates dependent domains after write operations, including
when a write might have committed before a later exception/cancellation. Fixed
domain generations are included in keys, so an old in-flight read cannot restore
the generation used by requests beginning after a write. Tags remove obsolete
entries; unrelated waveform/lyric/catalog entries survive likes and plays.
New login/registration identities use different keys and do not globally flush
existing users' caches. Dependencies are deliberately conservative domain-wide
invalidations, not a guarantee of a high hit rate during heavy write traffic.

Future follow/privacy operations must invalidate Relationships, and future
catalog/import/content updates must invalidate their respective domains. Changes
made directly in the database or by another process are seen at TTL expiration;
there is no database notification subscription. This is a single-process cache.
Multiple API instances require a shared store AND coordinated generations/
invalidation, not merely replacing the memory store with Redis.

Story expiry and carousel scheduling use internal [JsonIgnore] CacheExpiresAt
metadata. Lifetime is capped before serialization AND again when the framework
stores the completed response, so a slow body write cannot extend content expiry.
Features also check the next eligible scheduled start, including an empty list.
No public DTO field or request/response JSON contract changes.

## Limits and ownership

appsettings.json ResponseCache settings:

- Enabled: true; false bypasses response caching.
- SizeLimitBytes: 67,108,864 (64 MiB of cache storage; not preallocated RAM).
- MaximumBodySizeBytes: 2,097,152 (2 MiB per cached response).
- MaximumRequestBodyBytes: 65,536 (64 KiB for read-only POST hashing).

Larger or unknown-length request bodies still execute normally without caching.
Large responses are returned normally without being stored. Pooled request buffers
are cleared/returned. The request owns buffering streams; DI disposes cache/store
services. OutputCache supplies concurrent-miss locking. The limits do not bound
the whole process working set or all concurrent request allocations.

## Verification and measurements — 2026-11-09

Release/x64 build and ApiDiagnostics passed with JSON reflection disabled.
Real loopback Kestrel tests cover identity/session/query/device/body isolation,
shared catalogs, auth before hits, request body preservation, no-store/no-cache,
large bodies/responses, business errors, invalidations, in-flight races, expiry
(including a slow response), cancellation recovery and 16 simultaneous misses.
Real HTTPS read checks, HTTP/2+HTTP/3, streaming and SignalR also passed.
No actual write endpoints or database mutations were used for tests.

Controlled benchmark: same otherwise-idle API process, current local SQL Server
fixtures and identical requests; Cache-Control:no-store bypass versus normal
cache hits. Three rounds, 50 calls per route/mode, alternating order; 2,100
measured requests. TLS/EF/JIT were warmed before measuring; each cached batch's
first miss was measured separately. All response hashes matched and private
expiry metadata was absent from JSON. Times include HTTP/2 HTTPS transport and
reading the complete response, but exclude the client's JSON/hash verification.

| Endpoint | Mean bypass / hit (ms) | p95 bypass / hit (ms) | SQL commands per 150 bypass / hit requests |
| --- | --- | --- | --- |
| Me | 5.532 / 0.409 | 8.333 / 0.754 | 900 / 0 |
| Music | 6.181 / 0.321 | 7.843 / 0.492 | 1,350 / 0 |
| Library | 4.160 / 0.412 | 5.922 / 0.731 | 750 / 0 |
| Features | 7.371 / 0.348 | 11.064 / 0.661 | 1,500 / 0 |
| Following | 12.186 / 0.303 | 17.081 / 0.494 | 2,400 / 0 |
| Stories | 3.301 / 0.296 | 6.712 / 0.469 | 300 / 0 |
| Waveform | 16.684 / 2.827 | 24.323 / 7.912 | 450 / 0 |

The 1,050 bypass requests executed 7,650 EF commands; the 1,050 cache hits
executed zero. API-process CPU accumulated 10,515.625 ms versus 1,203.125 ms
(about 88.6% lower in this test). This excludes SQL Server and benchmark-client
CPU; Windows CPU counters have coarse granularity. EF command logging was enabled
equally in both modes to count SQL, so the bypass CPU includes that logging work.

Process working set ranged about 165-217 MiB in BOTH modes, and private bytes
about 69-120 MiB. These alternating snapshots do not isolate cache memory from
JIT/GC/connection buffers, and do not establish any RAM reduction. Cache trades
storage for saved queries/serialization. Its configured 64 MiB cap is not its
measured retained size or the application's total RAM limit.

First cache-miss latency still includes database work and cache population.
For example, Following misses were 19.77/11.86/13.97 ms across rounds, versus
0.303 ms average for hits. Me first misses varied 3.91-32.56 ms. These local,
small-data, repeated-key measurements are not a production SLA or expected
hit rate for arbitrary searches/users. Expiry and writes produce new misses.

Evidence is local (contains real response data; do not commit):
D:/MizonTaskDiagnostics/ResponseCache-20261109/

- checks.log, http-checks.log, build.log and server-http.log.
- Benchmark/cache-benchmark.json: raw samples, first misses, CPU and memory snapshots.
- Benchmark/summary.json, benchmark.log and server-benchmark.log: aggregation/SQL evidence.

Reusable benchmark command after starting a dedicated API on an unused port:

```powershell
dotnet <ApiDiagnostics.dll> --cache-benchmark https://localhost:55128 <API-project-directory> <output-directory> <server-process-ID> <optional-server-stdout-log>
```

The cache implementation does not add JSON reflection or runtime-generated types.
The existing EF query-precompilation limits still prevent certification of the
entire application as NativeAOT; a managed Release test is not a native publish test.

References:
- https://learn.microsoft.com/en-us/aspnet/core/performance/caching/output?view=aspnetcore-10.0
- https://github.com/dotnet/aspnetcore/tree/v10.0.0/src/Middleware/OutputCaching/src
