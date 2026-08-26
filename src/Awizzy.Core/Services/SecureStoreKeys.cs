namespace Awizzy.Core.Services;

public static class SecureStoreKeys
{
    public static string ClientRegistration(string region) => $"sso-client:{region}";
    public static string SsoToken(Guid integrationId) => $"sso-token:{integrationId}";
}
