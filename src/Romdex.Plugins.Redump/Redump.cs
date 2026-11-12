using System.IO.Compression;
using System.Text.Json;
using Romdex.Contracts.Plugins;
using Romdex.Contracts.Plugins.Index;
using Romdex.Plugins.DatFolder;

namespace Romdex.Plugins.Redump;

/// <summary>Entry point for the Redump index plugin.</summary>
public sealed class RedumpFactory : IPluginFactory
{
    public const string PluginId = "romdex.index.redump";

    public PluginManifest Manifest { get; } = new()
    {
        Id = PluginId,
        Name = "Redump",
        Version = "0.1.0",
        Type = PluginType.Index,
        ContractVersion = ContractVersions.Index.ToString(),
        Description = "Fetches Redump's DATs for the disc systems you choose, and their BIOS DATs where Redump has them, and checks for new "
                      + "versions every few days.",
        Homepage = "https://redump.info",
        Capabilities = [IndexCapabilities.FetchDats],
        Access = new PluginAccess { Network = ["redump.info"] },
        MaxRequestsPerSecond = 1,
        SettingsSchema = JsonDocument.Parse("""
            {
              "type": "object",
              "required": ["systems"],
              "properties": {
                "systems": {
                  "type": "string",
                  "default": "PSX, GC",
                  "title": "Systems",
                  "description": "Redump's codes for them, comma-separated, as on redump.info/downloads: PSX is the PlayStation, PS2 the PlayStation 2, GC the GameCube, WII the Wii, SS the Saturn, DC the Dreamcast, MCD the Mega CD, PCE the PC Engine CD."
                },
                "bios": {
                  "type": "boolean",
                  "default": true,
                  "title": "BIOS DATs",
                  "description": "Fetch the BIOS DAT too, for systems Redump has one for."
                },
                "everyDays": {
                  "type": "integer",
                  "default": 7,
                  "minimum": 1,
                  "maximum": 90,
                  "title": "Check every (days)"
                },
                "baseUrl": {
                  "type": "string",
                  "default": "https://redump.info",
                  "title": "Address",
                  "description": "Where Redump is. Change it only if Redump moves again."
                }
              }
            }
            """).RootElement,
    };

    public IPlugin Create(PluginContext context)
    {
        var settings = context.Settings;
        string Text(string name, string fallback) => settings.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : fallback;
        var systems = Text("systems", "PSX, GC").Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().ToList();
        var bios = !settings.TryGetProperty("bios", out var b) || b.ValueKind != JsonValueKind.False;
        var days = settings.TryGetProperty("everyDays", out var d) && d.TryGetInt32(out var n) ? Math.Clamp(n, 1, 90) : 7;
        return new Redump(context.HttpClient, context.DataDirectory, Text("baseUrl", "https://redump.info").TrimEnd('/'), systems, bios, TimeSpan.FromDays(days));
    }
}

