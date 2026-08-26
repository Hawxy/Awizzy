namespace Awizzy.Core.Models;

/// <summary>An IAM Identity Center (AWS SSO) connection. The access token itself lives in the secure store.</summary>
public class SsoIntegration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Alias { get; set; }
    public required string PortalUrl { get; set; }
    public required string Region { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }

    public bool IsLoggedIn(TimeProvider time) =>
        AccessTokenExpiresAt is { } expiry && expiry > time.GetUtcNow();
}
