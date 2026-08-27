using System.Collections.ObjectModel;
using Avalonia.Threading;
using Awizzy.App.Services;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using Awizzy.Core.Services;
using Awizzy.Mcp;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace Awizzy.App.ViewModels;

public enum SessionSortColumn
{
    Account,
    Role,
    Profile,
    Region,
    Status,
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly WorkspaceState _state;
    private readonly IIntegrationService _integrationService;
    private readonly ISessionManager _sessionManager;
    private readonly IDialogService _dialogService;
    private readonly IClipboardService _clipboard;
    private readonly SessionActionsService _sessionActions;
    private readonly TimeProvider _time;
    private readonly IMcpServerHost _mcpHost;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _searchDebounceTimer;

    /// <summary>Row view models by session id, reused across rebuilds so unchanged rows
    /// keep their containers instead of re-resolving every style and dynamic resource.</summary>
    private readonly Dictionary<Guid, SessionItemViewModel> _sessionItemCache = [];

    public MainWindowViewModel(
        WorkspaceState state,
        IIntegrationService integrationService,
        ISessionManager sessionManager,
        IDialogService dialogService,
        IClipboardService clipboard,
        SessionActionsService sessionActions,
        TimeProvider time,
        IMcpServerHost mcpHost,
        McpChangeNotifier mcpNotifier,
        ISukiDialogManager dialogManager,
        ISukiToastManager toastManager)
    {
        DialogManager = dialogManager;
        ToastManager = toastManager;
        _mcpHost = mcpHost;
        _state = state;
        _integrationService = integrationService;
        _sessionManager = sessionManager;
        _dialogService = dialogService;
        _clipboard = clipboard;
        _sessionActions = sessionActions;
        _time = time;

        // A full rebuild (diff-based, so cheap) keeps active sessions promoted to the top
        // as their state changes, besides refreshing the row's own bindings.
        _sessionManager.SessionChanged += (_, _) => Dispatcher.UIThread.Post(RebuildSessions);

        _mcpHost.StatusChanged += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(IsMcpRunning));
                OnPropertyChanged(nameof(McpStatusText));
            });

        // MCP sync adds/removes sessions without per-session events; rebuild the views.
        mcpNotifier.WorkspaceChanged += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var integration in Integrations)
                    integration.RaiseChanged();
                RebuildSessions();
            });

        _countdownTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(30),
            DispatcherPriority.Background,
            (_, _) =>
            {
                foreach (var session in ActiveSessions)
                    session.RaiseChanged();
                // Keeps relative sync-age text and token-expiry-driven state fresh.
                foreach (var integration in Integrations)
                    integration.RaiseChanged();
            });
        _countdownTimer.Start();

        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RebuildSessions();
        };

        RebuildIntegrations();
        RebuildSessions();
    }

    public ISukiDialogManager DialogManager { get; }

    public ISukiToastManager ToastManager { get; }

    public ObservableCollection<IntegrationItemViewModel> Integrations { get; } = [];

    /// <summary>Account headers and the session rows of expanded accounts, flattened into one
    /// list so a single virtualizing ItemsControl can render the table.</summary>
    public ObservableCollection<object> Rows { get; } = [];

    /// <summary>Running sessions, shown in the pinned box above the table instead of
    /// under their account groups.</summary>
    public ObservableCollection<SessionItemViewModel> ActiveSessions { get; } = [];

    public bool HasActiveSessions => ActiveSessions.Count > 0;

    private readonly List<AccountGroupViewModel> _groups = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showFavoritesOnly;

    [ObservableProperty]
    private IntegrationItemViewModel? _selectedIntegration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountHeader))]
    [NotifyPropertyChangedFor(nameof(RoleHeader))]
    [NotifyPropertyChangedFor(nameof(ProfileHeader))]
    [NotifyPropertyChangedFor(nameof(RegionHeader))]
    [NotifyPropertyChangedFor(nameof(StatusHeader))]
    private SessionSortColumn _sortColumn = SessionSortColumn.Account;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountHeader))]
    [NotifyPropertyChangedFor(nameof(RoleHeader))]
    [NotifyPropertyChangedFor(nameof(ProfileHeader))]
    [NotifyPropertyChangedFor(nameof(RegionHeader))]
    [NotifyPropertyChangedFor(nameof(StatusHeader))]
    private bool _sortAscending = true;

    public bool HasNoSessions => _state.Workspace.Sessions.Count == 0;

    /// <summary>Startable profiles exist but the current search or filters match none.
    /// Running sessions don't count; they render in the pinned box regardless.</summary>
    public bool HasNoMatches => _groups.Count == 0 && _state.Workspace.Sessions.Any(s => !IsRunning(s));

    public bool IsMcpRunning => _mcpHost.IsRunning;

    public string McpStatusText =>
        _mcpHost.IsRunning ? $"MCP on localhost:{_mcpHost.Port}" : "MCP server off";

    public string AccountHeader => HeaderFor("Account", SessionSortColumn.Account);
    public string RoleHeader => HeaderFor("Role", SessionSortColumn.Role);
    public string ProfileHeader => HeaderFor("Profile", SessionSortColumn.Profile);
    public string RegionHeader => HeaderFor("Region", SessionSortColumn.Region);
    public string StatusHeader => HeaderFor("Status", SessionSortColumn.Status);

    private string HeaderFor(string title, SessionSortColumn column) =>
        SortColumn == column ? $"{title} {(SortAscending ? "▲" : "▼")}" : title;

    // Coalesces rapid typing into a single rebuild.
    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    partial void OnSelectedIntegrationChanged(IntegrationItemViewModel? value) => RebuildSessions();

    partial void OnShowFavoritesOnlyChanged(bool value) => RebuildSessions();

    [RelayCommand]
    private void ToggleSort(SessionSortColumn column)
    {
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = true;
        }

        RebuildSessions();
    }

    [RelayCommand]
    private async Task AddIntegrationAsync()
    {
        var input = await _dialogService.ShowAddIntegrationAsync();
        if (input is null)
            return;

        await GuardAsync(async () =>
        {
            var integration = await _integrationService.CreateAsync(input.Alias, input.PortalUrl, input.Region);
            RebuildIntegrations();
            SelectedIntegration = Integrations.FirstOrDefault(i => i.Id == integration.Id);
        });
    }

    [RelayCommand]
    private async Task EditIntegrationAsync(IntegrationItemViewModel item)
    {
        var input = await _dialogService.ShowAddIntegrationAsync(item.Integration);
        if (input is null)
            return;

        await GuardAsync(async () =>
        {
            await _integrationService.UpdateAsync(item.Id, input.Alias, input.PortalUrl, input.Region);
            item.RaiseChanged();
        });
    }

    [RelayCommand]
    private async Task LoginAsync(IntegrationItemViewModel item)
    {
        var success = await _dialogService.ShowLoginAsync(item.Integration);
        item.RaiseChanged();
        if (!success)
            return;

        await SyncAsync(item);
    }

    [RelayCommand]
    private async Task LogoutAsync(IntegrationItemViewModel item)
    {
        await GuardAsync(async () =>
        {
            await _integrationService.LogoutAsync(item.Id);
            RebuildSessions();
            item.RaiseChanged();
            ShowToast(item.Alias, "Logged out; sessions and credentials cleared.", NotificationType.Success);
        });
    }

    [RelayCommand]
    private async Task SyncAsync(IntegrationItemViewModel item)
    {
        item.IsSyncing = true;
        var loadingToast = ToastManager.CreateToast()
            .WithTitle(item.Alias)
            .WithContent("Loading accounts and roles…")
            .WithLoadingState(true)
            .Queue();
        try
        {
            await GuardAsync(async () =>
            {
                var result = await _integrationService.SyncSessionsAsync(item.Id);
                RebuildSessions();
                ShowToast(item.Alias,
                    $"{result.Total} roles ({result.Added} new, {result.Removed} removed).",
                    NotificationType.Success);
            });
        }
        finally
        {
            ToastManager.Dismiss(loadingToast, SukiToastDismissSource.Code);
            item.IsSyncing = false;
            item.RaiseChanged();
        }
    }

    [RelayCommand]
    private async Task DeleteIntegrationAsync(IntegrationItemViewModel item)
    {
        var confirmed = await _dialogService.ConfirmAsync(
            "Delete integration",
            $"Delete '{item.Alias}' and all its sessions? Active sessions will be stopped.");
        if (!confirmed)
            return;

        await GuardAsync(async () =>
        {
            await _integrationService.DeleteAsync(item.Id);
            RebuildIntegrations();
            RebuildSessions();
        });
    }

    [RelayCommand]
    private async Task StartSessionAsync(SessionItemViewModel item)
    {
        // State transitions raise SessionChanged, which rebuilds the views.
        await GuardAsync(() => _sessionManager.StartSessionAsync(item.Id));
    }

    [RelayCommand]
    private async Task StopSessionAsync(SessionItemViewModel item)
    {
        await GuardAsync(() => _sessionManager.StopSessionAsync(item.Id));
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(SessionItemViewModel item)
    {
        await GuardAsync(async () =>
        {
            var favorites = _state.Workspace.Settings.FavoriteRoles;
            var removed = favorites.RemoveAll(key =>
                string.Equals(key, item.Session.RoleKey, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                favorites.Add(item.Session.RoleKey);
            await _state.SaveAsync();

            // Unfavoriting under the favorites filter removes the row.
            if (ShowFavoritesOnly)
                RebuildSessions();
            else
                item.RaiseChanged();
        });
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var result = await _dialogService.ShowSettingsAsync();
        if (result is null)
            return;

        await GuardAsync(async () =>
        {
            var settings = _state.Workspace.Settings;
            var pathChanged = !string.Equals(
                settings.CredentialsFilePath, result.CredentialsFilePath, StringComparison.OrdinalIgnoreCase);

            // Sessions keep writing to the old file; stop them before the path changes.
            if (pathChanged)
            {
                var running = _state.Workspace.Sessions
                    .Where(s => s.State is SessionState.Active or SessionState.Refreshing or SessionState.Starting)
                    .ToList();
                foreach (var session in running)
                    await _sessionManager.StopSessionAsync(session.Id);
            }

            settings.RefreshMargin = result.RefreshMargin;
            settings.CredentialsFilePath = result.CredentialsFilePath;
            settings.Theme = result.Theme;
            settings.McpServerEnabled = result.McpServerEnabled;
            settings.McpServerPort = result.McpServerPort;
            settings.McpExcludedRoles = result.McpExcludedRoles.ToList();
            await _state.SaveAsync();
            ThemeApplier.Apply(result.Theme);
            RebuildSessions();
            ShowToast("Settings", "Settings saved.", NotificationType.Success);

            if (result.McpServerEnabled)
            {
                if (!_mcpHost.IsRunning || _mcpHost.Port != result.McpServerPort)
                    await _mcpHost.StartAsync(result.McpServerPort);
            }
            else if (_mcpHost.IsRunning)
            {
                await _mcpHost.StopAsync();
            }
        });
    }

    [RelayCommand]
    private Task CopyProfileNameAsync(SessionItemViewModel item) =>
        RunSessionActionAsync(item, () => _sessionActions.CopyProfileNameAsync(item.Session));

    [RelayCommand]
    private Task CopyAccountIdAsync(SessionItemViewModel item) =>
        RunSessionActionAsync(item, () => _sessionActions.CopyAccountIdAsync(item.Session));

    [RelayCommand]
    private Task CopyCredentialsPowerShellAsync(SessionItemViewModel item) =>
        RunSessionActionAsync(item, () => _sessionActions.CopyCredentialsPowerShellAsync(item.Session));

    [RelayCommand]
    private Task CopyCredentialsBashAsync(SessionItemViewModel item) =>
        RunSessionActionAsync(item, () => _sessionActions.CopyCredentialsBashAsync(item.Session));

    [RelayCommand]
    private Task CopyCredentialsProfileAsync(SessionItemViewModel item) =>
        RunSessionActionAsync(item, () => _sessionActions.CopyCredentialsProfileAsync(item.Session));

    [RelayCommand]
    private Task OpenConsoleAsync(SessionItemViewModel item) =>
        RunSessionActionAsync(item, () => _sessionActions.OpenConsoleAsync(item.Session));

    private Task RunSessionActionAsync(SessionItemViewModel item, Func<Task<string>> action) =>
        GuardAsync(async () => ShowToast(item.DisplayName, await action(), NotificationType.Information));

    [RelayCommand]
    private Task CopyMcpSetupAsync(string provider)
    {
        var port = _mcpHost.Port ?? _state.Workspace.Settings.McpServerPort;
        var url = $"http://localhost:{port}";
        var (label, text) = provider switch
        {
            "claude" => ("Claude Code", $"claude mcp add --transport http awizzy {url}"),
            "codex" => ("Codex CLI", $"codex mcp add awizzy --url {url}"),
            "gemini" => ("Gemini CLI", $"gemini mcp add --transport http awizzy {url}"),
            "cursor" => ("Cursor", $$"""{ "mcpServers": { "awizzy": { "url": "{{url}}" } } }"""),
            "vscode" => ("VS Code", $$"""{ "servers": { "awizzy": { "type": "http", "url": "{{url}}" } } }"""),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };

        return GuardAsync(async () =>
        {
            await _clipboard.SetTextAsync(text);
            ShowToast(label, "MCP setup command copied to clipboard.", NotificationType.Information);
        });
    }

    [RelayCommand]
    private async Task EditSessionAsync(SessionItemViewModel item)
    {
        var result = await _dialogService.ShowSessionOptionsAsync(item.Session);
        if (result is null)
            return;

        await GuardAsync(async () =>
        {
            var targets = result.ApplyProfileToAccount
                ? _state.Workspace.Sessions
                    .Where(s => s.IntegrationId == item.Session.IntegrationId
                                && s.AccountId == item.Session.AccountId)
                    .ToList()
                : [item.Session];

            // A running session keeps writing to its old profile section; stop before repointing.
            foreach (var session in targets.Where(s =>
                         s.State is SessionState.Active or SessionState.Refreshing or SessionState.Starting
                         && !string.Equals(s.ProfileName, result.ProfileName, StringComparison.OrdinalIgnoreCase)))
            {
                await _sessionManager.StopSessionAsync(session.Id);
            }

            foreach (var session in targets)
                session.ProfileName = result.ProfileName;
            item.Session.Region = result.Region;
            await _state.SaveAsync();
            RebuildSessions();
        });
    }

    private async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowToast("Error", ex.Message, NotificationType.Error);
        }
    }

    private void ShowToast(string title, string message, NotificationType type)
    {
        // Errors stay up longer; everything is click-dismissable.
        var lifetime = type == NotificationType.Error ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(4);
        ToastManager.CreateToast()
            .WithTitle(title)
            .WithContent(message)
            .OfType(type)
            .Dismiss().After(lifetime)
            .Dismiss().ByClicking()
            .Queue();
    }

    private void RebuildIntegrations()
    {
        var selectedId = SelectedIntegration?.Id;
        Integrations.Clear();
        foreach (var integration in _state.Workspace.Integrations)
            Integrations.Add(new IntegrationItemViewModel(integration, _time));
        SelectedIntegration = Integrations.FirstOrDefault(i => i.Id == selectedId);
    }

    private void RebuildSessions()
    {
        // A direct rebuild supersedes any rebuild the search debounce still has pending.
        _searchDebounceTimer.Stop();

        // The pinned box shows every running session regardless of the filters, so it
        // stays in view however the table below is narrowed.
        SyncCollection(ActiveSessions, _state.Workspace.Sessions
            .Where(IsRunning)
            .OrderBy(s => s.AccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.RoleName, StringComparer.OrdinalIgnoreCase)
            .Select(GetOrCreateSessionItem)
            .ToList());

        var query = _state.Workspace.Sessions.AsEnumerable();
        if (SelectedIntegration is { } selected)
            query = query.Where(s => s.IntegrationId == selected.Id);

        // Group summaries count the account's full inventory, including the running
        // sessions that render in the pinned box instead of under the group.
        var accountCounts = query
            .GroupBy(s => s.AccountId)
            .ToDictionary(g => g.Key, g => (Total: g.Count(), Active: g.Count(IsRunning)));

        query = query.Where(s => !IsRunning(s));
        if (ShowFavoritesOnly)
            query = query.Where(IsFavorite);
        if (SearchText is { Length: > 0 } search)
        {
            query = query.Where(s =>
                s.AccountName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || s.RoleName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || s.AccountId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || s.ProfileName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var grouped = query.GroupBy(s => (s.AccountId, s.AccountName));
        var ordered = SortColumn == SessionSortColumn.Account && !SortAscending
            ? grouped.OrderByDescending(g => g.Key.AccountName, StringComparer.OrdinalIgnoreCase)
            : grouped.OrderBy(g => g.Key.AccountName, StringComparer.OrdinalIgnoreCase);

        var existingGroups = new Dictionary<string, AccountGroupViewModel>();
        foreach (var group in _groups)
            existingGroups.TryAdd(group.AccountId, group);

        _groups.Clear();
        foreach (var g in ordered)
        {
            if (!existingGroups.TryGetValue(g.Key.AccountId, out var group)
                || !string.Equals(group.AccountName, g.Key.AccountName, StringComparison.Ordinal))
            {
                var replaced = group;
                group = new AccountGroupViewModel(g.Key.AccountName, g.Key.AccountId);
                if (replaced is not null)
                    group.IsExpanded = replaced.IsExpanded;
                group.PropertyChanged += OnGroupPropertyChanged;
            }

            var counts = accountCounts[g.Key.AccountId];
            group.TotalRoles = counts.Total;
            group.ActiveCount = counts.Active;
            SyncCollection(group.Sessions, SortSessions(g).Select(GetOrCreateSessionItem).ToList());
            _groups.Add(group);
        }

        SyncRows();

        // Reused rows read the session lazily; re-evaluate their bindings in place.
        foreach (var group in _groups)
        {
            group.RaiseChanged();
            foreach (var session in group.Sessions)
                session.RaiseChanged();
        }

        foreach (var session in ActiveSessions)
            session.RaiseChanged();

        PruneSessionItemCache();
        OnPropertyChanged(nameof(HasNoSessions));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(HasActiveSessions));
    }

    private void OnGroupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountGroupViewModel.IsExpanded))
            SyncRows();
    }

    /// <summary>Projects the groups into the flat row list the virtualized table binds to.</summary>
    private void SyncRows()
    {
        var desired = new List<object>();
        foreach (var group in _groups)
        {
            desired.Add(group);
            if (group.IsExpanded)
                desired.AddRange(group.Sessions);
        }

        SyncCollection(Rows, desired);
    }

    private SessionItemViewModel GetOrCreateSessionItem(AwsSession session)
    {
        if (!_sessionItemCache.TryGetValue(session.Id, out var item))
            _sessionItemCache[session.Id] = item = new SessionItemViewModel(session, _time, IsFavorite);
        return item;
    }

    private bool IsFavorite(AwsSession session) =>
        _state.Workspace.Settings.FavoriteRoles.Contains(session.RoleKey, StringComparer.OrdinalIgnoreCase);

    private static bool IsRunning(AwsSession session) =>
        session.State is SessionState.Active or SessionState.Refreshing or SessionState.Starting;

    private void PruneSessionItemCache()
    {
        var live = _state.Workspace.Sessions.Select(s => s.Id).ToHashSet();
        foreach (var stale in _sessionItemCache.Keys.Where(id => !live.Contains(id)).ToList())
            _sessionItemCache.Remove(stale);
    }

    /// <summary>Applies minimal inserts, moves, and removals so entries already in place keep
    /// their item containers (a Clear/re-add tears down and rebuilds every row). Assumes the
    /// desired entries are distinct. Departed entries are removed first so the survivors stay
    /// aligned; otherwise a removal in the middle (collapsing a group) degrades into one Move
    /// event, and a container shuffle, per row below it.</summary>
    private static void SyncCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        var desiredSet = new HashSet<T>(desired);
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(target[i]))
                target.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i]))
                continue;

            // The prefix already matches, so if the entry is present it sits past i.
            var existingIndex = IndexOfFrom(target, desired[i], i + 1);
            if (existingIndex > i)
                target.Move(existingIndex, i);
            else
                target.Insert(i, desired[i]);
        }
    }

    private static int IndexOfFrom<T>(ObservableCollection<T> target, T item, int start) where T : class
    {
        for (var i = start; i < target.Count; i++)
        {
            if (ReferenceEquals(target[i], item))
                return i;
        }

        return -1;
    }

    private IEnumerable<AwsSession> SortSessions(IEnumerable<AwsSession> sessions)
    {
        Func<AwsSession, string> key = SortColumn switch
        {
            SessionSortColumn.Profile => s => s.ProfileName,
            SessionSortColumn.Region => s => s.Region,
            SessionSortColumn.Status => s => s.State.ToString(),
            _ => s => s.RoleName,
        };
        return SortAscending || SortColumn == SessionSortColumn.Account
            ? sessions.OrderBy(key, StringComparer.OrdinalIgnoreCase)
            : sessions.OrderByDescending(key, StringComparer.OrdinalIgnoreCase);
    }
}
