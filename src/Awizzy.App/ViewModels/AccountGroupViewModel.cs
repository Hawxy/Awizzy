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

    public int ActiveCount => Sessions.Count(s => s.IsActive);

    public string SummaryText
    {
        get
        {
            var roles = Sessions.Count == 1 ? "1 role" : $"{Sessions.Count} roles";
            return ActiveCount > 0 ? $"{roles} · {ActiveCount} active" : roles;
        }
    }

    public void RaiseChanged() => OnPropertyChanged(string.Empty);
}
