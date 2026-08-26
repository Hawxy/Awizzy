using Awizzy.Core.Models;

namespace Awizzy.Core.Abstractions;

public record SessionSyncResult(int Added, int Removed, int Total);

public interface IIntegrationService
{
    /// <summary>Throws when the input would be rejected by Create/Update; lets dialogs
    /// validate before closing. Pass the integration's id when editing an existing one.</summary>
    void ValidateInput(string alias, string portalUrl, string region, Guid? excludeId = null);

    Task<SsoIntegration> CreateAsync(string alias, string portalUrl, string region, CancellationToken ct = default);
    Task UpdateAsync(Guid integrationId, string alias, string portalUrl, string region, CancellationToken ct = default);

    /// <summary>Deletes the integration: stops its active sessions, removes its sessions, and discards its token.</summary>
    Task DeleteAsync(Guid integrationId, CancellationToken ct = default);

    /// <summary>Diffs the portal's account/role list against existing sessions. Existing sessions keep
    /// their profile and region settings; sessions whose (account, role) disappeared are removed.</summary>
    Task<SessionSyncResult> SyncSessionsAsync(Guid integrationId, CancellationToken ct = default);
}
