# 2026-11-16 API request language

- Owner: Codex
- Branch: `codex/api-language`; base `50c603b`
- Related: Mizon.API.DotNet and Mizon.Windows language handoffs
- Scope: API middleware, existing operation executor, operation messages and diagnostics

Send `Accept-Language: fa-IR` or `Accept-Language: en-US` from the application's
current language preference. The API accepts neutral tags and regional variants,
honors quality weights and uses English as the fallback. Responses declare
`Content-Language` and vary by `Accept-Language`. The existing server output
cache already keys on this header. API-owned operation errors have localized
titles and details where applicable, with unchanged keys, error codes and HTTP
statuses. Successful payloads keep their existing fields and user-written text.

`dotnet run --project Tools/ApiDiagnostics -p:NuGetAudit=false` passed with JSON
reflection disabled. Real loopback checks cover negotiation (including malformed
input), concurrent isolation, registration validation details and cache variants.
The login failure is synthetic and no live accounts or database writes were used.
A full server NativeAOT publish was not part of this check; existing EF AOT
limitations remain. No database migration, UI or additional MCP permission scope
is needed. Use SDK and Windows commits for those layers.
