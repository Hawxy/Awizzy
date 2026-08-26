namespace Awizzy.Core.Models;

public static class ProfileNames
{
    /// <summary>Derives a credentials-file profile name from an account name, e.g. "Acme Prod" → "acme-prod".</summary>
    public static string DeriveFromAccountName(string accountName)
    {
        var chars = accountName.Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')
            .ToArray();
        var collapsed = string.Join('-',
            new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length > 0 ? collapsed : "default";
    }

    /// <summary>Validates and normalizes a user-entered profile name.</summary>
    public static string Validate(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("Profile name cannot be empty.");
        if (name.Any(c => c is '[' or ']' or '\r' or '\n' || (char.IsWhiteSpace(c) && c != ' ')))
            throw new ArgumentException("Profile name contains invalid characters.");
        return name;
    }
}
