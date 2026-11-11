# Refresh the existing music home demo artwork

This mode updates only the twelve known demo stories and four known featured
slides listed by public ID in `MusicHomeArtworkInitializeOperation`. It does not
create clients/stories/features, edit personal avatars/music covers, change schema,
register an endpoint, or run automatically at startup. It uses the same local
integrated-security Mizon database as the existing development tool/API.

All inserts and updates live in the .NET business operation. No SQL script is used.
Original Media rows/files, story types, tags, titles, authors, likes, views and music
relations are retained. Image stories use the same image for content and preview.
Video fixtures retain video support through silent licensed photo-motion loops
with posters extracted from their actual first frame.

## Plan, then apply

Run from the server repository root. Use the canonical repository folder that
will remain on disk while the backend serves media, not a diagnostic worktree.

```powershell
$artwork = Join-Path (Get-Location) 'Mizon.Server.API\App_Data\MusicHomeArtwork'
dotnet run --project Tools/SeedStoryTestData -c Release -p:Platform=x64 -- --refresh-music-home-artwork --artwork-directory $artwork
dotnet run --project Tools/SeedStoryTestData -c Release -p:Platform=x64 -- --refresh-music-home-artwork --artwork-directory $artwork --apply
```

Without `--apply`, this mode only hashes local files and selects database fields.
The report includes previous media IDs for review/recovery. Missing/empty files,
missing fixtures, changed image/video types, or independently replaced artwork
fail before mutation. Ctrl+C cancels the operation. The transaction rolls back
all new media and relation updates if a fixture changes concurrently or an update
fails. All `Where` conjunctions are separate in the EF queries.

The initial replacement inserts eighteen dedicated Media rows. A repeat apply at
the same path/content is a no-op. A fresh readback verifies each content/preview
URI, SHA-256, size and dedicated filename. Cache keys change because newly inserted
media have new public IDs; shared originals are never overwritten.

The original IDs remain in the operation's fixture catalog for recovery using a
reviewed .NET business operation. There is deliberately no generic write endpoint
or automatic rollback that could overwrite later user edits.

## Validation

After regenerating assets, decode every JPG and every full MP4 with FFmpeg; verify
H.264/yuv420p, 1280×720, 30 fps, twelve seconds, and zero audio streams with ffprobe.
The three image sources reused from existing local discovery art are documented in
the source catalog along with newly downloaded photographs. Actual author avatars
are unchanged even if the old fixture reused that Media in another relationship.

This asset-only change reuses existing music-home MCP read/refresh and story/hero
controls. It does not add artificial artwork tools or bypass in-app permissions.
Headless decode/readback checks do not establish WinUI rendering or animation quality.
