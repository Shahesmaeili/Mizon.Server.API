# Mizon: .NET 10 to .NET 11 measurements

Measured 2026-11-10 on this development laptop. These are measured component results, not estimated percentages for the complete UI.

Follow-up: [actual packaged WinUI memory on Login and Music Home](WINUI_MEMORY.md), measured separately on 2026-11-11. Do not interpret the headless SDK process peaks below as the Windows application's RAM.

## Environment and method

- Intel Core i7-12700H, 14 cores / 20 logical processors, about 32 GiB RAM; Windows build 26340.
- Baseline: SDK 10.0.400-preview.0.26322.102, runtime 10.0.9, EF 10.0.5; SDK libraries 10.0.14. Candidate: SDK 11.0.100, runtime/framework/EF 11.0.0.
- Release/x64, same source and fixtures, five fresh-process repetitions per version, alternating order. Builds paused during timing. Desktop services remained running.
- Tiered compilation disabled identically for stable steady-state comparison; no production setting changed. This measures the complete compiler/runtime/framework dependency upgrade, not just the JIT.
- SQL:53 actual read-only operation cases,80 measured iterations after warmup per process; 400 per case/version. All 530 operation result hashes and query-count comparisons matched the initial reference. No database writes.
- In-memory benchmark categories use9–15 batches per process. Tables report the median of five process medians. Ranges are the minimum/maximum process medians, not confidence intervals.
- 69 reported cases include four search-first-item measurements. Cold EF model creation is one measurement per process; it is not full app startup.
- Negative change means less time or allocation. Small differences and overlapping ranges may be noise; no statistical significance is claimed.

## Timing and allocation

Time is **microseconds per operation** (divide by 1000 for milliseconds). Allocation is **bytes allocated per operation**, not retained RAM. LogicPerformance uses approximate process-wide GC allocation counters around each async operation; API/SDK counters also include their own background work. These are not object-retention measurements. SearchFirstItem allocation is not separately measured; use the full Search operation row.

