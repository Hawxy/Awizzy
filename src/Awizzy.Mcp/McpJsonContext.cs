using System.Text.Json.Serialization;

namespace Awizzy.Mcp;

/// <summary>Source-generated JSON metadata for tool inputs/outputs so the MCP server
/// works under trimming and Native AOT. CamelCase matches the SDK's default wire format.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntegrationInfo))]
[JsonSerializable(typeof(IReadOnlyList<IntegrationInfo>))]
[JsonSerializable(typeof(SessionInfo))]
[JsonSerializable(typeof(IReadOnlyList<SessionInfo>))]
[JsonSerializable(typeof(SyncInfo))]
[JsonSerializable(typeof(string))]
internal partial class McpJsonContext : JsonSerializerContext;
