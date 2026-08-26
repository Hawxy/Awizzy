namespace Awizzy.Core.Models;

/// <summary>Short-lived role credentials returned by the SSO portal.</summary>
public record RoleCredentialSet(
    string AccessKeyId,
    string SecretAccessKey,
    string SessionToken,
    DateTimeOffset Expiration);
