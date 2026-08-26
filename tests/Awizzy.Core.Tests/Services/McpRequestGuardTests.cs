using Awizzy.Mcp;

namespace Awizzy.Core.Tests.Services;

public class McpRequestGuardTests
{
    [Test]
    [Arguments("localhost")]
    [Arguments("LOCALHOST")]
    [Arguments("127.0.0.1")]
    [Arguments("::1")]
    public async Task AllowsLocalHostNamesWithoutOrigin(string host)
    {
        await Assert.That(McpRequestGuard.IsAllowed(host, origin: null)).IsTrue();
    }

    [Test]
    [Arguments("attacker.example.com")]
    [Arguments("192.168.1.10")]
    [Arguments("localhost.attacker.example.com")]
    [Arguments("")]
    [Arguments(null)]
    public async Task RejectsForeignHostNames(string? host)
    {
        // A DNS-rebinding page reaches 127.0.0.1 but carries its own domain in the Host header.
        await Assert.That(McpRequestGuard.IsAllowed(host, origin: null)).IsFalse();
    }

    [Test]
    [Arguments("http://localhost:5000")]
    [Arguments("https://127.0.0.1")]
    [Arguments("http://[::1]:8080")]
    public async Task AllowsLocalOrigins(string origin)
    {
        await Assert.That(McpRequestGuard.IsAllowed("localhost", origin)).IsTrue();
    }

    [Test]
    [Arguments("https://attacker.example.com")]
    [Arguments("http://localhost.attacker.example.com")]
    [Arguments("null")]
    [Arguments("file://localhost")]
    public async Task RejectsForeignOrigins(string origin)
    {
        await Assert.That(McpRequestGuard.IsAllowed("localhost", origin)).IsFalse();
    }
}
