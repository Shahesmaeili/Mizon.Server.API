[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateRange(1, [int]::MaxValue)][int]$TargetProcessId,
    [Parameter(Mandatory)][string]$ExpectedExecutablePath,
    [Parameter(Mandatory)][ValidateRange(6, 99)][int]$ExpectedRuntimeMajor,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]{1,48}$')][string]$VersionLabel,
    [Parameter(Mandatory)][ValidateSet('Release', 'Debug')][string]$ConfigurationLabel,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]{1,48}$')][string]$Page,
    [Parameter(Mandatory)][ValidateRange(1, 999)][int]$Run,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ReadinessEvidence = 'Not independently verified by this sampler.',
    [ValidateRange(1, 3600)][int]$Samples = 30,
    [ValidateRange(0.1, 10)][double]$IntervalSeconds = 1,
    [switch]$SmokeTest
)

$ErrorActionPreference = 'Stop'
if (-not [OperatingSystem]::IsWindows()) { throw 'This sampler requires Windows.' }
if (-not $SmokeTest -and $ConfigurationLabel -ne 'Release') {
    throw 'Formal comparison requires Release. Use -SmokeTest only for harness verification.'
}
if ([IntPtr]::Size -ne 8) { throw 'Run this script from 64-bit PowerShell.' }

if (-not ('MizonMemoryNativeV1' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class MizonMemoryNativeV1
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Counters
    {
        public uint cb, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage, PeakPagefileUsage, PrivateUsage;
        public UIntPtr PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    public sealed class WindowSnapshot
    {
        public long Handle;
        public bool Visible, Minimized, Foreground;
        public uint Cloaked, Dpi;
        public int Left, Top, Width, Height, ClientWidth, ClientHeight;
        public int CloakQueryHResult;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool K32GetProcessMemoryInfo(SafeProcessHandle process, ref Counters counters, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CheckRemoteDebuggerPresent(SafeProcessHandle process, [MarshalAs(UnmanagedType.Bool)] out bool present);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out uint value, uint size);

    public static SafeProcessHandle Open(int processId)
    {
        // Query-only rights; no memory writes, debug attach or process manipulation.
        var result = OpenProcess(0x1000 | 0x0400, false, processId);
        if (result.IsInvalid) {
            int error = Marshal.GetLastWin32Error();
            result.Dispose();
            throw new Win32Exception(error, "Could not open the target process for read-only queries.");
        }
        return result;
    }

    public static Counters ReadMemory(SafeProcessHandle process)
    {
        var counters = new Counters();
        counters.cb = (uint)Marshal.SizeOf(typeof(Counters));
        if (!K32GetProcessMemoryInfo(process, ref counters, counters.cb))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "PROCESS_MEMORY_COUNTERS_EX2 query failed.");
        return counters;
    }

    public static bool ReadDebugger(SafeProcessHandle process)
    {
        bool present;
        if (!CheckRemoteDebuggerPresent(process, out present))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Debugger status could not be verified.");
        return present;
    }

    public static WindowSnapshot ReadWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) throw new InvalidOperationException("The target has no main window handle.");
        Rect rect, client;
        if (!GetWindowRect(window, out rect) || !GetClientRect(window, out client))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Window geometry could not be read.");
        uint cloak;
        int result = DwmGetWindowAttribute(window, 14, out cloak, sizeof(uint));
        return new WindowSnapshot {
            Handle = window.ToInt64(), Visible = IsWindowVisible(window), Minimized = IsIconic(window),
            Foreground = GetForegroundWindow() == window, Cloaked = cloak, CloakQueryHResult = result,
            Dpi = GetDpiForWindow(window), Left = rect.Left, Top = rect.Top,
            Width = rect.Right - rect.Left, Height = rect.Bottom - rect.Top,
            ClientWidth = client.Right - client.Left, ClientHeight = client.Bottom - client.Top
        };
    }
}
'@
}

