---
title: "Due: report what is overdue, due today and coming up"
labels: [feature, area:due]
---

## Context

`GET /tasks` can filter by `dueBefore` and sort by `due`, but nothing answers the
question a task list is actually for: what needs attention now. This issue adds a
read-only due-date report.

The feature is pure projection over the board. It injects `ITaskStore` and
`TimeProvider`, holds no state of its own, and knows about no other feature.
Nothing has to run before it works.

Read `AGENTS.md` before starting, especially "The domain" and "The seed". The
rules there override anything you would otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Due/`
- `tests/Taskboard.Api.Tests/Features/Due/`

You own exactly two routes: `GET /due/tasks` and `GET /due/summary`. Do not map
anything else. In particular `/tasks` and `/tasks/{taskId}` belong to
`area:tasks` and are already implemented - see the route ownership table in
`AGENTS.md`.

Read tasks only through `ITaskStore`. Do not copy the seed data into your own
folder, do not read `SeedData` from feature code, and do not mutate anything you
get back from the store.

Get "today" from the injected `TimeProvider`:

```csharp
var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
```

Never call `DateTime.UtcNow`, `DateTime.Today` or `DateTimeOffset.Now`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Due.DueFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or either implemented
feature's folder. If you believe you need such a change, stop and say so in the
PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Due/DueFeature.cs` exposes exactly one public member,
with exactly this signature:

```csharp
namespace Taskboard.Api.Features.Due;

internal static class DueFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### Buckets

Both endpoints classify a task into one bucket, from its `dueOn` against today:

| Bucket     | Condition                                        |
| ---------- | ------------------------------------------------ |
| `overdue`  | `dueOn < today`                                  |
| `today`    | `dueOn == today`                                 |
| `upcoming` | `today < dueOn <= today + window`                |

**Only open tasks are ever reported.** A task whose status is `done` or
`cancelled` is excluded from both endpoints regardless of its due date, and so is
a task with no `dueOn` at all.

### `GET /due/tasks?window=<int>&listId=<string>&limit=<int>&offset=<int>`

Responds `200` with `Page<T>` from `Taskboard.Api.Contracts`, where the item type
is local to your feature and wraps the task with its bucket and how far off it
is:

```json
{
  "items": [
    {
      "bucket": "overdue",
      "daysUntilDue": -9,
      "task": {
        "id": "fix-leaking-tap",
        "title": "Fix the leaking tap",
        "listId": "home",
        "status": "todo",
        "priority": "high",
        "dueOn": "2026-09-02",
        "notes": null,
        "createdAt": "2026-08-17T00:00:00+00:00",
        "updatedAt": "2026-08-17T00:00:00+00:00",
        "completedAt": null
      }
    }
  ],
  "total": 9,
  "limit": 25,
  "offset": 0
}
```

- `task` is the shared `TaskItem` - do not redefine it or reshape its properties.
- `bucket` is `overdue`, `today` or `upcoming`, serialised as a lower-case string.
- `daysUntilDue` is `dueOn - today` in whole days: negative for overdue, `0`
  today, positive for upcoming.
- `window` is optional, defaults to `7`, and must be an integer between `0` and
  `365`. `window=0` reports only `overdue` and `today`.
- `listId` is optional. When present it must be a known list id; an unknown one
  returns `404`.
- `limit` and `offset` go through `PageRequest.From`, so `limit` defaults to
  `AppConfig.DefaultPageSize` and is capped by `AppConfig.MaxPageSize`.
- Ordered by `dueOn` ascending, then `priority` **descending** (urgent first),
  then `id` ordinal ascending. Because undated tasks are excluded, there is no
  null case in this sort.

### `GET /due/summary?window=<int>&listId=<string>`

Responds `200` with a type local to your feature. No paging.

```json
{
  "today": "2026-09-11",
  "window": 7,
  "overdue": 3,
  "dueToday": 3,
  "upcoming": 3,
  "noDueDate": 3,
  "nextDueOn": "2026-09-13",
  "mostOverdueTaskId": "fix-leaking-tap"
}
```

- `today` is the date the counts were computed against.
- `noDueDate` counts open tasks with no `dueOn`. They are counted here and
  nowhere else.
- `nextDueOn` is the earliest `dueOn` strictly after today among open tasks,
  ignoring `window`, or `null` when there is none.
- `mostOverdueTaskId` is the id of the open task with the earliest `dueOn` before
  today, ties broken by `id` ordinal ascending, or `null` when nothing is
  overdue.
- `window` and `listId` behave exactly as on `/due/tasks`.

## Acceptance criteria

Assert against offsets from `SeedData.Today`, never against a literal date. The
counts below are locked in by `StoreTests.cs`.

- `GET /api/due/tasks` with no parameters returns `200` with `total` = 9: three
  overdue, three due today, three within the next seven days.
- The first item is `fix-leaking-tap` with `bucket` = `"overdue"` and
  `daysUntilDue` = `-9`.
- Every returned task has a non-null `dueOn` and a status that is neither `done`
  nor `cancelled`. In particular `water-the-plants` (done, due yesterday) and
  `cancel-gym-membership` (cancelled, overdue) never appear.
- `?window=0` returns `total` = 6 and no item with `bucket` = `"upcoming"`.
- `?window=365` returns `total` = 13 - every open, dated task.
- Ordering is asserted with a case that exercises the priority tiebreak: three
  tasks are due today, and `reply-to-landlord` and `review-pull-requests`
  (`high`) both come before `buy-milk` (`low`).
- `?listId=work` narrows the result set and a count is asserted.
  `?listId=no-such-list` returns `404` with `code` = `NOT_FOUND`.
- `?window=-1`, `?window=366`, a non-integer `window`, a `limit` below 1 or above
  `AppConfig.MaxPageSize`, or a negative `offset` each return `400` with
  `application/problem+json` and `code` = `BAD_REQUEST`, thrown as
  `AppException.BadRequest(...)`.
- Paging works: `?limit=4` returns `total` = 9 and four items, and `offset=4`
  skips exactly the first four.
- `GET /api/due/summary` returns `overdue` = 3, `dueToday` = 3, `upcoming` = 3,
  `noDueDate` = 3, `today` equal to `SeedData.Today`, `mostOverdueTaskId` =
  `"fix-leaking-tap"`, and `nextDueOn` equal to `SeedData.Today.AddDays(2)`.
- After completing an overdue task through `POST /api/tasks/{id}/complete`, the
  summary's `overdue` count drops by one - proving the report reads live store
  state rather than a cached snapshot.
- Time is read only through the injected `TimeProvider`, tasks only through
  `ITaskStore`, and configuration only through `IOptions<AppConfig>`.
- Tests live in `tests/Taskboard.Api.Tests/Features/Due/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, drive the app through `ApiFactory`, and cover
  each bullet above.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Due/`,
  `tests/Taskboard.Api.Tests/Features/Due/`, and the one registration line in
  `Program.cs`.
