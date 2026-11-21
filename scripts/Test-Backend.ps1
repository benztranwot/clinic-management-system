[CmdletBinding()]
param(
    [ValidateSet('Full', 'Fast', 'Scale', 'Benchmark')]
    [string]$Mode = 'Full',
    [ValidateSet(1, 2, 4)]
    [int]$Workers = 4,
    [ValidateRange(1, 5)]
    [int]$Repeat = 1
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'backend/HealthcareApp.Api.IntegrationTests'
$resultsRoot = Join-Path $repoRoot ('TestResults/backend/' + [Guid]::NewGuid().ToString('N'))

function Invoke-TestRun([int]$ThreadCount, [string]$Label, [string]$Filter) {
    $resultDirectory = Join-Path $resultsRoot $Label
    $testArguments = @(
        'test', $project, '-c', 'Release', '--no-build', '--no-restore',
        '--results-directory', $resultDirectory,
        '--logger', 'trx;LogFileName=tests.trx'
    )
    if ($Filter) { $testArguments += @('--filter', $Filter) }
    $testArguments += @(
        '--', 'xUnit.ParallelizeTestCollections=true',
        "xUnit.MaxParallelThreads=$ThreadCount", 'xUnit.ParallelAlgorithm=conservative'
    )

    Write-Host "Running $Label with $ThreadCount worker(s)..."
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @testArguments | Out-Host
    $testExit = $LASTEXITCODE
    $timer.Stop()
    if ($testExit -ne 0) { throw "Backend tests failed ($testExit). Results: $resultDirectory" }

    $reportPath = Join-Path $resultDirectory 'tests.trx'
    [xml]$report = Get-Content -LiteralPath $reportPath -Raw
    $results = @($report.SelectNodes("//*[local-name()='UnitTestResult']"))
    $counters = $report.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counters -or [int]$counters.total -eq 0 -or
        [int]$counters.failed -ne 0 -or [int]$counters.passed -ne [int]$counters.total) {
        throw "The run did not pass every selected test. Inspect $reportPath"
    }

    Write-Host 'Slowest tests:'
    $results | ForEach-Object {
        [pscustomobject]@{
            Seconds = [math]::Round([TimeSpan]::Parse($_.duration).TotalSeconds, 2)
            Test = $_.testName
        }
    } | Sort-Object Seconds -Descending | Select-Object -First 8 | Format-Table -AutoSize | Out-Host

    [pscustomobject]@{
        Workers = $ThreadCount
        Seconds = [math]::Round($timer.Elapsed.TotalSeconds, 2)
        Passed = [int]$counters.passed
        Total = [int]$counters.total
        Report = $reportPath
    }
}

Push-Location $repoRoot
try {
    # Build once; every timing run below uses exactly the same Release binaries.
    & dotnet build $project -c Release --nologo --disable-build-servers -m:1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }

    if ($Mode -eq 'Benchmark') {
        $runs = @(
            for ($trial = 1; $trial -le $Repeat; $trial++) {
                # Reverse alternate trials to reduce the effect of warm-up/order.
                $counts = if ($trial % 2 -eq 1) { @(1, 2, 4) } else { @(4, 2, 1) }
                foreach ($count in $counts) {
                    Invoke-TestRun $count "workers-$count-trial-$trial" ''
                }
            }
        )
        $runs | Export-Csv -LiteralPath (Join-Path $resultsRoot 'timings.csv') -NoTypeInformation
        $runs | Format-Table Workers, Seconds, Passed, Total -AutoSize | Out-Host
        Write-Host "Compare repeated passing runs when choosing concurrency. Reports: $resultsRoot"
    } else {
        $filter = switch ($Mode) {
            'Fast' { 'Category!=Scale' }
            'Scale' { 'Category=Scale' }
            default { '' }
        }
        Invoke-TestRun $Workers ($Mode.ToLowerInvariant()) $filter | Format-List | Out-Host
    }
} finally {
    Pop-Location
}
