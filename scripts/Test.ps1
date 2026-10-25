param([switch]$SkipInstallTest)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Common.ps1')
& (Join-Path $PSScriptRoot 'build.ps1') | Out-Host
$testRoot = Join-Path $repoRoot '.test-work'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$testExe = Join-Path $testRoot 'CoreTests.exe'
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 /main:CodexVoiceKey.CoreTests ('/out:' + $testExe) /reference:System.Web.Extensions.dll @sources (Join-Path $repoRoot 'tests\CoreTests.cs') (Join-Path $repoRoot 'tests\ResidentTests.cs') (Join-Path $repoRoot 'tests\AudioMuteTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Core tests did not compile.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
foreach ($scriptFile in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'scripts') -Filter '*.ps1') + @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'skills') -Filter '*.ps1' -Recurse)) {
    $parseTokens = $null; $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName, [ref]$parseTokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw ($scriptFile.Name + ': ' + ($parseErrors | Out-String)) }
}
if (-not $SkipInstallTest) {
    $installPath = Join-Path $testRoot ('install with spaces ' + [Guid]::NewGuid().ToString('N'))
    & (Join-Path $PSScriptRoot 'Install.ps1') -InstallDirectory $installPath -Hotkey 'Ctrl+B' -AllApplications -SkipBackend -NoStart -NoStartup -NoSkill
    & (Join-Path $PSScriptRoot 'Doctor.ps1') -InstallDirectory $installPath | Out-Host
    & (Join-Path $PSScriptRoot 'Set-Hotkey.ps1') -InstallDirectory $installPath -Hotkey 'MouseX1' -NoRestart
    $settings = Get-Content -LiteralPath (Join-Path $installPath 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($settings.Hotkey -ne 'MouseX1') { throw 'Hotkey setting was not saved.' }
    if ($settings.AllApplications -ne $true -or $settings.LivePreview -ne $true) { throw 'Global dictation or live-preview settings were not saved.' }
    # Simulate upgrading a 1.0 configuration that predates resident inference.
    $settings.PSObject.Properties.Remove('KeepModelLoaded')
    $settings.PSObject.Properties.Remove('LivePreview')
    $settings.PSObject.Properties.Remove('MuteOutputWhileRecording')
    Write-VoiceJson -Path (Join-Path $installPath 'config.json') -Value $settings
    & (Join-Path $PSScriptRoot 'Install.ps1') -InstallDirectory $installPath -SkipBackend -NoStart -NoStartup -NoSkill
    $settings = Get-Content -LiteralPath (Join-Path $installPath 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($settings.Hotkey -ne 'MouseX1') { throw 'Upgrade overwrote the existing hotkey.' }
    if ($settings.KeepModelLoaded -ne $true) { throw 'Upgrade did not add the new resident-model default.' }
    if ($settings.LivePreview -ne $true) { throw 'Upgrade did not add live-preview defaults.' }
    if ($settings.MuteOutputWhileRecording -ne $true) { throw 'Upgrade did not add output-mute defaults.' }
    if ($settings.AllApplications -ne $true) { throw 'Upgrade disabled global dictation.' }
    $settings.KeepModelLoaded = $false
    $settings.LivePreview = $false
    $settings.AllApplications = $false
    $settings.MuteOutputWhileRecording = $false
    Write-VoiceJson -Path (Join-Path $installPath 'config.json') -Value $settings
    & (Join-Path $PSScriptRoot 'Install.ps1') -InstallDirectory $installPath -SkipBackend -NoStart -NoStartup -NoSkill
    $settings = Get-Content -LiteralPath (Join-Path $installPath 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($settings.KeepModelLoaded -ne $false) { throw 'Upgrade reenabled an explicitly disabled resident model.' }
    if ($settings.LivePreview -ne $false -or $settings.AllApplications -ne $false) { throw 'Upgrade overwrote explicitly disabled preview or global scope.' }
    if ($settings.MuteOutputWhileRecording -ne $false) { throw 'Upgrade reenabled explicitly disabled output muting.' }
    # Exercise the login task with a unique install path; do not run it or alter
    # another installation's Startup shortcut. Uninstall must remove this task.
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Voice Key.lnk'
    $shortcutBefore = if (Test-Path -LiteralPath $shortcutPath) { (Get-FileHash -LiteralPath $shortcutPath).Hash } else { $null }
    $taskName = Get-VoiceStartupTaskName $installPath
    try {
        & (Join-Path $PSScriptRoot 'Install.ps1') -InstallDirectory $installPath -SkipBackend -NoStart -NoSkill
        $task = Get-ScheduledTask -TaskName $taskName
        if ($task.Actions[0].Execute -ne (Join-Path $installPath 'CodexVoiceKey.exe') -or
            $task.Actions[0].Arguments -ne ('--config "' + (Join-Path $installPath 'config.json') + '"')) { throw 'Login task does not point to its own executable/configuration.' }
        if ($task.Principal.RunLevel -ne 'Limited' -or $task.Triggers.Count -ne 1) { throw 'Login task must use normal user privileges and a single trigger.' }
        $shortcutAfter = if (Test-Path -LiteralPath $shortcutPath) { (Get-FileHash -LiteralPath $shortcutPath).Hash } else { $null }
        if ($shortcutBefore -ne $shortcutAfter) { throw "Installation changed another installation's Startup shortcut." }
    } finally { & (Join-Path $PSScriptRoot 'Uninstall.ps1') -InstallDirectory $installPath }
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { throw 'Uninstall left the login task.' }
    if (Test-Path -LiteralPath (Join-Path $installPath 'CodexVoiceKey.exe')) { throw 'Uninstall left the executable.' }
    if (-not (Test-Path -LiteralPath (Join-Path $installPath 'config.json'))) { throw 'Uninstall removed user configuration.' }
    Write-Output 'PASS: install, diagnosis, rebind, upgrade, login-task ownership and uninstall in an isolated directory.'
}
