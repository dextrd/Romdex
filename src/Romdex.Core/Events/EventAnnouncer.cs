using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Romdex.Client;
using Romdex.Core.Data;
using Romdex.Core.Tasks;

namespace Romdex.Core.Events;

/// <summary>
/// Turns changes into events: for now, a background task finishing or failing. It looks for tasks finished and not yet
/// announced, rather than being told by the code that finishes them, so nothing is missed, even across a restart. Each
/// event goes to the event stream and, as a delivery row saved with the announcement, to every webhook that wants it.
/// </summary>
public sealed class EventAnnouncer(IServiceScopeFactory scopes, EventFeed feed, WebhookSender sender, ILogger<EventAnnouncer> log)
    : BackgroundService
{
    /// <summary>How often to look even without a change being seen, as a backstop.</summary>
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _wake = new(0, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        feed.Raised += OnRaised;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    while (await AnnounceAsync(stoppingToken))
                    {
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    log.LogError(e, "Couldn't announce events");
                }
                await _wake.WaitAsync(Every, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
        finally
        {
            feed.Raised -= OnRaised;
        }
    }

    private void OnRaised(string kind)
    {
        if (kind != EventFeed.Tasks)
            return;
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already woken.
        }
    }

    private const int Batch = 100;

    /// <summary>Announces what changed since last time. True if there may be more.</summary>
    public async Task<bool> AnnounceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RomdexDbContext>();
        var titles = scope.ServiceProvider.GetRequiredService<TaskTitles>();
        var events = new List<RomdexEvent>();

        var tasks = await db.BackgroundTasks
            .Where(t => !t.Announced && (t.Status == BackgroundTaskStatus.Succeeded || t.Status == BackgroundTaskStatus.Failed))
            .OrderBy(t => t.FinishedAt).Take(Batch).ToListAsync(ct);
        foreach (var task in tasks)
        {
            events.Add(New(task.Status == BackgroundTaskStatus.Succeeded ? EventTypes.TaskCompleted : EventTypes.TaskFailed,
                task.FinishedAt ?? DateTime.UtcNow, new TaskEventData(titles.ToInfo(task))));
            task.Announced = true;
        }

        if (tasks.Count == 0)
            return false;

        var webhooks = await db.Webhooks.Where(w => w.Enabled).ToListAsync(ct);
        foreach (var @event in events)
        foreach (var webhook in webhooks.Where(w => Wants(w, @event.Type)))
            db.WebhookDeliveries.Add(Delivery(webhook, @event));
        await db.SaveChangesAsync(ct);

        foreach (var @event in events)
            feed.Publish(@event);
        if (webhooks.Count > 0)
            sender.Wake();
        return tasks.Count == Batch;
    }

    public static RomdexEvent New<T>(string type, DateTime occurredAt, T data) =>
        new(Guid.CreateVersion7(), type, DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc), JsonSerializer.SerializeToElement(data, RomdexJson.Options));

    public static WebhookDelivery Delivery(Webhook webhook, RomdexEvent @event) => new()
    {
        WebhookId = webhook.Id,
        Webhook = webhook,
        Event = @event.Type,
        PayloadJson = JsonSerializer.Serialize(@event, RomdexJson.Options),
    };

    public static bool Wants(Webhook webhook, string type) =>
        webhook.Events.Length == 0 || webhook.Events.Split(',').Contains(type, StringComparer.Ordinal);
}
