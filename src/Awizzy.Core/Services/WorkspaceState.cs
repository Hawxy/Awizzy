using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;

namespace Awizzy.Core.Services;

/// <summary>Owns the single in-memory workspace instance and serializes saves.</summary>
public class WorkspaceState(IWorkspaceRepository repository)
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private Workspace? _workspace;

    public Workspace Workspace => _workspace ??= repository.Load();

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _saveLock.WaitAsync(ct);
        try
        {
            await repository.SaveAsync(Workspace, ct);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
