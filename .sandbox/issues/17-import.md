---
title: "Import: create many tasks at once, all-or-nothing, with a dry run"
labels: [feature, area:import]
---

## Context

Moving a to-do list from somewhere else onto the board means one `POST /tasks` per
row, and a bad row halfway through leaves half the list imported. This issue adds
a batch import that validates every row before writing any, reports every problem
at once, and can be run as a dry run.

This is deliberately the opposite trade-off to a per-item bulk operation: an import
is **atomic**. Either every row is created or none is.

The feature holds no state of its own. It reads and writes the board through
`ITaskStore` and `ITaskListStore`, and knows about no other feature.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Import/`
- `tests/Taskboard.Api.Tests/Features/Import/`

You own exactly one route: `POST /import/tasks`. Do not map anything else, and do
not map a catch-all. `POST /tasks` belongs to `area:tasks`; do not call into
`Features/Tasks/`. Build each `TaskItem` yourself, derive ids with
`Domain/Slug.From`, and pick free ids the way the tasks feature documents it: the
slug itself, else `-2`, `-3`, ... up to `-999`.

`ITaskStore` has no transaction. Make the import atomic by validating every row
and allocating every id **before** the first `TryAdd`; if a `TryAdd` then fails
because something else took the id in the meantime, remove the tasks this request
already added and return `409`.

Get timestamps from the injected `TimeProvider`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Import.ImportFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Import/ImportFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Import;

internal static class ImportFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `POST /import/tasks?dryRun=<bool>`

```json
{
  "tasks": [
    { "title": "Buy milk", "listId": "errands", "priority": "low", "dueOn": "2026-09-14" },
    { "title": "Call the plumber", "notes": "Ask about Thursday.", "status": "blocked" }
  ]
}
```

Each row:

| Field      | Rule                                                                 |
| ---------- | -------------------------------------------------------------------- |
| `title`    | Required, trimmed, 1-120 characters, at least one ASCII letter or digit |
| `notes`    | Optional, trimmed, up to 2000 characters; blank becomes `null`        |
| `listId`   | Optional, defaults to `inbox`, must name an existing list            |
| `priority` | Optional camelCase `TaskPriority`, defaults to `normal`               |
| `status`   | Optional, one of `todo`, `inProgress`, `blocked`; defaults to `todo`  |
| `dueOn`    | Optional ISO date                                                    |
| anything else | Rejected                                                          |

`tasks` must hold 1-100 rows. A missing body, a missing or empty `tasks`, or more
than 100 rows is a `400` with `code` = `BAD_REQUEST`.

If **any** row is invalid, the response is `422` with `code` =
`UNPROCESSABLE_ENTITY` and every problem in `details`, and nothing is written:

```json
{ "details": { "errors": [ { "row": 1, "field": "listId", "message": "No list 'garage'." } ] } }
```

`row` is zero-based. Every invalid field on every row is reported, not just the
first.

Otherwise the response is `201` (or `200` when `dryRun=true`):

```json
{ "dryRun": false, "created": [ { "row": 0, "id": "buy-milk-2" }, { "row": 1, "id": "call-the-plumber" } ] }
```

- Ids are allocated in row order, and must be unique both against the board and
  **within the batch**: two rows titled "Buy milk" become `buy-milk-2` and
  `buy-milk-3`.
- Every created task has `createdAt` = `updatedAt` = now and `completedAt` = `null`.
- A dry run performs every validation and allocates the same ids a real run would
  at that moment, but writes nothing.

## Acceptance criteria

- Importing two valid rows returns `201`, and both tasks then exist through
  `GET /api/tasks/{id}` with the given fields and defaults applied.
- A row titled "Buy milk" gets `buy-milk-2`; two such rows get `buy-milk-2` and
  `buy-milk-3`.
- `?dryRun=true` returns `200`, `dryRun` = `true`, the same ids a real run then
  returns, and `GET /api/tasks` still reports `total` = 20 afterwards.
- A batch of three rows where row 1 has an unknown `listId` and row 2 has a blank
  `title` and `status` = `done` returns `422` with exactly three errors, and
  `GET /api/tasks` still reports `total` = 20 - row 0 was **not** written.
- An unknown field on a row, a bad `priority`, a bad `dueOn`, and a 121-character
  title are each reported as a row error.
- A missing body, `{"tasks":[]}` and 101 rows each return `400`.
- `status` = `inProgress` on a row is honoured.
- Tests live in `tests/Taskboard.Api.Tests/Features/Import/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, and cover each bullet above.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Import/`,
  `tests/Taskboard.Api.Tests/Features/Import/`, and the one registration line in
  `Program.cs`.
