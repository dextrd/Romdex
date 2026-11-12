using Microsoft.EntityFrameworkCore;
using Romdex.Client;
using Romdex.Core.Access;
using Romdex.Core.Catalogue;
using Romdex.Core.Data;
using Romdex.Core.Library;
using Romdex.Core.Tasks;

namespace Romdex.Core.Api;

/// <summary>The library: scanning it, what's missing across systems, and what matches nothing.</summary>
public static class LibraryEndpoints
{
    public static RouteGroupBuilder MapLibraryEndpoints(this RouteGroupBuilder api)
    {
        var library = api.MapGroup("/library").WithTags("Library");

        library.MapGet("", async (RomdexDbContext db, CancellationToken ct) =>
            {
                var files = await db.LibraryFiles.CountAsync(ct);
                var bytes = files == 0 ? 0 : await db.LibraryFiles.SumAsync(f => f.Size, ct);
                var unknown = await db.LibraryContents.CountAsync(c => !c.Matches.Any(), ct);
                var unreadable = await db.LibraryFiles.CountAsync(f => f.Error != null, ct);
                var scanned = await db.BackgroundTasks.Where(t => t.Kind == LibraryScanTask.TaskKind && t.Status == BackgroundTaskStatus.Succeeded)
                    .MaxAsync(t => t.FinishedAt, ct);
                var updated = await db.Systems.MaxAsync(s => (DateTime?)s.ImportedAt, ct);
                return new LibraryStatus(files, bytes, unknown, unreadable, scanned, updated);
            })
            .WithName("GetLibraryStatus").WithSummary("What the library holds, and when it was last scanned and the catalogue last updated.");

        library.MapPost("/scan", async (TaskQueue queue, TaskTitles titles, CancellationToken ct) =>
                titles.ToInfo(await queue.EnqueueAsync(LibraryScanTask.TaskKind, null, ct)))
            .RequireScope(Scopes.Scan).WithName("ScanLibrary")
            .WithSummary("Hashes what's new or changed in the library and matches it. Asking while a scan is queued returns that one.");

        library.MapGet("/missing", async (LibraryViewBuilder views, int? skip, int? take, CancellationToken ct) =>
            {
                var missing = new List<TitleSummary>();
                foreach (var view in await views.GetAllAsync(ct))
                    missing.AddRange(view.Titles.Where(t => t.Shown && !t.Ignored && t.Status != TitleStatus.Owned)
                        .Select(t => CatalogueEndpoints.Summary(view.Summary.Id, t)));
                return CatalogueEndpoints.Page(missing, skip, take);
            })
            .WithName("ListMissing").WithSummary("Titles not owned, across every system, after each system's preferences: missing, partial and bad.");

        library.MapGet("/unknown", async (RomdexDbContext db, int? skip, int? take, CancellationToken ct) =>
            {
                var contents = db.LibraryContents.AsNoTracking().Where(c => !c.Matches.Any());
                var unreadable = db.LibraryFiles.AsNoTracking().Where(f => f.Error != null);
                var total = await contents.CountAsync(ct) + await unreadable.CountAsync(ct);
                var from = Math.Max(0, skip ?? 0);
                var size = Math.Clamp(take ?? 100, 1, 1000);
                var errors = await unreadable.OrderBy(f => f.Path).Skip(from).Take(size)
                    .Select(f => new UnknownFile(f.Id, f.Path, null, f.Size, null, null, null, null, f.Error)).ToListAsync(ct);
                var rest = size - errors.Count;
                var skipContents = Math.Max(0, from - await unreadable.CountAsync(ct));
                var found = rest == 0 ? [] : await contents.OrderBy(c => c.File!.Path).ThenBy(c => c.Entry).Skip(skipContents).Take(rest)
                    .Select(c => new { c.LibraryFileId, c.File!.Path, c.Entry, c.Size, c.Crc32, c.Md5, c.Sha1 }).ToListAsync(ct);
                var names = found.Select(c => Path.GetFileName(c.Entry ?? c.Path)).Distinct().ToList();
                var lookalikes = (await db.DumpFiles.AsNoTracking().Where(f => names.Contains(f.Name))
                        .Select(f => new { f.Name, f.Dump!.Id, DumpName = f.Dump.Name, f.Dump.Regions }).ToListAsync(ct))
                    .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var items = errors.Concat(found.Select(c =>
                {
                    var looks = lookalikes.GetValueOrDefault(Path.GetFileName(c.Entry ?? c.Path));
                    return new UnknownFile(c.LibraryFileId, c.Path, c.Entry, c.Size, c.Crc32, c.Md5, c.Sha1,
                        looks is null ? null : new DumpRef(looks.Id, looks.DumpName, looks.Regions.Length == 0 ? [] : looks.Regions.Split(',')), null);
                })).ToList();
                return new UnknownPage(items, total);
            })
            .WithName("ListUnknown")
            .WithSummary("Files that couldn't be read, then files, and files inside archives, that match nothing in the catalogue. They're never renamed or moved.");

        return api;
    }
}
