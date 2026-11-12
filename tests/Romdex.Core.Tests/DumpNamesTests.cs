using Romdex.Core.Catalogue;

namespace Romdex.Core.Tests;

/// <summary>Reading No-Intro and Redump names into their parts.</summary>
public sealed class DumpNamesTests
{
    [Fact]
    public void Regions_languages_and_revision_are_read_from_the_name()
    {
        var name = DumpNames.Parse("Hopper Quest (USA, Europe) (En,Fr,De) (Rev 2)");

        Assert.Equal("Hopper Quest", name.Title);
        Assert.Equal(["USA", "Europe"], name.Regions);
        Assert.Equal(["En", "Fr", "De"], name.Languages);
        Assert.True(string.CompareOrdinal(name.Revision, DumpNames.Parse("Hopper Quest (USA) (Rev 1)").Revision) > 0);
        Assert.True(string.CompareOrdinal(DumpNames.Parse("Hopper Quest (USA) (Rev 1)").Revision, DumpNames.Parse("Hopper Quest (USA)").Revision) > 0);
        Assert.True(string.CompareOrdinal(DumpNames.Parse("Hopper Quest (USA) (v1.10)").Revision, DumpNames.Parse("Hopper Quest (USA) (v1.9)").Revision) > 0);
        Assert.Equal(DumpKinds.None, name.Kinds);
    }

    [Fact]
    public void Languages_come_from_the_regions_when_the_name_has_none()
    {
        Assert.Equal(["Ja"], DumpNames.Parse("Hopper Quest (Japan)").Languages);
        Assert.Equal(["En"], DumpNames.Parse("Hopper Quest (World)").Languages);
    }

    [Theory]
    [InlineData("Hopper Quest (USA) (Demo)", DumpKinds.Demo)]
    [InlineData("Hopper Quest (USA) (Kiosk)", DumpKinds.Demo)]
    [InlineData("Hopper Quest (USA) (Beta 2)", DumpKinds.Beta)]
    [InlineData("Hopper Quest (USA) (Proto 1)", DumpKinds.Proto)]
    [InlineData("Hopper Quest (USA) (Unl)", DumpKinds.Unlicensed)]
    [InlineData("Hopper Quest (World) (Aftermarket) (Unl)", DumpKinds.Aftermarket | DumpKinds.Unlicensed)]
    [InlineData("Hopper Quest (Europe) (Pirate)", DumpKinds.Pirate)]
    [InlineData("Hopper Quest (USA) (Alt)", DumpKinds.Alternate)]
    [InlineData("[BIOS] Handheld Boot ROM (World)", DumpKinds.Bios)]
    [InlineData("Hopper Quest (USA) [b]", DumpKinds.Bad)]
    public void Kinds_are_read_from_tags(string name, DumpKinds kinds) => Assert.Equal(kinds, DumpNames.Parse(name).Kinds);

    [Fact]
    public void Redump_categories_say_what_the_name_doesnt()
    {
        Assert.Equal(DumpKinds.Demo, DumpNames.Parse("Disc Hopper (Europe)", "Demos").Kinds);
        Assert.Equal(DumpKinds.Proto, DumpNames.Parse("Disc Hopper (Europe)", "Preproduction").Kinds);
        Assert.Equal(DumpKinds.None, DumpNames.Parse("Disc Hopper (Europe)", "Games").Kinds);
    }

    [Fact]
    public void Discs_of_one_release_share_it_and_one_title()
    {
        var first = DumpNames.Parse("Disc Hopper (USA) (Disc 1)");
        var second = DumpNames.Parse("Disc Hopper (USA) (Disc 2)");

        Assert.Equal("1", first.Disc);
        Assert.Equal("Disc Hopper (USA)", first.Release);
        Assert.Equal(first.Release, second.Release);
        Assert.Equal("Disc Hopper", second.Title);
        Assert.Null(DumpNames.Parse("Disc Hopper (USA)").Disc);
    }

    [Fact]
    public void Title_keys_ignore_case_and_spacing() =>
        Assert.Equal(DumpNames.TitleKey("Hopper  Quest"), DumpNames.TitleKey("hopper quest"));
}
