using Awizzy.Core.Models;

namespace Awizzy.Core.Abstractions;

public interface ICredentialsFileWriter
{
    Task WriteProfileAsync(string profileName, RoleCredentialSet credentials, string? region, CancellationToken ct = default);

    /// <summary>Removes the profile section, but only if this app wrote it.</summary>
    Task RemoveProfileAsync(string profileName, CancellationToken ct = default);
}
