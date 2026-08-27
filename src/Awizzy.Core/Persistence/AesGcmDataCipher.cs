using System.Security.Cryptography;
using Awizzy.Core.Abstractions;

namespace Awizzy.Core.Persistence;

/// <summary>Encrypts blobs with AES-256-GCM under a caller-supplied key (on macOS the key
/// lives in the login Keychain). Blob layout: version byte, 12-byte nonce, 16-byte tag,
/// ciphertext. The version byte is a data-format constant; bump it only with a read path
/// for the old layout.</summary>
public sealed class AesGcmDataCipher(byte[] key) : IDataCipher
{
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public byte[] Protect(byte[] plaintext)
    {
        var result = new byte[1 + NonceSize + TagSize + plaintext.Length];
        result[0] = FormatVersion;
        var nonce = result.AsSpan(1, NonceSize);
        var tag = result.AsSpan(1 + NonceSize, TagSize);
        var ciphertext = result.AsSpan(1 + NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return result;
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        // Callers rely on CryptographicException for "not decryptable" (they fall back to
        // a fresh login or a fresh workspace), so malformed input maps to it as well.
        if (ciphertext.Length < 1 + NonceSize + TagSize || ciphertext[0] != FormatVersion)
            throw new CryptographicException("The blob is not in a recognized format.");

        var nonce = ciphertext.AsSpan(1, NonceSize);
        var tag = ciphertext.AsSpan(1 + NonceSize, TagSize);
        var payload = ciphertext.AsSpan(1 + NonceSize + TagSize);
        var plaintext = new byte[payload.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, payload, tag, plaintext);
        return plaintext;
    }
}
