param([string]$InstallDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$InstallDirectory = Get-VoiceInstallDirectory $InstallDirectory
$reportPath = Join-Path $InstallDirectory 'diagnostics.json'
$process = Start-Process -FilePath (Join-Path $InstallDirectory 'CodexVoiceKey.exe') -ArgumentList @('--config',('"' + (Join-Path $InstallDirectory 'config.json') + '"'),'--diagnose',('"' + $reportPath + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Diagnosis failed. Check $InstallDirectory\voice-bridge.log" }
$report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
$executable = Join-Path $InstallDirectory 'CodexVoiceKey.exe'
$configPath = Join-Path $InstallDirectory 'config.json'
$physicalExecutable = Get-VoicePhysicalFile $executable
$physicalConfig = Get-VoicePhysicalFile $configPath
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Voice Key.lnk'
$startup = $null
if (Test-Path -LiteralPath $shortcutPath) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $startup = [ordered]@{
        target=$shortcut.TargetPath; arguments=$shortcut.Arguments
        matchesInstallation=($shortcut.TargetPath -eq $physicalExecutable -and $shortcut.Arguments -eq ('--config "' + $physicalConfig + '"'))
    }
}
$configArgument = '--config "' + $configPath + '"'
$taskName = Get-VoiceStartupTaskName $InstallDirectory
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($task) {
    $startup = [ordered]@{
        kind='logonTask'; taskName=$taskName; enabled=[bool]$task.Settings.Enabled
        target=$task.Actions[0].Execute; arguments=$task.Actions[0].Arguments
        matchesInstallation=($task.Actions[0].Execute -eq $physicalExecutable -and $task.Actions[0].Arguments -eq $configArgument)
    }
}
$instances = @(Get-CimInstance Win32_Process -Filter "Name='CodexVoiceKey.exe'" | Where-Object {
    $_.CommandLine -notmatch '--asr-worker|--audio-guard|--restore-audio|--diagnose' -and
    ($_.ExecutablePath -eq $physicalExecutable -or ($_.CommandLine -and $_.CommandLine.IndexOf($configArgument, [StringComparison]::OrdinalIgnoreCase) -ge 0))
} | Select-Object ProcessId,ExecutablePath,CommandLine)
$report | Add-Member -NotePropertyName installationContext -NotePropertyValue ([ordered]@{
    redirectedFileView=(Test-VoiceVirtualizedContext); physicalExecutable=$physicalExecutable; physicalConfig=$physicalConfig
    shadowedInstallation=($physicalExecutable -ne $executable -or $physicalConfig -ne $configPath)
    startup=$startup; runningProcesses=$instances
})
$report | ConvertTo-Json -Depth 6
Write-Host 'No recording, keystrokes or transcript content is included in this report.'
