// The in-memory task store.
//
// SHARED FILE - read-only for feature agents.
//
// A dictionary behind a lock. Every public member takes the lock, so the store
// is safe under concurrent requests and every operation is atomic with respect
// to the others. Twenty-odd rows make sorting on read cheap enough that keeping
// a second index would be a pessimisation.
//
// ResetToSeed is test infrastructure, not application surface: it is not on
// ITaskStore, so no feature can reach it. See AGENTS.md.

using Taskboard.Api.Domain;

namespace Taskboard.Api.Store;

/// <summary>Process-lifetime <see cref="ITaskStore"/> backed by a dictionary.</summary>
internal sealed class InMemoryTaskStore : ITaskStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TaskItem> _tasks = new(StringComparer.Ordinal);

    /// <summary>Creates a store already populated with the seeded tasks.</summary>
    public InMemoryTaskStore() => ResetToSeed();

    public IReadOnlyList<TaskItem> Tasks
    {
        get
        {
            lock (_gate)
            {
                return [.. _tasks.Values.OrderBy(task => task.Id, StringComparer.Ordinal)];
            }
        }
    }

    public TaskItem? Find(string id)
    {
        if (id is null)
        {
            return null;
        }

        lock (_gate)
        {
            return _tasks.GetValueOrDefault(id);
        }
    }

    public bool TryAdd(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_gate)
        {
            return _tasks.TryAdd(task.Id, task);
        }
    }

    public bool TryReplace(TaskItem updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        lock (_gate)
        {
            if (!_tasks.ContainsKey(updated.Id))
            {
                return false;
            }

            _tasks[updated.Id] = updated;
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
            return _tasks.Remove(id);
        }
    }

    /// <summary>Discards every change and restores the seeded tasks. Test-only.</summary>
    public void ResetToSeed()
    {
        lock (_gate)
        {
            _tasks.Clear();

            foreach (var task in SeedData.Tasks)
            {
                _tasks[task.Id] = task;
            }
        }
    }
}
