using System.Text.Json.Serialization;
using Awizzy.Core.Models;

namespace Awizzy.Core;

/// <summary>Source-generated JSON metadata so serialization works under trimming and Native AOT.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Workspace))]
[JsonSerializable(typeof(StoredSsoToken))]
[JsonSerializable(typeof(SsoClientRegistration))]
[JsonSerializable(typeof(FederationSession))]
public partial class CoreJsonContext : JsonSerializerContext;
