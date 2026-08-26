using Awizzy.Core.Models;

namespace Awizzy.Core.Abstractions;

public class SessionChangedEventArgs(AwsSession session) : EventArgs
{
    public AwsSession Session { get; } = session;
}

public interface ISessionManager
{
    /// <summary>Fetches role credentials and writes them to the session's profile.
    /// If another session is active on the same profile, it is stopped first.</summary>
    Task StartSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Removes the profile section from the credentials file and marks the session inactive.</summary>
    Task StopSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Re-fetches credentials for an active session. Used by the background refresher.</summary>
    Task RefreshSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Returns the credentials most recently written for the session,
    /// or null if the session is not running.</summary>
    RoleCredentialSet? GetCachedCredentials(Guid sessionId);

    event EventHandler<SessionChangedEventArgs>? SessionChanged;
}
