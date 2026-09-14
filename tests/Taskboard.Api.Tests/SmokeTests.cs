// Proves the scaffold boots and the shared error contract holds.
//
// SHARED FILE - read-only for feature agents. Write your own tests under
// tests/Taskboard.Api.Tests/Features/<Name>/ instead. See AGENTS.md.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Taskboard.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class SmokeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task HealthEndpointReportsOk()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal("ok", payload.GetProperty("status").GetString());
        Assert.True(payload.GetProperty("uptimeSeconds").GetInt64() >= 0);
        Assert.True(payload.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task UnknownRouteReturnsProblemDetailsNotFound()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/definitely-not-a-route", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("NOT_FOUND", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApiDocumentIsServed()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Scalar renders 3.1 natively. Pinning the version here stops a future
        // change to the document from silently breaking the reference page.
        Assert.StartsWith("3.1", document.GetProperty("openapi").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScalarReferenceIsServed()
    {
        using var client = _factory.CreateClient();

        // The bare /scalar path redirects; the client follows it.
        using var response = await client.GetAsync(new Uri("/scalar", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("<title>Taskboard API</title>", html, StringComparison.Ordinal);

        // Scalar emits the document URL relative to the computed app root, then
        // rebases it in the browser. Asserting the relative form is what actually
        // ships in the page.
        Assert.Contains("openapi/v1.json", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScalarBundleIsServedLocallyRatherThanFromACdn()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/scalar/scalar.js", UriKind.Relative), TestContext.Current.CancellationToken);

        // The whole reference UI must work with no outbound network access.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Content.Headers.ContentLength > 100_000);
    }
}
