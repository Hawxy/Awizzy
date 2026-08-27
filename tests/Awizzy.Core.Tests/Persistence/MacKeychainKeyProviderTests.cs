using System.Runtime.Versioning;
using Awizzy.Core.Persistence;
using TUnit.Core.Enums;

namespace Awizzy.Core.Tests.Persistence;

// SupportedOSPlatform satisfies the analyzer; RunOn is the runtime skip.
[SupportedOSPlatform("macos")]
[RunOn(OS.MacOs)]
public class MacKeychainKeyProviderTests
{
    [Test]
    public async Task GetOrCreateMasterKey_Returns32StableBytes()
    {
        var first = MacKeychainKeyProvider.GetOrCreateMasterKey();
        var second = MacKeychainKeyProvider.GetOrCreateMasterKey();

        await Assert.That(first).Count().IsEqualTo(32);
        await Assert.That(second.SequenceEqual(first)).IsTrue();
    }
}
