using System.Text.Json;
using System.Text.Json.Serialization;

namespace Romdex.Contracts.Plugins.Intake;

// The intake contract (version 1.0) as DTOs. Intake plugins run in their own containers and answer over HTTP; settings
// travel with every call, so a container keeps no setup of its own. There's no search: a plugin hands over files the
// user made or was given, and the core works out what they are by hash. No plugin downloads commercial games.
//
//   GET  /v1/manifest                the plugin's PluginManifest, with type intake
//   POST /v1/health                  HealthRequest → HealthReport
//   POST /v1/checks/{id}             HealthRequest → CheckResult, for each check the manifest offers
//
// Listing and staging items come with Phase 2, which adds them here as optional members.

/// <summary>Body of <c>POST /v1/health</c> and <c>POST /v1/checks/{id}</c>.</summary>
/// <param name="Settings">The instance's settings.</param>
public sealed record HealthRequest(JsonElement Settings);

/// <summary>What a check found.</summary>
public sealed record CheckResult(bool Passed, string Message);

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
