namespace Awizzy.Core.Abstractions;

/// <summary>Encrypted key-value store for secrets (SSO tokens, OIDC client registrations).</summary>
public interface ISecureStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