| Area / case | .NET10 µs | .NET11 µs | Time change | .NET10 B/op | .NET11 B/op | Allocation change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| API / Api.Ndjson20Rows | 32.441 | 20.627 | -36.42% | 664 | 664 | 0.00% |
| API / Api.OutputCacheBypassHttp | 128.571 | 123.189 | -4.19% | 5280 | 5370 | 1.69% |
| API / Api.OutputCacheHitHttp | 151.706 | 144.956 | -4.45% | 8310 | 8276 | -0.41% |
| Database / ColdModelAndProvider | 1365142.700 | 1021371.100 | -25.18% | 1170200 | 983184 | -15.98% |
| Database / NewContextAndSet | 85.134 | 27.316 | -67.91% | 65214 | 35096 | -46.18% |
| Database / PooledContextAndSet | 0.244 | 0.257 | 5.34% | 80 | 80 | 0.00% |
| Logic / Album | 1707.100 | 1641.500 | -3.84% | 188568 | 155744 | -17.41% |
| Logic / Comments | 1623.700 | 1553.400 | -4.33% | 139400 | 106600 | -23.53% |
| Logic / Contributors | 957.300 | 800.500 | -16.38% | 114800 | 82000 | -28.57% |
| Logic / ContributorSongs | 1318.000 | 1203.100 | -8.72% | 155792 | 122984 | -21.06% |
| Logic / Features | 5050.600 | 4168.700 | -17.46% | 434600 | 401136 | -7.70% |
| Logic / FindClient | 534.500 | 409.800 | -23.33% | 90192 | 57400 | -36.36% |
| Logic / FindClient.Missing | 468.000 | 395.100 | -15.58% | 89952 | 57344 | -36.25% |
| Logic / Following.All | 8866.500 | 8273.700 | -6.69% | 762560 | 725624 | -4.84% |
| Logic / Following.EmptyPage | 0.300 | 0.300 | 0.00% | 0 | 0 | n/a |
| Logic / Following.Music | 8766.300 | 8315.700 | -5.14% | 737808 | 696976 | -5.53% |
| Logic / Genres | 606.100 | 465.400 | -23.21% | 90200 | 57400 | -36.36% |
| Logic / Library | 2125.500 | 1960.200 | -7.78% | 196800 | 171880 | -12.66% |
| Logic / Library.EmptyPage | 1287.400 | 1115.100 | -13.38% | 131200 | 98400 | -25.00% |
| Logic / LyricLines | 2292.200 | 2271.000 | -0.92% | 180400 | 155768 | -13.65% |
| Logic / Lyrics | 803.400 | 743.900 | -7.41% | 106600 | 73800 | -30.77% |
| Logic / Me | 2303.300 | 2120.000 | -7.96% | 213136 | 180368 | -15.37% |
| Logic / Media | 646.900 | 423.000 | -34.61% | 90200 | 65592 | -27.28% |
| Logic / Moods | 8933.200 | 8958.300 | 0.28% | 516600 | 489848 | -5.18% |
| Logic / Music | 3290.600 | 2882.600 | -12.40% | 254112 | 221296 | -12.91% |
| Logic / Music.Missing | 560.700 | 397.600 | -29.09% | 90200 | 65552 | -27.33% |
| Logic / MusicFiles | 911.600 | 722.700 | -20.72% | 114800 | 82000 | -28.57% |
| Logic / MusicStories | 2081.600 | 1919.400 | -7.79% | 205000 | 171400 | -16.39% |
| Logic / PlaylistDetails | 2339.100 | 2034.700 | -13.01% | 205000 | 172200 | -16.00% |
| Logic / PlaylistDetails.EmptyPage | 1474.000 | 1161.500 | -21.20% | 139400 | 106600 | -23.53% |
| Logic / Playlists | 974.400 | 943.800 | -3.14% | 123000 | 90200 | -26.67% |
| Logic / Profile | 1522.900 | 1361.300 | -10.61% | 139400 | 106600 | -23.53% |
| Logic / Reactions | 927.800 | 875.100 | -5.68% | 118296 | 90192 | -23.76% |
| Logic / ReactionTypes | 519.800 | 359.900 | -30.76% | 82000 | 57256 | -30.18% |
| Logic / Search.25 | 0.200 | 0.200 | 0.00% | 0 | 0 | n/a |
| Logic / Search.616C697368 | 1228.400 | 1079.700 | -12.11% | 237656 | 172200 | -27.54% |
| Logic / Search.6172 | 1397.200 | 1063.200 | -23.90% | 237720 | 172200 | -27.56% |
| Logic / Search.6E6F2D6D617463682D6D697A6F6E2D37383435 | 1181.800 | 993.000 | -15.98% | 237416 | 172200 | -27.47% |
| Logic / Search.7374 | 1522.100 | 1340.600 | -11.92% | 245928 | 180400 | -26.65% |
| Logic / Search.D8A2 | 0.300 | 0.200 | -33.33% | 0 | 0 | n/a |
| Logic / Search.D8AED8A7 | 1291.300 | 1078.300 | -16.50% | 237768 | 172200 | -27.58% |
| Logic / Stems | 1284.900 | 1190.900 | -7.32% | 147600 | 122976 | -16.68% |
| Logic / Stems.All.0 | 1480.800 | 1354.700 | -8.52% | 147600 | 122984 | -16.68% |
| Logic / Stems.All.1 | 834.900 | 737.400 | -11.68% | 106600 | 73800 | -30.77% |
| Logic / Stems.All.10 | 817.100 | 708.000 | -13.35% | 106600 | 73800 | -30.77% |
| Logic / Stems.All.11 | 1463.100 | 1164.900 | -20.38% | 147600 | 114800 | -22.22% |
| Logic / Stems.All.2 | 873.600 | 724.700 | -17.04% | 106600 | 73800 | -30.77% |
| Logic / Stems.All.3 | 887.100 | 717.300 | -19.14% | 106600 | 73800 | -30.77% |
| Logic / Stems.All.4 | 880.400 | 750.900 | -14.71% | 106600 | 73800 | -30.77% |
| Logic / Stems.All.5 | 836.100 | 727.800 | -12.95% | 106600 | 73800 | -30.77% |
| Logic / Stems.All.6 | 876.200 | 752.800 | -14.08% | 106600 | 73800 | -30.77% |
| Logic / Stems.All.7 | 827.900 | 729.100 | -11.93% | 106600 | 73800 | -30.77% |
| Logic / Stems.All.8 | 828.500 | 646.300 | -21.99% | 106600 | 73800 | -30.77% |
| Logic / Stems.All.9 | 831.400 | 727.700 | -12.47% | 106600 | 73800 | -30.77% |
| Logic / Stems.Missing | 495.000 | 350.400 | -29.21% | 82000 | 57400 | -30.00% |
| Logic / Stories | 1648.900 | 1533.400 | -7.00% | 196800 | 155800 | -20.83% |
| Logic / Stories.Missing | 1288.100 | 1208.800 | -6.16% | 139400 | 106600 | -23.53% |
| Logic / StoryDetails | 3533.100 | 3379.800 | -4.34% | 344096 | 311424 | -9.50% |
| Logic / Waveform | 8011.600 | 7590.100 | -5.26% | 1377576 | 1347392 | -2.19% |
| SDK / cached-get | 0.251 | 0.137 | -45.64% | 208 | 208 | 0.00% |
| SDK / large-json-1200-rows | 2580.894 | 2546.649 | -1.33% | 448070 | 448006 | -0.01% |
| SDK / ndjson-search-200-rows | 336.675 | 335.943 | -0.22% | 59868 | 59612 | -0.43% |
| SDK / small-json-get | 4.579 | 4.296 | -6.19% | 2523 | 2459 | -2.54% |
| SearchFirstItem / Search.616C697368 | 1152.000 | 1015.300 | -11.87% | n/a | n/a | n/a |
| SearchFirstItem / Search.6172 | 1367.100 | 1020.300 | -25.37% | n/a | n/a | n/a |
| SearchFirstItem / Search.7374 | 1306.100 | 1141.200 | -12.63% | n/a | n/a | n/a |
| SearchFirstItem / Search.D8AED8A7 | 1259.800 | 1044.400 | -17.10% | n/a | n/a | n/a |
| Windows / windows.deeplink.parse_roundtrip | 0.914 | 0.936 | 2.37% | 1170 | 1154 | -1.37% |
| Windows / windows.mcp.json_frame_roundtrip | 9.183 | 5.967 | -35.03% | 3840 | 2888 | -24.79% |

