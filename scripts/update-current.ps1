param([string]$PackageDirectory = '', [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$taskArtifacts = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'))
$taskCurrent = Join-Path $taskArtifacts 'Wording-current'

function Assert-CurrentCanUpdate {
    if (Test-Path -LiteralPath $taskCurrent) {
        $taskEntry = Get-Item -LiteralPath $taskCurrent
        if (-not $taskEntry.PSIsContainer -or $taskEntry.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            throw 'Wording-current must be a normal directory inside workspace artifacts.'
        }
    }
    $taskRunning = Get-Process Wording -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and $_.Path.StartsWith($taskCurrent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
    }
    if ($taskRunning) { throw 'Close Wording-current before updating the pinned app. Its existing files have been preserved.' }
}

Assert-CurrentCanUpdate
if ($CheckOnly) { return }
if (-not $PackageDirectory) { throw 'PackageDirectory is required.' }
$taskSource = (Resolve-Path -LiteralPath $PackageDirectory).Path
if (-not $taskSource.StartsWith($taskArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $taskSource.Equals($taskCurrent, [StringComparison]::OrdinalIgnoreCase) -or
    (Get-Item -LiteralPath $taskSource).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
    throw 'The source package must be a separate normal directory inside workspace artifacts.'
}
if (-not (Test-Path -LiteralPath (Join-Path $taskSource 'Wording.exe') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $taskSource 'Wording.runtimeconfig.json') -PathType Leaf)) {
    throw 'The source directory is missing the published app.'
}

# Prepare the full new copy before touching the app used by the pinned shortcut.
$taskSuffix = [Guid]::NewGuid().ToString('N')
$taskStaging = Join-Path $taskArtifacts ".Wording-current-staging-$taskSuffix"
$taskPrevious = Join-Path $taskArtifacts ".Wording-current-previous-$taskSuffix"
foreach ($taskTarget in @($taskCurrent, $taskStaging, $taskPrevious)) {
    $taskAbsolute = [IO.Path]::GetFullPath($taskTarget)
    if (-not [IO.Path]::GetDirectoryName($taskAbsolute).Equals($taskArtifacts, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Update targets must stay directly inside workspace artifacts.'
    }
}
try {
    Copy-Item -LiteralPath $taskSource -Destination $taskStaging -Recurse
    Assert-CurrentCanUpdate
    if (Test-Path -LiteralPath $taskCurrent) {
        Move-Item -LiteralPath $taskCurrent -Destination $taskPrevious
    }
    try {
        Move-Item -LiteralPath $taskStaging -Destination $taskCurrent
    } catch {
        if (Test-Path -LiteralPath $taskPrevious) {
            Move-Item -LiteralPath $taskPrevious -Destination $taskCurrent
        }
        throw
    }
    if (Test-Path -LiteralPath $taskPrevious) {
        Remove-Item -LiteralPath $taskPrevious -Recurse -Force
    }
    Write-Output "Pinned app updated: $(Join-Path $taskCurrent 'Wording.exe')"
} finally {
    if (Test-Path -LiteralPath $taskStaging) {
        Remove-Item -LiteralPath $taskStaging -Recurse -Force
    }
}
