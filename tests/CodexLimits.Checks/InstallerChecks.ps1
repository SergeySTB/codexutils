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
Check ($cmd.Contains('powershell.exe -NoProfile -WindowStyle Hidden')) 'installer launcher hides its PowerShell window'
Check ($install.Contains("Join-Path `$env:ProgramFiles 'Codex Limits'")) 'installer targets Program Files'
Check ($install.Contains('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLimits')) 'installer creates an uninstall registry entry'
Check ($install.Contains("'CommonPrograms'")) 'installer creates an all-users Start menu shortcut'
Check ($install.Contains("'Update-Config.ps1'")) 'installer migrates the existing user configuration'
Check ($install.Contains('-Verb RunAs -WindowStyle Hidden')) 'elevated installer stays hidden'
Check ($uninstall.Contains('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLimits')) 'uninstaller removes its registry entry'
Check ($uninstall.Contains("Join-Path `$env:ProgramFiles 'Codex Limits'")) 'uninstaller validates its installation directory'
Check ($uninstall.Contains('Remove-Item -LiteralPath $destination -Recurse -Force')) 'uninstaller removes the Program Files directory'
Check ($uninstall.Contains('-Verb RunAs -WindowStyle Hidden')) 'elevated uninstaller stays hidden'

foreach ($script in @('packaging/windows/Install.ps1', 'packaging/windows/Uninstall.ps1')) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $repoRoot $script), [ref]$tokens, [ref]$errors)
    Check ($errors.Count -eq 0) "$script parses"
}


# Exercise the actual shutdown function without stopping the user's widget.
$ast = [Management.Automation.Language.Parser]::ParseInput($install, [ref]$null, [ref]$null)
$stopFunction = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Stop-RunningWidget' }, $true)
. ([scriptblock]::Create($stopFunction.Extent.Text))
foreach ($mode in @('graceful', 'hidden', 'stalled', 'denied', 'exited')) {
    $fake = [pscustomobject]@{ Id = 123; Mode = $mode; HasExited = ($mode -eq 'exited'); Killed = $false; Waits = 0 }
    $fake | Add-Member ScriptMethod CloseMainWindow { return $this.Mode -ne 'hidden' }
    $fake | Add-Member ScriptMethod WaitForExit {
        param($timeout)
        if ($timeout -ne 5000) { throw 'Unbounded wait' }
        $this.Waits++
        if ($this.Mode -eq 'graceful' -or $this.Killed) { $this.HasExited = $true }
        return $this.HasExited
    }
    $fake | Add-Member ScriptMethod Kill {
        if ($this.Mode -eq 'denied') { throw 'Access denied' }
        $this.Killed = $true
    }
    $failed = $false
    try { Stop-RunningWidget @($fake) } catch { $failed = $true }
    Check ($failed -eq ($mode -eq 'denied')) "shutdown handles $mode process"
    if (-not $failed) { Check $fake.HasExited "shutdown waits for $mode process to exit" }
}
Stop-RunningWidget @()
Check ($install -notmatch '-Verb RunAs -Wait') 'elevated installer does not wait for launched widget descendants'

# Inspect real controls and simulate the dialog result without displaying a window.
$dialogFunction = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Show-InstallationResult' }, $true)
. ([scriptblock]::Create($dialogFunction.Extent.Text.Replace('$form.ShowDialog()', '(& $script:dialogProbe $launch $detailsBox)')))
foreach ($scenario in @('launch', 'unchecked', 'failed', 'dismissed')) {
    $script:dialogProbe = {
        param($launch, $detailsBox)
        $success = $scenario -ne 'failed'
        if ($launch.Enabled -ne $success -or $launch.Checked -ne $success) { throw 'Incorrect launch checkbox state' }
        if ($detailsBox.Text -ne 'Test details') { throw 'Missing installation details' }
        if ($scenario -eq 'unchecked') { $launch.Checked = $false }
        if ($scenario -eq 'dismissed') { return [Windows.Forms.DialogResult]::Cancel }
        return [Windows.Forms.DialogResult]::OK
    }
    $shouldLaunch = Show-InstallationResult ($scenario -ne 'failed') 'Test details'
    Check ($shouldLaunch -eq ($scenario -eq 'launch')) "completion dialog handles $scenario"
}
Remove-Variable dialogProbe -Scope Script
Write-Output 'All installer checks passed.'
