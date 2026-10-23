param([string]$HandyPath, [switch]$UseMirror)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $HandyPath) {
    $candidates = @((Join-Path $env:LOCALAPPDATA 'Handy\handy.exe'), (Join-Path $env:ProgramFiles 'Handy\handy.exe'))
    $HandyPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $HandyPath -or -not (Test-Path -LiteralPath $HandyPath)) {
    $cacheDirectory = Join-Path $env:LOCALAPPDATA 'CodexVoiceKey-Downloads'
    New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
    $setup = Join-Path $cacheDirectory 'Handy_0.9.8_x64-setup.exe'
    Get-CheckedDownload -Uri 'https://github.com/cjpais/Handy/releases/download/v0.9.8/Handy_0.9.8_x64-setup.exe' -Destination $setup -Sha256 '4212edb5658ec2867855fc2ee83ac16ec5bbad09db3c2da8d3d9d5f8bd5eeccc'
    $installation = Start-Process -FilePath $setup -ArgumentList '/S' -WindowStyle Hidden -Wait -PassThru
    if ($installation.ExitCode -ne 0) { throw "Handy installer failed: $($installation.ExitCode)" }
    $HandyPath = Join-Path $env:LOCALAPPDATA 'Handy\handy.exe'
}
if (-not (Test-Path -LiteralPath $HandyPath)) { throw 'Handy was not found. Supply -HandyPath with its executable path.' }
$backendVersion = (Get-Item -LiteralPath $HandyPath).VersionInfo.ProductVersion
if ($backendVersion -and [version]($backendVersion -replace '-.*$','') -lt [version]'0.9.8') {
    throw 'Handy 0.9.8 or newer is required for file transcription. Update Handy and rerun setup.'
}

$modelDirectory = Join-Path $env:APPDATA 'com.pais.handy\models'
New-Item -ItemType Directory -Path $modelDirectory -Force | Out-Null
$modelPath = Join-Path $modelDirectory 'Qwen3-ASR-1.7B-Q5_K_M.gguf'
$domain = if ($UseMirror) { 'https://hf-mirror.com' } else { 'https://huggingface.co' }
$modelUrl = $domain + '/handy-computer/Qwen3-ASR-1.7B-gguf/resolve/92282af1610a2db19d66f2bef1e260f5deca782d/Qwen3-ASR-1.7B-Q5_K_M.gguf'
Get-CheckedDownload -Uri $modelUrl -Destination $modelPath -Sha256 '034c557fe92ff8fcd9a9c041cbdaad347be0a86a58d3a348f63cf3f0180879d0'
Write-Output ([IO.Path]::GetFullPath($HandyPath))
