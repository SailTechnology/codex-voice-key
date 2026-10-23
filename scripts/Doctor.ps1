param([string]$InstallDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$InstallDirectory = Get-VoiceInstallDirectory $InstallDirectory
$reportPath = Join-Path $InstallDirectory 'diagnostics.json'
$process = Start-Process -FilePath (Join-Path $InstallDirectory 'CodexVoiceKey.exe') -ArgumentList @('--config',('"' + (Join-Path $InstallDirectory 'config.json') + '"'),'--diagnose',('"' + $reportPath + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Diagnosis failed. Check $InstallDirectory\voice-bridge.log" }
Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8
Write-Host 'No recording, keystrokes or transcript content is included in this report.'
