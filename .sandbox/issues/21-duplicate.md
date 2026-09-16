---
title: "Duplicate: copy an existing task as a fresh one"
labels: [feature, area:duplicate]
---

## Context

Plenty of tasks are "the same as that one, again": another dentist appointment,
another expenses claim. Recreating one means copying its title, notes, list and
priority by hand. This issue adds a single call that duplicates a task into a
fresh, open copy.

The feature holds no state of its own. It reads and writes the board through
`ITaskStore` and `ITaskListStore`, and knows about no other feature.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Duplicate/`
- `tests/Taskboard.Api.Tests/Features/Duplicate/`

You own exactly one route: `POST /tasks/{taskId}/duplicate`. It carries the literal
`duplicate` segment - keep it that way, and do not map a catch-all.

`POST /tasks` belongs to `area:tasks`; do not call into `Features/Tasks/`. Build
the new `TaskItem` yourself, derive its id with `Domain/Slug.From`, and pick a free
id the way the tasks feature documents it: the slug itself, else `-2`, `-3`, ... up
to `-999`.

Get timestamps from the injected `TimeProvider`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Duplicate.DuplicateFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Duplicate/DuplicateFeature.cs` exposes exactly one
public member:

```csharp
namespace Taskboard.Api.Features.Duplicate;

internal static class DuplicateFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `POST /tasks/{taskId}/duplicate`

The body is optional. When present it is a JSON object with any of:

| Field          | Default                     | Rule                                              |
| -------------- | --------------------------- | ------------------------------------------------- |
| `title`        | `"<original title> (copy)"` | Trimmed, 1-120 characters, a letter or digit      |
| `listId`       | the original's `listId`     | Must name an existing list                        |
| `keepDueDate`  | `true`                      | Boolean; `false` gives the copy no `dueOn`        |

Any other field is a `400`.

When the default title would exceed 120 characters, the original title is cut
(and trailing whitespace trimmed) so that `"<cut title> (copy)"` is exactly 120
characters or fewer.

If the original's list no longer exists and no `listId` is given, the copy goes on
`inbox`.

The copy:

- carries the original's `notes` and `priority`;
- has `status` = `todo` and `completedAt` = `null` **whatever the original's
  status was**;
- has `createdAt` = `updatedAt` = now;
- has an id from `Slug.From(title)`, made free as above.

The original task is not changed in any way.

Responds `201` with the new `TaskItem` and a `Location: /api/tasks/{newId}` header.
An unknown `taskId` is `404`.

## Acceptance criteria

- `POST /api/tasks/buy-milk/duplicate` with no body returns `201`, title
  `"Buy milk (copy)"`, id `buy-milk-copy`, `listId` = `"errands"`, `priority` =
  `"low"`, `dueOn` equal to the original's, and a `Location` header that resolves.
- Duplicating `buy-milk` a second time gives `buy-milk-copy-2`.
- Duplicating `mail-birthday-card` (done) gives a copy with `status` = `"todo"` and
  `completedAt` = `null`, while the original stays `done` with the same
  `completedAt` and `updatedAt`.
- `{"title":"Book the hygienist"}` gives id `book-the-hygienist` and that title,
  and keeps `book-dentist`'s notes.
- `{"listId":"work","keepDueDate":false}` gives a copy on `work` with `dueOn` =
  `null`.
- A task with a 120-character title duplicates to a title of at most 120 characters
  ending in `" (copy)"`.
- `{"listId":"no-such-list"}`, `{"title":"   "}`, `{"title":"!!!"}`,
  `{"keepDueDate":"yes"}` and an unknown field each return `400`, and nothing is
  created (`GET /api/tasks` still reports `total` = 20).
- `POST /api/tasks/no-such-task/duplicate` returns `404`.
- Tests live in `tests/Taskboard.Api.Tests/Features/Duplicate/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, and cover each bullet above.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Duplicate/`,
  `tests/Taskboard.Api.Tests/Features/Duplicate/`, and the one registration line
  in `Program.cs`.
