# External Mizon WinUI memory sampling

`Measure-WinUiMemory.ps1` queries a real, already-running `Mizon.Windows` process.
It does not start/stop/focus/navigate the app, attach a debugger, alter consent,
force a GC, trim the working set, minimize a window or modify project source.
Requires 64-bit PowerShell 7 on Windows 11 22H2 or newer with the September 2023
update, which supplies `PROCESS_MEMORY_COUNTERS_EX2`.

```powershell
$appPath = (Resolve-Path '<your Release app directory>/Mizon.Windows.exe').Path
$resultsPath = Join-Path ([IO.Path]::GetTempPath()) 'Mizon-WinUiMemory'

./Tools/RuntimeUpgradeBenchmarks/WinUiMemory/Measure-WinUiMemory.ps1 `
  -TargetProcessId 12345 `
  -ExpectedExecutablePath $appPath `
  -ExpectedRuntimeMajor 11 -VersionLabel net11 -ConfigurationLabel Release `
  -Page login -Run 1 -OutputDirectory $resultsPath `
  -ReadinessEvidence 'MCP login page, controls enabled, not busy; fixed 30s settling completed.'
```

Resolve the actual process ID after launch; the example ID is a placeholder. Use
runtime major `10` and label `net10` for the separately built baseline.

The default is 30 samples spaced one second apart, with the **first sample after
one second**, not immediately at startup. `-Samples` and
`-IntervalSeconds` are configurable. Use `-SmokeTest` for testing the harness
against a Debug/debugger-attached process; such output is **always** marked
`ValidForComparison=false` and must not appear as a formal benchmark result.

## Metric meanings

- **PrivateWorkingSetBytes**: resident private RAM; useful primary comparison
  close to Task Manager's private working-set memory display. Task Manager may
  group related processes or apply active/suspended semantics differently.
- **WorkingSetBytes**: total resident pages for this process, including shared
  pages. Summing this across processes double-counts shared pages.
- **PrivateCommitBytes**: committed private memory (`PrivateUsage`), **not RAM**.
  It includes memory which need not currently be resident.
- **LifetimePeakWorkingSetBytes**: whole-process lifetime peak, including startup;
  this is not the maximum within the current sampling window.
- **SharedCommitBytes**: OS shared-commit counter; neither private RAM nor GPU VRAM.
- CPU deltas are read from cumulative process CPU time, with both one-core and
  machine-normalized percentages. No activity is induced in the target.

The JSON includes all samples, time-window medians/ranges, PID/start time,
executable SHA256/path, selected loaded runtime/app modules and their versions,
debugger status, window geometry/DPI, visibility, minimized/cloaked state and
foreground status. CSV contains individual observations. No account information,
command-line arguments, credentials, tokens or app-memory contents are collected.
Diagnostic output does contain machine-local executable/module paths and freeform
readiness evidence. Keep raw files outside the repository; publish only reviewed,
sanitized summaries. Never put account names, credentials or raw MCP output in
`ReadinessEvidence`.

## Validation and comparable runs

The executable path must match exactly and the **loaded** `coreclr.dll` major
version must match. This catches launching a baseline that redirects to an already
running candidate through single-instance activation. The script validates process
identity and debugger state throughout sampling. A formal run requires the Release
label, and rejects an explicit conflicting Debug/Release path segment. A label
cannot independently prove compiler optimization flags; keep the actual build log.

The script rejects incomplete/error runs, debugger attachment, invisible/minimized/
cloaked windows, size/DPI changes and foreground-state changes. Stable background
visibility is recorded, not silently claimed to be foreground. It does not prove
whether another window visually covers the app. It never infers that the desired
page has finished loading merely because a process/window exists; readiness is
external evidence supplied by the operator/MCP and must be verified separately.
`ValidForComparison` means these sampler checks passed, not that the operator's
page label, build flags or equivalent content were independently proven.

The [MCP state probe](McpProbe/README.md) can inspect supported authentication state
using the user's existing scoped approval. Run it before sampling, wait for it to
exit, then allow the same settling interval. Navigation state alone does not prove
that music widgets, images or a model have finished loading.

For before/after comparisons, use actual Release/x64 versions, identical account,
window bounds/DPI/theme, scroll position, page content, playback state and warmed
asset caches. Confirm page readiness, allow the same settling interval, then sample
with the same interval/count. Run at least three fresh-process repetitions per
version in alternating order. Keep normal animations running. Do not compare a
foreground music page against a minimized, empty, signed-out or error page.

Report median of run medians and the run range. Main-process figures exclude
independent taskbar/Widgets/MCP processes. Measure these separately if needed;
do not include unrelated WebView2 instances or global DWM memory. These OS counters
cannot attribute RAM to a particular widget, .NET managed objects, WinUI or GPU.

## Aggregating a completed comparison

`Summarize-WinUiMemory.ps1 -InputDirectory <local-results> -OutputDirectory <public-evidence>`
requires exactly three valid runs per runtime/page, each containing30 samples.
Every valid run must have a matching `<version>-<page>-run<number>-readiness.json`
sidecar containing Version, Page, Run, the measured ProcessId, McpPageBefore/McpPageAfter,
HasSession, HasError, Language and NativeModelLoaded, plus Started, ReadyAt and
SampleCompletedAt timestamps with an offset or `Z`. Login expects `login`; music-home expects
`home` and a loaded native model. Capture these through the existing approved MCP
read-state operation and the current process's model log, not fabricated values.
The aggregator rejects mismatched PID/page evidence, reused process identities,
duplicate/incomplete samples, debugger runs, a settling interval shorter than30s,
or different window geometry/DPI/foreground/language/WinUI-version conditions. It publishes only
counter samples, nonsecret module-version/window provenance and medians/ranges.

Aggregate **run medians**, not all360 autocorrelated observations as independent
experiments. A change percentage is `(after - before) / before *100`; negative
means less memory. Range columns are the lowest/highest of the three run medians,
not a statistical confidence interval. A small difference within run-to-run noise
does not establish a reliable application memory improvement or regression.

## Counter and sampler validation

The x64 native structure is 96 bytes, with `PrivateWorkingSetSize` after
`PrivateUsage`. A query-only cross-check against `System.Diagnostics.Process`
verified that native `WorkingSetSize` equals `WorkingSet64` and native
`PrivateUsage` equals `PrivateMemorySize64`. Adjacent queries can differ when the
running process changes memory; they are not an atomic joint snapshot. Private
working set is a different measure and must not be compared to private commit.

The sampler uses a monotonic clock and absolute observation deadlines, recording
the actual elapsed interval for CPU deltas. A delayed sample is not treated as
having taken exactly one second. Every sample checks the process start time,
debugger state and native window state. Native and managed process handles are
disposed even when sampling fails. A three-sample harness smoke run passed;
smoke output remains excluded from comparison results.

References:

- [PROCESS_MEMORY_COUNTERS_EX2](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex2)
- [GetProcessMemoryInfo](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo)
- [Working set](https://learn.microsoft.com/en-us/windows/win32/memory/working-set)
- [Microsoft's Task Manager memory explanation](https://blogs.windows.com/msedgedev/2021/01/13/investigate-microsoft-edge-memory-usage/)
