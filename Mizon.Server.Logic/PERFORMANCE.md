# Logic performance and NativeAOT audit

Verified on 2026-11-08, .NET 10 / EF Core SQL Server 10.0.5.

## Query structure

Small, separate EF queries are deliberately retained. No large consolidated
query, response cache, result truncation, schema migration or data update was
introduced. Contexts supplied by DI remain owned by DI. Global Search still
streams clients and music concurrently through separate contexts and bounded
channels; its production source was left unchanged.

Changes:

- Story creation fetches widget types, styles, music references and client
  references once per distinct set, in **four separate queries**. Validation
  still follows input order. A 24-widget mixed request needs 7 SELECT commands
  including media/content-type validation, instead of the former 75.
- Stem mixing retains separate music, source-file and stem queries. A batched
  stem read replaces one read per source file, with at most 3 SELECTs and no
  pairing across different source files.
- Feature durations are a separate projection returning only the first source
  file's duration for each displayed music, rather than loading all files.
- Empty library/detail/story pages skip irrelevant enrichment queries. Following
  activity pages skip data sources absent from the final page; actor username
  and profile projections select only the latest matching rows.
- File/playlist responses are projected directly, without intermediate response
  copies. View existence uses AnyAsync. Unused empty contributor lists are not
  allocated for populated results.
- The empty mood catalog skips its effects read. An alternative streaming
  implementation was measured and discarded because the extra lookup did not
  reduce allocations for the current 580-mood/123-effect catalog.
- GlobalUsings was reviewed; all current shared namespaces are used.
- Library publishing flags were replaced with IsAotCompatible. Actual executable
  publication remains the API application's decision.

The related Database build was also corrected: EF 10 model generation runs
before the usual output copy. Its narrowly scoped preparatory target stages the
current assembly and runtime/dependency metadata, preventing missing/stale
assemblies on a fresh build. Clean, changed-model and artifacts-path builds were
verified in an isolated copy; no real entity or migration changed.

## Measurements

The reusable runner is Tools/LogicPerformance. It uses the existing local SQL
Server database, three warmups and 30 measured iterations for each of 53 read
cases. All 53 response snapshots matched the saved pre-change binary. Invalid
request cases are included but are not mistaken for executed SQL.

These are **observations on the current small database**, not an SLA or an
estimate for production-sized tables. Each operation uses a fresh context, with
warm connection/query/model caches; HTTP, authentication, JSON serialization,
context construction and response transmission are outside operation timing.

| Case | Commands before -> after | Operation median before -> after |
| --- | --- | --- |
| Empty library continuation | 5 -> 3 | 4.357 -> 2.570 ms |
| Empty playlist detail continuation | 5 -> 3 | 3.709 -> 2.047 ms |
| Empty music-story list | 2 -> 1 | 2.177 -> 1.977 ms |
| Music following widget (current mixed fixture) | 17 -> 16 | 15.502 -> 16.605 ms |

Unchanged paths also varied between runs. The reduced query counts and identical
results are reliable; a global speedup is not claimed from this short benchmark.
The following widget's measured latency did **not** improve in this sample.

4,547 of 4,830 measured reader-execution events (94.1%) were below 1 ms.
The interceptor duration measures dispatch through ExecuteReader returning,
including provider/SQL/network work. It is **not SQL Server CPU time**, nor the
time to consume/materialize all rows. The full operation timings include
enumeration and response assembly and are longer.

Examples that still exceed the requested 1 ms threshold:

- Full 580-item mood catalog: about 13.3 ms for the operation; command p95 8.5 ms.
- Full 4,384-frame waveform: about 16.0 ms; command p95 6.3 ms.
- Search "st": about 2.2 ms for the two concurrent streams; command p95 1.6 ms.
- Music details: about 5.9 ms for nine deliberately separate SELECTs.

A small contention check used 8 workers, 80 requests per case, each with its
own context: music details median/p95 5.0/14.4 ms; empty library continuation
1.9/4.3 ms; following widget 14.1/18.6 ms. It is not a production load test.

Next performance investigations require representative data and execution plans:
waveform ordering/covering index, exact active username/email lookups, and full
catalog transport. Existing activity, public-ID, like and stem indexes were
reviewed; duplicate speculative indexes were not added. Wildcard substring
search cannot be made universally sub-millisecond simply by changing LINQ.

## Validation and limits

- Release x64 API build using Visual Studio MSBuild: passed.
- 53 before/after read-response snapshots: identical.
- Story validation, duplicate tags, transforms, error ordering and cancellation:
  passed; SaveChanges is blocked before SQL by a test interceptor.
- 2,200 distinct references per widget lookup set: passed, with OPENJSON fallback
  in all four queries and one parameter each; no artificial request cap.
- Following music/all scopes, 4/40-item pages, ownership, ordering, routing,
  cursor continuation and no-follow state: passed. The 40-item fixture fits one
  page, so its continuation branch is explicitly reported as skipped.
- Search early-disposal and pre-cancellation: passed.
- No database writes or schema changes were performed.
- Existing NuGet audit warnings remain visible, not suppressed in production.

A real EF query-precompilation audit was run against the current Logic sources.
EF 10.0.5 rejected multiple dynamic/captured-context query paths, including
visibility helpers, feed composition and initialization paths. Therefore the
entire Logic/API layer is **not certified for NativeAOT**. IsAotCompatible is
analysis, not proof of successful native query execution. No runtime reflection
fallback or blanket AOT warning suppression was added. The isolated audit
disabled NuGet audit only to let EF's design-time workspace reach query analysis;
the production project retains normal auditing.

Official background:
- https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries
- https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying

## Local evidence

Detailed logs, response snapshots, SQL text without parameter values and timings:

D:/source/repos/Mizon.Diagnostics/LogicOptimization-20261108/

- baseline-results/measurements.json
- verified-results/measurements.json
- verified-comparison.json
- verified-run.log
- api-vs-build.log
- aot-query-audit.log

The diagnostic output contains real response data; keep it local rather than
committing it to the repository.
