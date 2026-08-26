namespace Awizzy.Core.Models;

/// <summary>Root persisted document, stored encrypted on disk.</summary>
public class Workspace
{
    public int SchemaVersion { get; set; } = 2;
    public List<SsoIntegration> Integrations { get; set; } = [];
    public List<AwsSession> Sessions { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
}
