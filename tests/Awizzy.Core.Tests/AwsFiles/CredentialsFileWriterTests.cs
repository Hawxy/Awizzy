using System.IO.Abstractions.TestingHelpers;
using Awizzy.Core.Abstractions;
using Awizzy.Core.AwsFiles;
using Awizzy.Core.Models;
using Awizzy.Core.Services;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Awizzy.Core.Tests.AwsFiles;

public class CredentialsFileWriterTests
{
    private static readonly string CredentialsPath = @"C:\Users\test\.aws\credentials";

    private static readonly RoleCredentialSet Credentials = new(
        "AKIATEST",
        "secret123",
        "token456",
        new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));

    private readonly MockFileSystem _fs = new();
    private readonly CredentialsFileWriter _writer;

    public CredentialsFileWriterTests()
    {
        var repository = Substitute.For<IWorkspaceRepository>();
        var workspace = new Workspace();
        workspace.Settings.CredentialsFilePath = CredentialsPath;
        repository.Load().Returns(workspace);
        _writer = new CredentialsFileWriter(
            _fs,
            new CredentialsFilePathResolver(new WorkspaceState(repository)),
            new FakeTimeProvider());
    }

    [Test]
    public async Task WriteProfileAsync_WithNoFile_CreatesFileAndDirectory()
    {
        await _writer.WriteProfileAsync("default", Credentials, "eu-west-1");

        var content = _fs.File.ReadAllText(CredentialsPath);
        await Assert.That(content).Contains("[default]");
        await Assert.That(content).Contains(CredentialsFileWriter.Marker);
        await Assert.That(content).Contains("aws_access_key_id = AKIATEST");
        await Assert.That(content).Contains("aws_secret_access_key = secret123");
        await Assert.That(content).Contains("aws_session_token = token456");
        await Assert.That(content).Contains("region = eu-west-1");
    }

    [Test]
    public async Task WriteProfileAsync_WithoutRegion_OmitsRegionKey()
    {
        await _writer.WriteProfileAsync("default", Credentials, region: null);

        var content = _fs.File.ReadAllText(CredentialsPath);
        await Assert.That(content).DoesNotContain("region");
    }

    [Test]
    public async Task WriteProfileAsync_PreservesForeignProfiles()
    {
        _fs.AddFile(CredentialsPath, new MockFileData(
            "[personal]\naws_access_key_id = AKIAPERSONAL\naws_secret_access_key = personalsecret\n"));

        await _writer.WriteProfileAsync("work", Credentials, "us-east-1");

        var content = _fs.File.ReadAllText(CredentialsPath);
        await Assert.That(content).Contains("[personal]");
        await Assert.That(content).Contains("AKIAPERSONAL");
        await Assert.That(content).Contains("[work]");
    }

    [Test]
    public async Task WriteProfileAsync_RefreshesExistingManagedProfile()
    {
        await _writer.WriteProfileAsync("default", Credentials, "eu-west-1");
        var refreshed = Credentials with { AccessKeyId = "AKIAREFRESHED" };

        await _writer.WriteProfileAsync("default", refreshed, "eu-west-1");

        var content = _fs.File.ReadAllText(CredentialsPath);
        await Assert.That(content).Contains("AKIAREFRESHED");
        await Assert.That(content).DoesNotContain("AKIATEST");
    }

    [Test]
    public async Task WriteProfileAsync_RefusesToOverwriteForeignProfile()
    {
        _fs.AddFile(CredentialsPath, new MockFileData(
            "[work]\naws_access_key_id = AKIAFOREIGN\naws_secret_access_key = foreignsecret\n"));

        await Assert.That(async () => { await _writer.WriteProfileAsync("work", Credentials, "us-east-1"); })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("not written by Awizzy");

        var content = _fs.File.ReadAllText(CredentialsPath);
        await Assert.That(content).Contains("AKIAFOREIGN");
        await Assert.That(content).DoesNotContain("AKIATEST");
    }

    [Test]
    public async Task WriteProfileAsync_LeavesNoTempFileBehind()
    {
        await _writer.WriteProfileAsync("default", Credentials, "eu-west-1");

        await Assert.That(_fs.AllFiles.Count(f => f.Contains("tmp", StringComparison.OrdinalIgnoreCase))).IsEqualTo(0);
    }

    [Test]
    public async Task RemoveProfileAsync_RemovesManagedProfile()
    {
        await _writer.WriteProfileAsync("default", Credentials, "eu-west-1");

        await _writer.RemoveProfileAsync("default");

        var content = _fs.File.ReadAllText(CredentialsPath);
        await Assert.That(content).DoesNotContain("[default]");
        await Assert.That(content).DoesNotContain("AKIATEST");
    }

    [Test]
    public async Task RemoveProfileAsync_LeavesForeignProfileAlone()
    {
        _fs.AddFile(CredentialsPath, new MockFileData(
            "[default]\naws_access_key_id = AKIAFOREIGN\n"));

        await _writer.RemoveProfileAsync("default");

        var content = _fs.File.ReadAllText(CredentialsPath);
        await Assert.That(content).Contains("AKIAFOREIGN");
    }

    [Test]
    public async Task RemoveProfileAsync_WithNoFile_DoesNotCreateOne()
    {
        await _writer.RemoveProfileAsync("default");

        await Assert.That(_fs.File.Exists(CredentialsPath)).IsFalse();
    }
}
