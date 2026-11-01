param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskRoot 'artifacts\Wording-reading-prototype'
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$taskRunning = Get-Process Wording -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.StartsWith($taskOutput + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
if ($taskRunning) { throw '請先關閉朗讀原型，再重新建置。' }
$taskDotnet = & (Join-Path $PSScriptRoot 'resolve-dotnet.ps1')
& $taskDotnet publish (Join-Path $taskRoot 'src\Wording.Desktop\Wording.Desktop.csproj') -c Release -r win-x64 --self-contained true `
    -p:RestoreLockedMode=true -p:UseSharedCompilation=false "-p:InformationalVersion=$taskVersion-reading-prototype" -o $taskOutput
if ($LASTEXITCODE -ne 0) { throw '朗讀原型建置失敗。' }
Write-Output "Reading prototype: $(Join-Path $taskOutput 'Wording.exe')"
Write-Output '固定入口與版本號未修改。關閉原本 Wording 後，可自行開啟這份原型。'