/// <summary>
/// Redump's DATs, kept in the plugin's own folder and read like a DAT folder. Redump hands each out as a zip, at
/// <c>/datfile/{code}</c>, and BIOS DATs at <c>/bios/{code}</c>; a download replaces what's kept only when it's a newer version.
/// </summary>
public sealed class Redump(HttpClient http, string folder, string baseUrl, IReadOnlyList<string> systems, bool bios, TimeSpan every)
    : IIndexPlugin, IDatFetcher
{
    /// <summary>The systems Redump has a BIOS DAT for.</summary>
    public static readonly IReadOnlySet<string> BiosSystems = new HashSet<string>(["PSX", "PS2", "GC", "XBOX"]);

    private readonly DatFolder.DatFolder _dats = new(folder);
    private string StatePath => Path.Combine(folder, "state.json");

    public async Task<HealthReport> CheckHealthAsync(CancellationToken cancellationToken)
    {
        if (systems.Count == 0)
            return new HealthReport(HealthStatus.Unhealthy, "No systems chosen. Add Redump's codes for them, such as PSX or GC.");
        var state = ReadState();
        var have = (await _dats.ListSystemsAsync(cancellationToken)).Count;
        if (state.Error is { } error)
            return new HealthReport(HealthStatus.Degraded, $"The last fetch failed: {error}. {Count(have)} kept from before.");
        if (state.FetchedAt is null)
        {
            // Not fetched yet: check Redump answers at all.
            using var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"{baseUrl}/downloads/"), cancellationToken);
            return response.IsSuccessStatusCode
                ? new HealthReport(HealthStatus.Healthy, $"Redump answers. Its DATs for {string.Join(", ", systems)} are fetched with the next catalogue update.")
                : new HealthReport(HealthStatus.Unhealthy, $"Redump answered {(int)response.StatusCode} at {baseUrl}.");
        }
        return new HealthReport(HealthStatus.Healthy, $"{Count(have)}, last checked {state.FetchedAt:d MMM yyyy}.");
    }

    public Task<IReadOnlyList<IndexSystem>> ListSystemsAsync(CancellationToken cancellationToken) => _dats.ListSystemsAsync(cancellationToken);

    public Task<IndexSystemDetail?> GetSystemAsync(string systemId, CancellationToken cancellationToken) => _dats.GetSystemAsync(systemId, cancellationToken);

    public async Task<IReadOnlyList<IndexSystem>> FetchAsync(CancellationToken cancellationToken)
    {
        var state = ReadState();
        var wanted = systems.Select(s => (Url: $"{baseUrl}/datfile/{s}", File: $"{s}.zip"))
            .Concat(bios ? systems.Where(BiosSystems.Contains).Select(s => (Url: $"{baseUrl}/bios/{s}", File: $"{s}-bios.zip")) : [])
            .ToList();
        var missing = wanted.Any(w => !File.Exists(Path.Combine(folder, w.File)));
        if (!missing && state.FetchedAt is { } last && DateTime.UtcNow - last < every)
            return [];

        // One that fails doesn't stop the rest; what failed is reported once all have been tried.
        var changed = new List<IndexSystem>();
        var errors = new List<Exception>();
        foreach (var (url, file) in wanted)
        {
            try
            {
                if (await FetchOneAsync(url, Path.Combine(folder, file), cancellationToken) is { } system)
                    changed.Add(system);
            }
            catch (Exception e) when (e is HttpRequestException or InvalidDataException or IOException)
            {
                errors.Add(e);
            }
        }
        WriteState(errors.Count == 0 ? new State(DateTime.UtcNow, null) : state with { Error = string.Join("; ", errors.Select(e => e.Message)) });
        if (errors.Count > 0)
            throw errors.Count == 1 ? errors[0] : new AggregateException("Some DATs couldn't be fetched: " + string.Join("; ", errors.Select(e => e.Message)), errors);
        return changed;
    }

    // Downloads one DAT; keeps it, and returns its system, only when it's a version newer than the one kept.
    private async Task<IndexSystem?> FetchOneAsync(string url, string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{url} answered {(int)response.StatusCode}");
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var fetched = ReadHeader(bytes) ?? throw new InvalidDataException($"{url} didn't send a DAT.");
        if (File.Exists(path) && ReadHeader(await File.ReadAllBytesAsync(path, ct)) is { } kept && string.CompareOrdinal(kept.Version, fetched.Version) >= 0)
            return null;
        var partial = path + ".part";
        await File.WriteAllBytesAsync(partial, bytes, ct);
        File.Move(partial, path, overwrite: true);
        return fetched;
    }

    private static IndexSystem? ReadHeader(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return null;
            using var stream = entry.Open();
            return Logiqx.ReadHeader(stream);
        }
        catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string Count(int n) => n == 1 ? "1 DAT" : $"{n} DATs";

    private sealed record State(DateTime? FetchedAt, string? Error);

    private State ReadState()
    {
        try
        {
            return File.Exists(StatePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State(null, null) : new State(null, null);
        }
        catch (JsonException)
        {
            return new State(null, null);
        }
    }

    private void WriteState(State state) => File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
}
