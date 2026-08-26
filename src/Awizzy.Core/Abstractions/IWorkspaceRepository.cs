using Awizzy.Core.Models;

namespace Awizzy.Core.Abstractions;

public interface IWorkspaceRepository
{
    /// <summary>Loads the workspace, creating a default one if the file is missing or unreadable.
    /// An unreadable file is backed up before being replaced.</summary>
    Workspace Load();

    Task SaveAsync(Workspace workspace, CancellationToken ct = default);
}
