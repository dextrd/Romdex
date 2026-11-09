using System.Text.Json;
using Romdex.Contracts.Plugins;
using Romdex.Contracts.Plugins.Intake;

namespace Romdex.Core.Tests;

public class ContractTests
{
    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1.2", true)]
    [InlineData("1.3", false)]
    [InlineData("0.9", false)]
    [InlineData("2.0", false)]
    public void Core_accepts_plugins_for_its_major_version_up_to_its_minor(string plugin, bool supported)
    {
        var core = new ContractVersion(1, 2);

        Assert.Equal(supported, core.Supports(ContractVersion.Parse(plugin)));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.x")]
    [InlineData("1.0.0")]
    public void Malformed_contract_versions_are_rejected(string value)
    {
        Assert.Throws<FormatException>(() => ContractVersion.Parse(value));
    }

    [Fact]
    public void An_intake_manifest_round_trips_with_its_settings_schema()
    {
        const string json = """
            {
              "id": "romdex.intake.inbox",
              "name": "Inbox folder",
              "version": "0.1.0",
              "type": "intake",
              "contractVersion": "1.0",
              "settingsSchema": { "type": "object", "properties": { "folder": { "type": "string" } } },
              "access": { "files": ["inbox (read)", "staging (write)"] }
            }
            """;

        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, IntakeJson.Options)!;

        Assert.Equal(PluginType.Intake, manifest.Type);
        Assert.Equal("object", manifest.SettingsSchema!.Value.GetProperty("type").GetString());
        Assert.Contains("\"type\":\"intake\"", JsonSerializer.Serialize(manifest, IntakeJson.Options));
    }

    [Fact]
    public void Every_plugin_type_has_a_contract_version()
    {
        foreach (var type in Enum.GetValues<PluginType>())
            Assert.Equal(1, ContractVersions.For(type).Major);
    }
}
