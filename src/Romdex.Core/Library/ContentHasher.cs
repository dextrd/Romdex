using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Crc32 = System.IO.Hashing.Crc32;

namespace Romdex.Core.Library;

/// <summary>One file's hashes, as a DAT would list them. Size -1 means the reader couldn't say, as for an RVZ.</summary>
public sealed record ContentHashes(string? Entry, long Size, string Crc32, string Md5, string Sha1, HeaderlessHashes? Headerless = null);

/// <summary>The hashes of a file without its header, which is how DATs list such files.</summary>
public sealed record HeaderlessHashes(long Size, string Crc32, string Md5, string Sha1);

/// <summary>
/// Hashes a file's content in one pass: CRC32, MD5 and SHA-1 together, and, where it starts with a copier or emulator
/// header the DATs leave out, the same again without it.
/// </summary>
public static class ContentHasher
{
    private const int BufferSize = 1 << 20;

    // A copier header is 512 bytes on top of a size that's a whole number of kilobytes; only cartridge images have them.
    private static readonly HashSet<string> SnesExtensions = new(StringComparer.OrdinalIgnoreCase) { ".smc", ".sfc", ".swc", ".fig" };

    /// <summary>The header to leave out, in bytes, judging by the first bytes of the file; 0 for none.</summary>
    public static int HeaderLength(ReadOnlySpan<byte> start, long size, string? name)
    {
        if (start.Length >= 16 && start[..4].SequenceEqual("NES\x1A"u8))
            return 16;
        if (start.Length >= 16 && start[..4].SequenceEqual("FDS\x1A"u8))
            return 16;
        if (start.Length >= 64 && start[..4].SequenceEqual("LYNX"u8))
            return 64;
        if (start.Length >= 128 && start[1..10].SequenceEqual("ATARI7800"u8))
            return 128;
        if (size % 1024 == 512 && size > 512 && name is not null && SnesExtensions.Contains(Path.GetExtension(name)))
            return 512;
        return 0;
    }

    public static async Task<ContentHashes> HashAsync(Stream stream, string? entry, string? name, CancellationToken ct)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var crc = new Crc32();
        using var bareMd5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var bareSha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var bareCrc = new Crc32();

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            // The first block is read whole, so the header check sees enough of the file.
            var filled = 0;
            while (filled < 512)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(filled, BufferSize - filled), ct);
                if (read == 0)
                    break;
                filled += read;
            }
            long size = filled;
            var knownSize = stream.CanSeek ? stream.Length : -1;
            var header = HeaderLength(buffer.AsSpan(0, filled), knownSize >= 0 ? knownSize : filled, name);
            if (header > filled)
                header = 0;
            Append(buffer.AsSpan(0, filled));
            if (header > 0)
                AppendBare(buffer.AsSpan(header, filled - header));

            int count;
            while ((count = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
            {
                size += count;
                Append(buffer.AsSpan(0, count));
                if (header > 0)
                    AppendBare(buffer.AsSpan(0, count));
            }

            return new ContentHashes(entry, size, Hex(crc), Convert.ToHexStringLower(md5.GetHashAndReset()), Convert.ToHexStringLower(sha1.GetHashAndReset()),
                header > 0 ? new HeaderlessHashes(size - header, Hex(bareCrc), Convert.ToHexStringLower(bareMd5.GetHashAndReset()),
                    Convert.ToHexStringLower(bareSha1.GetHashAndReset())) : null);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        void Append(ReadOnlySpan<byte> data)
        {
            crc.Append(data);
            md5.AppendData(data);
            sha1.AppendData(data);
        }

        void AppendBare(ReadOnlySpan<byte> data)
        {
            bareCrc.Append(data);
            bareMd5.AppendData(data);
            bareSha1.AppendData(data);
        }
    }

    // DATs write a CRC32 as the number in hex, most significant byte first.
    private static string Hex(Crc32 crc) => crc.GetCurrentHashAsUInt32().ToString("x8");

    /// <summary>The CRC32 of some bytes, as a DAT writes it: for tests and fixtures.</summary>
    public static string Crc32Of(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[4];
        Crc32.Hash(data, hash);
        return BinaryPrimitives.ReadUInt32LittleEndian(hash).ToString("x8");
    }
}
