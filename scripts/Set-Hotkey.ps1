param([Parameter(Mandatory=$true)][string]$Hotkey, [string]$InstallDirectory, [switch]$NoRestart)
. (Join-Path $PSScriptRoot 'Common.ps1')
$InstallDirectory = Get-VoiceInstallDirectory $InstallDirectory
$configPath = Join-Path $InstallDirectory 'config.json'
if (-not (Test-Path -LiteralPath $configPath)) { throw 'Install Codex Voice Key first.' }
$executable = Join-Path $InstallDirectory 'CodexVoiceKey.exe'
$check = Start-Process -FilePath $executable -ArgumentList @('--validate-hotkey',('"' + $Hotkey + '"'),'--config',('"' + $configPath + '"')) -WindowStyle Hidden -Wait -PassThru
if ($check.ExitCode -ne 0) { throw "Unsupported hotkey: $Hotkey" }
$settings = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
$settings.Hotkey = $Hotkey
Copy-Item -LiteralPath $configPath -Destination ($configPath + '.previous') -Force
Write-VoiceJson -Path $configPath -Value $settings
if (-not $NoRestart) {
    $wasRunning = Stop-VoiceBridge $InstallDirectory
    if ($wasRunning) { [void](Start-VoiceBridge $InstallDirectory) }
}
Write-Host "Voice key: $Hotkey"
