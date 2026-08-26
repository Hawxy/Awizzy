namespace Awizzy.Mcp;

/// <summary>Validates MCP request headers. The loopback binding limits callers to this machine,
/// but a browser can still be tricked into reaching it: DNS rebinding sends a foreign Host header,
/// and cross-site requests send a foreign Origin. Both are rejected here.</summary>
public static class McpRequestGuard
{
    /// <param name="host">Host header value without the port (e.g. Kestrel's HostString.Host).</param>
    /// <param name="origin">Raw Origin header value, or null when the header is absent.</param>
    public static bool IsAllowed(string? host, string? origin) =>
        IsLocalHostName(host) && (string.IsNullOrEmpty(origin) || IsLocalOrigin(origin));

    private static bool IsLocalHostName(string? host) =>
        host is not null
        && (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host is "127.0.0.1" or "::1" or "[::1]");

    private static bool IsLocalOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && IsLocalHostName(uri.IdnHost);
}
