using System.Text.Json.Serialization;

namespace UsageMon;

// Source-generated JSON: works in trimmed builds, where reflection-based serialization is disabled.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Preferences))]
[JsonSerializable(typeof(Usage.UsageSnapshot[]))]
[JsonSerializable(typeof(DateTimeOffset))]
internal partial class AppJsonContext : JsonSerializerContext;
