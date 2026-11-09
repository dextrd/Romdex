using System.IO.Compression;
using System.Text.Json;
using Romdex.Contracts.Plugins;
using Romdex.Contracts.Plugins.Index;

namespace Romdex.Plugins.DatFolder;

/// <summary>Entry point for the DAT folder index plugin.</summary>
public sealed class DatFolderFactory : IPluginFactory
{
    public const string PluginId = "romdex.index.dat-folder";

    /// <summary>The setting that names the folder.</summary>
    public const string FolderSetting = "folder";

    public PluginManifest Manifest { get; } = new()
    {
        Id = PluginId,
        Name = "DAT folder",
        Version = "0.1.0",
        Type = PluginType.Index,
        ContractVersion = ContractVersions.Index.ToString(),
        Description = "Reads No-Intro and Redump DATs (Logiqx XML) from a folder, plain or zipped. Download them from DAT-o-MATIC and "
                      + "redump.info and drop them in; a newer DAT for a system replaces the older one.",
        Access = new PluginAccess { Files = ["DAT folder (read)"] },
        SettingsSchema = JsonDocument.Parse("""
            {
              "type": "object",
              "required": ["folder"],
              "properties": {
                "folder": {
                  "type": "string",
                  "default": "/dats",
                  "title": "Folder",
                  "description": "Where the DATs are, as the Romdex server sees it. Subfolders are read too."
                }
              }
            }
            """).RootElement,
    };

    public IPlugin Create(PluginContext context) => new DatFolder(
        context.Settings.TryGetProperty(FolderSetting, out var folder) && folder.GetString() is { Length: > 0 } path ? path : "/dats");
}

/// <summary>
/// The DATs in a folder. Each file is one system; where two describe the same system, the newer version wins. Only
/// headers are read to list systems, so listing stays quick however big the DATs are.
/// </summary>
public sealed class DatFolder(string folder) : IIndexPlugin
{
    private static readonly string[] Extensions = [".dat", ".xml", ".zip"];

    public Task<HealthReport> CheckHealthAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
            return Task.FromResult(new HealthReport(HealthStatus.Unhealthy, $"There's no folder {folder}, or Romdex can't see it."));
        var (systems, unreadable) = Read(cancellationToken);
        var skipped = unreadable.Count == 0 ? "" : $" {Count(unreadable.Count, "file")} couldn't be read as DATs: {string.Join(", ", unreadable.Take(3))}{(unreadable.Count > 3 ? "…" : "")}.";
        return Task.FromResult(systems.Count == 0
            ? new HealthReport(HealthStatus.Degraded, $"No DATs in {folder} yet. Drop No-Intro or Redump DATs in.{skipped}")
            : new HealthReport(unreadable.Count == 0 ? HealthStatus.Healthy : HealthStatus.Degraded, $"{Count(systems.Count, "system")} in {folder}.{skipped}"));
    }

    public Task<IReadOnlyList<IndexSystem>> ListSystemsAsync(CancellationToken cancellationToken)
    {
        var (systems, _) = Read(cancellationToken);
        return Task.FromResult<IReadOnlyList<IndexSystem>>(systems.Values.Select(s => s.System).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public Task<IndexSystemDetail?> GetSystemAsync(string systemId, CancellationToken cancellationToken)
    {
        var (systems, _) = Read(cancellationToken);
        if (!systems.TryGetValue(systemId, out var found))
            return Task.FromResult<IndexSystemDetail?>(null);
        return Task.FromResult<IndexSystemDetail?>(new IndexSystemDetail(found.System, ReadDat(found.File, s => Logiqx.ReadDumps(s, cancellationToken))));
    }

    // Every DAT's header, the newest for each system, and the files that weren't DATs.
    private (Dictionary<string, (IndexSystem System, string File)> Systems, List<string> Unreadable) Read(CancellationToken ct)
    {
        var systems = new Dictionary<string, (IndexSystem System, string File)>(StringComparer.Ordinal);
        var unreadable = new List<string>();
        if (!Directory.Exists(folder))
            return (systems, unreadable);
        var files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            IndexSystem? system;
            try
            {
                system = ReadDat(file, Logiqx.ReadHeader);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
            {
                system = null;
            }
            if (system is null)
                unreadable.Add(Path.GetRelativePath(folder, file));
            else if (!systems.TryGetValue(system.Id, out var kept) || string.CompareOrdinal(system.Version, kept.System.Version) > 0)
                systems[system.Id] = (system, file);
        }
        return (systems, unreadable);
    }

    // The DAT itself, or the first DAT inside a zip, which is how DAT-o-MATIC and Redump hand them out.
    private static T ReadDat<T>(string file, Func<Stream, T> read)
    {
        if (!Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var plain = File.OpenRead(file);
            return read(plain);
        }
        using var zip = ZipFile.OpenRead(file);
        var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("The zip has no DAT in it.");
        using var stream = entry.Open();
        return read(stream);
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
