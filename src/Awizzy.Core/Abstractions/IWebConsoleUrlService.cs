using Awizzy.Core.Models;

namespace Awizzy.Core.Abstractions;

public interface IWebConsoleUrlService
{
    /// <summary>Builds a federation sign-in URL that opens the AWS web console
    /// authenticated with the given role credentials, in the given region.</summary>
    Task<string> BuildConsoleUrlAsync(RoleCredentialSet credentials, string region, CancellationToken ct = default);
}
