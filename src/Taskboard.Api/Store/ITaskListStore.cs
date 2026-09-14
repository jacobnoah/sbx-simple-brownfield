// Read/write access to the lists on the board.
//
// SHARED FILE - read-only for feature agents.
//
// The inbox list (TaskList.InboxId) is seeded and must always exist. The store
// does not enforce that - the Lists feature does.

using Taskboard.Api.Domain;

namespace Taskboard.Api.Store;

/// <summary>The task lists every feature reads and writes.</summary>
public interface ITaskListStore
{
    /// <summary>Every list, in a stable order (by <see cref="TaskList.Id"/>, ordinal).</summary>
    IReadOnlyList<TaskList> Lists { get; }

    /// <summary>Finds one list by its id, or returns <c>null</c>.</summary>
    TaskList? Find(string id);

    /// <summary>Adds a list. Returns <c>false</c> when the id is already taken.</summary>
    bool TryAdd(TaskList list);

    /// <summary>Replaces the list with <paramref name="updated"/>'s id.</summary>
    bool TryReplace(TaskList updated);

    /// <summary>Removes a list. Returns <c>false</c> when no list has that id.</summary>
    bool Remove(string id);
}
