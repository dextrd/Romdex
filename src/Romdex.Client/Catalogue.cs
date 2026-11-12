namespace Romdex.Client;

/// <summary>Where a title stands in the library. Worked out from hashes each time; never stored.</summary>
public enum TitleStatus
{
    /// <summary>A whole dump of it is in the library: every file, every disc.</summary>
    Owned,

    /// <summary>Some discs of a multi-disc release are, but not all.</summary>
    Partial,

    /// <summary>Not owned, and the library has files of it that don't verify: a disc with tracks missing or wrong, or a file with its name but other hashes.</summary>
    Bad,

    Missing,
}

/// <summary>Where one dump stands in the library.</summary>
public enum DumpStatus
{
    /// <summary>Every file verifies.</summary>
    Good,

    /// <summary>Some of its files verify, others are missing.</summary>
    Incomplete,

    /// <summary>A library file has one of its names but other hashes.</summary>
    Bad,

    Missing,
}

/// <summary>What a dump is beyond a retail game, as its name or DAT category says.</summary>
public enum DumpKind
{
    Demo,
    Beta,
    Proto,
    Pirate,
    Unlicensed,
    Aftermarket,
    Homebrew,
    Program,
    Debug,
    Bad,
    Alternate,
}

/// <summary>
/// 1G1R ("one game, one ROM") choices for a system: which of a title's dumps to prefer, and which titles to leave out
/// of what's missing.
/// </summary>
/// <param name="Regions">Regions in order of preference.</param>
/// <param name="Languages">Languages in order of preference, as two-letter codes such as <c>En</c>.</param>
/// <param name="OnlyListedRegions">Leave out titles with no dump in the listed regions, rather than only preferring them.</param>
/// <param name="Hidden">Kinds of dump left out: a title of only these isn't listed, unless it's owned.</param>
public sealed record SystemPreferences(
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Languages,
    bool OnlyListedRegions,
    IReadOnlyList<DumpKind> Hidden)
{
    public static SystemPreferences Default { get; } = new(
        ["World", "Europe", "UK", "USA", "Australia", "Japan"],
        ["En"],
        false,
        [DumpKind.Demo, DumpKind.Beta, DumpKind.Proto, DumpKind.Pirate, DumpKind.Program, DumpKind.Debug, DumpKind.Bad]);
}

/// <summary>Body and answer of <c>GET/PUT /systems/{id}/preferences</c>.</summary>
/// <param name="Own">The system has preferences of its own; otherwise it follows everyone's default.</param>
public sealed record SystemPreferencesInfo(SystemPreferences Preferences, bool Own);

/// <summary>A system with how much of it is owned, after 1G1R.</summary>
/// <param name="Titles">Titles listed after the preferences: owned, partial, bad and missing together, without ignored ones.</param>
/// <param name="Ignored">Titles someone said never to list as missing.</param>
/// <param name="Bios">BIOS and firmware files the system's DATs list, and how many of them are owned.</param>
public sealed record SystemSummary(
    Guid Id,
    string Name,
    string Source,
    string DatVersion,
    DateTime? DatDate,
    int Titles,
    int Owned,
    int Partial,
    int Bad,
    int Missing,
    int Ignored,
    int Bios,
    int BiosOwned,
    bool OwnPreferences)
{
    /// <summary>The share owned, from 0 to 1.</summary>
    public double Completeness => Titles == 0 ? 0 : Owned / (double)Titles;
}

/// <summary>One title in a system's list.</summary>
/// <param name="Preferred">The dump 1G1R picks, or the one someone chose instead.</param>
/// <param name="Have">The dump owned, when one is; the preferred one when that's owned.</param>
public sealed record TitleSummary(Guid Id, Guid SystemId, string Name, TitleStatus Status, DumpRef Preferred, DumpRef? Have, bool Ignored, int Dumps);

/// <summary>A dump, named.</summary>
public sealed record DumpRef(Guid Id, string Name, IReadOnlyList<string> Regions);

/// <summary>A page of a system's titles.</summary>
public sealed record TitlePage(IReadOnlyList<TitleSummary> Items, int Total);

/// <summary>A title with every dump the DAT lists for it.</summary>
/// <param name="ChosenDumpId">The dump someone chose to prefer over what 1G1R picks; null to let 1G1R pick.</param>
public sealed record TitleDetail(
    Guid Id,
    Guid SystemId,
    string SystemName,
    string Name,
    TitleStatus Status,
    bool Ignored,
    Guid? ChosenDumpId,
    IReadOnlyList<DumpInfo> Dumps);

/// <summary>One dump of a title, as the DAT lists it, and whether the library has it.</summary>
/// <param name="Preferred">The one 1G1R picks, or someone chose.</param>
/// <param name="Hidden">Left out by the system's preferences, such as a demo.</param>
public sealed record DumpInfo(
    Guid Id,
    string Name,
    string? Serial,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Languages,
    IReadOnlyList<DumpKind> Kinds,
    string? Disc,
    DumpStatus Status,
    bool Preferred,
    bool Hidden,
    IReadOnlyList<DumpFileInfo> Files);

/// <summary>One file of a dump, with its hashes, and where the library has it.</summary>
/// <param name="Library">The library files that verify as it.</param>
public sealed record DumpFileInfo(string Name, long Size, string? Crc32, string? Md5, string? Sha1, IReadOnlyList<LibraryPlace> Library);

/// <summary>Where in the library something is: a file, and the entry inside it for an archive or a CHD.</summary>
public sealed record LibraryPlace(Guid FileId, string Path, string? Entry);

/// <summary>A system's BIOS and firmware.</summary>
public sealed record BiosList(IReadOnlyList<DumpInfo> Items);

/// <summary>A library file, or a file inside one, that matches nothing in the catalogue, or couldn't be read.</summary>
/// <param name="LooksLike">A dump with a file of this name, whose hashes differ: likely a bad or modified dump.</param>
/// <param name="Error">Why it couldn't be read, when it couldn't.</param>
public sealed record UnknownFile(
    Guid FileId,
    string Path,
    string? Entry,
    long Size,
    string? Crc32,
    string? Md5,
    string? Sha1,
    DumpRef? LooksLike,
    string? Error);

/// <summary>A page of unknown files.</summary>
public sealed record UnknownPage(IReadOnlyList<UnknownFile> Items, int Total);

/// <summary>Body of <c>PUT /titles/{id}/preferred</c>.</summary>
/// <param name="DumpId">The dump to prefer; null to let 1G1R pick again.</param>
public sealed record ChoosePreferred(Guid? DumpId);

/// <summary>What the library holds, in total.</summary>
/// <param name="Unknown">Files, and files inside archives, that match nothing.</param>
/// <param name="Unreadable">Files that couldn't be read, such as damaged archives.</param>
public sealed record LibraryStatus(int Files, long Bytes, int Unknown, int Unreadable, DateTime? ScannedAt, DateTime? CatalogueUpdatedAt);
