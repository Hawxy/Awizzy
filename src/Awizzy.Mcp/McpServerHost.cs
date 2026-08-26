using System.Net;
using System.Text.Json;
using Awizzy.Core.Abstractions;
using Awizzy.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Awizzy.Mcp;

public interface IMcpServerHost
{
    bool IsRunning { get; }
    int? Port { get; }
    event EventHandler? StatusChanged;

    /// <summary>Starts (or restarts on a new port) the MCP server on the loopback interface.</summary>
    Task StartAsync(int port, CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);
}

/// <summary>Runs the MCP server as a loopback-only Kestrel host inside the app process,
/// so tools operate on the same live session state the UI shows.</summary>
public class McpServerHost(
    WorkspaceState state,
    ISessionManager sessionManager,
    IIntegrationService integrationService,
    IWebConsoleUrlService consoleUrlService,
    TimeProvider time,
    McpChangeNotifier notifier,
    IMainThreadDispatcher mainThreadDispatcher) : IMcpServerHost, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private WebApplication? _app;

    public bool IsRunning => _app is not null;
    public int? Port { get; private set; }
    public event EventHandler? StatusChanged;

    public async Task StartAsync(int port, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await StopCoreAsync(ct);

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));

            // The tools must operate on the app's live singletons, not fresh instances.
            builder.Services.AddSingleton(state);
            builder.Services.AddSingleton(sessionManager);
            builder.Services.AddSingleton(integrationService);
            builder.Services.AddSingleton(consoleUrlService);
            builder.Services.AddSingleton(time);
            builder.Services.AddSingleton(notifier);
            builder.Services.AddSingleton(mainThreadDispatcher);
            builder.Services.AddSingleton<McpTools>();
            // Source-generated serializer metadata keeps tool marshaling Native AOT compatible.
            var toolSerializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
            toolSerializerOptions.TypeInfoResolverChain.Add(McpJsonContext.Default);
            builder.Services
                .AddMcpServer(options => options.ServerInfo = new Implementation
                {
                    Name = "awizzy",
                    Version = "1.0.0",
                })
                .WithHttpTransport()
                .WithTools<McpTools>(toolSerializerOptions);

            var app = builder.Build();
            app.MapMcp();

            try
            {
                await app.StartAsync(ct);
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }

            _app = app;
            Port = port;
        }
        finally
        {
            _lock.Release();
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await StopCoreAsync(ct);
        }
        finally
        {
            _lock.Release();
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task StopCoreAsync(CancellationToken ct)
    {
        if (_app is null)
            return;

        try
        {
            await _app.StopAsync(ct);
        }
        finally
        {
            await _app.DisposeAsync();
            _app = null;
            Port = null;
        }
    }

    public void Dispose()
    {
        // Bounded so an open streaming connection cannot stall app shutdown.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            StopAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
    }
}