## Variability across processes

| Area / case | .NET10 range, µs | .NET11 range, µs |
| --- | ---: | ---: |
| API / Api.Ndjson20Rows | 31.472–34.669 | 19.410–33.706 |
| API / Api.OutputCacheBypassHttp | 119.381–137.235 | 115.070–153.728 |
| API / Api.OutputCacheHitHttp | 142.492–173.065 | 143.895–175.595 |
| Database / ColdModelAndProvider | 1347046.700–1389452.900 | 1011416.500–1040911.500 |
| Database / NewContextAndSet | 83.049–86.113 | 26.112–28.604 |
| Database / PooledContextAndSet | 0.233–0.260 | 0.236–0.267 |
| Logic / Album | 1686.200–1983.300 | 1430.700–1679.300 |
| Logic / Comments | 1344.200–1830.900 | 1446.900–1969.900 |
| Logic / Contributors | 765.900–983.200 | 767.200–945.000 |
| Logic / ContributorSongs | 1213.800–1407.100 | 1077.400–1253.500 |
| Logic / Features | 4211.500–5513.900 | 3958.400–4532.800 |
| Logic / FindClient | 410.400–575.900 | 359.200–672.700 |
| Logic / FindClient.Missing | 389.400–616.800 | 351.000–453.100 |
| Logic / Following.All | 8431.300–9618.400 | 7698.400–8953.300 |
| Logic / Following.EmptyPage | 0.300–0.400 | 0.300–0.400 |
| Logic / Following.Music | 7910.000–9312.700 | 7274.000–8474.700 |
| Logic / Genres | 515.700–643.600 | 445.700–503.900 |
| Logic / Library | 1858.800–2947.500 | 1800.100–2043.300 |
| Logic / Library.EmptyPage | 1087.100–1396.300 | 999.200–1185.100 |
| Logic / LyricLines | 2124.400–3701.400 | 2090.900–2605.800 |
| Logic / Lyrics | 685.700–876.100 | 697.200–879.000 |
| Logic / Me | 2185.100–3674.600 | 2056.200–2363.000 |
| Logic / Media | 543.100–866.000 | 382.900–507.100 |
| Logic / Moods | 8764.900–9377.000 | 8532.500–9470.600 |
| Logic / Music | 2947.400–3381.000 | 2798.100–3236.100 |
| Logic / Music.Missing | 452.800–750.700 | 374.800–427.900 |
| Logic / MusicFiles | 826.000–948.400 | 683.800–772.000 |
| Logic / MusicStories | 2005.200–2338.900 | 1888.100–2231.800 |
| Logic / PlaylistDetails | 2087.600–3680.000 | 1734.700–2291.000 |
| Logic / PlaylistDetails.EmptyPage | 1253.700–2303.500 | 1150.700–1325.600 |
| Logic / Playlists | 950.500–1345.600 | 881.700–980.600 |
| Logic / Profile | 1221.200–1770.500 | 1167.700–1537.000 |
| Logic / Reactions | 825.200–949.500 | 751.100–1350.600 |
| Logic / ReactionTypes | 442.000–563.700 | 316.200–440.900 |
| Logic / Search.25 | 0.200–1.400 | 0.200–0.200 |
| Logic / Search.616C697368 | 1087.800–1694.900 | 1015.300–1229.700 |
| Logic / Search.6172 | 1152.800–1881.500 | 1003.800–1266.800 |
| Logic / Search.6E6F2D6D617463682D6D697A6F6E2D37383435 | 1068.800–1703.300 | 907.300–1041.600 |
| Logic / Search.7374 | 1262.100–2034.000 | 1210.500–1520.200 |
| Logic / Search.D8A2 | 0.200–1.000 | 0.200–0.200 |
| Logic / Search.D8AED8A7 | 1211.500–1801.600 | 998.700–1126.600 |
| Logic / Stems | 1157.100–1408.500 | 1154.700–1309.800 |
| Logic / Stems.All.0 | 1274.400–2434.300 | 1238.500–1872.600 |
| Logic / Stems.All.1 | 786.900–1385.100 | 721.400–889.100 |
| Logic / Stems.All.10 | 739.700–1438.000 | 622.600–1105.900 |
| Logic / Stems.All.11 | 1140.200–2075.400 | 1109.800–1284.200 |
| Logic / Stems.All.2 | 785.700–1295.200 | 644.000–781.400 |
| Logic / Stems.All.3 | 642.800–1414.200 | 660.200–791.900 |
| Logic / Stems.All.4 | 705.800–1378.900 | 702.000–842.900 |
| Logic / Stems.All.5 | 724.900–1265.200 | 629.800–830.800 |
| Logic / Stems.All.6 | 780.500–1497.400 | 624.200–984.200 |
| Logic / Stems.All.7 | 660.700–1357.400 | 647.000–973.000 |
| Logic / Stems.All.8 | 674.400–1481.800 | 618.700–737.900 |
| Logic / Stems.All.9 | 760.700–1463.000 | 610.900–765.600 |
| Logic / Stems.Missing | 467.100–660.200 | 324.400–419.400 |
| Logic / Stories | 1496.200–1776.700 | 1481.400–1661.500 |
| Logic / Stories.Missing | 1285.300–1384.900 | 1185.100–1352.800 |
| Logic / StoryDetails | 3178.300–6544.700 | 3041.900–3873.300 |
| Logic / Waveform | 7529.300–9269.900 | 7231.800–8032.800 |
| SDK / cached-get | 0.242–0.256 | 0.130–0.141 |
| SDK / large-json-1200-rows | 2515.001–2603.161 | 2495.521–2650.174 |
| SDK / ndjson-search-200-rows | 330.453–355.867 | 321.991–369.516 |
| SDK / small-json-get | 4.547–4.909 | 4.222–4.569 |
| SearchFirstItem / Search.616C697368 | 1035.100–1437.500 | 955.400–1173.700 |
| SearchFirstItem / Search.6172 | 1124.000–1843.100 | 968.300–1237.400 |
| SearchFirstItem / Search.7374 | 1151.900–1659.500 | 1068.900–1207.200 |
| SearchFirstItem / Search.D8AED8A7 | 1167.500–1723.300 | 964.800–1096.600 |
| Windows / windows.deeplink.parse_roundtrip | 0.904–0.927 | 0.926–0.962 |
| Windows / windows.mcp.json_frame_roundtrip | 9.095–9.187 | 5.867–6.032 |

