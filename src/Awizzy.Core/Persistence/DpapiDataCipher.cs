using System.Runtime.Versioning;
using System.Security.Cryptography;
using Awizzy.Core.Abstractions;

namespace Awizzy.Core.Persistence;

[SupportedOSPlatform("windows")]
public class DpapiDataCipher : IDataCipher
{
    private static readonly byte[] Entropy = "Awizzy.v1"u8.ToArray();

    // Blobs written before the rename to Awizzy used this entropy; keep reading them.
    private static readonly byte[] LegacyEntropy = "AwsProfileManager.v1"u8.ToArray();

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext)
    {
        try
        {
            return ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return ProtectedData.Unprotect(ciphertext, LegacyEntropy, DataProtectionScope.CurrentUser);
        }
    }
}
