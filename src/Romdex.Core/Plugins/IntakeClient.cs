using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Romdex.Contracts.Plugins;
using Romdex.Contracts.Plugins.Intake;
using Romdex.Core.Data;

namespace Romdex.Core.Plugins;

/// <summary>
/// An intake plugin instance, reached over the HTTP contract (see <see cref="IntakeJson"/>). Settings are sent with every
/// call, so the container keeps no setup of its own.
/// </summary>
public sealed class IntakeClient(HttpClient http, JsonElement settings) : IPlugin
{
    public static async Task<PluginManifest> GetManifestAsync(HttpClient http, CancellationToken ct)
    {
        using var response = await http.GetAsync("v1/manifest", ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<PluginManifest>(IntakeJson.Options, ct))!;
    }

    public async Task<HealthReport> CheckHealthAsync(CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("v1/health", new HealthRequest(settings), IntakeJson.Options, cancellationToken);
        await EnsureAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<HealthReport>(IntakeJson.Options, cancellationToken))!;
    }

    /// <summary>Runs one of the checks the manifest offers.</summary>
    public async Task<CheckResult> RunCheckAsync(string check, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"v1/checks/{Uri.EscapeDataString(check)}", new HealthRequest(settings), IntakeJson.Options, cancellationToken);
        await EnsureAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CheckResult>(IntakeJson.Options, cancellationToken))!;
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        IntakeError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<IntakeError>(IntakeJson.Options, ct);
        }
        catch (JsonException)
        {
        }
        throw new IntakeException(error?.Error ?? $"The plugin answered {(int)response.StatusCode} {response.ReasonPhrase}", error?.Code, response.StatusCode);
    }
}

/// <summary>An intake plugin said no, with its reason.</summary>
public sealed class IntakeException(string message, string? code, HttpStatusCode status) : Exception(message)
{
    public string? Code { get; } = code;
    public HttpStatusCode Status { get; } = status;
}

/// <summary>The intake plugins the user set up.</summary>
public sealed class IntakePlugins(RomdexDbContext db, IHttpClientFactory httpFactory)
{
    public const string HttpClientName = "intake";

    public async Task<IReadOnlyList<(PluginInstance Instance, IntakeClient Plugin)>> GetAllAsync(CancellationToken ct)
    {
        var instances = await db.PluginInstances.Where(p => p.Type == PluginType.Intake && p.Enabled && p.Endpoint != null)
            .OrderBy(p => p.Priority).ThenBy(p => p.CreatedAt).ToListAsync(ct);
        return instances.Select(i => (i, Create(i))).ToList();
    }

    public IntakeClient Create(PluginInstance instance) =>
        new(Client(instance.Endpoint!), JsonDocument.Parse(instance.SettingsJson).RootElement.Clone());

    public HttpClient Client(string endpoint)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/");
        return http;
    }
}
