namespace Romdex.Client;

/// <summary>
/// Answer to <c>GET /setup</c>: how far a new install has got with the steps the web app walks the admin through after
/// making their account.
/// </summary>
/// <param name="Done">The steps were finished or skipped.</param>
/// <param name="LibraryPath">The ROM library, as the core sees it.</param>
/// <param name="LibraryReadable">The core can list the library folder.</param>
/// <param name="LibraryFiles">Files found in it, counting no further than <see cref="CountLimit"/>.</param>
/// <param name="DatPath">The folder DATs are dropped into, as the core sees it.</param>
/// <param name="DatFiles">DAT files in it, counting no further than <see cref="CountLimit"/>.</param>
/// <param name="Catalogue">An index plugin has been added, so Romdex knows what exists.</param>
/// <param name="Connector">An app such as RomM has been connected.</param>
public sealed record SetupStatus(bool Done, string LibraryPath, bool LibraryReadable, int LibraryFiles, string DatPath, int DatFiles, bool Catalogue,
    bool Connector)
{
    public const int CountLimit = 10_000;
}

/// <summary>Body of <c>POST /setup/reset</c>.</summary>
/// <param name="Confirm">"reset", to show the admin means it.</param>
public sealed record FactoryResetRequest(string? Confirm);

/// <summary>Answer to <c>GET /version</c>.</summary>
/// <param name="Version">The server's version, with the commit it was built from after a <c>+</c> where known.</param>
/// <param name="Contracts">The plugin contract versions it supports, by kind of plugin.</param>
public sealed record ServerVersion(string Version, string ApiVersion, IReadOnlyDictionary<string, string> Contracts);
