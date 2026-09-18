using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using ShapePath = System.Windows.Shapes.Path;

namespace CodexLimits;

public sealed class WidgetWindow : Window
{
    private static readonly Brush Ink = Brush("#EEF4FC"), Muted = Brush("#9CAFC5"), Mint = Brush("#77E5CB"), Purple = Brush("#B6A3FF");
    private readonly string configPath;
    private readonly bool demo;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly List<AccountView> accounts = [];
    private Settings settings;
    private StackPanel? cardsPanel;
    private PixelRect? lastPlacement;
    private string? connectionProblem;
    private bool refreshing, closed, loggingIn, monitorMissing, placing, overlapsTaskbar;
    private int generation;

    private sealed class AccountView(AccountSettings config, CodexClient? client)
    {
        public AccountSettings Config = config;
        public CodexClient? Client = client;
        public AccountSnapshot? Snapshot;
        public string Status = "Подключение…";
        public bool Failed;
        public bool SigningIn;
        public int Failures;
        public DateTimeOffset NextRefresh, RateLimitUntil;
        public TextBlock Caption = new();
        public TextBlock Email = new();
        public TextBlock Footer = new();
        public Button Name = new();
        public ToolTip Details = new();
        public TextBlock[] Values = [new(), new()];
        public TextBlock[] Resets = [new(), new()];
        public ProgressBar[] Bars = [new(), new()];
        public ShapePath[] Rings = [new(), new()];
    }

