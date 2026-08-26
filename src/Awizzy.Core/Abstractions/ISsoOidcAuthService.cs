using Awizzy.Core.Models;

namespace Awizzy.Core.Abstractions;

public interface ISsoOidcAuthService
{
    /// <summary>Registers (or reuses) an OIDC client and starts a device authorization.
    /// The caller should open <see cref="DeviceAuthorization.VerificationUriComplete"/> in a browser.</summary>
    Task<DeviceAuthorization> BeginLoginAsync(SsoIntegration integration, CancellationToken ct = default);

    /// <summary>Polls until the user approves the device authorization, then stores the access token.
    /// Throws <see cref="Exceptions.SsoLoginTimeoutException"/> or <see cref="Exceptions.SsoLoginDeniedException"/>.</summary>
    Task CompleteLoginAsync(SsoIntegration integration, DeviceAuthorization authorization, CancellationToken ct = default);

    /// <summary>Invalidates the token server-side (best effort) and removes it from the secure store.</summary>
    Task LogoutAsync(SsoIntegration integration, CancellationToken ct = default);
}
