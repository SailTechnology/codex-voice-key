param(
    [string]$Hotkey,
    [string]$InstallDirectory,
    [string]$HandyPath,
    [switch]$UseMirror,
    [switch]$SkipBackend,
    [switch]$NoStart,
    [switch]$NoStartup,
    [switch]$NoSkill
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$repoRoot = Split-Path $PSScriptRoot -Parent
$InstallDirectory = Get-VoiceInstallDirectory $InstallDirectory
$systemRoot = [IO.Path]::GetPathRoot($InstallDirectory)
if ($InstallDirectory.TrimEnd('\') -eq $systemRoot.TrimEnd('\')) { throw 'InstallDirectory cannot be a drive root.' }
$executable = Join-Path $repoRoot 'bin\CodexVoiceKey.exe'
if (-not (Test-Path -LiteralPath $executable)) { & (Join-Path $PSScriptRoot 'build.ps1') | Out-Host }
if ($Hotkey) {
    $check = Start-Process -FilePath $executable -ArgumentList @('--validate-hotkey',('"' + $Hotkey + '"'),'--config',('"' + (Join-Path $repoRoot 'config.example.json') + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($check.ExitCode -ne 0) { throw "Unsupported hotkey: $Hotkey" }
}
New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
$configPath = Join-Path $InstallDirectory 'config.json'
$defaults = Get-Content -LiteralPath (Join-Path $repoRoot 'config.example.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$settings = if (Test-Path -LiteralPath $configPath) { Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json } else { $defaults }
foreach ($property in $defaults.PSObject.Properties) {
    if ($null -eq $settings.PSObject.Properties[$property.Name]) {
        $settings | Add-Member -NotePropertyName $property.Name -NotePropertyValue $property.Value
    }
}
if ($Hotkey) { $settings.Hotkey = $Hotkey }
if ($HandyPath) { $settings.HandyPath = [IO.Path]::GetFullPath($HandyPath) }
if (-not $SkipBackend) {
    $backendOutput = @(& (Join-Path $PSScriptRoot 'Initialize-Backend.ps1') -HandyPath $HandyPath -UseMirror:$UseMirror)
    $settings.HandyPath = [string]$backendOutput[-1]
} else { Write-Host 'Backend setup skipped; local Handy and a model are required for recognition.' }

if (Test-Path -LiteralPath $configPath) { Copy-Item -LiteralPath $configPath -Destination ($configPath + '.previous') -Force }
$installedExe = Join-Path $InstallDirectory 'CodexVoiceKey.exe'
[void](Stop-VoiceBridge $InstallDirectory)
if (Test-Path -LiteralPath $installedExe) { Copy-Item -LiteralPath $installedExe -Destination ($installedExe + '.previous') -Force }
Copy-Item -LiteralPath $executable -Destination $installedExe -Force
Write-VoiceJson -Path $configPath -Value $settings
$runtimeScripts = @('Common.ps1','Set-Hotkey.ps1','Doctor.ps1','Uninstall.ps1')
$scriptDirectory = Join-Path $InstallDirectory 'scripts'
New-Item -ItemType Directory -Path $scriptDirectory -Force | Out-Null
foreach ($scriptName in $runtimeScripts) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination (Join-Path $scriptDirectory $scriptName) -Force }

if (-not $NoStartup) {
    $startupDirectory = [Environment]::GetFolderPath('Startup')
    $shellObject = New-Object -ComObject WScript.Shell
    $shortcut = $shellObject.CreateShortcut((Join-Path $startupDirectory 'Codex Voice Key.lnk'))
    $shortcut.TargetPath = $installedExe
    $shortcut.Arguments = '--config "' + $configPath + '"'
    $shortcut.WorkingDirectory = $InstallDirectory
    $shortcut.Save()
}
if (-not $NoSkill) {
    $skillRoot = if ($env:CODEX_HOME) { Join-Path $env:CODEX_HOME 'skills' } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex\skills' }
    $skillDestination = Join-Path $skillRoot 'codex-voice-key'
    New-Item -ItemType Directory -Path $skillDestination -Force | Out-Null
    Copy-Item -Path (Join-Path $repoRoot 'skills\codex-voice-key\*') -Destination $skillDestination -Recurse -Force
}
Write-VoiceJson -Path (Join-Path $InstallDirectory 'installation.json') -Value ([ordered]@{
    version='1.1.0'; installedAt=(Get-Date -Format o); files=@('CodexVoiceKey.exe','installation.json') + @($runtimeScripts | ForEach-Object { 'scripts\' + $_ })
})
if (-not $NoStart) { Write-Host ('Voice bridge running, PID ' + (Start-VoiceBridge $InstallDirectory)) }
Write-Host "Installed: $InstallDirectory"
Write-Host "Hold $($settings.Hotkey) to speak in Codex; release to transcribe."
