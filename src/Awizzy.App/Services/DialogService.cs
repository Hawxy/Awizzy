using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Awizzy.App.ViewModels;
using Awizzy.App.Views;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using Awizzy.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Dialogs;

namespace Awizzy.App.Services;

public class DialogService(IServiceProvider services) : IDialogService
{
    private static Window Owner =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
        ?? throw new InvalidOperationException("Main window is not available.");

    public async Task<IntegrationInput?> ShowAddIntegrationAsync(SsoIntegration? existing = null)
    {
        var integrationService = services.GetRequiredService<IIntegrationService>();
        var viewModel = new AddIntegrationDialogViewModel
        {
            Title = existing is null ? "Add integration" : "Edit integration",
            Alias = existing?.Alias ?? string.Empty,
            PortalUrl = existing?.PortalUrl ?? string.Empty,
            Region = existing?.Region ?? "us-east-1",
            Validator = input => integrationService.ValidateInput(
                input.Alias, input.PortalUrl, input.Region, existing?.Id),
        };
        var dialog = new AddIntegrationDialog { DataContext = viewModel };
        return await dialog.ShowDialog<IntegrationInput?>(Owner);
    }

    public async Task<bool> ShowLoginAsync(SsoIntegration integration)
    {
        var viewModel = new LoginDialogViewModel(
            services.GetRequiredService<ISsoOidcAuthService>(),
            services.GetRequiredService<IBrowserLauncher>());
        var dialog = new LoginProgressDialog { DataContext = viewModel };
        viewModel.Completed += (_, success) => dialog.Close(success);
        dialog.Opened += async (_, _) => await viewModel.RunAsync(integration);
        return await dialog.ShowDialog<bool>(Owner);
    }

    public Task<bool> ConfirmAsync(string title, string message) =>
        services.GetRequiredService<ISukiDialogManager>()
            .CreateDialog()
            .WithTitle(title)
            .WithContent(message)
            .WithYesNoResult("OK", "Cancel")
            .TryShowAsync();

    public async Task<SettingsResult?> ShowSettingsAsync()
    {
        var workspace = services.GetRequiredService<WorkspaceState>().Workspace;
        var settings = workspace.Settings;
        var resolver = services.GetRequiredService<Awizzy.Core.AwsFiles.CredentialsFilePathResolver>();
        var excluded = settings.McpExcludedRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var listedKeys = workspace.Sessions.Select(s => s.McpRoleKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var viewModel = new SettingsDialogViewModel
        {
            RefreshMarginMinutes = (decimal)settings.RefreshMargin.TotalMinutes,
            CredentialsFilePath = settings.CredentialsFilePath ?? string.Empty,
            Theme = settings.Theme,
            McpServerEnabled = settings.McpServerEnabled,
            McpServerPort = settings.McpServerPort,
            DefaultCredentialsPath = resolver.GetDefaultPath(),
            McpRoles = workspace.Sessions
                .OrderBy(s => s.AccountName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.RoleName, StringComparer.OrdinalIgnoreCase)
                .Select(s => new McpRoleExclusionViewModel(
                    s.DisplayName, s.McpRoleKey, excluded.Contains(s.McpRoleKey)))
                .ToList(),
            UnlistedMcpExclusions = settings.McpExcludedRoles
                .Where(key => !listedKeys.Contains(key))
                .ToList(),
        };
        var dialog = new SettingsDialog { DataContext = viewModel };
        return await dialog.ShowDialog<SettingsResult?>(Owner);
    }

    public async Task<SessionOptionsResult?> ShowSessionOptionsAsync(AwsSession session)
    {
        var viewModel = new SessionOptionsDialogViewModel
        {
            SessionName = session.DisplayName,
            AccountName = session.AccountName,
            ProfileName = session.ProfileName,
            Region = session.Region,
        };
        var dialog = new SessionOptionsDialog { DataContext = viewModel };
        return await dialog.ShowDialog<SessionOptionsResult?>(Owner);
    }
}
