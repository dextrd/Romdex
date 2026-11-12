using System.Text.Json;
using System.Text.Json.Serialization;

namespace Romdex.Contracts.Plugins.Intake;

// The intake contract (version 1.0) as DTOs. Intake plugins run in their own containers and answer over HTTP, as
// Melodex's retrieval plugins do; settings travel with every call, so a container keeps no setup of its own. A plugin
// finds dumps for a game the core asks about, or hands over what arrived by itself, such as an inbox folder's files.
// Either way the core works out what the files are by hash, and files nothing it can't verify. Romdex ships no plugin
// that fetches commercial games.
//
//   GET    /v1/manifest             the plugin's PluginManifest, with type intake
//   POST   /v1/health               HealthRequest → HealthReport
//   POST   /v1/checks/{id}          HealthRequest → CheckResult, for each check the manifest offers
//   POST   /v1/search               SearchRequest → SearchResponse, with the search capability
//   POST   /v1/fetch                FetchRequest → FetchStatus, with the search capability; returns once accepted
//   GET    /v1/fetch/{jobId}        → FetchStatus
//   DELETE /v1/fetch/{jobId}        with the cancel capability
//
// Listing what arrived by itself comes with Phase 2, which adds it here as the list capability.

/// <summary>Body of <c>POST /v1/health</c> and <c>POST /v1/checks/{id}</c>.</summary>
/// <param name="Settings">The instance's settings.</param>
public sealed record HealthRequest(JsonElement Settings);

/// <summary>What a check found.</summary>
public sealed record CheckResult(bool Passed, string Message);

/// <summary>The game the core wants dumps of, with every dump the DAT lists for it, most wanted first.</summary>
/// <param name="System">The system, as its DAT names it, such as <c>Nintendo - Game Boy</c>.</param>
/// <param name="Title">The game's name without regions or versions.</param>
/// <param name="Dumps">Its dumps, the one the user prefers first. Any of them will do unless <paramref name="DumpName"/> says otherwise.</param>
/// <param name="DumpName">The one dump wanted, when the user picked one.</param>
public sealed record IntakeTarget(string System, string Title, IReadOnlyList<TargetDump> Dumps, string? DumpName = null);

/// <summary>One dump of a target, as the DAT lists it.</summary>
/// <param name="Name">Its full name, with regions and versions, such as <c>Game (USA) (Rev 1)</c>.</param>
/// <param name="Regions">Its regions.</param>
/// <param name="Serial">The serial on its box or disc, if the DAT has it.</param>
/// <param name="Files">Its files, which the fetched files must match.</param>
public sealed record TargetDump(string Name, IReadOnlyList<string> Regions, string? Serial, IReadOnlyList<TargetFile> Files);

/// <summary>One file of a dump, with the checksums the DAT gives.</summary>
/// <param name="Name">The file's name.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Crc32">CRC32, as lowercase hex.</param>
/// <param name="Md5">MD5, as lowercase hex.</param>
/// <param name="Sha1">SHA-1, as lowercase hex.</param>
public sealed record TargetFile(string Name, long Size, string? Crc32, string? Md5, string? Sha1);

/// <summary>Body of <c>POST /v1/search</c>.</summary>
/// <param name="Settings">The instance's settings.</param>
/// <param name="Target">What to find.</param>
/// <param name="Limit">The most candidates to return.</param>
public sealed record SearchRequest(JsonElement Settings, IntakeTarget Target, int Limit = 10);

/// <summary>Answer to <c>POST /v1/search</c>.</summary>
/// <param name="Candidates">What the plugin could fetch, best first.</param>
public sealed record SearchResponse(IReadOnlyList<Candidate> Candidates);

