---
title: "Kanban: the board as status columns, with a work-in-progress limit"
labels: [feature, area:kanban]
---

## Context

A kanban client wants the board as columns - one per status - and wants dragging a
card into "in progress" to respect a work-in-progress limit. The existing API has
neither the column shape nor any limit. This issue adds both.

The feature holds no state of its own. It reads and writes the board through
`ITaskStore` and `ITaskListStore`, and knows about no other feature.

Read `AGENTS.md` before starting, especially "The seed". The rules there override
anything you would otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Kanban/`
- `tests/Taskboard.Api.Tests/Features/Kanban/`

You own exactly two routes: `GET /kanban` and
`POST /kanban/tasks/{taskId}/move`. Do not map anything else, and do not map a
catch-all.

`POST /tasks/{taskId}/complete` and `PATCH /tasks/{taskId}` belong to
`area:tasks`; do not call into `Features/Tasks/`. Write status changes yourself
with `with` and `TryReplace`, following the semantics below.

The WIP limit is a `private const int` in your folder, set to `3`. Do not add a
configuration setting.

The limit check and the write must be atomic with respect to other moves made
through this feature, so that two concurrent moves cannot together push the column
past the limit.

Get timestamps from the injected `TimeProvider`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Kanban.KanbanFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Kanban/KanbanFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Kanban;

internal static class KanbanFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `GET /kanban?listId=<string>`

Responds `200`:

```json
{
  "listId": null,
  "columns": [
    { "status": "todo", "count": 13, "wipLimit": null, "tasks": [ ... ] },
    { "status": "inProgress", "count": 2, "wipLimit": 3, "tasks": [ ... ] },
    { "status": "blocked", "count": 1, "wipLimit": null, "tasks": [ ... ] },
    { "status": "done", "count": 3, "wipLimit": null, "tasks": [ ... ] },
    { "status": "cancelled", "count": 1, "wipLimit": null, "tasks": [ ... ] }
  ]
}
```

- Exactly five columns, always, in that order, even when empty.
- Within a column: `priority` descending, then `dueOn` ascending with undated
  tasks last, then `id` ordinal.
- `listId` is optional and must name a known list (`404` otherwise). The WIP
  limit applies to the **whole board**, so `inProgress.wipLimit` is `3` either
  way, but `count` reflects the filtered list.

### `POST /kanban/tasks/{taskId}/move`

Body `{ "status": "inProgress" }`, a camelCase `TaskState` name. Responds `200` with
the updated `TaskItem`.

- Moving to the status the task already has is a no-op: `200` with the task
  unchanged, `updatedAt` included.
- Moving **into** `inProgress` when the board already has 3 `inProgress` tasks is
  `409` with `code` = `CONFLICT` and `details.wipLimit` = 3. Nothing is written.
- Moving into `done` sets `completedAt` to now.
- Moving out of `done` clears `completedAt`. Moving into `cancelled` leaves
  `completedAt` `null`.
- Every real move sets `updatedAt` to now.
- An unknown task is `404`; a missing or unknown `status` is `400`.

## Acceptance criteria

- `GET /api/kanban` returns the five columns with counts 13, 2, 1, 3, 1.
- The `todo` column is ordered exactly: `pay-council-tax`, `renew-passport`,
  `fix-leaking-tap`, `reply-to-landlord`, `review-pull-requests`,
  `renew-car-insurance`, `file-expenses`, `book-dentist`, `buy-milk`,
  `order-printer-ink`, `archive-old-invoices`, `read-onboarding-docs`,
  `unsubscribe-newsletters`.
- The `done` column is `submit-timesheet`, `mail-birthday-card`,
  `water-the-plants`.
- `?listId=inbox` returns `todo` = 3 and every other column empty but present;
  `?listId=nope` returns `404`.
- Moving `book-dentist` to `inProgress` returns `200`; moving `buy-milk` to
  `inProgress` next returns `409` with `details.wipLimit` = 3, and `buy-milk` is
  still `todo`.
- Moving `buy-milk` to `done` sets a non-null `completedAt`; moving it back to
  `todo` clears it.
- Moving `buy-milk` to `todo` (where it already is) leaves `updatedAt` unchanged.
- A `status` of `"later"` or a missing body returns `400`;
  `POST /api/kanban/tasks/no-such-task/move` returns `404`.
- Tests live in `tests/Taskboard.Api.Tests/Features/Kanban/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, and cover each bullet above.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Kanban/`,
  `tests/Taskboard.Api.Tests/Features/Kanban/`, and the one registration line in
  `Program.cs`.
