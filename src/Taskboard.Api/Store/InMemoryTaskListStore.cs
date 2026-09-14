// The in-memory list store.
//
// SHARED FILE - read-only for feature agents. Same shape as
// InMemoryTaskStore - see the comment there.

using Taskboard.Api.Domain;

namespace Taskboard.Api.Store;

/// <summary>Process-lifetime <see cref="ITaskListStore"/> backed by a dictionary.</summary>
internal sealed class InMemoryTaskListStore : ITaskListStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TaskList> _lists = new(StringComparer.Ordinal);

    /// <summary>Creates a store already populated with the seeded lists.</summary>
    public InMemoryTaskListStore() => ResetToSeed();

    public IReadOnlyList<TaskList> Lists
    {
        get
        {
            lock (_gate)
            {
                return [.. _lists.Values.OrderBy(list => list.Id, StringComparer.Ordinal)];
            }
        }
    }

    public TaskList? Find(string id)
    {
        if (id is null)
        {
            return null;
        }

        lock (_gate)
        {
            return _lists.GetValueOrDefault(id);
        }
    }

    public bool TryAdd(TaskList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        lock (_gate)
        {
            return _lists.TryAdd(list.Id, list);
        }
    }

    public bool TryReplace(TaskList updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        lock (_gate)
        {
            if (!_lists.ContainsKey(updated.Id))
            {
                return false;
            }

            _lists[updated.Id] = updated;
            return true;
        }
    }

    public bool Remove(string id)
    {
        if (id is null)
        {
            return false;
        }

        lock (_gate)
        {
            return _lists.Remove(id);
        }
    }

    /// <summary>Discards every change and restores the seeded lists. Test-only.</summary>
    public void ResetToSeed()
    {
        lock (_gate)
        {
            _lists.Clear();

            foreach (var list in SeedData.Lists)
            {
                _lists[list.Id] = list;
            }
        }
    }
}
