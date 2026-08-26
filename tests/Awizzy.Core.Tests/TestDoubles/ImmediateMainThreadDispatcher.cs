using Awizzy.Core.Abstractions;

namespace Awizzy.Core.Tests.TestDoubles;

/// <summary>Runs dispatched work inline; tests have no UI thread.</summary>
public class ImmediateMainThreadDispatcher : IMainThreadDispatcher
{
    public Task InvokeAsync(Func<Task> action) => action();

    public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());

    public Task<T> InvokeAsync<T>(Func<Task<T>> action) => action();
}
