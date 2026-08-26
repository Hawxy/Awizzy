using System.Diagnostics;
using Awizzy.Core.Abstractions;

namespace Awizzy.Core.Services;

public class BrowserLauncher : IBrowserLauncher
{
    public void Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"Refusing to open non-HTTP URL: {url}");
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
