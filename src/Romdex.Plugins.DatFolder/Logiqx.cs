using System.Globalization;
using System.Xml;
using Romdex.Contracts.Plugins.Index;

namespace Romdex.Plugins.DatFolder;

/// <summary>
/// Reads Logiqx XML DATs, the format No-Intro and Redump both publish: a header naming the system and version, then a
/// <c>game</c> (or <c>machine</c>) per dump with a <c>rom</c> per file.
/// </summary>
public static class Logiqx
{
    // DATs carry a DOCTYPE pointing at the Logiqx DTD; it's never fetched.
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true,
    };

    /// <summary>The system a DAT describes, from its header alone; null when it isn't a Logiqx DAT.</summary>
    public static IndexSystem? ReadHeader(Stream stream)
    {
        using var reader = XmlReader.Create(stream, Settings);
        if (!reader.ReadToFollowing("datafile") || !reader.ReadToDescendant("header"))
            return null;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var header = reader.ReadSubtree())
        {
            header.Read();
            header.Read();
            // Reading an element's text moves on to what follows it, so only read on when nothing was taken.
            while (!header.EOF)
                if (header.NodeType == XmlNodeType.Element && header.Depth == 1)
                    fields[header.Name] = header.ReadElementContentAsString();
                else
                    header.Read();
        }
        if (fields.GetValueOrDefault("name") is not { Length: > 0 } name)
            return null;
        return new IndexSystem(name, name, SourceOf(fields), fields.GetValueOrDefault("version") ?? "", DateOf(fields.GetValueOrDefault("date")));
    }

    /// <summary>Every dump a DAT lists, in its order.</summary>
    public static IReadOnlyList<IndexDump> ReadDumps(Stream stream, CancellationToken ct)
    {
        var dumps = new List<IndexDump>();
        // No-Intro's newer DATs name a clone's parent by its id rather than by name, so ids are resolved at the end.
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var parentIds = new Dictionary<int, string>();
        using var reader = XmlReader.Create(stream, Settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name is not ("game" or "machine") || reader.GetAttribute("name") is not { } name)
                continue;
            ct.ThrowIfCancellationRequested();
            var cloneOf = reader.GetAttribute("cloneof");
            if (reader.GetAttribute("id") is { Length: > 0 } id)
                ids[id] = name;
            if (string.IsNullOrEmpty(cloneOf) && reader.GetAttribute("cloneofid") is { Length: > 0 } parentId)
                parentIds[dumps.Count] = parentId;
            var isBios = reader.GetAttribute("isbios") == "yes" || name.StartsWith("[BIOS]", StringComparison.Ordinal);
            string? description = null, serial = null, category = null;
            var files = new List<IndexDumpFile>();
            using (var game = reader.ReadSubtree())
            {
                game.Read();
                game.Read();
                // As in the header, reading an element's text moves on by itself.
                while (!game.EOF)
                {
                    if (game.NodeType != XmlNodeType.Element)
                    {
                        game.Read();
                        continue;
                    }
                    switch (game.Name)
                    {
                        case "rom" when game.GetAttribute("name") is { } file:
                            files.Add(new IndexDumpFile(file, long.TryParse(game.GetAttribute("size"), CultureInfo.InvariantCulture, out var size) ? size : 0,
                                Hash(game.GetAttribute("crc")), Hash(game.GetAttribute("md5")), Hash(game.GetAttribute("sha1"))));
                            serial ??= game.GetAttribute("serial");
                            game.Read();
                            break;
                        case "description":
                            description = game.ReadElementContentAsString();
                            break;
                        case "serial":
                            serial = game.ReadElementContentAsString();
                            break;
                        case "category":
                            category ??= game.ReadElementContentAsString();
                            break;
                        default:
                            game.Read();
                            break;
                    }
                }
            }
            dumps.Add(new IndexDump(name, string.IsNullOrEmpty(cloneOf) ? null : cloneOf, files, string.IsNullOrWhiteSpace(serial) ? null : serial.Trim(),
                isBios, description == name ? null : description, string.IsNullOrWhiteSpace(category) ? null : category.Trim()));
        }
        foreach (var (index, parentId) in parentIds)
            if (ids.TryGetValue(parentId, out var parent) && parent != dumps[index].Name)
                dumps[index] = dumps[index] with { CloneOf = parent };
        return dumps;
    }

    private static string? Hash(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    // No-Intro and Redump each say who they are in the header's homepage or url.
    private static string SourceOf(Dictionary<string, string> header)
    {
        var said = $"{header.GetValueOrDefault("homepage")} {header.GetValueOrDefault("url")}";
        return said.Contains("no-intro", StringComparison.OrdinalIgnoreCase) ? DatSources.NoIntro
            : said.Contains("redump", StringComparison.OrdinalIgnoreCase) ? DatSources.Redump
            : DatSources.Other;
    }

    // Redump writes its dates with dashes in the time too, such as 2026-11-09 22-03-17.
    private static readonly string[] DateFormats = ["yyyy-MM-dd HH-mm-ss", "yyyyMMdd-HHmmss", "yyyyMMdd"];

    private static DateTimeOffset? DateOf(string? value) =>
        value is null ? null
        : DateTimeOffset.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var exact) ? exact
        : DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date
        : null;
}
