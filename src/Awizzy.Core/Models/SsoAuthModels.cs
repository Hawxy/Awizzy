namespace Awizzy.Core.Models;

/// <summary>A pending device authorization the user must approve in the browser.</summary>
public record DeviceAuthorization(
    string VerificationUriComplete,
    string UserCode,
    string DeviceCode,
    TimeSpan Interval,
    DateTimeOffset ExpiresAt);

/// <summary>OIDC client registration, cached per region in the secure store (valid ~90 days).</summary>
public record SsoClientRegistration(string ClientId, string ClientSecret, DateTimeOffset ExpiresAt);

/// <summary>SSO access token, stored per integration in the secure store.</summary>
public record StoredSsoToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>An (account, role) pair the logged-in user can assume via the SSO portal.</summary>
public record AccountRole(string AccountId, string AccountName, string RoleName);

/// <summary>Session payload for the AWS console federation endpoint (field names are its wire format).</summary>
public record FederationSession(
    [property: System.Text.Json.Serialization.JsonPropertyName("sessionId")] string SessionId,
    [property: System.Text.Json.Serialization.JsonPropertyName("sessionKey")] string SessionKey,
    [property: System.Text.Json.Serialization.JsonPropertyName("sessionToken")] string SessionToken);
