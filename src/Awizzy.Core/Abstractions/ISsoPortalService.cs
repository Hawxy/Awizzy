using Awizzy.Core.Models;

namespace Awizzy.Core.Abstractions;

public interface ISsoPortalService
{
    /// <summary>Enumerates every account and role the logged-in user can access.</summary>
    Task<IReadOnlyList<AccountRole>> ListAccountRolesAsync(SsoIntegration integration, CancellationToken ct = default);

    Task<RoleCredentialSet> GetRoleCredentialsAsync(SsoIntegration integration, string accountId, string roleName, CancellationToken ct = default);
}
