using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Romdex.Contracts.Plugins.Index;
using Romdex.Core.Data;

namespace Romdex.Core.Catalogue;

/// <summary>
/// Puts one system's DAT into the catalogue. Entries are updated in place by name, so a new DAT version keeps every ID,
/// and with them what people ignored or preferred; entries the DAT no longer has go, and titles left empty go with them.
/// </summary>
public sealed partial class CatalogueImporter(RomdexDbContext db)
{
    /// <param name="Added">Dumps new to the catalogue.</param>
    /// <param name="Removed">Dumps the DAT no longer lists.</param>
    public sealed record Result(int Dumps, int Added, int Removed, int Titles);

    public async Task<Result> ImportAsync(Guid? pluginInstanceId, IndexSystemDetail detail, CancellationToken ct)
    {
        var source = detail.System;
        var system = await db.Systems.FirstOrDefaultAsync(s => s.Name == source.Name, ct);
        if (system is null)
        {
            system = new CatalogueSystem { Name = source.Name, Source = source.Source, DatVersion = source.Version, PluginSystemId = source.Id };
            db.Systems.Add(system);
        }
        system.Source = source.Source;
        system.DatVersion = source.Version;
        system.DatDate = source.Date?.UtcDateTime;
        system.PluginInstanceId = pluginInstanceId;
        system.PluginSystemId = source.Id;
        system.ImportedAt = DateTime.UtcNow;

        var titles = await db.Titles.Where(t => t.SystemId == system.Id).ToDictionaryAsync(t => t.Key, ct);
        var dumps = await db.Dumps.Where(d => d.SystemId == system.Id).Include(d => d.Files).ToDictionaryAsync(d => d.Name, ct);

        // A clone joins its parent's title; anything else is grouped by its name without tags, since Redump and
        // No-Intro's standard DATs don't say which entries are clones.
        var names = detail.Dumps.GroupBy(d => d.Name).Select(g => g.First()).ToDictionary(d => d.Name, d => DumpNames.Parse(d.Name, d.Category, d.IsBios));
        string TitleOf(IndexDump dump) =>
            dump.CloneOf is { } parent && names.TryGetValue(parent, out var parentName) ? parentName.Title : names[dump.Name].Title;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int added = 0;
        foreach (var entry in detail.Dumps)
        {
            ct.ThrowIfCancellationRequested();
            if (!seen.Add(entry.Name))
                continue;
            var name = names[entry.Name];
            var titleName = TitleOf(entry);
            var key = DumpNames.TitleKey(titleName);
            if (!titles.TryGetValue(key, out var title))
            {
                title = new Title { SystemId = system.Id, Name = titleName, Key = key };
                titles[key] = title;
                db.Titles.Add(title);
            }
            // The parent names the title, where there is one.
            else if (entry.CloneOf is null && title.Name != titleName)
                title.Name = titleName;

            if (!dumps.TryGetValue(entry.Name, out var dump))
            {
                dump = new Dump { SystemId = system.Id, Name = entry.Name, Release = name.Release };
                dumps[entry.Name] = dump;
                db.Dumps.Add(dump);
                added++;
            }
            dump.TitleId = title.Id;
            dump.CloneOf = entry.CloneOf;
            dump.Serial = entry.Serial;
            dump.Description = entry.Description;
            dump.Category = entry.Category;
            dump.Release = name.Release;
            dump.Disc = name.Disc;
            dump.Regions = string.Join(',', name.Regions);
            dump.Languages = string.Join(',', name.Languages);
            dump.Revision = name.Revision;
            dump.Kinds = name.Kinds;
            UpdateFiles(dump, entry.Files);
        }

        var gone = dumps.Values.Where(d => !seen.Contains(d.Name)).ToList();
        db.Dumps.RemoveRange(gone);
        var used = dumps.Values.Where(d => seen.Contains(d.Name)).Select(d => d.TitleId).ToHashSet();
        db.Titles.RemoveRange(titles.Values.Where(t => !used.Contains(t.Id) && db.Entry(t).State != EntityState.Added));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new Result(seen.Count, added, gone.Count, used.Count);
    }

    private void UpdateFiles(Dump dump, IReadOnlyList<IndexDumpFile> files)
    {
        var existing = dump.Files.ToDictionary(f => f.Name, StringComparer.Ordinal);
        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (!kept.Add(file.Name))
                continue;
            if (!existing.TryGetValue(file.Name, out var row))
            {
                row = new DumpFile { DumpId = dump.Id, Name = file.Name };
                dump.Files.Add(row);
            }
            row.Size = file.Size;
            row.Crc32 = file.Crc32;
            row.Md5 = file.Md5;
            row.Sha1 = file.Sha1;
        }
        foreach (var gone in existing.Values.Where(f => !kept.Contains(f.Name)))
            db.DumpFiles.Remove(gone);
    }

    /// <summary>
    /// Points DATs of BIOS files only, such as Redump's <c>Sony - PlayStation - BIOS Images</c>, at the system they're
    /// for, by name.
    /// </summary>
    public async Task LinkBiosSystemsAsync(CancellationToken ct)
    {
        var systems = await db.Systems.ToListAsync(ct);
        var byName = systems.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var system in systems)
            system.BiosForId = BiosDat().Match(system.Name) is { Success: true } match && byName.TryGetValue(match.Groups[1].Value.Trim(), out var parent)
                                                                                      && parent.Id != system.Id
                ? parent.Id
                : null;
        await db.SaveChangesAsync(ct);
    }

    [GeneratedRegex(@"^(.+?)\s+-\s+BIOS\b", RegexOptions.IgnoreCase)]
    private static partial Regex BiosDat();
}
