// Tests for the Tasks feature.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Taskboard.Api.Store;

namespace Taskboard.Api.Tests.Features.Tasks;

[Collection(ApiCollection.Name)]
public sealed class TasksTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private HttpClient NewClient()
    {
        _factory.ResetBoard();
        return _factory.CreateClient();
    }

    private static Uri Relative(string path) => new(path, UriKind.Relative);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!)];

    [Fact]
    public async Task ListReturnsTheWholeSeededBoardOnOnePage()
    {
        using var client = NewClient();

        var page = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks"), TestContext.Current.CancellationToken);

        Assert.Equal(20, page.GetProperty("total").GetInt32());
        Assert.Equal(20, page.GetProperty("items").GetArrayLength());
        Assert.Equal(25, page.GetProperty("limit").GetInt32());
        Assert.Equal(0, page.GetProperty("offset").GetInt32());
    }

    [Fact]
    public async Task ListDefaultsToIdOrder()
    {
        using var client = NewClient();

        var page = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks"), TestContext.Current.CancellationToken);

        var ids = Ids(page);

        Assert.Equal("archive-old-invoices", ids[0]);
        Assert.Equal("water-the-plants", ids[^1]);
        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal).ToList(), ids);
    }

    [Fact]
    public async Task ListPagesWithoutChangingTotal()
    {
        using var client = NewClient();

        var page = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks?limit=5&offset=5"), TestContext.Current.CancellationToken);

        Assert.Equal(20, page.GetProperty("total").GetInt32());
        Assert.Equal(5, page.GetProperty("items").GetArrayLength());
        Assert.Equal("draft-q3-report", Ids(page)[0]);
    }

    [Theory]
    [InlineData("/api/tasks?limit=0")]
    [InlineData("/api/tasks?limit=101")]
    [InlineData("/api/tasks?offset=-1")]
    [InlineData("/api/tasks?status=nonsense")]
    [InlineData("/api/tasks?priority=nonsense")]
    [InlineData("/api/tasks?sort=nonsense")]
    [InlineData("/api/tasks?dueBefore=not-a-date")]
    [InlineData("/api/tasks?q=")]
    public async Task ListRejectsBadParameters(string path)
    {
        using var client = NewClient();

        using var response = await client.GetAsync(Relative(path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal("BAD_REQUEST", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListFiltersByStatusPriorityAndList()
    {
        using var client = NewClient();

        var todo = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks?status=todo"), TestContext.Current.CancellationToken);
        var urgent = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks?priority=urgent"), TestContext.Current.CancellationToken);
        var work = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks?listId=work"), TestContext.Current.CancellationToken);

        Assert.Equal(13, todo.GetProperty("total").GetInt32());
        Assert.Equal(6, work.GetProperty("total").GetInt32());
        Assert.Equal(2, urgent.GetProperty("total").GetInt32());
        Assert.Equal("pay-council-tax", Ids(urgent)[0]);
        Assert.Equal("renew-passport", Ids(urgent)[1]);
    }

    [Fact]
    public async Task ListSearchesTitleAndNotesCaseInsensitively()
    {
        using var client = NewClient();

        var byTitle = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks?q=RENEW"), TestContext.Current.CancellationToken);
        var byNotes = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks?q=ladder"), TestContext.Current.CancellationToken);
        var noMatch = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks?q=zzzznothing"), TestContext.Current.CancellationToken);

        Assert.Equal(2, byTitle.GetProperty("total").GetInt32());
        Assert.Equal("renew-car-insurance", Ids(byTitle)[0]);
        Assert.Equal("renew-passport", Ids(byTitle)[1]);
        Assert.Equal("clean-the-gutters", Assert.Single(Ids(byNotes)));
        Assert.Equal(0, noMatch.GetProperty("total").GetInt32());
        Assert.Empty(noMatch.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ListSortsDatedTasksByDueDateAscending()
    {
        using var client = NewClient();

        // dueBefore leaves only tasks that have a due date at all.
        var page = await client.GetFromJsonAsync<JsonElement>(
            Relative($"/api/tasks?sort=due&dueBefore={Iso(SeedData.Today.AddDays(365))}"),
            TestContext.Current.CancellationToken);

        var dueDates = page.GetProperty("items").EnumerateArray()
            .Select(item => DateOnly.Parse(item.GetProperty("dueOn").GetString()!, CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(17, dueDates.Count);
        Assert.Equal(dueDates.Order().ToList(), dueDates);
        Assert.Equal("fix-leaking-tap", Ids(page)[0]);
    }

    [Fact]
    public async Task ListSortsByTitleAndByCreated()
    {
        using var client = NewClient();

        var byTitle = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks?sort=title"), TestContext.Current.CancellationToken);
        var byCreated = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks?sort=created"), TestContext.Current.CancellationToken);

        Assert.Equal("archive-old-invoices", Ids(byTitle)[0]);
        Assert.Equal("water-the-plants", Ids(byTitle)[^1]);

        // Newest first: buy-milk and review-pull-requests were both created yesterday.
        Assert.True(Ids(byCreated)[0] is "buy-milk" or "review-pull-requests");
        Assert.Equal("unsubscribe-newsletters", Ids(byCreated)[^1]);
    }

    [Fact]
    public async Task GetReturnsOneTask()
    {
        using var client = NewClient();

        var task = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks/pay-council-tax"), TestContext.Current.CancellationToken);

        Assert.Equal("pay-council-tax", task.GetProperty("id").GetString());
        Assert.Equal("Pay the council tax", task.GetProperty("title").GetString());
        Assert.Equal("home", task.GetProperty("listId").GetString());
        Assert.Equal("todo", task.GetProperty("status").GetString());
        Assert.Equal("urgent", task.GetProperty("priority").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("completedAt").ValueKind);
    }

    [Fact]
    public async Task GetUnknownTaskReturnsProblemDetailsNotFound()
    {
        using var client = NewClient();

        using var response = await client.GetAsync(
            Relative("/api/tasks/no-such-task"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal("NOT_FOUND", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateDerivesASlugAndDefaultsToTheInbox()
    {
        using var client = NewClient();

        using var response = await client.PostAsJsonAsync(
            Relative("/api/tasks"),
            new { title = "  Take the Bins Out!  " },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/tasks/take-the-bins-out", response.Headers.Location?.ToString());

        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal("take-the-bins-out", created.GetProperty("id").GetString());
        Assert.Equal("Take the Bins Out!", created.GetProperty("title").GetString());
        Assert.Equal("inbox", created.GetProperty("listId").GetString());
        Assert.Equal("todo", created.GetProperty("status").GetString());
        Assert.Equal("normal", created.GetProperty("priority").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("notes").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("dueOn").ValueKind);
    }

    [Fact]
    public async Task CreateSuffixesAClashingSlug()
    {
        using var client = NewClient();

        using var response = await client.PostAsJsonAsync(
            Relative("/api/tasks"),
            new { title = "Buy milk", listId = "errands", priority = "high" },
            TestContext.Current.CancellationToken);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("buy-milk-2", created.GetProperty("id").GetString());
        Assert.Equal("errands", created.GetProperty("listId").GetString());
        Assert.Equal("high", created.GetProperty("priority").GetString());
    }

    [Fact]
    public async Task CreateRejectsABadTitleOrList()
    {
        using var client = NewClient();

        using var noTitle = await client.PostAsJsonAsync(
            Relative("/api/tasks"), new { notes = "orphan" }, TestContext.Current.CancellationToken);
        using var blankTitle = await client.PostAsJsonAsync(
            Relative("/api/tasks"), new { title = "   " }, TestContext.Current.CancellationToken);
        using var punctuationTitle = await client.PostAsJsonAsync(
            Relative("/api/tasks"), new { title = "!!!" }, TestContext.Current.CancellationToken);
        using var longTitle = await client.PostAsJsonAsync(
            Relative("/api/tasks"), new { title = new string('x', 121) }, TestContext.Current.CancellationToken);
        using var unknownList = await client.PostAsJsonAsync(
            Relative("/api/tasks"),
            new { title = "Somewhere else", listId = "no-such-list" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, noTitle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blankTitle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, punctuationTitle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longTitle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownList.StatusCode);
    }

    [Fact]
    public async Task PatchLeavesOmittedFieldsAloneAndClearsExplicitNulls()
    {
        using var client = NewClient();

        using var renamed = await client.PatchAsJsonAsync(
            Relative("/api/tasks/clean-the-gutters"),
            new { title = "Clean the gutters properly" },
            TestContext.Current.CancellationToken);

        var afterRename = await renamed.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Clean the gutters properly", afterRename.GetProperty("title").GetString());
        Assert.Equal("Borrow the long ladder.", afterRename.GetProperty("notes").GetString());
        Assert.Equal("blocked", afterRename.GetProperty("status").GetString());

        using var cleared = await client.PatchAsJsonAsync(
            Relative("/api/tasks/clean-the-gutters"),
            new { notes = (string?)null, dueOn = (string?)null },
            TestContext.Current.CancellationToken);

        var afterClear = await cleared.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(JsonValueKind.Null, afterClear.GetProperty("notes").ValueKind);
        Assert.Equal(JsonValueKind.Null, afterClear.GetProperty("dueOn").ValueKind);
        Assert.Equal("Clean the gutters properly", afterClear.GetProperty("title").GetString());
    }

    [Fact]
    public async Task PatchMovesAndRepricesATask()
    {
        using var client = NewClient();

        using var response = await client.PatchAsJsonAsync(
            Relative("/api/tasks/read-onboarding-docs"),
            new { listId = "work", priority = "high", dueOn = Iso(SeedData.Today.AddDays(4)) },
            TestContext.Current.CancellationToken);

        var patched = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("work", patched.GetProperty("listId").GetString());
        Assert.Equal("high", patched.GetProperty("priority").GetString());
        Assert.Equal(Iso(SeedData.Today.AddDays(4)), patched.GetProperty("dueOn").GetString());
    }

    [Fact]
    public async Task PatchRejectsUnknownFieldsAndEmptyBodies()
    {
        using var client = NewClient();

        using var unknown = await client.PatchAsJsonAsync(
            Relative("/api/tasks/buy-milk"), new { colour = "red" }, TestContext.Current.CancellationToken);
        using var empty = await client.PatchAsJsonAsync(
            Relative("/api/tasks/buy-milk"), new { }, TestContext.Current.CancellationToken);
        using var nullList = await client.PatchAsJsonAsync(
            Relative("/api/tasks/buy-milk"),
            new { listId = (string?)null },
            TestContext.Current.CancellationToken);
        using var badStatus = await client.PatchAsJsonAsync(
            Relative("/api/tasks/buy-milk"), new { status = "nonsense" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nullList.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
    }

    [Fact]
    public async Task PatchingStatusToDoneStampsCompletedAt()
    {
        using var client = NewClient();

        using var response = await client.PatchAsJsonAsync(
            Relative("/api/tasks/buy-milk"), new { status = "done" }, TestContext.Current.CancellationToken);

        var patched = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal("done", patched.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, patched.GetProperty("completedAt").ValueKind);
    }

    [Fact]
    public async Task CompleteIsRejectedTwice()
    {
        using var client = NewClient();

        using var first = await client.PostAsync(
            Relative("/api/tasks/file-expenses/complete"), content: null, TestContext.Current.CancellationToken);

        var completed = await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("done", completed.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, completed.GetProperty("completedAt").ValueKind);

        using var second = await client.PostAsync(
            Relative("/api/tasks/file-expenses/complete"), content: null, TestContext.Current.CancellationToken);

        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("CONFLICT", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReopenClearsCompletedAtAndRejectsOpenTasks()
    {
        using var client = NewClient();

        using var reopened = await client.PostAsync(
            Relative("/api/tasks/submit-timesheet/reopen"), content: null, TestContext.Current.CancellationToken);

        var task = await reopened.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal("todo", task.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("completedAt").ValueKind);

        using var again = await client.PostAsync(
            Relative("/api/tasks/submit-timesheet/reopen"), content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task ReopenAcceptsACancelledTask()
    {
        using var client = NewClient();

        using var response = await client.PostAsync(
            Relative("/api/tasks/cancel-gym-membership/reopen"),
            content: null,
            TestContext.Current.CancellationToken);

        var task = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("todo", task.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DeleteRemovesTheTaskOnceOnly()
    {
        using var client = NewClient();

        using var first = await client.DeleteAsync(
            Relative("/api/tasks/order-printer-ink"), TestContext.Current.CancellationToken);
        using var second = await client.DeleteAsync(
            Relative("/api/tasks/order-printer-ink"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);

        var page = await client.GetFromJsonAsync<JsonElement>(
            Relative("/api/tasks"), TestContext.Current.CancellationToken);

        Assert.Equal(19, page.GetProperty("total").GetInt32());
    }
}
