using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Romdex.Contracts.Plugins.Index;
using Romdex.Core.Data;
using Romdex.Core.Library;
using Romdex.Core.Plugins;
using Romdex.Core.Tasks;

namespace Romdex.Core.Catalogue;

/// <summary>
/// Brings the catalogue up to date: each index plugin that can fetches newer DATs, then every system whose DAT changed
/// is imported again, and the library is matched against the result. A system goes when no plugin lists it any more.
/// </summary>
public sealed class CatalogueRefreshTask(
    IndexPlugins plugins,
    CatalogueImporter importer,
    LibraryMatcher matcher,
    RomdexDbContext db,
    ILogger<CatalogueRefreshTask> logger) : ITaskHandler
{
    public const string TaskKind = "catalogue-refresh";

    public string Kind => TaskKind;

    public string Title(string? payload) => "Catalogue update";

    public async Task RunAsync(BackgroundTask task, IProgress<string> progress, CancellationToken ct)
    {
        // The first plugin in order to list a system wins it; a plugin that fails keeps the systems it had.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        int imported = 0, unchanged = 0;
        foreach (var (instance, plugin) in await plugins.GetAllAsync(ct))
        {
            if (plugin is IDatFetcher fetcher)
            {
                progress.Report($"Fetching DATs: {instance.Name}");
                try
                {
                    var fetched = await fetcher.FetchAsync(ct);
                    if (fetched.Count > 0)
                        logger.LogInformation("{Plugin} fetched {Count} new DATs: {Systems}", instance.Name, fetched.Count, string.Join(", ", fetched.Select(s => s.Name)));
                }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogWarning(e, "{Plugin} couldn't fetch new DATs", instance.Name);
                    notes.Add($"{instance.Name} couldn't fetch new DATs: {e.Message}");
                }
            }

            IReadOnlyList<IndexSystem> systems;
            try
            {
                systems = await plugin.ListSystemsAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(e, "{Plugin} couldn't list its systems", instance.Name);
                notes.Add($"{instance.Name} couldn't list its systems: {e.Message}");
                claimed.UnionWith(await db.Systems.Where(s => s.PluginInstanceId == instance.Id).Select(s => s.Name).ToListAsync(ct));
                continue;
            }

            foreach (var system in systems)
            {
                if (!claimed.Add(system.Name))
                    continue;
                var known = await db.Systems.AsNoTracking().FirstOrDefaultAsync(s => s.Name == system.Name, ct);
                if (known is not null && known.PluginInstanceId == instance.Id && known.PluginSystemId == system.Id && known.DatVersion == system.Version)
                {
                    unchanged++;
                    continue;
                }
                progress.Report($"Importing {system.Name} ({system.Version})");
                try
                {
                    if (await plugin.GetSystemAsync(system.Id, ct) is not { } detail)
                        continue;
                    var result = await importer.ImportAsync(instance.Id, detail, ct);
                    imported++;
                    logger.LogInformation("Imported {System} {Version} from {Plugin}: {Dumps} dumps in {Titles} titles ({Added} new, {Removed} gone)",
                        system.Name, system.Version, instance.Name, result.Dumps, result.Titles, result.Added, result.Removed);
                }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogWarning(e, "Couldn't import {System} from {Plugin}", system.Name, instance.Name);
                    notes.Add($"{system.Name} couldn't be imported: {e.Message}");
                }
            }
        }

        var gone = await db.Systems.Where(s => !claimed.Contains(s.Name)).ToListAsync(ct);
        foreach (var system in gone)
            logger.LogInformation("Removed {System}: no catalogue lists it any more", system.Name);
        db.Systems.RemoveRange(gone);
        await db.SaveChangesAsync(ct);
        await importer.LinkBiosSystemsAsync(ct);

        progress.Report("Matching the library");
        await matcher.MatchAsync(ct);
        if (imported > 0)
            await db.OptimiseAsync(ct);
        var summary = $"{claimed.Count} systems: {imported} imported, {unchanged} unchanged" + (gone.Count > 0 ? $", {gone.Count} removed" : "");
        progress.Report(summary + string.Concat(notes.Select(n => "; " + n)));
    }
}

/// <summary>Updates the catalogue once a day, and soon after starting, so fetched DATs stay current and dropped-in ones get read.</summary>
public sealed class CatalogueSchedule(IServiceScopeFactory scopes, IOptions<RomdexOptions> options, ILogger<CatalogueSchedule> logger) : BackgroundService
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.CatalogueRefreshHours <= 0)
            return;
        var every = TimeSpan.FromHours(options.Value.CatalogueRefreshHours);
        try
        {
            await Task.Delay(FirstRun, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<RomdexDbContext>();
                    var last = await db.BackgroundTasks.Where(t => t.Kind == CatalogueRefreshTask.TaskKind)
                        .MaxAsync(t => (DateTime?)t.CreatedAt, stoppingToken);
                    if (last is null || DateTime.UtcNow - last >= every)
                        await scope.ServiceProvider.GetRequiredService<TaskQueue>().EnqueueAsync(CatalogueRefreshTask.TaskKind, null, stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "Couldn't queue the catalogue update");
                }
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
