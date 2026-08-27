using System.IO.Abstractions;
using System.Text.Json;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using Microsoft.Extensions.Logging;

namespace Awizzy.Core.Persistence;

public class WorkspaceRepository(
    IFileSystem fs,
    IDataCipher cipher,
    AppPaths paths,
    TimeProvider time,
    ILogger<WorkspaceRepository> logger) : IWorkspaceRepository
{
    public Workspace Load()
    {
        if (!fs.File.Exists(paths.WorkspaceFile))
            return CreateDefault();

        try
        {
            var ciphertext = fs.File.ReadAllBytes(paths.WorkspaceFile);
            var json = cipher.Unprotect(ciphertext);
            var workspace = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Workspace)
                ?? throw new JsonException("Workspace deserialized to null.");
            EnsureDefaults(workspace);
            return workspace;
        }
        catch (Exception ex) when (ex is JsonException or System.Security.Cryptography.CryptographicException or IOException)
        {
            var backup = $"{paths.WorkspaceFile}.bad-{time.GetUtcNow():yyyyMMdd-HHmmss}";
            logger.LogError(ex, "Workspace file is unreadable; backing it up to {Backup} and starting fresh.", backup);
            fs.File.Move(paths.WorkspaceFile, backup, overwrite: true);
            return CreateDefault();
        }
    }

    public Task SaveAsync(Workspace workspace, CancellationToken ct = default)
    {
        fs.Directory.CreateDirectory(paths.RootDirectory);
        var json = JsonSerializer.SerializeToUtf8Bytes(workspace, CoreJsonContext.Default.Workspace);
        var ciphertext = cipher.Protect(json);

        // Atomic-ish replace: write to a temp file on the same volume, then move over the target.
        var temp = paths.WorkspaceFile + ".tmp";
        fs.File.WriteAllBytes(temp, ciphertext);
        if (!OperatingSystem.IsWindows())
            fs.File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        fs.File.Move(temp, paths.WorkspaceFile, overwrite: true);
        return Task.CompletedTask;
    }

    private static Workspace CreateDefault() => new();

    private static void EnsureDefaults(Workspace workspace)
    {
        foreach (var session in workspace.Sessions.Where(s => string.IsNullOrWhiteSpace(s.ProfileName)))
            session.ProfileName = ProfileNames.DeriveFromAccountName(session.AccountName);
    }
}
