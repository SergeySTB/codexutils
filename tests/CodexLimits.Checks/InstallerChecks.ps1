$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')

function Check($Condition, $Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Output "PASS: $Message"
}

$build = Get-Content -LiteralPath (Join-Path $repoRoot 'build.ps1') -Raw
$install = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/windows/Install.ps1') -Raw
$uninstall = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/windows/Uninstall.ps1') -Raw
$cmd = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/windows/install.cmd') -Raw

Check ($build.Contains('CodexLimits-Setup_v$version.exe')) 'installer filename includes major.minor version'
Check ($build.Contains("'Install.ps1'") -and $build.Contains("'Uninstall.ps1'")) 'installer payload includes install and uninstall scripts'
Check ($cmd.Contains('Install.ps1')) 'IExpress launcher starts the installer script'
Check ($install.Contains("Join-Path `$env:ProgramFiles 'Codex Limits'")) 'installer targets Program Files'
Check ($install.Contains('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLimits')) 'installer creates an uninstall registry entry'
Check ($install.Contains("'CommonPrograms'")) 'installer creates an all-users Start menu shortcut'
Check ($install.Contains("'Update-Config.ps1'")) 'installer migrates the existing user configuration'
Check ($uninstall.Contains('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLimits')) 'uninstaller removes its registry entry'
Check ($uninstall.Contains("Join-Path `$env:ProgramFiles 'Codex Limits'")) 'uninstaller validates its installation directory'
Check ($uninstall.Contains('Remove-Item -LiteralPath $destination -Recurse -Force')) 'uninstaller removes the Program Files directory'

foreach ($script in @('packaging/windows/Install.ps1', 'packaging/windows/Uninstall.ps1')) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $repoRoot $script), [ref]$tokens, [ref]$errors)
    Check ($errors.Count -eq 0) "$script parses"
}

Write-Output 'All installer checks passed.'
