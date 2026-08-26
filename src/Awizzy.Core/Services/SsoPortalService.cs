using System.Text.Json;
using Amazon.SSO;
using Amazon.SSO.Model;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Exceptions;
using Awizzy.Core.Models;

namespace Awizzy.Core.Services;

public class SsoPortalService(
    ISsoClientFactory clientFactory,
    ISecureStore secureStore,
    TimeProvider time) : ISsoPortalService
{
    public async Task<IReadOnlyList<AccountRole>> ListAccountRolesAsync(SsoIntegration integration, CancellationToken ct = default)
    {
        var token = await GetValidTokenAsync(integration, ct);
        using var sso = clientFactory.CreateSsoClient(integration.Region);

        try
        {
            var result = new List<AccountRole>();
            string? accountsToken = null;
            do
            {
                var accountsResponse = await sso.ListAccountsAsync(new ListAccountsRequest
                {
                    AccessToken = token.AccessToken,
                    NextToken = accountsToken,
                }, ct);

                foreach (var account in accountsResponse.AccountList ?? [])
                {
                    string? rolesToken = null;
                    do
                    {
                        var rolesResponse = await sso.ListAccountRolesAsync(new ListAccountRolesRequest
                        {
                            AccessToken = token.AccessToken,
                            AccountId = account.AccountId,
                            NextToken = rolesToken,
                        }, ct);

                        result.AddRange((rolesResponse.RoleList ?? []).Select(role =>
                            new AccountRole(account.AccountId, account.AccountName, role.RoleName)));
                        rolesToken = rolesResponse.NextToken;
                    } while (rolesToken is not null);
                }

                accountsToken = accountsResponse.NextToken;
            } while (accountsToken is not null);

            return result;
        }
        catch (UnauthorizedException ex)
        {
            throw new SsoSessionExpiredException($"The SSO session for '{integration.Alias}' was rejected: {ex.Message}");
        }
    }

    public async Task<RoleCredentialSet> GetRoleCredentialsAsync(SsoIntegration integration, string accountId, string roleName, CancellationToken ct = default)
    {
        var token = await GetValidTokenAsync(integration, ct);
        using var sso = clientFactory.CreateSsoClient(integration.Region);

        try
        {
            var response = await sso.GetRoleCredentialsAsync(new GetRoleCredentialsRequest
            {
                AccessToken = token.AccessToken,
                AccountId = accountId,
                RoleName = roleName,
            }, ct);

            var credentials = response.RoleCredentials;
            return new RoleCredentialSet(
                credentials.AccessKeyId,
                credentials.SecretAccessKey,
                credentials.SessionToken,
                DateTimeOffset.FromUnixTimeMilliseconds(credentials.Expiration.GetValueOrDefault()));
        }
        catch (UnauthorizedException ex)
        {
            throw new SsoSessionExpiredException($"The SSO session for '{integration.Alias}' was rejected: {ex.Message}");
        }
    }

    private async Task<StoredSsoToken> GetValidTokenAsync(SsoIntegration integration, CancellationToken ct)
    {
        var json = await secureStore.GetAsync(SecureStoreKeys.SsoToken(integration.Id), ct);
        if (json is null)
            throw new SsoSessionExpiredException($"No SSO session for '{integration.Alias}'; log in first.");

        StoredSsoToken? token;
        try
        {
            token = JsonSerializer.Deserialize(json, CoreJsonContext.Default.StoredSsoToken);
        }
        catch (JsonException)
        {
            token = null;
        }

        if (token is null || token.ExpiresAt <= time.GetUtcNow())
            throw new SsoSessionExpiredException($"The SSO session for '{integration.Alias}' has expired; log in again.");

        return token;
    }
}
