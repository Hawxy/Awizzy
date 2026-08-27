using System.IO.Abstractions.TestingHelpers;
using Awizzy.Core.Models;
using Awizzy.Core.Persistence;
using Awizzy.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TUnit.Core.Enums;

namespace Awizzy.Core.Tests.Persistence;

public class WorkspaceRepositoryTests
{
    private static readonly AppPaths Paths = new(TestPaths.Root("appdata/Awizzy"));

    private static WorkspaceRepository CreateRepository(MockFileSystem fs) =>
        new(fs, new FakeDataCipher(), Paths, new FakeTimeProvider(), NullLogger<WorkspaceRepository>.Instance);

    [Test]
    public async Task Load_WithNoFile_ReturnsEmptyWorkspace()
    {
        var repository = CreateRepository(new MockFileSystem());

        var workspace = repository.Load();

        await Assert.That(workspace.Integrations).IsEmpty();
        await Assert.That(workspace.Sessions).IsEmpty();
    }

    [Test]
    public async Task SaveAndLoad_RoundTripsWorkspace()
    {
        var fs = new MockFileSystem();
        var repository = CreateRepository(fs);
        var workspace = repository.Load();
        var integration = new SsoIntegration
        {
            Alias = "Acme",
            PortalUrl = "https://acme.awsapps.com/start",
            Region = "eu-west-1",
        };
        workspace.Integrations.Add(integration);
        workspace.Sessions.Add(new AwsSession
        {
            IntegrationId = integration.Id,
            AccountId = "123456789012",
            AccountName = "acme-prod",
            RoleName = "Admin",
            Region = "eu-west-1",
            ProfileName = "acme-prod",
        });

        await repository.SaveAsync(workspace);
        var reloaded = CreateRepository(fs).Load();

        await Assert.That(reloaded.Integrations).HasSingleItem();
        await Assert.That(reloaded.Integrations[0].Alias).IsEqualTo("Acme");
        await Assert.That(reloaded.Sessions).HasSingleItem();
        await Assert.That(reloaded.Sessions[0].ProfileName).IsEqualTo("acme-prod");
        await Assert.That(reloaded.Sessions[0].State).IsEqualTo(SessionState.Inactive);
    }

    [Test]
    [ExcludeOn(OS.Windows)]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task SaveAsync_KeepsWorkspaceFileUserOnly()
    {
        var fs = new MockFileSystem();
        var repository = CreateRepository(fs);

        await repository.SaveAsync(repository.Load());

        await Assert.That(fs.File.GetUnixFileMode(Paths.WorkspaceFile))
            .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Test]
    public async Task Load_FillsInMissingProfileNamesFromAccountName()
    {
        var fs = new MockFileSystem();
        var repository = CreateRepository(fs);
        var workspace = repository.Load();
        workspace.Sessions.Add(new AwsSession
        {
            IntegrationId = Guid.NewGuid(),
            AccountId = "123456789012",
            AccountName = "Acme Prod",
            RoleName = "Admin",
            Region = "eu-west-1",
            ProfileName = "",
        });
        await repository.SaveAsync(workspace);

        var reloaded = CreateRepository(fs).Load();

        await Assert.That(reloaded.Sessions[0].ProfileName).IsEqualTo("acme-prod");
    }

    [Test]
    public async Task Save_EncryptsFileOnDisk()
    {
        var fs = new MockFileSystem();
        var repository = CreateRepository(fs);
        var workspace = repository.Load();
        workspace.Integrations.Add(new SsoIntegration
        {
            Alias = "Acme",
            PortalUrl = "https://acme.awsapps.com/start",
            Region = "eu-west-1",
        });

        await repository.SaveAsync(workspace);

        var bytes = fs.File.ReadAllBytes(Paths.WorkspaceFile);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        await Assert.That(text).DoesNotContain("Acme");
    }

    [Test]
    public async Task Load_WithCorruptFile_BacksUpAndReturnsFreshWorkspace()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory(Paths.RootDirectory);
        fs.File.WriteAllText(Paths.WorkspaceFile, "not encrypted at all");

        var workspace = CreateRepository(fs).Load();

        await Assert.That(workspace.Integrations).IsEmpty();
        await Assert.That(fs.File.Exists(Paths.WorkspaceFile)).IsFalse();
        var backups = fs.Directory.GetFiles(Paths.RootDirectory, "workspace.json.bad-*");
        await Assert.That(backups).HasSingleItem();
    }

    [Test]
    public async Task Save_LeavesNoTempFileBehind()
    {
        var fs = new MockFileSystem();
        var repository = CreateRepository(fs);

        await repository.SaveAsync(repository.Load());

        await Assert.That(fs.File.Exists(Paths.WorkspaceFile + ".tmp")).IsFalse();
        await Assert.That(fs.File.Exists(Paths.WorkspaceFile)).IsTrue();
    }
}
