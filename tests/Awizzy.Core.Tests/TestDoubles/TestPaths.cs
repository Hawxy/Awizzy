namespace Awizzy.Core.Tests.TestDoubles;

/// <summary>Rooted fake paths that are valid on the host OS; MockFileSystem normalizes
/// per platform, so Windows-drive literals break on Unix runners.</summary>
public static class TestPaths
{
    /// <summary>Roots a relative path like "appdata/Awizzy" for the host OS.</summary>
    public static string Root(string relative)
    {
        var parts = relative.Split('/');
        return OperatingSystem.IsWindows()
            ? Path.Combine([@"C:\", .. parts])
            : Path.Combine(["/", .. parts]);
    }
}