    public WidgetWindow(string configPath, Settings settings, bool demo)
    {
        this.configPath = configPath;
        this.settings = settings;
        this.demo = demo;
        Title = "Codex Limits";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        FontFamily = new FontFamily("Segoe UI");
        Foreground = Ink;
        Resources[typeof(Button)] = (Style)XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Setter Property="Template"><Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border Name="frame" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" CornerRadius="3" BorderThickness="1" BorderBrush="Transparent">
                    <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" />
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="frame" Property="Background" Value="#263446" /></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="frame" Property="BorderBrush" Value="#77E5CB" /></Trigger>
                    <Trigger Property="IsEnabled" Value="False"><Setter TargetName="frame" Property="Opacity" Value="0.6" /></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value></Setter>
            </Style>
            """);
        trayIcon = LoadApplicationIcon();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Codex Limits", Visible = true };
        tray.DoubleClick += (_, _) => { Show(); Activate(); };
        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
            Place();
        };
        Loaded += async (_, _) => { Place(); timer.Start(); await RefreshAsync(); };
        timer.Tick += async (_, _) => { MaintainTaskbarOverlay(); UpdateDisplay(); await RefreshAsync(); };
        Closed += (_, _) =>
        {
            closed = true;
            timer.Stop();
            foreach (var account in accounts) { account.Details.IsOpen = false; account.Client?.Dispose(); }
            tray.Visible = false;
            tray.Dispose();
            trayIcon.Dispose();
        };
        Apply(settings);
    }

    private static System.Drawing.Icon LoadApplicationIcon()
    {
        var path = Environment.ProcessPath;
        return path == null
            ? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone()
            : System.Drawing.Icon.ExtractAssociatedIcon(path)
                ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private void Apply(Settings next)
    {
        string? executable = null, problem = null;
        if (!demo)
        {
            try { executable = next.FindCodex(); }
            catch (Exception error) when (error is IOException or ArgumentException) { problem = error.Message; }
        }
        generation++;
        foreach (var account in accounts) { account.Details.IsOpen = false; account.Client?.Dispose(); }
        accounts.Clear();
        settings = next;
        Topmost = settings.Widget.AlwaysOnTop;
        foreach (var config in settings.Accounts)
        {
            var view = new AccountView(config, executable == null ? null : new CodexClient(executable, config.CodexHome));
            if (problem != null) { view.Status = "Codex не найден"; view.Failed = true; }
            accounts.Add(view);
        }
        if (demo)
        {
            var samples = new AccountSnapshot[]
            {
                new("personal@example.com", "pro", new(new(72, DateTimeOffset.Now.AddHours(3)), new(38, DateTimeOffset.Now.AddDays(2))), DateTimeOffset.Now),
                new("work@example.com", "plus", new(null, new(84, DateTimeOffset.Now.AddDays(5))), DateTimeOffset.Now)
            };
            for (int i = 0; i < accounts.Count; i++)
            {
                accounts[i].Snapshot = samples[i % samples.Length] with
                {
                    Email = i < samples.Length ? samples[i].Email : $"account{i + 1}@example.com"
                };
                accounts[i].Status = "Демонстрация";
            }
        }
        BuildContent();
        BuildMenus();
        connectionProblem = problem;
        UpdateDisplay();
        if (IsLoaded) Place();
    }

    private void BuildContent()
    {
        cardsPanel = null;
        if (settings.Widget.DisplayMode == "cards")
        {
            bool vertical = settings.Widget.Edge is "left" or "right";
            cardsPanel = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal };
            foreach (var account in accounts)
            {
                var cardContent = BuildDetailsContent(account);
                account.Name = new Button { Content = "Войти через ChatGPT", Foreground = Ink, Margin = new Thickness(0, 8, 0, 0) };
                AutomationProperties.SetName(account.Name, "Войти: " + account.Config.Name);
                account.Name.Click += async (_, _) => await SignInAsync(account);
                cardContent.Children.Add(account.Name);
                cardsPanel.Children.Add(new Border
                {
                    Background = Brush("#151B24"), BorderBrush = Brush("#364153"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12), Child = new ScrollViewer
                    {
                        Content = cardContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Padding = new Thickness(0), BorderThickness = new Thickness(0)
                    },
                    Margin = account == accounts[^1] ? new Thickness(0) : vertical ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 6, 0)
                });
            }
            Content = new ScrollViewer
            {
                Content = cardsPanel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0), BorderThickness = new Thickness(0)
            };
            return;
        }
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 3, 5, 3) };
        for (int index = 0; index < accounts.Count; index++)
        {
            var account = accounts[index];
            var icon = new Grid { Width = 34, Height = 34 };
            for (int ring = 0; ring < 2; ring++)
            {
                double radius = ring == 0 ? 15 : 11.5;
                icon.Children.Add(new ShapePath { Data = new EllipseGeometry(new Point(17, 17), radius, radius), Stroke = Brush("#354153"), StrokeThickness = 2 });
                account.Rings[ring] = new ShapePath { Stroke = ring == 0 ? Mint : Purple, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                icon.Children.Add(account.Rings[ring]);
            }
            icon.Children.Add(new TextBlock
            {
                Text = StringInfo.GetNextTextElement(account.Config.Name.Trim()).ToUpperInvariant(),
                FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ink,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            account.Name = new Button
            {
                Content = icon, Margin = new Thickness(1, 0, 1, 0), Padding = new Thickness(0),
                Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand
            };
            AutomationProperties.SetAutomationId(account.Name, "AccountIcon" + index);
            account.Details = BuildDetails(account);
            account.Name.ToolTip = account.Details;
            ToolTipService.SetInitialShowDelay(account.Name, 150);
            ToolTipService.SetBetweenShowDelay(account.Name, 0);
            ToolTipService.SetShowDuration(account.Name, int.MaxValue);
            account.Name.Click += async (_, _) =>
            {
                if (!demo && account.Snapshot == null)
                {
                    account.Details.IsOpen = false;
                    await SignInAsync(account);
                }
                else account.Details.IsOpen = true;
            };
            account.Name.GotKeyboardFocus += (_, _) => account.Details.IsOpen = true;
            account.Name.LostKeyboardFocus += (_, _) => account.Details.IsOpen = false;
            account.Name.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) { account.Details.IsOpen = false; e.Handled = true; }
            };
            panel.Children.Add(account.Name);
        }
        Content = new Border
        {
            Background = Brush("#151B24"), BorderBrush = Brush("#364153"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = panel }
        };
    }

    private StackPanel BuildDetailsContent(AccountView account)
    {
        var panel = new StackPanel { Width = 270, Margin = new Thickness(15) };
        panel.Children.Add(new TextBlock { Text = demo ? "CODEX / DEMO" : "CODEX", Foreground = Mint, FontSize = 10, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBlock { Text = account.Config.Name, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        account.Email = new TextBlock { Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        account.Caption = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        panel.Children.Add(account.Email);
        panel.Children.Add(account.Caption);
        for (int i = 0; i < 2; i++)
        {
            var line = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
            line.Children.Add(new TextBlock { Text = i == 0 ? "5 часов" : "Неделя", Foreground = Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            account.Values[i] = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = i == 0 ? Mint : Purple, TextAlignment = TextAlignment.Right };
            line.Children.Add(account.Values[i]);
            panel.Children.Add(line);
            account.Bars[i] = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, BorderThickness = new Thickness(0), Foreground = i == 0 ? Mint : Purple, Background = Brush("#303A49") };
            panel.Children.Add(account.Bars[i]);
            account.Resets[i] = new TextBlock { FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };
            panel.Children.Add(account.Resets[i]);
        }
        account.Footer = new TextBlock { FontSize = 10, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 13, 0, 0) };
        panel.Children.Add(account.Footer);
        return panel;
    }

    private ToolTip BuildDetails(AccountView account)
    {
        var tip = new ToolTip
        {
            Content = BuildDetailsContent(account), Foreground = Ink, FontFamily = FontFamily, Padding = new Thickness(0), HasDropShadow = false,
            PlacementTarget = account.Name,
            Placement = settings.Widget.Edge switch { "bottom" => PlacementMode.Top, "left" => PlacementMode.Right, "right" => PlacementMode.Left, _ => PlacementMode.Bottom }
        };
        tip.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ToolTip">
              <Border Background="#151B24" BorderBrush="#364153" BorderThickness="1" CornerRadius="12">
                <ContentPresenter />
              </Border>
            </ControlTemplate>
            """);
        return tip;
    }

    private static Geometry RingGeometry(double remaining, double radius)
    {
        if (remaining <= 0) return Geometry.Empty;
        if (remaining >= 100) return new EllipseGeometry(new Point(17, 17), radius, radius);
        double angle = remaining / 100 * Math.PI * 2;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(17, 17 - radius), false, false);
            context.ArcTo(new Point(17 + Math.Sin(angle) * radius, 17 - Math.Cos(angle) * radius),
                new Size(radius, radius), 0, remaining > 50, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void BuildMenus()
    {
        var context = new ContextMenu();
        var trayMenu = new Forms.ContextMenuStrip();
        void Item(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => action();
            context.Items.Add(item);
            trayMenu.Items.Add(label, null, (_, _) => Dispatcher.Invoke(action));
        }
        Item("Обновить сейчас", async () => await RefreshAsync(force: true));
        foreach (var account in accounts)
            Item("Войти: " + account.Config.Name, async () => await SignInAsync(account));
        Item("Открыть конфигурацию", OpenConfig);
        Item("Применить конфигурацию", Reload);
        Item("Показать виджет", () => { Show(); Place(); });
        var version = "Версия " + ProductInfo.Version;
        context.Items.Add(new MenuItem { Header = version, IsEnabled = false });
        trayMenu.Items.Add(new Forms.ToolStripMenuItem(version) { Enabled = false });
        Item("Выход", Close);
        ContextMenu = context;
        var old = tray.ContextMenuStrip;
        tray.ContextMenuStrip = trayMenu;
        old?.Dispose();
    }

    private async Task RefreshAsync(bool force = false)
    {
        if (refreshing || demo || closed) return;
        refreshing = true;
        var version = generation;
        var resetDetected = 0;
        try
        {
            await Task.WhenAll(accounts.Where(a => a.Client != null && !a.SigningIn &&
                DateTimeOffset.Now >= a.RateLimitUntil && (force || DateTimeOffset.Now >= a.NextRefresh)).Select(async account =>
            {
                try
                {
                    var snapshot = await account.Client!.ReadAsync();
                    if (version != generation || closed) return;
                    if (settings.NotifyOnLimitReset && account.Snapshot is { } previous && Limits.WasReset(previous.Limits, snapshot.Limits))
                        Interlocked.Exchange(ref resetDetected, 1);
                    account.Snapshot = snapshot;
                    account.Failed = false;
                    account.Failures = 0;
                    account.Status = "Подключён";
                    account.NextRefresh = DateTimeOffset.Now.AddSeconds(settings.RefreshSeconds);
                }
                catch (Exception error)
                {
                    if (version != generation || closed) return;
                    account.Failed = true;
                    account.Failures++;
                    account.Status = error switch
                    {
                        CodexException known => known.Message,
                        TimeoutException => "Нет ответа от Codex",
                        UnauthorizedAccessException => "Нет доступа к профилю",
                        _ => "Не удалось подключиться"
                    };
                    var wait = Math.Min(900, Math.Max(60, settings.RefreshSeconds) * Math.Pow(2, Math.Min(account.Failures - 1, 4)));
                    account.NextRefresh = DateTimeOffset.Now.AddSeconds(wait);
                    if (error is CodexException { Kind: FailureKind.RateLimited }) account.RateLimitUntil = account.NextRefresh;
                }
            }));
            if (resetDetected != 0) SystemSounds.Asterisk.Play();
        }
        finally { refreshing = false; if (!closed) UpdateDisplay(); }
    }

    private void UpdateDisplay()
    {
        var duplicateEmails = accounts.Select(a => a.Snapshot?.Email).OfType<string>()
            .Where(email => email.Length != 0).GroupBy(email => email, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var account in accounts)
        {
            account.Caption.Text = account.SigningIn ? "Вход в браузере…" : account.Failed && account.Snapshot != null ? "Данные устарели · " + account.Status : account.Status;
            if (account.Snapshot?.Plan is { } plan) account.Caption.Text = plan.ToUpperInvariant() + " · " + account.Caption.Text;
            account.Caption.Foreground = account.Failed ? Brush("#FFC38A") : Muted;
            account.Email.Text = account.Snapshot?.Email ?? "Аккаунт ещё не подключён";
            account.Name.Opacity = account.Failed ? 0.55 : 1;
            if (cardsPanel != null)
            {
                account.Name.Visibility = account.Snapshot == null ? Visibility.Visible : Visibility.Collapsed;
                account.Name.IsEnabled = !loggingIn;
            }
            account.Footer.Text = (demo ? "Пример данных · " : "") + (account.Snapshot is { } snapshot
                ? $"Остаток лимитов · обновлено {snapshot.UpdatedAt:HH:mm:ss}"
                : cardsPanel == null ? "Нажмите иконку для входа через ChatGPT" : "Войдите через ChatGPT") + "\nПравый клик — меню";
            if (account.Snapshot?.Email is { } email && duplicateEmails.Contains(email))
                account.Footer.Text += "\nПроверьте профили: одинаковый email";
            if (monitorMissing) account.Footer.Text += "\nМонитор недоступен · показано на основном";
            if (connectionProblem != null) account.Footer.Text += "\n" + connectionProblem;
            var windows = new[] { account.Snapshot?.Limits.FiveHour, account.Snapshot?.Limits.Weekly };
            for (int i = 0; i < 2; i++)
            {
                var limit = windows[i];
                account.Values[i].Text = limit is null ? "—" : $"{Math.Floor(limit.Remaining):0}%";
                account.Bars[i].Value = limit?.Remaining ?? 0;
                account.Rings[i].Data = RingGeometry(limit?.Remaining ?? 0, i == 0 ? 15 : 11.5);
                account.Values[i].Opacity = account.Bars[i].Opacity = account.Failed ? 0.45 : 1;
                string detail = limit is null ? "Нет данных об этом лимите" :
                    Limits.ResetText(limit, DateTimeOffset.Now) +
                    (limit.ResetsAt is { } reset ? $"\n{reset.ToLocalTime():dd.MM.yyyy HH:mm}" : "");
                account.Resets[i].Text = detail;
                AutomationProperties.SetName(account.Bars[i], account.Config.Name + (i == 0 ? ", 5 часов. " : ", неделя. ") + detail);
            }
            if (cardsPanel == null)
                AutomationProperties.SetName(account.Name, $"{account.Config.Name}. {account.Caption.Text}. 5 часов: {account.Values[0].Text}. Неделя: {account.Values[1].Text}");
        }
        if (cardsPanel != null && IsLoaded) Place();
    }

    private async Task SignInAsync(AccountView account)
    {
        if (demo || loggingIn || closed) return;
        if (account.Client == null) { MessageBox.Show(connectionProblem, "Codex Limits"); return; }
        loggingIn = true;
        account.SigningIn = true;
        account.Snapshot = null;
        var version = generation;
        UpdateDisplay();
        try
        {
            await account.Client.LoginAsync(url => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }));
            account.Failed = false;
            account.Status = "Вход выполнен";
            account.NextRefresh = account.RateLimitUntil = DateTimeOffset.MinValue;
        }
        catch (Exception error)
        {
            if (closed || version != generation) return;
            account.Failed = true;
            account.Status = error is CodexException known ? known.Message : "Вход не завершён. Повторите попытку";
            MessageBox.Show(account.Status, "Codex Limits", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            loggingIn = false;
            account.SigningIn = false;
            if (!closed) { UpdateDisplay(); await RefreshAsync(force: true); }
        }
    }

    private void OpenConfig()
    {
        try
        {
            if (!File.Exists(configPath))
            {
                MessageBox.Show("В режиме демонстрации конфигурация не создаётся. Запустите приложение без --demo или укажите --config.", "Codex Limits");
                return;
            }
            var start = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
            start.ArgumentList.Add(configPath);
            Process.Start(start);
        }
        catch (Exception) { MessageBox.Show("Не удалось открыть файл: " + configPath, "Codex Limits"); }
    }

    private void Reload()
    {
        try
        {
            if (loggingIn) { MessageBox.Show("Завершите вход в аккаунт перед изменением настроек.", "Codex Limits"); return; }
            Apply(Settings.Load(configPath));
            _ = RefreshAsync();
        }
        catch (Exception error) { MessageBox.Show("Настройки не применены.\n" + error.Message, "Codex Limits"); }
    }

    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || placing || closed) return;
        placing = true;
        try
        {
            var monitor = settings.Widget.Monitor == "primary" ? Forms.Screen.PrimaryScreen :
                Forms.Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, settings.Widget.Monitor, StringComparison.OrdinalIgnoreCase));
            monitorMissing = monitor is null;
            monitor ??= Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
            var area = settings.Widget.RespectTaskbar ? monitor.WorkingArea : monitor.Bounds;
            var screen = monitor.Bounds;
            var dpi = GetDpiForWindow(handle);
            double scale = (dpi == 0 ? 96 : dpi) / 96.0;
            int? width = null, height = null;
            if (cardsPanel != null)
            {
                foreach (Border card in cardsPanel.Children)
                {
                    card.Width = settings.Widget.CardWidthPx == 0 ? 302 : settings.Widget.CardWidthPx / scale;
                    card.Height = settings.Widget.CardHeightPx == 0 ? double.NaN : settings.Widget.CardHeightPx / scale;
                    ((StackPanel)((ScrollViewer)card.Child).Content).Width = double.NaN;
                }
                cardsPanel.InvalidateMeasure();
                cardsPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                width = (int)Math.Ceiling(cardsPanel.DesiredSize.Width * scale);
                height = (int)Math.Ceiling(cardsPanel.DesiredSize.Height * scale);
                var available = settings.Widget.MarginPx < 0 ? screen : area;
                // Reserve the outer scrollbars' space so they do not cover a card's edge.
                if (height > available.Height) width += (int)Math.Ceiling(SystemParameters.VerticalScrollBarWidth * scale);
                if (width > available.Width) height += (int)Math.Ceiling(SystemParameters.HorizontalScrollBarHeight * scale);
            }
            var rect = Placement.Calculate(new(area.X, area.Y, area.Width, area.Height), settings.Widget,
                new(screen.X, screen.Y, screen.Width, screen.Height), width, height);
            overlapsTaskbar = !monitor.WorkingArea.Contains(new System.Drawing.Rectangle(rect.X, rect.Y, rect.Width, rect.Height));
            Width = rect.Width * 96.0 / (dpi == 0 ? 96 : dpi);
            Height = rect.Height * 96.0 / (dpi == 0 ? 96 : dpi);
            if (lastPlacement != rect)
            {
                SetWindowPos(handle, settings.Widget.AlwaysOnTop ? new IntPtr(-1) : new IntPtr(-2), rect.X, rect.Y, rect.Width, rect.Height, 0x0010);
                lastPlacement = rect;
            }
        }
        finally { placing = false; }
    }

    private void MaintainTaskbarOverlay()
    {
        if (closed || !IsVisible || !Topmost || !overlapsTaskbar ||
            accounts.Any(a => a.Details.IsOpen) || ContextMenu?.IsOpen == true || tray.ContextMenuStrip?.Visible == true) return;
        // ponytail: reuse the 1s timer; shell activation can cover us until its next tick.
        // Do not raise over our own tooltip or menus, and never take keyboard focus.
        SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Reapply physical-pixel placement after monitor, DPI or taskbar work-area changes.
        if (message is 0x02E0 or 0x007E || (message == 0x001A && wParam.ToInt64() == 0x002F))
        {
            lastPlacement = null;
            Dispatcher.BeginInvoke(Place, DispatcherPriority.Loaded);
        }
        return IntPtr.Zero;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
