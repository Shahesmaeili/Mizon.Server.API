# Serve registered Music Home and mood assets over HTTPS

- Status: source verified
- Owner: Codex media transport task
- Date: 2026-11-21
- Repository and branch: `Mizon.Server.API`, `codex/local-media-content`
- Base commit: `3b20a2a`
- Related repositories: `Mizon.API.Swift` and `Mizon.Apple`
- Owned areas: media route, local file eligibility, isolated API diagnostics

## Request and acceptance criteria

The Apple Music Home needs the same registered image, video and mood assets used by Windows. Existing metadata for local assets contains a Windows `file://` storage URI. An authenticated client can retrieve those same bytes over HTTPS using the public media ID, including exact HTTP ranges for chunked downloads.

## Implementation and decisions

`GET /Medias/MediaContent?MediaPublicId=<uuid>` uses `MediaOperation` for the existing media lookup. Eligible content is served by ASP.NET Core's range-enabled file result and marked `private, no-store`. The route requires a bearer header. Unknown or ineligible media return 404. The metadata route and its storage URIs retain their existing contract.

The built-in media roots are `App_Data/MusicHomeArtwork`, `App_Data/MoodEmojis`, and `App_Data/PersonProfileFixtures` under the server content root. The authenticated endpoint serves registered `.glb` models as `model/gltf-binary` and supports byte ranges. For provisioned files outside the source tree, `Media:LocalContentRoots` accepts a list of deployment-owned absolute directories. This includes the exact `Assets/Discovery/Models` directory in the Windows checkout when a local featured-person fixture references files there. Only configure dedicated media directories writable by trusted deployment tooling. File type, registered size, local path containment, and reparse-point checks apply to every file. The endpoint accepts public IDs and derives the file path from the registered database row. Database data and schema are unchanged.

The Swift SDK release at `6b6279cff54d35e747934cf168d38331cd24c1bd` uses the API session for Windows-local media and the existing direct download path for HTTPS storage. Apple can play a video from the resulting cache file. Existing Music Home actions and controls use the same app services and business operations.

## Validation

| Check | Result | Limits |
| --- | --- | --- |
| Swift SDK `swift test --disable-sandbox` | 31 tests passed on macOS | Synthetic HTTPS ranges, origin, redirect, and authorization checks |
| `.NET 11.0.100` Release build | Passed | Existing EF AOT and obsolete SQL API warnings remain outside this change |
| Release `ApiDiagnostics` on macOS Arm64 | Passed | Real loopback Kestrel with synthetic JWTs and asset rows. Verified exact 206 ranges, 416, private cache headers, anonymous and query-token denial, root and symlink restrictions, and the legacy metadata response. |

## Handoff

The Visual Studio LAN listener uses its own existing HTTPS configuration. Verify access from macOS and iOS against the Windows host after the backend update. No Windows service or database was modified during this task.

## Publication

Publish the server source separately from runtime deployment. This task uses database source `05f3a3e` and Swift SDK source `6b6279cff54d35e747934cf168d38331cd24c1bd`.
