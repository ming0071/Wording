param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$LockedRestore
)
$ErrorActionPreference = 'Stop'
$taskDotnet = & (Join-Path $PSScriptRoot 'resolve-dotnet.ps1')
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $taskRestoreArguments = @('restore', 'Wording.sln')
    if ($LockedRestore) { $taskRestoreArguments += '--locked-mode' }
    & $taskDotnet @taskRestoreArguments
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    & $taskDotnet build Wording.sln --no-restore -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & $taskDotnet test Wording.sln --no-build -c $Configuration --logger 'trx;LogFileName=tests.trx' --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally { Pop-Location }
