namespace Awizzy.Core.Models;

public class AppSettings
{
    /// <summary>How long before credential expiry an active session is refreshed.</summary>
    public TimeSpan RefreshMargin { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Overrides the AWS credentials file location. Null means the standard resolution
    /// (AWS_SHARED_CREDENTIALS_FILE, then ~/.aws/credentials).</summary>
    public string? CredentialsFilePath { get; set; }

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>Whether the local MCP server is exposed on the loopback interface.</summary>
    public bool McpServerEnabled { get; set; }

    public int McpServerPort { get; set; } = 52100;

    /// <summary>Sessions (as AccountId/RoleName keys) excluded from MCP control: still listed,
    /// but MCP tools refuse to start or stop them or issue console URLs.</summary>
    public List<string> McpExcludedRoles { get; set; } = [];

    /// <summary>Sessions (as AccountId/RoleName keys) the user starred in the session table.</summary>
    public List<string> FavoriteRoles { get; set; } = [];
}
