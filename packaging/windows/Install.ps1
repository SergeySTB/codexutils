$ErrorActionPreference = 'Stop'

function Show-InstallationResult([bool]$Succeeded) {
    Add-Type -AssemblyName System.Windows.Forms
    $form = New-Object Windows.Forms.Form
    $version = ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PSScriptRoot 'CodexLimits.exe')).ProductVersion -split '\+')[0]
    $form.Text = "Установка AI Usage Monitor $version"
    $form.ClientSize = New-Object Drawing.Size(520, 145)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.AutoScaleMode = 'Dpi'
    $status = New-Object Windows.Forms.Label
    $status.SetBounds(20, 20, 480, 30)
    $status.Text = if ($Succeeded) { 'Установка успешно завершена.' } else { 'Не удалось завершить установку.' }
    $launch = New-Object Windows.Forms.CheckBox
    $launch.SetBounds(20, 55, 480, 25)
    $launch.Text = 'Запустить AI Usage Monitor после установки'
    $launch.Enabled = $Succeeded
    $launch.Checked = $Succeeded
    $finish = New-Object Windows.Forms.Button
    $finish.SetBounds(390, 95, 110, 30)
    $finish.Text = 'Закрыть'
    $finish.DialogResult = 'OK'
    $form.AcceptButton = $finish
    $form.Controls.AddRange(@($status, $launch, $finish))
    try {
        $result = $form.ShowDialog()
        return ($Succeeded -and $result -eq 'OK' -and $launch.Checked)
    } finally { $form.Dispose() }
}

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
        [void](Show-InstallationResult $false)
        exit 1
    }
}

try {
$source = $PSScriptRoot
$destination = Join-Path $env:ProgramFiles 'AI Usage Monitor'
$app = Join-Path $destination 'CodexLimits.exe'
$legacyApp = Join-Path $env:LOCALAPPDATA 'CodexLimits/app'
$previousDestination = Join-Path $env:ProgramFiles 'Codex Limits'

Stop-RunningWidget @(Get-Process -Name CodexLimits -ErrorAction SilentlyContinue)
& (Join-Path $source 'Update-Config.ps1') -TemplatePath (Join-Path $source 'config.example.json')

New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($file in @('CodexLimits.exe', 'CodexLimits.dll', 'CodexLimits.deps.json', 'CodexLimits.runtimeconfig.json', 'config.example.json', 'README.md', 'Uninstall.ps1')) {
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
$uninstallKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLimits'
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

} catch {
    [void](Show-InstallationResult $false)
    exit 1
}

if (Show-InstallationResult $true) {
    try { Start-Process -FilePath $app -WorkingDirectory $destination }
    catch {
        [void](Show-InstallationResult $false)
        exit 1
    }
}
exit 0
