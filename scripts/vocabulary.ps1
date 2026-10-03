param(
    [Parameter(Mandatory = $true)][ValidateSet('import', 'export')][string]$Action,
    [Parameter(Mandatory = $true)][string]$File,
    [string]$DataDirectory,
    [string]$Executable = (Join-Path $PSScriptRoot '../src/WordTrail.Desktop/bin/Release/net10.0-windows/WordTrail.exe')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Executable)) { throw '找不到 WordTrail.exe，請先編譯或用 -Executable 指定執行檔。' }
$resultPath = Join-Path ([IO.Path]::GetTempPath()) ('wordtrail-result-' + [guid]::NewGuid().ToString('N') + '.json')
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = [IO.Path]::GetFullPath($Executable)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
# Quote Windows argv paths; paths cannot contain a double quote, and full paths do not end in a separator here.
$filePath = [IO.Path]::GetFullPath($File)
$start.Arguments = '--' + $Action + '-vocabulary "' + $filePath + '" --result-file "' + $resultPath + '"'
if ($DataDirectory) {
    $dataPath = [IO.Path]::GetFullPath($DataDirectory).TrimEnd('\', '/')
    $start.Arguments += ' --data-dir "' + $dataPath + '"'
}
try {
    $process = [System.Diagnostics.Process]::Start($start)
    $process.WaitForExit()
    if (-not (Test-Path -LiteralPath $resultPath)) { throw ('WordTrail 未回傳結果，結束代碼：' + $process.ExitCode) }
    $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $result.success) { throw $result.message }
    Write-Output $result.message
} finally {
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
    if ($process) { $process.Dispose() }
}
