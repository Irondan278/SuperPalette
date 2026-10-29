using System.Text.Json.Serialization;

namespace TaskImporter.SuperProductivity.Models;

/// <summary>
/// Every Local REST API response is wrapped like this:
/// { "ok": true, "data": ... } or { "ok": false, "error": { "code", "message" } }
/// </summary>
public sealed class ApiEnvelope<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("error")]
    public ApiError? Error { get; set; }
}

public sealed class ApiError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    public override string ToString()
        => string.IsNullOrWhiteSpace(Code) ? Message ?? "Unknown error" : $"{Code}: {Message}";
}

public sealed class HealthDto
{
    [JsonPropertyName("server")]
    public string? Server { get; set; }

    [JsonPropertyName("rendererReady")]
    public bool RendererReady { get; set; }
}

public sealed class ProjectDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Some payloads use "name" rather than "title"; accept either.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    public string DisplayName => Title ?? Name ?? Id;
}

public sealed class TagDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The tag's tasks in the order Super Productivity shows them. For TODAY
    /// this is the Today list as the user arranged it.
    /// </summary>
    [JsonPropertyName("taskIds")]
    public string[]? TaskIds { get; set; }

    public string DisplayName => Title ?? Name ?? Id;
}

public sealed class TaskDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("tagIds")]
    public string[]? TagIds { get; set; }

    [JsonPropertyName("isDone")]
    public bool IsDone { get; set; }

    [JsonPropertyName("timeSpent")]
    public long TimeSpent { get; set; }

    [JsonPropertyName("timeEstimate")]
    public long TimeEstimate { get; set; }

    [JsonPropertyName("dueDay")]
    public string? DueDay { get; set; }
}

/// <summary>The body of POST /tasks. Null fields are omitted on the wire.</summary>
public sealed class CreateTaskRequest
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }

    [JsonPropertyName("projectId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectId { get; set; }

    [JsonPropertyName("tagIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? TagIds { get; set; }

    [JsonPropertyName("timeEstimate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TimeEstimate { get; set; }

    /// <summary>yyyy-MM-dd.</summary>
    [JsonPropertyName("dueDay")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DueDay { get; set; }

    /// <summary>
    /// Epoch milliseconds. NOTE: the exact encoding the API wants for this field
    /// is the one thing in the client not confirmed against a live instance —
    /// see the README. If it is rejected, this is the field to change.
    /// </summary>
    [JsonPropertyName("dueWithTime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DueWithTime { get; set; }
}

public sealed class UpdateTaskRequest
{
    [JsonPropertyName("isDone")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsDone { get; set; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonPropertyName("notes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Notes { get; set; }
}

public sealed class SetCurrentTaskRequest
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }
}