function Get-Median([double[]]$Values) {
    if ($Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 0) { return ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    return $sorted[$middle]
}

function Get-MemorySummary($Rows, [string]$Property) {
    if ($Rows.Count -eq 0) { return $null }
    $values = [double[]]@($Rows | ForEach-Object { $_.$Property })
    return [ordered]@{
        MedianBytes = Get-Median $values
        MedianMiB = (Get-Median $values) / 1MB
        MinimumBytes = ($values | Measure-Object -Minimum).Minimum
        MaximumBytes = ($values | Measure-Object -Maximum).Maximum
    }
}

$target = $null
$nativeHandle = $null
$records = [Collections.Generic.List[object]]::new()
$issues = [Collections.Generic.List[string]]::new()
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$fileStem = '{0}-{1}-{2}-run{3:D2}-pid{4}-{5}' -f $VersionLabel, $ConfigurationLabel.ToLowerInvariant(), $Page, $Run, $TargetProcessId, [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$jsonPath = Join-Path $outputRoot ($fileStem + '.json')
$csvPath = Join-Path $outputRoot ($fileStem + '.csv')
$startedUtc = [DateTimeOffset]::UtcNow
$metadata = $null
$failure = $null
try {
    $target = [Diagnostics.Process]::GetProcessById($TargetProcessId)
    if ($target.ProcessName -ne 'Mizon.Windows') { throw 'The target is not the Mizon.Windows main app.' }
    $target.Refresh()
    $processStartUtc = $target.StartTime.ToUniversalTime()
    $actualPath = [IO.Path]::GetFullPath($target.MainModule.FileName)
    $expectedPath = [IO.Path]::GetFullPath($ExpectedExecutablePath)
    if (-not [string]::Equals($actualPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The actual executable path differs from the requested build; possible single-instance redirection.'
    }
    $configurationSegment = if ($actualPath -match '[\\/](Debug|Release)[\\/]') { $Matches[1] } else { $null }
    if ($configurationSegment -and $configurationSegment -ne $ConfigurationLabel) {
        throw 'The configuration label conflicts with the actual executable path.'
    }

    $moduleNames = @('coreclr.dll', 'clrjit.dll', 'Microsoft.UI.Xaml.dll', 'Microsoft.WindowsAppRuntime.dll', 'Mizon.Windows.dll', 'Mizon.Windows.exe')
    $moduleRecords = @($target.Modules | Where-Object ModuleName -in $moduleNames | ForEach-Object {
        $info = $_.FileVersionInfo
        [pscustomobject]@{ Name=$_.ModuleName; Path=$_.FileName; FileVersion=$info.FileVersion; ProductVersion=$info.ProductVersion; FileMajorPart=$info.FileMajorPart }
    })
    $coreclr = @($moduleRecords | Where-Object Name -eq 'coreclr.dll')
    if ($coreclr.Count -ne 1 -or $coreclr[0].FileMajorPart -ne $ExpectedRuntimeMajor) {
        throw 'Loaded coreclr.dll does not match the expected runtime major version.'
    }
    $nativeHandle = [MizonMemoryNativeV1]::Open($TargetProcessId)
    $debugger = [MizonMemoryNativeV1]::ReadDebugger($nativeHandle)
    if (-not $SmokeTest -and $debugger) { throw 'A debugger is attached. This is not a valid Release comparison run.' }
    $initialWindow = [MizonMemoryNativeV1]::ReadWindow($target.MainWindowHandle)
    $metadata = [ordered]@{
        VersionLabel=$VersionLabel; ConfigurationLabel=$ConfigurationLabel; ConfigurationPathSegment=$configurationSegment
        Page=$Page; Run=$Run; SmokeTest=[bool]$SmokeTest; ReadinessEvidence=$ReadinessEvidence
        ProcessId=$TargetProcessId; ProcessName=$target.ProcessName; ProcessStartUtc=$processStartUtc.ToString('O')
        ExecutablePath=$actualPath; ExecutableSha256=(Get-FileHash -LiteralPath $actualPath -Algorithm SHA256).Hash
        ExpectedRuntimeMajor=$ExpectedRuntimeMajor; LoadedModules=$moduleRecords; DebuggerAttachedAtStart=$debugger
        InitialWindow=$initialWindow; LogicalProcessors=[Environment]::ProcessorCount
        OperatingSystem=[Runtime.InteropServices.RuntimeInformation]::OSDescription
        SamplerArchitecture=[Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        RequestedSamples=$Samples; IntervalSeconds=$IntervalSeconds; StartedUtc=$startedUtc.ToString('O')
    }

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $lastElapsed = 0.0
    $lastCpuSeconds = $target.TotalProcessorTime.TotalSeconds
    for ($index = 1; $index -le $Samples; $index++) {
        $due = $index * $IntervalSeconds
        $waitMilliseconds = [int][Math]::Ceiling(($due - $clock.Elapsed.TotalSeconds) * 1000)
        if ($waitMilliseconds -gt 0) { Start-Sleep -Milliseconds $waitMilliseconds }
        $target.Refresh()
        if ($target.HasExited -or $target.StartTime.ToUniversalTime() -ne $processStartUtc) {
            throw 'The target exited or its PID was reused; do not combine different processes.'
        }
        $memory = [MizonMemoryNativeV1]::ReadMemory($nativeHandle)
        $window = [MizonMemoryNativeV1]::ReadWindow($target.MainWindowHandle)
        $debuggerNow = [MizonMemoryNativeV1]::ReadDebugger($nativeHandle)
        $elapsed = $clock.Elapsed.TotalSeconds
        $cpuSeconds = $target.TotalProcessorTime.TotalSeconds
        $elapsedDelta = $elapsed - $lastElapsed
        $cpuDelta = [Math]::Max(0.0, $cpuSeconds - $lastCpuSeconds)
        $cpuOneCore = if ($elapsedDelta -gt 0) { 100.0 * $cpuDelta / $elapsedDelta } else { 0.0 }
        $sampleIssues = [Collections.Generic.List[string]]::new()
        if ($debuggerNow) { $sampleIssues.Add('debugger_attached') }
        if (-not $window.Visible -or $window.Minimized -or $window.Cloaked -ne 0) { $sampleIssues.Add('window_not_visible_normal') }
        if ($window.CloakQueryHResult -ne 0) { $sampleIssues.Add('window_cloak_query_failed') }
        if ($window.Width -ne $initialWindow.Width -or $window.Height -ne $initialWindow.Height -or $window.Dpi -ne $initialWindow.Dpi) { $sampleIssues.Add('window_size_or_dpi_changed') }
        if ($window.Foreground -ne $initialWindow.Foreground) { $sampleIssues.Add('foreground_state_changed') }
        foreach ($issue in $sampleIssues) { if (-not $issues.Contains($issue)) { $issues.Add($issue) } }
        $records.Add([pscustomobject]@{
            Sample=$index; TimestampUtc=[DateTimeOffset]::UtcNow.ToString('O'); ElapsedSeconds=$elapsed
            ProcessId=$TargetProcessId; PrivateWorkingSetBytes=$memory.PrivateWorkingSetSize.ToUInt64()
            WorkingSetBytes=$memory.WorkingSetSize.ToUInt64(); PrivateCommitBytes=$memory.PrivateUsage.ToUInt64()
            LifetimePeakWorkingSetBytes=$memory.PeakWorkingSetSize.ToUInt64(); SharedCommitBytes=$memory.SharedCommitUsage
            CpuSecondsDelta=$cpuDelta; SampleIntervalSeconds=$elapsedDelta; CpuPercentOneCore=$cpuOneCore
            CpuPercentMachine=$cpuOneCore / [Environment]::ProcessorCount
            HandleCount=$target.HandleCount; DebuggerAttached=$debuggerNow
            WindowHandle=$window.Handle; WindowVisible=$window.Visible; WindowMinimized=$window.Minimized
            WindowForeground=$window.Foreground; WindowCloaked=$window.Cloaked; WindowDpi=$window.Dpi
            WindowLeft=$window.Left; WindowTop=$window.Top; WindowWidth=$window.Width; WindowHeight=$window.Height
            ClientWidth=$window.ClientWidth; ClientHeight=$window.ClientHeight
            QualityIssues=($sampleIssues -join ';')
        })
        $lastElapsed = $elapsed
        $lastCpuSeconds = $cpuSeconds
    }
    $clock.Stop()
}
catch {
    # Only diagnostic validation/native query messages, never app command lines or account payloads.
    $failure = $_.Exception.Message
    $issues.Add('sampling_or_validation_failed')
}
finally {
    if ($null -ne $nativeHandle) { $nativeHandle.Dispose() }
    if ($null -ne $target) { $target.Dispose() }
}

$valid = -not $SmokeTest -and $null -eq $failure -and $records.Count -eq $Samples -and $issues.Count -eq 0
$report = [ordered]@{
    SchemaVersion=1; Scope='Main Mizon.Windows process only; no companion, GPU or whole-system attribution.'
    MetricSource='K32GetProcessMemoryInfo / PROCESS_MEMORY_COUNTERS_EX2; external read-only sampler.'
    Metadata=$metadata; CompletedUtc=[DateTimeOffset]::UtcNow.ToString('O')
    ValidForComparison=$valid; SampleCount=$records.Count; QualityIssues=$issues.ToArray(); Failure=$failure
    Summary=[ordered]@{
        PrivateWorkingSet=Get-MemorySummary $records 'PrivateWorkingSetBytes'
        TotalWorkingSet=Get-MemorySummary $records 'WorkingSetBytes'
        PrivateCommit=Get-MemorySummary $records 'PrivateCommitBytes'
    }
    Samples=$records.ToArray()
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding utf8
if ($records.Count -gt 0) { $records | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8 }
[pscustomobject]@{ Json=$jsonPath; Csv=$(if ($records.Count -gt 0) {$csvPath} else {$null}); Samples=$records.Count; ValidForComparison=$valid; SmokeTest=[bool]$SmokeTest; Issues=$issues.ToArray(); Failure=$failure } | ConvertTo-Json -Compress
if ($failure) { throw $failure }
if (-not $SmokeTest -and -not $valid) { throw 'Run completed but quality checks failed. Keep it as invalid evidence; do not use it in the comparison.' }
