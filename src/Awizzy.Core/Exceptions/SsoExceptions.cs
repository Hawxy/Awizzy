namespace Awizzy.Core.Exceptions;

/// <summary>The integration's SSO access token is missing, expired, or rejected; the user must log in again.</summary>
public class SsoSessionExpiredException(string message) : Exception(message);

/// <summary>The device authorization expired before the user approved it in the browser.</summary>
public class SsoLoginTimeoutException(string message) : Exception(message);

/// <summary>The user declined the device authorization in the browser.</summary>
public class SsoLoginDeniedException(string message) : Exception(message);
