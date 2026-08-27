using System.IO.Abstractions;
using Awizzy.Core.Abstractions;
using Awizzy.Core.AwsFiles;
using Awizzy.Core.Persistence;
using Awizzy.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Awizzy.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAwizzyCore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<AppPaths>();

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IDataCipher, DpapiDataCipher>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Resolved eagerly: the Keychain call is cheap, and a failure should surface
            // at startup rather than on first save.
            services.AddSingleton<IDataCipher>(
                new AesGcmDataCipher(MacKeychainKeyProvider.GetOrCreateMasterKey()));
        }
        else
        {
            throw new PlatformNotSupportedException("Only Windows and macOS are supported in this version.");
        }

        services.AddSingleton<CredentialsFilePathResolver>();
        services.AddSingleton<ICredentialsFileWriter, CredentialsFileWriter>();
        services.AddSingleton<ISecureStore, FileSecureStore>();
        services.AddSingleton<IWorkspaceRepository, WorkspaceRepository>();
        services.AddSingleton<WorkspaceState>();
        services.AddSingleton<ISsoClientFactory, SsoClientFactory>();
        services.AddSingleton<ISsoOidcAuthService, SsoOidcAuthService>();
        services.AddSingleton<ISsoPortalService, SsoPortalService>();
        services.AddSingleton<IBrowserLauncher, BrowserLauncher>();
        services.AddSingleton<IWebConsoleUrlService>(_ => new WebConsoleUrlService(new HttpClient()));
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IIntegrationService, IntegrationService>();
        services.AddHostedService<CredentialRefreshService>();
        return services;
    }
}
