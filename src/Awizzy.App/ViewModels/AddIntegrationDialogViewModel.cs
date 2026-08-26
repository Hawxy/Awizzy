using Awizzy.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Awizzy.App.ViewModels;

public record IntegrationInput(string Alias, string PortalUrl, string Region);

public partial class AddIntegrationDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _alias = string.Empty;

    [ObservableProperty]
    private string _portalUrl = string.Empty;

    [ObservableProperty]
    private string _region = "us-east-1";

    public IReadOnlyList<string> Regions => AwsRegions.All;

    public string Title { get; init; } = "Add integration";

    public IntegrationInput ToInput() => new(Alias, PortalUrl, Region);
}
