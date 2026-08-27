using System.Text.Json.Serialization;

namespace Awizzy.Core.Models;

/// <summary>One (account, role) pair discovered from an integration. Identity is (IntegrationId, AccountId, RoleName).</summary>
public class AwsSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid IntegrationId { get; set; }
    public required string AccountId { get; set; }
    public required string AccountName { get; set; }
    public required string RoleName { get; set; }
    public required string Region { get; set; }

    /// <summary>Credentials-file profile this session writes to. Defaults to a name derived
    /// from the account, so every role in an account shares the account's profile.</summary>
    public required string ProfileName { get; set; }

    [JsonIgnore]
    public SessionState State { get; set; } = SessionState.Inactive;

    [JsonIgnore]
    public DateTimeOffset? CredentialsExpireAt { get; set; }

    [JsonIgnore]
    public string? ErrorMessage { get; set; }

    public string DisplayName => $"{AccountName} / {RoleName}";

    /// <summary>Stable key for settings that reference a role (MCP exclusions, favorites);
    /// survives sessions being recreated by sync.</summary>
    [JsonIgnore]
    public string RoleKey => $"{AccountId}/{RoleName}";

    public bool HasSameIdentity(Guid integrationId, string accountId, string roleName) =>
        IntegrationId == integrationId
        && string.Equals(AccountId, accountId, StringComparison.Ordinal)
        && string.Equals(RoleName, roleName, StringComparison.Ordinal);
}
