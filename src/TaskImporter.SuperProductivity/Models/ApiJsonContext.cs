using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskImporter.SuperProductivity.Models;

/// <summary>
/// Source-generated serialization for everything that crosses the wire.
///
/// The packaged extension is trimmed in Release, and trimming switches
/// reflection-based System.Text.Json off, so any type the client reads or
/// writes must be listed here. The test project runs with reflection off as
/// well, so a missing entry fails there first.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ApiEnvelope<HealthDto>))]
[JsonSerializable(typeof(ApiEnvelope<TaskDto>))]
[JsonSerializable(typeof(ApiEnvelope<IReadOnlyList<TaskDto>>))]
[JsonSerializable(typeof(ApiEnvelope<IReadOnlyList<ProjectDto>>))]
[JsonSerializable(typeof(ApiEnvelope<IReadOnlyList<TagDto>>))]
[JsonSerializable(typeof(ApiEnvelope<JsonElement>))]
[JsonSerializable(typeof(CreateTaskRequest))]
[JsonSerializable(typeof(UpdateTaskRequest))]
[JsonSerializable(typeof(SetCurrentTaskRequest))]
public sealed partial class ApiJsonContext : JsonSerializerContext
{
}
