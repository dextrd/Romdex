using Romdex.Core.Catalogue;
using Romdex.Core.Library;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Romdex.Client;
using Romdex.Contracts.Plugins.Index;
using Romdex.Core;
using Romdex.Core.Access;
using Romdex.Core.Api;
using Romdex.Core.Data;
using Romdex.Core.Diagnostics;
using Romdex.Core.Events;
using Romdex.Core.Plugins;
using Romdex.Core.Settings;
using Romdex.Core.Tasks;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection(RomdexOptions.Section).Get<RomdexOptions>() ?? new RomdexOptions();
builder.Services.Configure<RomdexOptions>(builder.Configuration.GetSection(RomdexOptions.Section));
builder.Services.AddOptions<PluginHttpOptions>();

Directory.CreateDirectory(options.DataPath);
var database = Path.Combine(options.DataPath, "romdex.db");
var reset = FactoryReset.ApplyIfRequested(options, database);
builder.Services.AddSingleton<EventFeed>();
builder.Services.AddDbContext<RomdexDbContext>((services, db) => db
    .UseSqlite(builder.Configuration.GetConnectionString("Romdex") ?? $"Data Source={database}", RomdexDbContext.Sqlite)
    .AddInterceptors(services.GetRequiredService<EventFeed>()));

// The activity log the web app shows. Registered as a provider so everything logged reaches it.
var activity = new ActivityLog();
builder.Services.AddSingleton(activity);
builder.Logging.AddProvider(new ActivityLogProvider(activity));
builder.Services.AddHostedService<LogArchive>();

// Who may call the API: users' sessions and apps' keys. The keys that sign
// sessions are kept with the data, so a restart doesn't sign everyone out.
builder.Services.AddRomdexAccess();
builder.Services.AddDataProtection()
    .SetApplicationName("Romdex")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.DataPath, "keys")));

builder.Services.ConfigureHttpJsonOptions(json => RomdexJson.Configure(json.SerializerOptions));

// Plugins
builder.Services.AddSingleton<PluginCatalog>();
builder.Services.AddSingleton<PluginHost>();
builder.Services.AddScoped<IndexPlugins>();
builder.Services.AddScoped<ConnectorPlugins>();
builder.Services.AddScoped<IntakePlugins>();
builder.Services.AddHttpClient(IntakePlugins.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(2));
builder.Services.AddScoped<SettingsService>();

// Background work
builder.Services.AddSingleton<TaskSignal>();
builder.Services.AddSingleton<TaskCancellations>();
builder.Services.AddScoped<TaskQueue>();
builder.Services.AddScoped<TaskTitles>();
builder.Services.AddHostedService<TaskWorker>();

// The catalogue and the library.
builder.Services.AddScoped<CatalogueImporter>();
builder.Services.AddScoped<Preferences>();
builder.Services.AddSingleton<LibraryViews>();
builder.Services.AddScoped<LibraryViewBuilder>();
builder.Services.AddScoped<LibraryMatcher>();
builder.Services.AddScoped<LibraryScanner>();
builder.Services.AddSingleton<ContainerReader>();
builder.Services.AddScoped<ITaskHandler, CatalogueRefreshTask>();
builder.Services.AddScoped<ITaskHandler, LibraryScanTask>();
builder.Services.AddHostedService<CatalogueSchedule>();

// Events: announced on the event stream and sent to webhooks
builder.Services.AddHttpClient(WebhookSender.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<WebhookSender>();
builder.Services.AddHostedService(services => services.GetRequiredService<WebhookSender>());
builder.Services.AddHostedService<EventAnnouncer>();

builder.Services.AddRomdexOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RomdexDbContext>();
    await db.Database.MigrateAsync();
    if (reset)
        app.Logger.LogWarning("Romdex was reset, as the admin asked: open the web app to set it up again");
    // Write-ahead logging, so pages keep reading while a scan writes. It's stored in the file, so this is once.
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await IndexPlugins.EnsureDefaultAsync(db, scope.ServiceProvider.GetRequiredService<PluginCatalog>(), options);
}

// The way back in when the admin's password is lost: start once with Romdex__ResetLogin=true, then set a new one.
// Other users, and everyone's keys, are kept.
if (options.ResetLogin)
{
    await app.Services.GetRequiredService<AccessService>().ResetAdminAsync(CancellationToken.None);
    app.Logger.LogWarning("The admin's password was cleared, as ResetLogin is set. Open the web app to set a new one, then unset ResetLogin");
}

// The query planner's statistics, worked out in the background where they're missing or stale.
_ = Task.Run(async () =>
{
    using var scope = app.Services.CreateScope();
    try
    {
        await scope.ServiceProvider.GetRequiredService<RomdexDbContext>().OptimiseAsync();
    }
    catch (Exception e)
    {
        app.Logger.LogWarning("Couldn't update the database's statistics: {Message}", e.Message);
    }
});

// A catalogue source that's busy or down is the client's cue to try again later, not a server error.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (IndexUnavailableException e) when (!context.Response.HasStarted)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "30";
        await context.Response.WriteAsJsonAsync(new { title = "The catalogue source is busy", detail = e.Message, status = 503 });
    }
});

app.UseAuthentication();
app.UseApiAccess();

// The spec is part of the public contract, so it's served in every environment.
app.MapOpenApi();
app.MapRomdexApiReference();

var api = app.MapGroup("/api/v1");
api.MapSystemEndpoints();
api.MapSetupEndpoints();
api.MapTaskEndpoints();
api.MapCatalogueEndpoints();
api.MapLibraryEndpoints();
api.MapPluginEndpoints();
api.MapAccessEndpoints();
api.MapWebhookEndpoints();

// The web app: Blazor WebAssembly, served from the same origin and talking only to the public API.
app.MapStaticAssets();
// The page itself is checked on every visit, as /index.html is: kept by the browser, an old one would load the new
// app without the scripts it now needs.
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = file => file.Context.Response.Headers.CacheControl = "no-cache",
});

app.Run();

public partial class Program;
