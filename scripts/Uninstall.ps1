param([string]$InstallDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$InstallDirectory = Get-VoiceInstallDirectory $InstallDirectory
$marker = Join-Path $InstallDirectory 'installation.json'
if (-not (Test-Path -LiteralPath $marker)) { throw 'Installation marker missing; refusing to remove untracked files.' }
$manifest = Get-Content -LiteralPath $marker -Raw -Encoding UTF8 | ConvertFrom-Json
$ownedFiles = @()
foreach ($relativePath in $manifest.files) {
    $resolved = [IO.Path]::GetFullPath((Join-Path $InstallDirectory $relativePath))
    if (-not $resolved.StartsWith($InstallDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid path in installation manifest.' }
    $ownedFiles += $resolved
}
[void](Stop-VoiceBridge $InstallDirectory)
$executable = Join-Path $InstallDirectory 'CodexVoiceKey.exe'
$taskName = Get-VoiceStartupTaskName $InstallDirectory
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($task -and @($task.Actions | Where-Object { $_.Execute -eq $executable }).Count -gt 0) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Voice Key.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    $shellObject = New-Object -ComObject WScript.Shell
    $shortcut = $shellObject.CreateShortcut($shortcutPath)
    if ($shortcut.TargetPath -eq $executable) { Remove-Item -LiteralPath $shortcutPath -Force }
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValue = $null
if (Test-Path -LiteralPath $runKey) {
    $runProperties = Get-ItemProperty -LiteralPath $runKey
    $runProperty = $runProperties.PSObject.Properties['CodexVoiceKey']
    if ($runProperty) { $runValue = $runProperty.Value }
}
if ($runValue -and $runValue.Contains($executable)) { Remove-ItemProperty -LiteralPath $runKey -Name 'CodexVoiceKey' }
foreach ($ownedFile in $ownedFiles) { if (Test-Path -LiteralPath $ownedFile) { Remove-Item -LiteralPath $ownedFile -Force } }
Write-Host "Removed Codex Voice Key. Configuration, logs, model files and the reusable skill are preserved in $InstallDirectory."
