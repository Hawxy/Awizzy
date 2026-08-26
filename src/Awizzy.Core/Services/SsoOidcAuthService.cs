using System.Text.Json;
using Amazon.SSO;
using Amazon.SSO.Model;
using Amazon.SSOOIDC.Model;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Exceptions;
using Awizzy.Core.Models;
using Microsoft.Extensions.Logging;

namespace Awizzy.Core.Services;

public class SsoOidcAuthService(
    ISsoClientFactory clientFactory,
    ISecureStore secureStore,
    WorkspaceState state,
    TimeProvider time,
    ILogger<SsoOidcAuthService> logger) : ISsoOidcAuthService
{
    private const string ClientName = "awizzy";
    private const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    /// <summary>Registrations within this margin of expiry are re-registered rather than reused.</summary>
    private static readonly TimeSpan RegistrationExpiryMargin = TimeSpan.FromDays(1);

    public async Task<DeviceAuthorization> BeginLoginAsync(SsoIntegration integration, CancellationToken ct = default)
    {
        var registration = await GetOrRegisterClientAsync(integration.Region, ct);
        using var oidc = clientFactory.CreateOidcClient(integration.Region);

        var response = await oidc.StartDeviceAuthorizationAsync(new StartDeviceAuthorizationRequest
        {
            ClientId = registration.ClientId,
            ClientSecret = registration.ClientSecret,
            StartUrl = integration.PortalUrl,
        }, ct);

        return new DeviceAuthorization(
            response.VerificationUriComplete,
            response.UserCode,
            response.DeviceCode,
            TimeSpan.FromSeconds(Math.Max(1, response.Interval.GetValueOrDefault(5))),
            time.GetUtcNow().AddSeconds(response.ExpiresIn.GetValueOrDefault(600)));
    }

    public async Task CompleteLoginAsync(SsoIntegration integration, DeviceAuthorization authorization, CancellationToken ct = default)
    {
        var registration = await GetOrRegisterClientAsync(integration.Region, ct);
        using var oidc = clientFactory.CreateOidcClient(integration.Region);

        var interval = authorization.Interval;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (time.GetUtcNow() >= authorization.ExpiresAt)
                throw new SsoLoginTimeoutException("The login request expired before it was approved in the browser.");

            try
            {
                var response = await oidc.CreateTokenAsync(new CreateTokenRequest
                {
                    ClientId = registration.ClientId,
                    ClientSecret = registration.ClientSecret,
                    GrantType = DeviceCodeGrantType,
                    DeviceCode = authorization.DeviceCode,
                }, ct);

                var expiresAt = time.GetUtcNow().AddSeconds(response.ExpiresIn.GetValueOrDefault(3600));
                await secureStore.SetAsync(
                    SecureStoreKeys.SsoToken(integration.Id),
                    JsonSerializer.Serialize(new StoredSsoToken(response.AccessToken, expiresAt), CoreJsonContext.Default.StoredSsoToken),
                    ct);

                integration.AccessTokenExpiresAt = expiresAt;
                await state.SaveAsync(ct);
                logger.LogInformation("Logged in to integration {Alias}; token valid until {ExpiresAt}.", integration.Alias, expiresAt);
                return;
            }
            catch (AuthorizationPendingException)
            {
                // User has not approved yet; keep polling.
            }
            catch (SlowDownException)
            {
                interval += TimeSpan.FromSeconds(5);
            }
            catch (Amazon.SSOOIDC.Model.ExpiredTokenException)
            {
                throw new SsoLoginTimeoutException("The login request expired before it was approved in the browser.");
            }
            catch (Amazon.SSOOIDC.Model.AccessDeniedException)
            {
                throw new SsoLoginDeniedException("The login request was declined.");
            }

            await Task.Delay(interval, time, ct);
        }
    }

    public async Task LogoutAsync(SsoIntegration integration, CancellationToken ct = default)
    {
        var tokenJson = await secureStore.GetAsync(SecureStoreKeys.SsoToken(integration.Id), ct);
        if (tokenJson is not null)
        {
            try
            {
                var token = JsonSerializer.Deserialize(tokenJson, CoreJsonContext.Default.StoredSsoToken);
                if (token is not null)
                {
                    using var sso = clientFactory.CreateSsoClient(integration.Region);
                    await sso.LogoutAsync(new LogoutRequest { AccessToken = token.AccessToken }, ct);
                }
            }
            catch (Exception ex) when (ex is AmazonSSOException or JsonException)
            {
                logger.LogWarning(ex, "Server-side logout for {Alias} failed; discarding the local token anyway.", integration.Alias);
            }
        }

        await secureStore.DeleteAsync(SecureStoreKeys.SsoToken(integration.Id), ct);
        integration.AccessTokenExpiresAt = null;
        await state.SaveAsync(ct);
    }

    private async Task<SsoClientRegistration> GetOrRegisterClientAsync(string region, CancellationToken ct)
    {
        var key = SecureStoreKeys.ClientRegistration(region);
        var cachedJson = await secureStore.GetAsync(key, ct);
        if (cachedJson is not null)
        {
            try
            {
                var cached = JsonSerializer.Deserialize(cachedJson, CoreJsonContext.Default.SsoClientRegistration);
                if (cached is not null && cached.ExpiresAt > time.GetUtcNow() + RegistrationExpiryMargin)
                    return cached;
            }
            catch (JsonException)
            {
                // Unreadable cache entry; fall through and re-register.
            }
        }

        using var oidc = clientFactory.CreateOidcClient(region);
        var response = await oidc.RegisterClientAsync(new RegisterClientRequest
        {
            ClientName = ClientName,
            ClientType = "public",
        }, ct);

        var registration = new SsoClientRegistration(
            response.ClientId,
            response.ClientSecret,
            DateTimeOffset.FromUnixTimeSeconds(response.ClientSecretExpiresAt.GetValueOrDefault()));
        await secureStore.SetAsync(key, JsonSerializer.Serialize(registration, CoreJsonContext.Default.SsoClientRegistration), ct);
        return registration;
    }
}
