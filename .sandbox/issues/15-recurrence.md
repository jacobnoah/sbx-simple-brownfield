---
title: "Recurrence: repeat a task on a schedule once it is done"
labels: [feature, area:recurrence]
---

## Context

"Submit the timesheet" is due every week. Today, once it is completed, someone has
to create next week's copy by hand. This issue attaches a recurrence rule to a
task and adds an endpoint that rolls a completed task forward into its next
occurrence.

Rules are private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/Recurrence/`, keyed by `TaskItem.Id`. Rolling forward
**creates a new task on the board** through `ITaskStore.TryAdd`; the completed
task itself is left exactly as it was.

Read `AGENTS.md` before starting, especially "The domain" and "The seed". The
rules there override anything you would otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Recurrence/`
- `tests/Taskboard.Api.Tests/Features/Recurrence/`

You own exactly these routes and no others:

```
GET    /tasks/{taskId}/recurrence
PUT    /tasks/{taskId}/recurrence
DELETE /tasks/{taskId}/recurrence
POST   /tasks/{taskId}/recurrence/next
```

Every route carries the literal `recurrence` segment - keep it that way, and do not
map a catch-all. `POST /tasks` belongs to `area:tasks`; do not call into
`Features/Tasks/`. Build the new `TaskItem` yourself, derive its id with
`Domain/Slug.From`, and find a free id the way the tasks feature documents it:
the slug itself, else `-2`, `-3`, ... up to `-999`.

`ApiFactory.ResetBoard()` does **not** reset your store, and you may not change
that. A rule set on a seeded task survives into later tests, so a test that
depends on a task having **no** rule creates its own task.

Get "today" and timestamps from the injected `TimeProvider`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Recurrence.RecurrenceFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Recurrence/RecurrenceFeature.cs` exposes exactly one
public member:

```csharp
namespace Taskboard.Api.Features.Recurrence;

internal static class RecurrenceFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### A rule

```json
{ "taskId": "submit-timesheet", "every": "week", "interval": 1 }
```

- `every` is `day`, `week` or `month`, case-insensitive on the way in and
  lower-case on the way out.
- `interval` is optional, defaults to `1`, and is an integer 1-365.

### Behaviour

- `PUT /tasks/{taskId}/recurrence` creates or replaces the rule and responds `200`
  with it.
- `GET /tasks/{taskId}/recurrence` responds `200`, or `404` when the task has no
  rule.
- `DELETE /tasks/{taskId}/recurrence` responds `204`, or `404` when there is no
  rule.
- `POST /tasks/{taskId}/recurrence/next` creates the next occurrence and responds
  `201` with the new `TaskItem` and a `Location: /api/tasks/{newId}` header:
  - The new task copies `title`, `notes`, `listId` and `priority`. Its `status` is
    `todo`, `completedAt` is `null`, and `createdAt` and `updatedAt` are now. If
    the original task's list no longer exists, it lands on `inbox`.
  - Its `dueOn` is the original's `dueOn` advanced by `interval` units of `every`
    (`AddDays`, `AddDays(7 * n)`, or `AddMonths`). If the original has no
    `dueOn`, it is **today** advanced the same way.
  - The rule is copied to the new task, so the new task can roll forward in turn.
  - `409` when the task is not `done`. `404` when the task has no rule.
  - `409` when this task has **already** been rolled forward and the task it
    produced is still on the board, with `details.nextTaskId` naming that task.
    Rolling is once per task; if the produced task has since been deleted, the
    task may be rolled again.

An unknown `taskId` is `404` on every route.

## Acceptance criteria

Assert dates as offsets from `SeedData.Today`.

- `PUT /api/tasks/submit-timesheet/recurrence` with `{"every":"Week"}` returns
  `200`, `every` = `"week"` and `interval` = 1.
- `POST .../submit-timesheet/recurrence/next` returns `201`, a new task with id
  `submit-the-timesheet`, `status` = `"todo"`, `listId` = `"work"`,
  `priority` = `"normal"`, `dueOn` = today+3 (today-4 plus 7) and a `Location`
  header. `GET /api/tasks/submit-the-timesheet/recurrence` returns the copied
  rule.
- Rolling `submit-timesheet` again returns `409` with `details.nextTaskId` =
  `"submit-the-timesheet"`.
- `submit-timesheet` itself is unchanged: still `done`, same `completedAt` and
  `updatedAt`.
- `water-the-plants` with `{"every":"day","interval":2}` rolls into
  `water-the-plants-2` (the slug of "Water the plants" is taken by the original)
  due today+1.
- `mail-birthday-card` with `{"every":"month"}` rolls into a task due on
  `(today-3).AddMonths(1)`.
- A done task with no `dueOn` (create one and complete it) rolls into a task due
  today plus the interval.
- After `DELETE /api/tasks/submit-the-timesheet`, rolling `submit-timesheet` again
  returns `201`.
- Rolling an open task (`buy-milk`, with a rule) returns `409`; rolling a done task
  with no rule (one the test creates and completes) returns `404`.
- `every` of `year`, and `interval` of `0` or `366`, return `400`.
- Every route under `/api/tasks/no-such-task/recurrence` returns `404`.
- Tests live in `tests/Taskboard.Api.Tests/Features/Recurrence/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, cover each bullet above, and pass regardless of
  the order tests run in.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Recurrence/`,
  `tests/Taskboard.Api.Tests/Features/Recurrence/`, and the one registration line
  in `Program.cs`.
