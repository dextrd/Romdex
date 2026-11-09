using System.IO.Compression;
using Romdex.Contracts.Plugins;
using Romdex.Contracts.Plugins.Index;
using Romdex.Core.Tests.Support;
using Romdex.Plugins.DatFolder;

namespace Romdex.Core.Tests;

/// <summary>The built-in catalogue: Logiqx DATs in a folder, plain or zipped.</summary>
public sealed class DatFolderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "romdex-tests", Guid.NewGuid().ToString("N"));
    private static readonly string Fixture = Path.Combine(TestRepository.Dats, "test-handheld.dat");

    public DatFolderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_header_says_which_system_and_whose_DAT_it_is()
    {
        using var stream = File.OpenRead(Fixture);
        var system = Logiqx.ReadHeader(stream)!;

        Assert.Equal("Test - Handheld", system.Id);
        Assert.Equal(DatSources.NoIntro, system.Source);
        Assert.Equal("20261101-000000", system.Version);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), system.Date);
    }

    [Fact]
    public void Dumps_keep_their_hashes_clones_serials_and_BIOS()
    {
        using var stream = File.OpenRead(Fixture);
        var dumps = Logiqx.ReadDumps(stream, CancellationToken.None);

        Assert.Equal(3, dumps.Count);
        Assert.True(dumps[0].IsBios);
        var hopper = dumps[1];
        Assert.False(hopper.IsBios);
        Assert.Null(hopper.CloneOf);
        Assert.Null(hopper.Description);
        Assert.Equal("DMG-HHPW", hopper.Serial);
        var file = Assert.Single(hopper.Files);
        Assert.Equal(32768, file.Size);
        Assert.Equal("1234abcd", file.Crc32);
        Assert.Equal("89abcdef0123456789abcdef0123456789abcdef", file.Sha1);
        Assert.Equal("Homebrew Hopper (World)", dumps[2].CloneOf);
        // Hashes are kept lower case, whatever case the DAT wrote them in.
        Assert.Equal("0a1b2c3d", dumps[0].Files[0].Crc32);
    }

    [Fact]
    public async Task The_folder_keeps_the_newest_DAT_per_system_zipped_or_not()
    {
        var plugin = Create();
        Assert.Equal(HealthStatus.Degraded, (await plugin.CheckHealthAsync(default)).Status);

        File.Copy(Fixture, Path.Combine(_folder, "old.dat"));
        var newer = File.ReadAllText(Fixture).Replace("20261101-000000", "20261108-000000").Replace("Homebrew Hopper (World) (Rev 1)", "Homebrew Hopper (World) (Rev 2)");
        using (var zip = ZipFile.Open(Path.Combine(_folder, "newer.zip"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("Test - Handheld (20261108-000000).dat").Open()))
            writer.Write(newer);
        File.WriteAllText(Path.Combine(_folder, "notes.xml"), "<notes>Not a DAT</notes>");

        var system = Assert.Single(await plugin.ListSystemsAsync(default));
        Assert.Equal("20261108-000000", system.Version);
        var detail = (await plugin.GetSystemAsync(system.Id, default))!;
        Assert.Contains(detail.Dumps, d => d.Name == "Homebrew Hopper (World) (Rev 2)");
        Assert.Null(await plugin.GetSystemAsync("Nothing - Here", default));

        var health = await plugin.CheckHealthAsync(default);
        Assert.Equal(HealthStatus.Degraded, health.Status);
        Assert.Contains("notes.xml", health.Message);
    }

    [Fact]
    public async Task A_missing_folder_is_unhealthy()
    {
        var health = await new DatFolder(Path.Combine(_folder, "nowhere")).CheckHealthAsync(default);
        Assert.Equal(HealthStatus.Unhealthy, health.Status);
    }

    private IIndexPlugin Create() => new DatFolder(_folder);
}
