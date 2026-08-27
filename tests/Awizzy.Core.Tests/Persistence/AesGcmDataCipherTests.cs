using System.Security.Cryptography;
using System.Text;
using Awizzy.Core.Persistence;

namespace Awizzy.Core.Tests.Persistence;

public class AesGcmDataCipherTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Test]
    public async Task ProtectUnprotect_RoundTrips()
    {
        var cipher = new AesGcmDataCipher(Key);

        var roundTripped = cipher.Unprotect(cipher.Protect("secret"u8.ToArray()));

        await Assert.That(Encoding.UTF8.GetString(roundTripped)).IsEqualTo("secret");
    }

    [Test]
    public async Task Protect_UsesAFreshNoncePerCall()
    {
        var cipher = new AesGcmDataCipher(Key);

        var first = cipher.Protect("secret"u8.ToArray());
        var second = cipher.Protect("secret"u8.ToArray());

        await Assert.That(first.SequenceEqual(second)).IsFalse();
    }

    [Test]
    public async Task Unprotect_TamperedBlob_ThrowsCryptographicException()
    {
        var cipher = new AesGcmDataCipher(Key);
        var blob = cipher.Protect("secret"u8.ToArray());
        blob[^1] ^= 0xFF;

        await Assert.That(() => cipher.Unprotect(blob)).Throws<CryptographicException>();
    }

    [Test]
    public async Task Unprotect_TruncatedBlob_ThrowsCryptographicException()
    {
        var cipher = new AesGcmDataCipher(Key);

        await Assert.That(() => cipher.Unprotect([1, 2, 3])).Throws<CryptographicException>();
    }

    [Test]
    public async Task Unprotect_UnknownFormatVersion_ThrowsCryptographicException()
    {
        var cipher = new AesGcmDataCipher(Key);
        var blob = cipher.Protect("secret"u8.ToArray());
        blob[0] = 9;

        await Assert.That(() => cipher.Unprotect(blob)).Throws<CryptographicException>();
    }

    [Test]
    public async Task Unprotect_WithDifferentKey_ThrowsCryptographicException()
    {
        var blob = new AesGcmDataCipher(Key).Protect("secret"u8.ToArray());
        var otherCipher = new AesGcmDataCipher(Enumerable.Repeat((byte)7, 32).ToArray());

        await Assert.That(() => otherCipher.Unprotect(blob)).Throws<CryptographicException>();
    }
}
