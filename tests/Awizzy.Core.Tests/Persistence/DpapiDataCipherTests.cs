using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Awizzy.Core.Persistence;
using TUnit.Core.Enums;

namespace Awizzy.Core.Tests.Persistence;

// SupportedOSPlatform satisfies the analyzer; RunOn is the runtime skip.
[SupportedOSPlatform("windows")]
[RunOn(OS.Windows)]
public class DpapiDataCipherTests
{
    [Test]
    public async Task ProtectUnprotect_RoundTrips()
    {
        var cipher = new DpapiDataCipher();

        var roundTripped = cipher.Unprotect(cipher.Protect("secret"u8.ToArray()));

        await Assert.That(Encoding.UTF8.GetString(roundTripped)).IsEqualTo("secret");
    }

    [Test]
    public async Task Unprotect_ReadsBlobsProtectedWithLegacyEntropy()
    {
        var legacyBlob = ProtectedData.Protect(
            "secret"u8.ToArray(), "AwsProfileManager.v1"u8.ToArray(), DataProtectionScope.CurrentUser);
        var cipher = new DpapiDataCipher();

        var plaintext = cipher.Unprotect(legacyBlob);

        await Assert.That(Encoding.UTF8.GetString(plaintext)).IsEqualTo("secret");
    }
}
