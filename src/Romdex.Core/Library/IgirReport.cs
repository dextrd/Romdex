using System.Text;
using Microsoft.EntityFrameworkCore;
using Romdex.Core.Catalogue;
using Romdex.Core.Data;

namespace Romdex.Core.Library;

/// <summary>
/// A system's dumps in the shape of igir's CSV report (<c>igir report</c>), so the two can be compared row by row: one
/// row per DAT entry, FOUND when every file of it verifies, INCOMPLETE when some do, MISSING when none do. Unlike the
/// web app's statuses, nothing here is explained away: a track shared with another dump counts, as it does for igir.
/// </summary>
public static class IgirReport
{
    public static readonly string[] Columns =
    [
        "DAT Name", "Game Name", "Status", "ROM Files", "Patched", "BIOS", "Retail Release", "Unlicensed", "Debug", "Demo", "Beta", "Sample",
        "Prototype", "Program", "Aftermarket", "Homebrew", "Bad",
    ];

    public static async Task<string> WriteAsync(RomdexDbContext db, SystemView view, CancellationToken ct)
    {
        var systemId = view.Summary.Id;
        var dumps = await db.Dumps.AsNoTracking().Where(d => d.SystemId == systemId).OrderBy(d => d.Name).ToListAsync(ct);
        var files = await db.DumpFiles.AsNoTracking().Where(f => f.Dump!.SystemId == systemId).Select(f => new { f.Id, f.DumpId }).ToListAsync(ct);
        var places = (await db.LibraryMatches.AsNoTracking().Where(m => m.DumpFile!.Dump!.SystemId == systemId)
                .Select(m => new { m.DumpFileId, m.Content!.File!.Path, m.Content.Entry }).ToListAsync(ct))
            .ToLookup(m => m.DumpFileId, m => m.Entry is null ? m.Path : $"{m.Path}|{m.Entry}");
        var byDump = files.ToLookup(f => f.DumpId);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', Columns));
        foreach (var dump in dumps)
        {
            var own = byDump[dump.Id].ToList();
            var found = own.Count(f => places[f.Id].Any());
            var status = own.Count == 0 || found == own.Count ? "FOUND" : found > 0 ? "INCOMPLETE" : "MISSING";
            var paths = own.SelectMany(f => places[f.Id].Take(1)).Distinct().Order(StringComparer.Ordinal);
            var kinds = dump.Kinds;
            bool Has(DumpKinds kind) => (kinds & kind) != 0;
            var retail = (kinds & ~(DumpKinds.Alternate | DumpKinds.Unlicensed)) == 0;
            csv.AppendLine(string.Join(',', new[]
            {
                Quote(view.Summary.Name), Quote(dump.Name), status, Quote(string.Join('|', paths)), Bool(false), Bool(Has(DumpKinds.Bios)),
                Bool(retail), Bool(Has(DumpKinds.Unlicensed)), Bool(Has(DumpKinds.Debug)), Bool(Has(DumpKinds.Demo)), Bool(Has(DumpKinds.Beta)),
                Bool(false), Bool(Has(DumpKinds.Proto)), Bool(Has(DumpKinds.Program)), Bool(Has(DumpKinds.Aftermarket)), Bool(Has(DumpKinds.Homebrew)),
                Bool(Has(DumpKinds.Bad)),
            }));
        }
        return csv.ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
}
