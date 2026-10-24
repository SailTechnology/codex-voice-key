param([string]$Version = '1.3.0')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'build.ps1') | Out-Host
$distribution = Join-Path $repoRoot 'dist'
$staging = [IO.Path]::GetFullPath((Join-Path $distribution 'staging'))
if (-not $staging.StartsWith([IO.Path]::GetFullPath($distribution).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging directory.' }
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
$appStage = Join-Path $staging 'CodexVoiceKey'
New-Item -ItemType Directory -Path $appStage -Force | Out-Null
foreach ($folder in @('bin','src','assets','scripts','skills','docs','tests')) { Copy-Item -LiteralPath (Join-Path $repoRoot $folder) -Destination (Join-Path $appStage $folder) -Recurse }
foreach ($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','config.example.json','Install.cmd')) { Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination (Join-Path $appStage $file) }
[IO.File]::WriteAllText((Join-Path $appStage 'VERSION'), $Version)
$appZip = Join-Path $distribution 'codex-voice-key-windows-x64.zip'
$skillZip = Join-Path $distribution 'codex-voice-key-skill.zip'
Compress-Archive -LiteralPath $appStage -DestinationPath $appZip -Force
Compress-Archive -LiteralPath (Join-Path $repoRoot 'skills\codex-voice-key') -DestinationPath $skillZip -Force
$checksumLines = foreach ($archive in @($appZip,$skillZip)) { (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive) }
[IO.File]::WriteAllLines((Join-Path $distribution 'SHA256SUMS.txt'), $checksumLines, (New-Object Text.UTF8Encoding($false)))
Get-Item -LiteralPath $appZip,$skillZip,(Join-Path $distribution 'SHA256SUMS.txt') | Select-Object Name,Length
