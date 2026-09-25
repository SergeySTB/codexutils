$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')

function Check($Condition, $Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Output "PASS: $Message"
}

$build = Get-Content -LiteralPath (Join-Path $repoRoot 'build.ps1') -Raw
$install = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/windows/Install.ps1') -Raw
$uninstall = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/windows/Uninstall.ps1') -Raw
$launcher = Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/windows/SetupLauncher.cs') -Raw

Check ($build.Contains('AIUsageMonitor-Setup_v$version.exe')) 'installer filename includes major.minor version'
Check ($build.Contains("'Install.ps1'") -and $build.Contains("'Uninstall.ps1'")) 'installer payload includes install and uninstall scripts'
Check ($build.Contains('AppLaunched=SetupLauncher.exe') -and -not $build.Contains('install.cmd')) 'installer starts a GUI launcher without CMD'
Check ($launcher.Contains('CreateNoWindow = true') -and -not $launcher.Contains('WindowStyle')) 'launcher avoids a console without hiding dialogs'
Check ($install.Contains("Join-Path `$env:ProgramFiles 'AI Usage Monitor'")) 'installer targets the named Program Files folder'
Check ($install.Contains("Join-Path `$env:ProgramFiles 'Codex Limits'")) 'installer removes the previous named Program Files folder during upgrade'
Check ($install.Contains('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AIUsageMonitor')) 'installer creates an uninstall registry entry'
Check ($install.Contains("'CommonPrograms'")) 'installer creates an all-users Start menu shortcut'
Check ($install.Contains("'Update-Config.ps1'")) 'installer migrates the existing user configuration'
Check ($install.Contains('-Verb RunAs -WindowStyle Hidden')) 'elevated installer stays hidden'
Check ($uninstall.Contains('HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AIUsageMonitor')) 'uninstaller removes its registry entry'
Check ($uninstall.Contains("Join-Path `$env:ProgramFiles 'AI Usage Monitor'")) 'uninstaller validates its installation directory'
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

Check ($launcher.Contains('CreateWelcomeDialog') -and $launcher.Contains('CreateResultDialog')) 'launcher owns both installer screens'
Check ($launcher.Contains('Application.ExecutablePath, "--install"') -and $launcher.Contains('welcome.ShowDialog()')) 'consent precedes elevation'
Check (-not $install.Contains('Show-InstallationResult')) 'install script leaves dialogs to launcher'

# Run the production child-launch path against a harmless script, without UAC or installation.
$probeRoot = Join-Path $repoRoot '.build/installer-launch-check'
New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
@'
using System;
using System.Windows.Forms;
internal static class LauncherProbe {
    [System.STAThread]
    private static int Main() {
        using (Form welcome = SetupLauncher.CreateWelcomeDialog(AppDomain.CurrentDomain.BaseDirectory)) {
            if (welcome.AcceptButton == null) return 43;
            if (welcome.CancelButton == null) return 46;
            if (((Button)welcome.AcceptButton).DialogResult != DialogResult.OK) return 47;
            if (((Button)welcome.CancelButton).DialogResult != DialogResult.Cancel) return 48;
            using (var timer = new Timer()) {
                timer.Interval = 30;
                timer.Tick += delegate { timer.Stop(); ((Button)welcome.CancelButton).PerformClick(); };
                timer.Start();
                if (welcome.ShowDialog() != DialogResult.Cancel) return 49;
            }
        }
        using (Form welcome = SetupLauncher.CreateWelcomeDialog(AppDomain.CurrentDomain.BaseDirectory))
        using (var timer = new Timer()) {
            timer.Interval = 30;
            timer.Tick += delegate { timer.Stop(); ((Button)welcome.AcceptButton).PerformClick(); };
            timer.Start();
            if (welcome.ShowDialog() != DialogResult.OK) return 50;
        }
        CheckBox launch;
        using (Form success = SetupLauncher.CreateResultDialog(AppDomain.CurrentDomain.BaseDirectory, true, out launch)) {
            if (!launch.Enabled || !launch.Checked || success.AcceptButton == null) return 44;
        }
        using (Form failure = SetupLauncher.CreateResultDialog(AppDomain.CurrentDomain.BaseDirectory, false, out launch)) {
            if (launch.Enabled || launch.Checked || failure.AcceptButton == null) return 45;
        }
        return SetupLauncher.RunScript(AppDomain.CurrentDomain.BaseDirectory);
    }
}
'@ | Set-Content -LiteralPath (Join-Path $probeRoot 'Probe.cs') -Encoding UTF8
@'
Add-Type -AssemblyName System.Windows.Forms
Add-Type 'public static class ConsoleProbe { [System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern System.IntPtr GetConsoleWindow(); [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr handle); }'
if ([ConsoleProbe]::GetConsoleWindow() -ne [IntPtr]::Zero) { exit 41 }
$form = New-Object Windows.Forms.Form
$form.Text = 'AI Usage Monitor launcher check'
$timer = New-Object Windows.Forms.Timer
$timer.Interval = 300
$timer.Add_Tick({
    $script:visible = [ConsoleProbe]::IsWindowVisible($form.Handle)
    $timer.Stop()
    $form.Close()
})
$timer.Start()
[void]$form.ShowDialog()
$timer.Dispose()
$form.Dispose()
if (-not $script:visible) { exit 42 }
exit 17
'@ | Set-Content -LiteralPath (Join-Path $probeRoot 'Install.ps1') -Encoding UTF8
$probeExe = Join-Path $probeRoot 'LauncherProbe.exe'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /main:LauncherProbe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$probeExe" (Join-Path $repoRoot 'packaging/windows/SetupLauncher.cs') (Join-Path $probeRoot 'Probe.cs')
Check ($LASTEXITCODE -eq 0) 'GUI launcher compiles with built-in .NET Framework'
$process = Start-Process -FilePath $probeExe -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'Launcher dialog check timed out' }
Check ($process.ExitCode -eq 17) 'launcher creates no console, shows the dialog and returns its exit code'
Write-Output 'All installer checks passed.'
