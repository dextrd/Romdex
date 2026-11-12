using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Romdex.Core.Catalogue;

/// <summary>What a dump is beyond being a retail game, as its name or DAT category says. A dump can be several.</summary>
[Flags]
public enum DumpKinds
{
    None = 0,
    Demo = 1 << 0,
    Beta = 1 << 1,
    Proto = 1 << 2,
    Pirate = 1 << 3,
    Unlicensed = 1 << 4,
    Aftermarket = 1 << 5,
    Homebrew = 1 << 6,
    Program = 1 << 7,
    Debug = 1 << 8,
    Bad = 1 << 9,
    Bios = 1 << 10,
    Alternate = 1 << 11,
}

/// <summary>A dump's name, read into its parts.</summary>
/// <param name="Title">The name without any bracketed tags, such as <c>Tetris</c>: what groups a game's regions and revisions.</param>
/// <param name="Release">The name without its disc tag, so every disc of one release shares it.</param>
/// <param name="Regions">The regions in the name, in its order.</param>
/// <param name="Languages">The languages in the name, or, where it has none, those its regions imply.</param>
/// <param name="Revision">A sortable form of its revision or version: higher is newer, and none sorts first.</param>
/// <param name="Disc">The disc tag's value, such as <c>1</c> or <c>A</c>, for one disc of several.</param>
public sealed record DumpName(string Title, string Release, IReadOnlyList<string> Regions, IReadOnlyList<string> Languages, string Revision, string? Disc,
    DumpKinds Kinds);

/// <summary>
/// Reads No-Intro and Redump names, such as <c>Tetris (Japan, USA) (En) (Rev 1)</c>. Names only describe dumps here:
/// matching is by hash alone.
/// </summary>
public static partial class DumpNames
{
    /// <summary>Regions as the two DATs spell them, in the order shown when choosing preferences.</summary>
    public static readonly IReadOnlyList<string> KnownRegions =
    [
        "World", "Europe", "USA", "Japan", "UK", "Australia", "Canada", "Asia", "Korea", "China", "Taiwan", "Hong Kong", "Brazil", "Latin America",
        "Mexico", "Argentina", "France", "Germany", "Italy", "Spain", "Netherlands", "Belgium", "Sweden", "Norway", "Denmark", "Finland",
        "Scandinavia", "Nordic", "Portugal", "Greece", "Poland", "Russia", "Austria", "Switzerland", "Ireland", "New Zealand", "India",
        "South Africa", "Croatia", "Czech", "Hungary", "Turkey", "Israel", "United Arab Emirates", "Singapore", "Thailand", "Indonesia", "Unknown",
    ];

    private static readonly HashSet<string> Regions = new(KnownRegions, StringComparer.OrdinalIgnoreCase) { "United Kingdom" };

