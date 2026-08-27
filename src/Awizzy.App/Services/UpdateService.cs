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

    public async Task CheckForUpdateAsync()
    {
        UpdateManager manager;
        UpdateInfo? update;
        try
        {
            manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
            if (!manager.IsInstalled)
                return;

            update = await manager.CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            // A failed background check is logged, never surfaced.
            logger.LogWarning(ex, "Update check failed.");
            return;
        }

        if (update is null)
            return;

        toastManager.CreateToast()
            .WithTitle("Update available")
            .WithContent($"Awizzy {update.TargetFullRelease.Version} is ready to install.")
            .OfType(NotificationType.Information)
            .WithActionButton("Install and restart", toast => _ = DownloadAndApplyAsync(manager, update), dismissOnClick: true)
            .Dismiss().ByClicking()
            .Queue();
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
