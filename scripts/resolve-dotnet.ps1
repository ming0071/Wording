$taskDotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($taskDotnetCommand) { return $taskDotnetCommand.Source }
$taskDotnetPath = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'
if (Test-Path -LiteralPath $taskDotnetPath) { return $taskDotnetPath }
throw '找不到 .NET 10 SDK。請安裝 SDK，重新開啟 PowerShell 後再執行。'
