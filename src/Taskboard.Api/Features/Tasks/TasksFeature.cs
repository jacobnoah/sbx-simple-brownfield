// Tasks: the core CRUD surface of the board.
//
// Owns /tasks, /tasks/{taskId}, /tasks/{taskId}/complete and
// /tasks/{taskId}/reopen. See the route ownership table in AGENTS.md.

using System.Text.Json;

using Microsoft.Extensions.Options;

using Taskboard.Api.Configuration;
using Taskboard.Api.Contracts;
using Taskboard.Api.Domain;
using Taskboard.Api.Errors;
using Taskboard.Api.Routing;
using Taskboard.Api.Store;

namespace Taskboard.Api.Features.Tasks;

/// <summary>Registers the task endpoints.</summary>
internal static class TasksFeature
{
    private const int MaxTitleLength = 120;
    private const int MaxNotesLength = 2000;

    public static void Register(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/tasks", List)
            .WithName("TasksList")
            .WithSummary("List tasks, filtered, sorted and paged.")
            .WithDescription(
                "sort accepts id (default), created, due, priority and title. " +
                "Tasks with no due date sort last under due and priority.")
            .Produces<Page<TaskItem>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        routes.MapPost("/tasks", Create)
            .WithName("TasksCreate")
            .WithSummary("Create a task. The id is derived from the title.")
            .Produces<TaskItem>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        routes.MapGet("/tasks/{taskId}", Get)
            .WithName("TasksGet")
            .WithSummary("Fetch one task by id.")
            .Produces<TaskItem>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        routes.MapPatch("/tasks/{taskId}", Patch)
            .WithName("TasksPatch")
            .WithSummary("Update a task. Omitted fields are left alone; an explicit null clears notes or dueOn.")
            .Produces<TaskItem>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        routes.MapDelete("/tasks/{taskId}", Delete)
            .WithName("TasksDelete")
            .WithSummary("Delete a task.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        routes.MapPost("/tasks/{taskId}/complete", Complete)
            .WithName("TasksComplete")
            .WithSummary("Mark a task done and stamp completedAt.")
            .Produces<TaskItem>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        routes.MapPost("/tasks/{taskId}/reopen", Reopen)
            .WithName("TasksReopen")
            .WithSummary("Return a done or cancelled task to todo and clear completedAt.")
            .Produces<TaskItem>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static Page<TaskItem> List(
        string? status,
        string? priority,
        string? listId,
        string? q,
        string? dueBefore,
        string? sort,
        int? limit,
        int? offset,
        ITaskStore tasks,
        IOptions<AppConfig> options)
    {
        var filter = TaskQuery.Parse(status, priority, listId, q, dueBefore, sort);
        var page = PageRequest.From(limit, offset, options.Value);

        return page.Apply(TaskQuery.Apply(tasks.Tasks, filter));
    }

    private static TaskItem Get(string taskId, ITaskStore tasks) =>
        tasks.Find(taskId) ?? throw NoSuchTask(taskId);

    private static IResult Create(
        CreateTaskRequest request,
        ITaskStore tasks,
        ITaskListStore lists,
        IOptions<AppConfig> options,
        TimeProvider clock)
    {
        if (request is null)
        {
            throw AppException.BadRequest("A request body is required.");
        }

        var title = RequireTitle(request.Title);
        var notes = NormaliseNotes(request.Notes);
        var listIdentifier = RequireList(request.ListId ?? TaskList.InboxId, lists);
        var priority = request.Priority ?? TaskPriority.Normal;

        if (!Enum.IsDefined(priority))
        {
            throw AppException.BadRequest(
                $"priority must be one of: {string.Join(", ", Enum.GetNames<TaskPriority>())}.");
        }

        var slug = Slug.From(title);

        if (slug.Length is 0)
        {
            throw AppException.BadRequest("title must contain at least one letter or digit.", new { title });
        }

        var now = clock.GetUtcNow();

        var task = new TaskItem(
            FreeId(slug, tasks),
            title,
            notes,
            listIdentifier,
            TaskState.Todo,
            priority,
            request.DueOn,
            now,
            now,
            CompletedAt: null);

        if (!tasks.TryAdd(task))
        {
            throw AppException.Conflict($"A task with id '{task.Id}' already exists.");
        }

        var prefix = options.Value.FeatureRoutePrefix;

        return Results.Created($"{prefix}/tasks/{task.Id}", task);
    }

    private static TaskItem Patch(
        string taskId,
        JsonElement body,
        ITaskStore tasks,
        ITaskListStore lists,
        TimeProvider clock)
    {
        var existing = tasks.Find(taskId) ?? throw NoSuchTask(taskId);
        var patch = TaskPatch.Parse(body);

        if (patch.IsEmpty)
        {
            throw AppException.BadRequest("The request body must change at least one field.");
        }

        var updated = existing;

        if (patch.HasTitle)
        {
            updated = updated with { Title = RequireTitle(patch.Title) };
        }

        if (patch.HasNotes)
        {
            updated = updated with { Notes = NormaliseNotes(patch.Notes) };
        }

        if (patch.HasListId)
        {
            if (patch.ListId is null)
            {
                throw AppException.BadRequest("listId cannot be null; every task belongs to a list.");
            }

            updated = updated with { ListId = RequireList(patch.ListId, lists) };
        }

        if (patch.HasPriority)
        {
            updated = updated with { Priority = patch.Priority };
        }

        if (patch.HasDueOn)
        {
            updated = updated with { DueOn = patch.DueOn };
        }

        var now = clock.GetUtcNow();

        if (patch.HasStatus && patch.Status != existing.Status)
        {
            updated = updated with
            {
                Status = patch.Status,
                CompletedAt = patch.Status is TaskState.Done ? now : null,
            };
        }

        updated = updated with { UpdatedAt = now };

        if (!tasks.TryReplace(updated))
        {
            throw NoSuchTask(taskId);
        }

        return updated;
    }

    private static IResult Delete(string taskId, ITaskStore tasks) =>
        tasks.Remove(taskId) ? Results.NoContent() : throw NoSuchTask(taskId);

    private static TaskItem Complete(string taskId, ITaskStore tasks, TimeProvider clock)
    {
        var existing = tasks.Find(taskId) ?? throw NoSuchTask(taskId);

        if (existing.Status is TaskState.Done)
        {
            throw AppException.Conflict($"Task '{taskId}' is already done.");
        }

        var now = clock.GetUtcNow();
        var updated = existing with { Status = TaskState.Done, CompletedAt = now, UpdatedAt = now };

        if (!tasks.TryReplace(updated))
        {
            throw NoSuchTask(taskId);
        }

        return updated;
    }

    private static TaskItem Reopen(string taskId, ITaskStore tasks, TimeProvider clock)
    {
        var existing = tasks.Find(taskId) ?? throw NoSuchTask(taskId);

        if (existing.Status is not (TaskState.Done or TaskState.Cancelled))
        {
            throw AppException.Conflict($"Task '{taskId}' is not done or cancelled, so there is nothing to reopen.");
        }

        var now = clock.GetUtcNow();
        var updated = existing with { Status = TaskState.Todo, CompletedAt = null, UpdatedAt = now };

        if (!tasks.TryReplace(updated))
        {
            throw NoSuchTask(taskId);
        }

        return updated;
    }

    private static string FreeId(string slug, ITaskStore tasks)
    {
        if (tasks.Find(slug) is null)
        {
            return slug;
        }

        for (var suffix = 2; suffix <= 999; suffix++)
        {
            var candidate = $"{slug}-{suffix}";

            if (tasks.Find(candidate) is null)
            {
                return candidate;
            }
        }

        throw AppException.Conflict($"Could not derive a free id from '{slug}'.");
    }

    private static string RequireTitle(string? title)
    {
        var trimmed = title?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxTitleLength)
        {
            throw AppException.BadRequest(
                $"title is required and must be between 1 and {MaxTitleLength} characters.",
                new { length = trimmed?.Length ?? 0 });
        }

        return trimmed;
    }

    private static string? NormaliseNotes(string? notes)
    {
        var trimmed = notes?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxNotesLength)
        {
            throw AppException.BadRequest(
                $"notes must be {MaxNotesLength} characters or fewer.",
                new { length = trimmed.Length });
        }

        return trimmed;
    }

    private static string RequireList(string listId, ITaskListStore lists)
    {
        var trimmed = listId.Trim();

        return lists.Find(trimmed) is not null
            ? trimmed
            : throw AppException.BadRequest($"No list '{trimmed}'.", new { listId = trimmed });
    }

    private static AppException NoSuchTask(string taskId) =>
        AppException.NotFound($"No task '{taskId}'.", new { taskId });
}
