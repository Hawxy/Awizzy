using Awizzy.App.ViewModels;
using Awizzy.Core.Models;

namespace Awizzy.App.Services;

public interface IDialogService
{
    /// <summary>Returns the entered values, or null if the user cancelled.</summary>
    Task<IntegrationInput?> ShowAddIntegrationAsync(SsoIntegration? existing = null);

    /// <summary>Runs the full device-flow login with progress UI. Returns true on success.</summary>
    Task<bool> ShowLoginAsync(SsoIntegration integration);

    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Returns the chosen profile name and region, or null if cancelled.</summary>
    Task<SessionOptionsResult?> ShowSessionOptionsAsync(AwsSession session);

    /// <summary>Returns the edited settings, or null if cancelled.</summary>
    Task<SettingsResult?> ShowSettingsAsync();
}
