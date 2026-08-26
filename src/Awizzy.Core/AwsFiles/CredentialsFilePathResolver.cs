using Awizzy.Core.Services;

namespace Awizzy.Core.AwsFiles;

/// <summary>Resolves the AWS shared credentials file location: app setting override,
/// then AWS_SHARED_CREDENTIALS_FILE, then ~/.aws/credentials.</summary>
public class CredentialsFilePathResolver(WorkspaceState state)
{
    public string GetPath()
    {
        if (state.Workspace.Settings.CredentialsFilePath is { Length: > 0 } configured)
            return configured;

        return GetDefaultPath();
    }

    /// <summary>The path used when no app-setting override is configured.</summary>
    public string GetDefaultPath()
    {
        if (Environment.GetEnvironmentVariable("AWS_SHARED_CREDENTIALS_FILE") is { Length: > 0 } fromEnv)
            return fromEnv;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".aws",
            "credentials");
    }
}
