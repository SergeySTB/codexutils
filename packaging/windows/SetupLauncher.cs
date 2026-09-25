using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

internal static class SetupLauncher
{
    private static string Tr(string russian, string english)
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? russian : english;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string directory = AppDomain.CurrentDomain.BaseDirectory;
        try
        {
            if (args.Length != 1 || args[0] != "--install")
            {
                using (Form welcome = CreateWelcomeDialog(directory))
                    if (welcome.ShowDialog() != DialogResult.OK) return 0;
            }

            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    using (var elevated = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--install")
                    {
                        UseShellExecute = true, Verb = "runas"
                    }))
                    {
                        elevated.WaitForExit();
                        return elevated.ExitCode;
                    }
                }
            }

            int result = RunScript(directory);
            CheckBox launch;
            using (Form completion = CreateResultDialog(directory, result == 0, out launch))
            {
                if (completion.ShowDialog() == DialogResult.OK && result == 0 && launch.Checked)
                {
                    try
                    {
                        Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                            @"AI Usage Monitor\AIUsageMonitor.exe"));
                    }
                    catch (Exception error)
                    {
                        MessageBox.Show(error.Message, Tr("Не удалось запустить AI Usage Monitor", "Could not start AI Usage Monitor"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 1;
                    }
                }
            }
            return result;
        }
        catch (Win32Exception error)
        {
            if (error.NativeErrorCode == 1223) return 0; // UAC was cancelled.
            MessageBox.Show(error.Message, "AI Usage Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "AI Usage Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static Form CreateWelcomeDialog(string directory)
    {
        Form form = CreateDialog(directory, Tr("Установите за пару секунд", "Install in a few seconds"),
            Tr("Лимиты Codex и Claude всегда перед глазами.", "Keep Codex and Claude limits in view."),
            Tr("Готово к установке", "Ready to install"),
            Tr("Для всех пользователей · C:\\Program Files\\AI Usage Monitor\nНастройки сохранятся при обновлении.",
                "For all users · C:\\Program Files\\AI Usage Monitor\nYour settings are kept during updates."), false);
        Button cancel = Action(Tr("Отмена", "Cancel"), 310, false);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.AccessibleName = Tr("Отмена установки", "Cancel installation");
        Button install = Action(Tr("Установить", "Install"), 474, true);
        install.DialogResult = DialogResult.OK;
        install.AccessibleName = Tr("Начать установку", "Start installation");
        form.Controls.AddRange(new Control[] { cancel, install });
        form.AcceptButton = install;
        form.CancelButton = cancel;
        return form;
    }

    internal static Form CreateResultDialog(string directory, bool succeeded, out CheckBox launch)
    {
        Form form = CreateDialog(directory, succeeded ? Tr("Всё готово", "All set") : Tr("Не удалось установить", "Installation failed"),
            succeeded ? Tr("AI Usage Monitor готов следить за вашими лимитами.", "AI Usage Monitor is ready to track your limits.") :
                Tr("Проверьте права доступа и повторите установку.", "Check your permissions and try again."),
            succeeded ? Tr("Установка завершена", "Installation complete") : Tr("Установка прервана", "Installation interrupted"),
            succeeded ? Tr("Приложение добавлено в меню «Пуск».\nВаши настройки сохранены.",
                "The app was added to the Start menu.\nYour settings were kept.") :
                Tr("Закройте установщик и повторите попытку.\nЕсли ошибка повторится, проверьте права доступа.",
                    "Close the installer and try again.\nIf it still fails, check your permissions."), !succeeded);
        launch = new CheckBox
        {
            Text = Tr("Запустить AI Usage Monitor", "Launch AI Usage Monitor"),
            AccessibleName = Tr("Запустить AI Usage Monitor после установки", "Launch AI Usage Monitor after installation"),
            Checked = succeeded,
            Enabled = succeeded,
            ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(226, 235, 242),
            BackColor = form.BackColor,
            Location = new Point(37, 331), Size = new Size(290, 32), TabIndex = 0
        };
        Button finish = Action(Tr("Готово", "Finish"), 474, true);
        finish.DialogResult = DialogResult.OK;
        finish.AccessibleName = Tr("Закрыть установщик", "Close installer");
        finish.TabIndex = 1;
        form.Controls.AddRange(new Control[] { launch, finish });
        form.AcceptButton = finish;
        return form;
    }

    private static Form CreateDialog(string directory, string heading, string subheading,
        string cardHeading, string cardBody, bool failed)
    {
        bool contrast = SystemInformation.HighContrast;
        Color background = contrast ? SystemColors.Window : Color.FromArgb(13, 22, 32);
        Color ink = contrast ? SystemColors.WindowText : Color.FromArgb(240, 248, 250);
        Color muted = contrast ? SystemColors.WindowText : Color.FromArgb(164, 184, 198);
        Form form = new Form
        {
            Text = Tr("Установка AI Usage Monitor ", "AI Usage Monitor Setup ") + Version(directory),
            ClientSize = new Size(660, 410),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            Font = new Font("Segoe UI", 10),
            BackColor = background, ForeColor = ink
        };
        string app = Path.Combine(directory, "AIUsageMonitor.exe");
        if (File.Exists(app)) form.Icon = Icon.ExtractAssociatedIcon(app);
        if (!contrast)
        {
            int dark = 1;
            try { DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); }
            catch (DllNotFoundException) { }
        }

        Panel hero = new Panel { Bounds = new Rectangle(0, 0, 660, 196), BackColor = background };
        hero.Paint += delegate(object sender, PaintEventArgs e) { DrawHero(e.Graphics, hero.ClientRectangle, contrast); };
        hero.Controls.Add(Text("AI USAGE MONITOR", 190, 35, 430, 24, 9,
            contrast ? ink : Color.FromArgb(131, 230, 216), FontStyle.Bold));
        hero.Controls.Add(Text(heading, 190, 68, 440, 44, 24, ink, FontStyle.Bold));
        hero.Controls.Add(Text(subheading, 191, 122, 430, 50, 10, muted, FontStyle.Regular));
        form.Controls.Add(hero);

        Panel card = new Panel
        {
            Bounds = new Rectangle(32, 217, 596, 99),
            BackColor = contrast ? SystemColors.Window : Color.FromArgb(25, 38, 51)
        };
        card.Controls.Add(Text(cardHeading, 18, 13, 550, 27, 12,
            failed && !contrast ? Color.FromArgb(255, 178, 154) : ink, FontStyle.Bold));
        card.Controls.Add(Text(cardBody, 19, 43, 550, 48, 9, muted, FontStyle.Regular));
        form.Controls.Add(card);
        return form;
    }

    private static Label Text(string value, int x, int y, int width, int height, float size, Color color, FontStyle style)
    {
        return new Label
        {
            Text = value, Location = new Point(x, y), Size = new Size(width, height),
            Font = new Font("Segoe UI", size, style), ForeColor = color,
            BackColor = Color.Transparent, UseMnemonic = false
        };
    }

    private static Button Action(string label, int x, bool primary)
    {
        bool contrast = SystemInformation.HighContrast;
        Button button = new Button
        {
            Text = label, Bounds = new Rectangle(x, 337, 150, 44),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = contrast ? (primary ? SystemColors.Highlight : SystemColors.Control) :
                (primary ? Color.FromArgb(106, 232, 209) : Color.FromArgb(39, 55, 69)),
            ForeColor = contrast ? (primary ? SystemColors.HighlightText : SystemColors.ControlText) :
                (primary ? Color.FromArgb(10, 35, 42) : Color.FromArgb(226, 235, 242)),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            TabIndex = primary ? 1 : 0
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static void DrawHero(Graphics graphics, Rectangle bounds, bool contrast)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (!contrast)
        {
            using (var gradient = new LinearGradientBrush(bounds, Color.FromArgb(22, 47, 62),
                Color.FromArgb(33, 29, 66), LinearGradientMode.Horizontal))
                graphics.FillRectangle(gradient, bounds);
        }
        Color teal = contrast ? SystemColors.Highlight : Color.FromArgb(111, 239, 212);
        Color violet = contrast ? SystemColors.Highlight : Color.FromArgb(175, 160, 255);
        using (var ring = new Pen(teal, 9))
        using (var inner = new Pen(violet, 6))
        using (var spark = new Pen(contrast ? SystemColors.WindowText : Color.White, 3))
        {
            ring.StartCap = ring.EndCap = LineCap.Round;
            inner.StartCap = inner.EndCap = LineCap.Round;
            graphics.DrawArc(ring, 48, 48, 104, 104, -90, 295);
            graphics.DrawArc(inner, 64, 64, 72, 72, 72, 290);
            graphics.DrawLine(spark, 100, 79, 100, 122);
            graphics.DrawLine(spark, 78, 101, 122, 101);
            graphics.DrawLine(spark, 91, 92, 109, 110);
            graphics.DrawLine(spark, 109, 92, 91, 110);
        }
    }

    private static string Version(string directory)
    {
        string app = Path.Combine(directory, "AIUsageMonitor.exe");
        return File.Exists(app) ? FileVersionInfo.GetVersionInfo(app).ProductVersion.Split('+')[0] : "";
    }

    internal static int RunScript(string directory)
    {
        // No console allocation; unlike SW_HIDE, this does not hide dialogs.
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
