$ErrorActionPreference = 'Stop'

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return [Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    $process = Start-Process -FilePath powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
    )
    exit $process.ExitCode
}

$source = $PSScriptRoot
$destination = Join-Path $env:ProgramFiles 'Codex Limits'
$app = Join-Path $destination 'CodexLimits.exe'
$legacyApp = Join-Path $env:LOCALAPPDATA 'CodexLimits/app'

& (Join-Path $source 'Update-Config.ps1') -TemplatePath (Join-Path $source 'config.example.json')

Get-Process -Name CodexLimits -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($file in @('CodexLimits.exe', 'CodexLimits.dll', 'CodexLimits.deps.json', 'CodexLimits.runtimeconfig.json', 'config.example.json', 'README.md', 'Uninstall.ps1')) {
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $destination $file) -Force
}

if (Test-Path -LiteralPath $legacyApp) { Remove-Item -LiteralPath $legacyApp -Recurse -Force }
$legacyShortcut = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs/Codex Limits.lnk'
Remove-Item -LiteralPath $legacyShortcut -Force -ErrorAction SilentlyContinue

$shortcutPath = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Codex Limits.lnk'
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
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'Codex Limits' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $version -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'Codex Limits' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $destination -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value "$app,0" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`"" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

Start-Process -FilePath $app -WorkingDirectory $destination
