# Music home fixture artwork refresh

- Owner: Codex `/root/music_home_contracts`; integration owner: root music-home task.
- Branch: `codex/music-home-artwork`; base: `1db956ab0d1247ef334c951a739990d3d0ff2b3d`.
- Scope: this note, `App_Data/MusicHomeArtwork`, the explicit Logic operation,
  and `Tools/SeedStoryTestData` artwork mode. No API/SDK/schema/UI edits.

## Request and behavior

Replace unsuitable home story and featured hero pictures with cohesive music
photography. The inspected local database has twelve active demo stories
(six image/six video) and four active featured slides (three image/one video).
Old data includes selfies, football/car/movie pictures and a roadside video.
Some Media rows are shared by music covers, profiles or other stories.

The fixed fixture catalog uses exact public IDs; it cannot sweep future user
stories. Eighteen dedicated media records supply six attributed photos, six
silent photo-motion MP4s and six matching video posters. No old media is modified
or deleted. Content/preview foreign keys and their update timestamps are the only
existing fields changed. Tags, IDs, titles, authors, types, music links and all
personal avatars/music covers stay intact. Independent artwork edits are rejected.

All writes are in `MusicHomeArtworkInitializeOperation`; the local tool is only
an explicit invoker. No SQL business scripts, auto-seeding or write endpoint.
Missing assets fail before DB access, updates use a transaction and concurrency
predicates, and same-path/same-content reapplication is idempotent.

## Evidence before applying

- Release/x64 tool build and read-only plan: passed; exact twelve stories,
  four featured slides and eighteen files. Plan selects only needed fields in two
  queries; the diagnostic interceptor blocked all non-SELECT commands.
- Pre-cancelled plan: canceled before SQL; missing-file apply: rejected before SQL.
- Every source photo was visually inspected, and previous video frames were
  inspected to establish that they were unrelated to music.
- All eighteen files fully decoded with FFmpeg 9.0.1. All six MP4s are H.264,
  yuv420p, 1280×720, 30 fps, twelve seconds, silent. Source JPGs are 85–211 KiB;
  video loops are 0.65–1.61 MiB. All media totals approximately 7.4 MiB.
- Source licenses and derivative attribution are in `App_Data/MusicHomeArtwork/SOURCES.md`;
  SHA-256 for every media file is in `SHA256SUMS.txt`.
- A read-only preservation baseline for original media/profile/cover/story/feature/
  widget metadata was saved outside Git before mutation. Its hash is
  `B5AA15E2888F2E398845C07003F2E9C7763670CDA6BBAD76159E5D040714BABA`.

## Applied and verified

The root task integrated source commit `b58009c` into the canonical server checkout
before the explicit database apply. Media file URIs now resolve beneath
`C:\Users\Ali\source\repos\Mizon.Server.API\Mizon.Server.API\App_Data\MusicHomeArtwork`.
All canonical asset SHA-256 values were checked against the committed manifest.

- Authorized .NET business operation apply: **12 stories, 4 features, 18 new Media**.
- Fresh relation/URI/hash/size readback: **16/16 fixtures passed**.
- Immediate repeat apply: **0 stories, 0 features, 0 media** (idempotent).
- Preservation readback exactly matches the baseline hash: **1,322 original Media,
  11 profiles, 12 music covers, 12 story metadata, 4 feature metadata and 13 widgets
  unchanged**. Original assets were not removed. The sixteen updated relation pairs
  are the only intended changes to existing records (plus update timestamps).
- The real `MusicFeaturesOperation`, `StoriesOperation` and `MediaOperation` were
  then invoked under a SELECT-only interceptor for the existing local user's
  metadata: **4 featured items and 12 story previews** returned the new artwork;
  **13 unique media public IDs** needed by those list responses resolved to canonical
  local files with correct sizes and chunk metadata. This is business-operation
  verification, not an authenticated HTTP or live WinUI rendering test.
- No missing-file/cancellation test inserted any records.

Sanitized check logs are retained in `D:\MizonTaskDiagnostics\MusicHome\`:
`artwork-decode.log`, `artwork-before-checks.log`, `artwork-apply.log`,
`artwork-idempotency.log`, `artwork-after-checks.log`, `artwork-api-checks.log`.
The full preservation baseline stays local outside Git; no credentials were logged.

Publication is coordinated by the root task. No independent push was performed by
the asset subagent.

No app launch, UI automation or visual app test occurred. This data-only change
reuses existing home MCP read/refresh/story/featured tools; permission-controlled
live app readback remains with the root task. Existing EF AOT limitations and
obsolete ExecuteSqlInterpolatedAsync warnings elsewhere remain unchanged.
