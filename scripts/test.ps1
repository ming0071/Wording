param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$Filter = ''
)
$ErrorActionPreference = 'Stop'
$taskDotnet = & (Join-Path $PSScriptRoot 'resolve-dotnet.ps1')
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    # Keep test binaries separate from the app that may already be running.
    $taskReport = if ($Filter) { 'tests-filtered.trx' } else { 'tests.trx' }
    $taskArguments = @(
        'test', 'tests/WordTrail.Tests/WordTrail.Tests.csproj',
        '-c', $Configuration, "-p:OutputPath=bin/TestRun/$Configuration/",
        '--logger', "trx;LogFileName=$taskReport",
        '--results-directory', 'artifacts/test-results', '--nologo'
    )
    if ($Filter) { $taskArguments += @('--filter', $Filter) }
    & $taskDotnet @taskArguments
    if ($LASTEXITCODE -ne 0) { throw "Tests failed. See artifacts/test-results/$taskReport and the failures above." }
} finally { Pop-Location }
