using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Awizzy.Core.Abstractions;

namespace Awizzy.Core.Persistence;

/// <summary>Stores each secret as an encrypted blob file. File-based rather than the OS credential
/// manager because SSO access tokens can exceed Windows Credential Manager size limits.</summary>
public class FileSecureStore(IFileSystem fs, IDataCipher cipher, AppPaths paths) : ISecureStore
{
    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (!fs.File.Exists(path))
            return Task.FromResult<string?>(null);

        var ciphertext = fs.File.ReadAllBytes(path);
        try
        {
            var plaintext = cipher.Unprotect(ciphertext);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(plaintext));
        }
        catch (CryptographicException)
        {
            // Undecryptable blob (corrupt, or from another machine/user): treat the secret
            // as absent so the caller falls back to a fresh login instead of failing.
            fs.File.Delete(path);
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        fs.Directory.CreateDirectory(paths.SecureDirectory);
        var path = PathFor(key);
        var ciphertext = cipher.Protect(Encoding.UTF8.GetBytes(value));
        fs.File.WriteAllBytes(path, ciphertext);

        // Windows relies on the profile directory's ACL; Unix needs explicit user-only modes.
        if (!OperatingSystem.IsWindows())
        {
            fs.File.SetUnixFileMode(paths.SecureDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            fs.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (fs.File.Exists(path))
            fs.File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return fs.Path.Combine(paths.SecureDirectory, hash + ".bin");
    }
}
