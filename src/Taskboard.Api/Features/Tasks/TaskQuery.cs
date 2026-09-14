// Filtering and sorting for GET /tasks.

using System.Globalization;

using Taskboard.Api.Domain;
using Taskboard.Api.Errors;

namespace Taskboard.Api.Features.Tasks;

/// <summary>Sort orders accepted by <c>GET /tasks?sort=</c>.</summary>
internal enum TaskSort
{
    /// <summary>Id, ordinal ascending. The default.</summary>
    Id = 0,

    /// <summary>Newest first, then id ascending.</summary>
    Created = 1,

    /// <summary>Soonest due first, undated tasks last, then id ascending.</summary>
    Due = 2,

    /// <summary>Most urgent first, then soonest due, then id ascending.</summary>
    Priority = 3,

    /// <summary>Title, case-insensitive ascending, then id ascending.</summary>
    Title = 4,
}

/// <summary>The filters <c>GET /tasks</c> understands, already validated.</summary>
internal sealed record TaskFilter(
    TaskState? Status,
    TaskPriority? Priority,
    string? ListId,
    string? Query,
    DateOnly? DueBefore,
    TaskSort Sort);

/// <summary>Turns raw query-string values into a filter, and applies it.</summary>
internal static class TaskQuery
{
    public static TaskFilter Parse(
        string? status,
        string? priority,
        string? listId,
        string? q,
        string? dueBefore,
        string? sort) =>
        new(
            ParseEnum<TaskState>(status, "status"),
            ParseEnum<TaskPriority>(priority, "priority"),
            ParseListId(listId),
            ParseQuery(q),
            ParseDate(dueBefore, "dueBefore"),
            ParseEnum<TaskSort>(sort, "sort") ?? TaskSort.Id);

    public static IReadOnlyList<TaskItem> Apply(IReadOnlyList<TaskItem> tasks, TaskFilter filter)
    {
        var matched = tasks.Where(task => Matches(task, filter));

        return [.. Sort(matched, filter.Sort)];
    }

    private static bool Matches(TaskItem task, TaskFilter filter)
    {
        if (filter.Status is { } status && task.Status != status)
        {
            return false;
        }

        if (filter.Priority is { } priority && task.Priority != priority)
        {
            return false;
        }

        if (filter.ListId is { } listId && !string.Equals(task.ListId, listId, StringComparison.Ordinal))
        {
            return false;
        }

        if (filter.DueBefore is { } dueBefore && (task.DueOn is null || task.DueOn >= dueBefore))
        {
            return false;
        }

        if (filter.Query is { } query &&
            !task.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            !(task.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<TaskItem> Sort(IEnumerable<TaskItem> tasks, TaskSort sort) => sort switch
    {
        TaskSort.Created => tasks
            .OrderByDescending(task => task.CreatedAt)
            .ThenBy(task => task.Id, StringComparer.Ordinal),
        TaskSort.Due => tasks
            .OrderBy(task => task.DueOn)
            .ThenBy(task => task.Id, StringComparer.Ordinal),
        TaskSort.Priority => tasks
            .OrderByDescending(task => task.Priority)
            .ThenBy(task => task.DueOn)
            .ThenBy(task => task.Id, StringComparer.Ordinal),
        TaskSort.Title => tasks
            .OrderBy(task => task.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => task.Id, StringComparer.Ordinal),
        _ => tasks.OrderBy(task => task.Id, StringComparer.Ordinal),
    };

    private static TEnum? ParseEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw AppException.BadRequest(
            $"{field} must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.",
            new { field, value });
    }

    private static string? ParseListId(string? listId) =>
        string.IsNullOrWhiteSpace(listId) ? null : listId.Trim();

    private static string? ParseQuery(string? q)
    {
        if (q is null)
        {
            return null;
        }

        var trimmed = q.Trim();

        if (trimmed.Length is 0 or > 120)
        {
            throw AppException.BadRequest("q must be between 1 and 120 characters.", new { length = trimmed.Length });
        }

        return trimmed;
    }

    private static DateOnly? ParseDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        throw AppException.BadRequest($"{field} must be an ISO-8601 date (yyyy-MM-dd).", new { field, value });
    }
}
