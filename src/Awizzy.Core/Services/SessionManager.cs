using Awizzy.Core.Abstractions;
using Awizzy.Core.Exceptions;
using Awizzy.Core.Models;
using Microsoft.Extensions.Logging;

namespace Awizzy.Core.Services;

public class SessionManager(
    WorkspaceState state,
    ISsoPortalService portal,
    ICredentialsFileWriter credentialsWriter,
    ILogger<SessionManager> logger) : ISessionManager
{
    private readonly Dictionary<Guid, RoleCredentialSet> _credentialCache = [];
    private readonly Lock _cacheLock = new();

    public event EventHandler<SessionChangedEventArgs>? SessionChanged;

    public RoleCredentialSet? GetCachedCredentials(Guid sessionId)
    {
        lock (_cacheLock)
            return _credentialCache.GetValueOrDefault(sessionId);
    }

    public async Task StartSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = Get(sessionId);
        var integration = GetIntegration(session);

        // Only one session may be active per profile; starting this one supersedes the other.
        var conflicting = state.Workspace.Sessions.FirstOrDefault(s =>
            s.Id != session.Id
            && string.Equals(s.ProfileName, session.ProfileName, StringComparison.OrdinalIgnoreCase)
            && s.State is SessionState.Active or SessionState.Refreshing or SessionState.Starting);
        if (conflicting is not null)
            await StopSessionAsync(conflicting.Id, ct);

        SetState(session, SessionState.Starting);
        try
        {
            await WriteCredentialsAsync(session, integration, ct);
            SetState(session, SessionState.Active);
            logger.LogInformation("Session {Session} started; credentials expire at {ExpiresAt}.",
                session.DisplayName, session.CredentialsExpireAt);
        }
        catch (Exception ex)
        {
            SetError(session, ex);
            throw;
        }
    }

    public async Task StopSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = Get(sessionId);
        await credentialsWriter.RemoveProfileAsync(ProfileName(session), ct);
        lock (_cacheLock)
            _credentialCache.Remove(session.Id);
        session.CredentialsExpireAt = null;
        session.ErrorMessage = null;
        SetState(session, SessionState.Inactive);
        logger.LogInformation("Session {Session} stopped.", session.DisplayName);
    }

    public async Task RefreshSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = Get(sessionId);
        if (session.State is not (SessionState.Active or SessionState.Refreshing))
            return;

        var integration = GetIntegration(session);
        SetState(session, SessionState.Refreshing);
        try
        {
            await WriteCredentialsAsync(session, integration, ct);
            SetState(session, SessionState.Active);
            logger.LogInformation("Session {Session} refreshed; credentials expire at {ExpiresAt}.",
                session.DisplayName, session.CredentialsExpireAt);
        }
        catch (SsoSessionExpiredException ex)
        {
            SetError(session, ex);
            throw;
        }
        catch (Exception ex)
        {
            // Transient failure (network, file lock): stay Active so the refresher retries next tick.
            logger.LogWarning(ex, "Refresh of session {Session} failed; will retry.", session.DisplayName);
            SetState(session, SessionState.Active);
            throw;
        }
    }

    private async Task WriteCredentialsAsync(AwsSession session, SsoIntegration integration, CancellationToken ct)
    {
        var credentials = await portal.GetRoleCredentialsAsync(integration, session.AccountId, session.RoleName, ct);
        await credentialsWriter.WriteProfileAsync(ProfileName(session), credentials, session.Region, ct);
        lock (_cacheLock)
            _credentialCache[session.Id] = credentials;
        session.CredentialsExpireAt = credentials.Expiration;
        session.ErrorMessage = null;
    }

    private void SetError(AwsSession session, Exception ex)
    {
        lock (_cacheLock)
            _credentialCache.Remove(session.Id);
        session.ErrorMessage = ex is SsoSessionExpiredException
            ? "SSO session expired; log in to the integration again."
            : ex.Message;
        SetState(session, SessionState.Error);
        logger.LogError(ex, "Session {Session} failed.", session.DisplayName);
    }

    private void SetState(AwsSession session, SessionState newState)
    {
        session.State = newState;
        SessionChanged?.Invoke(this, new SessionChangedEventArgs(session));
    }

    private static string ProfileName(AwsSession session) => session.ProfileName;

    private AwsSession Get(Guid sessionId) =>
        state.Workspace.Sessions.FirstOrDefault(s => s.Id == sessionId)
        ?? throw new InvalidOperationException("Session not found.");

    private SsoIntegration GetIntegration(AwsSession session) =>
        state.Workspace.Integrations.FirstOrDefault(i => i.Id == session.IntegrationId)
        ?? throw new InvalidOperationException($"Session '{session.DisplayName}' references an integration that no longer exists.");
}
