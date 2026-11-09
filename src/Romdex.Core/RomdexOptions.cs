namespace Romdex.Core;

/// <summary>Paths the core works with, bound from the <c>Romdex</c> configuration section.</summary>
public class RomdexOptions
{
    public const string Section = "Romdex";

    /// <summary>Config and database: settings, catalogue, hash cache and jobs.</summary>
    public string DataPath { get; set; } = "data";

    /// <summary>The real ROM library. The core only reads it; the igir worker writes it.</summary>
    public string LibraryPath { get; set; } = "roms";

    /// <summary>Arrived dumps awaiting verification, written by intake plugins.</summary>
    public string StagingPath { get; set; } = "staging";

    /// <summary>DAT files, dropped in by hand or fetched by index plugins.</summary>
    public string DatPath { get; set; } = "dats";

    /// <summary>Where in-process plugin assemblies (index plugins and connectors) are loaded from.</summary>
    public string PluginsPath { get; set; } = "plugins";

    /// <summary>
    /// Clears the admin's password when the core starts, so the next visit sets a new one: for a forgotten password.
    /// Other users, API keys and everything else are kept. Turn it off again afterwards.
    /// </summary>
    public bool ResetLogin { get; set; }
}
