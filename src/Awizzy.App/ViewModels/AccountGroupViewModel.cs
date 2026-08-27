using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Awizzy.App.ViewModels;

/// <summary>One account with its role sessions grouped underneath.</summary>
public partial class AccountGroupViewModel(string accountName, string accountId) : ObservableObject
{
    public string AccountName { get; } = accountName;
    public string AccountId { get; } = accountId;
    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Account-wide counts, set during rebuild. Not derived from Sessions:
    /// running sessions render in the pinned box, not under the group.</summary>
    public int TotalRoles { get; set; }

    public int ActiveCount { get; set; }

    public string SummaryText
    {
        get
        {
            var roles = TotalRoles == 1 ? "1 role" : $"{TotalRoles} roles";
            return ActiveCount > 0 ? $"{roles} · {ActiveCount} active" : roles;
        }
    }

    public void RaiseChanged() => OnPropertyChanged(string.Empty);
}
