using Awizzy.Core.Models;
using Awizzy.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Awizzy.App;

/// <summary>Seeds an in-memory sample workspace for UI work (run with --demo).</summary>
public static class DemoData
{
    public static void Seed(IServiceProvider services)
    {
        var state = services.GetRequiredService<WorkspaceState>();
        var time = services.GetRequiredService<TimeProvider>();
        var workspace = state.Workspace;
        if (workspace.Integrations.Count > 0)
            return;

        var integration = new SsoIntegration
        {
            Alias = "Acme Corp",
            PortalUrl = "https://acme.awsapps.com/start",
            Region = "eu-west-1",
            AccessTokenExpiresAt = time.GetUtcNow().AddHours(7),
            LastSyncedAt = time.GetUtcNow().AddMinutes(-23),
        };
        workspace.Integrations.Add(integration);

        (string AccountId, string AccountName, string Role, SessionState State)[] rows =
        [
            ("111111111111", "acme-prod", "AdministratorAccess", SessionState.Inactive),
            ("111111111111", "acme-prod", "ReadOnlyAccess", SessionState.Active),
            ("111111111111", "acme-prod", "PowerUserAccess", SessionState.Inactive),
            ("222222222222", "acme-dev", "AdministratorAccess", SessionState.Active),
            ("222222222222", "acme-dev", "ReadOnlyAccess", SessionState.Inactive),
            ("333333333333", "acme-sandbox", "AdministratorAccess", SessionState.Error),
        ];

        foreach (var row in rows)
        {
            workspace.Sessions.Add(new AwsSession
            {
                IntegrationId = integration.Id,
                AccountId = row.AccountId,
                AccountName = row.AccountName,
                RoleName = row.Role,
                Region = "eu-west-1",
                ProfileName = ProfileNames.DeriveFromAccountName(row.AccountName),
                State = row.State,
                CredentialsExpireAt = row.State == SessionState.Active
                    ? time.GetUtcNow().AddMinutes(47)
                    : null,
                ErrorMessage = row.State == SessionState.Error
                    ? "SSO session expired; log in to the integration again."
                    : null,
            });
        }
    }
}
