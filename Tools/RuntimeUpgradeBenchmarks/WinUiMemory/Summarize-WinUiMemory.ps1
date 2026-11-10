[CmdletBinding()]
param([Parameter(Mandatory)][string]$InputDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'

function Median([double[]]$values) {
    $s=@($values | Sort-Object); if($s.Count -eq 0){throw 'No samples'}
    $m=[int][Math]::Floor($s.Count/2)
    if($s.Count%2){return $s[$m]}; return ($s[$m-1]+$s[$m])/2
}

function UtcInstant($value, [string]$field) {
    if($value -is [DateTimeOffset]){return $value.ToUniversalTime()}
    if($value -is [DateTime]){
        if($value.Kind -eq [DateTimeKind]::Unspecified){throw "Missing time zone: $field"}
        return [DateTimeOffset]::new($value.ToUniversalTime())
    }
    if($value -isnot [string] -or $value -notmatch '(Z|[+-][0-9]{2}:[0-9]{2})$'){
        throw "Missing timestamp/time zone: $field"
    }
    return [DateTimeOffset]::Parse($value, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime()
}

function PositiveNumber($value, [string]$field) {
    if($null -eq $value -or $value -is [string] -or $value -is [bool]){throw "Missing/invalid numeric counter: $field"}
    $number=[double]$value
    if(-not [double]::IsFinite($number) -or $number -le 0){throw "Counter must be positive and finite: $field"}
    return $number
}

$runs=[Collections.Generic.List[object]]::new()
$samples=[Collections.Generic.List[object]]::new()
$excluded=[Collections.Generic.List[object]]::new()
$processes=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($file in Get-ChildItem -LiteralPath $InputDirectory -Filter '*-pid*.json') {
    $r=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if($r.ValidForComparison -ne $true -or $r.Metadata.SmokeTest -eq $true){
        $excluded.Add([pscustomobject]@{Version=$r.Metadata.VersionLabel;Page=$r.Metadata.Page;Run=$r.Metadata.Run;Reason=($r.QualityIssues -join ',');SmokeTest=$r.Metadata.SmokeTest}); continue
    }
    $m=$r.Metadata
    if($r.SchemaVersion -ne 1 -or $null -eq $m -or @($r.QualityIssues).Count -ne 0 -or $r.Failure){throw 'Invalid measurement provenance.'}
    if($m.VersionLabel -notin @('net10','net11') -or $m.Page -notin @('login','music-home')){throw 'Unexpected runtime/page label.'}
    if($m.ConfigurationLabel -ne 'Release' -or $m.SmokeTest -ne $false -or $m.DebuggerAttachedAtStart -ne $false -or
        $m.RequestedSamples -ne 30 -or $m.IntervalSeconds -ne 1 -or $r.SampleCount -ne 30 -or $r.Samples.Count -ne 30){
        throw 'Inconsistent measurement protocol.'
    }
    if($m.ProcessName -ne 'Mizon.Windows' -or $m.ProcessId -le 0 -or $m.Run -le 0){throw 'Invalid process/run identity.'}
    $runtime=@($m.LoadedModules | Where-Object Name -eq 'coreclr.dll')
    $expectedRuntime=if($m.VersionLabel -eq 'net10'){10}else{11}
    if($runtime.Count -ne 1 -or $runtime[0].FileMajorPart -ne $expectedRuntime -or $m.ExpectedRuntimeMajor -ne $expectedRuntime){
        throw 'Runtime label does not match the loaded coreclr.dll.'
    }
    $winui=@($m.LoadedModules | Where-Object Name -eq 'Microsoft.UI.Xaml.dll')
    $winuiVersions=@($winui.FileVersion | Sort-Object -Unique)
    if($winui.Count -eq 0 -or $winuiVersions.Count -ne 1 -or [string]::IsNullOrWhiteSpace($winuiVersions[0])){
        throw 'Missing or ambiguous WinUI module version.'
    }

    $processStarted=UtcInstant $m.ProcessStartUtc 'ProcessStartUtc'
    $started=UtcInstant $m.StartedUtc 'StartedUtc'
    $completed=UtcInstant $r.CompletedUtc 'CompletedUtc'
    if($started -lt $processStarted -or $completed -le $started){throw 'Invalid measurement time window.'}
    if(-not $processes.Add("$($m.ProcessId):$($processStarted.ToString('O'))")){
        throw 'Each run must use a fresh process; a PID/start-time pair was measured twice.'
    }

    $sidecar=Join-Path $InputDirectory "$($m.VersionLabel)-$($m.Page)-run$($m.Run)-readiness.json"
    if(-not(Test-Path -LiteralPath $sidecar)){throw 'Missing page-readiness evidence.'}
    $ready=Get-Content -LiteralPath $sidecar -Raw | ConvertFrom-Json
    $expectedPage=if($m.Page -eq 'login'){'login'}else{'home'}
    if($ready.Version -ne $expectedRuntime -or $ready.Page -ne $m.Page -or $ready.Run -ne $m.Run -or
        $ready.ProcessId -ne $m.ProcessId -or $ready.McpPageBefore -ne $expectedPage -or $ready.McpPageAfter -ne $expectedPage -or
        $ready.HasError -ne $false -or $ready.HasSession -ne $true -or [string]::IsNullOrWhiteSpace($ready.Language) -or
        ($m.Page -eq 'music-home' -and $ready.NativeModelLoaded -ne $true)){
        throw 'Page readiness did not match the measured process.'
    }
    $readyStarted=UtcInstant $ready.Started 'readiness.Started'
    $readyAt=UtcInstant $ready.ReadyAt 'readiness.ReadyAt'
    $readyCompleted=UtcInstant $ready.SampleCompletedAt 'readiness.SampleCompletedAt'
    if($readyStarted -gt $processStarted -or $readyAt -lt $processStarted -or ($started-$readyAt).TotalSeconds -lt 30 -or
        $readyCompleted -lt $completed){throw 'Readiness time window/30-second settling evidence is inconsistent.'}

    $w=$m.InitialWindow
    if($w.Visible -ne $true -or $w.Minimized -ne $false -or $w.Cloaked -ne 0 -or $w.CloakQueryHResult -ne 0 -or
        $w.Width -le 0 -or $w.Height -le 0 -or $w.Dpi -le 0 -or $w.Foreground -isnot [bool]){
        throw 'Invalid initial window state.'
    }
    $previousTime=$started
    $previousElapsed=0.0
    for($index=0; $index -lt 30; $index++) {
        $s=$r.Samples[$index]
        if($s.Sample -ne ($index+1) -or $s.ProcessId -ne $m.ProcessId -or $s.DebuggerAttached -ne $false -or
            $s.QualityIssues -or $s.WindowVisible -ne $true -or $s.WindowMinimized -ne $false -or $s.WindowCloaked -ne 0 -or
            $s.WindowWidth -ne $w.Width -or $s.WindowHeight -ne $w.Height -or $s.WindowDpi -ne $w.Dpi -or
            $s.WindowForeground -ne $w.Foreground -or $s.WindowHandle -ne $w.Handle){
            throw 'Duplicate/missing/out-of-order sample or inconsistent process/window state.'
        }
        $privateResident=PositiveNumber $s.PrivateWorkingSetBytes 'PrivateWorkingSetBytes'
        $totalResident=PositiveNumber $s.WorkingSetBytes 'WorkingSetBytes'
        $privateCommit=PositiveNumber $s.PrivateCommitBytes 'PrivateCommitBytes'
        if($privateResident -gt $totalResident){throw 'Private resident memory exceeds total resident memory.'}
        $sampleTime=UtcInstant $s.TimestampUtc 'TimestampUtc'
        $elapsed=PositiveNumber $s.ElapsedSeconds 'ElapsedSeconds'
        $null=PositiveNumber $s.SampleIntervalSeconds 'SampleIntervalSeconds'
        if($sampleTime -le $previousTime -or $sampleTime -gt $completed -or $elapsed -le $previousElapsed){throw 'Nonmonotonic/out-of-window sample time.'}
        $previousTime=$sampleTime; $previousElapsed=$elapsed
        $samples.Add([pscustomobject][ordered]@{
            Version=$m.VersionLabel;Page=$m.Page;Run=$m.Run;Sample=$s.Sample;TimestampUtc=$sampleTime.ToString('O')
            PrivateResidentBytes=$s.PrivateWorkingSetBytes;TotalResidentBytes=$s.WorkingSetBytes;PrivateCommitBytes=$s.PrivateCommitBytes
            Foreground=$s.WindowForeground;Width=$s.WindowWidth;Height=$s.WindowHeight;Dpi=$s.WindowDpi
        })
    }
    $runs.Add([pscustomobject][ordered]@{
        Version=$m.VersionLabel; Page=$m.Page; Run=$m.Run; ProcessId=$m.ProcessId
        ProcessStartUtc=$processStarted.ToString('O');RuntimeVersion=$runtime[0].ProductVersion;WinUiVersion=$winuiVersions[0]
        PrivateResidentMiB=(Median ([double[]]$r.Samples.PrivateWorkingSetBytes))/1MB
        TotalResidentMiB=(Median ([double[]]$r.Samples.WorkingSetBytes))/1MB
        PrivateCommitMiB=(Median ([double[]]$r.Samples.PrivateCommitBytes))/1MB
        Width=$w.Width;Height=$w.Height;Dpi=$w.Dpi;Foreground=$w.Foreground
        Language=$ready.Language;StartedUtc=$started.ToString('O');CompletedUtc=$completed.ToString('O')
    })
}
if($runs.Count -ne 12){throw "Need exactly12 valid fresh-process runs; found $($runs.Count)."}
if(@($runs | Group-Object Version,Page,Run | Where-Object Count -ne 1).Count){throw 'Duplicate measured run.'}
if(@($runs | Group-Object Width,Height,Dpi,Foreground,Language,WinUiVersion).Count -ne 1){throw 'Window/language/WinUI conditions differ; do not combine runs.'}
if($samples.Count -ne 360){throw 'Need exactly360 validated observations.'}
$comparisons=[Collections.Generic.List[object]]::new()
foreach($page in @('login','music-home')) {
    foreach($metric in @('PrivateResidentMiB','TotalResidentMiB','PrivateCommitMiB')) {
        $before=@($runs | Where-Object Version -eq 'net10' | Where-Object Page -eq $page)
        $after=@($runs | Where-Object Version -eq 'net11' | Where-Object Page -eq $page)
        if($before.Count -ne 3 -or $after.Count -ne 3){throw 'Need three runs of each version/page.'}
        $b=[double[]]@($before | ForEach-Object {$_.$metric}); $a=[double[]]@($after | ForEach-Object {$_.$metric})
        $bm=Median $b; $am=Median $a
        $comparisons.Add([pscustomobject]@{Page=$page;Metric=$metric;BeforeMiB=$bm;AfterMiB=$am;ChangeMiB=$am-$bm;ChangePercent=100*($am-$bm)/$bm;BeforeMinMiB=($b|Measure-Object -Minimum).Minimum;BeforeMaxMiB=($b|Measure-Object -Maximum).Maximum;AfterMinMiB=($a|Measure-Object -Minimum).Minimum;AfterMaxMiB=($a|Measure-Object -Maximum).Maximum})
    }
}
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
$runs | Sort-Object Page,Run,Version | Export-Csv (Join-Path $OutputDirectory 'runs.csv') -NoTypeInformation -Encoding utf8
$samples | Export-Csv (Join-Path $OutputDirectory 'samples.csv') -NoTypeInformation -Encoding utf8
[ordered]@{SchemaVersion=1;Aggregation='Median of three independent run medians per version/page; each run has 30 samples.';Unit='MiB (1048576 bytes)';Comparisons=$comparisons.ToArray();RejectedInInput=$excluded.ToArray();AdditionalExclusionsFile=$(if(Test-Path -LiteralPath (Join-Path $OutputDirectory 'exclusions.json')){'exclusions.json'}else{$null});Runs=$runs.ToArray()} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'summary.json') -Encoding utf8
$comparisons | Format-Table Page,Metric,BeforeMiB,AfterMiB,ChangeMiB,ChangePercent
