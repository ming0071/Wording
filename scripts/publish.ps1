param([string]$Version = '0.3.0')
$ErrorActionPreference = 'Stop'
$taskDotnet = & (Join-Path $PSScriptRoot 'resolve-dotnet.ps1')
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $taskOutput = Join-Path (Get-Location) "artifacts/Wording-$Version-win-x64"
    & $taskDotnet publish src/Wording.Desktop/Wording.Desktop.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -p:PublishTrimmed=false -o $taskOutput
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item -LiteralPath README.md,THIRD_PARTY_NOTICES.md -Destination $taskOutput
    Copy-Item -LiteralPath docs -Destination $taskOutput -Recurse -Force
    $taskZip = Join-Path (Get-Location) "artifacts/Wording-$Version-win-x64.zip"
    Compress-Archive -Path "$taskOutput/*" -DestinationPath $taskZip -Force
    $taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$taskHash  $([IO.Path]::GetFileName($taskZip))" | Set-Content -LiteralPath "$taskZip.sha256" -Encoding ascii
} finally { Pop-Location }
