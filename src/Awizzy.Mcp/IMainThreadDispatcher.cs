namespace Awizzy.Mcp;

/// <summary>Runs tool work on the application's main thread. Workspace state is single-threaded;
/// MCP requests arrive on server threads and must not touch it directly.</summary>
public interface IMainThreadDispatcher
{
    Task<T> InvokeAsync<T>(Func<T> action);
    Task<T> InvokeAsync<T>(Func<Task<T>> action);
}
