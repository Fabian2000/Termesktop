using System.Text.Json;
using System.Text.Json.Serialization;
using Termesktop.Apps;

namespace Termesktop;

[JsonSerializable(typeof(DesktopSettings))]
[JsonSerializable(typeof(List<string>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class AppJsonContext : JsonSerializerContext
{
}
