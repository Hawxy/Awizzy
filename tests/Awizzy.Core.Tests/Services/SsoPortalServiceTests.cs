using System.Text.Json;
using Amazon.SSO;
using Amazon.SSO.Model;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Exceptions;
using Awizzy.Core.Models;
using Awizzy.Core.Services;
using Awizzy.Core.Tests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Awizzy.Core.Tests.Services;

public class SsoPortalServiceTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
    private readonly InMemorySecureStore _secureStore = new();
    private readonly IAmazonSSO _sso = Substitute.For<IAmazonSSO>();
    private readonly SsoIntegration _integration = new()
    {
        Alias = "Acme",
        PortalUrl = "https://acme.awsapps.com/start",
        Region = "eu-west-1",
    };
    private readonly SsoPortalService _service;

    public SsoPortalServiceTests()
    {
        var factory = Substitute.For<ISsoClientFactory>();
        factory.CreateSsoClient(Arg.Any<string>()).Returns(_sso);
        _service = new SsoPortalService(factory, _secureStore, _time);
    }

    private void StoreValidToken() =>
        _secureStore.Values[SecureStoreKeys.SsoToken(_integration.Id)] =
            JsonSerializer.Serialize(new StoredSsoToken("valid-token", _time.GetUtcNow().AddHours(8)));

    [Test]
    public async Task ListAccountRolesAsync_PaginatesAccountsAndRoles()
    {
        StoreValidToken();
        _sso.ListAccountsAsync(Arg.Is<ListAccountsRequest>(r => r.NextToken == null), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ListAccountsResponse
            {
                AccountList = [new AccountInfo { AccountId = "111111111111", AccountName = "prod" }],
                NextToken = "page2",
            }));
        _sso.ListAccountsAsync(Arg.Is<ListAccountsRequest>(r => r.NextToken == "page2"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ListAccountsResponse
            {
                AccountList = [new AccountInfo { AccountId = "222222222222", AccountName = "dev" }],
            }));
        _sso.ListAccountRolesAsync(Arg.Is<ListAccountRolesRequest>(r => r.AccountId == "111111111111" && r.NextToken == null), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ListAccountRolesResponse
            {
                RoleList = [new RoleInfo { RoleName = "Admin" }],
                NextToken = "rolepage2",
            }));
        _sso.ListAccountRolesAsync(Arg.Is<ListAccountRolesRequest>(r => r.AccountId == "111111111111" && r.NextToken == "rolepage2"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ListAccountRolesResponse
            {
                RoleList = [new RoleInfo { RoleName = "ReadOnly" }],
            }));
        _sso.ListAccountRolesAsync(Arg.Is<ListAccountRolesRequest>(r => r.AccountId == "222222222222"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ListAccountRolesResponse
            {
                RoleList = [new RoleInfo { RoleName = "Admin" }],
            }));

        var roles = await _service.ListAccountRolesAsync(_integration);

        await Assert.That(roles).Count().IsEqualTo(3);
        await Assert.That(roles.Select(r => $"{r.AccountName}/{r.RoleName}"))
            .Contains("prod/Admin");
        await Assert.That(roles.Select(r => $"{r.AccountName}/{r.RoleName}"))
            .Contains("prod/ReadOnly");
        await Assert.That(roles.Select(r => $"{r.AccountName}/{r.RoleName}"))
            .Contains("dev/Admin");
    }

    [Test]
    public async Task ListAccountRolesAsync_WithNoStoredToken_ThrowsSessionExpired()
    {
        await Assert.That(async () => { await _service.ListAccountRolesAsync(_integration); })
            .Throws<SsoSessionExpiredException>();
    }

    [Test]
    public async Task ListAccountRolesAsync_WithExpiredToken_ThrowsSessionExpired()
    {
        _secureStore.Values[SecureStoreKeys.SsoToken(_integration.Id)] =
            JsonSerializer.Serialize(new StoredSsoToken("stale", _time.GetUtcNow().AddHours(-1)));

        await Assert.That(async () => { await _service.ListAccountRolesAsync(_integration); })
            .Throws<SsoSessionExpiredException>();
    }

    [Test]
    public async Task ListAccountRolesAsync_WhenServerRejectsToken_ThrowsSessionExpired()
    {
        StoreValidToken();
        _sso.ListAccountsAsync(Arg.Any<ListAccountsRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<ListAccountsResponse>(new UnauthorizedException("revoked")));

        await Assert.That(async () => { await _service.ListAccountRolesAsync(_integration); })
            .Throws<SsoSessionExpiredException>();
    }

    [Test]
    public async Task GetRoleCredentialsAsync_MapsCredentialsAndExpiration()
    {
        StoreValidToken();
        var expiration = _time.GetUtcNow().AddHours(1);
        _sso.GetRoleCredentialsAsync(Arg.Any<GetRoleCredentialsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GetRoleCredentialsResponse
            {
                RoleCredentials = new RoleCredentials
                {
                    AccessKeyId = "AKIA123",
                    SecretAccessKey = "secret",
                    SessionToken = "token",
                    Expiration = expiration.ToUnixTimeMilliseconds(),
                },
            }));

        var credentials = await _service.GetRoleCredentialsAsync(_integration, "111111111111", "Admin");

        await Assert.That(credentials.AccessKeyId).IsEqualTo("AKIA123");
        await Assert.That(credentials.Expiration).IsEqualTo(expiration);
    }
}
