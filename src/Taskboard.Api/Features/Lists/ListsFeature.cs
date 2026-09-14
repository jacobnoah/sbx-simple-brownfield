// Lists: the groupings tasks are filed under.
//
// Owns /lists, /lists/{listId} and /lists/{listId}/tasks. See the route
// ownership table in AGENTS.md.

using System.Text.Json;

using Microsoft.Extensions.Options;

using Taskboard.Api.Configuration;
using Taskboard.Api.Contracts;
using Taskboard.Api.Domain;
using Taskboard.Api.Errors;
using Taskboard.Api.Routing;
using Taskboard.Api.Store;

namespace Taskboard.Api.Features.Lists;

/// <summary>Registers the list endpoints.</summary>
internal static class ListsFeature
{
    private const int MaxNameLength = 40;

    public static void Register(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/lists", List)
            .WithName("ListsList")
            .WithSummary("List every task list, with its task counts.")
            .Produces<Page<TaskListSummary>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        routes.MapPost("/lists", Create)
            .WithName("ListsCreate")
            .WithSummary("Create a task list. The id is derived from the name.")
            .Produces<TaskListSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        routes.MapGet("/lists/{listId}", Get)
            .WithName("ListsGet")
            .WithSummary("Fetch one task list by id.")
            .Produces<TaskListSummary>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        routes.MapPatch("/lists/{listId}", Rename)
            .WithName("ListsRename")
            .WithSummary("Rename a task list. The id never changes.")
            .Produces<TaskListSummary>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        routes.MapDelete("/lists/{listId}", Delete)
            .WithName("ListsDelete")
            .WithSummary("Delete a task list and move its tasks to another list.")
            .WithDescription(
                "Tasks are never deleted with the list: every task on it moves to " +
                "reassignTo, which defaults to the inbox. The inbox itself cannot be deleted.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        routes.MapGet("/lists/{listId}/tasks", TasksOnList)
            .WithName("ListsTasks")
            .WithSummary("List the tasks filed under one task list, by id ascending.")
            .Produces<Page<TaskItem>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static Page<TaskListSummary> List(
        int? limit,
        int? offset,
        ITaskListStore lists,
        ITaskStore tasks,
        IOptions<AppConfig> options)
    {
        var page = PageRequest.From(limit, offset, options.Value);
        var all = tasks.Tasks;

        return page.Apply([.. lists.Lists.Select(list => Summarise(list, all))]);
    }

    private static TaskListSummary Get(string listId, ITaskListStore lists, ITaskStore tasks) =>
        Summarise(lists.Find(listId) ?? throw NoSuchList(listId), tasks.Tasks);

    private static IResult Create(
        CreateListRequest request,
        ITaskListStore lists,
        ITaskStore tasks,
        IOptions<AppConfig> options,
        TimeProvider clock)
    {
        if (request is null)
        {
            throw AppException.BadRequest("A request body is required.");
        }

        var name = RequireName(request.Name);
        var id = Slug.From(name);

        if (id.Length is 0)
        {
            throw AppException.BadRequest("name must contain at least one letter or digit.", new { name });
        }

        if (lists.Find(id) is not null)
        {
            throw AppException.Conflict($"A list with id '{id}' already exists.", new { listId = id });
        }

        var list = new TaskList(id, name, clock.GetUtcNow());

        if (!lists.TryAdd(list))
        {
            throw AppException.Conflict($"A list with id '{id}' already exists.", new { listId = id });
        }

        var prefix = options.Value.FeatureRoutePrefix;

        return Results.Created($"{prefix}/lists/{list.Id}", Summarise(list, tasks.Tasks));
    }

    private static TaskListSummary Rename(
        string listId,
        JsonElement body,
        ITaskListStore lists,
        ITaskStore tasks)
    {
        var existing = lists.Find(listId) ?? throw NoSuchList(listId);

        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("name", out var name))
        {
            throw AppException.BadRequest("The request body must be a JSON object carrying a name.");
        }

        if (name.ValueKind != JsonValueKind.String)
        {
            throw AppException.BadRequest("name must be a string.");
        }

        var updated = existing with { Name = RequireName(name.GetString()) };

        if (!lists.TryReplace(updated))
        {
            throw NoSuchList(listId);
        }

        return Summarise(updated, tasks.Tasks);
    }

    private static IResult Delete(
        string listId,
        string? reassignTo,
        ITaskListStore lists,
        ITaskStore tasks)
    {
        var existing = lists.Find(listId) ?? throw NoSuchList(listId);

        if (string.Equals(existing.Id, TaskList.InboxId, StringComparison.Ordinal))
        {
            throw AppException.Conflict("The inbox cannot be deleted.", new { listId });
        }

        var destinationId = string.IsNullOrWhiteSpace(reassignTo) ? TaskList.InboxId : reassignTo.Trim();

        if (string.Equals(destinationId, existing.Id, StringComparison.Ordinal))
        {
            throw AppException.BadRequest("reassignTo cannot be the list being deleted.", new { reassignTo });
        }

        if (lists.Find(destinationId) is null)
        {
            throw AppException.BadRequest($"No list '{destinationId}'.", new { reassignTo = destinationId });
        }

        if (!lists.Remove(existing.Id))
        {
            throw NoSuchList(listId);
        }

        return Results.NoContent();
    }

    private static Page<TaskItem> TasksOnList(
        string listId,
        string? status,
        int? limit,
        int? offset,
        ITaskListStore lists,
        ITaskStore tasks,
        IOptions<AppConfig> options)
    {
        _ = lists.Find(listId) ?? throw NoSuchList(listId);

        var page = PageRequest.From(limit, offset, options.Value);
        var wanted = ParseStatus(status);

        var matched = tasks.Tasks
            .Where(task => string.Equals(task.ListId, listId, StringComparison.Ordinal))
            .Where(task => wanted is null || task.Status == wanted)
            .ToList();

        return page.Apply(matched);
    }

    private static TaskListSummary Summarise(TaskList list, IReadOnlyList<TaskItem> tasks)
    {
        var onList = tasks.Where(task => string.Equals(task.ListId, list.Id, StringComparison.Ordinal)).ToList();

        return new TaskListSummary(
            list.Id,
            list.Name,
            list.CreatedAt,
            onList.Count,
            onList.Count(task => task.Status is not (TaskState.Done or TaskState.Cancelled)));
    }

    private static TaskState? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        if (Enum.TryParse<TaskState>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw AppException.BadRequest(
            $"status must be one of: {string.Join(", ", Enum.GetNames<TaskState>())}.",
            new { status });
    }

    private static string RequireName(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength)
        {
            throw AppException.BadRequest(
                $"name is required and must be between 1 and {MaxNameLength} characters.",
                new { length = trimmed?.Length ?? 0 });
        }

        return trimmed;
    }

    private static AppException NoSuchList(string listId) =>
        AppException.NotFound($"No list '{listId}'.", new { listId });
}
