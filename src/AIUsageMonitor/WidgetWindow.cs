using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Media;
using System.Windows.Input;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using ShapePath = System.Windows.Shapes.Path;

namespace AIUsageMonitor;

public sealed class WidgetWindow : Window
{
    private static readonly Brush Ink = Brush("#EEF4FC"), Muted = Brush("#9CAFC5"), Mint = Brush("#77E5CB"), Purple = Brush("#B6A3FF"), Claude = Brush("#FFBF8A");
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
    private Point? dragStart;
    private bool dragging;

    private sealed class AccountView(AccountSettings config, IUsageClient? client)
    {
        public AccountSettings Config = config;
        public IUsageClient? Client = client;
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
        Title = "AI Usage Monitor";
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
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "AI Usage Monitor", Visible = true };
        tray.DoubleClick += (_, _) => { Show(); Activate(); };
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            // Leave scrollbars and the sign-in button to their normal mouse handling.
            for (var source = e.OriginalSource as DependencyObject; source != null && source != this;
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
                if (source is ScrollBar || ((cardsPanel != null || accounts.Count == 0) && source is ButtonBase)) return;
            dragStart = PointToScreen(e.GetPosition(this));
        };
        PreviewMouseLeftButtonUp += (_, _) => dragStart = null;
        PreviewMouseMove += (_, e) =>
        {
            if (dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed || dragging) return;
            var point = PointToScreen(e.GetPosition(this));
            if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragStart = null;
            e.Handled = true;
            foreach (var account in accounts) account.Details.IsOpen = false;
            Mouse.Capture(null);
            dragging = true;
            try { DragMove(); SavePosition(); }
            catch (Exception error) { MessageBox.Show("Позиция не сохранена.\n" + error.Message, "AI Usage Monitor"); }
            finally { dragging = false; lastPlacement = null; Place(); }
        };
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
        if (!demo && next.Accounts.Any(a => a.Provider == "codex"))
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
            IUsageClient? client = config.Provider == "claude" ? new ClaudeClient(config.ClaudeConfigDir!)
                : executable == null ? null : new CodexClient(executable, config.CodexHome!);
            var view = new AccountView(config, client);
            if (config.Provider == "codex" && problem != null) { view.Status = "Codex не найден"; view.Failed = true; }
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
        if (accounts.Count == 0)
        {
            var add = new Button { Content = "+", FontSize = 22, Foreground = Ink, Background = Brushes.Transparent,
                ToolTip = "Добавить аккаунт", Cursor = System.Windows.Input.Cursors.Hand };
            AutomationProperties.SetName(add, "Добавить аккаунт");
            add.Click += async (_, _) => await AddAccountAsync();
            Content = new Border { Background = Brush("#151B24"), BorderBrush = Brush("#364153"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = add };
            return;
        }
        if (settings.Widget.DisplayMode == "cards")
        {
            bool vertical = settings.Widget.Edge is "left" or "right";
            cardsPanel = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal };
            foreach (var account in accounts)
            {
                var cardContent = BuildDetailsContent(account);
                account.Name = new Button { Content = account.Config.Provider == "claude" ? "Как войти в Claude Code" : "Войти через ChatGPT", Foreground = Ink, Margin = new Thickness(0, 8, 0, 0) };
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
            var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/AIUsageMonitor;component/Assets/provider_" +
                    (account.Config.Provider == "claude" ? "claude" : "openai") + ".png")),
                Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center
            });
            label.Children.Add(new TextBlock { Text = StringInfo.GetNextTextElement(account.Config.Name.Trim()).ToUpperInvariant(),
                FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0) });
            icon.Children.Add(label);
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
        panel.Children.Add(new TextBlock { Text = (account.Config.Provider == "claude" ? "CLAUDE CODE" : "GPT / CODEX") + (demo ? " / DEMO" : ""),
            Foreground = account.Config.Provider == "claude" ? Claude : Mint, FontSize = 10, Margin = new Thickness(0, 0, 0, 8) });
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
        Item("Добавить аккаунт", async () => await AddAccountAsync());
        if (accounts.Count > 0)
        {
            var remove = new MenuItem { Header = "Убрать аккаунт" };
            var trayRemove = new Forms.ToolStripMenuItem("Убрать аккаунт");
            foreach (var account in accounts)
            {
                string label = (account.Config.Provider == "claude" ? "Claude · " : "Codex · ") + account.Config.Name;
                var item = new MenuItem { Header = label };
                item.Click += (_, _) => RemoveAccount(account);
                remove.Items.Add(item);
                trayRemove.DropDownItems.Add(label, null, (_, _) => Dispatcher.Invoke(() => RemoveAccount(account)));
            }
            context.Items.Add(remove);
            trayMenu.Items.Add(trayRemove);
        }
        Item(settings.Widget.DisplayMode == "icons" ? "Показать карточки" : "Показать иконки",
            () => SetDisplayMode(settings.Widget.DisplayMode == "icons" ? "cards" : "icons"));
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
                    account.NextRefresh = DateTimeOffset.Now.AddSeconds(account.Config.Provider == "claude" ? Math.Max(300, settings.RefreshSeconds) : settings.RefreshSeconds);
                }
                catch (Exception error)
                {
                    if (version != generation || closed) return;
                    account.Failed = true;
                    account.Failures++;
                    account.Status = error switch
                    {
                        CodexException known => known.Message,
                        TimeoutException => "Нет ответа от сервиса",
                        UnauthorizedAccessException => "Нет доступа к профилю",
                        _ => "Не удалось подключиться"
                    };
                    var wait = Math.Min(900, Math.Max(account.Config.Provider == "claude" ? 300 : 60, settings.RefreshSeconds) * Math.Pow(2, Math.Min(account.Failures - 1, 4)));
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
            account.Email.Text = account.Snapshot?.Email ?? (account.Config.Provider == "claude" ? "Claude Code" : "Аккаунт ещё не подключён");
            account.Name.Opacity = account.Failed ? 0.55 : 1;
            if (cardsPanel != null)
            {
                account.Name.Visibility = account.Snapshot == null ? Visibility.Visible : Visibility.Collapsed;
                account.Name.IsEnabled = !loggingIn;
            }
            account.Footer.Text = (demo ? "Пример данных · " : "") + (account.Snapshot is { } snapshot
                ? $"Остаток лимитов · обновлено {snapshot.UpdatedAt:HH:mm:ss}"
                : account.Config.Provider == "claude" ? "Войдите в Claude Code в указанном профиле"
                : cardsPanel == null ? "Нажмите иконку для входа через ChatGPT" : "Войдите через ChatGPT") + "\nПравый клик — меню";
            if (account.Snapshot?.Email is { } email && duplicateEmails.Contains(email))
                account.Footer.Text += "\nПроверьте профили: одинаковый email";
            if (monitorMissing) account.Footer.Text += "\nМонитор недоступен · показано на основном";
            if (connectionProblem != null && account.Config.Provider == "codex") account.Footer.Text += "\n" + connectionProblem;
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
                AutomationProperties.SetName(account.Name, $"{(account.Config.Provider == "claude" ? "Claude" : "GPT Codex")}, {account.Config.Name}. {account.Caption.Text}. 5 часов: {account.Values[0].Text}. Неделя: {account.Values[1].Text}");
        }
        if (cardsPanel != null && IsLoaded) Place();
    }

    private async Task SignInAsync(AccountView account)
    {
        if (demo || loggingIn || closed) return;
        if (account.Config.Provider == "claude")
        {
            MessageBox.Show("Войдите в Claude Code с CLAUDE_CONFIG_DIR=" + account.Config.ClaudeConfigDir +
                " и затем нажмите «Обновить сейчас». Приложение читает сохранённый вход, но не изменяет его.", "Claude Code");
            return;
        }
        if (account.Client == null) { MessageBox.Show(connectionProblem, "AI Usage Monitor"); return; }
        loggingIn = true;
        account.SigningIn = true;
        account.Snapshot = null;
        var version = generation;
        UpdateDisplay();
        try
        {
            await ((CodexClient)account.Client).LoginAsync(url => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }));
            account.Failed = false;
            account.Status = "Вход выполнен";
            account.NextRefresh = account.RateLimitUntil = DateTimeOffset.MinValue;
        }
        catch (Exception error)
        {
            if (closed || version != generation) return;
            account.Failed = true;
            account.Status = error is CodexException known ? known.Message : "Вход не завершён. Повторите попытку";
            MessageBox.Show(account.Status, "AI Usage Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            loggingIn = false;
            account.SigningIn = false;
            if (!closed) { UpdateDisplay(); await RefreshAsync(force: true); }
        }
    }

    private async Task AddAccountAsync()
    {
        if (demo) { MessageBox.Show("В демонстрации аккаунты не меняются.", "AI Usage Monitor"); return; }
        if (loggingIn) { MessageBox.Show("Завершите вход перед изменением аккаунтов.", "AI Usage Monitor"); return; }
        if (!IsVisible) Show();
        var dialog = new Window { Title = "Добавить аккаунт", Owner = this, Width = 440,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var fields = new StackPanel { Margin = new Thickness(20) };
        var provider = new ComboBox { Margin = new Thickness(0, 4, 0, 12), Height = 28 };
        provider.Items.Add("Codex");
        provider.Items.Add("Claude");
        provider.SelectedIndex = 0;
        AutomationProperties.SetName(provider, "Провайдер");
        fields.Children.Add(new TextBlock { Text = "Провайдер" });
        fields.Children.Add(provider);
        var name = new TextBox { Margin = new Thickness(0, 4, 0, 12), MaxLength = 60, Height = 28 };
        AutomationProperties.SetName(name, "Название аккаунта");
        fields.Children.Add(new TextBlock { Text = "Название аккаунта" });
        fields.Children.Add(name);
        var pathLabel = new TextBlock();
        fields.Children.Add(pathLabel);
        var folder = new TextBox { Height = 28 };
        AutomationProperties.SetName(folder, "Папка профиля");
        var browse = new Button { Content = "Обзор…", Width = 82, Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            using var picker = new Forms.FolderBrowserDialog { Description = "Выберите папку профиля", ShowNewFolderButton = true };
            try
            {
                var selected = Settings.ExpandPath(folder.Text.Trim());
                if (Directory.Exists(selected)) picker.SelectedPath = selected;
            }
            catch (Exception) { }
            if (picker.ShowDialog() == Forms.DialogResult.OK) folder.Text = picker.SelectedPath;
        };
        var folderRow = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(folder);
        fields.Children.Add(folderRow);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Muted,
            Margin = new Thickness(0, 0, 0, 16) };
        fields.Children.Add(hint);
        void UpdateHint()
        {
            bool claude = provider.SelectedIndex == 1;
            pathLabel.Text = claude ? "Папка профиля Claude Code" : "Папка профиля Codex";
            hint.Text = claude
                ? "Например, %USERPROFILE%/.claude. Сначала войдите в Claude Code с этой папкой профиля."
                : "Укажите отдельную папку профиля; после добавления откроется вход через ChatGPT.";
        }
        provider.SelectionChanged += (_, _) => UpdateHint();
        UpdateHint();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Отмена", Width = 84, Height = 30, IsCancel = true };
        var add = new Button { Content = "Добавить", Width = 92, Height = 30,
            Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        fields.Children.Add(buttons);
        dialog.Content = fields;
        AccountSettings? added = null;
        add.Click += (_, _) =>
        {
            try
            {
                string title = name.Text.Trim(), path = folder.Text.Trim();
                var entry = provider.SelectedIndex == 1
                    ? new AccountSettings(title, Provider: "claude", ClaudeConfigDir: path)
                    : new AccountSettings(title, CodexHome: path);
                Settings.EditAccounts(configPath, settings.Accounts, existing => [.. existing, entry]);
                added = entry;
                dialog.DialogResult = true;
            }
            catch (Exception error)
            {
                MessageBox.Show(dialog, "Аккаунт не добавлен.\n" + error.Message, "AI Usage Monitor",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        if (dialog.ShowDialog() != true || added is null) return;
        try
        {
            Apply(Settings.Load(configPath));
            if (added.Provider == "codex") await SignInAsync(accounts[^1]);
            else await RefreshAsync(force: true);
        }
        catch (Exception error) { MessageBox.Show("Настройки сохранены, но не применены.\n" + error.Message, "AI Usage Monitor"); }
    }

    private void RemoveAccount(AccountView account)
    {
        if (demo) { MessageBox.Show("В демонстрации аккаунты не меняются.", "AI Usage Monitor"); return; }
        if (loggingIn) { MessageBox.Show("Завершите вход перед изменением аккаунтов.", "AI Usage Monitor"); return; }
        int index = accounts.IndexOf(account);
        if (index < 0) return;
        string profile = account.Config.Provider == "claude" ? account.Config.ClaudeConfigDir! : account.Config.CodexHome!;
        if (MessageBox.Show("Убрать аккаунт «" + account.Config.Name + "» из приложения?\n\n" +
            "Папка профиля и данные входа останутся на диске:\n" + profile,
            "Убрать аккаунт", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            Settings.EditAccounts(configPath, settings.Accounts,
                existing => existing.Where((_, position) => position != index).ToArray());
            Apply(Settings.Load(configPath));
            _ = RefreshAsync();
        }
        catch (Exception error) { MessageBox.Show("Аккаунт не убран.\n" + error.Message, "AI Usage Monitor"); }
    }

    private void OpenConfig()
    {
        try
        {
            if (!File.Exists(configPath))
            {
                MessageBox.Show("В режиме демонстрации конфигурация не создаётся. Запустите приложение без --demo или укажите --config.", "AI Usage Monitor");
                return;
            }
            var start = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
            start.ArgumentList.Add(configPath);
            Process.Start(start);
        }
        catch (Exception) { MessageBox.Show("Не удалось открыть файл: " + configPath, "AI Usage Monitor"); }
    }

    private void Reload()
    {
        try
        {
            if (loggingIn) { MessageBox.Show("Завершите вход в аккаунт перед изменением настроек.", "AI Usage Monitor"); return; }
            Apply(Settings.Load(configPath));
            _ = RefreshAsync();
        }
        catch (Exception error) { MessageBox.Show("Настройки не применены.\n" + error.Message, "AI Usage Monitor"); }
    }

    private void SetDisplayMode(string displayMode)
    {
        try
        {
            if (loggingIn) { MessageBox.Show("Завершите вход в аккаунт перед изменением настроек.", "AI Usage Monitor"); return; }
            Settings.SaveWidgetValues(configPath, new() { ["displayMode"] = displayMode });
            Reload();
        }
        catch (Exception error) { MessageBox.Show("Режим отображения не изменён.\n" + error.Message, "AI Usage Monitor"); }
    }

    private void SavePosition()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(handle, out var bounds)) throw new IOException("Не удалось прочитать позицию окна.");
        var monitor = Forms.Screen.FromHandle(handle);
        var screen = monitor.Bounds;
        var widget = Placement.FromPosition(new(screen.X, screen.Y, screen.Width, screen.Height),
            new(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), settings.Widget, monitor.DeviceName);
        if (!demo) Settings.SaveWidgetValues(configPath, new()
        {
            ["monitor"] = widget.Monitor, ["offsetPx"] = widget.OffsetPx,
            ["marginPx"] = widget.MarginPx, ["respectTaskbar"] = widget.RespectTaskbar
        });
        settings = settings with { Widget = widget };
    }

    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || placing || closed || dragging) return;
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
                    ((StackPanel)((ScrollViewer)card.Child).Content).Width = Math.Max(0, card.Width - 30);
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
        if (closed || dragging || !IsVisible || !Topmost || !overlapsTaskbar ||
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
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
