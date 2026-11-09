namespace Romdex.Contracts.Plugins.Index;

/// <summary>
/// Index plugin contract (version 1.0): answers what exists, as preservation DATs (No-Intro, Redump) describe it.
/// Caching and rate limiting live in the core, so implementations should read their source directly and not cache
/// on their own.
/// </summary>
/// <remarks>
/// Methods return <c>null</c> when something doesn't exist and throw for anything else, such as an unreadable file or
/// an upstream error; <see cref="IndexUnavailableException"/> says to try again later. Optional features are separate
/// interfaces (<see cref="IDatFetcher"/>, <see cref="IHashLookup"/>), each matched by a capability name in the manifest.
/// </remarks>
public interface IIndexPlugin : IPlugin
{
    /// <summary>Every system the plugin has a DAT for, with the DAT's version, without its dumps.</summary>
    Task<IReadOnlyList<IndexSystem>> ListSystemsAsync(CancellationToken cancellationToken);

    /// <summary>A system's dumps and their files, as its DAT lists them.</summary>
    /// <param name="systemId">An <see cref="IndexSystem.Id"/> from <see cref="ListSystemsAsync"/>.</param>
    /// <param name="cancellationToken">Stops the load.</param>
    Task<IndexSystemDetail?> GetSystemAsync(string systemId, CancellationToken cancellationToken);
}

/// <summary>A platform as one DAT describes it, such as <c>Nintendo - Game Boy</c>.</summary>
/// <param name="Id">The plugin's own ID for it, stable across DAT versions, such as the DAT's name.</param>
/// <param name="Name">The DAT's name for the system.</param>
/// <param name="Source">Who publishes the DAT: see <see cref="DatSources"/>.</param>
/// <param name="Version">The DAT's version, as its header gives it, such as <c>20260930-102438</c>.</param>
/// <param name="Date">When the DAT was made, if its header says.</param>
/// <param name="Dumps">How many dumps it lists, if the plugin knows without loading them.</param>
public sealed record IndexSystem(string Id, string Name, string Source, string Version, DateTimeOffset? Date = null, int? Dumps = null);

/// <summary>A system with its dumps.</summary>
public sealed record IndexSystemDetail(IndexSystem System, IReadOnlyList<IndexDump> Dumps);

/// <summary>
/// One DAT entry: a specific region, language set and revision of a game, or a BIOS. Clones name their parent, which
/// groups a game's regions and revisions into one title.
/// </summary>
/// <param name="Name">The entry's name, such as <c>Tetris (World) (Rev 1)</c>.</param>
/// <param name="Files">Its files: one for a cartridge, a cue sheet and tracks for a disc.</param>
/// <param name="CloneOf">The parent entry's name, for a clone; null for a parent or a game on its own.</param>
/// <param name="Serial">The product serial, where the DAT gives one.</param>
/// <param name="IsBios">A BIOS or firmware, which other dumps of the system may need to run.</param>
/// <param name="Description">The DAT's description of it, where that differs from its name.</param>
public sealed record IndexDump(string Name, string? CloneOf, IReadOnlyList<IndexDumpFile> Files, string? Serial = null, bool IsBios = false,
    string? Description = null);

/// <summary>One file of a dump: a cartridge image, or a disc's cue sheet or one of its tracks.</summary>
/// <param name="Name">The file's name in the DAT.</param>
/// <param name="Size">Its size in bytes, without any header the DAT leaves out.</param>
/// <param name="Crc32">Lower-case hex, as are the other hashes. Any may be missing where the DAT leaves it out.</param>
/// <param name="Md5">Its MD5.</param>
/// <param name="Sha1">Its SHA-1.</param>
public sealed record IndexDumpFile(string Name, long Size, string? Crc32, string? Md5, string? Sha1);

/// <summary>Who publishes a DAT.</summary>
public static class DatSources
{
    /// <summary>No-Intro: cartridges and other non-disc media.</summary>
    public const string NoIntro = "no-intro";

    /// <summary>Redump: discs.</summary>
    public const string Redump = "redump";

    /// <summary>Anything else the plugin reads.</summary>
    public const string Other = "other";
}

/// <summary>Optional: fetching new DAT versions. Capability <see cref="IndexCapabilities.FetchDats"/>.</summary>
public interface IDatFetcher
{
    /// <summary>Fetches newer DATs where the source has them. Returns the systems whose DAT changed.</summary>
    Task<IReadOnlyList<IndexSystem>> FetchAsync(CancellationToken cancellationToken);
}

/// <summary>Optional: titles and cover art by a dump's hash. Capability <see cref="IndexCapabilities.HashLookup"/>.</summary>
public interface IHashLookup
{
    /// <summary>What the source knows about the dump with this file, by its hashes. Null when it knows nothing.</summary>
    Task<HashLookupResult?> LookUpAsync(IndexDumpFile file, CancellationToken cancellationToken);
}

/// <param name="Title">The game's name, as people know it.</param>
/// <param name="CoverUrl">A picture of its box or cover, if there is one.</param>
/// <param name="Ids">The source's own IDs, such as an IGDB ID, by scheme.</param>
public sealed record HashLookupResult(string? Title, string? CoverUrl, IReadOnlyDictionary<string, string> Ids);

/// <summary>Capability names an index plugin can declare in its manifest.</summary>
public static class IndexCapabilities
{
    /// <summary>Implements <see cref="IDatFetcher"/>.</summary>
    public const string FetchDats = "fetch-dats";

    /// <summary>Implements <see cref="IHashLookup"/>.</summary>
    public const string HashLookup = "hash-lookup";
}
