// The routing surface features register against.
//
// SHARED FILE - read-only for feature agents.
//
// A feature exposes one static Register(IEndpointRouteBuilder) method and maps
// its endpoints onto the builder it is handed. It must not build its own
// WebApplication, add global middleware, or map anything outside its own route
// space. See AGENTS.md.

using Microsoft.Extensions.Options;

using Taskboard.Api.Configuration;

namespace Taskboard.Api.Routing;

/// <summary>The exact shape of a feature's registration method.</summary>
/// <param name="routes">The builder the feature maps its endpoints onto.</param>
public delegate void FeatureRegistration(IEndpointRouteBuilder routes);

/// <summary>Builds the route group that every feature is registered into.</summary>
public static class FeatureRoutes
{
    /// <summary>
    /// Creates the shared feature route group, mounted at
    /// <see cref="AppConfig.FeatureRoutePrefix"/>.
    /// </summary>
    public static RouteGroupBuilder CreateFeatureGroup(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var config = app.Services.GetRequiredService<IOptions<AppConfig>>().Value;

        return app.MapGroup(config.FeatureRoutePrefix)
            .WithTags("features");
    }
}
