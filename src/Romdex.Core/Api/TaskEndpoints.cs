using Microsoft.EntityFrameworkCore;
using Romdex.Client;
using Romdex.Core.Access;
using Romdex.Core.Data;
using Romdex.Core.Events;
using Romdex.Core.Tasks;

namespace Romdex.Core.Api;

/// <summary>Background work, and the event stream that says when anything changes.</summary>
public static class TaskEndpoints
{
    /// <summary>How often an idle event stream sends a comment, so proxies keep it open.</summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder api)
    {
        var tasks = api.MapGroup("/tasks").WithTags("Tasks");

        tasks.MapGet("", async (RomdexDbContext db, TaskTitles titles, bool? active, int? limit, CancellationToken ct) =>
            {
                var query = db.BackgroundTasks.AsQueryable();
                if (active is { } a)
                    query = a
                        ? query.Where(t => t.Status == BackgroundTaskStatus.Queued || t.Status == BackgroundTaskStatus.Running)
                        : query.Where(t => t.Status != BackgroundTaskStatus.Queued && t.Status != BackgroundTaskStatus.Running);
                var found = await query.OrderByDescending(t => t.CreatedAt).Take(Math.Clamp(limit ?? 50, 1, 500)).ToListAsync(ct);
                return found.Select(titles.ToInfo).ToList();
            })
            .WithName("ListTasks").WithSummary("Background work such as library scans, newest first.");

        tasks.MapGet("/{id:guid}", async (Guid id, RomdexDbContext db, TaskTitles titles, CancellationToken ct) =>
                await db.BackgroundTasks.FindAsync([id], ct) is { } task ? Results.Ok(titles.ToInfo(task)) : Results.NotFound())
            .WithName("GetTask").WithSummary("Status of a background task.")
            .Produces<TaskInfo>().Produces(StatusCodes.Status404NotFound);

        // Stopping and ordering background work. Each answers 409 when the task is past it, such as cancelling a finished one.
        tasks.MapPost("/{id:guid}/cancel", (Guid id, RomdexDbContext db, TaskQueue queue, TaskTitles titles, CancellationToken ct) =>
                ActAsync(id, db, titles, task => queue.CancelAsync(task, ct), ct))
            .RequireScope(Scopes.Scan).WithName("CancelTask").WithSummary("Stop a queued or running task for good.")
            .Produces<TaskInfo>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);
        tasks.MapPost("/{id:guid}/pause", (Guid id, RomdexDbContext db, TaskQueue queue, TaskTitles titles, CancellationToken ct) =>
                ActAsync(id, db, titles, task => queue.PauseAsync(task, ct), ct))
            .RequireScope(Scopes.Scan).WithName("PauseTask")
            .WithSummary("Hold a task back until resumed. A running scan stops, and carries on from where it was when resumed; shorter tasks start again.")
            .Produces<TaskInfo>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);
        tasks.MapPost("/{id:guid}/resume", (Guid id, RomdexDbContext db, TaskQueue queue, TaskTitles titles, CancellationToken ct) =>
                ActAsync(id, db, titles, task => queue.ResumeAsync(task, ct), ct))
            .RequireScope(Scopes.Scan).WithName("ResumeTask").WithSummary("Let a paused task go again, in its place in the queue.")
            .Produces<TaskInfo>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);
        tasks.MapPost("/{id:guid}/move", (Guid id, MoveInQueue body, RomdexDbContext db, TaskQueue queue, TaskTitles titles, CancellationToken ct) =>
                ActAsync(id, db, titles, task => queue.MoveAsync(task, body.Before, ct), ct))
            .RequireScope(Scopes.Scan).WithName("MoveTask").WithSummary("Move a queued task to just before another, or to the end. Tasks run one at a time.")
            .Produces<TaskInfo>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);

        api.MapGet("/events", async (HttpContext http, EventFeed feed, CancellationToken ct) =>
            {
                http.Response.Headers.ContentType = "text/event-stream";
                http.Response.Headers.CacheControl = "no-cache";
                // Tells nginx and similar proxies not to hold events back.
                http.Response.Headers["X-Accel-Buffering"] = "no";
                using var subscription = feed.Subscribe();
                try
                {
                    await http.Response.WriteAsync(": connected\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                    while (!ct.IsCancellationRequested)
                    {
                        using var beat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        beat.CancelAfter(KeepAlive);
                        try
                        {
                            await subscription.Reader.WaitToReadAsync(beat.Token);
                            while (subscription.Reader.TryRead(out var message))
                                await http.Response.WriteAsync(
                                    (message.Id is null ? "" : $"id: {message.Id}\n") + $"event: {message.Type}\ndata: {message.Data}\n\n", ct);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            // Nothing for a while: a comment keeps proxies from closing the connection.
                            await http.Response.WriteAsync(": keep-alive\n\n", ct);
                        }
                        await http.Response.Body.FlushAsync(ct);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // The client went away.
                }
            })
            .WithTags("System")
            .WithName("Events")
            .WithSummary("Server-sent events. Unnamed kinds say what changed (tasks, for now), so you reload what you show; "
                         + "named events such as task.completed carry what happened, as webhooks do.")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");

        return api;
    }

    private static async Task<IResult> ActAsync(Guid id, RomdexDbContext db, TaskTitles titles, Func<BackgroundTask, Task<bool>> action, CancellationToken ct)
    {
        if (await db.BackgroundTasks.FindAsync([id], ct) is not { } task)
            return Results.NotFound();
        return await action(task) ? Results.Ok(titles.ToInfo(task)) : Results.Conflict();
    }
}
