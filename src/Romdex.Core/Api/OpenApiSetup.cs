using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Romdex.Core.Access;
using Scalar.AspNetCore;

namespace Romdex.Core.Api;

/// <summary>
/// The OpenAPI document, at <c>/openapi/v1.json</c>, and the interactive reference built from it, at <c>/api/docs</c>.
/// Each operation says the scope a key needs for it.
/// </summary>
public static class OpenApiSetup
{
    private const string KeyScheme = "ApiKey";

    public static IServiceCollection AddRomdexOpenApi(this IServiceCollection services) => services.AddOpenApi("v1", openApi =>
    {
        openApi.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Romdex API";
            document.Info.Description =
                "Find what's missing from a ROM library, verified by hash against the preservation DATs, and bring dumps in through plugins.\n\n"
                + $"Apps authenticate with an API key in the `{AccessService.KeyHeader}` header, made under Settings → API. Each key has scopes, "
                + "the same permissions users have (`read`, `intake`, `play`, `review` and so on), and never more than the user who made it; `admin` is all they may. Each operation below says the scope it needs. "
                + "The client guide (`docs/client-guide.md`) covers events, webhooks and errors.";
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[KeyScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = AccessService.KeyHeader,
                Description = "An API key from Settings → API. Where a header can't be set, such as a download link, pass it as the apikey query parameter.",
            };
            document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(KeyScheme, document)] = [] }];
            return Task.CompletedTask;
        });

        openApi.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<OpenAccess>().Any())
            {
                // No key needed.
                operation.Security = [];
                operation.Description = Append(operation.Description, "Open to anyone: no key needed.");
            }
            else
            {
                var scope = AccessEndpointExtensions.ScopeFor(metadata);
                operation.Description = Append(operation.Description,
                    scope == Scopes.SignedIn ? "For someone signed in to the web app, not for keys." : $"Needs the `{scope}` scope.");
            }
            return Task.CompletedTask;
        });
    });

    /// <summary>The interactive reference. Open to anyone, as is the document it shows: neither holds anything private.</summary>
    public static void MapRomdexApiReference(this WebApplication app)
    {
        app.MapScalarApiReference("/api/docs", options => options
                .WithTitle("Romdex API")
                .WithOpenApiRoutePattern("/openapi/{documentName}.json")
                .AddPreferredSecuritySchemes(KeyScheme)
                .AddApiKeyAuthentication(KeyScheme, _ => { })
                // Self-hosted: nothing from Scalar's own servers, and none of its cloud features.
                .DisableDefaultFonts()
                .DisableTelemetry()
                .DisableAgent()
                .DisableMcp()
                .HideClientButton()
                .HideDeveloperTools())
            .AllowAnyone();
    }

    private static string Append(string? description, string line) =>
        string.IsNullOrEmpty(description) ? line : $"{description}\n\n{line}";
}
