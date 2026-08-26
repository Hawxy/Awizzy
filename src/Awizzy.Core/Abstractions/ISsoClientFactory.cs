using Amazon.SSO;
using Amazon.SSOOIDC;

namespace Awizzy.Core.Abstractions;

/// <summary>Creates AWS SDK clients for a given SSO region. Both APIs authenticate with the
/// SSO access token per request, so the clients themselves use anonymous credentials.</summary>
public interface ISsoClientFactory
{
    IAmazonSSOOIDC CreateOidcClient(string region);
    IAmazonSSO CreateSsoClient(string region);
}
