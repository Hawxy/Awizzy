using Awizzy.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Awizzy.App.ViewModels;

public record SessionOptionsResult(string ProfileName, string Region, bool ApplyProfileToAccount);

public partial class SessionOptionsDialogViewModel : ObservableObject
{
    public required string SessionName { get; init; }
    public required string AccountName { get; init; }

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private bool _applyProfileToAccount = true;

    [ObservableProperty]
    private string _region = "us-east-1";

    public IReadOnlyList<string> Regions => AwsRegions.All;

    public string ApplyToAccountLabel => $"Use this profile for every role in {AccountName}";

    public SessionOptionsResult ToResult() =>
        new(ProfileNames.Validate(ProfileName), Region, ApplyProfileToAccount);
}
