namespace Romdex.Core;

/// <summary>
/// Puts Romdex back as it was first installed: the database (users, settings, plugins, the catalogue, the library's
/// hashes, tasks), the cache, pictures and logs all go. The ROM library and the DAT folder are never touched, nor
/// plugins' own data, nor the keys that sign sessions.
/// </summary>
/// <remarks>
/// Asking for it leaves a mark in the data folder and stops Romdex; the wiping happens as it starts again, before the
/// database is opened, so nothing still running holds on to what's being wiped, or keeps any of it in memory. The
/// compose file restarts Romdex when it stops.
/// </remarks>
public static class FactoryReset
{
    /// <summary>What the admin types to show they mean it.</summary>
    public const string Confirmation = "reset";

    private const string Mark = "factory-reset";

    private static readonly string[] Folders = ["cache", "logs", "artwork"];

    public static void Request(RomdexOptions options) =>
        File.WriteAllText(Path.Combine(options.DataPath, Mark), DateTime.UtcNow.ToString("O"));

    /// <summary>Wipes everything, if a reset was asked for. Returns whether it did.</summary>
    public static bool ApplyIfRequested(RomdexOptions options, string database)
    {
        var mark = Path.Combine(options.DataPath, Mark);
        if (!File.Exists(mark))
            return false;
        foreach (var file in new[] { database, database + "-wal", database + "-shm" })
            File.Delete(file);
        foreach (var folder in Folders.Select(f => Path.Combine(options.DataPath, f)))
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        File.Delete(mark);
        return true;
    }
}
