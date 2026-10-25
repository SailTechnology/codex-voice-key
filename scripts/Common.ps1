$ErrorActionPreference = 'Stop'

function Initialize-VoicePathApi {
    if ('CodexVoiceKey.InstallContext' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace CodexVoiceKey {
    public static class InstallContext {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern uint GetFinalPathNameByHandle(IntPtr handle, StringBuilder path, uint length, uint flags);
        public static string PhysicalFile(string path) {
            using (FileStream stream=new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                StringBuilder name=new StringBuilder(32768);
                uint size=GetFinalPathNameByHandle(stream.SafeFileHandle.DangerousGetHandle(), name, (uint)name.Capacity, 0);
                if(size==0 || size>=name.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
                string value=name.ToString();
                if(value.StartsWith(@"\\?\UNC\")) return @"\\"+value.Substring(8);
                return value.StartsWith(@"\\?\") ? value.Substring(4) : value;
            }
        }
    }
}
'@
}

function Get-VoicePhysicalFile {
    param([string]$Path)
    Initialize-VoicePathApi
    return [CodexVoiceKey.InstallContext]::PhysicalFile([IO.Path]::GetFullPath($Path))
}

function Test-VoiceVirtualizedContext {
    # Child processes can have redirected file access without package identity.
    # Test the actual file view rather than GetCurrentPackageFullName alone.
    $probePath = Join-Path $env:LOCALAPPDATA ('CodexVoiceKey-context-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($probePath, '')
        return (Get-VoicePhysicalFile $probePath) -ne [IO.Path]::GetFullPath($probePath)
    } finally { if (Test-Path -LiteralPath $probePath) { Remove-Item -LiteralPath $probePath -Force } }
}

function Invoke-VoiceUnpackagedInstall {
    param([string]$Installer, [hashtable]$Options)
    Initialize-VoicePathApi
    if (-not (Test-VoiceVirtualizedContext)) { return $false }
    # MSIX children inherit AppData virtualization. Login processes do not.
    # Run the installer once in the user's normal interactive context, without
    # elevation, and reuse any existing backend through explicit physical paths.
    $jobId = [Guid]::NewGuid().ToString('N')
    $jobDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) ('.codex\voice-key-install-jobs\' + $jobId)
    New-Item -ItemType Directory -Path $jobDirectory -Force | Out-Null
    $installDirectory = Get-VoiceInstallDirectory $Options.InstallDirectory
    $Options.InstallDirectory = $installDirectory
    if ($Options.HandyPath) { $Options.HandyPath = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Options.HandyPath)) }
    $existingConfig = Join-Path $installDirectory 'config.json'
    $settings = if (Test-Path -LiteralPath $existingConfig) { Get-Content -LiteralPath $existingConfig -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
    $privateInstall = if ($settings) { Split-Path (Get-VoicePhysicalFile $existingConfig) -Parent } else { $null }
    $migration = [ordered]@{ InstallDirectory=$installDirectory; Configuration=$settings; PreferConfiguration=($privateInstall -and $privateInstall -ne $installDirectory); Files=@() }
    if (-not $Options.SkipBackend) {
        $handyCandidate = if ($Options.HandyPath) { $Options.HandyPath } elseif ($settings -and $settings.HandyPath) { $settings.HandyPath } else { Join-Path $env:LOCALAPPDATA 'Handy\handy.exe' }
        $handyCandidate = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($handyCandidate))
        if (Test-Path -LiteralPath $handyCandidate) {
            $physicalHandy = Get-VoicePhysicalFile $handyCandidate
            if ($physicalHandy -ne $handyCandidate) {
                $migration.Files += @{ Source=(Split-Path $physicalHandy -Parent); Destination=(Split-Path $handyCandidate -Parent); Directory=$true }
            }
        }
        $modelFile = if ($settings -and $settings.ModelId) { [IO.Path]::GetFileName($settings.ModelId) } else { 'Qwen3-ASR-1.7B-Q5_K_M.gguf' }
        $modelCandidate = Join-Path $env:APPDATA ('com.pais.handy\models\' + $modelFile)
        if (Test-Path -LiteralPath $modelCandidate) {
            $physicalModel = Get-VoicePhysicalFile $modelCandidate
            if ($physicalModel -ne $modelCandidate) { $migration.Files += @{ Source=$physicalModel; Destination=$modelCandidate; Directory=$false } }
        }
    }
    $jobPath = Join-Path $jobDirectory 'job.json'
    Write-VoiceJson -Path $jobPath -Value @{ Installer=(Get-VoicePhysicalFile $Installer); Options=$Options; Migration=$migration }
    $workerScript = Get-VoicePhysicalFile (Join-Path $PSScriptRoot 'Install-Unpackaged.ps1')
    $taskName = 'CodexVoiceKey-Install-' + $jobId
    $principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
    $action = New-ScheduledTaskAction -Execute (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $workerScript + '" -JobPath "' + $jobPath + '"')
    $taskSettings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 60) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    Write-Host 'Installing outside the MSIX app context so login uses the same configuration and model.'
    try {
        Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $taskSettings | Out-Null
        Start-ScheduledTask -TaskName $taskName
        $resultPath = Join-Path $jobDirectory 'result.json'
        $wait = [Diagnostics.Stopwatch]::StartNew()
        while (-not (Test-Path -LiteralPath $resultPath)) {
            if ($wait.Elapsed.TotalMinutes -gt 60) { throw "Installation timed out. Check $jobDirectory\install.log" }
            Start-Sleep -Milliseconds 500
            $taskInfo = Get-ScheduledTaskInfo -TaskName $taskName
            if ($taskInfo.LastRunTime.Year -gt 2000 -and $taskInfo.LastTaskResult -notin @(0,267009,267011)) {
                throw "Unpackaged installer failed ($($taskInfo.LastTaskResult)). Check $jobDirectory\install.log"
            }
        }
        $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $result.Success) { throw "Unpackaged installation failed: $($result.Error). Check $jobDirectory\install.log" }
        if ($privateInstall -and $privateInstall -ne $installDirectory) {
            # Retire the shadow config only after the independent installation
            # succeeded. Keep every original file in a recoverable sibling.
            $packageRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Packages')).TrimEnd('\') + '\'
            $source = [IO.Path]::GetFullPath($privateInstall).TrimEnd('\')
            $destination = $source + '.before-shared-install-' + $jobId
            if (-not $source.StartsWith($packageRoot, [StringComparison]::OrdinalIgnoreCase) -or
                -not $destination.StartsWith($packageRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Unexpected private installation path; leaving the original files in place.'
            }
            Move-Item -LiteralPath $source -Destination $destination
            Write-Host "Original private installation backed up: $destination"
        }
        Write-Host "Installed: $($result.InstallDirectory). Details: $jobDirectory\install.log"
    } finally { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue }
    return $true
}

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

function Get-VoiceStartupTaskName {
    param([string]$InstallDirectory)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $digest = $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes(([IO.Path]::GetFullPath($InstallDirectory)).TrimEnd('\').ToLowerInvariant())) }
    finally { $hasher.Dispose() }
    return 'CodexVoiceKey-' + ([BitConverter]::ToString($digest).Replace('-','').Substring(0,12).ToLowerInvariant())
}

function Stop-VoiceBridge {
    param([string]$InstallDirectory)
    $executable = Join-Path $InstallDirectory 'CodexVoiceKey.exe'
    $physicalExe = if (Test-Path -LiteralPath $executable) { Get-VoicePhysicalFile $executable } else { $executable }
    $configArgument = '--config "' + (Join-Path $InstallDirectory 'config.json') + '"'
    $instances = @(Get-CimInstance Win32_Process -Filter "Name='CodexVoiceKey.exe'" | Where-Object {
        $_.ExecutablePath -eq $executable -or $_.ExecutablePath -eq $physicalExe -or
        ($_.CommandLine -and $_.CommandLine.IndexOf($configArgument, [StringComparison]::OrdinalIgnoreCase) -ge 0)
    })
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
    $taskName = Get-VoiceStartupTaskName $InstallDirectory
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($task -and $task.Actions[0].Execute -eq $executable -and $task.Actions[0].Arguments -eq ('--config "' + $configPath + '"')) {
        # Start through the permanent task, whose lifetime is unlimited. Do not
        # leave the bridge attached to a temporary install task's time limit.
        Start-ScheduledTask -TaskName $taskName
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            Start-Sleep -Milliseconds 250
            $instance = Get-CimInstance Win32_Process -Filter "Name='CodexVoiceKey.exe'" | Where-Object {
                $_.ExecutablePath -eq $executable -and $_.CommandLine -notmatch '--asr-worker|--audio-guard|--restore-audio|--diagnose'
            } | Select-Object -First 1
            if ($instance) { return $instance.ProcessId }
        }
        throw "Login task did not start the voice bridge. Check $InstallDirectory\voice-bridge.log"
    }
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
