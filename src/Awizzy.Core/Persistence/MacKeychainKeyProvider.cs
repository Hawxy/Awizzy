using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Awizzy.Core.Persistence;

/// <summary>Keeps the AES master key as a generic password in the user's login Keychain.
/// Uses the classic SecKeychain C API: a stable ABI that needs no CoreFoundation interop,
/// and the same one keytar-style tools use. The BCL exposes no Keychain API on the plain
/// net TFM, so this is a direct Security.framework P/Invoke.</summary>
[SupportedOSPlatform("macos")]
public static class MacKeychainKeyProvider
{
    private const string ServiceName = "Awizzy";
    private const string AccountName = "master-key";
    private const int ErrSecItemNotFound = -25300;

    public static byte[] GetOrCreateMasterKey()
    {
        if (TryFind(out var key))
            return key;

        key = RandomNumberGenerator.GetBytes(32);
        Add(key);
        return key;
    }

    private static bool TryFind(out byte[] key)
    {
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)ServiceName.Length, ServiceName,
            (uint)AccountName.Length, AccountName,
            out var length, out var data, IntPtr.Zero);

        if (status == ErrSecItemNotFound)
        {
            key = [];
            return false;
        }

        if (status != 0)
            throw new CryptographicException(
                $"Reading the Awizzy master key from the Keychain failed (OSStatus {status}).");

        try
        {
            key = new byte[length];
            Marshal.Copy(data, key, 0, (int)length);
            return true;
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
        }
    }

    private static void Add(byte[] key)
    {
        var status = SecKeychainAddGenericPassword(
            IntPtr.Zero,
            (uint)ServiceName.Length, ServiceName,
            (uint)AccountName.Length, AccountName,
            (uint)key.Length, key, IntPtr.Zero);

        if (status != 0)
            throw new CryptographicException(
                $"Storing the Awizzy master key in the Keychain failed (OSStatus {status}).");
    }

    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";

    // Strings marshal as UTF-8 on Unix; the length arguments are byte counts, which match
    // because the service and account names are ASCII.
    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength, string serviceName,
        uint accountNameLength, string accountName,
        out uint passwordLength, out IntPtr passwordData,
        IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength, string serviceName,
        uint accountNameLength, string accountName,
        uint passwordLength, byte[] passwordData,
        IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);
}
