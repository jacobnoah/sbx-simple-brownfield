// Tests for the Lists feature.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Taskboard.Api.Tests.Features.Lists;

[Collection(ApiCollection.Name)]
public sealed class ListsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private HttpClient NewClient()
    {
        _factory.ResetBoard();
        return _factory.CreateClient();
    }

    private static Uri Relative(string path) => new(path, UriKind.Relative);

    private static IReadOnlyList<string> Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!)];

    [Fact]
    public async Task ListReturnsEverySeededListWithItsCounts()
    {
        using var client = NewClient();

        var page = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/lists"), TestContext.Current.CancellationToken);

        Assert.Equal(4, page.GetProperty("total").GetInt32());

        var ids = Ids(page);

        Assert.Equal("errands", ids[0]);
        Assert.Equal("home", ids[1]);
        Assert.Equal("inbox", ids[2]);
        Assert.Equal("work", ids[3]);

        var counts = page.GetProperty("items").EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("id").GetString()!,
                item => item.GetProperty("taskCount").GetInt32(),
                StringComparer.Ordinal);

        Assert.Equal(4, counts["errands"]);
        Assert.Equal(7, counts["home"]);
        Assert.Equal(3, counts["inbox"]);
        Assert.Equal(6, counts["work"]);
        Assert.Equal(20, counts.Values.Sum());
    }

    [Fact]
    public async Task GetReportsOpenTasksSeparately()
    {
        using var client = NewClient();

        var list = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/lists/errands"), TestContext.Current.CancellationToken);

        Assert.Equal("errands", list.GetProperty("id").GetString());
        Assert.Equal("Errands", list.GetProperty("name").GetString());
        Assert.Equal(4, list.GetProperty("taskCount").GetInt32());

        // One is done and one is cancelled, so two of the four are still open.
        Assert.Equal(2, list.GetProperty("openTaskCount").GetInt32());
    }

    [Fact]
    public async Task GetUnknownListReturnsProblemDetailsNotFound()
    {
        using var client = NewClient();

        using var response = await client.GetAsync(
            Relative("/api/lists/no-such-list"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal("NOT_FOUND", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateDerivesASlugAndRejectsADuplicate()
    {
        using var client = NewClient();

        using var created = await client.PostAsJsonAsync(
            Relative("/api/lists"), new { name = "  Side Project  " }, TestContext.Current.CancellationToken);

        var list = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/lists/side-project", created.Headers.Location?.ToString());
        Assert.Equal("side-project", list.GetProperty("id").GetString());
        Assert.Equal("Side Project", list.GetProperty("name").GetString());
        Assert.Equal(0, list.GetProperty("taskCount").GetInt32());

        using var duplicate = await client.PostAsJsonAsync(
            Relative("/api/lists"), new { name = "side project" }, TestContext.Current.CancellationToken);

        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("CONFLICT", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateRejectsABadName()
    {
        using var client = NewClient();

        using var missing = await client.PostAsJsonAsync(
            Relative("/api/lists"), new { }, TestContext.Current.CancellationToken);
        using var blank = await client.PostAsJsonAsync(
            Relative("/api/lists"), new { name = "   " }, TestContext.Current.CancellationToken);
        using var punctuation = await client.PostAsJsonAsync(
            Relative("/api/lists"), new { name = "***" }, TestContext.Current.CancellationToken);
        using var tooLong = await client.PostAsJsonAsync(
            Relative("/api/lists"), new { name = new string('x', 41) }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, punctuation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task RenameKeepsTheId()
    {
        using var client = NewClient();

        using var response = await client.PatchAsJsonAsync(
            Relative("/api/lists/work"), new { name = "Day job" }, TestContext.Current.CancellationToken);

        var list = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("work", list.GetProperty("id").GetString());
        Assert.Equal("Day job", list.GetProperty("name").GetString());

        using var blank = await client.PatchAsJsonAsync(
            Relative("/api/lists/work"), new { name = "" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task TasksOnListAreFilteredAndPaged()
    {
        using var client = NewClient();

        var all = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/lists/home/tasks"), TestContext.Current.CancellationToken);
        var todo = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/lists/home/tasks?status=todo"), TestContext.Current.CancellationToken);
        var paged = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/lists/home/tasks?limit=2&offset=1"), TestContext.Current.CancellationToken);

        Assert.Equal(7, all.GetProperty("total").GetInt32());
        Assert.Equal("book-dentist", Ids(all)[0]);
        Assert.Equal(5, todo.GetProperty("total").GetInt32());
        Assert.Equal(7, paged.GetProperty("total").GetInt32());
        Assert.Equal(2, paged.GetProperty("items").GetArrayLength());
        Assert.Equal("clean-the-gutters", Ids(paged)[0]);
    }

    [Fact]
    public async Task TasksOnListRejectsBadParameters()
    {
        using var client = NewClient();

        using var unknownList = await client.GetAsync(
            Relative("/api/lists/no-such-list/tasks"), TestContext.Current.CancellationToken);
        using var badStatus = await client.GetAsync(
            Relative("/api/lists/home/tasks?status=nonsense"), TestContext.Current.CancellationToken);
        using var badLimit = await client.GetAsync(
            Relative("/api/lists/home/tasks?limit=0"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, unknownList.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);
    }

    [Fact]
    public async Task DeleteRemovesTheList()
    {
        using var client = NewClient();

        using var created = await client.PostAsJsonAsync(
            Relative("/api/lists"), new { name = "Holiday" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var deleted = await client.DeleteAsync(
            Relative("/api/lists/holiday"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var gone = await client.GetAsync(
            Relative("/api/lists/holiday"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        var page = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/lists"), TestContext.Current.CancellationToken);

        Assert.Equal(4, page.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task DeleteRejectsTheInboxAndUnknownDestinations()
    {
        using var client = NewClient();

        using var inbox = await client.DeleteAsync(
            Relative("/api/lists/inbox"), TestContext.Current.CancellationToken);
        using var unknown = await client.DeleteAsync(
            Relative("/api/lists/no-such-list"), TestContext.Current.CancellationToken);
        using var unknownDestination = await client.DeleteAsync(
            Relative("/api/lists/work?reassignTo=nowhere"), TestContext.Current.CancellationToken);
        using var itself = await client.DeleteAsync(
            Relative("/api/lists/work?reassignTo=work"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, inbox.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownDestination.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, itself.StatusCode);
    }
}
