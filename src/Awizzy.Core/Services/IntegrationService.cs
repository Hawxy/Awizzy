using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using Microsoft.Extensions.Logging;

namespace Awizzy.Core.Services;

public class IntegrationService(
    WorkspaceState state,
    ISsoPortalService portal,
    ISsoOidcAuthService authService,
    ISessionManager sessionManager,
    TimeProvider time,
    ILogger<IntegrationService> logger) : IIntegrationService
{
    public void ValidateInput(string alias, string portalUrl, string region, Guid? excludeId = null) =>
        Validate(alias, portalUrl, region, excludeId);

    public async Task<SsoIntegration> CreateAsync(string alias, string portalUrl, string region, CancellationToken ct = default)
    {
        var integration = new SsoIntegration
        {
            Alias = Validate(alias, portalUrl, region),
            PortalUrl = portalUrl.Trim(),
            Region = region.Trim(),
        };
        state.Workspace.Integrations.Add(integration);
        await state.SaveAsync(ct);
        return integration;
    }

    public async Task UpdateAsync(Guid integrationId, string alias, string portalUrl, string region, CancellationToken ct = default)
    {
        var integration = Get(integrationId);
        integration.Alias = Validate(alias, portalUrl, region, excludeId: integrationId);
        integration.PortalUrl = portalUrl.Trim();
        integration.Region = region.Trim();
        await state.SaveAsync(ct);
    }

    public async Task LogoutAsync(Guid integrationId, CancellationToken ct = default)
    {
        var integration = Get(integrationId);

        // Logging out invalidates every session, so stop the running ones (removes their
        // credentials from the file and the cache) and clear the account list.
        foreach (var session in state.Workspace.Sessions.Where(s => s.IntegrationId == integrationId).ToList())
        {
            if (session.State is SessionState.Active or SessionState.Refreshing or SessionState.Starting)
                await sessionManager.StopSessionAsync(session.Id, ct);
            state.Workspace.Sessions.Remove(session);
        }

        await authService.LogoutAsync(integration, ct);
        await state.SaveAsync(ct);
        logger.LogInformation("Logged out of {Alias}; sessions cleared.", integration.Alias);
    }

    public async Task DeleteAsync(Guid integrationId, CancellationToken ct = default)
    {
        var integration = Get(integrationId);
        await LogoutAsync(integrationId, ct);
        state.Workspace.Integrations.Remove(integration);
        await state.SaveAsync(ct);
        logger.LogInformation("Integration {Alias} deleted.", integration.Alias);
    }

    public async Task<SessionSyncResult> SyncSessionsAsync(Guid integrationId, CancellationToken ct = default)
    {
        var integration = Get(integrationId);
        var accountRoles = await portal.ListAccountRolesAsync(integration, ct);
        var sessions = state.Workspace.Sessions;

        var added = 0;
        foreach (var accountRole in accountRoles)
        {
            var existing = sessions.FirstOrDefault(s =>
                s.HasSameIdentity(integrationId, accountRole.AccountId, accountRole.RoleName));
            if (existing is not null)
            {
                // Account names can change; identity (id, role) has not.
                existing.AccountName = accountRole.AccountName;
                continue;
            }

            // Roles in the same account share that account's profile name by default.
            var accountProfile = sessions
                .FirstOrDefault(s => s.IntegrationId == integrationId && s.AccountId == accountRole.AccountId)
                ?.ProfileName;
            sessions.Add(new AwsSession
            {
                IntegrationId = integrationId,
                AccountId = accountRole.AccountId,
                AccountName = accountRole.AccountName,
                RoleName = accountRole.RoleName,
                Region = integration.Region,
                ProfileName = accountProfile ?? ProfileNames.DeriveFromAccountName(accountRole.AccountName),
            });
            added++;
        }

        var removed = 0;
        foreach (var session in sessions.Where(s => s.IntegrationId == integrationId).ToList())
        {
            if (accountRoles.Any(ar => session.HasSameIdentity(integrationId, ar.AccountId, ar.RoleName)))
                continue;

            if (session.State is SessionState.Active or SessionState.Refreshing or SessionState.Starting)
                await sessionManager.StopSessionAsync(session.Id, ct);
            sessions.Remove(session);
            removed++;
        }

        integration.LastSyncedAt = time.GetUtcNow();
        await state.SaveAsync(ct);
        logger.LogInformation("Synced {Alias}: {Added} added, {Removed} removed, {Total} total.",
            integration.Alias, added, removed, accountRoles.Count);
        return new SessionSyncResult(added, removed, accountRoles.Count);
    }

    private SsoIntegration Get(Guid integrationId) =>
        state.Workspace.Integrations.FirstOrDefault(i => i.Id == integrationId)
        ?? throw new InvalidOperationException("Integration not found.");

    private string Validate(string alias, string portalUrl, string region, Guid? excludeId = null)
    {
        alias = alias.Trim();
        if (alias.Length == 0)
            throw new ArgumentException("Integration name cannot be empty.");
        if (state.Workspace.Integrations.Any(i =>
                i.Id != excludeId && string.Equals(i.Alias, alias, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"An integration named '{alias}' already exists.");
        if (!Uri.TryCreate(portalUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new ArgumentException("The portal URL must be a valid https:// URL.");
        if (region.Trim().Length == 0)
            throw new ArgumentException("Region cannot be empty.");
        return alias;
    }
}
