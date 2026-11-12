using System.IO.Compression;
using System.Net;
using System.Text;
using Romdex.Contracts.Plugins;
using Romdex.Plugins.Redump;

namespace Romdex.Core.Tests;

/// <summary>Fetching Redump's DATs: zips from /datfile and /bios, kept only when newer, checked every few days.</summary>
public sealed class RedumpTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "romdex-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeRedump _site = new();

    public RedumpTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task Fetches_each_system_and_its_BIOS_then_waits_until_its_due()
    {
        _site.Dats["/datfile/PSX"] = Zip("Sony - PlayStation", "2026-11-01 10-00-00");
        _site.Dats["/bios/PSX"] = Zip("Sony - PlayStation - BIOS Images", "2026-06-16 00-00-00");
        _site.Dats["/datfile/SS"] = Zip("Sega - Saturn", "2026-10-01 10-00-00");
        var redump = Create("PSX, ss", bios: true);

        var fetched = await redump.FetchAsync(default);
        Assert.Equal(["Sega - Saturn", "Sony - PlayStation", "Sony - PlayStation - BIOS Images"], fetched.Select(s => s.Name).Order());
        Assert.Equal(3, (await redump.ListSystemsAsync(default)).Count);
        Assert.Equal(HealthStatus.Healthy, (await redump.CheckHealthAsync(default)).Status);

        // Not due yet: nothing is asked for.
        _site.Requests.Clear();
        Assert.Empty(await redump.FetchAsync(default));
        Assert.Empty(_site.Requests);
    }

    [Fact]
    public async Task Keeps_a_download_only_when_its_a_newer_version()
    {
        _site.Dats["/datfile/PSX"] = Zip("Sony - PlayStation", "2026-11-01 10-00-00");
        Assert.Single(await Create("PSX", days: 0).FetchAsync(default));

        // The same version again changes nothing; a newer one replaces it.
        Assert.Empty(await Create("PSX", days: 0).FetchAsync(default));
        _site.Dats["/datfile/PSX"] = Zip("Sony - PlayStation", "2026-11-09 22-03-17");
        Assert.Single(await Create("PSX", days: 0).FetchAsync(default));
        Assert.Equal("2026-11-09 22-03-17", Assert.Single(await Create("PSX").ListSystemsAsync(default)).Version);
    }

    [Fact]
    public async Task A_page_that_isnt_a_DAT_fails_the_fetch_and_keeps_what_was_there()
    {
        _site.Dats["/datfile/PSX"] = Zip("Sony - PlayStation", "2026-11-01 10-00-00");
        await Create("PSX", days: 0).FetchAsync(default);
        _site.Dats["/datfile/PSX"] = Encoding.UTF8.GetBytes("<html>Maintenance</html>");

        var redump = Create("PSX", days: 0);
        await Assert.ThrowsAsync<InvalidDataException>(() => redump.FetchAsync(default));
        Assert.Single(await redump.ListSystemsAsync(default));
        Assert.Equal(HealthStatus.Degraded, (await redump.CheckHealthAsync(default)).Status);
    }

    private Redump Create(string systems, int days = 7, bool bios = false) =>
        (Redump)new RedumpFactory().Create(new PluginContext
        {
            Settings = System.Text.Json.JsonDocument.Parse($$"""{"systems":"{{systems}}","bios":{{(bios ? "true" : "false")}},"everyDays":{{Math.Max(days, 1)}},"baseUrl":"https://redump.test"}""").RootElement,
            HttpClient = new HttpClient(_site),
            LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            DataDirectory = _folder,
        }) is var plugin && days == 0 ? ForceDue(plugin) : (Redump)plugin;

    // Forgets when it last fetched, so the next fetch is due at once.
    private Redump ForceDue(IPlugin plugin)
    {
        File.Delete(Path.Combine(_folder, "state.json"));
        return (Redump)plugin;
    }

    private static byte[] Zip(string system, string version)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry($"{system} - Datfile (1) ({version}).dat").Open()))
            writer.Write($"""
                <?xml version="1.0"?>
                <datafile><header><name>{system}</name><description>{system}</description><version>{version}</version><homepage>redump.info</homepage></header>
                <game name="Made Up (USA)"><category>Games</category><rom name="Made Up (USA).cue" size="1" crc="00000001"/></game></datafile>
                """);
        return memory.ToArray();
    }

    private sealed class FakeRedump : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Dats { get; } = [];
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(Dats.TryGetValue(request.RequestUri.AbsolutePath, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
