using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Romdex.Client;
using Romdex.Contracts.Plugins;
using Romdex.Core.Access;
using Romdex.Core.Data;

namespace Romdex.Core.Api;

/// <summary>The steps after making the admin account: the ROM library, the DATs, and an app such as RomM if there is one.</summary>
public static class SetupEndpoints
{
    private const string DoneKey = "setup.done";

    public static RouteGroupBuilder MapSetupEndpoints(this RouteGroupBuilder api)
    {
        var setup = api.MapGroup("/setup").WithTags("System").RequireScope(Scopes.Settings);

        setup.MapGet("", async (RomdexDbContext db, IOptions<RomdexOptions> options, CancellationToken ct) =>
            {
                var (readable, files) = Count(options.Value.LibraryPath, _ => true);
                var (_, dats) = Count(options.Value.DatPath, f => DatExtensions.Contains(Path.GetExtension(f)));
                return new SetupStatus(await IsDoneAsync(db, ct), Path.GetFullPath(options.Value.LibraryPath), readable, files,
                    Path.GetFullPath(options.Value.DatPath), dats,
                    await db.PluginInstances.AnyAsync(p => p.Type == PluginType.Index, ct),
                    await db.PluginInstances.AnyAsync(p => p.Type == PluginType.Connector, ct));
            })
            .WithName("GetSetup")
            .WithSummary("How far setup has got: whether the ROM library can be read, how many DATs there are, and what's been added.");

        setup.MapPost("/done", async (RomdexDbContext db, CancellationToken ct) =>
            {
                await MarkDoneAsync(db, ct);
                return Results.NoContent();
            })
            .RequireScope(Scopes.Settings)
            .WithName("FinishSetup")
            .WithSummary("Setup is finished, or skipped: the web app stops showing its steps.");

        setup.MapPost("/reset", (FactoryResetRequest body, HttpContext http, IOptions<RomdexOptions> options, IHostApplicationLifetime lifetime) =>
            {
                if (Caller.Of(http) is not { Kind: CallerKind.User, IsAdmin: true })
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only the admin, signed in, can reset Romdex.");
                if (!string.Equals(body.Confirm?.Trim(), FactoryReset.Confirmation, StringComparison.OrdinalIgnoreCase))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["confirm"] = [$"Type \"{FactoryReset.Confirmation}\" to reset Romdex."] });
                FactoryReset.Request(options.Value);
                // Once the answer is sent, Romdex stops, and wipes everything as it starts again.
                http.Response.OnCompleted(() =>
                {
                    lifetime.StopApplication();
                    return Task.CompletedTask;
                });
                return Results.Accepted();
            })
            .WithName("FactoryReset")
            .WithSummary("Resets Romdex as it was first installed, for the admin only, who must send confirm: \"reset\". Users, settings, plugins, "
                         + "the catalogue, the library's hashes, tasks and logs all go; the ROM library, the DAT folder and plugins' own data stay. Romdex restarts, so everyone is signed out, and the web app starts setup again.")
            .Produces(StatusCodes.Status202Accepted).ProducesValidationProblem();

        return api;
    }

    /// <summary>Whether setup is finished. Until it is, the web app shows only its steps.</summary>
    public static Task<bool> IsDoneAsync(RomdexDbContext db, CancellationToken ct) => db.Settings.AnyAsync(s => s.Key == DoneKey, ct);

    private static async Task MarkDoneAsync(RomdexDbContext db, CancellationToken ct)
    {
        if (await db.Settings.FindAsync([DoneKey], ct) is null)
        {
            db.Settings.Add(new Setting { Key = DoneKey, ValueJson = "true" });
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>The extensions DAT files have: plain, as XML, or zipped as No-Intro's packs and Redump's downloads come.</summary>
    public static readonly IReadOnlySet<string> DatExtensions = new HashSet<string>([".dat", ".xml", ".zip"], StringComparer.OrdinalIgnoreCase);

    // A quick look, not a scan: enough to say the folder is right, without walking a whole big library.
    private static (bool Readable, int Files) Count(string path, Func<string, bool> counts)
    {
        try
        {
            var files = Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(counts)
                .Take(SetupStatus.CountLimit)
                .Count();
            return (true, files);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (false, 0);
        }
    }
}
