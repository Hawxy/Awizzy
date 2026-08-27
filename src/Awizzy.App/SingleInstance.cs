using System.Net.Sockets;

namespace Awizzy.App;

/// <summary>Ensures one instance per mode and relays "activate" from later launches.
/// Windows uses kernel objects; Unix has no named EventWaitHandle, so a Unix domain
/// socket doubles as the instance lock and the activation channel.</summary>
public static class SingleInstance
{
    /// <summary>Claims the instance for this mode. Returns null when another instance is
    /// already running (after signaling it to come to the front); otherwise returns a
    /// handle the caller must keep alive for the process lifetime.</summary>
    public static IDisposable? TryClaim(string suffix, Action activate) =>
        OperatingSystem.IsWindows()
            ? TryClaimWindows(suffix, activate)
            : TryClaimUnix(suffix, activate);

    private static IDisposable? TryClaimWindows(string suffix, Action activate)
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\Awizzy" + suffix, out var isFirstInstance);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Awizzy-activate" + suffix);

        if (!isFirstInstance)
        {
            signal.Set();
            signal.Dispose();
            mutex.Dispose();
            return null;
        }

        var listener = new Thread(() =>
        {
            while (signal.WaitOne())
                activate();
        })
        { IsBackground = true };
        listener.Start();

        return new Claim(signal, mutex);
    }

    private static IDisposable? TryClaimUnix(string suffix, Action activate)
    {
        var endpoint = new UnixDomainSocketEndPoint(
            Path.Combine(Path.GetTempPath(), $"awizzy{suffix}.sock"));

        // A live instance answers on the socket; a stale file from a crash does not.
        using (var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            try
            {
                probe.Connect(endpoint);
                probe.Send([1]);
                return null;
            }
            catch (SocketException)
            {
            }
        }

        File.Delete(Path.Combine(Path.GetTempPath(), $"awizzy{suffix}.sock"));
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(endpoint);
        socket.Listen(1);

        var listener = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var client = socket.Accept();
                    client.Receive(new byte[1]);
                    activate();
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    // Socket disposed at shutdown.
                    return;
                }
            }
        })
        { IsBackground = true };
        listener.Start();

        return socket;
    }

    private sealed class Claim(params IDisposable[] resources) : IDisposable
    {
        public void Dispose()
        {
            foreach (var resource in resources)
                resource.Dispose();
        }
    }
}
