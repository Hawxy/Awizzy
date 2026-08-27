using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Awizzy.Core;
using Awizzy.Core.Persistence;
using Awizzy.App.ViewModels;
using Awizzy.App.Views;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Awizzy.App;

public class App : Application
{
    private IHost? _host;
    private Services.TrayIconManager? _trayIcon;

    /// <summary>Set when the user chose Exit; distinguishes real shutdown from hide-to-tray.</summary>
    public static bool IsExiting { get; private set; }

    public static void ActivateMainWindow()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            window.Show();
            window.WindowState = Avalonia.Controls.WindowState.Normal;
            window.Activate();
        }
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // --demo runs against a throwaway workspace in %TEMP% seeded with sample data.
        var isDemo = Environment.GetCommandLineArgs().Contains("--demo");
        var paths = isDemo
            ? new AppPaths(Path.Combine(Path.GetTempPath(), "AwizzyDemo"))
            : new AppPaths();

        // The file logger's RootPath requires an existing directory; a fresh install
        // has none and the host build crashes without it.
        Directory.CreateDirectory(paths.LogDirectory);
        CleanupOldLogs(paths.LogDirectory);

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddFile(options =>
                {
                    options.RootPath = paths.LogDirectory;
                    options.Files = [new LogFileOptions { Path = "app-<date>.log" }];
                });
            })
            .ConfigureServices(services =>
            {
                services.AddAwizzyCore();
                services.AddSingleton(paths);
                services.AddSingleton<SukiUI.Dialogs.ISukiDialogManager, SukiUI.Dialogs.SukiDialogManager>();
                services.AddSingleton<SukiUI.Toasts.ISukiToastManager, SukiUI.Toasts.SukiToastManager>();
                services.AddSingleton<Services.IDialogService, Services.DialogService>();
                services.AddSingleton<Services.IClipboardService, Services.ClipboardService>();
                services.AddSingleton<Services.SessionActionsService>();
                services.AddSingleton<Mcp.McpChangeNotifier>();
                services.AddSingleton<Core.Abstractions.IMainThreadDispatcher, Services.AvaloniaMainThreadDispatcher>();
                services.AddSingleton<Mcp.IMcpServerHost, Mcp.McpServerHost>();
                services.AddSingleton<Services.UpdateService>();
                services.AddSingleton<MainWindowViewModel>();
            })
            .Build();

        _host.Start();

        if (isDemo)
            DemoData.Seed(_host.Services);

        var settings = _host.Services.GetRequiredService<Core.Services.WorkspaceState>().Workspace.Settings;
        Services.ThemeApplier.Apply(settings.Theme);

        if (settings.McpServerEnabled)
        {
            var mcpHost = _host.Services.GetRequiredService<Mcp.IMcpServerHost>();
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            var mcpPort = settings.McpServerPort;
            _ = Task.Run(async () =>
            {
                try
                {
                    await mcpHost.StartAsync(mcpPort);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to start the MCP server on port {Port}.", mcpPort);
                }
            });
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the window hides to the tray; only the tray's Exit ends the app.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            var mainWindow = new MainWindow
            {
                DataContext = _host.Services.GetRequiredService<MainWindowViewModel>(),
            };
            mainWindow.Closing += (_, e) =>
            {
                if (!IsExiting)
                {
                    e.Cancel = true;
                    mainWindow.Hide();
                }
            };
            desktop.MainWindow = mainWindow;

            _trayIcon = new Services.TrayIconManager(
                _host.Services.GetRequiredService<Core.Services.WorkspaceState>(),
                _host.Services.GetRequiredService<Core.Abstractions.ISessionManager>(),
                showMainWindow: ActivateMainWindow,
                exit: () =>
                {
                    IsExiting = true;
                    desktop.Shutdown();
                });

            desktop.ShutdownRequested += OnShutdownRequested;

            if (!isDemo)
                _ = _host.Services.GetRequiredService<Services.UpdateService>().RunPeriodicChecksAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Keeps the newest 14 daily log files; the file logger has no retention of its own.</summary>
    private static void CleanupOldLogs(string logDirectory)
    {
        try
        {
            if (!Directory.Exists(logDirectory))
                return;
            foreach (var stale in Directory.GetFiles(logDirectory, "app-*.log").OrderDescending().Skip(14))
                File.Delete(stale);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best-effort; never block startup on it.
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // On macOS Cmd+Q requests shutdown directly; without this the window Closing
        // handler would cancel it into a hide instead.
        IsExiting = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        (_host?.Services.GetService<Mcp.IMcpServerHost>() as IDisposable)?.Dispose();
        _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        _host?.Dispose();
        _host = null;
    }
}
