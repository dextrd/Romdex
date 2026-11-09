using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Romdex.Client;
using Romdex.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The web app is just another client of the public API, served from the same origin.
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<Theme>();
builder.Services.AddScoped(services =>
    new HttpClient(new SignInHandler(services.GetRequiredService<AuthState>())) { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<RomdexClient>();
builder.Services.AddScoped<ServerEvents>();

await builder.Build().RunAsync();
