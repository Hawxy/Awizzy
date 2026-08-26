using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Awizzy.Core.Services;

/// <summary>Refreshes active sessions before their credentials expire.</summary>
public class CredentialRefreshService(
    WorkspaceState state,
    ISessionManager sessionManager,
    IMainThreadDispatcher dispatcher,
    TimeProvider time,
    ILogger<CredentialRefreshService> logger) : BackgroundService
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunDueRefreshesAsync(stoppingToken);
    }

    /// <summary>One refresh pass. Public so tests can drive it without the timer loop.
    /// Workspace state is single-threaded, so the pass runs on the main thread.</summary>
    public Task RunDueRefreshesAsync(CancellationToken ct = default) =>
        dispatcher.InvokeAsync(async () =>
        {
            var margin = state.Workspace.Settings.RefreshMargin;
            var now = time.GetUtcNow();
            var dueSessions = state.Workspace.Sessions
                .Where(s => s.State == SessionState.Active
                            && s.CredentialsExpireAt is { } expiry
                            && expiry - now < margin)
                .ToList();

            foreach (var session in dueSessions)
            {
                try
                {
                    await sessionManager.RefreshSessionAsync(session.Id, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // The session manager has already set the session state; just record it.
                    logger.LogWarning(ex, "Background refresh of {Session} failed.", session.DisplayName);
                }
            }
        });
}
