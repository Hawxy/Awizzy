using Awizzy.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Awizzy.App.ViewModels;

public class SessionItemViewModel(AwsSession session, TimeProvider time) : ObservableObject
{
    public AwsSession Session { get; } = session;

    public Guid Id => Session.Id;
    public string DisplayName => Session.DisplayName;
    public string AccountId => Session.AccountId;
    public string RoleName => Session.RoleName;
    public string Region => Session.Region;
    public string ProfileName => Session.ProfileName;

    public SessionState State => Session.State;
    public bool IsActive => State is SessionState.Active or SessionState.Refreshing;
    public bool IsBusy => State is SessionState.Starting or SessionState.Refreshing;
    public bool HasError => State is SessionState.Error;
    public string? ErrorMessage => Session.ErrorMessage;

    public string StateText => State switch
    {
        SessionState.Inactive => "Inactive",
        SessionState.Starting => "Starting…",
        SessionState.Active => "Active",
        SessionState.Refreshing => "Refreshing…",
        SessionState.Error => "Error",
        _ => State.ToString(),
    };

    public string ExpiryText
    {
        get
        {
            if (Session.CredentialsExpireAt is not { } expiry || !IsActive)
                return string.Empty;
            var remaining = expiry - time.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                return "expired";
            return remaining.TotalHours >= 1
                ? $"expires in {(int)remaining.TotalHours}h {remaining.Minutes:D2}m"
                : $"expires in {remaining.Minutes}m";
        }
    }

    public bool HasExpiry => ExpiryText.Length > 0;

    /// <summary>Re-evaluates every binding after the underlying session changed.</summary>
    public void RaiseChanged() => OnPropertyChanged(string.Empty);
}
