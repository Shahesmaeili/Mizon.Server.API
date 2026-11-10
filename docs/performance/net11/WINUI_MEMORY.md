# Actual WinUI process memory: .NET 10 versus .NET 11

Measured on the user's Windows machine on **2026-11-11 (Asia/Tehran)**. This is a separate experiment from the headless SDK/operation allocation benchmarks in [the migration report](README.md).

## Results

**Private resident RAM was practically unchanged in these scenarios.** The .NET 11 median is about 0.6 MiB higher on each page. The ranges overlap; these small differences do not establish a reliable memory improvement or regression. No statistical significance is claimed.

Values are **MiB**. Brackets show the lowest/highest of the three independent run medians, not confidence intervals. Each run contains 30 observations.

| Page | Metric | .NET10 median [range] | .NET11 median [range] | Change MiB | Change |
| --- | --- | ---: | ---: | ---: | ---: |
| Login | Private resident RAM | 59.28 [56.15–60.01] | 59.91 [59.15–60.58] | +0.63 | +1.06% |
| Music home | Private resident RAM | 181.46 [180.48–185.10] | 182.07 [179.09–182.43] | +0.61 | +0.34% |
| Login | Total resident working set | 191.72 [184.56–193.31] | 193.01 [192.31–193.72] | +1.29 | +0.67% |
| Music home | Total resident working set | 364.53 [363.58–368.39] | 366.06 [363.45–366.38] | +1.54 | +0.42% |
| Login | Private commit (not RAM) | 130.78 [129.91–131.16] | 130.80 [130.55–131.89] | +0.03 | +0.02% |
| Music home | Private commit (not RAM) | 279.62 [278.53–280.75] | 278.99 [277.66–289.25] | −0.63 | −0.22% |

Evidence: [summary](winui-memory/summary.json), [all 12 valid runs](winui-memory/runs.csv), [all 360 observations](winui-memory/samples.csv), [build provenance](winui-memory/provenance.json), [exclusions](winui-memory/exclusions.json).

## What was measured

The real packaged `Mizon.Windows.exe`, **Release/x64**, self-contained and trimmed/ReadyToRun, launched outside the Visual Studio debugger. The two scenarios are the sign-in form and the music discovery/home page. The sign-in form is opened without revoking the existing account session; it contains no entered credentials. Home uses the same saved account and the normal Welcome-to-Home initialization path. No playback or UI interaction is requested during the measurement.

The main comparison metric is **private resident RAM**, obtained from `PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize`. Total working set includes shared resident pages. Private commit is committed address-space backing, **not physical RAM**. These distinctions follow [Microsoft's counter definitions](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex2).

Independent taskbar, Widgets, Palettes, MCP companion, API, Visual Studio, DWM and GPU memory are excluded. The measured PID is always the actual main app. These counters cannot assign a percentage of memory to WinUI versus application code or to individual widgets.

## Matched conditions and protocol

- Baseline Windows source `8bd7dd9ad0b5e0ab4b85f6df55fbd71c55b9da2f`, SDK source `b547c08aa7c968ca3fd2ac9a6f5df728d30e8154`; .NET runtime **10.0.9**.
- Candidate Windows migration working tree later committed as `799d8a4163ff0a392fe0f03483812c2f0ca5e83e`, SDK source `dd8860eacf5ed9a82c01918a948615e6a2f0de45`; .NET runtime **11.0.0**. The candidate was compiled before the final migration commit, so its assembly informational version retains the base SHA `8bd7dd9`; the generated target-framework attribute, loaded runtime and staged DLL hash independently identify the actual .NET11 build.
- The product `.cs` and `.xaml` files did not change between these Windows commits. All **180 assets**, including the Persian font and native 3D model, are byte-identical. Framework packages and target framework changed together; this measures the actual upgrade, not an isolated CLR-only effect.
- Both full packages were staged from their generated `.build.appxrecipe`, including the final trimmed/ReadyToRun assemblies, fonts and native dependencies. Intermediate `bin` files are not the tested payload.
- The same package family, user state, warm media cache, Persian language and dark Acrylic setting were retained. Diagnostic package versions differ only to let Windows switch the loose package location; no package data was removed.
- One fixed local .NET11 backend was used for both clients. Startup asset seeding was disabled. No migrations, imports or synthetic database changes ran.
- A fresh main process is used for every run. Versions alternate within each page/round. Three valid runs are required per version/page.
- Existing approved MCP authentication tools open the form or normal Welcome flow and read state. No permission is granted by a test. The remembered Codex grant remains valid across restarts.
- Home readiness requires the home state, music window title and a fresh log entry confirming the native model loaded. A fixed **30-second settling period** follows readiness, then **30 samples at one-second intervals**.
- App PID, executable path, loaded `coreclr.dll`, debugger status, window size/DPI/visibility and foreground state are checked. Unstable/incomplete samples are retained as excluded evidence, not silently used.
- All final runs use a 3840×1019 outer window, 3824×1011 client area at 96 DPI. They are visible, nonminimized and uncloaked, with **foreground=false throughout**. This is a matched idle, unfocused-window comparison, not a focused interaction/frame-rate test.
- No forced garbage collection, working-set trimming, minimization or product optimization is performed.

## Limits

This is a short, warm-cache desktop observation on one real development machine, not a long-session leak test, an audio playback benchmark, or a NativeAOT-versus-JIT comparison. Other applications remain open. MCP confirms page state, not every widget's visual rendering; successful API requests/model loading and the settling period support readiness, but no Windows UI automation or screenshot comparison was performed. No claim about per-widget memory, frame rate or exact GPU residency follows from these measurements. Foreground/occlusion conditions must be read from the results; a visible window flag is not proof that no other window covers it.

The existing native artist preview uses Composition meshes and `LoadedImageSurface`, not a WebView2 renderer. Animations and visual features were not disabled or replaced; their normal unfocused-window lifecycle behavior was retained.

An entire preliminary series was excluded after focus/minimization interactions; the final series restarted after the user agreed not to interact. In the final series, one .NET10 music-home process crashed near the end of sampling. Windows recorded `0xc0000005` in `coreclr.dll`10.0.9 with a `GoToElementStateCore` WinUI ABI stack. Post-sampling MCP state verification failed, so that run was excluded despite the counter-only sampler flag and a fresh replacement was measured. This does not identify the root cause or establish that .NET11 fixes the crash. Every included run has successful before/after page evidence and no debugger/window-quality failures.

The final dataset contains 12 fresh processes / 360 observations. Native counter results were independently cross-checked against `System.Diagnostics.Process` for total working set and private commit. The aggregator checks runtime version, unique PID/start time, complete ordered samples, page evidence, settling duration and matched window/WinUI conditions.

## Cleanup

The original package version and registration path were restored and verified. The restored app reached Home through its normal MCP Welcome flow using the existing remembered grant. Test-owned main app and backend processes were closed afterward, matching the initial absence of those processes. The candidate build's temporary generated manifest change was reverted. No Visual Studio, Explorer or Windows Search process was stopped. Source, account session, media cache and permission grants were preserved.

## Reproduction and evidence

See [the external sampler and MCP probe](../../../Tools/RuntimeUpgradeBenchmarks/WinUiMemory/README.md). Raw local logs, permissions, package state and account responses are not published. Public measurement artifacts contain counter samples, aggregate statistics and nonsecret build/window provenance only.
