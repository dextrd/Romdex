using System.Net;
using System.Net.Http.Json;

namespace Romdex.Client;

/// <summary>Typed client for the Romdex public API. Pass an HttpClient whose BaseAddress is the Romdex server.</summary>
public sealed class RomdexClient(HttpClient http)
{
    private const string Api = "api/v1";

    // ---- Tasks and the log ----

    public async Task<IReadOnlyList<TaskInfo>> ListTasksAsync(bool? active = null, int? limit = null, CancellationToken ct = default)
    {
        var query = string.Join('&', new[] { active is { } a ? $"active={a.ToString().ToLowerInvariant()}" : null, limit is { } l ? $"limit={l}" : null }.OfType<string>());
        return await http.GetFromJsonAsync<List<TaskInfo>>($"{Api}/tasks{(query.Length > 0 ? "?" + query : "")}", RomdexJson.Options, ct) ?? [];
    }

    public Task<TaskInfo?> GetTaskAsync(Guid id, CancellationToken ct = default) =>
        http.GetFromJsonAsync<TaskInfo>($"{Api}/tasks/{id}", RomdexJson.Options, ct);

    public async Task<LogPage> GetLogAsync(long after = 0, int limit = 1000, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<LogPage>($"{Api}/log?after={after}&limit={limit}", RomdexJson.Options, ct) ?? new LogPage([], after);

    public async Task<LogSettings> GetLogSettingsAsync(CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<LogSettings>($"{Api}/settings/log", RomdexJson.Options, ct))!;

    public async Task SaveLogSettingsAsync(LogSettings settings, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"{Api}/settings/log", settings, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
    }

    // ---- Plugins ----

    public async Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<PluginInfo>>($"{Api}/plugins", RomdexJson.Options, ct) ?? [];

    public async Task<IReadOnlyList<AvailablePlugin>> ListAvailablePluginsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<AvailablePlugin>>($"{Api}/plugins/available", RomdexJson.Options, ct) ?? [];

    public async Task<PluginInfo> AddPluginAsync(AddPlugin plugin, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync($"{Api}/plugins", plugin, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<PluginInfo>(RomdexJson.Options, ct))!;
    }

    public async Task<PluginInfo> UpdatePluginAsync(Guid id, UpdatePlugin update, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"{Api}/plugins/{id}", update, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<PluginInfo>(RomdexJson.Options, ct))!;
    }

