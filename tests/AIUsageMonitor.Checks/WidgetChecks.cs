using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AIUsageMonitor;

internal static class WidgetChecks
{
    public static void Run(string outputDirectory, bool layoutOnly = false, bool dragOnly = false)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var originalCursor = new ScreenPoint();
            if (!layoutOnly) GetCursorPos(out originalCursor);
            app.Startup += async (_, _) =>
            {
                WidgetWindow? window = null;
                try
                {
                    Directory.CreateDirectory(outputDirectory);
                    if (dragOnly)
                    {
                        await CheckDrag(Path.GetFullPath(outputDirectory));
                        return;
                    }
                    if (layoutOnly)
                    {
                        await CheckLayouts(Path.GetFullPath(outputDirectory));
                        return;
                    }
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
                            IconWidthPx = 132, Edge = overlay ? "bottom" : edge, OffsetPx = 120,
                            MarginPx = edge == "bottom-negative" ? -50 : 4,
                            RespectTaskbar = edge != "bottom-screen"
                        } }.Validate();
                        window = new WidgetWindow(Path.Combine(outputDirectory, "unused-config.json"), settings, demo: true);
                        window.Show();
                        await Until(() => window.IsLoaded, "widget loaded");
                        Require(window.ContextMenu?.Items.OfType<MenuItem>().Any(item =>
                            (string?)item.Header == "Версия " + ProductInfo.Version && !item.IsEnabled) == true,
                            "current version appears in the menu");
                        Require(window.ContextMenu!.Items.OfType<MenuItem>().Any(item => (string?)item.Header == "Добавить аккаунт") &&
                            window.ContextMenu.Items.OfType<MenuItem>().Single(item => (string?)item.Header == "Убрать аккаунт").Items.Count == configuredAccounts.Length,
                            "account menu contains add and per-account removal");
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
                    if (!layoutOnly) SetCursorPos(originalCursor.X, originalCursor.Y);
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

    private static async Task CheckDrag(string outputDirectory)
    {
        foreach (string mode in new[] { "icons", "cards" })
        {
            var config = Path.Combine(outputDirectory, "drag-" + mode + ".json");
            var settings = new Settings
            {
                CodexExecutable = Path.Combine(outputDirectory, "missing.exe"),
                Widget = new() { DisplayMode = mode, OffsetPx = 100, MarginPx = 100, RespectTaskbar = false }
            };
            File.WriteAllText(config, System.Text.Json.JsonSerializer.Serialize(settings, Settings.JsonOptions));
            var window = new WidgetWindow(config, settings, demo: false);
            try
            {
                window.Show();
                await Task.Delay(250);
                var handle = new WindowInteropHelper(window).Handle;
                Require(GetWindowRect(handle, out var before), "read pre-drag bounds");
                // The icon is a button; dragging it must not trigger its sign-in action.
                int x = before.Left + 25, y = before.Top + 20;
                await Task.Run(() =>
                {
                    SetCursorPos(x, y);
                    Thread.Sleep(150);
                    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                    try
                    {
                        Thread.Sleep(100);
                        SetCursorPos(x + 12, y + 12);
                        Thread.Sleep(200);
                        SetCursorPos(x + 92, y + 72);
                        Thread.Sleep(150);
                    }
                    finally { mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }
                });
                await Task.Delay(1300);
                Require(GetWindowRect(handle, out var after) && after.Left > before.Left + 30 && after.Top > before.Top + 20,
                    mode + " follows mouse and stays in place across timer ticks");
                var saved = Settings.Load(config);
                Require(saved.Widget.OffsetPx != 100 && saved.Widget.MarginPx != 100, "drag writes configuration");
                window.Close();
                window = new WidgetWindow(config, saved, demo: false);
                window.Show();
                await Task.Delay(250);
                Require(GetWindowRect(new WindowInteropHelper(window).Handle, out var restored) &&
                    restored.Left == after.Left && restored.Top == after.Top, "restart restores " + mode + " position");
            }
            finally { window.Close(); }
        }
        Console.WriteLine("PASS: icons and cards drag, save, timer stability and restart restoration.");
    }

