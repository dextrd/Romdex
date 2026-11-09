namespace Romdex.Contracts.Plugins.Connectors;

/// <summary>
/// Connector contract (version 1.0): tells an app that uses the library, such as RomM, about new games, and finds
/// them there. Romdex works fully without one. Optional features are separate interfaces, each matched by a capability
/// name in the manifest; the core and API only offer actions the active connector supports.
/// </summary>
public interface IConnector : IPlugin
{
    /// <summary>
    /// Asks the app to pick up changes. <paramref name="paths"/> are library-relative paths that changed; apps that
    /// can't scan selectively may ignore them and scan everything new.
    /// </summary>
    Task TriggerScanAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the app's own ID for a library file. Returns <c>null</c> when the app doesn't have it (yet); the core asks
    /// again until it does or gives up.
    /// </summary>
    Task<string?> ResolveGameAsync(GameLookup lookup, CancellationToken cancellationToken);
}

/// <summary>What to look a game up by. Connectors should try the path first, then the hashes.</summary>
/// <param name="Path">Library-relative path of the file.</param>
/// <param name="System">The system's name in its DAT, such as <c>Nintendo - Game Boy</c>.</param>
/// <param name="Sha1">Lower-case hex SHA-1 of the file's contents, as the DAT gives it.</param>
/// <param name="Md5">Lower-case hex MD5, likewise.</param>
/// <param name="Crc32">Lower-case hex CRC32, likewise.</param>
public sealed record GameLookup(string? Path = null, string? System = null, string? Sha1 = null, string? Md5 = null, string? Crc32 = null);

/// <summary>Optional: a link that opens a game in the app. Capability <see cref="ConnectorCapabilities.LaunchLink"/>.</summary>
public interface ILaunchLink
{
    /// <summary>The address of the game's page or player in the app, by the ID <see cref="IConnector.ResolveGameAsync"/> gave.</summary>
    Uri LaunchUri(string gameId);
}

/// <summary>Capability names a connector can declare in its manifest.</summary>
public static class ConnectorCapabilities
{
    /// <summary>Implements <see cref="ILaunchLink"/>.</summary>
    public const string LaunchLink = "launch-link";

    /// <summary><see cref="IConnector.TriggerScanAsync"/> honours its paths.</summary>
    public const string SelectiveScan = "selective-scan";
}
