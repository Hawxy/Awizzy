using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using Awizzy.Core.Services;
using Awizzy.Core.Tests.TestDoubles;
using Awizzy.Mcp;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol;
using NSubstitute;

namespace Awizzy.Core.Tests.Services;

public class McpToolsTests
{
    private readonly Workspace _workspace = new();
    private readonly ISessionManager _sessionManager = Substitute.For<ISessionManager>();
    private readonly IIntegrationService _integrationService = Substitute.For<IIntegrationService>();
    private readonly IWebConsoleUrlService _consoleUrlService = Substitute.For<IWebConsoleUrlService>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly McpChangeNotifier _notifier = new();
    private readonly McpTools _tools;

    public McpToolsTests()
    {
        var repository = Substitute.For<IWorkspaceRepository>();
        repository.Load().Returns(_workspace);
        _tools = new McpTools(
            new WorkspaceState(repository), _sessionManager, _integrationService,
            _consoleUrlService, _time, _notifier, new ImmediateMainThreadDispatcher());
    }

    private SsoIntegration AddIntegration(string alias, bool loggedIn = true)
    {
        var integration = new SsoIntegration
        {
            Alias = alias,
            PortalUrl = $"https://{alias}.awsapps.com/start",
            Region = "eu-west-1",
            AccessTokenExpiresAt = loggedIn ? _time.GetUtcNow().AddHours(1) : null,
        };
        _workspace.Integrations.Add(integration);
        return integration;
    }

    private AwsSession AddSession(SsoIntegration integration, string accountId, string accountName, string role)
    {
        var session = new AwsSession
        {
            IntegrationId = integration.Id,
            AccountId = accountId,
            AccountName = accountName,
            RoleName = role,
            Region = "eu-west-1",
            ProfileName = accountName.ToLowerInvariant(),
        };
        _workspace.Sessions.Add(session);
        return session;
    }

    [Test]
    public async Task ListIntegrations_ReportsLoginStateAndSessionCount()
    {
        var integration = AddIntegration("Acme");
        AddSession(integration, "111111111111", "prod", "Admin");
        AddIntegration("Other", loggedIn: false);

        var result = await _tools.ListIntegrations();

        await Assert.That(result).Count().IsEqualTo(2);
        await Assert.That(result[0].Alias).IsEqualTo("Acme");
        await Assert.That(result[0].LoggedIn).IsTrue();
        await Assert.That(result[0].SessionCount).IsEqualTo(1);
        await Assert.That(result[1].LoggedIn).IsFalse();
    }

    [Test]
    public async Task ListSessions_FiltersByIntegrationAlias()
    {
        var acme = AddIntegration("Acme");
        var other = AddIntegration("Other");
        AddSession(acme, "111111111111", "prod", "Admin");
        AddSession(other, "222222222222", "dev", "Admin");

        var result = await _tools.ListSessions("acme");

        await Assert.That(result).HasSingleItem();
        await Assert.That(result[0].AccountName).IsEqualTo("prod");
        await Assert.That(result[0].State).IsEqualTo("Inactive");
    }

    [Test]
    public async Task ListSessions_FlagsExcludedRoles()
    {
        var integration = AddIntegration("Acme");
        AddSession(integration, "111111111111", "prod", "Admin");
        AddSession(integration, "111111111111", "prod", "ReadOnly");
        _workspace.Settings.McpExcludedRoles.Add("111111111111/Admin");

        var result = await _tools.ListSessions();

        await Assert.That(result).Count().IsEqualTo(2);
        await Assert.That(result.Single(s => s.RoleName == "Admin").ControlDisabled).IsTrue();
        await Assert.That(result.Single(s => s.RoleName == "ReadOnly").ControlDisabled).IsFalse();
    }

