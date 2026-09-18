using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

internal static class SetupLauncher
{
    [STAThread]
    private static int Main()
    {
        try
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    using (var elevated = Process.Start(new ProcessStartInfo(Application.ExecutablePath)
                    {
                        UseShellExecute = true, Verb = "runas"
                    }))
                    {
                        elevated.WaitForExit();
                        return elevated.ExitCode;
                    }
                }
            }
            return RunScript(AppDomain.CurrentDomain.BaseDirectory);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "AI Usage Monitor setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static int RunScript(string directory)
    {
        // No console allocation; unlike SW_HIDE, this does not hide the result dialog.
        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + Path.Combine(directory, "Install.ps1") + "\"",
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true
        }))
        {
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
