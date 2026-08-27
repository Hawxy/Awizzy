using Avalonia;
using Velopack;

namespace Awizzy.App;

internal static class Program
{
    // Avalonia configuration and startup. Application host wiring lives in App.axaml.cs.
    [STAThread]
    public static void Main(string[] args)
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

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
