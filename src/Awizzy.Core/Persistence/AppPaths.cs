namespace Awizzy.Core.Persistence;

/// <summary>Filesystem locations for app data. Overridable for tests.</summary>
public class AppPaths
{
    public AppPaths()
        : this(DefaultRoot())
    {
    }

    public AppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
    }

    public string RootDirectory { get; }
    public string WorkspaceFile => Path.Combine(RootDirectory, "workspace.json");
    public string SecureDirectory => Path.Combine(RootDirectory, "secure");
    public string LogDirectory => Path.Combine(RootDirectory, "logs");

    private static string DefaultRoot()
    {
        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "Awizzy");

        return MigrateFromLegacyDirectory();
    }

    /// <summary>Moves data from the pre-rename AwsProfileManager directory on first run.
    /// Windows-only history; macOS never had the old directory.</summary>
    private static string MigrateFromLegacyDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, "Awizzy");
        var legacy = Path.Combine(appData, "AwsProfileManager");
        if (!Directory.Exists(root) && Directory.Exists(legacy))
        {
            try
            {
                Directory.Move(legacy, root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another instance may hold the directory; fall back to a fresh one.
            }
        }

        return root;
    }
}
