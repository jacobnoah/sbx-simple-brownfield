// Read/write access to the tasks on the board.
//
// SHARED FILE - read-only for feature agents.
//
// Unlike a catalogue, this store is mutable: features create, update and delete
// tasks through it. Every implementation is thread-safe and every method is
// atomic with respect to the others.
//
// TaskItem is a record. "Updating" a task means reading it, producing a modified
// copy with a `with` expression, and calling TryReplace. Nothing mutates a
// TaskItem in place.

using Taskboard.Api.Domain;

namespace Taskboard.Api.Store;

/// <summary>The tasks every feature reads and writes.</summary>
public interface ITaskStore
{
    /// <summary>Every task, in a stable order (by <see cref="TaskItem.Id"/>, ordinal).</summary>
    IReadOnlyList<TaskItem> Tasks { get; }

    /// <summary>Finds one task by its id, or returns <c>null</c>.</summary>
    TaskItem? Find(string id);

    /// <summary>Adds a task. Returns <c>false</c> when the id is already taken.</summary>
    bool TryAdd(TaskItem task);

    /// <summary>
    /// Replaces the task with <paramref name="updated"/>'s id. Returns <c>false</c>
    /// when no task has that id.
    /// </summary>
    bool TryReplace(TaskItem updated);

    /// <summary>Removes a task. Returns <c>false</c> when no task has that id.</summary>
    bool Remove(string id);
}
