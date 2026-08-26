namespace Awizzy.Core.Abstractions;

/// <summary>Runs work on the application's main thread. Workspace state is single-threaded;
/// background services and server threads must not touch it directly.</summary>
public interface IMainThreadDispatcher
{
    Task InvokeAsync(Func<Task> action);
    Task<T> InvokeAsync<T>(Func<T> action);
    Task<T> InvokeAsync<T>(Func<Task<T>> action);
}
