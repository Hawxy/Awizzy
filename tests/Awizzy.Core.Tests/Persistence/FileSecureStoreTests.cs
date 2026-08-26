using System.IO.Abstractions.TestingHelpers;
using Awizzy.Core.Persistence;
using Awizzy.Core.Tests.TestDoubles;

namespace Awizzy.Core.Tests.Persistence;

public class FileSecureStoreTests
{
    private static readonly AppPaths Paths = new(@"C:\appdata\Awizzy");

    private static FileSecureStore CreateStore(MockFileSystem fs) =>
        new(fs, new FakeDataCipher(), Paths);

    [Test]
    public async Task GetAsync_WithUnknownKey_ReturnsNull()
    {
        var store = CreateStore(new MockFileSystem());

        await Assert.That(await store.GetAsync("sso-token:missing")).IsNull();
    }

    [Test]
    public async Task SetAndGet_RoundTripsValue()
    {
        var store = CreateStore(new MockFileSystem());

        await store.SetAsync("sso-token:abc", """{"accessToken":"secret"}""");

        await Assert.That(await store.GetAsync("sso-token:abc"))
            .IsEqualTo("""{"accessToken":"secret"}""");
    }

    [Test]
    public async Task Set_DoesNotStoreValueOrKeyInPlaintext()
    {
        var fs = new MockFileSystem();
        var store = CreateStore(fs);

        await store.SetAsync("sso-token:abc", "super-secret-value");

        var files = fs.Directory.GetFiles(Paths.SecureDirectory);
        await Assert.That(files).HasSingleItem();
        await Assert.That(files[0]).DoesNotContain("sso-token");
        var content = System.Text.Encoding.UTF8.GetString(fs.File.ReadAllBytes(files[0]));
        await Assert.That(content).DoesNotContain("super-secret-value");
    }

    [Test]
    public async Task GetAsync_WithUndecryptableBlob_ReturnsNullAndDeletesFile()
    {
        var fs = new MockFileSystem();
        var store = CreateStore(fs);
        await store.SetAsync("sso-token:abc", "value");
        var file = fs.Directory.GetFiles(Paths.SecureDirectory)[0];
        fs.File.WriteAllBytes(file, [1, 2, 3]);

        await Assert.That(await store.GetAsync("sso-token:abc")).IsNull();
        await Assert.That(fs.File.Exists(file)).IsFalse();
    }

    [Test]
    public async Task DeleteAsync_RemovesValue()
    {
        var store = CreateStore(new MockFileSystem());
        await store.SetAsync("sso-token:abc", "value");

        await store.DeleteAsync("sso-token:abc");

        await Assert.That(await store.GetAsync("sso-token:abc")).IsNull();
    }

    [Test]
    public async Task DeleteAsync_WithUnknownKey_DoesNotThrow()
    {
        var store = CreateStore(new MockFileSystem());

        await store.DeleteAsync("never-existed");
    }
}