## What these results establish

- New EF context/model overhead and Windows MCP JSON/framing improve clearly in these fixtures. Existing pooled contexts remain close, with a small measured slowdown.
- Music detail and several real SQL-facing operations improve, but complete operations still take milliseconds. This does not establish a universal sub-millisecond SQL latency guarantee.
- Large SDK JSON and 200-row SDK search streaming are essentially unchanged within observed variability. Cached SDK reads improve; cached reads do no network request.
- API NDJSON measures 20 rows written through the real server writer. API cache HTTP fixtures include the loopback client and server; their trivial uncached handler does no SQL, so cache-hit vs bypass is not a real backend-cache benefit comparison.
- Windows route parsing/MCP framing are headless. No rendered UI, animations, playback, GPU memory or whole WinUI working set was benchmarked.
- The SDK benchmark process peak working set median was 65.4 MiB on10 and 56.9 MiB on11. This belongs only to that standalone benchmark process, not the Mizon Windows app.

## Evidence and reproduction

- [Summary JSON](summary.json), [CSV](summary.csv), [all process medians](process-medians.json), [run metadata](run.json).
- [Runner and methodology](../../../Tools/RuntimeUpgradeBenchmarks/README.md). Retain baseline/candidate binaries and run sequentially; do not compare Debug against Release.
- Baseline commits: Server 9fe3eb9, Database c31909d, SDK b547c08, Windows 8bd7dd9. Identical newly-added benchmark source was included in each baseline build before retargeting.
- Raw results/build logs: D:/MizonTaskDiagnostics/Net11. Personal response snapshots stay local and are not published.
- NativeAOT model/SDK/MCP/route checks are separate functional validation, not these JIT benchmark numbers. Full EF-consuming server and full WinUI NativeAOT support are not inferred.

