using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using Awizzy.Core.Services;
using Awizzy.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Awizzy.Core.Tests.Services;

public class CredentialRefreshServiceTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
    private readonly Workspace _workspace = new();
    private readonly ISessionManager _sessionManager = Substitute.For<ISessionManager>();
    private readonly AwsSession _session;
    private readonly CredentialRefreshService _service;

    public CredentialRefreshServiceTests()
    {
        _session = new AwsSession
        {
            IntegrationId = Guid.NewGuid(),
            AccountId = "111111111111",
            AccountName = "prod",
            RoleName = "Admin",
            Region = "eu-west-1",
            ProfileName = "prod",
        };
        _workspace.Sessions.Add(_session);
        var repository = Substitute.For<IWorkspaceRepository>();
        repository.Load().Returns(_workspace);
        _service = new CredentialRefreshService(
            new WorkspaceState(repository), _sessionManager, new ImmediateMainThreadDispatcher(), _time,
            NullLogger<CredentialRefreshService>.Instance);
    }

    [After(Test)]
    public void Cleanup() => _service.Dispose();

    [Test]
    public async Task RefreshesActiveSessionInsideMargin()
    {
        _session.State = SessionState.Active;
        _session.CredentialsExpireAt = _time.GetUtcNow().AddMinutes(5);

        await _service.RunDueRefreshesAsync();

        await _sessionManager.Received(1).RefreshSessionAsync(_session.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LeavesSessionAloneWhenExpiryIsFarAway()
    {
        _session.State = SessionState.Active;
        _session.CredentialsExpireAt = _time.GetUtcNow().AddHours(4);

        await _service.RunDueRefreshesAsync();

        await _sessionManager.DidNotReceive().RefreshSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BecomesDueAsTimeAdvances()
    {
        _session.State = SessionState.Active;
        _session.CredentialsExpireAt = _time.GetUtcNow().AddHours(1);

        await _service.RunDueRefreshesAsync();
        await _sessionManager.DidNotReceive().RefreshSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromMinutes(55));
        await _service.RunDueRefreshesAsync();

        await _sessionManager.Received(1).RefreshSessionAsync(_session.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task IgnoresInactiveSessions()
    {
        _session.State = SessionState.Inactive;
        _session.CredentialsExpireAt = _time.GetUtcNow().AddMinutes(1);

        await _service.RunDueRefreshesAsync();

        await _sessionManager.DidNotReceive().RefreshSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshesSessionWhoseCredentialsAlreadyExpired()
    {
        _session.State = SessionState.Active;
        _session.CredentialsExpireAt = _time.GetUtcNow().AddMinutes(-5);

        await _service.RunDueRefreshesAsync();

        await _sessionManager.Received(1).RefreshSessionAsync(_session.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SurvivesRefreshFailureAndRetriesNextPass()
    {
        _session.State = SessionState.Active;
        _session.CredentialsExpireAt = _time.GetUtcNow().AddMinutes(5);
        _sessionManager.RefreshSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException(new IOException("file locked")),
                _ => Task.CompletedTask);

        await _service.RunDueRefreshesAsync();
        await _service.RunDueRefreshesAsync();

        await _sessionManager.Received(2).RefreshSessionAsync(_session.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshesOnlyDueSessionsWhenSeveralAreActive()
    {
        _session.State = SessionState.Active;
        _session.CredentialsExpireAt = _time.GetUtcNow().AddMinutes(5);
        var farAway = new AwsSession
        {
            IntegrationId = Guid.NewGuid(),
            AccountId = "222222222222",
            AccountName = "dev",
            RoleName = "Admin",
            Region = "eu-west-1",
            ProfileName = "prod",
            State = SessionState.Active,
            CredentialsExpireAt = _time.GetUtcNow().AddHours(6),
        };
        _workspace.Sessions.Add(farAway);

        await _service.RunDueRefreshesAsync();

        await _sessionManager.Received(1).RefreshSessionAsync(_session.Id, Arg.Any<CancellationToken>());
        await _sessionManager.DidNotReceive().RefreshSessionAsync(farAway.Id, Arg.Any<CancellationToken>());
    }
}
