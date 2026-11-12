using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Romdex.Core.Data;
using SharpCompress.Archives.SevenZip;

namespace Romdex.Core.Library;

/// <summary>A file that couldn't be read, with a reason people can act on.</summary>
public sealed class UnreadableFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads what a library file holds, as a DAT would describe it: the file itself, the files inside a zip or 7z, the
/// tracks of a CHD, or the disc image inside an RVZ. CHD and RVZ are read with chdman and DolphinTool, which the Docker
/// image has; reading never changes the file.
/// </summary>
public sealed partial class ContainerReader(IOptions<RomdexOptions> options)
{
    public static Container ContainerOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".zip" => Container.Zip,
        ".7z" => Container.SevenZip,
        ".chd" => Container.Chd,
        ".rvz" => Container.Rvz,
        _ => Container.Raw,
    };

    public async Task<IReadOnlyList<ContentHashes>> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            return ContainerOf(path) switch
            {
                Container.Zip => await ReadZipAsync(path, ct),
                Container.SevenZip => await ReadSevenZipAsync(path, ct),
                Container.Chd => await ReadChdAsync(path, ct),
                Container.Rvz => [await ReadRvzAsync(path, ct)],
                _ => [await ReadRawAsync(path, ct)],
            };
        }
        catch (Exception e) when (e is InvalidDataException or SharpCompress.Common.ArchiveException or SharpCompress.Common.InvalidFormatException)
        {
            throw new UnreadableFileException($"It's damaged or isn't really a {Path.GetExtension(path).TrimStart('.')} file: {e.Message}", e);
        }
    }

    private static async Task<ContentHashes> ReadRawAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan | FileOptions.Asynchronous);
        return await ContentHasher.HashAsync(stream, null, path, ct);
    }

    private static async Task<IReadOnlyList<ContentHashes>> ReadZipAsync(string path, CancellationToken ct)
    {
        var contents = new List<ContentHashes>();
        await using var zip = await ZipFile.OpenReadAsync(path, ct);
        foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')).OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            await using var stream = await entry.OpenAsync(ct);
            contents.Add(await ContentHasher.HashAsync(stream, entry.FullName, entry.FullName, ct));
        }
        return contents;
    }

    private static async Task<IReadOnlyList<ContentHashes>> ReadSevenZipAsync(string path, CancellationToken ct)
    {
        var contents = new List<ContentHashes>();
        using var archive = SevenZipArchive.OpenArchive(path);
        // Read in order through one reader: a solid archive would otherwise be unpacked again for every entry.
        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory || reader.Entry.Key is not { } key)
                continue;
            await using var stream = reader.OpenEntryStream();
            contents.Add(await ContentHasher.HashAsync(stream, key, key, ct));
        }
        return contents;
    }

    // A CHD is unpacked to a scratch folder, a file per track, and each track hashed. Its cue sheet is chdman's own, not
    // the original, so it isn't kept: a disc in a CHD counts as whole when its tracks are.
    private async Task<IReadOnlyList<ContentHashes>> ReadChdAsync(string path, CancellationToken ct)
    {
        var chdman = options.Value.ChdmanPath;
        var info = await RunAsync(chdman, ["info", "-i", path], "chdman", ct);
        var scratch = Directory.CreateTempSubdirectory("romdex-chd-");
        try
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (CdMetadata().IsMatch(info))
                await RunAsync(chdman, ["extractcd", "-i", path, "-o", Path.Combine(scratch.FullName, name + ".cue"), "--splitbin", "-f"], "chdman", ct);
            else if (info.Contains("DVD", StringComparison.Ordinal))
                await RunAsync(chdman, ["extractdvd", "-i", path, "-o", Path.Combine(scratch.FullName, name + ".iso"), "-f"], "chdman", ct);
            else
                await RunAsync(chdman, ["extractraw", "-i", path, "-o", Path.Combine(scratch.FullName, name + ".img"), "-f"], "chdman", ct);

            var contents = new List<ContentHashes>();
            foreach (var file in scratch.EnumerateFiles().Where(f => !f.Extension.Equals(".cue", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                await using var stream = file.OpenRead();
                contents.Add(await ContentHasher.HashAsync(stream, file.Name, null, ct));
            }
            return contents;
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    // DolphinTool hashes the disc image an RVZ was made from without unpacking it to disk. It doesn't say the image's
    // size, so that's left unknown and the match goes on the hashes alone.
    private async Task<ContentHashes> ReadRvzAsync(string path, CancellationToken ct)
    {
        var tool = options.Value.DolphinToolPath;
        var hashes = new Dictionary<string, string>();
        foreach (var algorithm in new[] { "crc32", "md5", "sha1" })
        {
            var output = await RunAsync(tool, ["verify", "-i", path, "-a", algorithm], "DolphinTool", ct);
            // With an algorithm named, it prints only the digest.
            var digest = output.Trim().Split('\n').LastOrDefault()?.Trim().ToLowerInvariant() ?? "";
            hashes[algorithm] = Hex().IsMatch(digest) ? digest : throw new UnreadableFileException($"DolphinTool didn't give its {algorithm}: {output.Trim()}");
        }
        return new ContentHashes(Path.ChangeExtension(Path.GetFileName(path), ".iso"), -1, hashes["crc32"].PadLeft(8, '0'), hashes["md5"], hashes["sha1"]);
    }

    private static async Task<string> RunAsync(string tool, IReadOnlyList<string> arguments, string toolName, CancellationToken ct)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new UnreadableFileException($"{toolName} couldn't be started.");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new UnreadableFileException($"Reading it needs {toolName}, which isn't installed here. The Docker image has it.", e);
        }
        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            if (process.ExitCode != 0)
                throw new UnreadableFileException($"{toolName} couldn't read it: {Last(await error, await output)}");
            return await output;
        }
    }

    private static string Last(string error, string output)
    {
        var text = (error.Length > 0 ? error : output).Trim();
        return text.Length > 300 ? "…" + text[^300..] : text;
    }

    [GeneratedRegex(@"CHT2|CHTR|CHCD|CHGD")]
    private static partial Regex CdMetadata();

    [GeneratedRegex("^[0-9a-f]{1,40}$")]
    private static partial Regex Hex();
}
