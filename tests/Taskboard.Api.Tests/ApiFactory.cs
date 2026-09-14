// Shared test host.
//
// SHARED FILE - read-only for feature agents.
//
// It boots the real application pipeline once and hands out HttpClients. Feature
// tests join the [Collection] below instead of building their own host.
//
// The board is mutable, so unlike an immutable-catalogue blueprint the host
// carries state between tests. Call ResetBoard() at the top of every test that
// reads or writes the seeded data. Tests in one collection run one at a time, so
// a reset is safe. See AGENTS.md.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Taskboard.Api.Store;

namespace Taskboard.Api.Tests;

/// <summary>Boots <see cref="global::Taskboard.Api.Program"/> in-memory for tests.</summary>
public sealed class ApiFactory : WebApplicationFactory<global::Taskboard.Api.Program>
{
    /// <summary>
    /// Throws away every change made to the board and restores the seed. Call it
    /// first thing in any test whose expectations depend on the seeded data.
    /// </summary>
    public void ResetBoard()
    {
        Services.GetRequiredService<InMemoryTaskStore>().ResetToSeed();
        Services.GetRequiredService<InMemoryTaskListStore>().ResetToSeed();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");

        // Pin the settings tests assert against, so a change to appsettings.json
        // defaults cannot silently break unrelated feature tests.
        builder.UseSetting("App:FeatureRoutePrefix", "/api");
        builder.UseSetting("App:DefaultPageSize", "25");
        builder.UseSetting("App:MaxPageSize", "100");
        builder.UseSetting("App:MaxRequestBodyBytes", "1048576");
    }
}

/// <summary>
/// Test collection sharing one <see cref="ApiFactory"/>. Every test class -
/// including every feature's - should carry <c>[Collection(ApiCollection.Name)]</c>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    /// <summary>Name referenced by <c>[Collection(...)]</c>.</summary>
    public const string Name = "api";
}