/// <summary>Something a plugin could fetch for a target.</summary>
/// <param name="CandidateId">
/// Opaque to the core and passed back unchanged to <c>POST /v1/fetch</c>. It carries everything the plugin needs to
/// fetch, because plugins keep nothing between calls.
/// </param>
/// <param name="Name">What the source calls it.</param>
/// <param name="Source">Where it's from, for people, shown on the Activity page.</param>
/// <param name="DumpName">The target's dump the plugin thinks it is, if it can tell.</param>
/// <param name="Url">A link to it at its source, if it has one.</param>
/// <param name="Format">How it comes, such as <c>zip</c>, <c>7z</c>, <c>chd</c> or <c>rvz</c>.</param>
/// <param name="Size">Its size in bytes, if known.</param>
/// <param name="Verified">Whether the source gives checksums that match the target's, so it's known to be a good dump.</param>
/// <param name="Score">The plugin's confidence from 0 to 1 that it's the target.</param>
public sealed record Candidate(
    string CandidateId,
    string Name,
    string Source,
    string? DumpName = null,
    string? Url = null,
    string? Format = null,
    long? Size = null,
    bool? Verified = null,
    double? Score = null);

/// <summary>Body of <c>POST /v1/fetch</c>.</summary>
/// <param name="Settings">The instance's settings.</param>
/// <param name="JobId">The core's ID for the job, for <c>GET</c> and <c>DELETE /v1/fetch/{jobId}</c>.</param>
/// <param name="Target">What the candidate was chosen for.</param>
/// <param name="Candidate">The candidate chosen, as search returned it.</param>
/// <param name="StagingDirectory">
/// The folder to write into, relative to the staging volume. The core makes it; the plugin writes only inside it.
/// </param>
public sealed record FetchRequest(JsonElement Settings, string JobId, IntakeTarget Target, Candidate Candidate, string StagingDirectory);

/// <summary>Where a fetch is.</summary>
public enum FetchState
{
    /// <summary>Accepted, not started.</summary>
    Queued,

    /// <summary>Downloading or unpacking.</summary>
    Running,

    /// <summary>Every file is in the staging folder.</summary>
    Completed,

    /// <summary>Gave up; see <see cref="FetchStatus.Error"/>.</summary>
    Failed,

    /// <summary>The core cancelled it.</summary>
    Cancelled,
}

/// <summary>Answer to <c>POST /v1/fetch</c> and <c>GET /v1/fetch/{jobId}</c>.</summary>
/// <param name="JobId">The core's ID for the job.</param>
/// <param name="State">Where it is.</param>
/// <param name="Progress">From 0 to 1, if known.</param>
/// <param name="Files">The files written, once it's <see cref="FetchState.Completed"/>.</param>
/// <param name="Error">Why it failed.</param>
public sealed record FetchStatus(string JobId, FetchState State, double? Progress = null, IReadOnlyList<FetchedFile>? Files = null, string? Error = null);

/// <summary>A file a plugin wrote to staging. The core hashes it; nothing the plugin says about it is trusted.</summary>
/// <param name="Path">Its path, relative to the request's staging folder.</param>
/// <param name="Size">Its size in bytes.</param>
public sealed record FetchedFile(string Path, long Size);

/// <summary>Capabilities an intake plugin can declare in its manifest.</summary>
public static class IntakeCapabilities
{
    /// <summary>Searches for a game and fetches what it finds: <c>/v1/search</c> and <c>/v1/fetch</c>.</summary>
    public const string Search = "search";

    /// <summary>Can stop a fetch: <c>DELETE /v1/fetch/{jobId}</c>.</summary>
    public const string Cancel = "cancel";
}

/// <summary>An error from an intake plugin, as JSON with a 4xx or 5xx status.</summary>
/// <param name="Error">What went wrong, for people.</param>
/// <param name="Code">A stable code for programs, if there is one.</param>
public sealed record IntakeError(string Error, string? Code = null);

/// <summary>Serializer options matching the intake plugins' wire format.</summary>
public static class IntakeJson
{
    /// <summary>Serializer options matching the wire format.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
