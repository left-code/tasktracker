using System.Text.Json;
using System.Text.Json.Serialization;
using TaskTracker.Models;

namespace TaskTracker.Storage;

/// <summary>
/// JSON code generated at build time for the files TaskTracker writes. The published exe is trimmed,
/// and the reflection-based serializer could lose the members it needs.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(DayLog))]
[JsonSerializable(typeof(OpenTask))]
internal sealed partial class LogJson : JsonSerializerContext;
