using Awizzy.Core.Models;

namespace Awizzy.Core.Abstractions;

public record SessionSyncResult(int Added, int Removed, int Total);

public interface IIntegrationService
{
    Task<SsoIntegration> CreateAsync(string alias, string portalUrl, string region, CancellationToken ct = default);
    Task UpdateAsync(Guid integrationId, string alias, string portalUrl, string region, CancellationToken ct = default);

    /// <summary>Deletes the integration: stops its active sessions, removes its sessions, and discards its token.</summary>
    Task DeleteAsync(Guid integrationId, CancellationToken ct = default);

    /// <summary>Diffs the portal's account/role list against existing sessions. Existing sessions keep
    /// their profile and region settings; sessions whose (account, role) disappeared are removed.</summary>
    Task<SessionSyncResult> SyncSessionsAsync(Guid integrationId, CancellationToken ct = default);
}
