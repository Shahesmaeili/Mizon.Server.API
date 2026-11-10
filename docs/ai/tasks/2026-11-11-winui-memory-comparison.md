# Actual WinUI memory comparison — .NET 10 / .NET 11

- Owner: Codex root; branch `codex/winui-net11-memory`.
- Request: measure the actual Windows app on sign-in and music discovery, using both runtimes.
- Scope: external measurement tools and reproducible evidence. No product UI, playback, permission or database changes.
- Server base: `068bc93937facd53a23a7fb03493d7a2383ea4e7`.
- Windows baseline: `8bd7dd9ad0b5e0ab4b85f6df55fbd71c55b9da2f`; candidate: `799d8a4163ff0a392fe0f03483812c2f0ca5e83e`.
- SDK baseline: `b547c08aa7c968ca3fd2ac9a6f5df728d30e8154`; candidate: `dd8860eacf5ed9a82c01918a948615e6a2f0de45`.

## Protocol

Build both actual applications in Release/x64, launch outside Visual Studio, and verify the loaded runtime rather than assuming the launch selected the intended instance. Reuse the same package identity, account, cached media, theme, window geometry and backend. Start a fresh process for each page/run. Allow normal loading and animations to settle; never force garbage collection or trim a working set. Record private resident memory separately from total resident memory and private committed bytes. Keep companion processes outside the main-app result.

## Ownership

- Root: app lifetime/package registration, page-state checks, sample orchestration and report.
- Windows agent: isolated baseline source/build only.
- SDK agent: external Windows memory-counter sampler only.
- Database agent: official MCP client probe, using existing supported tools and consent only.

## Completion

Completed12 valid fresh-process runs (three per runtime/page), with30 observations
each after30s settling. .NET10.0.9 versus11.0.0, Release/x64, complete trimmed/R2R
package layouts, byte-identical180assets and unchanged product C#/XAML.

Private resident medians: Login59.28→59.91MiB (+1.06%); MusicHome181.46→182.07MiB
(+0.34%). Overlapping run ranges do not establish a reliable improvement/regression.
All final windows were visible/nonminimized/uncloaked but not foreground; no UI
automation or visual/frame-rate claim. The older headless benchmark is not WinUI RAM.

One final-series .NET10home process crashed with0xc0000005 in a WinUI ABI call stack;
post-sampling MCP verification failed, and the run was excluded and replaced.
Its cause and whether .NET11 fixes it are not established. Preliminary samples
affected by window interactions were excluded as a whole before the final series.

Validation: native EX2 counters cross-checked, sampler/parser checks, official MCP
probe Release build (zero warnings/errors), before/after page state, remembered
consent across restarts, runtime/module/path and window checks, strict360-sample
aggregation. Original package registration/generated manifest restored; restored
app reached Home; test-owned app/API closed. No Explorer/VS/Search restart.

Publish only tools, sanitized counter CSV/JSON and documentation. No raw app logs,
account payloads, permission stores, package files or machine credentials.

See [the complete report](../../performance/net11/WINUI_MEMORY.md).
