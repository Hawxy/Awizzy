using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using Awizzy.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Awizzy.Core.Tests.Services;

public class IntegrationServiceTests
{
    private readonly Workspace _workspace = new();
    private readonly ISsoPortalService _portal = Substitute.For<ISsoPortalService>();
    private readonly ISsoOidcAuthService _auth = Substitute.For<ISsoOidcAuthService>();
    private readonly ISessionManager _sessionManager = Substitute.For<ISessionManager>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly IntegrationService _service;

    public IntegrationServiceTests()
    {
        var repository = Substitute.For<IWorkspaceRepository>();
        repository.Load().Returns(_workspace);
        _service = new IntegrationService(
            new WorkspaceState(repository), _portal, _auth, _sessionManager, _time,
            NullLogger<IntegrationService>.Instance);
    }

    private Task<SsoIntegration> CreateIntegration() =>
        _service.CreateAsync("Acme", "https://acme.awsapps.com/start", "eu-west-1");

    private void PortalReturns(params AccountRole[] roles) =>
        _portal.ListAccountRolesAsync(Arg.Any<SsoIntegration>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AccountRole>>(roles));

    [Test]
    public async Task CreateAsync_AddsIntegration()
    {
        var integration = await CreateIntegration();

        await Assert.That(_workspace.Integrations).Contains(integration);
        await Assert.That(integration.Alias).IsEqualTo("Acme");
    }

    [Test]
    public async Task CreateAsync_WithDuplicateAlias_Throws()
    {
        await CreateIntegration();

        await Assert.That(async () => { await _service.CreateAsync("acme", "https://x.awsapps.com/start", "us-east-1"); })
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CreateAsync_WithHttpUrl_Throws()
    {
        await Assert.That(async () => { await _service.CreateAsync("Acme", "http://insecure.example.com", "eu-west-1"); })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task SyncSessionsAsync_DerivesProfileNameFromAccountName()
    {
        var integration = await CreateIntegration();
        PortalReturns(
            new AccountRole("111111111111", "Acme Prod", "Admin"),
            new AccountRole("111111111111", "Acme Prod", "ReadOnly"));

        var result = await _service.SyncSessionsAsync(integration.Id);

        await Assert.That(result.Added).IsEqualTo(2);
        await Assert.That(_workspace.Sessions).Count().IsEqualTo(2);
        await Assert.That(_workspace.Sessions[0].ProfileName).IsEqualTo("acme-prod");
        await Assert.That(_workspace.Sessions[0].Region).IsEqualTo("eu-west-1");
    }

    [Test]
    public async Task SyncSessionsAsync_RolesInSameAccountShareTheAccountProfile()
    {
        var integration = await CreateIntegration();
        PortalReturns(new AccountRole("111111111111", "Acme Prod", "Admin"));
        await _service.SyncSessionsAsync(integration.Id);
        _workspace.Sessions[0].ProfileName = "my-custom-prod";

        // A new role appears in the same account; it inherits the customized account profile.
        PortalReturns(
            new AccountRole("111111111111", "Acme Prod", "Admin"),
            new AccountRole("111111111111", "Acme Prod", "ReadOnly"));
        await _service.SyncSessionsAsync(integration.Id);

        var newSession = _workspace.Sessions.First(s => s.RoleName == "ReadOnly");
        await Assert.That(newSession.ProfileName).IsEqualTo("my-custom-prod");
    }

    [Test]
    public async Task SyncSessionsAsync_PreservesUserSettingsOnExistingSessions()
    {
        var integration = await CreateIntegration();
        PortalReturns(new AccountRole("111111111111", "prod", "Admin"));
        await _service.SyncSessionsAsync(integration.Id);
        var session = _workspace.Sessions[0];
        session.ProfileName = "custom-name";
        session.Region = "us-west-2";

        PortalReturns(new AccountRole("111111111111", "prod-renamed", "Admin"));
        var result = await _service.SyncSessionsAsync(integration.Id);

        await Assert.That(result.Added).IsEqualTo(0);
        await Assert.That(_workspace.Sessions).HasSingleItem();
        await Assert.That(session.ProfileName).IsEqualTo("custom-name");
        await Assert.That(session.Region).IsEqualTo("us-west-2");
        await Assert.That(session.AccountName).IsEqualTo("prod-renamed");
    }

    [Test]
    public async Task SyncSessionsAsync_RecordsSyncTime()
    {
        var integration = await CreateIntegration();
        PortalReturns(new AccountRole("111111111111", "prod", "Admin"));

        await Assert.That(integration.LastSyncedAt).IsNull();
        await _service.SyncSessionsAsync(integration.Id);

        await Assert.That(integration.LastSyncedAt).IsEqualTo(_time.GetUtcNow());
    }

    [Test]
    public async Task SyncSessionsAsync_RemovesSessionsWhoseRoleDisappeared()
    {
        var integration = await CreateIntegration();
        PortalReturns(
            new AccountRole("111111111111", "prod", "Admin"),
            new AccountRole("111111111111", "prod", "ReadOnly"));
        await _service.SyncSessionsAsync(integration.Id);
        var removedSession = _workspace.Sessions.First(s => s.RoleName == "ReadOnly");
        removedSession.State = SessionState.Active;

        PortalReturns(new AccountRole("111111111111", "prod", "Admin"));
        var result = await _service.SyncSessionsAsync(integration.Id);

        await Assert.That(result.Removed).IsEqualTo(1);
        await Assert.That(_workspace.Sessions).HasSingleItem();
        await _sessionManager.Received(1).StopSessionAsync(removedSession.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LogoutAsync_StopsSessionsClearsListAndDiscardsToken()
    {
        var integration = await CreateIntegration();
        PortalReturns(
            new AccountRole("111111111111", "prod", "Admin"),
            new AccountRole("222222222222", "dev", "Admin"));
        await _service.SyncSessionsAsync(integration.Id);
        var active = _workspace.Sessions[0];
        active.State = SessionState.Active;

        await _service.LogoutAsync(integration.Id);

        await _sessionManager.Received(1).StopSessionAsync(active.Id, Arg.Any<CancellationToken>());
        await _auth.Received(1).LogoutAsync(integration, Arg.Any<CancellationToken>());
        await Assert.That(_workspace.Sessions).IsEmpty();
        await Assert.That(_workspace.Integrations).Contains(integration);
    }

    [Test]
    public async Task DeleteAsync_CascadesSessionsAndLogsOut()
    {
        var integration = await CreateIntegration();
        PortalReturns(new AccountRole("111111111111", "prod", "Admin"));
        await _service.SyncSessionsAsync(integration.Id);
        var session = _workspace.Sessions[0];
        session.State = SessionState.Active;

        await _service.DeleteAsync(integration.Id);

        await _sessionManager.Received(1).StopSessionAsync(session.Id, Arg.Any<CancellationToken>());
        await _auth.Received(1).LogoutAsync(integration, Arg.Any<CancellationToken>());
        await Assert.That(_workspace.Integrations).IsEmpty();
        await Assert.That(_workspace.Sessions).IsEmpty();
    }
}
