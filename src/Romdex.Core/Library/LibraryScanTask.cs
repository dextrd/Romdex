using Romdex.Core.Data;
using Romdex.Core.Tasks;

namespace Romdex.Core.Library;

/// <summary>Hashes what's new or changed in the library, then matches it all against the catalogue.</summary>
public sealed class LibraryScanTask(LibraryScanner scanner, LibraryMatcher matcher, RomdexDbContext db) : ITaskHandler
{
    public const string TaskKind = "library-scan";

    public string Kind => TaskKind;

    public string Title(string? payload) => "Library scan";

    public async Task RunAsync(BackgroundTask task, IProgress<string> progress, CancellationToken ct)
    {
        var scan = await scanner.ScanAsync(progress, ct);
        progress.Report("Matching");
        await matcher.MatchAsync(ct);
        if (scan.Hashed > 0 || scan.Removed > 0)
            await db.OptimiseAsync(ct);
        var notes = new List<string>();
        if (scan.Removed > 0)
            notes.Add($"{Count(scan.Removed, "file")} gone");
        if (scan.Unreadable > 0)
            notes.Add($"{Count(scan.Unreadable, "file")} couldn't be read");
        progress.Report($"{Count(scan.Files, "file")}: {scan.Hashed:N0} hashed ({LibraryScanner.Size(scan.HashedBytes)})" + string.Concat(notes.Select(n => "; " + n)));
    }

    private static string Count(int n, string one) => n == 1 ? $"1 {one}" : $"{n:N0} {one}s";
}
