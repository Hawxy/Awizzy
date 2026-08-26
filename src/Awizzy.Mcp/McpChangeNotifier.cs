namespace Awizzy.Mcp;

/// <summary>Signals workspace-level changes made through MCP tools (e.g. sessions added by a sync)
/// so the UI can rebuild views that session-level events do not cover.</summary>
public class McpChangeNotifier
{
    public event EventHandler? WorkspaceChanged;

    public void NotifyWorkspaceChanged() => WorkspaceChanged?.Invoke(this, EventArgs.Empty);
}
