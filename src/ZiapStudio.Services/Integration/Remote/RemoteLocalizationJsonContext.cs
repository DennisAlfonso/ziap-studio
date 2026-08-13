using System.Text.Json.Serialization;
using ZiapStudio.Core.Localization;

namespace ZiapStudio.Services.Integration.Remote;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(RemoteLocalizationManifest))]
internal sealed partial class RemoteLocalizationJsonContext : JsonSerializerContext
{
}
