$ErrorActionPreference = 'Stop'

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return [Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Stop-RunningWidget($Processes) {
    foreach ($widget in $Processes) {
        try {
            if ($widget.HasExited) { continue }
            if ($widget.CloseMainWindow() -and $widget.WaitForExit(5000)) { continue }
            if (-not $widget.HasExited) { $widget.Kill() }
            if (-not $widget.WaitForExit(5000)) { throw 'Process did not exit within 5 seconds.' }
        } catch {
            if (-not $widget.HasExited) {
                throw "Cannot close AI Usage Monitor (PID $($widget.Id)). Close it and retry installation. $($_.Exception.Message)"
            }
        }
    }
}

if (-not (Test-Administrator)) {
    try {
    $process = Start-Process -FilePath powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
    )
    # Wait only for the installer, not for the widget it launches afterwards.
    $process.WaitForExit()
    exit $process.ExitCode
    } catch {
        exit 1
    }
}

try {
$source = $PSScriptRoot
$destination = Join-Path $env:ProgramFiles 'AI Usage Monitor'
$app = Join-Path $destination 'AIUsageMonitor.exe'
$legacyApp = Join-Path $env:LOCALAPPDATA 'CodexLimits/app'
$previousDestination = Join-Path $env:ProgramFiles 'Codex Limits'

Stop-RunningWidget @(Get-Process -Name AIUsageMonitor,CodexLimits -ErrorAction SilentlyContinue)
& (Join-Path $source 'Update-Config.ps1') -TemplatePath (Join-Path $source 'config.example.json')

New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($file in @('AIUsageMonitor.exe', 'AIUsageMonitor.dll', 'AIUsageMonitor.deps.json', 'AIUsageMonitor.runtimeconfig.json', 'config.example.json', 'README.md', 'Uninstall.ps1')) {
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $destination $file) -Force
}

if (Test-Path -LiteralPath $legacyApp) { Remove-Item -LiteralPath $legacyApp -Recurse -Force }
$oldShortcutPath = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Codex Limits.lnk'
Remove-Item -LiteralPath $oldShortcutPath -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $previousDestination) { Remove-Item -LiteralPath $previousDestination -Recurse -Force }
$legacyShortcut = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs/Codex Limits.lnk'
Remove-Item -LiteralPath $legacyShortcut -Force -ErrorAction SilentlyContinue

$shortcutPath = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'AI Usage Monitor.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $app
$shortcut.WorkingDirectory = $destination
$shortcut.IconLocation = "$app,0"
$shortcut.Save()

$version = ([Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion -split '\+')[0]
$uninstallKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AIUsageMonitor'
$uninstaller = Join-Path $destination 'Uninstall.ps1'
New-Item -Path $uninstallKey -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'AI Usage Monitor' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $version -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'AI Usage Monitor' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $destination -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value "$app,0" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`"" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

# Retire the old registration only after the replacement is fully registered.
Remove-Item -LiteralPath 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLimits' -Recurse -Force -ErrorAction SilentlyContinue
foreach ($file in @('CodexLimits.exe', 'CodexLimits.dll', 'CodexLimits.deps.json', 'CodexLimits.runtimeconfig.json')) {
    Remove-Item -LiteralPath (Join-Path $destination $file) -Force -ErrorAction SilentlyContinue
}

} catch {
    exit 1
}

exit 0
