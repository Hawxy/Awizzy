using Amazon;
using Amazon.Runtime;
using Amazon.SSO;
using Amazon.SSOOIDC;
using Awizzy.Core.Abstractions;

namespace Awizzy.Core.Services;

public class SsoClientFactory : ISsoClientFactory
{
    public IAmazonSSOOIDC CreateOidcClient(string region) =>
        new AmazonSSOOIDCClient(new AnonymousAWSCredentials(), RegionEndpoint.GetBySystemName(region));

    public IAmazonSSO CreateSsoClient(string region) =>
        new AmazonSSOClient(new AnonymousAWSCredentials(), RegionEndpoint.GetBySystemName(region));
}
