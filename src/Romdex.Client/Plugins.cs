using System.Text.Json;
using Romdex.Contracts.Plugins;

namespace Romdex.Client;

// Plugins: index plugins and connectors load into the core; intake plugins run in their own containers and are
// added by URL. Every one is configured through a form rendered from its settings schema.

/// <summary>A configured plugin.</summary>
/// <param name="PluginId">The manifest ID, for example <c>romdex.index.dat-folder</c>.</param>
/// <param name="Name">The name you gave it.</param>
/// <param name="Endpoint">For intake plugins, the container's URL.</param>
/// <param name="Priority">Lower goes first: which index plugin's DAT wins when two have the same system.</param>
/// <param name="Settings">Its settings, with secrets left out.</param>
/// <param name="Manifest">What the plugin says about itself, including its settings schema and capabilities.</param>
public sealed record PluginInfo(
    Guid Id,
    string PluginId,
    PluginType Type,
    string Name,
    string? Endpoint,
    int Priority,
    bool Enabled,
    HealthStatus? Health,
    string? HealthMessage,
    DateTime? HealthCheckedAt,
    JsonElement Settings,
    PluginManifest? Manifest);

/// <summary>A plugin that can be set up: loaded into the core, or, for intake plugins, any container you point it at.</summary>
/// <param name="Problem">Why it can't be set up, if it can't.</param>
public sealed record AvailablePlugin(PluginManifest Manifest, string Source, string? Problem);

/// <summary>Body of <c>POST /plugins</c>.</summary>
/// <param name="PluginId">An in-process plugin from <c>GET /plugins/available</c>; leave out for an intake plugin.</param>
/// <param name="Endpoint">An intake plugin's container URL, for example <c>http://intake-inbox:8080</c>.</param>
/// <param name="Name">Defaults to the plugin's name.</param>
/// <param name="Settings">Defaults to the schema's defaults.</param>
public sealed record AddPlugin(string? PluginId = null, string? Endpoint = null, string? Name = null, JsonElement? Settings = null);

/// <summary>Body of <c>PUT /plugins/{id}</c>. Anything left out stays as it is; a secret left out of the settings keeps its value.</summary>
public sealed record UpdatePlugin(string? Name = null, JsonElement? Settings = null, bool? Enabled = null, string? Endpoint = null);

/// <summary>Body of <c>POST /plugins/{id}/checks/{check}</c>.</summary>
/// <param name="Settings">Settings to check with, as in the settings form before saving; left out, the saved ones.</param>
public sealed record RunPluginCheck(JsonElement? Settings = null);

/// <summary>What a plugin's check found.</summary>
public sealed record PluginCheckResult(bool Passed, string Message);

/// <summary>Body of <c>PUT /plugins/order</c>: the instances of one type, in the order to use them.</summary>
public sealed record PluginOrder(IReadOnlyList<Guid> Ids);
