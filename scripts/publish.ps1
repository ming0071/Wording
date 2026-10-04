param([string]$Version = '', [switch]$LockedRestore)
$ErrorActionPreference = 'Stop'
$taskDotnet = & (Join-Path $PSScriptRoot 'resolve-dotnet.ps1')
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    if (-not $Version) { $Version = ([xml](Get-Content -LiteralPath Directory.Build.props -Raw)).Project.PropertyGroup.Version }
    if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Version must be a semantic version, such as 0.3.0.' }
    $taskOutput = Join-Path (Get-Location) "artifacts/Wording-$Version-win-x64"
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
} finally { Pop-Location }
