// The domain model: one task, and the two enumerations that describe it.
//
// SHARED FILE - read-only for feature agents. See AGENTS.md.

namespace Taskboard.Api.Domain;

/// <summary>Where a task has got to.</summary>
/// <remarks>
/// Named <c>TaskState</c> rather than <c>TaskStatus</c> because
/// <see cref="System.Threading.Tasks.TaskStatus"/> is in scope via implicit
/// usings. The JSON property is still <c>status</c>.
/// </remarks>
public enum TaskState
{
    /// <summary>Not started.</summary>
    Todo = 0,

    /// <summary>Started but not finished.</summary>
    InProgress = 1,

    /// <summary>Waiting on something outside the board.</summary>
    Blocked = 2,

    /// <summary>Finished. Carries a <see cref="TaskItem.CompletedAt"/>.</summary>
    Done = 3,

    /// <summary>Abandoned. Never going to be done.</summary>
    Cancelled = 4,
}

/// <summary>How urgent a task is. Ordered, so it sorts.</summary>
public enum TaskPriority
{
    /// <summary>Whenever.</summary>
    Low = 0,

    /// <summary>The default.</summary>
    Normal = 1,

    /// <summary>Soon.</summary>
    High = 2,

    /// <summary>Now.</summary>
    Urgent = 3,
}

/// <summary>A single task on the board.</summary>
/// <param name="Id">Stable slug, lower-case kebab-case. Used in URLs.</param>
/// <param name="Title">One-line summary, 1-120 characters.</param>
/// <param name="Notes">Free text, or <c>null</c>.</param>
/// <param name="ListId">Id of the list this task belongs to. Never null.</param>
/// <param name="Status">Where the task has got to.</param>
/// <param name="Priority">How urgent it is.</param>
/// <param name="DueOn">Date it is due, or <c>null</c> when it has no due date.</param>
/// <param name="CreatedAt">When the task was created, UTC.</param>
/// <param name="UpdatedAt">When the task last changed, UTC.</param>
/// <param name="CompletedAt">When it was completed, UTC, or <c>null</c>.</param>
public sealed record TaskItem(
    string Id,
    string Title,
    string? Notes,
    string ListId,
    TaskState Status,
    TaskPriority Priority,
    DateOnly? DueOn,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);
