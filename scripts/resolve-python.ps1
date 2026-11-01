$taskPythonCommand = Get-Command python -ErrorAction SilentlyContinue
if ($taskPythonCommand -and $taskPythonCommand.Source -notmatch 'WindowsApps') { return $taskPythonCommand.Source }
$taskBundledPython = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
if (Test-Path -LiteralPath $taskBundledPython) { return $taskBundledPython }
throw 'Python 3.12 or later is required for package validation.'
