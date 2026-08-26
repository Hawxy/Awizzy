using Awizzy.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Awizzy.App.ViewModels;

public record SettingsResult(
    TimeSpan RefreshMargin,
    string? CredentialsFilePath,
    ThemeMode Theme,
    bool McpServerEnabled,
    int McpServerPort);

public partial class SettingsDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private decimal? _refreshMarginMinutes = 10;

    [ObservableProperty]
    private string _credentialsFilePath = string.Empty;

    [ObservableProperty]
    private ThemeMode _theme = ThemeMode.System;

    [ObservableProperty]
    private bool _mcpServerEnabled;

    [ObservableProperty]
    private decimal? _mcpServerPort = 52100;

    public required string DefaultCredentialsPath { get; init; }

    public ThemeMode[] Themes { get; } = [ThemeMode.System, ThemeMode.Light, ThemeMode.Dark];

    public SettingsResult ToResult()
    {
        // SSO role credentials last at most an hour, so a margin close to that would refresh constantly.
        if (RefreshMarginMinutes is not { } minutes || minutes is < 1 or > 55)
            throw new ArgumentException("Refresh margin must be between 1 and 55 minutes.");

        if (McpServerPort is not { } port || port is < 1024 or > 65535)
            throw new ArgumentException("MCP server port must be between 1024 and 65535.");

        var path = CredentialsFilePath.Trim();
        return new SettingsResult(
            TimeSpan.FromMinutes((int)minutes),
            path.Length == 0 ? null : path,
            Theme,
            McpServerEnabled,
            (int)port);
    }
}
