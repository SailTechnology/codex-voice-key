param([Parameter(Mandatory=$true)][string]$JobPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
$jobDirectory = Split-Path $JobPath -Parent
$resultPath = Join-Path $jobDirectory 'result.json'
try {
    if (Test-VoiceVirtualizedContext) { throw 'The installer still has redirected AppData access.' }
    $job = Get-Content -LiteralPath $JobPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $migration = $job.Migration
    foreach ($entry in @($migration.Files)) {
        # Reuse existing private files without downloading a second model.
        # Never replace an already installed, independent shared dependency.
        if (Test-Path -LiteralPath $entry.Destination) { continue }
        New-Item -ItemType Directory -Path (Split-Path $entry.Destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Recurse:([bool]$entry.Directory)
    }
    $configPath = Join-Path $migration.InstallDirectory 'config.json'
    if ($migration.Configuration -and ($migration.PreferConfiguration -or -not (Test-Path -LiteralPath $configPath))) {
        New-Item -ItemType Directory -Path $migration.InstallDirectory -Force | Out-Null
        if (Test-Path -LiteralPath $configPath) {
            Copy-Item -LiteralPath $configPath -Destination ($configPath + '.before-private-migration-' + (Split-Path $jobDirectory -Leaf))
        }
        Write-VoiceJson -Path $configPath -Value $migration.Configuration
    }
    $options = @{}
    foreach ($property in $job.Options.PSObject.Properties) { $options[$property.Name] = $property.Value }
    & $job.Installer @options *>&1 | Out-File -LiteralPath (Join-Path $jobDirectory 'install.log') -Encoding UTF8
    Write-VoiceJson -Path ($resultPath + '.tmp') -Value @{ Success=$true; InstallDirectory=(Get-VoiceInstallDirectory $options.InstallDirectory) }
} catch {
    $_ | Out-String | Out-File -LiteralPath (Join-Path $jobDirectory 'install.log') -Encoding UTF8 -Append
    Write-VoiceJson -Path ($resultPath + '.tmp') -Value @{ Success=$false; Error=$_.Exception.Message }
}
Move-Item -LiteralPath ($resultPath + '.tmp') -Destination $resultPath -Force
