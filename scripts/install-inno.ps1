param([string]$Destination = '')
$ErrorActionPreference = 'Stop'
if (-not $Destination) { $Destination = Join-Path (Split-Path $PSScriptRoot -Parent) '.tools\innosetup' }
$taskCompiler = Join-Path $Destination 'ISCC.exe'
if (Test-Path -LiteralPath $taskCompiler) { return $taskCompiler }
$taskDownload = Join-Path ([IO.Path]::GetTempPath()) ('wording-inno-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' -OutFile $taskDownload
    if ((Get-FileHash -LiteralPath $taskDownload -Algorithm SHA256).Hash.ToLowerInvariant() -ne '8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0') {
        throw 'Official Inno Setup download checksum mismatch.'
    }
    $taskInstall = Start-Process -FilePath $taskDownload -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/CURRENTUSER',('/DIR="' + $Destination + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($taskInstall.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $taskCompiler)) { throw 'Inno Setup compiler installation failed.' }
    return $taskCompiler
} finally { if (Test-Path -LiteralPath $taskDownload) { Remove-Item -LiteralPath $taskDownload } }
