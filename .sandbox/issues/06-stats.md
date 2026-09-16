---
title: "Stats: board-wide and per-list progress figures"
labels: [feature, area:stats]
---

## Context

`GET /lists` folds a task count and an open count into each list, and that is the
only aggregate the API offers. There is no single call that says how the board
is doing: how much is finished, how much is overdue, what has been sitting open
the longest. This issue adds a read-only statistics report.

The feature is pure projection over the board. It injects `ITaskStore`,
`ITaskListStore` and `TimeProvider`, holds no state of its own, and knows about no
other feature. Nothing has to run before it works.

Read `AGENTS.md` before starting, especially "The domain" and "The seed". The
rules there override anything you would otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Stats/`
- `tests/Taskboard.Api.Tests/Features/Stats/`

You own exactly two routes: `GET /stats/board` and `GET /stats/lists/{listId}`.
Do not map anything else, and do not map a catch-all. `/lists/{listId}` belongs
to `area:lists` and is already implemented - see the route ownership table in
`AGENTS.md`.

Get "today" from the injected `TimeProvider`, never from `DateTime.UtcNow`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Stats.StatsFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Stats/StatsFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Stats;

internal static class StatsFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `GET /stats/board`

Responds `200` with a type local to your feature:

```json
{
  "today": "2026-09-11",
  "total": 20,
  "open": 16,
  "byStatus": { "todo": 13, "inProgress": 2, "blocked": 1, "done": 3, "cancelled": 1 },
  "byPriority": { "low": 7, "normal": 6, "high": 5, "urgent": 2 },
  "overdue": 3,
  "dueToday": 3,
  "noDueDate": 3,
  "completionRate": 0.1579,
  "oldestOpenTaskId": "unsubscribe-newsletters"
}
```

- `open` counts tasks whose status is neither `done` nor `cancelled`.
- `byStatus` and `byPriority` always carry **every** enum member as a camelCase
  key, including those with a count of `0`.
- `overdue`, `dueToday` and `noDueDate` count **open** tasks only.
- `completionRate` is `done / (total - cancelled)`, rounded to four decimal
  places with `MidpointRounding.AwayFromZero`, or `null` when the denominator is
  `0`.
- `oldestOpenTaskId` is the open task with the earliest `createdAt`, ties broken by
  `id` ordinal ascending, or `null` when nothing is open.

### `GET /stats/lists/{listId}`

The same shape plus `listId` and `listName`, computed over the tasks on that list
only. An unknown `listId` returns `404` with `code` = `NOT_FOUND`.

## Acceptance criteria

- `GET /api/stats/board` returns exactly the figures in the example above (with
  `today` equal to `SeedData.Today`).
- A board-level key with a zero count is still present: after
  `ApiFactory.ResetBoard()`, `GET /api/stats/lists/inbox` reports
  `byStatus.done` = `0` and `byPriority.urgent` = `0`, not a missing key.
- `GET /api/stats/lists/work` returns `total` = 6, `open` = 5, `overdue` = 1,
  `completionRate` = `0.1667` and `oldestOpenTaskId` = `"archive-old-invoices"`.
- `GET /api/stats/lists/errands` returns `completionRate` = `0.3333` - the
  cancelled task is excluded from the denominator.
- `GET /api/stats/lists/inbox` returns `completionRate` = `0`.
- A list with no tasks (create one through `POST /api/lists`) returns `total` =
  `0`, `completionRate` = `null` and `oldestOpenTaskId` = `null`.
- `GET /api/stats/lists/no-such-list` returns `404` with `code` = `NOT_FOUND`.
- Completing `fix-leaking-tap` through `POST /api/tasks/{id}/complete` moves
  `byStatus.done` to 4 and `overdue` to 2 - proving the report reads live store
  state.
- Tasks are read only through `ITaskStore`, lists only through `ITaskListStore`,
  and time only through the injected `TimeProvider`.
- Tests live in `tests/Taskboard.Api.Tests/Features/Stats/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, and cover each bullet above.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Stats/`,
  `tests/Taskboard.Api.Tests/Features/Stats/`, and the one registration line in
  `Program.cs`.
