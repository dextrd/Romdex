using Romdex.Contracts.Plugins;

namespace Romdex.Core.Data;

// Work entities, filled by the core. The catalogue and library entities come with Phase 1.

/// <summary>A configured plugin.</summary>
public class PluginInstance
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The manifest ID, for example <c>romdex.index.dat-folder</c>.</summary>
    public required string PluginId { get; set; }

    public PluginType Type { get; set; }

    /// <summary>User-facing name, for example <c>No-Intro DATs</c>.</summary>
    public required string Name { get; set; }

    /// <summary>The contract version the plugin declared when it was set up.</summary>
    public required string ContractVersion { get; set; }

    /// <summary>Settings as JSON, validated against the manifest's schema.</summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>For intake plugins, the base URL of the plugin's container.</summary>
    public string? Endpoint { get; set; }

    /// <summary>For intake plugins, the manifest as the container last served it, so capabilities are known offline.</summary>
    public string? ManifestJson { get; set; }

    /// <summary>Lower runs first: which index plugin's DAT wins when two have the same system.</summary>
    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;
    public HealthStatus? Health { get; set; }
    public string? HealthMessage { get; set; }
    public DateTime? HealthCheckedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A user setting, stored as JSON under a key.</summary>
public class Setting
{
    public required string Key { get; set; }
    public required string ValueJson { get; set; }
}

public enum BackgroundTaskStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>Database-backed queue of internal work, such as library scans, so it survives restarts.</summary>
public class BackgroundTask
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>What to do, for example <c>library-scan</c>.</summary>
    public required string Kind { get; set; }

    public string? PayloadJson { get; set; }
    public BackgroundTaskStatus Status { get; set; }

    /// <summary>Human-readable progress, for example <c>Hashing 40 of 85 files</c>.</summary>
    public string? Progress { get; set; }

    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>Whether its finishing has been announced to webhooks and the event stream.</summary>
    public bool Announced { get; set; }

    /// <summary>Where it is in the queue: lower goes first.</summary>
    public double QueueOrder { get; set; } = QueueOrders.Now();

    /// <summary>Held back by the user. A paused task runs again from the start when resumed, unless it can pick up where it was.</summary>
    public bool Paused { get; set; }
}

public static class QueueOrders
{
    /// <summary>Now, in seconds: fine enough to keep tasks queued a moment apart in order.</summary>
    public static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    /// <summary>
    /// The place to put something so it comes just before <paramref name="next"/>, after <paramref name="previous"/>,
    /// or at an end of the queue when either is missing.
    /// </summary>
    public static double Between(double? previous, double? next) => (previous, next) switch
    {
        ({ } p, { } n) => (p + n) / 2,
        (null, { } n) => n - 1,
        ({ } p, null) => p + 1,
        _ => Now(),
    };
}
