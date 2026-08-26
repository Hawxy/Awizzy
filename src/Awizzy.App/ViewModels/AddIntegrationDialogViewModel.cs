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

    /// <summary>Throws when the input is invalid; keeps the dialog open so nothing typed is lost.</summary>
    public required Action<IntegrationInput> Validator { get; init; }

    public IntegrationInput ToValidatedInput()
    {
        var input = new IntegrationInput(Alias, PortalUrl, Region);
        Validator(input);
        return input;
    }
}
