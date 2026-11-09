namespace Romdex.Core.Access;

/// <summary>
/// What a caller may do: the permissions a user is given, and an API key's scopes, never more than its owner's. The
/// admin has them all. Admin covers the rest.
/// </summary>
public static class Scopes
{
    /// <summary>Browse the systems, the catalogue and the library, what's arriving, tasks and events.</summary>
    public const string Read = "read";

    /// <summary>Accept, reject and retry arriving dumps, set each system's preferences, and ignore titles.</summary>
    public const string Intake = "intake";

    /// <summary>Play games from the library in the browser.</summary>
    public const string Play = "play";

    /// <summary>Look over unknown files and bad dumps, and keep or delete them.</summary>
    public const string Review = "review";

    /// <summary>Rename, move and convert files in the library.</summary>
    public const string Files = "files";

    /// <summary>Scan the library, and pause, cancel and reorder background tasks.</summary>
    public const string Scan = "scan";

    /// <summary>Change settings and plugins, and read the log.</summary>
    public const string Settings = "settings";

    /// <summary>Make API keys and manage webhooks.</summary>
    public const string Keys = "keys";

    /// <summary>Add, change and remove users, other than the admin.</summary>
    public const string Users = "users";

    /// <summary>
    /// Everything. Only the admin has it; on an API key it means everything its owner may do. Anything without a
    /// permission of its own needs it.
    /// </summary>
    public const string Admin = "admin";

    /// <summary>
    /// Not a permission: anyone signed in, whatever they've been given, for their own account and how the web app
    /// looks to them. Keys never have it.
    /// </summary>
    public const string SignedIn = "signed-in";

    /// <summary>The permissions a user can be given, in the order they're listed.</summary>
    public static readonly IReadOnlyList<string> Permissions = [Read, Intake, Play, Review, Files, Scan, Settings, Keys, Users];

    /// <summary>Every scope a key can have: the permissions, and admin.</summary>
    public static readonly IReadOnlyList<string> All = [.. Permissions, Admin];

    public static bool IsKnown(string scope) => All.Contains(scope);

    public static bool IsPermission(string scope) => Permissions.Contains(scope);

    public static IEnumerable<string> Parse(string? list) => (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(IsKnown);

    /// <summary>A list kept in order, without repeats or unknown names.</summary>
    public static string Join(IEnumerable<string> scopes)
    {
        var set = scopes.ToHashSet();
        return string.Join(",", All.Where(set.Contains));
    }
}

/// <summary>The scope an endpoint needs, when it isn't the default: read for GET, admin (the admin only) for anything that changes something.</summary>
public sealed record RequiredScope(string Scope);

/// <summary>An endpoint anyone may call, signed in or not: signing in itself, health checks, and signed game streams.</summary>
public sealed class OpenAccess
{
    public static readonly OpenAccess Instance = new();
}

public static class AccessEndpointExtensions
{
    /// <summary>Needs this scope, rather than the default for its method.</summary>
    public static TBuilder RequireScope<TBuilder>(this TBuilder builder, string scope) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RequiredScope(scope));

    /// <summary>Open to anyone. Use only where the endpoint checks access itself, or gives nothing away.</summary>
    public static TBuilder AllowAnyone<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(OpenAccess.Instance);

    /// <summary>The scope this endpoint needs: its own, or read for GET and admin for anything else.</summary>
    public static string ScopeFor(Endpoint endpoint) => ScopeFor(endpoint.Metadata);

    /// <inheritdoc cref="ScopeFor(Endpoint)"/>
    public static string ScopeFor(IEnumerable<object> metadata)
    {
        var list = metadata as IReadOnlyList<object> ?? metadata.ToList();
        if (list.OfType<RequiredScope>().LastOrDefault() is { } required)
            return required.Scope;
        var methods = list.OfType<IHttpMethodMetadata>().LastOrDefault()?.HttpMethods ?? [];
        return methods.Count > 0 && methods.All(m => m is "GET" or "HEAD") ? Scopes.Read : Scopes.Admin;
    }
}

public enum CallerKind
{
    /// <summary>Someone signed in.</summary>
    User,

    /// <summary>An app with an API key.</summary>
    Key,
}

/// <summary>Who's calling, and what they may do.</summary>
/// <param name="Name">The user's username, or the key's name.</param>
/// <param name="UserId">The user, or the key's owner.</param>
/// <param name="IsAdmin">The caller is the admin, or a key of theirs.</param>
public sealed record Caller(CallerKind Kind, string Name, IReadOnlySet<string> Scopes, Guid? UserId = null, Guid? KeyId = null, bool IsAdmin = false)
{
    public bool May(string scope) =>
        scope == Access.Scopes.SignedIn ? Kind == CallerKind.User : Scopes.Contains(scope) || Scopes.Contains(Access.Scopes.Admin);

    public static Caller Of(HttpContext http) => http.Items[typeof(Caller)] as Caller ?? Unknown;

    private static readonly Caller Unknown = new(CallerKind.User, "unknown", new HashSet<string>());
}
