using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Romdex.Core.Data;
using Romdex.Core.Tasks;

namespace Romdex.Core.Library;

/// <summary>
/// Walks the library folder and hashes what's new or changed. A file's path, size and modified time are its cache key,
/// so a rescan only hashes what changed; files that couldn't be read are tried again each scan. It never writes to the
/// library.
/// </summary>
public sealed class LibraryScanner(RomdexDbContext db, ContainerReader reader, IOptions<RomdexOptions> options, ILogger<LibraryScanner> logger)
{
    /// <summary>Files that are never game data: they're left out rather than listed as unknown.</summary>
    public static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".nfo", ".diz", ".md", ".pdf", ".htm", ".html", ".url", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".xml", ".dat", ".json",
        ".db", ".ini", ".cfg", ".log", ".m3u", ".sav", ".srm", ".state", ".rtc", ".mcr", ".part",
    };

    /// <param name="Hashed">New and changed files hashed this time.</param>
    /// <param name="Unreadable">Files that couldn't be read, kept to try again next scan.</param>
    public sealed record Result(int Files, int Hashed, int Removed, int Unreadable, long HashedBytes);

    private sealed record Hashed(string Path, long Size, DateTime ModifiedAt, IReadOnlyList<ContentHashes>? Contents, string? Error);

    public async Task<Result> ScanAsync(IProgress<string> progress, CancellationToken ct)
    {
        var root = Path.GetFullPath(options.Value.LibraryPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"The library folder {root} doesn't exist, or Romdex can't see it.");

        progress.Report("Finding files");
        var found = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden })
            .Where(f => !Ignored.Contains(Path.GetExtension(f)) && !Path.GetFileName(f).StartsWith('.'))
            .Select(f => new FileInfo(f))
            .ToDictionary(f => Relative(root, f.FullName), StringComparer.Ordinal);
        var known = await db.LibraryFiles.AsNoTracking()
            .Select(f => new { f.Id, f.Path, f.Size, f.ModifiedAt, f.HashedAt })
            .ToDictionaryAsync(f => f.Path, StringComparer.Ordinal, ct);
        if (found.Count == 0 && known.Count > 0)
            // An empty folder is far more likely a drive that isn't mounted than a library that was deleted.
            throw new InvalidOperationException($"The library folder {root} is empty, so nothing was changed. Check it's mounted.");

        var removed = known.Keys.Where(p => !found.ContainsKey(p)).ToList();
        foreach (var chunk in removed.Chunk(500))
            await db.LibraryFiles.Where(f => chunk.Contains(f.Path)).ExecuteDeleteAsync(ct);

        var wanted = found
            .Where(f => !known.TryGetValue(f.Key, out var k) || k.HashedAt is null || k.Size != f.Value.Length || k.ModifiedAt != f.Value.LastWriteTimeUtc)
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .ToList();
        var totalBytes = wanted.Sum(f => f.Value.Length);
        logger.LogInformation("Found {Count} files in {Root}: {Wanted} to hash, {Removed} gone", found.Count, root, wanted.Count, removed.Count);

        // Hashed on several threads, and saved by this one, a few at a time.
        var results = Channel.CreateBounded<Hashed>(new BoundedChannelOptions(32) { SingleReader = true });
        var hashing = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(wanted, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Value.HashThreads), CancellationToken = ct },
                    async (file, token) =>
                    {
                        Hashed result;
                        try
                        {
                            result = new Hashed(file.Key, file.Value.Length, file.Value.LastWriteTimeUtc, await reader.ReadAsync(file.Value.FullName, token), null);
                        }
                        catch (Exception e) when (e is UnreadableFileException or IOException or UnauthorizedAccessException)
                        {
                            result = new Hashed(file.Key, file.Value.Length, file.Value.LastWriteTimeUtc, null, e.Message);
                        }
                        await results.Writer.WriteAsync(result, token);
                    });
                results.Writer.Complete();
            }
            catch (Exception e)
            {
                results.Writer.Complete(e);
            }
        }, ct);

        var ids = known.ToDictionary(k => k.Key, k => k.Value.Id, StringComparer.Ordinal);
        int done = 0, unreadable = 0;
        long bytes = 0;
        var batch = new List<Hashed>();
        await foreach (var result in results.Reader.ReadAllAsync(ct))
        {
            batch.Add(result);
            done++;
            bytes += result.Size;
            if (result.Error is not null)
            {
                unreadable++;
                logger.LogWarning("Couldn't read {Path}: {Error}", result.Path, result.Error);
            }
            if (batch.Count >= 25)
            {
                await SaveAsync(batch, ids, ct);
                batch.Clear();
                // Saved as it goes, so a paused or interrupted scan carries on where it was.
                progress.Report($"Hashing: {done:N0} of {wanted.Count:N0} files ({Size(bytes)} of {Size(totalBytes)})");
            }
        }
        await hashing;
        await SaveAsync(batch, ids, ct);
        return new Result(found.Count, done - unreadable, removed.Count, unreadable, bytes);
    }

    private async Task SaveAsync(List<Hashed> batch, Dictionary<string, Guid> known, CancellationToken ct)
    {
        if (batch.Count == 0)
            return;
        var ids = batch.Where(b => known.ContainsKey(b.Path)).Select(b => known[b.Path]).ToList();
        if (ids.Count > 0)
            await db.LibraryContents.Where(c => ids.Contains(c.LibraryFileId)).ExecuteDeleteAsync(ct);
        var files = await db.LibraryFiles.Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Path, StringComparer.Ordinal, ct);
        foreach (var result in batch)
        {
            if (!files.TryGetValue(result.Path, out var file))
            {
                file = new LibraryFile { Path = result.Path };
                db.LibraryFiles.Add(file);
            }
            file.Size = result.Size;
            file.ModifiedAt = result.ModifiedAt;
            file.Container = ContainerReader.ContainerOf(result.Path);
            file.Error = result.Error;
            file.HashedAt = result.Error is null ? DateTime.UtcNow : null;
            foreach (var content in result.Contents ?? [])
                db.LibraryContents.Add(new LibraryContent
                {
                    LibraryFileId = file.Id,
                    Entry = content.Entry,
                    Size = content.Size,
                    Crc32 = content.Crc32,
                    Md5 = content.Md5,
                    Sha1 = content.Sha1,
                    HeaderlessSize = content.Headerless?.Size,
                    HeaderlessCrc32 = content.Headerless?.Crc32,
                    HeaderlessMd5 = content.Headerless?.Md5,
                    HeaderlessSha1 = content.Headerless?.Sha1,
                });
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.#} KB",
        _ => $"{bytes} bytes",
    };
}
