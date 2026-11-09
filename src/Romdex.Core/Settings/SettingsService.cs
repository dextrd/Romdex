using System.Text.Json;
using Romdex.Client;
using Romdex.Core.Data;

namespace Romdex.Core.Settings;

public sealed class SettingsService(RomdexDbContext db)
{
    private const string LogKey = "log";

    public Task<LogSettings> GetLogAsync(CancellationToken ct) => GetAsync<LogSettings>(LogKey, ct);

    public Task SaveLogAsync(LogSettings value, CancellationToken ct) => SaveAsync(LogKey, value, ct);

    private async Task<T> GetAsync<T>(string key, CancellationToken ct) where T : new()
    {
        var row = await db.Settings.FindAsync([key], ct);
        return row is null ? new T() : JsonSerializer.Deserialize<T>(row.ValueJson, RomdexJson.Options) ?? new T();
    }

    private async Task SaveAsync<T>(string key, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, RomdexJson.Options);
        var row = await db.Settings.FindAsync([key], ct);
        if (row is null)
            db.Settings.Add(new Setting { Key = key, ValueJson = json });
        else
            row.ValueJson = json;
        await db.SaveChangesAsync(ct);
    }
}
