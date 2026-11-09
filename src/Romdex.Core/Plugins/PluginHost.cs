using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Romdex.Contracts.Plugins;
using Romdex.Contracts.Plugins.Connectors;
using Romdex.Contracts.Plugins.Index;
using Romdex.Core.Data;
using Romdex.Plugins.DatFolder;

namespace Romdex.Core.Plugins;

/// <summary>
/// Creates and caches live instances of configured in-process plugins. An instance is rebuilt when its settings
/// change; its HTTP client (and so its rate limit) lives as long as the instance.
/// </summary>
public sealed class PluginHost(
    PluginCatalog catalog,
    IOptions<RomdexOptions> options,
    IOptions<PluginHttpOptions> httpOptions,
    ILoggerFactory loggerFactory)
{
    private readonly ConcurrentDictionary<Guid, (string Settings, IPlugin Plugin)> _instances = new();

    public T Get<T>(PluginInstance instance) where T : class, IPlugin
    {
        var cached = _instances.AddOrUpdate(instance.Id,
            _ => (instance.SettingsJson, Create(instance)),
            (_, existing) => existing.Settings == instance.SettingsJson ? existing : (instance.SettingsJson, Create(instance)));
        return cached.Plugin as T
               ?? throw new InvalidOperationException($"Plugin {instance.PluginId} doesn't implement {typeof(T).Name}.");
    }

    /// <summary>
    /// The instance as it runs: with the settings of the plugin it borrows them from, if it does. Null when that
    /// plugin isn't set up, so this one can't run.
    /// </summary>
    public async Task<PluginInstance?> ResolveAsync(RomdexDbContext db, PluginInstance instance, CancellationToken ct)
    {
        if (catalog.Find(instance.PluginId)?.Manifest.UsesSettingsOf is not { } lender)
            return instance;
        var settings = await db.PluginInstances.Where(p => p.PluginId == lender).OrderBy(p => p.Priority)
            .Select(p => p.SettingsJson).FirstOrDefaultAsync(ct);
        return settings is null
            ? null
            : new PluginInstance
            {
                Id = instance.Id, PluginId = instance.PluginId, Type = instance.Type, Name = instance.Name,
                ContractVersion = instance.ContractVersion, SettingsJson = settings, Priority = instance.Priority, Enabled = instance.Enabled,
            };
    }

    private IPlugin Create(PluginInstance instance)
    {
        var plugin = catalog.Find(instance.PluginId)
                     ?? throw new InvalidOperationException($"Plugin {instance.PluginId} isn't installed.");
        if (plugin.Factory is null)
            throw new InvalidOperationException($"Plugin {instance.PluginId} can't be loaded: {plugin.Rejection}");

        var dataDirectory = Path.Combine(options.Value.DataPath, "plugins", instance.Id.ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        return plugin.Factory.Create(new PluginContext
        {
            Settings = JsonDocument.Parse(instance.SettingsJson).RootElement.Clone(),
            HttpClient = PluginHttp.CreateClient(httpOptions.Value, plugin.Manifest.MaxRequestsPerSecond),
            LoggerFactory = loggerFactory,
            DataDirectory = dataDirectory,
        });
    }
}

/// <summary>
/// The catalogues that are on, in the user's order: where two have a DAT for the same system, the first wins. There
/// may be none: Romdex then knows nothing of what exists, so nothing is missing and nothing can be verified.
/// </summary>
public sealed class IndexPlugins(RomdexDbContext db, PluginHost host)
{
    public const string DefaultsSetting = "index.defaults";

    public async Task<IReadOnlyList<(PluginInstance Instance, IIndexPlugin Plugin)>> GetAllAsync(CancellationToken cancellationToken)
    {
        var instances = await db.PluginInstances
            .Where(p => p.Type == PluginType.Index && p.Enabled)
            .OrderBy(p => p.Priority)
            .ToListAsync(cancellationToken);
        return instances.Select(i => (i, host.Get<IIndexPlugin>(i))).ToList();
    }

    /// <summary>
    /// On first run, sets up the DAT folder as the catalogue, reading the folder Romdex was given, so DATs dropped in
    /// work out of the box. Only once: removed, it stays removed.
    /// </summary>
    public static async Task EnsureDefaultAsync(RomdexDbContext db, PluginCatalog catalog, RomdexOptions options)
    {
        if (await db.Settings.FindAsync(DefaultsSetting) is not null)
            return;
        db.Settings.Add(new Setting { Key = DefaultsSetting, ValueJson = "true" });
        if (!await db.PluginInstances.AnyAsync(p => p.Type == PluginType.Index) && catalog.Find(DatFolderFactory.PluginId) is { Factory: not null } found)
        {
            var settings = SettingsSchema.Defaults(found.Manifest.SettingsSchema);
            settings[DatFolderFactory.FolderSetting] = Path.GetFullPath(options.DatPath);
            db.PluginInstances.Add(new PluginInstance
            {
                PluginId = found.Manifest.Id,
                Type = PluginType.Index,
                Name = found.Manifest.Name,
                ContractVersion = found.Manifest.ContractVersion,
                SettingsJson = settings.ToJsonString(),
            });
        }
        await db.SaveChangesAsync();
    }
}

/// <summary>The active connector, if one is set up: the enabled instance with the lowest priority.</summary>
public sealed class ConnectorPlugins(RomdexDbContext db, PluginHost host)
{
    public async Task<(PluginInstance Instance, IConnector Connector)?> GetActiveAsync(CancellationToken cancellationToken)
    {
        var instance = await db.PluginInstances
            .Where(p => p.Type == PluginType.Connector && p.Enabled)
            .OrderBy(p => p.Priority)
            .FirstOrDefaultAsync(cancellationToken);
        return instance is null ? null : (instance, host.Get<IConnector>(instance));
    }
}
