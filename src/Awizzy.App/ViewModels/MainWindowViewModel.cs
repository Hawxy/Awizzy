using System.Collections.ObjectModel;
using Avalonia.Threading;
using Awizzy.App.Services;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using Awizzy.Core.Services;
using Awizzy.Mcp;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Dialogs;

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
    private readonly ISsoOidcAuthService _authService;
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
        ISsoOidcAuthService authService,
        SessionActionsService sessionActions,
        TimeProvider time,
        IMcpServerHost mcpHost,
        McpChangeNotifier mcpNotifier,
        ISukiDialogManager dialogManager)
    {
        DialogManager = dialogManager;
        _mcpHost = mcpHost;
        _state = state;
        _integrationService = integrationService;
        _sessionManager = sessionManager;
        _dialogService = dialogService;
        _authService = authService;
        _sessionActions = sessionActions;
        _time = time;

        _sessionManager.SessionChanged += (_, e) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_sessionItemCache.TryGetValue(e.Session.Id, out var item))
                    item.RaiseChanged();
                _groups.FirstOrDefault(g => g.AccountId == e.Session.AccountId)?.RaiseChanged();
            });

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
                foreach (var session in AllSessions().Where(s => s.IsActive))
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

    public ObservableCollection<IntegrationItemViewModel> Integrations { get; } = [];

    /// <summary>Account headers and the session rows of expanded accounts, flattened into one
    /// list so a single virtualizing ItemsControl can render the table.</summary>
    public ObservableCollection<object> Rows { get; } = [];

    private readonly List<AccountGroupViewModel> _groups = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private IntegrationItemViewModel? _selectedIntegration;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _statusOpen;

    [ObservableProperty]
    private bool _isAnySyncing;

    partial void OnStatusMessageChanged(string value) => StatusOpen = value.Length > 0;

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

    public bool HasNoSessions => _groups.Count == 0;

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
            await _authService.LogoutAsync(item.Integration);
            item.RaiseChanged();
            StatusMessage = $"Logged out of {item.Alias}.";
        });
    }

    [RelayCommand]
    private async Task SyncAsync(IntegrationItemViewModel item)
    {
        item.IsSyncing = true;
        IsAnySyncing = true;
        StatusMessage = $"Syncing {item.Alias}…";
        try
        {
            await GuardAsync(async () =>
            {
                var result = await _integrationService.SyncSessionsAsync(item.Id);
                RebuildSessions();
                StatusMessage = $"{item.Alias}: {result.Total} roles ({result.Added} new, {result.Removed} removed).";
            });
        }
        finally
        {
            item.IsSyncing = false;
            IsAnySyncing = Integrations.Any(i => i.IsSyncing);
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
        await GuardAsync(() => _sessionManager.StartSessionAsync(item.Id));
        foreach (var session in AllSessions())
            session.RaiseChanged();
        foreach (var group in _groups)
            group.RaiseChanged();
    }

    [RelayCommand]
    private async Task StopSessionAsync(SessionItemViewModel item)
    {
        await GuardAsync(() => _sessionManager.StopSessionAsync(item.Id));
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
            await _state.SaveAsync();
            ThemeApplier.Apply(result.Theme);
            RebuildSessions();
            StatusMessage = "Settings saved.";

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
        RunSessionActionAsync(() => _sessionActions.CopyProfileNameAsync(item.Session));

    [RelayCommand]
    private Task CopyAccountIdAsync(SessionItemViewModel item) =>
        RunSessionActionAsync(() => _sessionActions.CopyAccountIdAsync(item.Session));

    [RelayCommand]
    private Task CopyCredentialsPowerShellAsync(SessionItemViewModel item) =>
        RunSessionActionAsync(() => _sessionActions.CopyCredentialsPowerShellAsync(item.Session));

    [RelayCommand]
    private Task CopyCredentialsBashAsync(SessionItemViewModel item) =>
        RunSessionActionAsync(() => _sessionActions.CopyCredentialsBashAsync(item.Session));

    [RelayCommand]
    private Task CopyCredentialsProfileAsync(SessionItemViewModel item) =>
        RunSessionActionAsync(() => _sessionActions.CopyCredentialsProfileAsync(item.Session));

    [RelayCommand]
    private Task OpenConsoleAsync(SessionItemViewModel item) =>
        RunSessionActionAsync(() => _sessionActions.OpenConsoleAsync(item.Session));

    private Task RunSessionActionAsync(Func<Task<string>> action) =>
        GuardAsync(async () => StatusMessage = await action());

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
            StatusMessage = ex.Message;
        }
    }

    private IEnumerable<SessionItemViewModel> AllSessions() =>
        _groups.SelectMany(g => g.Sessions);

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

        var query = _state.Workspace.Sessions.AsEnumerable();
        if (SelectedIntegration is { } selected)
            query = query.Where(s => s.IntegrationId == selected.Id);
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

        PruneSessionItemCache();
        OnPropertyChanged(nameof(HasNoSessions));
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
            _sessionItemCache[session.Id] = item = new SessionItemViewModel(session, _time);
        return item;
    }

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
