using Avalonia.Styling;
using Awizzy.Core.Models;
using SukiUI;

namespace Awizzy.App.Services;

public static class ThemeApplier
{
    private static bool _watchingVariantChanges;

    public static void Apply(ThemeMode mode)
    {
        if (Avalonia.Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        RefreshPrimaryShades(app);

        // In System mode the OS can flip the variant while the app runs, which
        // leaves the shades stale the same way.
        if (!_watchingVariantChanges)
        {
            _watchingVariantChanges = true;
            app.ActualThemeVariantChanged += (_, _) => RefreshPrimaryShades(app);
        }
    }

    /// <summary>SukiUI bakes SukiPrimaryColor120/150 in code from the variant active at
    /// the moment the color theme is set (light: raw primary, dark: lightened). Changing
    /// RequestedThemeVariant directly does not recompute them, leaving primary-blue text
    /// on the dark theme when the variants differ; re-setting the color theme does.</summary>
    private static void RefreshPrimaryShades(Avalonia.Application app)
    {
        var suki = SukiTheme.GetInstance(app);
        if (suki.ActiveColorTheme is { } colorTheme)
            suki.ChangeColorTheme(colorTheme);
    }
}
