---
title: "Assignees: who a task belongs to, and each person's workload"
labels: [feature, area:assignees]
---

## Context

The board has no notion of people. Every task is everybody's, so "what is on
Sam's plate" cannot be answered. This issue lets a task be assigned to one person
and reports each person's workload.

Assignments are private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/Assignees/`, keyed by `TaskItem.Id`. The feature reads
the board, but **never writes to it** - `TaskItem` does not gain a field.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Assignees/`
- `tests/Taskboard.Api.Tests/Features/Assignees/`

You own exactly these routes and no others:

```
GET    /tasks/{taskId}/assignee
PUT    /tasks/{taskId}/assignee
DELETE /tasks/{taskId}/assignee
GET    /assignees
GET    /assignees/{assignee}/tasks?status=<status>&limit=<int>&offset=<int>
```

Every task route carries the literal `assignee` segment - keep it that way, and do
not map a catch-all.

`ApiFactory.ResetBoard()` does **not** reset your store, and you may not change
that. Write tests that do not depend on execution order: use an assignee handle
unique to each test (a short random suffix works) so that counts belong to that
test alone.

Get timestamps from the injected `TimeProvider`, never from `DateTime.UtcNow`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Assignees.AssigneesFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Assignees/AssigneesFeature.cs` exposes exactly one
public member:

```csharp
namespace Taskboard.Api.Features.Assignees;

internal static class AssigneesFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### Handles

An assignee is a handle: trimmed, lower-cased, then must match
`^[a-z0-9][a-z0-9._-]{0,39}$`. `"  Sam.K "` and `"sam.k"` are the same person.
The `{assignee}` route value is normalised the same way.

### Behaviour

- `PUT /tasks/{taskId}/assignee` with `{ "assignee": "sam" }` responds `200` with
  `{ "taskId": "buy-milk", "assignee": "sam", "assignedAt": "..." }`. Reassigning
  to someone else replaces the assignment and resets `assignedAt`; re-sending the
  **same** handle is a no-op that keeps the original `assignedAt`.
- `GET /tasks/{taskId}/assignee` responds `200` with that shape, or `404` with
  `code` = `NOT_FOUND` when the task is unassigned.
- `DELETE /tasks/{taskId}/assignee` responds `204`, or `404` when unassigned.
- `GET /assignees` responds `200` with every handle that has at least one assigned
  task still on the board, ordered by handle:
  `[{ "assignee": "sam", "openTaskCount": 2, "taskCount": 3 }]`. "Open" means
  neither `done` nor `cancelled`.
- `GET /assignees/{assignee}/tasks` responds `200` with `Page<TaskItem>` of that
  person's tasks ordered by `id`, optionally filtered by `status` (a camelCase
  `TaskState` name; anything else is a `400`). An unknown handle returns an empty
  page, not a `404`. Paging goes through `PageRequest.From`.
- Every task route returns `404` when `ITaskStore.Find` does not know the task. A
  task deleted from the board no longer counts anywhere.

## Acceptance criteria

- Assigning `buy-milk`, `fix-leaking-tap` and `submit-timesheet` (done) to one
  handle makes `GET /api/assignees` report `taskCount` = 3 and `openTaskCount` = 2
  for it.
- `GET /api/assignees/{handle}/tasks` returns those three in id order, `total` =
  3; `?status=done` returns only `submit-timesheet`.
- `" Sam.K "` on `PUT` is stored and returned as `"sam.k"`, and
  `GET /api/assignees/SAM.K/tasks` finds its tasks.
- Re-`PUT`ting the same handle keeps `assignedAt`; `PUT`ting a different one
  changes it and moves the task out of the first handle's counts.
- `GET` and `DELETE` on an unassigned task return `404`; `DELETE` on an assigned
  one returns `204`.
- A missing, blank or invalid handle (`"has space"`, `"-dash"`, 41 characters) on
  `PUT` returns `400`; `?status=later` returns `400`.
- After `DELETE /api/tasks/buy-milk`, that task no longer appears in the handle's
  task page or counts.
- Every route under `/api/tasks/no-such-task/assignee` returns `404`.
- Tests live in `tests/Taskboard.Api.Tests/Features/Assignees/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, cover each bullet above, and pass regardless of
  the order tests run in.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Assignees/`,
  `tests/Taskboard.Api.Tests/Features/Assignees/`, and the one registration line
  in `Program.cs`.
