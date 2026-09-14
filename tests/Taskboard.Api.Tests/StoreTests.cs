// Locks in the invariants every feature is allowed to rely on.
//
// SHARED FILE - read-only for feature agents. See AGENTS.md.

using Microsoft.Extensions.DependencyInjection;

using Taskboard.Api.Domain;
using Taskboard.Api.Store;

namespace Taskboard.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class StoreTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private ITaskStore Tasks
    {
        get
        {
            _factory.ResetBoard();
            return _factory.Services.GetRequiredService<ITaskStore>();
        }
    }

    private ITaskListStore Lists
    {
        get
        {
            _factory.ResetBoard();
            return _factory.Services.GetRequiredService<ITaskListStore>();
        }
    }

    [Fact]
    public void BoardIsSeededAndResolvable()
    {
        Assert.Equal(20, Tasks.Tasks.Count);
        Assert.Equal(4, Lists.Lists.Count);
    }

    [Fact]
    public void IdsAreUniqueLowerCaseSlugs()
    {
        var ids = Tasks.Tasks.Select(task => task.Id).Concat(Lists.Lists.Select(list => list.Id)).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", id));
    }

    [Fact]
    public void EverythingIsOrderedById()
    {
        var taskIds = Tasks.Tasks.Select(task => task.Id).ToList();
        var listIds = Lists.Lists.Select(list => list.Id).ToList();

        Assert.Equal(taskIds.OrderBy(id => id, StringComparer.Ordinal).ToList(), taskIds);
        Assert.Equal(listIds.OrderBy(id => id, StringComparer.Ordinal).ToList(), listIds);
    }

    [Fact]
    public void TheInboxExistsAndEveryTaskBelongsToARealList()
    {
        var lists = Lists;

        Assert.NotNull(lists.Find(TaskList.InboxId));
        Assert.All(Tasks.Tasks, task => Assert.NotNull(lists.Find(task.ListId)));
    }

    [Fact]
    public void EveryListHasAtLeastOneTask()
    {
        // Features that group by list need every group to be non-empty.
        var tasks = Tasks.Tasks;

        Assert.All(Lists.Lists, list =>
            Assert.Contains(tasks, task => string.Equals(task.ListId, list.Id, StringComparison.Ordinal)));
    }

    [Fact]
    public void StatusesAreSpreadAcrossTheSeed()
    {
        var byStatus = Tasks.Tasks
            .GroupBy(task => task.Status)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(13, byStatus[TaskState.Todo]);
        Assert.Equal(2, byStatus[TaskState.InProgress]);
        Assert.Equal(1, byStatus[TaskState.Blocked]);
        Assert.Equal(3, byStatus[TaskState.Done]);
        Assert.Equal(1, byStatus[TaskState.Cancelled]);
        Assert.Equal(20, byStatus.Values.Sum());
    }

    [Fact]
    public void PrioritiesAreSpreadAcrossTheSeed()
    {
        var byPriority = Tasks.Tasks
            .GroupBy(task => task.Priority)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(7, byPriority[TaskPriority.Low]);
        Assert.Equal(6, byPriority[TaskPriority.Normal]);
        Assert.Equal(5, byPriority[TaskPriority.High]);
        Assert.Equal(2, byPriority[TaskPriority.Urgent]);
    }

    [Fact]
    public void CompletedAtIsSetExactlyOnDoneTasks()
    {
        Assert.All(Tasks.Tasks, task =>
        {
            if (task.Status is TaskState.Done)
            {
                Assert.NotNull(task.CompletedAt);
            }
            else
            {
                Assert.Null(task.CompletedAt);
            }
        });
    }

    [Fact]
    public void DueDatesStraddleToday()
    {
        // Search, due-date and reporting features all need each of these buckets
        // to be non-empty, and none of them may be empty by accident.
        var open = Tasks.Tasks
            .Where(task => task.Status is not (TaskState.Done or TaskState.Cancelled))
            .ToList();

        Assert.Equal(16, open.Count);
        Assert.Equal(3, open.Count(task => task.DueOn is null));
        Assert.Equal(3, open.Count(task => task.DueOn < SeedData.Today));
        Assert.Equal(3, open.Count(task => task.DueOn == SeedData.Today));
        Assert.Equal(3, open.Count(task => task.DueOn > SeedData.Today && task.DueOn <= SeedData.Today.AddDays(7)));
        Assert.Equal(4, open.Count(task => task.DueOn > SeedData.Today.AddDays(7)));
    }

    [Fact]
    public void FindReturnsTheMatchingRowOrNull()
    {
        var first = Tasks.Tasks[0];

        Assert.Equal(first, Tasks.Find(first.Id));
        Assert.Null(Tasks.Find("no-such-task"));
        Assert.Null(Lists.Find("no-such-list"));
    }

    [Fact]
    public void TheTaskStoreRoundTrips()
    {
        var store = new InMemoryTaskStore();
        var now = DateTimeOffset.UtcNow;
        var added = new TaskItem(
            "round-trip", "Round trip", null, TaskList.InboxId,
            TaskState.Todo, TaskPriority.Normal, null, now, now, null);

        Assert.True(store.TryAdd(added));
        Assert.False(store.TryAdd(added));
        Assert.Equal(added, store.Find("round-trip"));

        var renamed = added with { Title = "Round tripped" };

        Assert.True(store.TryReplace(renamed));
        Assert.Equal("Round tripped", store.Find("round-trip")?.Title);

        Assert.True(store.Remove("round-trip"));
        Assert.False(store.Remove("round-trip"));
        Assert.Null(store.Find("round-trip"));
        Assert.Equal(20, store.Tasks.Count);
    }

    [Fact]
    public void ResettingRestoresTheSeed()
    {
        var store = new InMemoryTaskStore();

        Assert.True(store.Remove("buy-milk"));
        Assert.Equal(19, store.Tasks.Count);

        store.ResetToSeed();

        Assert.Equal(20, store.Tasks.Count);
        Assert.NotNull(store.Find("buy-milk"));
    }
}
