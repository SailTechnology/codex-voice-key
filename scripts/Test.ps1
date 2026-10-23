param([switch]$SkipInstallTest)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1') | Out-Host
$testRoot = Join-Path $repoRoot '.test-work'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$testExe = Join-Path $testRoot 'CoreTests.exe'
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 /main:CodexVoiceKey.CoreTests ('/out:' + $testExe) /reference:System.Web.Extensions.dll @sources (Join-Path $repoRoot 'tests\CoreTests.cs')
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
    & (Join-Path $PSScriptRoot 'Install.ps1') -InstallDirectory $installPath -Hotkey 'Ctrl+B' -SkipBackend -NoStart -NoStartup -NoSkill
    & (Join-Path $PSScriptRoot 'Doctor.ps1') -InstallDirectory $installPath | Out-Host
    & (Join-Path $PSScriptRoot 'Set-Hotkey.ps1') -InstallDirectory $installPath -Hotkey 'MouseX1' -NoRestart
    $settings = Get-Content -LiteralPath (Join-Path $installPath 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($settings.Hotkey -ne 'MouseX1') { throw 'Hotkey setting was not saved.' }
    & (Join-Path $PSScriptRoot 'Install.ps1') -InstallDirectory $installPath -SkipBackend -NoStart -NoStartup -NoSkill
    $settings = Get-Content -LiteralPath (Join-Path $installPath 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($settings.Hotkey -ne 'MouseX1') { throw 'Upgrade overwrote the existing hotkey.' }
    & (Join-Path $PSScriptRoot 'Uninstall.ps1') -InstallDirectory $installPath
    if (Test-Path -LiteralPath (Join-Path $installPath 'CodexVoiceKey.exe')) { throw 'Uninstall left the executable.' }
    if (-not (Test-Path -LiteralPath (Join-Path $installPath 'config.json'))) { throw 'Uninstall removed user configuration.' }
    Write-Output 'PASS: install, diagnosis, rebind, upgrade and uninstall in an isolated directory.'
}
