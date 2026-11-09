namespace Romdex.Client;

/// <summary>Answer to <c>GET /auth/status</c>: whether there's an admin yet, and who this caller is.</summary>
/// <param name="SetUp">The admin account has been made. Until then the web app asks for one, and the API refuses everything else.</param>
/// <param name="SignedIn">This browser has a session.</param>
/// <param name="LetIn">This caller may use the API: signed in, or an app with a key.</param>
/// <param name="Username">Who's signed in, or the key's name.</param>
/// <param name="UserId">The user signed in, or the key's owner.</param>
/// <param name="IsAdmin">The admin is signed in, who may do everything.</param>
/// <param name="Permissions">What this caller may do: see <see cref="UserPermissions"/>. The admin has every one, and admin.</param>
/// <param name="Appearance">How the web app looks to whoever's signed in: their own look, or everyone's.</param>
/// <param name="OwnAppearance">They've chosen their own look, rather than following everyone's.</param>
/// <param name="SetupFinished">The admin has been through setup. Until then the web app shows only its steps.</param>
public sealed record AuthStatus(bool SetUp, bool SignedIn, bool LetIn, string? Username, Guid? UserId = null, bool IsAdmin = false,
    IReadOnlyList<string>? Permissions = null, Appearance? Appearance = null, bool OwnAppearance = false, bool SetupFinished = true)
{
    public bool May(string permission) => IsAdmin || Permissions?.Contains(permission) == true || Permissions?.Contains(UserPermissions.Admin) == true;
}

/// <summary>A theme's colour. Blue is Romdex's own; pink is its sister Melodex's.</summary>
public enum ThemeColor { Blue, Pink, Green, Grey, Purple, Amber }

/// <summary>Light or dark. Auto follows the device; black is dark with pure black behind, for OLED screens.</summary>
public enum ThemeMode { Auto, Light, Dark, Black }

/// <summary>How the web app looks for someone, kept with their account: <c>PUT /auth/appearance</c>.</summary>
/// <param name="HighContrast">Stronger text and borders, and status colours that colour-blind people can tell apart.</param>
public sealed record Appearance(ThemeColor Color = ThemeColor.Blue, ThemeMode Mode = ThemeMode.Auto, bool HighContrast = false);

/// <summary>Body of <c>POST /auth/setup</c>: the admin account, made the first time Romdex runs.</summary>
public sealed record SetUpLogin(string Username, string Password);

/// <summary>Body of <c>POST /auth/login</c>.</summary>
/// <param name="RememberMe">Keep this browser signed in for 30 days from its last visit, rather than until it closes.</param>
public sealed record SignIn(string Username, string Password, bool RememberMe = false);

/// <summary>Body of <c>PUT /auth/account</c>: a new username or password for yourself, or both. The current password is always needed.</summary>
public sealed record UpdateLogin(string CurrentPassword, string? Username = null, string? NewPassword = null);

/// <summary>What a user may do, by name, as <c>GET /users</c> and API keys list them.</summary>
public static class UserPermissions
{
    public const string Read = "read";
    public const string Intake = "intake";
    public const string Play = "play";
    public const string Review = "review";
    public const string Files = "files";
    public const string Scan = "scan";
    public const string Settings = "settings";
    public const string Keys = "keys";
    public const string Users = "users";

    /// <summary>Everything. Only the admin has it; a key with it may do all its owner may.</summary>
    public const string Admin = "admin";

    public sealed record Permission(string Id, string Name, string Hint);

    public static readonly IReadOnlyList<Permission> All =
    [
        new(Read, "Browse", "See the systems, what's owned and missing, what's arriving and what's running"),
        new(Intake, "Intake", "Accept, reject and retry dumps as they arrive, set what each system prefers, and ignore titles"),
        new(Play, "Play", "Play games from the library in the browser"),
        new(Review, "Review", "Look over unknown files and bad dumps, and keep or delete them"),
        new(Files, "Tidy files", "Rename, move and convert files in the library"),
        new(Scan, "Scan", "Scan the library, and pause, cancel and reorder tasks"),
        new(Settings, "Settings and plugins", "Change settings and plugins, and read the log"),
        new(Keys, "API keys and webhooks", "Make API keys for apps, and manage webhooks"),
        new(Users, "Manage users", "Add, change and remove other users"),
    ];
}

/// <summary>Someone who signs in, as listed under Settings → Users.</summary>
/// <param name="IsAdmin">The admin, made at setup: may do everything, and only they can change their account.</param>
/// <param name="Permissions">See <see cref="UserPermissions"/>; every one for the admin.</param>
/// <param name="Keys">How many API keys they've made.</param>
public sealed record UserInfo(Guid Id, string Username, bool IsAdmin, IReadOnlyList<string> Permissions, DateTime CreatedAt, DateTime? LastSignInAt, int Keys = 0);

/// <summary>Body of <c>POST /users</c>.</summary>
public sealed record CreateUser(string Username, string Password, IReadOnlyList<string> Permissions);

/// <summary>Body of <c>PUT /users/{id}</c>: what changes. A new password signs them out everywhere.</summary>
public sealed record UpdateUser(string? Username = null, string? Password = null, IReadOnlyList<string>? Permissions = null);

/// <summary>An app's API key, as listed: never the key itself, which is shown only when it's made.</summary>
/// <param name="Prefix">The start of the key, to tell keys apart.</param>
/// <param name="Scopes">What it may do, as far as its owner may: see <see cref="UserPermissions"/>. Admin means all its owner may.</param>
/// <param name="Owner">The username of whoever made it.</param>
public sealed record ApiKeyInfo(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, DateTime CreatedAt, DateTime? LastUsedAt,
    Guid? UserId = null, string? Owner = null);

/// <summary>Body of <c>POST /keys</c>. A key can only be given permissions its maker has.</summary>
public sealed record CreateApiKey(string Name, IReadOnlyList<string> Scopes);

/// <summary>A key just made. <paramref name="Key"/> is shown this once: Romdex keeps only a hash of it.</summary>
public sealed record CreatedApiKey(ApiKeyInfo Info, string Key);
