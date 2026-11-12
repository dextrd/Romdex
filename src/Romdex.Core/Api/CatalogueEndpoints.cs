using Microsoft.EntityFrameworkCore;
using Romdex.Client;
using Romdex.Core.Access;
using Romdex.Core.Catalogue;
using Romdex.Core.Data;
using Romdex.Core.Library;
using Romdex.Core.Tasks;

namespace Romdex.Core.Api;

/// <summary>Systems and titles with where each stands, 1G1R preferences, and what's ignored.</summary>
public static class CatalogueEndpoints
{
    public static RouteGroupBuilder MapCatalogueEndpoints(this RouteGroupBuilder api)
    {
        var systems = api.MapGroup("/systems").WithTags("Catalogue");

        systems.MapGet("", async (LibraryViewBuilder views, CancellationToken ct) =>
                (await views.GetAllAsync(ct)).Select(v => v.Summary).ToList())
            .WithName("ListSystems").WithSummary("Every system with how much of it is owned, after 1G1R, and its DAT's version.");

        systems.MapGet("/{id:guid}", async (Guid id, LibraryViewBuilder views, CancellationToken ct) =>
                await views.GetAsync(id, ct) is { } view ? Results.Ok(view.Summary) : Results.NotFound())
            .WithName("GetSystem").Produces<SystemSummary>().Produces(StatusCodes.Status404NotFound);

        systems.MapGet("/{id:guid}/titles", async (Guid id, LibraryViewBuilder views, TitleStatus? status, string? q, bool? ignored, bool? hidden,
                bool? have, int? skip, int? take, CancellationToken ct) =>
            {
                if (await views.GetAsync(id, ct) is not { } view)
                    return Results.NotFound();
                var titles = Filter(view.Titles, status, q, have == true ? null : ignored ?? false, hidden ?? false, have ?? false);
                return Results.Ok(Page(titles.Select(t => Summary(id, t)), skip, take));
            })
            .WithName("ListTitles")
            .WithSummary("A system's titles, by name. By default the ones its preferences list and nobody ignored; ignored=true lists the ignored ones, hidden=true adds those the preferences leave out, and have=true lists only what the library has some of.")
            .Produces<TitlePage>().Produces(StatusCodes.Status404NotFound);

        systems.MapGet("/{id:guid}/bios", async (Guid id, LibraryViewBuilder views, RomdexDbContext db, CancellationToken ct) =>
                await views.GetAsync(id, ct) is { } view ? Results.Ok(new BiosList(await DumpInfosAsync(db, view.Bios, null, ct))) : Results.NotFound())
            .WithName("ListBios").WithSummary("A system's BIOS and firmware files, which many of its games need to run.")
            .Produces<BiosList>().Produces(StatusCodes.Status404NotFound);

        systems.MapGet("/{id:guid}/preferences", async (Guid id, RomdexDbContext db, Preferences preferences, CancellationToken ct) =>
            {
                if (await db.Systems.FindAsync([id], ct) is not { } system)
                    return Results.NotFound();
                var (found, own) = await preferences.GetAsync(system.Name, ct);
                return Results.Ok(new SystemPreferencesInfo(found, own));
            })
            .WithName("GetSystemPreferences").Produces<SystemPreferencesInfo>().Produces(StatusCodes.Status404NotFound);
        systems.MapPut("/{id:guid}/preferences", async (Guid id, SystemPreferences body, RomdexDbContext db, Preferences preferences, CancellationToken ct) =>
            {
                if (await db.Systems.FindAsync([id], ct) is not { } system)
                    return Results.NotFound();
                if (Preferences.Validate(body) is { Count: > 0 } errors)
                    return Results.ValidationProblem(errors);
                await preferences.SaveAsync(system.Name, body, ct);
                return Results.Ok(new SystemPreferencesInfo(body, true));
            })
            .RequireScope(Scopes.Intake).WithName("SetSystemPreferences").WithSummary("Gives a system 1G1R preferences of its own.")
            .Produces<SystemPreferencesInfo>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound);
        systems.MapDelete("/{id:guid}/preferences", async (Guid id, RomdexDbContext db, Preferences preferences, CancellationToken ct) =>
            {
                if (await db.Systems.FindAsync([id], ct) is not { } system)
                    return Results.NotFound();
                await preferences.SaveAsync(system.Name, null, ct);
                return Results.Ok(new SystemPreferencesInfo(await preferences.GetDefaultAsync(ct), false));
            })
            .RequireScope(Scopes.Intake).WithName("ResetSystemPreferences").WithSummary("Puts a system back on everyone's default preferences.")
            .Produces<SystemPreferencesInfo>().Produces(StatusCodes.Status404NotFound);

        systems.MapGet("/{id:guid}/report.csv", async (Guid id, LibraryViewBuilder views, RomdexDbContext db, CancellationToken ct) =>
                await views.GetAsync(id, ct) is { } view ? Results.Text(await IgirReport.WriteAsync(db, view, ct), "text/csv") : Results.NotFound())
            .WithName("GetSystemReport")
            .WithSummary("Every dump in the system's DAT and whether the library has it whole, in the shape of igir's report, to compare the two.")
            .Produces<string>(contentType: "text/csv").Produces(StatusCodes.Status404NotFound);

