using System.IO.Abstractions;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;

namespace Awizzy.Core.AwsFiles;

public class CredentialsFileWriter(
    IFileSystem fs,
    CredentialsFilePathResolver pathResolver,
    TimeProvider time) : ICredentialsFileWriter
{
    public const string Marker = "; managed by awizzy";

    private const int RetryCount = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task WriteProfileAsync(string profileName, RoleCredentialSet credentials, string? region, CancellationToken ct = default) =>
        MutateAsync(ini =>
        {
            var values = new List<KeyValuePair<string, string>>
            {
                new("aws_access_key_id", credentials.AccessKeyId),
                new("aws_secret_access_key", credentials.SecretAccessKey),
                new("aws_session_token", credentials.SessionToken),
            };
            if (region is { Length: > 0 })
                values.Add(new("region", region));

            ini.SetSection(profileName, values, Marker);
        }, ct);

    public Task RemoveProfileAsync(string profileName, CancellationToken ct = default) =>
        MutateAsync(ini =>
        {
            if (ini.SectionHasMarker(profileName, Marker))
                ini.RemoveSection(profileName);
        }, ct);

    private async Task MutateAsync(Action<IniFile> mutate, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    MutateOnce(mutate);
                    return;
                }
                catch (IOException) when (attempt < RetryCount)
                {
                    // Another tool (AWS CLI, SDK) may briefly hold the file open.
                    await Task.Delay(RetryDelay, time, ct);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private void MutateOnce(Action<IniFile> mutate)
    {
        var path = pathResolver.GetPath();
        var original = fs.File.Exists(path) ? fs.File.ReadAllText(path) : null;
        var ini = original is not null ? IniFile.Parse(original) : IniFile.Empty();

        mutate(ini);

        var updated = ini.ToString();
        var unchanged = original is not null ? updated == original : updated.Length == 0;
        if (unchanged)
            return;

        var directory = fs.Path.GetDirectoryName(path);
        if (directory is { Length: > 0 })
            fs.Directory.CreateDirectory(directory);
        fs.File.WriteAllText(path, updated);
    }
}
