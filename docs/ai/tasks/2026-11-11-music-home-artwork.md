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

## Integration / pending verification

Integrate the assets into the canonical server checkout **before** applying the
operation. Local StoragePath values must use that durable checkout, not this
diagnostic worktree. Apply/readback/idempotency and preservation evidence will be
recorded after canonical integration. No database writes have occurred at this
initial handoff.

No app launch, UI automation or visual app test occurred. This data-only change
reuses existing home MCP read/refresh/story/featured tools; permission-controlled
live app readback remains with the root task. Existing EF AOT limitations and
obsolete ExecuteSqlInterpolatedAsync warnings elsewhere remain unchanged.
