using System.IO.Compression;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using Romdex.Core.Library;

namespace Romdex.Core.Tests.Support;

/// <summary>
/// A catalogue and library made up for a test: dumps of random bytes, a Logiqx DAT that lists them, and library files
/// written from them. Nothing in it exists anywhere else.
/// </summary>
public sealed class MadeUpDats : IDisposable
{
    private readonly Dictionary<string, List<(string Name, string? CloneOf, string? Category, List<(string Name, byte[] Data)> Files)>> _systems = [];

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "romdex-tests", Guid.NewGuid().ToString("N"));
    public string Dats => Path.Combine(Root, "dats");
    public string Roms => Path.Combine(Root, "roms");
    public string Data => Path.Combine(Root, "data");

    public MadeUpDats()
    {
        Directory.CreateDirectory(Dats);
        Directory.CreateDirectory(Roms);
    }

    /// <summary>Adds a dump to a system's DAT, with a file of random bytes for each extension given, and returns its files.</summary>
    public IReadOnlyList<(string Name, byte[] Data)> Dump(string system, string name, string[] extensions, string? cloneOf = null, string? category = null, int size = 4096)
    {
        if (!_systems.TryGetValue(system, out var dumps))
            _systems[system] = dumps = [];
        var files = extensions.Select((ext, i) => (Name: extensions.Length > 2 && ext == ".bin" ? $"{name} (Track {i}){ext}" : name + ext, Data: RandomNumberGenerator.GetBytes(size + i))).ToList();
        dumps.Add((name, cloneOf, category, files));
        return files;
    }

    public void WriteDats(string version = "20261101-000000")
    {
        foreach (var (system, dumps) in _systems)
        {
            var xml = new StringBuilder();
            xml.AppendLine("<?xml version=\"1.0\"?>").AppendLine("<datafile>")
                .AppendLine($"<header><name>{Escape(system)}</name><description>{Escape(system)}</description><version>{version}</version><homepage>No-Intro</homepage></header>");
            foreach (var (name, cloneOf, category, files) in dumps)
            {
                xml.Append($"<game name=\"{Escape(name)}\"").Append(cloneOf is null ? "" : $" cloneof=\"{Escape(cloneOf)}\"").AppendLine(">");
                if (category is not null)
                    xml.AppendLine($"<category>{Escape(category)}</category>");
                xml.AppendLine($"<description>{Escape(name)}</description>");
                foreach (var (file, data) in files)
                    xml.AppendLine($"<rom name=\"{Escape(file)}\" size=\"{data.Length}\" crc=\"{ContentHasher.Crc32Of(data)}\" md5=\"{Convert.ToHexStringLower(MD5.HashData(data))}\" sha1=\"{Convert.ToHexStringLower(SHA1.HashData(data))}\"/>");
                xml.AppendLine("</game>");
            }
            xml.AppendLine("</datafile>");
            File.WriteAllText(Path.Combine(Dats, system + ".dat"), xml.ToString());
        }
    }

    /// <summary>Writes a file into the library, under a folder of its own.</summary>
    public string Put(string relative, byte[] data)
    {
        var path = Path.Combine(Roms, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>Writes files into a zip in the library.</summary>
    public void Zip(string relative, params (string Name, byte[] Data)[] files)
    {
        var path = Path.Combine(Roms, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, data) in files)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(data);
        }
    }

    private static string Escape(string value) => SecurityElement.Escape(value);

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
