using Awizzy.Core.Abstractions;

namespace Awizzy.Core.Tests.TestDoubles;

/// <summary>Reversible stand-in for DPAPI: prefixes a marker and inverts every byte,
/// so unprotecting plain (unprotected) data fails like real DPAPI would.</summary>
public class FakeDataCipher : IDataCipher
{
    private static readonly byte[] Marker = [0xAF, 0x91];

    public byte[] Protect(byte[] plaintext) =>
        [.. Marker, .. plaintext.Select(b => (byte)~b)];

    public byte[] Unprotect(byte[] ciphertext)
    {
        if (ciphertext.Length < Marker.Length || ciphertext[0] != Marker[0] || ciphertext[1] != Marker[1])
            throw new System.Security.Cryptography.CryptographicException("Not protected data.");
        return [.. ciphertext.Skip(Marker.Length).Select(b => (byte)~b)];
    }
}
