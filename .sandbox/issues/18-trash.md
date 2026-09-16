---
title: "Trash: soft-delete tasks, restore them, or purge them for good"
labels: [feature, area:trash]
---

## Context

`DELETE /tasks/{taskId}` is permanent. One wrong click and the task, its notes and
its dates are gone. This issue adds a trash: moving a task there takes it off the
board, and it can be restored intact until it is purged.

The trash is private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/Trash/` that holds the full `TaskItem` records it has
taken off the board. Trashing removes the task through `ITaskStore.Remove`;
restoring puts it back through `ITaskStore.TryAdd`.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Trash/`
- `tests/Taskboard.Api.Tests/Features/Trash/`

You own exactly these routes and no others:

```
GET    /trash?limit=<int>&offset=<int>
POST   /trash
POST   /trash/{taskId}/restore
DELETE /trash/{taskId}
```

Do not map a catch-all. `DELETE /tasks/{taskId}` belongs to `area:tasks` and keeps
its permanent behaviour - do not change or wrap it.

`ApiFactory.ResetBoard()` restores the board but does **not** empty your trash, and
you may not change that. A reset can therefore put a seeded task back on the board
while a copy of it still sits in the trash. Handle that case as specified below,
and write tests that trash tasks they created through `POST /api/tasks`, so that no
test depends on the trash starting empty.

Get timestamps from the injected `TimeProvider`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Trash.TrashFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Trash/TrashFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Trash;

internal static class TrashFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `POST /trash`

Body `{ "taskId": "buy-milk" }`. Removes the task from the board and holds it in
the trash with a `trashedAt` of now. Responds `201` with the trash entry and a
`Location: /api/trash/{taskId}/restore` header:

```json
{ "task": { "id": "buy-milk", "...": "..." }, "trashedAt": "2026-09-11T10:00:00+00:00" }
```

- A task not on the board is `404`.
- If the trash already holds an entry with that id, the new entry **replaces** it.

### `GET /trash`

Responds `200` with `Page<T>` of trash entries, most recently trashed first, then
by id. Paging goes through `PageRequest.From`.

### `POST /trash/{taskId}/restore`

Puts the task back on the board and removes it from the trash. Responds `200` with
the restored `TaskItem`.

- The task keeps every field it had, except `updatedAt`, which is set to now.
- If its list no longer exists, it is restored onto `inbox`.
- If the board already has a task with that id, the response is `409` with
  `code` = `CONFLICT`, and the entry stays in the trash.
- An id not in the trash is `404`.

### `DELETE /trash/{taskId}`

Purges the entry permanently. Responds `204`, or `404` when it is not in the trash.

## Acceptance criteria

- Trashing a created task returns `201`; `GET /api/tasks/{id}` then returns `404`,
  and `GET /api/trash` lists the entry first.
- Restoring it returns `200`; the task is back through `GET /api/tasks/{id}` with
  the same `title`, `notes`, `status`, `priority`, `dueOn`, `createdAt` and
  `completedAt`, a newer `updatedAt`, and it is gone from the trash.
- A task trashed from a list that is then deleted through `DELETE /api/lists/{id}`
  is restored onto `inbox`.
- Trashing a task, creating a new task that takes the same id, then restoring
  returns `409`, and the entry is still in the trash.
- `DELETE /api/trash/{id}` returns `204`; restoring it afterwards returns `404`.
- `POST /api/trash` for `no-such-task`, and restore or purge of an id not in the
  trash, each return `404`. A missing or blank `taskId` returns `400`.
- Trashing the same id twice (trash, restore, trash again) leaves exactly one
  entry for it.
- Entries come back most-recent first, and `limit`/`offset` page them with a
  correct `total` relative to what the test itself trashed.
- Tests live in `tests/Taskboard.Api.Tests/Features/Trash/`, carry
  `[Collection(ApiCollection.Name)]`, cover each bullet above, and pass regardless
  of the order tests run in.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Trash/`,
  `tests/Taskboard.Api.Tests/Features/Trash/`, and the one registration line in
  `Program.cs`.
