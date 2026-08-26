using Avalonia.Threading;
using Awizzy.Core.Abstractions;

namespace Awizzy.App.Services;

public class AvaloniaMainThreadDispatcher : IMainThreadDispatcher
{
    public Task InvokeAsync(Func<Task> action) => Dispatcher.UIThread.InvokeAsync(action);

    public Task<T> InvokeAsync<T>(Func<T> action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public Task<T> InvokeAsync<T>(Func<Task<T>> action) => Dispatcher.UIThread.InvokeAsync(action);
}
