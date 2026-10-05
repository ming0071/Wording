$ErrorActionPreference = 'Stop'
$taskArtifacts = [IO.Path]::GetFullPath((Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'artifacts'))
$taskFixture = Join-Path $taskArtifacts ('.current-update-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $taskFixture 'scripts') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $taskFixture 'artifacts/package') | Out-Null
$taskUpdater = Join-Path $taskFixture 'scripts/update-current.ps1'
Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'update-current.ps1') -Destination $taskUpdater
$taskPackage = Join-Path $taskFixture 'artifacts/package'
$taskCurrent = Join-Path $taskFixture 'artifacts/Wording-current'

try {
    Set-Content -LiteralPath (Join-Path $taskPackage 'Wording.exe') -Value 'first version'
    Set-Content -LiteralPath (Join-Path $taskPackage 'Wording.runtimeconfig.json') -Value '{}'
    Set-Content -LiteralPath (Join-Path $taskPackage 'removed-in-next-version.txt') -Value 'old'
    & $taskUpdater -PackageDirectory $taskPackage | Out-Null
    if ((Get-Content -LiteralPath (Join-Path $taskCurrent 'Wording.exe') -Raw).Trim() -ne 'first version') {
        throw 'The initial package was not copied.'
    }

    $taskRejected = $false
    try { & $taskUpdater -PackageDirectory $taskCurrent | Out-Null } catch { $taskRejected = $true }
    if (-not $taskRejected) { throw 'Updating from the current directory must be rejected.' }

    Remove-Item -LiteralPath (Join-Path $taskPackage 'Wording.runtimeconfig.json')
    $taskRejected = $false
    try { & $taskUpdater -PackageDirectory $taskPackage | Out-Null } catch { $taskRejected = $true }
    if (-not $taskRejected -or (Get-Content -LiteralPath (Join-Path $taskCurrent 'Wording.exe') -Raw).Trim() -ne 'first version') {
        throw 'An incomplete package must preserve the existing app.'
    }

    Set-Content -LiteralPath (Join-Path $taskPackage 'Wording.runtimeconfig.json') -Value '{}'
    Set-Content -LiteralPath (Join-Path $taskPackage 'Wording.exe') -Value 'second version'
    Remove-Item -LiteralPath (Join-Path $taskPackage 'removed-in-next-version.txt')
    & $taskUpdater -PackageDirectory $taskPackage | Out-Null
    if ((Get-Content -LiteralPath (Join-Path $taskCurrent 'Wording.exe') -Raw).Trim() -ne 'second version' -or
        (Test-Path -LiteralPath (Join-Path $taskCurrent 'removed-in-next-version.txt'))) {
        throw 'Updating must replace the app at the same path and remove obsolete files.'
    }

    function Get-Process {
        [CmdletBinding()]
        param([string]$Name)
        [PSCustomObject]@{ Path = Join-Path $taskCurrent 'Wording.exe' }
    }
    $taskRejected = $false
    try { & $taskUpdater -CheckOnly } catch { $taskRejected = $true }
    Remove-Item -LiteralPath Function:Get-Process
    if (-not $taskRejected -or (Get-Content -LiteralPath (Join-Path $taskCurrent 'Wording.exe') -Raw).Trim() -ne 'second version') {
        throw 'A running app must block the update and preserve its files.'
    }
    if (Get-ChildItem -LiteralPath (Join-Path $taskFixture 'artifacts') -Force -Filter '.Wording-current-*') {
        throw 'The update left staging or previous-package directories behind.'
    }
    Write-Output 'PASS: fixed-path creation, replacement, obsolete-file removal, invalid-source preservation, and running-app guard.'
} finally {
    $taskResolvedFixture = (Resolve-Path -LiteralPath $taskFixture).Path
    if (-not [IO.Path]::GetDirectoryName($taskResolvedFixture).Equals($taskArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Test cleanup must stay inside workspace artifacts.'
    }
    Remove-Item -LiteralPath $taskResolvedFixture -Recurse -Force
}