        api.MapGet("/preferences", async (Preferences preferences, CancellationToken ct) => await preferences.GetDefaultAsync(ct))
            .WithTags("Catalogue").WithName("GetDefaultPreferences").WithSummary("The 1G1R preferences of every system without its own.");
        api.MapPut("/preferences", async (SystemPreferences body, Preferences preferences, CancellationToken ct) =>
            {
                if (Preferences.Validate(body) is { Count: > 0 } errors)
                    return Results.ValidationProblem(errors);
                await preferences.SaveDefaultAsync(body, ct);
                return Results.Ok(body);
            })
            .RequireScope(Scopes.Intake).WithTags("Catalogue").WithName("SetDefaultPreferences")
            .Produces<SystemPreferences>().ProducesValidationProblem();

        var titles = api.MapGroup("/titles").WithTags("Catalogue");

        titles.MapGet("", async (LibraryViewBuilder views, TitleStatus? status, string? q, bool? ignored, bool? hidden, bool? have, int? skip, int? take,
                CancellationToken ct) =>
            {
                var found = new List<TitleSummary>();
                foreach (var view in await views.GetAllAsync(ct))
                    found.AddRange(Filter(view.Titles, status, q, have == true ? null : ignored ?? false, hidden ?? false, have ?? false)
                        .Select(t => Summary(view.Summary.Id, t)));
                return Page(found.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase), skip, take);
            })
            .WithName("ListAllTitles")
            .WithSummary("Every system's titles together, by name, filtered as a system's are.");

        titles.MapGet("/{id:guid}", async (Guid id, RomdexDbContext db, LibraryViewBuilder views, CancellationToken ct) =>
                await DetailAsync(id, db, views, ct) is { } detail ? Results.Ok(detail) : Results.NotFound())
            .WithName("GetTitle").WithSummary("A title with every dump of it, their hashes, which one 1G1R prefers, and where the library has them.")
            .Produces<TitleDetail>().Produces(StatusCodes.Status404NotFound);

        titles.MapPost("/{id:guid}/ignore", (Guid id, RomdexDbContext db, LibraryViews cache, LibraryViewBuilder views, CancellationToken ct) =>
                SetIgnoredAsync(id, true, db, cache, views, ct))
            .RequireScope(Scopes.Intake).WithName("IgnoreTitle").WithSummary("Never list the title as missing. Doing it again changes nothing.")
            .Produces<TitleDetail>().Produces(StatusCodes.Status404NotFound);
        titles.MapDelete("/{id:guid}/ignore", (Guid id, RomdexDbContext db, LibraryViews cache, LibraryViewBuilder views, CancellationToken ct) =>
                SetIgnoredAsync(id, false, db, cache, views, ct))
            .RequireScope(Scopes.Intake).WithName("UnignoreTitle").WithSummary("List the title again.")
            .Produces<TitleDetail>().Produces(StatusCodes.Status404NotFound);

        titles.MapPut("/{id:guid}/preferred", async (Guid id, ChoosePreferred body, RomdexDbContext db, LibraryViews cache, LibraryViewBuilder views, CancellationToken ct) =>
            {
                if (await db.Titles.FindAsync([id], ct) is not { } title)
                    return Results.NotFound();
                if (body.DumpId is { } dumpId && !await db.Dumps.AnyAsync(d => d.Id == dumpId && d.TitleId == id, ct))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["dumpId"] = ["That isn't a dump of this title."] });
                title.PreferredDumpId = body.DumpId;
                await db.SaveChangesAsync(ct);
                cache.Invalidate();
                return Results.Ok(await DetailAsync(id, db, views, ct));
            })
            .RequireScope(Scopes.Intake).WithName("ChoosePreferredDump").WithSummary("Prefer one dump of a title over what 1G1R picks, or let 1G1R pick again.")
            .Produces<TitleDetail>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/catalogue/search", async (string q, LibraryViewBuilder views, Guid? system, bool? have, int? skip, int? take, CancellationToken ct) =>
            {
                var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (words.Length == 0)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["Say what to search for."] });
                var found = new List<(TitleSummary Title, int Rank)>();
                foreach (var view in await views.GetAllAsync(ct))
                {
                    if (system is { } id && view.Summary.Id != id)
                        continue;
                    foreach (var title in view.Titles)
                        if ((have is null || have == (title.Status != TitleStatus.Missing)) && SearchRank(title, q.Trim(), words) is { } rank)
                            found.Add((Summary(view.Summary.Id, title), rank));
                }
                return Results.Ok(Page(found.OrderBy(f => f.Rank).ThenBy(f => f.Title.Name, StringComparer.OrdinalIgnoreCase).Select(f => f.Title), skip, take));
            })
            .WithTags("Catalogue").WithName("SearchCatalogue")
            .WithSummary("Every game the DATs list, in the library or not, whose name or a dump's name has every word searched for. The closest names come first; system narrows it to one system, have to games the library has (true) or hasn't (false).")
            .Produces<TitlePage>().ProducesValidationProblem();

        api.MapPost("/catalogue/refresh", async (TaskQueue queue, TaskTitles titles, CancellationToken ct) =>
                titles.ToInfo(await queue.EnqueueAsync(CatalogueRefreshTask.TaskKind, null, ct)))
            .RequireScope(Scopes.Scan).WithTags("Catalogue").WithName("RefreshCatalogue")
            .WithSummary("Fetches new DATs where a plugin can, and imports every DAT that changed. Asking while one is queued returns that one.");

        return api;
    }

    private static IEnumerable<TitleView> Filter(IEnumerable<TitleView> titles, TitleStatus? status, string? q, bool? ignored, bool hidden, bool have = false) =>
        titles.Where(t => (hidden || t.Shown)
                          && (!have || t.Status != TitleStatus.Missing)
                          && (ignored is null || t.Ignored == ignored)
                          && (status is null || t.Status == status)
                          && (string.IsNullOrWhiteSpace(q) || t.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
                                                           || t.Dumps.Any(d => d.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase))));

    // How close a title is to what was searched for, best first; null when it doesn't match. Its own name counts for
    // more than its dumps', which also name regions and versions.
    private static int? SearchRank(TitleView title, string q, string[] words)
    {
        bool Has(string name) => words.All(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (title.Name.Equals(q, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (title.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (Has(title.Name))
            return 2;
        return title.Dumps.Any(d => Has(d.Name)) ? 3 : null;
    }

    public static TitleSummary Summary(Guid systemId, TitleView t) =>
        new(t.Id, systemId, t.Name, t.Status, t.Preferred.Ref, t.Have?.Ref, t.Ignored, t.Dumps.Count);

    public static TitlePage Page(IEnumerable<TitleSummary> items, int? skip, int? take)
    {
        var all = items.ToList();
        return new TitlePage(all.Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 100, 1, 1000)).ToList(), all.Count);
    }

    private static async Task<IResult> SetIgnoredAsync(Guid id, bool ignored, RomdexDbContext db, LibraryViews cache, LibraryViewBuilder views, CancellationToken ct)
    {
        if (await db.Titles.FindAsync([id], ct) is not { } title)
            return Results.NotFound();
        if (ignored != title.IgnoredAt is not null)
        {
            title.IgnoredAt = ignored ? DateTime.UtcNow : null;
            await db.SaveChangesAsync(ct);
            cache.Invalidate();
        }
        return Results.Ok(await DetailAsync(id, db, views, ct));
    }

    private static async Task<TitleDetail?> DetailAsync(Guid id, RomdexDbContext db, LibraryViewBuilder views, CancellationToken ct)
    {
        var title = await db.Titles.AsNoTracking().Include(t => t.System).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (title is null || await views.GetAsync(title.SystemId, ct) is not { } view || !view.ById.TryGetValue(id, out var titleView))
            return null;
        var preferred = titleView.Dumps.Where(d => d.Release == titleView.Preferred.Release).Select(d => d.Id).ToHashSet();
        var dumps = await DumpInfosAsync(db, titleView.Dumps.OrderBy(d => !preferred.Contains(d.Id)).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase), preferred, ct);
        return new TitleDetail(title.Id, title.SystemId, title.System!.Name, title.Name, titleView.Status, titleView.Ignored, title.PreferredDumpId, dumps);
    }

    /// <summary>Dumps with their files, hashes and library places, in the order given.</summary>
    public static async Task<IReadOnlyList<DumpInfo>> DumpInfosAsync(RomdexDbContext db, IEnumerable<DumpView> dumps, IReadOnlySet<Guid>? preferred, CancellationToken ct)
    {
        var list = dumps.ToList();
        var ids = list.Select(d => d.Id).ToList();
        var files = await db.DumpFiles.AsNoTracking().Where(f => ids.Contains(f.DumpId)).ToListAsync(ct);
        var fileIds = files.Select(f => f.Id).ToList();
        var places = (await db.LibraryMatches.AsNoTracking().Where(m => fileIds.Contains(m.DumpFileId))
                .Select(m => new { m.DumpFileId, m.Content!.LibraryFileId, m.Content.File!.Path, m.Content.Entry })
                .ToListAsync(ct))
            .ToLookup(m => m.DumpFileId, m => new LibraryPlace(m.LibraryFileId, m.Path, m.Entry));
        var serials = await db.Dumps.AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Serial, ct);
        var byDump = files.ToLookup(f => f.DumpId);
        return list.Select(d => new DumpInfo(d.Id, d.Name, serials.GetValueOrDefault(d.Id), d.Regions, d.Languages, DumpKindNames.FromFlags(d.Kinds), d.Disc, d.Status,
                preferred?.Contains(d.Id) ?? false, d.Hidden,
                byDump[d.Id].OrderBy(f => f.Name, StringComparer.Ordinal)
                    .Select(f => new DumpFileInfo(f.Name, f.Size, f.Crc32, f.Md5, f.Sha1, places[f.Id].ToList())).ToList()))
            .ToList();
    }
}
