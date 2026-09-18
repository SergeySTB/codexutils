param([switch]$Quiet)

$ErrorActionPreference = 'Stop'

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return [Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($Quiet) { $arguments += '-Quiet' }
    $process = Start-Process -FilePath powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList $arguments
    exit $process.ExitCode
}

$destination = [IO.Path]::GetFullPath($PSScriptRoot)
$expectedDestination = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'Codex Limits'))
if (-not $destination.Equals($expectedDestination, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to uninstall from unexpected path: $destination"
}

Get-Process -Name CodexLimits -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Codex Limits.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $destination -Recurse -Force
Remove-Item -LiteralPath 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLimits' -Recurse -Force -ErrorAction SilentlyContinue
