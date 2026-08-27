using Avalonia.Controls.Notifications;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;
using Velopack;
using Velopack.Sources;

namespace Awizzy.App.Services;

/// <summary>Checks the GitHub releases feed for a newer Velopack package and offers to
/// install it via a toast. No-ops when the app isn't running from a Velopack install
/// (local builds, dotnet run).</summary>
public class UpdateService(ISukiToastManager toastManager, ILogger<UpdateService> logger)
{
    private const string RepoUrl = "https://github.com/Hawxy/Awizzy";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private string? _offeredVersion;

    /// <summary>Checks once at startup, then hourly; the app lives in the tray for days.
    /// Started from the UI thread so toast continuations stay on it.</summary>
    public async Task RunPeriodicChecksAsync()
    {
        if (!await CheckForUpdateAsync())
            return;

        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync())
            await CheckForUpdateAsync();
    }

    /// <summary>Returns false when the app isn't running from a Velopack install
    /// (local builds, dotnet run), meaning further checks are pointless.</summary>
    private async Task<bool> CheckForUpdateAsync()
    {
        UpdateManager manager;
        UpdateInfo? update;
        try
        {
            manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
            if (!manager.IsInstalled)
                return false;

            update = await manager.CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            // A failed background check is logged, never surfaced; retry on the next tick.
            logger.LogWarning(ex, "Update check failed.");
            return true;
        }

        if (update is null)
            return true;

        // Offer each version once; a dismissed toast should not reappear every hour.
        var version = update.TargetFullRelease.Version.ToString();
        if (version == _offeredVersion)
            return true;
        _offeredVersion = version;

        toastManager.CreateToast()
            .WithTitle("Update available")
            .WithContent($"Awizzy {update.TargetFullRelease.Version} is ready to install.")
            .OfType(NotificationType.Information)
            .WithActionButton("Install and restart", toast => _ = DownloadAndApplyAsync(manager, update), dismissOnClick: true)
            .Dismiss().ByClicking()
            .Queue();
        return true;
    }

    private async Task DownloadAndApplyAsync(UpdateManager manager, UpdateInfo update)
    {
        var downloading = toastManager.CreateToast()
            .WithTitle("Awizzy update")
            .WithContent($"Downloading {update.TargetFullRelease.Version}…")
            .WithLoadingState(true)
            .Queue();
        try
        {
            await manager.DownloadUpdatesAsync(update);
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Downloading or applying the update failed.");
            toastManager.CreateToast()
                .WithTitle("Update failed")
                .WithContent(ex.Message)
                .OfType(NotificationType.Error)
                .Dismiss().After(TimeSpan.FromSeconds(8))
                .Dismiss().ByClicking()
                .Queue();
        }
        finally
        {
            toastManager.Dismiss(downloading, SukiToastDismissSource.Code);
        }
    }
}
