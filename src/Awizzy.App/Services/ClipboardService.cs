using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace Awizzy.App.Services;

public class ClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        var clipboard =
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
            .MainWindow?.Clipboard
            ?? throw new InvalidOperationException("Clipboard is not available.");
        await clipboard.SetTextAsync(text);
    }
}
