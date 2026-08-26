using Avalonia.Styling;
using Awizzy.Core.Models;

namespace Awizzy.App.Services;

public static class ThemeApplier
{
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
    }
}
