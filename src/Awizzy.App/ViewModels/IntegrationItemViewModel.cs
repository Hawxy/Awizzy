using Awizzy.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Awizzy.App.ViewModels;

public partial class IntegrationItemViewModel(SsoIntegration integration, TimeProvider time) : ObservableObject
{
    public SsoIntegration Integration { get; } = integration;

    public Guid Id => Integration.Id;
    public string Alias => Integration.Alias;
    public string PortalUrl => Integration.PortalUrl;
    public string Region => Integration.Region;
    public bool IsLoggedIn => Integration.IsLoggedIn(time);
    public string StatusText => $"{(IsLoggedIn ? "Logged in" : "Logged out")} {Region}";
    public bool CanLogin => !IsLoggedIn && !IsSyncing;

    public string LastSyncText
    {
        get
        {
            if (Integration.LastSyncedAt is not { } syncedAt)
                return "Never synced";
            var age = time.GetUtcNow() - syncedAt;
            return age switch
            {
                { TotalMinutes: < 1 } => "Synced just now",
                { TotalHours: < 1 } => $"Synced {(int)age.TotalMinutes}m ago",
                { TotalDays: < 1 } => $"Synced {(int)age.TotalHours}h ago",
                _ => $"Synced {(int)age.TotalDays}d ago",
            };
        }
    }

    [ObservableProperty]
    private bool _isSyncing;

    partial void OnIsSyncingChanged(bool value) => OnPropertyChanged(nameof(CanLogin));

    public void RaiseChanged() => OnPropertyChanged(string.Empty);
}
