# Track discussion API and operations

- Status: complete
- Owner: Codex
- Date: 2026-11-20
- Repository and branch: Mizon.Server.API, `codex/music-comment-discussion`
- Base commit: `d175b5c`
- Related repositories: Mizon.Database, Mizon.API.DotNet, Mizon.Windows
- Owned areas: music comment operations, endpoints, API generated JSON and SQL fixture.

## Request and acceptance criteria

List and sort track discussion in pages, look up an exact comment or thread,
post timed and untimed comments and replies, and manage an account’s vote.
Include posting platform and typed tags for the Windows discussion and MCP.

## Implementation and decisions

`GET Musics/MusicComments` accepts `PageSize`, `After`, `Sort`,
`ParentMusicCommentPublicId`, `MusicCommentPublicId` and `TimedOnly`.
Calls without pagination keep the existing read behavior. Windows discussion
requests a 24-item page and continues from the public ID returned as `Next`.
The waveform uses a separate bounded timed read. Comment creation validates the
track, actual duration, same-track parent and up to eight artist, account,
track or music video tags. Posting platform comes from the authenticated
session. `POST Musics/MusicCommentVote` uses desired vote -1, 0 or 1 and
invalidates comment cache entries. `GET Musics/MusicCommentTagSearch` returns
bounded prefix matches. Tags are loaded separately for the selected page.
Routes use generated JSON and cancellation. Full server NativeAOT with EF
remains subject to its existing limitations.

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| Server Debug build and API diagnostics, .NET 11 | Passed | Existing EF/dynamic-code warnings |
| `ApiDiagnostics --music-discussion-sql` | Passed | Disposable local SQL Server database with synthetic data. Up/down/up, cross-track replies, timestamp validity, exact links, paging, votes and tags |

## Handoff

Apply the additive `AddMusicCommentDiscussion` migration before serving the
updated endpoints. Translation is a UI placeholder. Deploy the corresponding
SDK and Windows revisions for the new interactive controls and scoped MCP.

## Publication

Database dependency: `05f3a3ebf084a1b848acd30589ef7ee2d63ff0db`. Target branch: `main`.
