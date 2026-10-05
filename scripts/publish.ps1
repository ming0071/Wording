param([string]$Version = '', [switch]$LockedRestore)
$ErrorActionPreference = 'Stop'
$taskDotnet = & (Join-Path $PSScriptRoot 'resolve-dotnet.ps1')
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    if (-not $Version) { $Version = ([xml](Get-Content -LiteralPath Directory.Build.props -Raw)).Project.PropertyGroup.Version }
    if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Version must be a semantic version, such as 0.3.0.' }
    # Check the pinned copy before doing any work, and again immediately before replacing it.
    & (Join-Path $PSScriptRoot 'update-current.ps1') -CheckOnly
    $taskOutput = Join-Path (Get-Location) "artifacts/Wording-$Version-win-x64"
    # Publish into an empty directory so removed assemblies or documents cannot survive in a release.
    if (Test-Path -LiteralPath $taskOutput) {
        $taskArtifactsRoot = [IO.Path]::GetFullPath((Join-Path (Get-Location) 'artifacts')) + [IO.Path]::DirectorySeparatorChar
        $taskResolvedOutput = (Resolve-Path -LiteralPath $taskOutput).Path
        if (-not $taskResolvedOutput.StartsWith($taskArtifactsRoot, [StringComparison]::OrdinalIgnoreCase) -or
            (Get-Item -LiteralPath $taskOutput).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            throw 'Publish output must be a normal directory inside workspace artifacts.'
        }
        $taskRunningApp = Get-Process Wording -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -and $_.Path.StartsWith($taskResolvedOutput + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
        }
        if ($taskRunningApp) { throw 'The app is running from this publish directory; close it before rebuilding this package.' }
        Remove-Item -LiteralPath $taskResolvedOutput -Recurse -Force
    }
    $taskPublishArguments = @('publish', 'src/Wording.Desktop/Wording.Desktop.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', "-p:Version=$Version", '-p:PublishTrimmed=false', '-o', $taskOutput)
    if ($LockedRestore) { $taskPublishArguments += '-p:RestoreLockedMode=true' }
    & $taskDotnet @taskPublishArguments
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item -LiteralPath README.md,THIRD_PARTY_NOTICES.md -Destination $taskOutput
    Copy-Item -LiteralPath docs -Destination $taskOutput -Recurse -Force
    $taskZip = Join-Path (Get-Location) "artifacts/Wording-$Version-win-x64.zip"
    Compress-Archive -Path "$taskOutput/*" -DestinationPath $taskZip -Force
    $taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$taskHash  $([IO.Path]::GetFileName($taskZip))" | Set-Content -LiteralPath "$taskZip.sha256" -Encoding ascii
    & (Join-Path $PSScriptRoot 'update-current.ps1') -PackageDirectory $taskOutput
} finally { Pop-Location }