    // The languages a region implies when the name doesn't list any, as Retool and igir assume.
    private static readonly Dictionary<string, string[]> RegionLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["World"] = ["En"], ["USA"] = ["En"], ["Europe"] = ["En"], ["UK"] = ["En"], ["United Kingdom"] = ["En"], ["Australia"] = ["En"],
        ["Canada"] = ["En", "Fr"], ["New Zealand"] = ["En"], ["Ireland"] = ["En"], ["Japan"] = ["Ja"], ["Korea"] = ["Ko"], ["China"] = ["Zh"],
        ["Taiwan"] = ["Zh"], ["Hong Kong"] = ["Zh"], ["Brazil"] = ["Pt"], ["Portugal"] = ["Pt"], ["France"] = ["Fr"], ["Belgium"] = ["Fr"],
        ["Germany"] = ["De"], ["Austria"] = ["De"], ["Switzerland"] = ["De"], ["Italy"] = ["It"], ["Spain"] = ["Es"], ["Latin America"] = ["Es"],
        ["Mexico"] = ["Es"], ["Argentina"] = ["Es"], ["Netherlands"] = ["Nl"], ["Sweden"] = ["Sv"], ["Norway"] = ["No"], ["Denmark"] = ["Da"],
        ["Finland"] = ["Fi"], ["Greece"] = ["El"], ["Poland"] = ["Pl"], ["Russia"] = ["Ru"],
    };

    private static readonly Dictionary<string, DumpKinds> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Demo"] = DumpKinds.Demo, ["Kiosk"] = DumpKinds.Demo, ["Sample"] = DumpKinds.Demo, ["Beta"] = DumpKinds.Beta, ["Proto"] = DumpKinds.Proto,
        ["Prototype"] = DumpKinds.Proto, ["Pirate"] = DumpKinds.Pirate, ["Unl"] = DumpKinds.Unlicensed, ["Aftermarket"] = DumpKinds.Aftermarket,
        ["Homebrew"] = DumpKinds.Homebrew, ["Program"] = DumpKinds.Program, ["Test Program"] = DumpKinds.Program, ["Debug"] = DumpKinds.Debug,
        ["Alt"] = DumpKinds.Alternate, ["Hack"] = DumpKinds.Pirate,
    };

    // Redump's categories, where they say more than the name does.
    private static readonly Dictionary<string, DumpKinds> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Demos"] = DumpKinds.Demo, ["Coverdiscs"] = DumpKinds.Demo, ["Preproduction"] = DumpKinds.Proto, ["Applications"] = DumpKinds.Program,
    };

    [GeneratedRegex(@"\(([^()]*)\)|\[([^\[\]]*)\]")]
    private static partial Regex Groups();

    [GeneratedRegex(@"^[A-Z][a-z](-[A-Za-z]+)?$")]
    private static partial Regex Language();

    [GeneratedRegex(@"^(?:Rev|Version|v)\s*([0-9A-Za-z.]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RevisionTag();

    [GeneratedRegex(@"^(?:Disc|Disk|CD)\s+([0-9A-Za-z]+)(?:\s+of\s+\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DiscTag();

    [GeneratedRegex(@"^(Demo|Kiosk|Sample|Beta|Proto|Prototype|Alt|Debug)(\s+\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberedTag();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    public static DumpName Parse(string name, string? category = null, bool isBios = false)
    {
        var regions = new List<string>();
        var languages = new List<string>();
        string revision = "", disc = "";
        string? discTag = null;
        var kinds = isBios || name.StartsWith("[BIOS]", StringComparison.OrdinalIgnoreCase) ? DumpKinds.Bios : DumpKinds.None;
        if (category is not null && Categories.TryGetValue(category, out var fromCategory))
            kinds |= fromCategory;

        foreach (Match group in Groups().Matches(name))
        {
            if (group.Groups[2].Success)
            {
                // Square brackets: [b] is a bad dump, [BIOS] a BIOS; the rest, such as [!] or [T+Eng], say nothing that matters here.
                var flag = group.Groups[2].Value.Trim();
                if (flag.Equals("b", StringComparison.OrdinalIgnoreCase) || flag.StartsWith("b ", StringComparison.OrdinalIgnoreCase))
                    kinds |= DumpKinds.Bad;
                else if (flag.Equals("BIOS", StringComparison.OrdinalIgnoreCase))
                    kinds |= DumpKinds.Bios;
                continue;
            }
            var text = group.Groups[1].Value.Trim();
            var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && regions.Count == 0 && parts.All(Regions.Contains))
                regions.AddRange(parts.Select(p => p.Equals("United Kingdom", StringComparison.OrdinalIgnoreCase) ? "UK" : p));
            else if (parts.Length > 0 && languages.Count == 0 && parts.All(p => Language().IsMatch(p)))
                languages.AddRange(parts);
            else if (RevisionTag().Match(text) is { Success: true } rev && (rev.Groups[1].Value.Any(char.IsDigit) || text.StartsWith("Rev", StringComparison.OrdinalIgnoreCase)))
                revision = SortableRevision(rev.Groups[1].Value);
            else if (DiscTag().Match(text) is { Success: true } d)
            {
                disc = d.Groups[1].Value;
                discTag = group.Value;
            }
            else if (Tags.TryGetValue(text, out var kind) || NumberedTag().Match(text) is { Success: true } numbered && Tags.TryGetValue(numbered.Groups[1].Value, out kind))
                kinds |= kind;
            else if (text.StartsWith("Beta", StringComparison.OrdinalIgnoreCase))
                kinds |= DumpKinds.Beta;
            else if (text.StartsWith("Proto", StringComparison.OrdinalIgnoreCase))
                kinds |= DumpKinds.Proto;
        }

        if (languages.Count == 0)
            languages.AddRange(regions.SelectMany(r => RegionLanguages.GetValueOrDefault(r) ?? []).Distinct());
        var title = Tidy(Groups().Replace(name, ""));
        var release = discTag is null ? name : Tidy(name.Replace(discTag, ""));
        return new DumpName(title.Length > 0 ? title : name, release, regions, languages, revision, disc.Length > 0 ? disc : null, kinds);
    }

    /// <summary>The key titles group on: the same however the name is cased or spaced.</summary>
    public static string TitleKey(string title) => Tidy(title).ToLowerInvariant();

    private static string Tidy(string value) => Spaces().Replace(value, " ").Trim();

    // Each dotted part is a zero-padded number, so v1.10 sorts after v1.9. Letters, as in "Rev A", count from 1. No
    // revision is "", which sorts before every revision.
    private static string SortableRevision(string value)
    {
        var builder = new StringBuilder();
        foreach (var part in value.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var number = int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n
                : part.Length == 1 && char.IsLetter(part[0]) ? char.ToUpperInvariant(part[0]) - 'A' + 1
                : int.TryParse(new string(part.TakeWhile(char.IsDigit).ToArray()), out var lead) ? lead : 0;
            builder.Append(number.ToString("D5", CultureInfo.InvariantCulture)).Append('.');
        }
        return builder.Length == 0 ? "00000." : builder.ToString();
    }
}
