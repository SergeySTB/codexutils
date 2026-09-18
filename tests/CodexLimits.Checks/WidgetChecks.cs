using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexLimits;

internal static class WidgetChecks
{
    public static void Run(string outputDirectory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            GetCursorPos(out var originalCursor);
            app.Startup += async (_, _) =>
            {
                WidgetWindow? window = null;
                try
                {
                    Directory.CreateDirectory(outputDirectory);
                    int checks = 0;
                    foreach (var edge in new[] { "top", "bottom", "left", "right", "bottom-negative", "bottom-screen" })
                    {
                        bool overlay = edge.StartsWith("bottom-");
                        AccountSettings[] configuredAccounts =
                        [
                            new("Личный", Path.Combine(outputDirectory, "personal")),
                            new("Рабочий", Path.Combine(outputDirectory, "work")),
                            new("Третий", Path.Combine(outputDirectory, "third"))
                        ];
                        var settings = new Settings { Accounts = configuredAccounts, Widget = new WidgetSettings
                        {
                            WidthPx = 132, Edge = overlay ? "bottom" : edge, OffsetPx = 120,
                            MarginPx = edge == "bottom-negative" ? -50 : 4,
                            RespectTaskbar = edge != "bottom-screen"
                        } }.Validate();
                        window = new WidgetWindow(Path.Combine(outputDirectory, "unused-config.json"), settings, demo: true);
                        window.Show();
                        await Until(() => window.IsLoaded, "widget loaded");
                        Require(window.ContextMenu?.Items.OfType<MenuItem>().Any(item =>
                            (string?)item.Header == "Версия " + ProductInfo.Version && !item.IsEnabled) == true,
                            "current version appears in the menu");
                        var viewbox = (Viewbox)((Border)window.Content).Child;
                        var strip = (StackPanel)viewbox.Child;
                        Require(strip.Children.Count == configuredAccounts.Length && strip.Children.OfType<Button>().Count() == configuredAccounts.Length,
                            "account icons follow configuration on " + edge);
                        if (overlay)
                        {
                            var handle = new WindowInteropHelper(window).Handle;
                            var monitor = System.Windows.Forms.Screen.FromHandle(handle);
                            Require(GetWindowRect(handle, out var rect), "read widget bounds");
                            Require(rect.Bottom > monitor.WorkingArea.Bottom && rect.Bottom <= monitor.Bounds.Bottom,
                                "widget overlaps actual taskbar and stays on screen");
                            // A second owned topmost window models the taskbar raising itself.
                            var cover = new Window { Width = 88, Height = 44, Topmost = true, ShowActivated = false,
                                ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
                            try
                            {
                                cover.Show();
                                SetWindowPos(new WindowInteropHelper(cover).Handle, new IntPtr(-1), rect.Left, rect.Top,
                                    rect.Right - rect.Left, rect.Bottom - rect.Top, 0x0010);
                                var sample = new ScreenPoint { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 };
                                await Until(() => GetAncestor(WindowFromPoint(sample), 2) == handle,
                                    "overlay returns above competing topmost window");
                            }
                            finally { cover.Close(); }
                        }
                        Save(window, Path.Combine(outputDirectory, edge + "-icons.png"));
                        string[] expectedEmails = ["personal@example.com", "work@example.com", "account3@example.com"];
                        for (int i = 0; i < configuredAccounts.Length; i++)
                        {
                            var icon = (Button)strip.Children[i];
                            var tip = (ToolTip)icon.ToolTip;
                            var center = icon.PointToScreen(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2));
                            Require(SetCursorPos((int)center.X, (int)center.Y), "move to observed icon position");
                            await Until(() => tip.IsOpen && tip.ActualWidth > 0, "hover opens account card");
                            var card = (StackPanel)tip.Content;
                            var text = string.Join("\n", Texts(card));
                            Require(text.Contains(expectedEmails[i]) &&
                                !expectedEmails.Where((_, index) => index != i).Any(text.Contains), "card belongs to hovered account");
                            Require(text.Contains(i == 1 ? "PLUS" : "PRO") && text.Contains("5 часов") && text.Contains("Неделя") && text.Contains("Сброс через") &&
                                text.Contains(i == 1 ? "Нет данных" : "72%"), "limits, missing window and reset shown");
                            Require(new ButtonAutomationPeer(icon).GetName().Contains(configuredAccounts[i].Name), "accessible account name");
                            Save(tip, Path.Combine(outputDirectory, edge + "-account-" + i + ".png"));
                            var outside = window.PointToScreen(new Point(-30, -30));
                            SetCursorPos((int)Math.Max(0, outside.X), (int)Math.Max(0, outside.Y));
                            await Until(() => !tip.IsOpen, "card closes when pointer leaves");
                            checks++;
                        }
                        window.Close();
                        window = null;
                    }
                    Console.WriteLine($"PASS: {checks} hover/open/identity/limits/close scenarios, including negative-margin and screen-edge taskbar overlays with z-order recovery. PNGs: {Path.GetFullPath(outputDirectory)}");
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    window?.Close();
                    SetCursorPos(originalCursor.X, originalCursor.Y);
                    app.Shutdown();
                }
            };
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("Widget checks failed", failure);
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var value in Texts(child)) yield return value;
    }

    private static async Task Until(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(40);
        Require(condition(), message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Save(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct ScreenRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out ScreenRect rect);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(ScreenPoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out ScreenPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
}
