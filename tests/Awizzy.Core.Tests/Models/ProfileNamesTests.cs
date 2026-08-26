using Awizzy.Core.Models;

namespace Awizzy.Core.Tests.Models;

public class ProfileNamesTests
{
    [Test]
    [Arguments("Acme Prod", "acme-prod")]
    [Arguments("acme-prod", "acme-prod")]
    [Arguments("Acme  (Prod)", "acme-prod")]
    [Arguments("ACME_dev.01", "acme_dev.01")]
    [Arguments("  spaced  ", "spaced")]
    [Arguments("???", "default")]
    public async Task DeriveFromAccountName_SanitizesName(string accountName, string expected)
    {
        await Assert.That(ProfileNames.DeriveFromAccountName(accountName)).IsEqualTo(expected);
    }

    [Test]
    public async Task Validate_TrimsValidName()
    {
        await Assert.That(ProfileNames.Validate("  my-profile ")).IsEqualTo("my-profile");
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("bad[name]")]
    [Arguments("bad\nname")]
    public async Task Validate_RejectsInvalidNames(string name)
    {
        await Assert.That(() => ProfileNames.Validate(name)).Throws<ArgumentException>();
    }
}
