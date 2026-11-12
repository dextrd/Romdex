using Microsoft.EntityFrameworkCore;
using Romdex.Core.Data;

namespace Romdex.Core.Library;

/// <summary>
/// Matches what's in the library to the catalogue by hash alone: there's no fuzzy fallback and no confidence to weigh.
/// A dump file is matched by its SHA-1 where the DAT gives one, else its MD5, else its CRC32, and always its size. Each
/// content is tried as it is and, where it has a header, without it.
/// </summary>
public sealed class LibraryMatcher(RomdexDbContext db, LibraryViews views)
{
    // Matched again from scratch each time: it's a few indexed joins, and it means a new DAT or a rescan can't leave a
    // stale match behind.
    private static readonly string[] Statements =
    [
        "DELETE FROM LibraryMatches;",
        Insert(headerless: false),
        Insert(headerless: true),
    ];

    private static string Insert(bool headerless)
    {
        var prefix = headerless ? "Headerless" : "";
        var flag = headerless ? 1 : 0;
        // A size of -1 is one the reader couldn't give, as for an RVZ: the hashes decide alone.
        var size = headerless ? "c.HeaderlessSize" : "c.Size";
        return $"""
            INSERT OR IGNORE INTO LibraryMatches (ContentId, DumpFileId, Headerless)
            SELECT c.Id, f.Id, {flag} FROM LibraryContents c JOIN DumpFiles f ON f.Sha1 = c.{prefix}Sha1 AND (f.Size = {size} OR {size} < 0)
            UNION
            SELECT c.Id, f.Id, {flag} FROM LibraryContents c JOIN DumpFiles f ON f.Sha1 IS NULL AND f.Md5 = c.{prefix}Md5 AND (f.Size = {size} OR {size} < 0)
            UNION
            SELECT c.Id, f.Id, {flag} FROM LibraryContents c JOIN DumpFiles f
                ON f.Sha1 IS NULL AND f.Md5 IS NULL AND f.Crc32 = c.{prefix}Crc32 AND (f.Size = {size} OR {size} < 0);
            """;
    }

    public async Task MatchAsync(CancellationToken ct)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            foreach (var statement in Statements)
                await db.Database.ExecuteSqlRawAsync(statement, ct);
            await transaction.CommitAsync(ct);
        }
        views.Invalidate();
    }
}
