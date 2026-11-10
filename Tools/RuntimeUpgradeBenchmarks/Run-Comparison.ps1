param(
    [Parameter(Mandatory)][string]$ArtifactRoot,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(3, 20)][int]$Rounds = 5,
    [ValidateRange(30, 1000)][int]$SqlIterations = 80
)

$ErrorActionPreference = 'Stop'
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$previousTiering = $env:DOTNET_TieredCompilation
$env:DOTNET_TieredCompilation = '0'
$started = [DateTimeOffset]::UtcNow
try {
    # Do not run builds, other benchmarks or import/simulator tools concurrently.
    # Alternate the order to reduce temperature, filesystem and SQL-cache bias.
    foreach ($category in @('database', 'sdk', 'windows', 'api', 'logic')) {
        for ($round = 1; $round -le $Rounds; $round++) {
            $versions = if ($round % 2) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
            foreach ($version in $versions) {
                $directory = Join-Path $ArtifactRoot "$version/$category"
                $name = "$category-$version-$round"
                $json = Join-Path $OutputDirectory "$name.json"
                $log = Join-Path $OutputDirectory "$name.log"
                Write-Output "RUN $name"
                switch ($category) {
                    'database' { & dotnet (Join-Path $directory 'Mizon.Database.Tests.dll') --benchmark $json *> $log }
                    'sdk' { & (Join-Path $directory 'SdkBenchmarks.exe') $json *> $log }
                    'windows' { & (Join-Path $directory 'Mizon.Windows.PerformanceBenchmarks.exe') --output $json *> $log }
                    'api' { & dotnet (Join-Path $directory 'ApiDiagnostics.dll') --component-benchmark $json *> $log }
                    'logic' { & dotnet (Join-Path $directory 'LogicPerformance.dll') (Join-Path $OutputDirectory $name) $SqlIterations *> $log }
                }
                if ($LASTEXITCODE -ne 0) {
                    Get-Content -LiteralPath $log -Tail 30
                    throw "Benchmark $name failed with exit code $LASTEXITCODE; results are incomplete."
                }
            }
        }
    }
    [pscustomobject]@{
        StartedUtc = $started.ToString('O')
        CompletedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        Rounds = $Rounds
        SqlIterationsPerOperation = $SqlIterations
        TieredCompilation = 0
        Notes = 'Release x64, alternating fresh processes; SQL SELECT-only; no UI memory claims.'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'run.json')
}
finally { $env:DOTNET_TieredCompilation = $previousTiering }
