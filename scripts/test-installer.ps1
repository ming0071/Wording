param([string]$Version = '')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (-not $Version) { $Version = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version }
$taskArtifacts = Join-Path $taskRoot 'artifacts'
$taskInstaller = Join-Path $taskArtifacts "Wording-$Version-win-x64-Setup.exe"
$taskProbeRoot = Join-Path $taskArtifacts ('.installer-test-' + [Guid]::NewGuid().ToString('N'))
$taskInstalled = Join-Path $taskProbeRoot 'installed'
$taskData = Join-Path $taskProbeRoot 'personal-data'
$taskFixture = Join-Path $taskProbeRoot 'fixture'
$taskPreviousVersion = '0.4.9'
$taskUninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{FC92DC0A-522D-41CD-88A7-267A81C00AC7}_is1'
if (Test-Path -LiteralPath $taskUninstallKey) { throw 'Run installer verification in a clean Windows account; an installed Wording will not be changed.' }
if (-not (Test-Path -LiteralPath $taskInstaller)) { throw 'Build the installer first.' }
New-Item -ItemType Directory -Path $taskProbeRoot -Force | Out-Null
$taskPython = & (Join-Path $PSScriptRoot 'resolve-python.ps1')
$taskDotnet = & (Join-Path $PSScriptRoot 'resolve-dotnet.ps1')
$taskCompiler = & (Join-Path $PSScriptRoot 'install-inno.ps1')
$taskProbe = Join-Path $PSScriptRoot 'installer-probe\Wording.InstallerProbe.csproj'
$taskVerifier = Join-Path $PSScriptRoot 'verify-package.py'

function Invoke-Probe([string]$Mode) {
    & $taskDotnet run --project $taskProbe -c Release -- $Mode $taskData
    if ($LASTEXITCODE -ne 0) { throw "Personal data probe failed: $Mode" }
}
function Invoke-Setup([string]$Path, [string[]]$Extra = @()) {
    $taskProcess = Start-Process -FilePath $Path -ArgumentList (@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/NOICONS',('/DIR="' + $taskInstalled + '"')) + $Extra) -WindowStyle Hidden -PassThru
    if (-not $taskProcess.WaitForExit(120000)) { throw 'Installer verification timed out.' }
    if ($taskProcess.ExitCode -ne 0) { throw "Installer failed: $($taskProcess.ExitCode)" }
}
function Invoke-InstalledExport {
    $taskArguments = @('--data-dir',('"' + $taskData + '"'),'--export-vocabulary',('"' + (Join-Path $taskProbeRoot 'export.json') + '"'),'--result-file',('"' + (Join-Path $taskProbeRoot 'result.json') + '"'))
    $taskProcess = Start-Process -FilePath (Join-Path $taskInstalled 'Wording.exe') -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
    if (-not $taskProcess.WaitForExit(60000) -or $taskProcess.ExitCode -ne 0) { throw 'Installed application could not read the existing vocabulary.' }
    $taskResult = Get-Content -LiteralPath (Join-Path $taskProbeRoot 'result.json') -Raw | ConvertFrom-Json
    if (-not $taskResult.success -or $taskResult.count -ne 1) { throw 'Installed application did not preserve the single private word.' }
}

try {
    Invoke-Probe 'seed'
    & $taskDotnet publish (Join-Path $taskRoot 'src\Wording.Desktop\Wording.Desktop.csproj') -c Release -r win-x64 --self-contained true "-p:Version=$taskPreviousVersion" -p:PublishTrimmed=false -p:RestoreLockedMode=true -o $taskFixture --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Previous-version fixture publish failed' }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md'),(Join-Path $taskRoot 'THIRD_PARTY_NOTICES.md') -Destination $taskFixture
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs') -Destination $taskFixture -Recurse
    & $taskCompiler /Q "/DAppVersion=$taskPreviousVersion" "/DSourceDir=$taskFixture" "/DOutputDir=$taskProbeRoot" "/DIconFile=$(Join-Path $taskRoot 'src\Wording.Desktop\Assets\Wording.ico')" (Join-Path $PSScriptRoot 'wording.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Previous-version fixture installer failed' }
    Invoke-Setup (Join-Path $taskProbeRoot "Wording-$taskPreviousVersion-win-x64-Setup.exe")
    if ((Get-Item -LiteralPath (Join-Path $taskInstalled 'Wording.dll')).VersionInfo.FileVersion -ne "$taskPreviousVersion.0") { throw 'Previous version did not install.' }
    Invoke-InstalledExport
    Invoke-Probe 'check'
    Invoke-Setup $taskInstaller @('/WORDINGUPDATE=1','/WORDINGRELAUNCH=0',('/WORDINGDATADIR="' + $taskData + '"'))
    if ((Get-Item -LiteralPath (Join-Path $taskInstalled 'Wording.dll')).VersionInfo.FileVersion -ne "$Version.0") { throw 'Installed app version mismatch' }
    & $taskPython $taskVerifier --directory $taskInstalled --version $Version
    if ($LASTEXITCODE -ne 0) { throw 'Installed payload verification failed' }
    Invoke-InstalledExport
    Invoke-Probe 'check'
    $taskFileCount = ((Get-Content -LiteralPath (Join-Path $taskInstalled 'package-manifest.json') -Raw | ConvertFrom-Json).files.PSObject.Properties | Measure-Object).Count
    $taskUninstall = Start-Process -FilePath (Join-Path $taskInstalled 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru
    if (-not $taskUninstall.WaitForExit(120000) -or $taskUninstall.ExitCode -ne 0) { throw 'Uninstall verification failed' }
    if (Test-Path -LiteralPath (Join-Path $taskInstalled 'Wording.exe')) { throw 'Uninstaller did not remove the application.' }
    Invoke-Probe 'check'
    $taskEvidence = [ordered]@{version=$Version;installerSha256=(Get-FileHash -LiteralPath $taskInstaller -Algorithm SHA256).Hash.ToLowerInvariant();installed=$true;upgraded=$true;uninstalled=$true;dataPreserved=$true;filesVerified=$taskFileCount}
    $taskEvidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskArtifacts 'installer-validation.json') -Encoding utf8
    Write-Output "PASS: real install, upgrade, uninstall and personal data preservation ($Version)."
} finally {
    if ((Test-Path -LiteralPath $taskUninstallKey) -and (Test-Path -LiteralPath (Join-Path $taskInstalled 'unins000.exe'))) {
        $taskRegisteredDirectory = (Get-ItemProperty -LiteralPath $taskUninstallKey).'Inno Setup: App Path'
        if ($taskRegisteredDirectory -eq $taskInstalled) {
            $taskCleanup = Start-Process -FilePath (Join-Path $taskInstalled 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru
            $null = $taskCleanup.WaitForExit(120000)
        }
    }
}
