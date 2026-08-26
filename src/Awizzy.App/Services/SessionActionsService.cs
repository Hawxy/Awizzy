using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;

namespace Awizzy.App.Services;

/// <summary>Per-session actions behind the session table's copy and console buttons.
/// Each method returns the status-bar message for the completed action.</summary>
public class SessionActionsService(
    IClipboardService clipboard,
    ISessionManager sessionManager,
    IWebConsoleUrlService consoleUrlService,
    IBrowserLauncher browserLauncher)
{
    public Task<string> CopyProfileNameAsync(AwsSession session) =>
        CopyAsync(session.ProfileName, "profile name");

    public Task<string> CopyAccountIdAsync(AwsSession session) =>
        CopyAsync(session.AccountId, "account id");

    public Task<string> CopyCredentialsPowerShellAsync(AwsSession session)
    {
        var c = GetCredentialsOrThrow(session);
        return CopyAsync(
            $"""
             $env:AWS_ACCESS_KEY_ID = "{c.AccessKeyId}"
             $env:AWS_SECRET_ACCESS_KEY = "{c.SecretAccessKey}"
             $env:AWS_SESSION_TOKEN = "{c.SessionToken}"
             $env:AWS_DEFAULT_REGION = "{session.Region}"
             """,
            "credentials (PowerShell)");
    }

    public Task<string> CopyCredentialsBashAsync(AwsSession session)
    {
        var c = GetCredentialsOrThrow(session);
        return CopyAsync(
            $"""
             export AWS_ACCESS_KEY_ID={c.AccessKeyId}
             export AWS_SECRET_ACCESS_KEY={c.SecretAccessKey}
             export AWS_SESSION_TOKEN={c.SessionToken}
             export AWS_DEFAULT_REGION={session.Region}
             """,
            "credentials (bash)");
    }

    public Task<string> CopyCredentialsProfileAsync(AwsSession session)
    {
        var c = GetCredentialsOrThrow(session);
        return CopyAsync(
            $"""
             [{session.ProfileName}]
             aws_access_key_id = {c.AccessKeyId}
             aws_secret_access_key = {c.SecretAccessKey}
             aws_session_token = {c.SessionToken}
             region = {session.Region}
             """,
            "credentials (profile block)");
    }

    public async Task<string> OpenConsoleAsync(AwsSession session)
    {
        var credentials = GetCredentialsOrThrow(session);
        var url = await consoleUrlService.BuildConsoleUrlAsync(credentials, session.Region);
        browserLauncher.Open(url);
        return $"Opened AWS console for {session.DisplayName}.";
    }

    private RoleCredentialSet GetCredentialsOrThrow(AwsSession session) =>
        sessionManager.GetCachedCredentials(session.Id)
        ?? throw new InvalidOperationException("No credentials for this session; start it first.");

    private async Task<string> CopyAsync(string text, string what)
    {
        await clipboard.SetTextAsync(text);
        return $"Copied {what} to clipboard.";
    }
}
