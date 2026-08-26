using System.Net;
using System.Text.Json;
using Awizzy.Core.Models;
using Awizzy.Core.Services;

namespace Awizzy.Core.Tests.Services;

public class WebConsoleUrlServiceTests
{
    private static readonly RoleCredentialSet Credentials = new(
        "AKIA123", "secret/key+x", "token==",
        new DateTimeOffset(2026, 8, 24, 13, 0, 0, TimeSpan.Zero));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            LastRequestBody = request.Content is { } content ? await content.ReadAsStringAsync(ct) : null;
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Test]
    public async Task BuildConsoleUrlAsync_PostsCredentialsAsSessionJson()
    {
        var handler = new StubHandler(_ => Json("""{"SigninToken":"TOK"}"""));
        var service = new WebConsoleUrlService(new HttpClient(handler));

        await service.BuildConsoleUrlAsync(Credentials, "eu-west-1");

        // Credentials travel in the POST body, never the URL, so they stay out of request logs.
        await Assert.That(handler.LastMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.LastRequestUri!.Query).IsEqualTo(string.Empty);
        var body = System.Web.HttpUtility.ParseQueryString(handler.LastRequestBody!);
        await Assert.That(body["Action"]).IsEqualTo("getSigninToken");
        using var doc = JsonDocument.Parse(body["Session"]!);
        await Assert.That(doc.RootElement.GetProperty("sessionId").GetString()).IsEqualTo("AKIA123");
        await Assert.That(doc.RootElement.GetProperty("sessionKey").GetString()).IsEqualTo("secret/key+x");
        await Assert.That(doc.RootElement.GetProperty("sessionToken").GetString()).IsEqualTo("token==");
    }

    [Test]
    public async Task BuildConsoleUrlAsync_ReturnsLoginUrlWithTokenAndRegionDestination()
    {
        var handler = new StubHandler(_ => Json("""{"SigninToken":"TOK/abc+=="}"""));
        var service = new WebConsoleUrlService(new HttpClient(handler));

        var url = await service.BuildConsoleUrlAsync(Credentials, "ap-southeast-2");

        await Assert.That(url).StartsWith("https://signin.aws.amazon.com/federation?Action=login");
        await Assert.That(url).Contains("SigninToken=" + Uri.EscapeDataString("TOK/abc+=="));
        var destination = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["Destination"];
        await Assert.That(destination).IsEqualTo("https://ap-southeast-2.console.aws.amazon.com/console/home?region=ap-southeast-2");
    }

    [Test]
    public async Task BuildConsoleUrlAsync_OnErrorStatus_ThrowsWithExpiryHint()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var service = new WebConsoleUrlService(new HttpClient(handler));

        await Assert.That(async () => { await service.BuildConsoleUrlAsync(Credentials, "eu-west-1"); })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("400");
    }

    [Test]
    public async Task BuildConsoleUrlAsync_OnMalformedResponse_Throws()
    {
        var handler = new StubHandler(_ => Json("not json"));
        var service = new WebConsoleUrlService(new HttpClient(handler));

        await Assert.That(async () => { await service.BuildConsoleUrlAsync(Credentials, "eu-west-1"); })
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BuildConsoleUrlAsync_OnMissingToken_Throws()
    {
        var handler = new StubHandler(_ => Json("""{"Other":"x"}"""));
        var service = new WebConsoleUrlService(new HttpClient(handler));

        await Assert.That(async () => { await service.BuildConsoleUrlAsync(Credentials, "eu-west-1"); })
            .Throws<InvalidOperationException>();
    }
}
