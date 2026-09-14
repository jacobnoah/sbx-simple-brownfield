---
title: "Bulk: apply one operation to many tasks in a single request"
labels: [feature, area:bulk]
---

## Context

Clearing a backlog through the existing API means one HTTP request per task.
Completing eleven tasks is eleven round trips, and there is no way to say "move
everything on this list and tell me what happened". This issue adds one endpoint
that applies a single operation across many task ids and reports the outcome of
each.

The feature holds no state of its own. It reads and writes the board through
`ITaskStore` and `ITaskListStore`, and knows about no other feature.

Read `AGENTS.md` before starting, especially "The domain". The rules there
override anything you would otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Bulk/`
- `tests/Taskboard.Api.Tests/Features/Bulk/`

You own exactly one route: `POST /bulk/tasks`. Do not map anything else, and do
not map a catch-all. `/tasks` and `/tasks/{taskId}` belong to `area:tasks` and
are already implemented - see the route ownership table in `AGENTS.md`.

`Features/Tasks/` already implements complete and reopen for a single task. You
may not call into it, reference its namespace, or move its code somewhere shared.
Reimplement the same semantics against `ITaskStore` inside your own folder, and
match the documented behaviour below exactly - a bulk complete must leave a task
in the same state a `POST /tasks/{id}/complete` would.

Get timestamps from the injected `TimeProvider`, never from `DateTime.UtcNow`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Bulk.BulkFeature.Register(app);
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

`src/Taskboard.Api/Features/Bulk/BulkFeature.cs` exposes exactly one public
member, with exactly this signature:

```csharp
namespace Taskboard.Api.Features.Bulk;

internal static class BulkFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

Route registered (relative to the group; the caller sees `/api/bulk/tasks`):

```
POST /bulk/tasks
```

Request body:

```json
{
  "operation": "complete",
  "ids": ["file-expenses", "buy-milk", "no-such-task"],
  "listId": "work",
  "priority": "high"
}
```

- `operation` is required and is one of `complete`, `reopen`, `delete`, `move` or
  `setPriority`, compared case-insensitively.
- `ids` is required, 1-200 entries, and may not contain a null, empty or
  whitespace-only entry. Duplicates are collapsed, keeping the first occurrence.
- `listId` is required for `move` and rejected for every other operation. It must
  name an existing list.
- `priority` is required for `setPriority` and rejected for every other
  operation.

### Operations

| Operation     | Effect on one task                                                              |
| ------------- | ------------------------------------------------------------------------------- |
| `complete`    | Status `done`, `completedAt` set to now. Already `done` is a `skipped` outcome.  |
| `reopen`      | Status `todo`, `completedAt` cleared. Only from `done` or `cancelled`.           |
| `delete`      | Removes the task.                                                               |
| `move`        | Sets `listId`. Already on that list is a `skipped` outcome.                     |
| `setPriority` | Sets `priority`. Already at that priority is a `skipped` outcome.                |

Every operation that changes a task also sets `updatedAt` to now.

### Response

`200` with a type local to your feature, carrying one result per **requested** id
in the order given (after duplicates are collapsed):

```json
{
  "operation": "complete",
  "applied": 1,
  "skipped": 1,
  "failed": 1,
  "results": [
    { "id": "file-expenses", "outcome": "applied" },
    { "id": "submit-timesheet", "outcome": "skipped", "reason": "Task is already done." },
    { "id": "no-such-task", "outcome": "notFound", "reason": "No task 'no-such-task'." }
  ]
}
```

- `outcome` is `applied`, `skipped` or `notFound`.
- `reason` is `null` for `applied` and a short sentence otherwise.
- `applied`, `skipped` and `failed` are counts; `failed` counts `notFound`.
- The response is `200` even when every id failed. An unknown task is a per-item
  outcome, **not** a `404` for the whole request.

This is deliberately **not** atomic: ids are processed in order and each one
stands or falls alone. A validation failure in the request itself - a bad
`operation`, an empty `ids`, a `listId` that does not exist - is different: it is
a `400` and nothing is written at all.

## Acceptance criteria

- `POST /api/bulk/tasks` with `{"operation":"complete","ids":["file-expenses","fix-leaking-tap"]}`
  returns `200`, `applied` = 2, and both tasks then report `status` = `"done"`
  with a non-null `completedAt` through `GET /api/tasks/{id}`.
- A bulk `complete` leaves a task in the same state as
  `POST /api/tasks/{id}/complete` does: a test completes one task each way and
  asserts both end with `status` = `"done"`, a non-null `completedAt`, and an
  `updatedAt` equal to that `completedAt`.
- Completing an already-done task yields `outcome` = `"skipped"` with a non-null
  `reason`, and does not change its `completedAt`.
- `reopen` on `submit-timesheet` and `cancel-gym-membership` both apply;
  `reopen` on an open task is `skipped`.
- `delete` removes the tasks, and `GET /api/tasks` then reports a `total` lower
  by exactly the number applied. A second identical `delete` returns `200` with
  every outcome `notFound`.
- `move` with `{"listId":"work"}` sets `listId` on each task; a task already on
  `work` is `skipped`.
- `setPriority` with `{"priority":"urgent"}` sets the priority; a task already
  `urgent` is `skipped`.
- An unknown id anywhere in `ids` produces `outcome` = `"notFound"` for that
  entry, `200` overall, and does not stop the ids after it from being applied. A
  test asserts the ids after the unknown one were applied.
- Results come back in the order requested, and duplicate ids are collapsed:
  `["buy-milk","buy-milk"]` yields exactly one result.
- These each return `400` with `application/problem+json` and `code` =
  `BAD_REQUEST`, thrown as `AppException.BadRequest(...)`, and write nothing:
  a missing body; a missing or unrecognised `operation`; a missing, null or empty
  `ids`; more than 200 ids; an entry that is null, empty or whitespace;
  `move` without `listId`; `move` with an unknown `listId`; `setPriority` without
  `priority`; `setPriority` with an unrecognised `priority`; and `listId` or
  `priority` supplied on an operation that does not take it.
- A test proves the "writes nothing" part: after a `400` for an unknown `listId`,
  every id named in the request is unchanged.
- Tasks are read and written only through `ITaskStore`, lists only through
  `ITaskListStore`, timestamps only through the injected `TimeProvider`, and
  configuration only through `IOptions<AppConfig>`.
- Nothing in `src/Taskboard.Api/Features/Tasks/` is referenced, imported or
  edited.
- Tests live in `tests/Taskboard.Api.Tests/Features/Bulk/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, drive the app through `ApiFactory`, and cover
  each bullet above.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Bulk/`,
  `tests/Taskboard.Api.Tests/Features/Bulk/`, and the one registration line in
  `Program.cs`.