    public async Task OrderPluginsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"{Api}/plugins/order", new PluginOrder(ids), RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
    }

    public async Task<PluginCheckResult> RunPluginCheckAsync(Guid id, string check, RunPluginCheck body, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync($"{Api}/plugins/{id}/checks/{Uri.EscapeDataString(check)}", body, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<PluginCheckResult>(RomdexJson.Options, ct))!;
    }

    public async Task<PluginInfo> TestPluginAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"{Api}/plugins/{id}/test", null, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<PluginInfo>(RomdexJson.Options, ct))!;
    }

    public async Task RemovePluginAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"{Api}/plugins/{id}", ct);
        await EnsureAsync(response, ct);
    }

    // ---- Catalogue ----

    public async Task<IReadOnlyList<SystemSummary>> ListSystemsAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<SystemSummary>>($"{Api}/systems", RomdexJson.Options, ct) ?? [];

    public Task<SystemSummary?> GetSystemAsync(Guid id, CancellationToken ct = default) => GetOrNullAsync<SystemSummary>($"{Api}/systems/{id}", ct);

    /// <summary>Titles by name; with <c>have</c>, only those the library has some of: owned, partial or bad, ignored or not.</summary>
    public async Task<TitlePage> ListTitlesAsync(Guid systemId, TitleStatus? status = null, string? q = null, bool ignored = false, bool hidden = false,
        int skip = 0, int take = 100, bool have = false, CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<TitlePage>($"{Api}/systems/{systemId}/titles?{TitleQuery(status, q, ignored, hidden, have, skip, take)}", RomdexJson.Options, ct))!;

    private static string TitleQuery(TitleStatus? status, string? q, bool ignored, bool hidden, bool have, int skip, int take)
    {
        var query = new List<string> { $"skip={skip}", $"take={take}" };
        if (status is { } s)
            query.Add($"status={s}");
        if (!string.IsNullOrWhiteSpace(q))
            query.Add($"q={Uri.EscapeDataString(q)}");
        if (ignored)
            query.Add("ignored=true");
        if (hidden)
            query.Add("hidden=true");
        if (have)
            query.Add("have=true");
        return string.Join('&', query);
    }

    /// <summary>Titles by name; with <c>have</c>, only those the library has some of: owned, partial or bad, ignored or not.</summary>
    public async Task<TitlePage> ListAllTitlesAsync(TitleStatus? status = null, string? q = null, bool ignored = false, int skip = 0, int take = 100,
        bool have = false, CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<TitlePage>($"{Api}/titles?{TitleQuery(status, q, ignored, false, have, skip, take)}", RomdexJson.Options, ct))!;

    public async Task<BiosList> ListBiosAsync(Guid systemId, CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<BiosList>($"{Api}/systems/{systemId}/bios", RomdexJson.Options, ct))!;

    public async Task<string> GetSystemReportAsync(Guid systemId, CancellationToken ct = default) =>
        await http.GetStringAsync($"{Api}/systems/{systemId}/report.csv", ct);

    public async Task<SystemPreferencesInfo> GetSystemPreferencesAsync(Guid systemId, CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<SystemPreferencesInfo>($"{Api}/systems/{systemId}/preferences", RomdexJson.Options, ct))!;

    public async Task<SystemPreferencesInfo> SetSystemPreferencesAsync(Guid systemId, SystemPreferences preferences, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"{Api}/systems/{systemId}/preferences", preferences, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<SystemPreferencesInfo>(RomdexJson.Options, ct))!;
    }

    public async Task<SystemPreferencesInfo> ResetSystemPreferencesAsync(Guid systemId, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"{Api}/systems/{systemId}/preferences", ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<SystemPreferencesInfo>(RomdexJson.Options, ct))!;
    }

    public async Task<SystemPreferences> GetDefaultPreferencesAsync(CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<SystemPreferences>($"{Api}/preferences", RomdexJson.Options, ct))!;

    public async Task SetDefaultPreferencesAsync(SystemPreferences preferences, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"{Api}/preferences", preferences, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
    }

    public Task<TitleDetail?> GetTitleAsync(Guid id, CancellationToken ct = default) => GetOrNullAsync<TitleDetail>($"{Api}/titles/{id}", ct);

    public async Task<TitleDetail> IgnoreTitleAsync(Guid id, bool ignore = true, CancellationToken ct = default)
    {
        using var response = ignore ? await http.PostAsync($"{Api}/titles/{id}/ignore", null, ct) : await http.DeleteAsync($"{Api}/titles/{id}/ignore", ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<TitleDetail>(RomdexJson.Options, ct))!;
    }

    public async Task<TitleDetail> ChoosePreferredAsync(Guid id, Guid? dumpId, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"{Api}/titles/{id}/preferred", new ChoosePreferred(dumpId), RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<TitleDetail>(RomdexJson.Options, ct))!;
    }

    /// <summary>
    /// Every game the DATs list, in the library or not, whose name has every word of <paramref name="q"/>, closest first;
    /// <paramref name="have"/> keeps to games the library has (true) or hasn't (false).
    /// </summary>
    public async Task<TitlePage> SearchCatalogueAsync(string q, Guid? systemId = null, bool? have = null, int skip = 0, int take = 50,
        CancellationToken ct = default)
    {
        var query = $"q={Uri.EscapeDataString(q)}&skip={skip}&take={take}";
        if (systemId is { } id)
            query += $"&system={id}";
        if (have is { } h)
            query += $"&have={(h ? "true" : "false")}";
        return (await http.GetFromJsonAsync<TitlePage>($"{Api}/catalogue/search?{query}", RomdexJson.Options, ct))!;
    }

    public async Task<TaskInfo> RefreshCatalogueAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"{Api}/catalogue/refresh", null, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<TaskInfo>(RomdexJson.Options, ct))!;
    }

    // ---- Library ----

    public async Task<LibraryStatus> GetLibraryStatusAsync(CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<LibraryStatus>($"{Api}/library", RomdexJson.Options, ct))!;

    public async Task<TaskInfo> ScanLibraryAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"{Api}/library/scan", null, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<TaskInfo>(RomdexJson.Options, ct))!;
    }

    public async Task<TitlePage> ListMissingAsync(int skip = 0, int take = 100, CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<TitlePage>($"{Api}/library/missing?skip={skip}&take={take}", RomdexJson.Options, ct))!;

    public async Task<UnknownPage> ListUnknownAsync(int skip = 0, int take = 100, CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<UnknownPage>($"{Api}/library/unknown?skip={skip}&take={take}", RomdexJson.Options, ct))!;

    // ---- Stopping and ordering tasks ----

    public Task CancelTaskAsync(Guid id, CancellationToken ct = default) => PostAsync($"{Api}/tasks/{id}/cancel", null, ct);

    public Task PauseTaskAsync(Guid id, CancellationToken ct = default) => PostAsync($"{Api}/tasks/{id}/pause", null, ct);

    public Task ResumeTaskAsync(Guid id, CancellationToken ct = default) => PostAsync($"{Api}/tasks/{id}/resume", null, ct);

    public Task MoveTaskAsync(Guid id, Guid? before, CancellationToken ct = default) => PostAsync($"{Api}/tasks/{id}/move", new MoveInQueue(before), ct);

    private async Task PostAsync(string url, object? body, CancellationToken ct)
    {
        using var response = body is null ? await http.PostAsync(url, null, ct) : await http.PostAsJsonAsync(url, body, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
    }

    // ---- Access ----

    public async Task<AuthStatus> GetAuthStatusAsync(CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<AuthStatus>($"{Api}/auth/status", RomdexJson.Options, ct))!;

    /// <summary>Makes the admin account, the first time, and signs in with it.</summary>
    public async Task SetUpLoginAsync(SetUpLogin login, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync($"{Api}/auth/setup", login, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
    }

    public async Task SignInAsync(SignIn signIn, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync($"{Api}/auth/login", signIn, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"{Api}/auth/logout", null, ct);
        await EnsureAsync(response, ct);
    }

    public async Task UpdateLoginAsync(UpdateLogin update, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"{Api}/auth/account", update, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
    }

    public async Task<ServerVersion> GetVersionAsync(CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<ServerVersion>($"{Api}/version", RomdexJson.Options, ct))!;

    public async Task<SetupStatus> GetSetupAsync(CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<SetupStatus>($"{Api}/setup", RomdexJson.Options, ct))!;

    /// <summary>Setup is finished or skipped: the web app stops showing its steps.</summary>
    public async Task FinishSetupAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"{Api}/setup/done", null, ct);
        await EnsureAsync(response, ct);
    }

    /// <summary>Resets Romdex as it was first installed. The admin only; Romdex restarts.</summary>
    public async Task FactoryResetAsync(string confirm, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync($"{Api}/setup/reset", new FactoryResetRequest(confirm), RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
    }

    /// <summary>Keeps how the web app looks with your account.</summary>
    public async Task SetAppearanceAsync(Appearance appearance, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"{Api}/auth/appearance", appearance, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
    }

    /// <summary>Go back to everyone's look; returns it.</summary>
    public async Task<Appearance> ClearAppearanceAsync(CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"{Api}/auth/appearance", ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<Appearance>(RomdexJson.Options, ct))!;
    }

    public async Task<Appearance> GetDefaultAppearanceAsync(CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<Appearance>($"{Api}/auth/appearance/default", RomdexJson.Options, ct))!;

    /// <summary>How Romdex looks for everyone who hasn't chosen their own look.</summary>
    public async Task SetDefaultAppearanceAsync(Appearance appearance, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"{Api}/auth/appearance/default", appearance, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
    }

    public async Task<IReadOnlyList<UserInfo>> ListUsersAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<UserInfo>>($"{Api}/users", RomdexJson.Options, ct) ?? [];

    public async Task<UserInfo> CreateUserAsync(CreateUser user, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync($"{Api}/users", user, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<UserInfo>(RomdexJson.Options, ct))!;
    }

    public async Task<UserInfo> UpdateUserAsync(Guid id, UpdateUser update, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"{Api}/users/{id}", update, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<UserInfo>(RomdexJson.Options, ct))!;
    }

    public async Task RemoveUserAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"{Api}/users/{id}", ct);
        await EnsureAsync(response, ct);
    }

    public async Task<IReadOnlyList<ApiKeyInfo>> ListApiKeysAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ApiKeyInfo>>($"{Api}/keys", RomdexJson.Options, ct) ?? [];

    /// <summary>Makes a key. Its <see cref="CreatedApiKey.Key"/> can't be had again, so show or keep it now.</summary>
    public async Task<CreatedApiKey> CreateApiKeyAsync(CreateApiKey key, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync($"{Api}/keys", key, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<CreatedApiKey>(RomdexJson.Options, ct))!;
    }

    public async Task RevokeApiKeyAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"{Api}/keys/{id}", ct);
        await EnsureAsync(response, ct);
    }

    // ---- Webhooks ----

    public async Task<IReadOnlyList<WebhookInfo>> ListWebhooksAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<WebhookInfo>>($"{Api}/webhooks", RomdexJson.Options, ct) ?? [];

    public async Task<WebhookInfo> CreateWebhookAsync(SaveWebhook webhook, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync($"{Api}/webhooks", webhook, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<WebhookInfo>(RomdexJson.Options, ct))!;
    }

    public async Task<WebhookInfo> UpdateWebhookAsync(Guid id, SaveWebhook webhook, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync($"{Api}/webhooks/{id}", webhook, RomdexJson.Options, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<WebhookInfo>(RomdexJson.Options, ct))!;
    }

    public async Task<WebhookInfo> RotateWebhookSecretAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"{Api}/webhooks/{id}/secret", null, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<WebhookInfo>(RomdexJson.Options, ct))!;
    }

    public async Task RemoveWebhookAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"{Api}/webhooks/{id}", ct);
        await EnsureAsync(response, ct);
    }

    /// <summary>Sends a ping now and says how it went.</summary>
    public async Task<WebhookDeliveryInfo> TestWebhookAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"{Api}/webhooks/{id}/test", null, ct);
        await EnsureAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<WebhookDeliveryInfo>(RomdexJson.Options, ct))!;
    }

    public async Task<IReadOnlyList<WebhookDeliveryInfo>> ListWebhookDeliveriesAsync(Guid id, int limit = 50, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<WebhookDeliveryInfo>>($"{Api}/webhooks/{id}/deliveries?limit={limit}", RomdexJson.Options, ct) ?? [];

    /// <summary>Throws <see cref="RomdexApiException"/> with the server's own explanation for anything but success.</summary>
    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        string? message = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(RomdexJson.Options, ct);
            if (problem.TryGetProperty("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Object)
                message = string.Join(" ", errors.EnumerateObject().SelectMany(e => e.Value.EnumerateArray().Select(v => v.GetString())));
            else if (problem.TryGetProperty("detail", out var detail))
                message = detail.GetString();
            else if (problem.TryGetProperty("title", out var title))
                message = title.GetString();
        }
        catch (System.Text.Json.JsonException)
        {
        }
        throw new RomdexApiException(response.StatusCode, string.IsNullOrWhiteSpace(message) ? $"{(int)response.StatusCode} {response.ReasonPhrase}" : message);
    }

    private async Task<T?> GetOrNullAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(RomdexJson.Options, ct);
    }
}

/// <summary>The server refused a request, with its reason.</summary>
public sealed class RomdexApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}
