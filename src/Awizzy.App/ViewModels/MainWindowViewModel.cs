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
                foreach (var group in AccountGroups)
                {
                    var item = group.Sessions.FirstOrDefault(s => s.Id == e.Session.Id);
                    if (item is not null)
                    {
                        item.RaiseChanged();
                        group.RaiseChanged();
                        return;
                    }
                }
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

        RebuildIntegrations();
        RebuildSessions();
    }

    public ISukiDialogManager DialogManager { get; }

    public ObservableCollection<IntegrationItemViewModel> Integrations { get; } = [];
    public ObservableCollection<AccountGroupViewModel> AccountGroups { get; } = [];

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

    public bool HasNoSessions => AccountGroups.Count == 0;

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

    partial void OnSearchTextChanged(string value) => RebuildSessions();

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
        foreach (var group in AccountGroups)
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
        AccountGroups.SelectMany(g => g.Sessions);

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
        var expandedState = AccountGroups.ToDictionary(g => g.AccountId, g => g.IsExpanded);
        AccountGroups.Clear();

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

        var groups = query
            .GroupBy(s => (s.AccountId, s.AccountName))
            .Select(g =>
            {
                var group = new AccountGroupViewModel(g.Key.AccountName, g.Key.AccountId);
                if (expandedState.TryGetValue(g.Key.AccountId, out var wasExpanded))
                    group.IsExpanded = wasExpanded;
                foreach (var session in SortSessions(g))
                    group.Sessions.Add(new SessionItemViewModel(session, _time));
                return group;
            });

        var ordered = SortColumn == SessionSortColumn.Account && !SortAscending
            ? groups.OrderByDescending(g => g.AccountName, StringComparer.OrdinalIgnoreCase)
            : groups.OrderBy(g => g.AccountName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in ordered)
            AccountGroups.Add(group);

        OnPropertyChanged(nameof(HasNoSessions));
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
