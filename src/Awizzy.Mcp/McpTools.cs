using System.ComponentModel;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using Awizzy.Core.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Awizzy.Mcp;

public record IntegrationInfo(
    string Alias,
    string PortalUrl,
    string Region,
    bool LoggedIn,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? LastSyncedAt,
    int SessionCount);

public record SessionInfo(
    string AccountId,
    string AccountName,
    string RoleName,
    string ProfileName,
    string Region,
    string State,
    DateTimeOffset? CredentialsExpireAt,
    string? Error);

public record SyncInfo(string Integration, int Added, int Removed, int Total);

/// <summary>Every tool body runs on the main thread via the dispatcher; workspace state
/// has a single-writer threading model and requests arrive on server threads.</summary>
[McpServerToolType]
public class McpTools(
    WorkspaceState state,
    ISessionManager sessionManager,
    IIntegrationService integrationService,
    IWebConsoleUrlService consoleUrlService,
    TimeProvider time,
    McpChangeNotifier notifier,
    IMainThreadDispatcher dispatcher)
{
    [McpServerTool(Name = "list_integrations")]
    [Description("Lists the configured AWS IAM Identity Center (SSO) integrations with their login and sync status.")]
    public Task<IReadOnlyList<IntegrationInfo>> ListIntegrations() =>
        dispatcher.InvokeAsync<IReadOnlyList<IntegrationInfo>>(() =>
            state.Workspace.Integrations
                .Select(i => new IntegrationInfo(
                    i.Alias, i.PortalUrl, i.Region, i.IsLoggedIn(time),
                    i.AccessTokenExpiresAt, i.LastSyncedAt,
                    state.Workspace.Sessions.Count(s => s.IntegrationId == i.Id)))
                .ToList());

    [McpServerTool(Name = "list_sessions")]
    [Description("Lists AWS sessions (account/role pairs). An Active session has live credentials written to the "
                 + "AWS credentials file under its profile name, usable with the AWS CLI via --profile.")]
    public Task<IReadOnlyList<SessionInfo>> ListSessions(
        [Description("Optional integration alias to filter by.")] string? integration = null) =>
        dispatcher.InvokeAsync<IReadOnlyList<SessionInfo>>(() =>
        {
            var sessions = state.Workspace.Sessions.AsEnumerable();
            if (integration is { Length: > 0 })
            {
                var match = ResolveIntegration(integration);
                sessions = sessions.Where(s => s.IntegrationId == match.Id);
            }

            return sessions.Select(ToInfo).ToList();
        });

    [McpServerTool(Name = "start_session")]
    [Description("Starts a session: fetches role credentials and writes them to the AWS credentials file under "
                 + "the session's profile name. The integration must be logged in.")]
    public Task<SessionInfo> StartSession(
        [Description("Account id or account name.")] string account,
        [Description("Role name.")] string role,
        CancellationToken ct) =>
        dispatcher.InvokeAsync(async () =>
        {
            var session = ResolveSession(account, role);
            try
            {
                await sessionManager.StartSessionAsync(session.Id, ct);
            }
            catch (Exception ex)
            {
                throw new McpException($"Failed to start {session.DisplayName}: {ex.Message}");
            }

            return ToInfo(session);
        });

    [McpServerTool(Name = "stop_session")]
    [Description("Stops a session and removes its profile from the AWS credentials file.")]
    public Task<SessionInfo> StopSession(
        [Description("Account id or account name.")] string account,
        [Description("Role name.")] string role,
        CancellationToken ct) =>
        dispatcher.InvokeAsync(async () =>
        {
            var session = ResolveSession(account, role);
            try
            {
                await sessionManager.StopSessionAsync(session.Id, ct);
            }
            catch (Exception ex)
            {
                throw new McpException($"Failed to stop {session.DisplayName}: {ex.Message}");
            }

            return ToInfo(session);
        });

    [McpServerTool(Name = "sync_integration")]
    [Description("Refreshes the account/role list of an integration from the SSO portal.")]
    public Task<SyncInfo> SyncIntegration(
        [Description("Integration alias.")] string integration,
        CancellationToken ct) =>
        dispatcher.InvokeAsync(async () =>
        {
            var match = ResolveIntegration(integration);
            if (!match.IsLoggedIn(time))
                throw new McpException($"Integration '{match.Alias}' is not logged in; log in from the app window first.");

            try
            {
                var result = await integrationService.SyncSessionsAsync(match.Id, ct);
                notifier.NotifyWorkspaceChanged();
                return new SyncInfo(match.Alias, result.Added, result.Removed, result.Total);
            }
            catch (McpException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new McpException($"Sync of '{match.Alias}' failed: {ex.Message}");
            }
        });

    [McpServerTool(Name = "get_console_url")]
    [Description("Returns a federated sign-in URL that opens the AWS web console for an active session. "
                 + "The URL grants console access as the session's role for a short time; treat it like a credential "
                 + "and do not log or share it.")]
    public Task<string> GetConsoleUrl(
        [Description("Account id or account name.")] string account,
        [Description("Role name.")] string role,
        CancellationToken ct) =>
        dispatcher.InvokeAsync(async () =>
        {
            var session = ResolveSession(account, role);
            var credentials = sessionManager.GetCachedCredentials(session.Id)
                ?? throw new McpException($"Session {session.DisplayName} is not active; start it first with start_session.");

            try
            {
                return await consoleUrlService.BuildConsoleUrlAsync(credentials, session.Region, ct);
            }
            catch (Exception ex)
            {
                throw new McpException($"Could not build a console URL for {session.DisplayName}: {ex.Message}");
            }
        });

    private static SessionInfo ToInfo(AwsSession s) => new(
        s.AccountId, s.AccountName, s.RoleName, s.ProfileName, s.Region,
        s.State.ToString(), s.CredentialsExpireAt, s.ErrorMessage);

    private AwsSession ResolveSession(string account, string role)
    {
        var matches = state.Workspace.Sessions
            .Where(s =>
                (string.Equals(s.AccountId, account, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(s.AccountName, account, StringComparison.OrdinalIgnoreCase))
                && string.Equals(s.RoleName, role, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new McpException(
                $"No session matches account '{account}' and role '{role}'. Use list_sessions to see what exists."),
            _ => throw new McpException(
                $"Multiple sessions match account '{account}' and role '{role}'; pass the 12-digit account id instead of the name."),
        };
    }

    private SsoIntegration ResolveIntegration(string alias) =>
        state.Workspace.Integrations.FirstOrDefault(i =>
            string.Equals(i.Alias, alias, StringComparison.OrdinalIgnoreCase))
        ?? throw new McpException(
            $"No integration named '{alias}'. Use list_integrations to see what exists.");
}
