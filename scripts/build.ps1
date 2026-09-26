param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet restore WordTrail.sln
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet build WordTrail.sln --no-restore -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet test WordTrail.sln --no-build -c $Configuration --logger 'trx;LogFileName=tests.trx' --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally { Pop-Location }