## Upgrade compatibility decisions

- [EF11 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/breaking-changes): replaced removed EFOptimizeContext flag with scaffold-stage configuration. Existing local SQL compatibility 170 supports EF11 default 160; no database setting changed.
- [MSBuild .NET TaskHost](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/sdk#configure-net-tasks): ExplorerCommands SDK COM tasks use Runtime=NET when invoked by full-framework Visual Studio MSBuild, preventing a verified HostModel dependency mismatch. CLI builds retain their normal path.

## Functional verification

| Scope | Result |
| --- | --- |
| Server, Logic, utilities | All13projects Release/x64 built; API auth, NDJSON/backpressure/cancellation, SignalR and OutputCache checks passed |
| Database | Managed40checks; actual win-x64 NativeAOT executable40checks passed; compiled-model generation passed |
| SDK | Managed diagnostic suite and actual win-x64 NativeAOT diagnostic suite passed |
| Windows main app | Visual Studio MSBuild Debug/x64 and Release/x64 passed; Release trimming/R2R pipeline completed |
| Windows components | All17projects built, including new benchmark; ExplorerCommands clean VS COM Rebuild passed |
| MCP and navigation | Managed600route checks,25pin checks,31MCP groups,110access assertions,158discovery assertions; NativeAOT600routes,25pin and realMCP catalog checks passed |
| Additional components | 46shortcut,16downloader,7MCP staging,315PaletteABI and49native-card-parser checks passed; Widgets native helper compiled |

All36projects across the four repositories built. Existing preview API, WinRT,
nullable and dependency trimming/AOT warnings remain. This is not a claim that
the entire WinUI application or EF-query server was published and tested NativeAOT.
No Windows UI automation, deployment, Windows Search registration or live account
form submission was performed.
