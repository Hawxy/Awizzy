using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using Awizzy.Core.Services;

namespace Awizzy.App.Services;

public sealed class TrayIconManager : IDisposable
{
    private readonly WorkspaceState _state;
    private readonly ISessionManager _sessionManager;
    private readonly Action _showMainWindow;
    private readonly Action _exit;
    private readonly TrayIcon _trayIcon;
    private readonly DispatcherTimer _refreshTimer;

    public TrayIconManager(
        WorkspaceState state,
        ISessionManager sessionManager,
        Action showMainWindow,
        Action exit)
    {
        _state = state;
        _sessionManager = sessionManager;
        _showMainWindow = showMainWindow;
        _exit = exit;

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Awizzy.App/Assets/app-icon.ico"))),
            ToolTipText = "Awizzy",
        };
        _trayIcon.Clicked += (_, _) => _showMainWindow();

        _sessionManager.SessionChanged += OnSessionChanged;
        // Sync adds and removes sessions without session-state events, so refresh periodically too.
        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) => RebuildMenu());
        _refreshTimer.Start();
        RebuildMenu();

        TrayIcon.SetIcons(Avalonia.Application.Current!, [_trayIcon]);
    }

    private void OnSessionChanged(object? sender, SessionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RebuildMenu);

    private void RebuildMenu()
    {
        var menu = new NativeMenu();

        var open = new NativeMenuItem("Open Awizzy");
        open.Click += (_, _) => _showMainWindow();
        menu.Add(open);
        menu.Add(new NativeMenuItemSeparator());

        // Only running sessions are listed; unchecking one stops it. Everything else
        // is managed from the main window.
        var active = _state.Workspace.Sessions
            .Where(s => s.State is SessionState.Active or SessionState.Refreshing or SessionState.Starting)
            .OrderBy(s => s.AccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.RoleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var session in active)
        {
            var item = new NativeMenuItem($"{session.AccountName} / {session.RoleName}")
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = true,
            };
            var sessionId = session.Id;
            item.Click += async (_, _) =>
            {
                try
                {
                    await _sessionManager.StopSessionAsync(sessionId);
                }
                catch
                {
                    // The session lands in Error state, which the menu and main window both show.
                }
            };
            menu.Add(item);
        }
        if (active.Count > 0)
            menu.Add(new NativeMenuItemSeparator());

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => _exit();
        menu.Add(quit);

        _trayIcon.Menu = menu;
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _sessionManager.SessionChanged -= OnSessionChanged;
        _trayIcon.Dispose();
    }
}
