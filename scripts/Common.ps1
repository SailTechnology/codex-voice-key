$ErrorActionPreference = 'Stop'

function Get-VoiceInstallDirectory {
    param([string]$InstallDirectory)
    if ([string]::IsNullOrWhiteSpace($InstallDirectory)) { $InstallDirectory = Join-Path $env:LOCALAPPDATA 'CodexVoiceKey' }
    return [IO.Path]::GetFullPath($InstallDirectory)
}

function Write-VoiceJson {
    param([string]$Path, [object]$Value)
    $jsonText = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($Path, $jsonText, (New-Object Text.UTF8Encoding($false)))
}

function Stop-VoiceBridge {
    param([string]$InstallDirectory)
    $executable = Join-Path $InstallDirectory 'CodexVoiceKey.exe'
    $instances = @(Get-CimInstance Win32_Process -Filter "Name='CodexVoiceKey.exe'" | Where-Object { $_.ExecutablePath -eq $executable })
    $audioGuards = @($instances | Where-Object { $_.CommandLine -match '--audio-guard|--restore-audio' })
    foreach ($instance in @($instances | Where-Object { $_.CommandLine -notmatch '--audio-guard|--restore-audio' })) {
        $runningProcess = Get-Process -Id $instance.ProcessId -ErrorAction SilentlyContinue
        if ($runningProcess) { Stop-Process -Id $runningProcess.Id -Force; [void]$runningProcess.WaitForExit(5000) }
    }
    # A dead parent's stdin closes automatically. Let its audio guard restore
    # output states before upgrading/removing the executable it is running.
    foreach ($instance in $audioGuards) {
        $runningProcess = Get-Process -Id $instance.ProcessId -ErrorAction SilentlyContinue
        if ($runningProcess -and -not $runningProcess.WaitForExit(6000)) {
            throw 'Audio restoration is still running. Wait briefly before retrying.'
        }
    }
    return ($instances.Count -gt 0)
}

function Start-VoiceBridge {
    param([string]$InstallDirectory)
    $executable = Join-Path $InstallDirectory 'CodexVoiceKey.exe'
    $configPath = Join-Path $InstallDirectory 'config.json'
    $runningProcess = Start-Process -FilePath $executable -ArgumentList @('--config',('"' + $configPath + '"')) -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 500
    $runningProcess.Refresh()
    if ($runningProcess.HasExited) { throw "Voice bridge exited at startup. Check $InstallDirectory\voice-bridge.log (or another bridge is already running)." }
    return $runningProcess.Id
}

function Get-CheckedDownload {
    param([string]$Uri, [string]$Destination, [string]$Sha256)
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    if (Test-Path -LiteralPath $Destination) {
        if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -eq $Sha256) { return }
    }
    $partial = $Destination + '.partial'
    Write-Host "Downloading $Uri"
    # curl.exe ships with current Windows 10/11 and supports resume without
    # loading the 1.5 GB model into PowerShell memory.
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & $curl.Source --fail --location --retry 3 --connect-timeout 30 --continue-at - --output $partial $Uri
        if ($LASTEXITCODE -ne 0) { throw "Download failed. Rerun to resume: $Uri" }
    } else {
        Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $partial -TimeoutSec 600
    }
    $actual = (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash
    if ($actual -ne $Sha256) { Remove-Item -LiteralPath $partial -Force; throw "SHA-256 mismatch for $Uri" }
    Move-Item -LiteralPath $partial -Destination $Destination -Force
}