    private static async Task CheckLayouts(string outputDirectory)
    {
        int checks = 0;
        foreach (int count in new[] { 1, 3 })
        foreach (string edge in new[] { "top", "bottom", "left", "right" })
        foreach (bool fixedSize in new[] { false, true })
        {
            var settings = new Settings
            {
                Accounts = Enumerable.Range(1, count).Select(i => new AccountSettings("Account " + i, Path.Combine(outputDirectory, "profile" + i))).ToArray(),
                Widget = new() { DisplayMode = "cards", Edge = edge, IconWidthPx = 0, IconHeightPx = -1,
                    CardWidthPx = fixedSize ? 340 : 0, CardHeightPx = fixedSize ? 240 : 0 }
            }.Validate();
            var config = Path.Combine(outputDirectory, "layout-config.json");
            var window = new WidgetWindow(config, settings, demo: true);
            try
            {
                window.Show();
                await Task.Delay(250);
                window.UpdateLayout();
                var scroll = (ScrollViewer)window.Content;
                var strip = (StackPanel)scroll.Content;
                Require(strip.Children.Count == count, "one full card per configured account");
                Require(strip.Orientation == (edge is "left" or "right" ? Orientation.Vertical : Orientation.Horizontal), "card orientation follows edge");
                var scale = VisualTreeHelper.GetDpi(window);
                var first = (Border)strip.Children[0];
                var handle = new WindowInteropHelper(window).Handle;
                Require(GetWindowRect(handle, out var bounds), "card window bounds available");
                var workArea = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
                var expected = Placement.Calculate(new(workArea.X, workArea.Y, workArea.Width, workArea.Height), settings.Widget,
                    widthPx: bounds.Right - bounds.Left, heightPx: bounds.Bottom - bounds.Top);
                Require(bounds.Left == expected.X && bounds.Top == expected.Y, "card window stays anchored to selected edge");
                Require(first.ActualWidth > 0 && first.ActualHeight > 0, "cards have measured size");
                if (fixedSize)
                {
                    Require(Math.Abs(first.ActualWidth * scale.DpiScaleX - 340) < 2 &&
                        Math.Abs(first.ActualHeight * scale.DpiScaleY - 240) < 2, "card dimensions are physical pixels");
                    Require(((ScrollViewer)first.Child).ScrollableHeight > 0, "short cards scroll without losing information");
                    Require(Math.Abs((edge is "left" or "right" ? window.ActualWidth * scale.DpiScaleX : window.ActualHeight * scale.DpiScaleY)
                        - (edge is "left" or "right" ? 340 : 240)) < 2, "window fits fixed cards without empty margins");
                }
                foreach (Border card in strip.Children)
                {
                    var panel = (StackPanel)((ScrollViewer)card.Child).Content;
                    Require(Texts(panel).Any(text => text.Contains("5 часов")) && Texts(panel).Any(text => text.Contains("Неделя")), "both quotas are visible in card content");
                    Require(panel.Children.OfType<Button>().All(button => button.Visibility == Visibility.Collapsed), "signed-in demo cards contain no visible icon or login button");
                }
                if (!fixedSize) Require(scroll.ScrollableWidth < 1, "automatic card width reserves vertical scrollbar space");
                if (count == 3)
                {
                    var next = (Border)strip.Children[1];
                    var a = first.TranslatePoint(new Point(), strip);
                    var b = next.TranslatePoint(new Point(), strip);
                    Require(edge is "left" or "right" ? b.Y > a.Y && b.X == a.X : b.X > a.X && b.Y == a.Y, "cards are arranged in a column or row");
                }
                Save(window, Path.Combine(outputDirectory, $"cards-{edge}-{count}-{fixedSize}.png"));
                var icons = settings with { Widget = settings.Widget with { DisplayMode = "icons", IconWidthPx = 132, IconHeightPx = 44 } };
                File.WriteAllText(config, System.Text.Json.JsonSerializer.Serialize(icons, Settings.JsonOptions));
                var reload = window.ContextMenu!.Items.OfType<MenuItem>().Single(item => (string?)item.Header == "Применить конфигурацию");
                reload.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                window.UpdateLayout();
                var iconStrip = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                Require(iconStrip.Children.OfType<Button>().Count() == count, "reload switches from cards to icons");
                window.ContextMenu!.Items.OfType<MenuItem>().Single(item => (string?)item.Header == "Показать карточки")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                window.UpdateLayout();
                Require(Settings.Load(config).Widget.DisplayMode == "cards" && window.Content is ScrollViewer, "menu switches from icons to cards");
                checks++;
            }
            finally { window.Close(); }
        }
        var empty = new WidgetWindow(Path.Combine(outputDirectory, "empty.json"),
            new Settings { Accounts = [] }.Validate(), demo: true);
        try
        {
            empty.Show();
            await Task.Delay(100);
            Require(empty.Content is Border { Child: Button } &&
                empty.ContextMenu!.Items.OfType<MenuItem>().Any(item => (string?)item.Header == "Добавить аккаунт") &&
                empty.ContextMenu.Items.OfType<MenuItem>().All(item => (string?)item.Header != "Убрать аккаунт"),
                "empty widget offers account addition");
        }
        finally { empty.Close(); }
        var signedOutSettings = new Settings
        {
            CodexExecutable = Path.Combine(outputDirectory, "missing-codex.exe"),
            Widget = new() { DisplayMode = "cards", CardWidthPx = 340 }
        }.Validate();
        var signedOut = new WidgetWindow(Path.Combine(outputDirectory, "unused.json"), signedOutSettings, demo: false);
        try
        {
            signedOut.Show();
            await Task.Delay(150);
            signedOut.UpdateLayout();
            var strip = (StackPanel)((ScrollViewer)signedOut.Content).Content;
            var panel = (StackPanel)((ScrollViewer)((Border)strip.Children[0]).Child).Content;
            var login = panel.Children.OfType<Button>().Single();
            Require(login.Visibility == Visibility.Visible && login.IsEnabled &&
                new ButtonAutomationPeer(login).GetName().Contains("Войти"), "signed-out card provides accessible login button");
            Require(Texts(panel).Any(text => text.Contains("Codex не найден")), "connection error remains visible in card");
        }
        finally { signedOut.Close(); }
        Console.WriteLine($"PASS: {checks} card layout, size, overflow and mode-switch scenarios. PNGs: {outputDirectory}");
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
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);
}
