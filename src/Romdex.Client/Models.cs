using System.Text.Json;
using System.Text.Json.Serialization;

namespace Romdex.Client;

// Models of the public API, /api/v1. Enums are camelCase strings on the wire.

/// <summary>Serializer settings matching the API.</summary>
public static class RomdexJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }
}

public enum TaskState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <param name="Title">What it's working on, such as "Library scan"; filled in by <c>GET /tasks</c>.</param>
/// <param name="Paused">Held back: it won't start until resumed.</param>
/// <param name="QueueOrder">Its place among queued tasks: lower runs first.</param>
public sealed record TaskInfo(Guid Id, string Kind, TaskState State, string? Progress, string? Error, DateTime CreatedAt, DateTime? StartedAt, DateTime? FinishedAt,
    string? Title = null, bool Paused = false, double QueueOrder = 0)
{
    public bool Active => State is TaskState.Queued or TaskState.Running;

    /// <summary>When it last changed: finished, started or queued.</summary>
    public DateTime UpdatedAt => FinishedAt ?? StartedAt ?? CreatedAt;
}

/// <summary>Body of <c>POST /tasks/{id}/move</c>.</summary>
/// <param name="Before">Put it just before this one in the queue; null for the end.</param>
public sealed record MoveInQueue(Guid? Before);

public enum LogEntryLevel
{
    Information,
    Warning,
    Error,
    /// <summary>Information about something that finished well, such as a scan.</summary>
    Success,
}

/// <summary>One event in the activity log.</summary>
/// <param name="Seq">Increases by one per entry; pass the latest you have as <c>after</c> to get only newer ones.</param>
/// <param name="Source">The part of Romdex it came from, such as TaskWorker or PluginCatalog.</param>
/// <param name="Error">The underlying error's message, if there was one.</param>
public sealed record LogEntry(long Seq, DateTime Time, LogEntryLevel Level, string Source, string Message, string? Error);

/// <summary>How long the activity log is kept.</summary>
public sealed record LogSettings
{
    /// <summary>Days of entries the Log page shows, from 1 to 90.</summary>
    public int MemoryDays { get; init; } = 7;

    /// <summary>Days the backup files under <c>data/logs</c> are kept, from 1 to 3650; at least <see cref="MemoryDays"/>.</summary>
    public int FileDays { get; init; } = 30;
}

/// <param name="Latest">The newest entry's sequence number, even when no entries are returned.</param>
public sealed record LogPage(IReadOnlyList<LogEntry> Entries, long Latest);
