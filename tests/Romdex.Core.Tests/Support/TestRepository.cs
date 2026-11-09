namespace Romdex.Core.Tests.Support;

/// <summary>Where the repository and its test fixtures are.</summary>
public static class TestRepository
{
    public static string Root { get; } = FindRoot();

    public static string Dats => Path.Combine(Root, "tests", "fixtures", "dats");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Romdex.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Couldn't find the repository root above " + AppContext.BaseDirectory);
    }
}
