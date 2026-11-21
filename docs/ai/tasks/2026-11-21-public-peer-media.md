# 2026-11-21 — Public peer media manifests

The authenticated `GET /Medias/PeerManifest?MediaPublicId=<uuid>` endpoint
reuses the existing media lookup and returns the normal API envelope. Its
content has `fileHash` (whole SHA-256 hex), `fileSize`, `chunkSize`, and
`pieceSha256` (base64 of concatenated binary SHA-256 digests). A 404 leaves
the normal download path available.

The server reads precomputed digests from the absolute
`PeerMedia:PublicManifestDirectory` configuration key. Only assets explicitly
published as eligible for local redistribution should get a manifest. Leave
the key unset to keep the endpoint disabled. No changes are required to the
database. The server never computes or imports digests during a request or on
startup. The endpoint requires the existing bearer authentication and disables
shared response caching.

## Import a manifest for published media

```powershell
dotnet run --project Tools/PeerMediaManifest -- <asset-path> <manifest-directory> <registered-chunk-size>
```

The tool produces `<file-sha256>.<chunk-size>.sha256` and prints the exact hash,
file size and piece count to compare to the media metadata. Deploy that file
to the configured directory with the application identity granted read access.
Scoped eligibility is essential because verified copies can be served to other
devices on the same local network without presenting an account credential.

## Checks

- .NET 11 Release x64 API build passed with existing EF warnings.
- The isolated HTTP diagnostics tested authenticated manifest reads, anonymous
  denial, missing eligibility, rejection of a malformed manifest and unchanged
  byte-range behavior. No real DB data was read or written by these fixtures.
- LAN transport and piece verification live in the SDK and platform apps.
- No deployment or server configuration was changed by this task.
