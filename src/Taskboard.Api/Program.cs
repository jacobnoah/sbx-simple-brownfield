// Application composition root and entry point.
//
// SHARED FILE. The ONLY edit a feature agent may make to this file is adding one
// line inside the FEATURE REGISTRATION block below. Do not restructure anything
// else here. See AGENTS.md.

using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

using Scalar.AspNetCore;

using Taskboard.Api.Configuration;
using Taskboard.Api.Contracts;
using Taskboard.Api.Errors;
using Taskboard.Api.Routing;
using Taskboard.Api.Store;

namespace Taskboard.Api;

/// <summary>Entry point. Also the composition root used by the test host.</summary>
public sealed partial class Program
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    private Program()
    {
    }

    /// <summary>Process entry point.</summary>
    public static void Main(string[] args) => Build(args).Run();

    /// <summary>
    /// Builds the application without starting it. The test host calls this, so
    /// tests exercise exactly the pipeline that production runs.
    /// </summary>
    internal static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var startupConfig =
            builder.Configuration.GetSection(AppConfig.SectionName).Get<AppConfig>() ?? new AppConfig();

        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Limits.MaxRequestBodySize = startupConfig.MaxRequestBodyBytes);

        builder.Services
            .AddOptions<AppConfig>()
            .Bind(builder.Configuration.GetSection(AppConfig.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<AppConfig>, AppConfigValidator>();

        // Enums cross the wire as camelCase names - "inProgress", not 1 - so the
        // JSON contract survives a reordering of the enum members.
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        // Features that need the current time inject TimeProvider. Nothing calls
        // DateTime.UtcNow directly, so a test can reason about "today".
        builder.Services.AddSingleton(TimeProvider.System);

        // The concrete stores are registered as themselves as well as behind
        // their interfaces, so the test host can reach ResetToSeed. Features see
        // only the interfaces.
        builder.Services.AddSingleton<InMemoryTaskStore>();
        builder.Services.AddSingleton<InMemoryTaskListStore>();
        builder.Services.AddSingleton<ITaskStore>(sp => sp.GetRequiredService<InMemoryTaskStore>());
        builder.Services.AddSingleton<ITaskListStore>(sp => sp.GetRequiredService<InMemoryTaskListStore>());

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AppExceptionHandler>();
        builder.Services.AddOpenApi();

        var app = builder.Build();

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapOpenApi();

        // Scalar renders the document produced above by Microsoft.AspNetCore.OpenApi.
        // It is a reader only - it generates nothing - so there is exactly one source of
        // truth for the document. Its JavaScript bundle is served by this app from
        // /scalar/scalar.js, not fetched from a CDN, so the page works offline.
        app.MapScalarApiReference(options => options
            .WithTitle("Taskboard API")
            .WithOpenApiRoutePattern("/openapi/{documentName}.json"));

        app.MapGet("/health", () => new HealthPayload(
                Status: "ok",
                UptimeSeconds: (long)(DateTimeOffset.UtcNow - StartedAt).TotalSeconds,
                Timestamp: DateTimeOffset.UtcNow))
            .WithName("Health")
            .WithTags("system");

        var routes = app.CreateFeatureGroup();
        RegisterFeatures(routes);

        app.MapFallback(FallbackToNotFound);

        return app;
    }

    private static IResult FallbackToNotFound(HttpContext context) =>
        throw AppException.NotFound($"No route for {context.Request.Method} {context.Request.Path}");

    // ---- FEATURE REGISTRATION ----
    // Agents: add exactly one call below.
    // Keep alphabetical. Do not restructure.
    private static void RegisterFeatures(IEndpointRouteBuilder app)
    {
        Features.Lists.ListsFeature.Register(app);
        Features.Tasks.TasksFeature.Register(app);
    }
    // ---- END FEATURE REGISTRATION ----
}
