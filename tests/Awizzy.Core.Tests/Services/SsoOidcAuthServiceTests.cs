using System.Text.Json;
using Amazon.SSOOIDC;
using Amazon.SSOOIDC.Model;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Exceptions;
using Awizzy.Core.Models;
using Awizzy.Core.Services;
using Awizzy.Core.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Awizzy.Core.Tests.Services;

public class SsoOidcAuthServiceTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
    private readonly InMemorySecureStore _secureStore = new();
    private readonly IAmazonSSOOIDC _oidc = Substitute.For<IAmazonSSOOIDC>();
    private readonly Workspace _workspace = new();
    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly SsoIntegration _integration = new()
    {
        Alias = "Acme",
        PortalUrl = "https://acme.awsapps.com/start",
        Region = "eu-west-1",
    };
    private readonly SsoOidcAuthService _service;

    public SsoOidcAuthServiceTests()
    {
        var factory = Substitute.For<ISsoClientFactory>();
        factory.CreateOidcClient(Arg.Any<string>()).Returns(_oidc);
        factory.CreateSsoClient(Arg.Any<string>()).Returns(Substitute.For<Amazon.SSO.IAmazonSSO>());
        _repository.Load().Returns(_workspace);
        _workspace.Integrations.Add(_integration);
        _service = new SsoOidcAuthService(
            factory, _secureStore, new WorkspaceState(_repository), _time,
            NullLogger<SsoOidcAuthService>.Instance);

        _oidc.RegisterClientAsync(Arg.Any<RegisterClientRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RegisterClientResponse
            {
                ClientId = "client-1",
                ClientSecret = "secret-1",
                ClientSecretExpiresAt = _time.GetUtcNow().AddDays(90).ToUnixTimeSeconds(),
            }));
        _oidc.StartDeviceAuthorizationAsync(Arg.Any<StartDeviceAuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new StartDeviceAuthorizationResponse
            {
                DeviceCode = "device-code",
                UserCode = "ABCD-1234",
                VerificationUri = "https://device.sso.eu-west-1.amazonaws.com",
                VerificationUriComplete = "https://device.sso.eu-west-1.amazonaws.com?user_code=ABCD-1234",
                ExpiresIn = 600,
                Interval = 5,
            }));
    }

    private static CreateTokenResponse TokenResponse => new()
    {
        AccessToken = "the-access-token",
        ExpiresIn = 28800,
    };

    /// <summary>Advances fake time until the task completes, yielding so continuations can run.</summary>
    private async Task PumpUntilComplete(Task task, int maxSteps = 500)
    {
        for (var i = 0; i < maxSteps && !task.IsCompleted; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }
        await task;
    }

    [Test]
    public async Task BeginLoginAsync_RegistersClientAndStartsDeviceAuthorization()
    {
        var authorization = await _service.BeginLoginAsync(_integration);

        await Assert.That(authorization.UserCode).IsEqualTo("ABCD-1234");
        await Assert.That(authorization.VerificationUriComplete).Contains("user_code=ABCD-1234");
        await Assert.That(authorization.Interval).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(authorization.ExpiresAt).IsEqualTo(_time.GetUtcNow().AddSeconds(600));
        await Assert.That(_secureStore.Values.Keys).Contains(SecureStoreKeys.ClientRegistration("eu-west-1"));
    }

    [Test]
    public async Task BeginLoginAsync_ReusesCachedClientRegistration()
    {
        await _service.BeginLoginAsync(_integration);
        await _service.BeginLoginAsync(_integration);

        await _oidc.Received(1).RegisterClientAsync(Arg.Any<RegisterClientRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BeginLoginAsync_ReregistersWhenCachedRegistrationNearsExpiry()
    {
        await _service.BeginLoginAsync(_integration);
        _time.Advance(TimeSpan.FromDays(90));

        await _service.BeginLoginAsync(_integration);

        await _oidc.Received(2).RegisterClientAsync(Arg.Any<RegisterClientRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompleteLoginAsync_AfterPendingPolls_StoresTokenAndUpdatesIntegration()
    {
        _oidc.CreateTokenAsync(Arg.Any<CreateTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<CreateTokenResponse>(new AuthorizationPendingException("pending")),
                _ => Task.FromException<CreateTokenResponse>(new AuthorizationPendingException("pending")),
                _ => Task.FromResult(TokenResponse));
        var authorization = await _service.BeginLoginAsync(_integration);

        await PumpUntilComplete(_service.CompleteLoginAsync(_integration, authorization));

        var stored = JsonSerializer.Deserialize<StoredSsoToken>(
            _secureStore.Values[SecureStoreKeys.SsoToken(_integration.Id)]);
        await Assert.That(stored!.AccessToken).IsEqualTo("the-access-token");
        await Assert.That(_integration.AccessTokenExpiresAt).IsNotNull();
        await _repository.Received().SaveAsync(_workspace, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompleteLoginAsync_WhenAuthorizationExpires_ThrowsTimeout()
    {
        _oidc.CreateTokenAsync(Arg.Any<CreateTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<CreateTokenResponse>(new AuthorizationPendingException("pending")));
        var authorization = await _service.BeginLoginAsync(_integration);

        var task = _service.CompleteLoginAsync(_integration, authorization);

        await Assert.That(() => PumpUntilComplete(task, maxSteps: 700))
            .Throws<SsoLoginTimeoutException>();
    }

    [Test]
    public async Task CompleteLoginAsync_WhenUserDeclines_ThrowsDenied()
    {
        _oidc.CreateTokenAsync(Arg.Any<CreateTokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<CreateTokenResponse>(new AccessDeniedException("declined")));
        var authorization = await _service.BeginLoginAsync(_integration);

        await Assert.That(() => _service.CompleteLoginAsync(_integration, authorization))
            .Throws<SsoLoginDeniedException>();
    }

    [Test]
    public async Task LogoutAsync_RemovesTokenAndClearsExpiry()
    {
        _secureStore.Values[SecureStoreKeys.SsoToken(_integration.Id)] =
            JsonSerializer.Serialize(new StoredSsoToken("token", _time.GetUtcNow().AddHours(8)));
        _integration.AccessTokenExpiresAt = _time.GetUtcNow().AddHours(8);

        await _service.LogoutAsync(_integration);

        await Assert.That(_secureStore.Values.ContainsKey(SecureStoreKeys.SsoToken(_integration.Id))).IsFalse();
        await Assert.That(_integration.AccessTokenExpiresAt).IsNull();
    }
}
