using System.Text.Json;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;

namespace Awizzy.Core.Services;

public class WebConsoleUrlService(HttpClient httpClient) : IWebConsoleUrlService
{
    private const string FederationEndpoint = "https://signin.aws.amazon.com/federation";

    public async Task<string> BuildConsoleUrlAsync(RoleCredentialSet credentials, string region, CancellationToken ct = default)
    {
        var sessionJson = JsonSerializer.Serialize(
            new FederationSession(credentials.AccessKeyId, credentials.SecretAccessKey, credentials.SessionToken),
            CoreJsonContext.Default.FederationSession);

        // POST keeps the secret credentials out of URLs, which proxies and HTTP diagnostics log.
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("Action", "getSigninToken"),
            new KeyValuePair<string, string>("Session", sessionJson),
        ]);
        using var response = await httpClient.PostAsync(FederationEndpoint, content, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"AWS federation endpoint returned {(int)response.StatusCode}; the session credentials may have expired.");

        var payload = await response.Content.ReadAsStringAsync(ct);
        string? signinToken;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            signinToken = doc.RootElement.GetProperty("SigninToken").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException("AWS federation endpoint returned an unexpected response.", ex);
        }

        if (string.IsNullOrEmpty(signinToken))
            throw new InvalidOperationException("AWS federation endpoint returned an empty signin token.");

        var destination = $"https://{region}.console.aws.amazon.com/console/home?region={region}";
        return $"{FederationEndpoint}?Action=login"
               + "&Issuer=awizzy"
               + $"&Destination={Uri.EscapeDataString(destination)}"
               + $"&SigninToken={Uri.EscapeDataString(signinToken)}";
    }
}
