using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Romdex.Client;
using Romdex.Core.Data;
using Romdex.Core.Tasks;
using Romdex.Core.Tests.Support;

namespace Romdex.Core.Tests;

/// <summary>Running, pausing, resuming, moving and cancelling background tasks.</summary>
public sealed class QueueTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "romdex-tests", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;
    private readonly RomdexClient _api;

    public QueueTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Romdex:DataPath", _dataPath).UseSetting("Romdex:CatalogueRefreshHours", "0")
            .ConfigureServices(services => services.AddScoped<ITaskHandler, Echo>()));
        _api = new RomdexClient(_factory.CreateSignedInClient());
    }

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataPath))
            Directory.Delete(_dataPath, recursive: true);
    }

    [Fact]
    public async Task A_queued_task_runs_with_its_handler_and_says_what_it_is()
    {
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TaskQueue>().EnqueueAsync(Echo.TaskKind, "\"hello\"", default);

        var task = await FinishedAsync((await _api.ListTasksAsync()).Single().Id);
        Assert.Equal(TaskState.Succeeded, task.State);
        Assert.Equal("Echo hello", task.Title);
        Assert.Equal("Said hello", task.Progress);
    }

    [Fact]
    public async Task Tasks_can_be_moved_paused_resumed_and_cancelled()
    {
        List<Guid> ids;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RomdexDbContext>();
            var tasks = new[] { "first", "second" }.Select((kind, i) => new BackgroundTask { Kind = kind, Paused = true, QueueOrder = i }).ToList();
            db.BackgroundTasks.AddRange(tasks);
            await db.SaveChangesAsync();
            ids = tasks.Select(t => t.Id).ToList();
        }

        await _api.MoveTaskAsync(ids[1], before: ids[0]);
        var queued = (await _api.ListTasksAsync()).Where(t => t.State == TaskState.Queued).OrderBy(t => t.QueueOrder).Select(t => t.Kind);
        Assert.Equal(["second", "first"], queued);

        await _api.CancelTaskAsync(ids[0]);
        var cancelled = await _api.GetTaskAsync(ids[0]);
        Assert.Equal(TaskState.Cancelled, cancelled!.State);
        Assert.False(cancelled.Active);
        var again = await Assert.ThrowsAsync<RomdexApiException>(() => _api.CancelTaskAsync(ids[0]));
        Assert.Equal(HttpStatusCode.Conflict, again.Status);

        // Resumed, the worker picks it up; it has no handler, so it fails rather than staying queued.
        await _api.ResumeTaskAsync(ids[1]);
        var task = await FinishedAsync(ids[1]);
        Assert.False(task.Paused);
        Assert.Equal(TaskState.Failed, task.State);
    }

    private async Task<TaskInfo> FinishedAsync(Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        TaskInfo? task;
        do
        {
            await Task.Delay(100);
            task = await _api.GetTaskAsync(id);
        } while (task!.State is TaskState.Queued or TaskState.Running && DateTime.UtcNow < deadline);
        return task;
    }

    /// <summary>A task that says what it was given.</summary>
    private sealed class Echo : ITaskHandler
    {
        public const string TaskKind = "echo";

        public string Kind => TaskKind;

        public string Title(string? payload) => $"Echo {payload?.Trim('"')}";

        public Task RunAsync(BackgroundTask task, IProgress<string> progress, CancellationToken ct)
        {
            progress.Report($"Said {task.PayloadJson?.Trim('"')}");
            return Task.CompletedTask;
        }
    }
}
