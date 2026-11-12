using Romdex.Core.Catalogue;

namespace Romdex.Core.Data;

// The catalogue, filled from DATs by index plugins. The DATs define it; the core only adds its own IDs and the grouping
// into titles. Each import updates entries in place by name, so IDs, and what refers to them, survive a new DAT version.

/// <summary>A platform as one DAT describes it, such as <c>Nintendo - Game Boy</c>.</summary>
public class CatalogueSystem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The DAT's name for it. Where two index plugins have the same system, the first in order wins.</summary>
    public required string Name { get; set; }

    /// <summary>Who publishes the DAT, such as <c>no-intro</c> or <c>redump</c>.</summary>
    public required string Source { get; set; }

    public required string DatVersion { get; set; }
    public DateTime? DatDate { get; set; }

    /// <summary>The index plugin it came from, and that plugin's ID for it.</summary>
    public Guid? PluginInstanceId { get; set; }

    public required string PluginSystemId { get; set; }

    /// <summary>For a DAT of BIOS files only, such as Redump's, the system they're for: they show on its page.</summary>
    public Guid? BiosForId { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public List<Title> Titles { get; set; } = [];
    public List<Dump> Dumps { get; set; } = [];
}

/// <summary>The game as people think of it, across its regions and revisions: a parent and its clones.</summary>
public class Title
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SystemId { get; set; }
    public CatalogueSystem? System { get; set; }

    /// <summary>The parent's name without its tags, such as <c>Tetris</c>.</summary>
    public required string Name { get; set; }

    /// <summary>What the title is found by: its name, cased and spaced the same whatever the DAT does.</summary>
    public required string Key { get; set; }

    /// <summary>When someone said never to list it as missing.</summary>
    public DateTime? IgnoredAt { get; set; }

    /// <summary>The dump someone chose to prefer over what 1G1R would pick.</summary>
    public Guid? PreferredDumpId { get; set; }

    public List<Dump> Dumps { get; set; } = [];
}

/// <summary>One DAT entry: a specific region, language set and revision of a game, or a BIOS.</summary>
public class Dump
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid SystemId { get; set; }
    public Guid TitleId { get; set; }
    public Title? Title { get; set; }

    public required string Name { get; set; }
    public string? CloneOf { get; set; }
    public string? Serial { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }

    // Read from the name by DumpNames, and kept so lists don't parse every name each time.

    /// <summary>The name without its disc tag: every disc of one release shares it.</summary>
    public required string Release { get; set; }

    public string? Disc { get; set; }

    /// <summary>Comma-separated, in the name's order.</summary>
    public string Regions { get; set; } = "";

    public string Languages { get; set; } = "";
    public string Revision { get; set; } = "";
    public DumpKinds Kinds { get; set; }

    public List<DumpFile> Files { get; set; } = [];
}

/// <summary>One file of a dump: a cartridge image, or a disc's cue sheet or one of its tracks.</summary>
public class DumpFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DumpId { get; set; }
    public Dump? Dump { get; set; }

    public required string Name { get; set; }
    public long Size { get; set; }

    /// <summary>Lower-case hex, as are the others. Any may be missing where the DAT leaves it out.</summary>
    public string? Crc32 { get; set; }

    public string? Md5 { get; set; }
    public string? Sha1 { get; set; }
}