    [Test]
    public async Task StartSession_ExcludedRole_RefusesWithClearError()
    {
        var integration = AddIntegration("Acme");
        AddSession(integration, "111111111111", "prod", "Admin");
        _workspace.Settings.McpExcludedRoles.Add("111111111111/Admin");

        await Assert.That(async () => { await _tools.StartSession("prod", "Admin", CancellationToken.None); })
            .Throws<McpException>().WithMessageContaining("excluded from MCP control");
        await _sessionManager.DidNotReceive().StartSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetConsoleUrl_ExcludedRole_Refuses()
    {
        var integration = AddIntegration("Acme");
        var session = AddSession(integration, "111111111111", "prod", "Admin");
        _sessionManager.GetCachedCredentials(session.Id)
            .Returns(new RoleCredentialSet("AKIA", "secret", "token", _time.GetUtcNow().AddHours(1)));
        _workspace.Settings.McpExcludedRoles.Add("111111111111/Admin");

        await Assert.That(async () => { await _tools.GetConsoleUrl("prod", "Admin", CancellationToken.None); })
            .Throws<McpException>().WithMessageContaining("excluded from MCP control");
    }

    [Test]
    public async Task StartSession_ResolvesByAccountName()
    {
        var integration = AddIntegration("Acme");
        var session = AddSession(integration, "111111111111", "prod", "Admin");

        await _tools.StartSession("PROD", "admin", CancellationToken.None);

        await _sessionManager.Received(1).StartSessionAsync(session.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StartSession_UnknownSession_Throws()
    {
        AddIntegration("Acme");

        await Assert.That(async () => { await _tools.StartSession("nope", "Admin", CancellationToken.None); })
            .Throws<McpException>();
    }

    [Test]
    public async Task StartSession_AmbiguousAccountName_Throws()
    {
        var acme = AddIntegration("Acme");
        var other = AddIntegration("Other");
        AddSession(acme, "111111111111", "prod", "Admin");
        AddSession(other, "222222222222", "prod", "Admin");

        await Assert.That(async () => { await _tools.StartSession("prod", "Admin", CancellationToken.None); })
            .Throws<McpException>();
    }

    [Test]
    public async Task StartSession_ManagerFailure_SurfacesMessage()
    {
        var integration = AddIntegration("Acme");
        AddSession(integration, "111111111111", "prod", "Admin");
        _sessionManager.StartSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        await Assert.That(async () => { await _tools.StartSession("prod", "Admin", CancellationToken.None); })
            .Throws<McpException>().WithMessageContaining("boom");
    }

    [Test]
    public async Task StopSession_ResolvesByAccountId()
    {
        var integration = AddIntegration("Acme");
        var session = AddSession(integration, "111111111111", "prod", "Admin");

        await _tools.StopSession("111111111111", "Admin", CancellationToken.None);

        await _sessionManager.Received(1).StopSessionAsync(session.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SyncIntegration_NotLoggedIn_Throws()
    {
        AddIntegration("Acme", loggedIn: false);

        await Assert.That(async () => { await _tools.SyncIntegration("Acme", CancellationToken.None); })
            .Throws<McpException>().WithMessageContaining("not logged in");
    }

    [Test]
    public async Task SyncIntegration_ReturnsCountsAndNotifies()
    {
        var integration = AddIntegration("Acme");
        _integrationService.SyncSessionsAsync(integration.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionSyncResult(2, 1, 5));
        var notified = false;
        _notifier.WorkspaceChanged += (_, _) => notified = true;

        var result = await _tools.SyncIntegration("acme", CancellationToken.None);

        await Assert.That(result.Added).IsEqualTo(2);
        await Assert.That(result.Removed).IsEqualTo(1);
        await Assert.That(result.Total).IsEqualTo(5);
        await Assert.That(notified).IsTrue();
    }

    [Test]
    public async Task GetConsoleUrl_InactiveSession_Throws()
    {
        var integration = AddIntegration("Acme");
        AddSession(integration, "111111111111", "prod", "Admin");
        _sessionManager.GetCachedCredentials(Arg.Any<Guid>()).Returns((RoleCredentialSet?)null);

        await Assert.That(async () => { await _tools.GetConsoleUrl("prod", "Admin", CancellationToken.None); })
            .Throws<McpException>().WithMessageContaining("not active");
    }

    [Test]
    public async Task GetConsoleUrl_ReturnsFederationUrl()
    {
        var integration = AddIntegration("Acme");
        var session = AddSession(integration, "111111111111", "prod", "Admin");
        var credentials = new RoleCredentialSet("AKIA", "secret", "token", _time.GetUtcNow().AddHours(1));
        _sessionManager.GetCachedCredentials(session.Id).Returns(credentials);
        _consoleUrlService.BuildConsoleUrlAsync(credentials, "eu-west-1", Arg.Any<CancellationToken>())
            .Returns("https://signin.aws.amazon.com/federation?Action=login&x=1");

        var url = await _tools.GetConsoleUrl("prod", "Admin", CancellationToken.None);

        await Assert.That(url).StartsWith("https://signin.aws.amazon.com/federation");
    }
}
