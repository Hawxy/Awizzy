using Avalonia;
using Velopack;

namespace Awizzy.App;

internal static class Program
{
    // Avalonia configuration and startup. Application host wiring lives in App.axaml.cs.
    [STAThread]
    public static void Main(string[] args)
    {
        // A GUI app has no console, and Native AOT fail-fasts on unhandled exceptions
        // (0xc0000409 in Event Viewer with no detail), so record them ourselves.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject);
        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }
    }

    private static void Run(string[] args)
    {
        // Velopack's install/update/uninstall hooks; must run before anything else.
        VelopackApp.Build().Run();

        // One instance per mode (demo and real may coexist); a second launch
        // signals the first to bring its window to the front, then exits.
        var suffix = args.Contains("--demo") ? "-demo" : string.Empty;
        using var instanceMutex = new Mutex(
            initiallyOwned: true, @"Local\Awizzy" + suffix, out var isFirstInstance);
        using var activateSignal = new EventWaitHandle(
            false, EventResetMode.AutoReset, @"Local\Awizzy-activate" + suffix);

        if (!isFirstInstance)
        {
            activateSignal.Set();
            return;
        }

        var listener = new Thread(() =>
        {
            while (activateSignal.WaitOne())
            {
                if (Application.Current is not null)
                    Avalonia.Threading.Dispatcher.UIThread.Post(App.ActivateMainWindow);
            }
        })
        { IsBackground = true };
        listener.Start();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Best-effort, self-contained: must work when nothing else has initialized.</summary>
    private static void LogCrash(object exception)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Awizzy", "logs");
            Directory.CreateDirectory(dir);
            var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
            File.AppendAllText(
                Path.Combine(dir, "crash.log"),
                $"[{DateTimeOffset.Now:O}] Awizzy {version} on {Environment.OSVersion}{Environment.NewLine}"
                + $"{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Crash logging must never mask the original failure.
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
