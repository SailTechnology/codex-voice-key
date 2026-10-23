param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'bin'))
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows x64 with .NET Framework 4.x is required.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$buildArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',
    ('/win32manifest:' + (Join-Path $repoRoot 'src\app.manifest')),
    ('/out:' + (Join-Path $OutputDirectory 'CodexVoiceKey.exe')),
    '/reference:System.Web.Extensions.dll',
    ('/resource:' + (Join-Path $repoRoot 'assets\mudkip-doctor-states.png') + ',CodexVoiceKey.MudkipStates.png'))
& $compiler @buildArgs @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output (Join-Path $OutputDirectory 'CodexVoiceKey.exe')
