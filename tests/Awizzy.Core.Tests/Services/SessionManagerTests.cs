using Awizzy.Core.Abstractions;
using Awizzy.Core.Exceptions;
using Awizzy.Core.Models;
using Awizzy.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Awizzy.Core.Tests.Services;

public class SessionManagerTests
{
    private readonly Workspace _workspace = new();
    private readonly ISsoPortalService _portal = Substitute.For<ISsoPortalService>();
    private readonly ICredentialsFileWriter _writer = Substitute.For<ICredentialsFileWriter>();
    private readonly SsoIntegration _integration;
    private readonly AwsSession _session;
    private readonly SessionManager _manager;

    private static readonly RoleCredentialSet Credentials = new(
        "AKIA123", "secret", "token",
        new DateTimeOffset(2026, 8, 24, 13, 0, 0, TimeSpan.Zero));

    public SessionManagerTests()
    {
        _integration = new SsoIntegration
        {
            Alias = "Acme",
            PortalUrl = "https://acme.awsapps.com/start",
            Region = "eu-west-1",
        };
        _session = new AwsSession
        {
            IntegrationId = _integration.Id,
            AccountId = "111111111111",
            AccountName = "prod",
            RoleName = "Admin",
            Region = "eu-west-1",
            ProfileName = "acme-prod",
        };
        _workspace.Integrations.Add(_integration);
        _workspace.Sessions.Add(_session);

        var repository = Substitute.For<IWorkspaceRepository>();
        repository.Load().Returns(_workspace);
        _manager = new SessionManager(
            new WorkspaceState(repository), _portal, _writer,
            NullLogger<SessionManager>.Instance);

        _portal.GetRoleCredentialsAsync(Arg.Any<SsoIntegration>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Credentials));
    }

    [Test]
    public async Task StartSessionAsync_WritesCredentialsAndActivates()
    {
        await _manager.StartSessionAsync(_session.Id);

        await _writer.Received(1).WriteProfileAsync(
            "acme-prod", Credentials, "eu-west-1", Arg.Any<CancellationToken>());
        await Assert.That(_session.State).IsEqualTo(SessionState.Active);
        await Assert.That(_session.CredentialsExpireAt).IsEqualTo(Credentials.Expiration);
    }

    [Test]
    public async Task StartSessionAsync_StopsConflictingSessionOnSameProfileName()
    {
        var other = new AwsSession
        {
            IntegrationId = _integration.Id,
            AccountId = "111111111111",
            AccountName = "prod",
            RoleName = "ReadOnly",
            Region = "eu-west-1",
            ProfileName = "ACME-PROD",
            State = SessionState.Active,
        };
        _workspace.Sessions.Add(other);

        await _manager.StartSessionAsync(_session.Id);

        await Assert.That(other.State).IsEqualTo(SessionState.Inactive);
        await Assert.That(_session.State).IsEqualTo(SessionState.Active);
    }

    [Test]
    public async Task StartSessionAsync_LeavesSessionsOnOtherProfilesRunning()
    {
        var other = new AwsSession
        {
            IntegrationId = _integration.Id,
            AccountId = "222222222222",
            AccountName = "dev",
            RoleName = "Admin",
            Region = "eu-west-1",
            ProfileName = "acme-dev",
            State = SessionState.Active,
        };
        _workspace.Sessions.Add(other);

        await _manager.StartSessionAsync(_session.Id);

        await Assert.That(other.State).IsEqualTo(SessionState.Active);
    }

    [Test]
    public async Task StartSessionAsync_WhenSsoSessionExpired_RevertsToInactiveAndRethrows()
    {
        _portal.GetRoleCredentialsAsync(Arg.Any<SsoIntegration>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<RoleCredentialSet>(new SsoSessionExpiredException("expired")));

        await Assert.That(async () => { await _manager.StartSessionAsync(_session.Id); })
            .Throws<SsoSessionExpiredException>();
        await Assert.That(_session.State).IsEqualTo(SessionState.Inactive);
        await Assert.That(_session.ErrorMessage).IsNull();
    }

    [Test]
    public async Task StopSessionAsync_RemovesProfileAndDeactivates()
    {
        await _manager.StartSessionAsync(_session.Id);

        await _manager.StopSessionAsync(_session.Id);

        await _writer.Received(1).RemoveProfileAsync("acme-prod", Arg.Any<CancellationToken>());
        await Assert.That(_session.State).IsEqualTo(SessionState.Inactive);
        await Assert.That(_session.CredentialsExpireAt).IsNull();
    }

    [Test]
    public async Task RefreshSessionAsync_OnActiveSession_RewritesCredentials()
    {
        await _manager.StartSessionAsync(_session.Id);
        _writer.ClearReceivedCalls();

        await _manager.RefreshSessionAsync(_session.Id);

        await _writer.Received(1).WriteProfileAsync(
            "acme-prod", Credentials, "eu-west-1", Arg.Any<CancellationToken>());
        await Assert.That(_session.State).IsEqualTo(SessionState.Active);
    }

    [Test]
    public async Task RefreshSessionAsync_OnTransientFailure_StaysActiveForRetry()
    {
        await _manager.StartSessionAsync(_session.Id);
        _portal.GetRoleCredentialsAsync(Arg.Any<SsoIntegration>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<RoleCredentialSet>(new IOException("network blip")));

        await Assert.That(async () => { await _manager.RefreshSessionAsync(_session.Id); })
            .Throws<IOException>();
        await Assert.That(_session.State).IsEqualTo(SessionState.Active);
    }

    [Test]
    public async Task RefreshSessionAsync_OnInactiveSession_DoesNothing()
    {
        await _manager.RefreshSessionAsync(_session.Id);

        await _writer.DidNotReceive().WriteProfileAsync(
            Arg.Any<string>(), Arg.Any<RoleCredentialSet>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await Assert.That(_session.State).IsEqualTo(SessionState.Inactive);
    }

    [Test]
    public async Task GetCachedCredentials_AfterStart_ReturnsWrittenCredentials()
    {
        await _manager.StartSessionAsync(_session.Id);

        await Assert.That(_manager.GetCachedCredentials(_session.Id)).IsEqualTo(Credentials);
    }

    [Test]
    public async Task GetCachedCredentials_ForUnstartedSession_ReturnsNull()
    {
        await Assert.That(_manager.GetCachedCredentials(_session.Id)).IsNull();
    }

    [Test]
    public async Task GetCachedCredentials_AfterStop_ReturnsNull()
    {
        await _manager.StartSessionAsync(_session.Id);
        await _manager.StopSessionAsync(_session.Id);

        await Assert.That(_manager.GetCachedCredentials(_session.Id)).IsNull();
    }

    [Test]
    public async Task GetCachedCredentials_AfterFailedRefresh_ReturnsNullOnError()
    {
        await _manager.StartSessionAsync(_session.Id);
        _portal.GetRoleCredentialsAsync(Arg.Any<SsoIntegration>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<RoleCredentialSet>(new SsoSessionExpiredException("expired")));

        await Assert.That(async () => { await _manager.RefreshSessionAsync(_session.Id); })
            .Throws<SsoSessionExpiredException>();
        await Assert.That(_manager.GetCachedCredentials(_session.Id)).IsNull();
    }

    [Test]
    public async Task SessionChanged_FiresOnStateTransitions()
    {
        var states = new List<SessionState>();
        _manager.SessionChanged += (_, e) => states.Add(e.Session.State);

        await _manager.StartSessionAsync(_session.Id);

        await Assert.That(states).IsEquivalentTo([SessionState.Starting, SessionState.Active]);
    }
}
