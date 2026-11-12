using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Romdex.Client;
using Romdex.Contracts.Plugins;
using Romdex.Core.Access;
using Romdex.Core.Data;
using Romdex.Core.Plugins;
using Romdex.Core.Tests.Support;
using Romdex.Plugins.DatFolder;

namespace Romdex.Core.Tests;

/// <summary>The first run's steps, everyone's look, the catalogue it starts with, and resetting it.</summary>
public sealed class SetupTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "romdex-tests", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public SetupTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Romdex:DataPath", _dataPath).UseSetting("Romdex:CatalogueRefreshHours", "0")
            .UseSetting("Romdex:LibraryPath", Path.Combine(_dataPath, "roms"))
            .UseSetting("Romdex:DatPath", TestRepository.Dats));
    }

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataPath))
            Directory.Delete(_dataPath, recursive: true);
    }

    [Fact]
    public async Task Setup_shows_until_the_admin_finishes_it()
    {
        var anyone = new RomdexClient(_factory.CreateClient());
        Assert.False((await anyone.GetAuthStatusAsync()).SetUp);

        var admin = new RomdexClient(_factory.CreateSignedInClient());
        var status = await admin.GetAuthStatusAsync();
        Assert.True(status.SetUp);
        Assert.False(status.SetupFinished);

        await admin.FinishSetupAsync();
        Assert.True((await admin.GetAuthStatusAsync()).SetupFinished);
    }

    [Fact]
    public async Task Setup_says_what_it_found_in_the_folders()
    {
        Directory.CreateDirectory(Path.Combine(_dataPath, "roms", "Test - Handheld"));
        File.WriteAllText(Path.Combine(_dataPath, "roms", "Test - Handheld", "Homebrew Hopper (World).gb"), "x");
        var admin = new RomdexClient(_factory.CreateSignedInClient());

        var setup = await admin.GetSetupAsync();
        Assert.True(setup.LibraryReadable);
        Assert.Equal(1, setup.LibraryFiles);
        Assert.Equal(1, setup.DatFiles);
        Assert.True(setup.Catalogue);
    }

    [Fact]
    public async Task The_DAT_folder_is_the_catalogue_from_the_start_and_passes_its_test()
    {
        var admin = new RomdexClient(_factory.CreateSignedInClient());
        var index = Assert.Single(await admin.ListPluginsAsync());
        Assert.Equal(DatFolderFactory.PluginId, index.PluginId);
        Assert.Equal(PluginType.Index, index.Type);

        var tested = await admin.TestPluginAsync(index.Id);
        Assert.Equal(HealthStatus.Healthy, tested.Health);
        Assert.Contains("1 system", tested.HealthMessage);

        // Removed, it stays removed.
        await admin.RemovePluginAsync(index.Id);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RomdexDbContext>();
            await IndexPlugins.EnsureDefaultAsync(db, scope.ServiceProvider.GetRequiredService<PluginCatalog>(), new RomdexOptions { DatPath = TestRepository.Dats });
            Assert.DoesNotContain(db.PluginInstances, p => p.Type == PluginType.Index);
        }
        Assert.False((await admin.GetSetupAsync()).Catalogue);

        // And can be added again.
        var added = await admin.AddPluginAsync(new AddPlugin(DatFolderFactory.PluginId));
        Assert.Equal(PluginType.Index, added.Type);
    }

    [Fact]
    public async Task Everyone_has_the_default_look_until_they_choose_their_own()
    {
        var admin = new RomdexClient(_factory.CreateSignedInClient());
        await admin.SetDefaultAppearanceAsync(new Appearance(ThemeColor.Purple, ThemeMode.Dark));
        var status = await admin.GetAuthStatusAsync();
        Assert.Equal(ThemeColor.Purple, status.Appearance?.Color);
        Assert.False(status.OwnAppearance);

        await admin.SetAppearanceAsync(new Appearance(ThemeColor.Green));
        status = await admin.GetAuthStatusAsync();
        Assert.Equal(ThemeColor.Green, status.Appearance?.Color);
        Assert.True(status.OwnAppearance);

        // A change to everyone's look doesn't reach someone with their own.
        await admin.SetDefaultAppearanceAsync(new Appearance(ThemeColor.Amber));
        Assert.Equal(ThemeColor.Green, (await admin.GetAuthStatusAsync()).Appearance?.Color);

        await admin.ClearAppearanceAsync();
        status = await admin.GetAuthStatusAsync();
        Assert.Equal(ThemeColor.Amber, status.Appearance?.Color);
        Assert.False(status.OwnAppearance);
    }

    [Fact]
    public async Task Only_the_admin_can_reset_Romdex_and_they_type_reset_to_do_it()
    {
        var admin = new RomdexClient(_factory.CreateSignedInClient());
        await admin.CreateUserAsync(new CreateUser("manager", "managing password", [Scopes.Read, Scopes.Settings]));
        var manager = _factory.CreateClient();
        (await manager.PostAsJsonAsync("api/v1/auth/login", new SignIn("manager", "managing password"))).EnsureSuccessStatusCode();
        var mark = Path.Combine(_dataPath, "factory-reset");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsJsonAsync("api/v1/setup/reset", new FactoryResetRequest("reset"), RomdexJson.Options)).StatusCode);
        var wrong = await Assert.ThrowsAsync<RomdexApiException>(() => admin.FactoryResetAsync("yes"));
        Assert.Contains("reset", wrong.Message);
        Assert.False(File.Exists(mark));

        await admin.FactoryResetAsync("Reset");
        Assert.True(File.Exists(mark));
    }

    [Fact]
    public void A_reset_wipes_what_Romdex_holds_but_never_the_games_or_DATs()
    {
        var root = Path.Combine(_dataPath, "reset");
        var options = new RomdexOptions
        {
            DataPath = Path.Combine(root, "data"),
            LibraryPath = Path.Combine(root, "roms"),
            DatPath = Path.Combine(root, "dats"),
        };
        var database = Path.Combine(options.DataPath, "romdex.db");
        string Make(params string[] parts)
        {
            var path = Path.Combine([root, .. parts]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
            return path;
        }
        var kept = new[]
        {
            Make("roms", "Test - Handheld", "Homebrew Hopper (World).gb"),
            Make("dats", "test-handheld.dat"),
            Make("data", "plugins", "abc", "state.json"),
            Make("data", "keys", "key.xml"),
        };
        var wiped = new[]
        {
            Make("data", "romdex.db"), Make("data", "romdex.db-wal"), Make("data", "cache", "hashes.json"),
            Make("data", "logs", "2026-11-08.log"), Make("data", "artwork", "a.png"),
        };

        Assert.False(FactoryReset.ApplyIfRequested(options, database));
        Assert.All(wiped, f => Assert.True(File.Exists(f)));

        FactoryReset.Request(options);
        Assert.True(FactoryReset.ApplyIfRequested(options, database));
        Assert.All(kept, f => Assert.True(File.Exists(f), f));
        Assert.All(wiped, f => Assert.False(File.Exists(f), f));
        Assert.False(FactoryReset.ApplyIfRequested(options, database));
    }
}
