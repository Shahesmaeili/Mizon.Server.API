param(
    [Parameter(Mandatory)][string]$InputDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$run = Get-Content -LiteralPath (Join-Path $InputDirectory 'run.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
function Median([double[]]$values) {
    $ordered = @($values | Sort-Object)
    if ($ordered.Count -eq 0) { throw 'Missing benchmark samples.' }
    return $ordered[[int][Math]::Floor($ordered.Count / 2)]
}
$rows = [Collections.Generic.List[object]]::new()
function Add-Row($category, $version, $round, $name, $time, $bytes) {
    $allocation = if ($null -eq $bytes) { $null } else { [double]$bytes }
    $rows.Add([pscustomobject]@{ Category=$category; Version=$version; Round=$round; Name=$name; Microseconds=[double]$time; AllocatedBytes=$allocation })
}
foreach ($version in @('baseline','candidate')) {
    for ($round=1; $round -le $run.Rounds; $round++) {
        $db = Get-Content (Join-Path $InputDirectory "database-$version-$round.json") -Raw | ConvertFrom-Json
        foreach ($m in $db.Measurements) { Add-Row 'Database' $version $round $m.Name ($m.MedianMilliseconds * 1000) $m.AllocatedBytesPerOperation }
        $sdk = Get-Content (Join-Path $InputDirectory "sdk-$version-$round.json") -Raw | ConvertFrom-Json
        foreach ($m in $sdk.Measurements) { Add-Row 'SDK' $version $round $m.Name $m.MedianMicrosecondsPerOperation $m.MedianAllocatedBytesPerOperation }
        $windows = Get-Content (Join-Path $InputDirectory "windows-$version-$round.json") -Raw | ConvertFrom-Json
        foreach ($m in $windows.Results) { Add-Row 'Windows' $version $round $m.Name ($m.MedianNanosecondsPerOperation / 1000) $m.MedianAllocatedBytesPerOperation }
        $api = Get-Content (Join-Path $InputDirectory "api-$version-$round.json") -Raw | ConvertFrom-Json
        foreach ($m in $api.Results) { Add-Row 'API' $version $round $m.Name (Median $m.Samples.MicrosecondsPerOperation) (Median $m.Samples.AllocatedBytesPerOperation) }
        $logic = Get-Content (Join-Path $InputDirectory "logic-$version-$round/measurements.json") -Raw | ConvertFrom-Json
        foreach ($m in $logic) {
            Add-Row 'Logic' $version $round $m.Name ($m.OperationMedianMs * 1000) $m.AllocatedBytesMedian
            if ($null -ne $m.FirstItemMedianMs) { Add-Row 'SearchFirstItem' $version $round $m.Name ($m.FirstItemMedianMs * 1000) $null }
        }
    }
}

# Validate response content and actual query counts across every measured run,
# without copying personal response snapshots into the report.
$reference = Get-Content (Join-Path $InputDirectory 'logic-baseline-1/measurements.json') -Raw | ConvertFrom-Json
$checks = [Collections.Generic.List[object]]::new()
foreach ($version in @('baseline','candidate')) {
    for ($round=1; $round -le $run.Rounds; $round++) {
        $measurements = Get-Content (Join-Path $InputDirectory "logic-$version-$round/measurements.json") -Raw | ConvertFrom-Json
        foreach ($expected in $reference) {
            $actual = $measurements | Where-Object Name -eq $expected.Name
            if ($null -eq $actual -or $actual.ResponseHash -ne $expected.ResponseHash -or
                $actual.MinCommands -ne $expected.MinCommands -or $actual.MaxCommands -ne $expected.MaxCommands) {
                $checks.Add([pscustomobject]@{Version=$version; Round=$round; Name=$expected.Name; Issue='Response hash or query count differs; investigate before claiming improvement.'})
            }
        }
    }
}
$summary = foreach ($group in ($rows | Group-Object Category,Name)) {
    $before = @($group.Group | Where-Object Version -eq baseline)
    $after = @($group.Group | Where-Object Version -eq candidate)
    $old = Median $before.Microseconds
    $new = Median $after.Microseconds
    $oldBytes = if ($before[0].Category -eq 'SearchFirstItem') { $null } else { Median $before.AllocatedBytes }
    $newBytes = if ($after[0].Category -eq 'SearchFirstItem') { $null } else { Median $after.AllocatedBytes }
    [pscustomobject]@{
        Category=$before[0].Category; Name=$before[0].Name; ProcessesPerVersion=$run.Rounds
        Net10Microseconds=$old; Net11Microseconds=$new
        TimeChangePercent= $(if ($old -gt 0) {100 * ($new / $old - 1)} else {$null})
        Net10RangeMicroseconds=@(($before.Microseconds | Measure-Object -Minimum).Minimum, ($before.Microseconds | Measure-Object -Maximum).Maximum)
        Net11RangeMicroseconds=@(($after.Microseconds | Measure-Object -Minimum).Minimum, ($after.Microseconds | Measure-Object -Maximum).Maximum)
        Net10AllocatedBytes=$oldBytes; Net11AllocatedBytes=$newBytes
        AllocationChangePercent= $(if ($oldBytes -gt 0) {100 * ($newBytes / $oldBytes - 1)} else {$null})
    }
}
$summary | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'summary.json')
$rows | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'process-medians.json')
$checks.ToArray() | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'validation-issues.json')
$summary | Select-Object Category,Name,Net10Microseconds,Net11Microseconds,TimeChangePercent,Net10AllocatedBytes,Net11AllocatedBytes,AllocationChangePercent | Export-Csv (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation
Write-Output "Summarized $($summary.Count) cases; validation issues: $($checks.Count). Negative changes mean less time/allocation."
if ($checks.Count -gt 0) { exit 1 }
