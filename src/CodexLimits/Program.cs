using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CodexLimits;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            string? config = null, screenshot = null;
            bool demo = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--demo": demo = true; break;
                    case "--config" when i + 1 < args.Length: config = Path.GetFullPath(args[++i]); break;
                    case "--smoke-test" when i + 1 < args.Length: screenshot = Path.GetFullPath(args[++i]); demo = true; break;
                    default: throw new InvalidDataException("Аргументы: --demo, --config <путь>, --smoke-test <PNG>.");
                }
            }
            config ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexLimits", "config.json");
            Settings settings;
            if (File.Exists(config)) settings = Settings.Load(config);
            else
            {
                settings = new Settings().Validate();
                if (!demo)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(config)!);
                    var example = Path.Combine(AppContext.BaseDirectory, "config.example.json");
                    if (File.Exists(example)) File.Copy(example, config);
                    else
                    {
                        using var file = new FileStream(config, FileMode.CreateNew, FileAccess.Write);
                        JsonSerializer.Serialize(file, new Settings(), Settings.JsonOptions);
                    }
                }
            }
            using var mutex = new Mutex(true, "Local\\CodexLimits-" + (demo ? "Demo" : "Live"), out var first);
            if (!first && screenshot == null)
            {
                MessageBox.Show("AI Usage Monitor уже работает. Меню доступно через значок в трее.", "AI Usage Monitor");
                return 0;
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new WidgetWindow(config, settings, demo);
            if (screenshot != null)
            {
                var target = screenshot;
                window.Loaded += async (_, _) =>
                {
                    try
                    {
                        await Task.Delay(350);
                        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                        var dpi = VisualTreeHelper.GetDpi(window);
                        var bitmap = new RenderTargetBitmap((int)Math.Round(window.ActualWidth * dpi.DpiScaleX),
                            (int)Math.Round(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        using var stream = File.Create(target);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        encoder.Save(stream);
                    }
                    catch (Exception error)
                    {
                        File.WriteAllText(target + ".error.txt", error.GetType().Name + ": " + error.Message);
                        Environment.ExitCode = 1;
                    }
                    finally { window.Close(); }
                };
            }
            app.Run(window);
            return Environment.ExitCode;
        }
        catch (Exception error)
        {
            MessageBox.Show("Не удалось запустить виджет.\n\n" + error.Message, "AI Usage Monitor", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
