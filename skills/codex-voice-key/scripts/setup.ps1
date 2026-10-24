param(
    [string]$Hotkey,
    [string]$InstallDirectory,
    [string]$HandyPath,
    [switch]$AllApplications,
    [string]$ReleaseTag = 'latest',
    [switch]$UseMirror,
    [switch]$SkipBackend,
    [switch]$NoStart,
    [switch]$NoStartup,
    [switch]$NoSkill
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitOperatingSystem) { throw 'This release requires Windows x64.' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$api = 'https://api.github.com/repos/SailTechnology/codex-voice-key/releases/'
$api += if ($ReleaseTag -eq 'latest') { 'latest' } else { 'tags/' + [Uri]::EscapeDataString($ReleaseTag) }
$headers = @{ 'User-Agent'='CodexVoiceKey-Setup'; Accept='application/vnd.github+json' }
$release = Invoke-RestMethod -Uri $api -Headers $headers
$archiveName = 'codex-voice-key-windows-x64.zip'
$archiveAsset = $release.assets | Where-Object { $_.name -eq $archiveName } | Select-Object -First 1
$checksumAsset = $release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' } | Select-Object -First 1
if (-not $archiveAsset -or -not $checksumAsset) { throw 'Release assets are incomplete. Download from the repository Releases page.' }
$workDirectory = Join-Path ([IO.Path]::GetTempPath()) ('CodexVoiceKey-setup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workDirectory | Out-Null
$archivePath = Join-Path $workDirectory $archiveName
$checksumPath = Join-Path $workDirectory 'SHA256SUMS.txt'
Invoke-WebRequest -UseBasicParsing -Uri $archiveAsset.browser_download_url -OutFile $archivePath
Invoke-WebRequest -UseBasicParsing -Uri $checksumAsset.browser_download_url -OutFile $checksumPath
$expectedHash = $null
foreach ($checksumLine in Get-Content -LiteralPath $checksumPath) {
    if ($checksumLine -match '^([a-fA-F0-9]{64})\s+\*?codex-voice-key-windows-x64\.zip$') { $expectedHash = $Matches[1] }
}
if (-not $expectedHash -or (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Release checksum mismatch.' }
$extracted = Join-Path $workDirectory 'extracted'
Expand-Archive -LiteralPath $archivePath -DestinationPath $extracted
$installer = Join-Path $extracted 'CodexVoiceKey\scripts\Install.ps1'
if (-not (Test-Path -LiteralPath $installer)) { throw 'Invalid release package structure.' }
$installOptions = @{ SkipBackend=$SkipBackend; NoStart=$NoStart; NoStartup=$NoStartup; NoSkill=$NoSkill; UseMirror=$UseMirror }
if ($Hotkey) { $installOptions.Hotkey = $Hotkey }
if ($InstallDirectory) { $installOptions.InstallDirectory = $InstallDirectory }
if ($HandyPath) { $installOptions.HandyPath = $HandyPath }
if ($PSBoundParameters.ContainsKey('AllApplications')) { $installOptions.AllApplications = $AllApplications }
& $installer @installOptions
Write-Host ('Release installed: ' + $release.tag_name)
