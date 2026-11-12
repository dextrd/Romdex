using System.Text.Json;
using Romdex.Client;
using Romdex.Core.Data;
using Romdex.Core.Library;

namespace Romdex.Core.Catalogue;

/// <summary>
/// 1G1R preferences: everyone's default, and a system's own where it has them. Kept by system name, so they survive
/// the system's DAT being replaced.
/// </summary>
public sealed class Preferences(RomdexDbContext db, LibraryViews views)
{
    private const string DefaultKey = "preferences";

    private static string KeyOf(string systemName) => $"preferences:{systemName}";

    public async Task<SystemPreferences> GetDefaultAsync(CancellationToken ct) =>
        await ReadAsync(DefaultKey, ct) ?? SystemPreferences.Default;

    /// <summary>A system's preferences, and whether they're its own rather than the default.</summary>
    public async Task<(SystemPreferences Preferences, bool Own)> GetAsync(string systemName, CancellationToken ct) =>
        await ReadAsync(KeyOf(systemName), ct) is { } own ? (own, true) : (await GetDefaultAsync(ct), false);

    public Task SaveDefaultAsync(SystemPreferences value, CancellationToken ct) => WriteAsync(DefaultKey, value, ct);

    /// <summary>Saves a system's own preferences; null goes back to the default.</summary>
    public Task SaveAsync(string systemName, SystemPreferences? value, CancellationToken ct) => WriteAsync(KeyOf(systemName), value, ct);

    /// <summary>What's wrong with some preferences, by field; empty when nothing is.</summary>
    public static Dictionary<string, string[]> Validate(SystemPreferences value)
    {
        var errors = new Dictionary<string, string[]>();
        var unknown = value.Regions.Where(r => !DumpNames.KnownRegions.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
            errors["regions"] = [$"Not regions the DATs use: {string.Join(", ", unknown)}."];
        if (value.OnlyListedRegions && value.Regions.Count == 0)
            errors["regions"] = ["List at least one region to keep to."];
        if (value.Languages.Any(l => l.Length is < 2 or > 8))
            errors["languages"] = ["Languages are codes such as En or Fr."];
        return errors;
    }

    private async Task<SystemPreferences?> ReadAsync(string key, CancellationToken ct) =>
        await db.Settings.FindAsync([key], ct) is { } setting ? JsonSerializer.Deserialize<SystemPreferences>(setting.ValueJson, RomdexJson.Options) : null;

    private async Task WriteAsync(string key, SystemPreferences? value, CancellationToken ct)
    {
        var setting = await db.Settings.FindAsync([key], ct);
        if (value is null)
        {
            if (setting is not null)
                db.Settings.Remove(setting);
        }
        else if (setting is null)
            db.Settings.Add(new Setting { Key = key, ValueJson = JsonSerializer.Serialize(value, RomdexJson.Options) });
        else
            setting.ValueJson = JsonSerializer.Serialize(value, RomdexJson.Options);
        await db.SaveChangesAsync(ct);
        views.Invalidate();
    }
}

/// <summary>Between the API's kinds of dump and the catalogue's flags.</summary>
public static class DumpKindNames
{
    public static DumpKinds ToFlags(IEnumerable<DumpKind> kinds) =>
        kinds.Aggregate(DumpKinds.None, (all, kind) => all | Enum.Parse<DumpKinds>(kind.ToString()));

    public static IReadOnlyList<DumpKind> FromFlags(DumpKinds kinds) =>
        Enum.GetValues<DumpKind>().Where(k => kinds.HasFlag(Enum.Parse<DumpKinds>(k.ToString()))).ToList();
}
