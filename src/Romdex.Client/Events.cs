using System.Text.Json;

namespace Romdex.Client;

/// <summary>
/// The events Romdex announces, to webhooks and as named events on <c>GET /events</c>. Each carries a
/// <see cref="RomdexEvent"/> whose <see cref="RomdexEvent.Data"/> is the record named here.
/// </summary>
public static class EventTypes
{
    /// <summary>A background task, such as a library scan, finished. Data: <see cref="TaskEventData"/>.</summary>
    public const string TaskCompleted = "task.completed";

    /// <summary>A background task failed. Data: <see cref="TaskEventData"/>.</summary>
    public const string TaskFailed = "task.failed";

    /// <summary>Sent by <c>POST /webhooks/{id}/test</c> only. Data: <see cref="PingEventData"/>.</summary>
    public const string Ping = "ping";

    public static IReadOnlyList<string> All { get; } = [TaskCompleted, TaskFailed];
}

/// <summary>Something that happened, as sent to webhooks and on the event stream.</summary>
/// <param name="Id">Unique to this event. A webhook retried after a failure has the same ID, so receivers can ignore repeats.</param>
/// <param name="Type">One of <see cref="EventTypes"/>.</param>
/// <param name="Data">Depends on the type; see <see cref="EventTypes"/>.</param>
public sealed record RomdexEvent(Guid Id, string Type, DateTime OccurredAt, JsonElement Data)
{
    public T? DataAs<T>() => Data.Deserialize<T>(RomdexJson.Options);
}

/// <summary>A background task that finished or failed: its kind says what it was, its progress what it found.</summary>
public sealed record TaskEventData(TaskInfo Task);

public sealed record PingEventData(string Message);

/// <summary>A server-sent event from <c>GET /events</c>: what changed, so a client can reload what it shows.</summary>
/// <param name="Type"><c>tasks</c> for now; later phases add the library and intake.</param>
public sealed record ServerEvent(string Type, DateTime At);

// ---- Webhooks ----

/// <summary>An address Romdex sends events to.</summary>
/// <param name="Secret">Signs each delivery; see the client guide for checking the <c>Romdex-Signature</c> header.</param>
/// <param name="Events">The event types it gets; empty for all of them.</param>
/// <param name="LastDelivery">The most recent delivery, to show whether it's working.</param>
public sealed record WebhookInfo(
    Guid Id,
    string Name,
    string Url,
    string Secret,
    IReadOnlyList<string> Events,
    bool Enabled,
    DateTime CreatedAt,
    WebhookDeliveryInfo? LastDelivery);

/// <summary>Body of <c>POST /webhooks</c> and <c>PUT /webhooks/{id}</c>.</summary>
/// <param name="Events">Event types to send; empty or null for all of them, including ones added later.</param>
public sealed record SaveWebhook(string Name, string Url, IReadOnlyList<string>? Events = null, bool Enabled = true);

public enum WebhookDeliveryState
{
    /// <summary>Not sent yet, or failed with tries left; see <see cref="WebhookDeliveryInfo.NextAttemptAt"/>.</summary>
    Pending,
    Delivered,

    /// <summary>Every try failed.</summary>
    Failed,
}

/// <param name="ResponseStatus">The HTTP status the last try got, if it got one.</param>
public sealed record WebhookDeliveryInfo(
    Guid Id,
    string Event,
    WebhookDeliveryState State,
    int Attempts,
    int? ResponseStatus,
    string? Error,
    DateTime CreatedAt,
    DateTime? FinishedAt,
    DateTime? NextAttemptAt);
