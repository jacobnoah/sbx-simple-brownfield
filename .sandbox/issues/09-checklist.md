---
title: "Checklist: sub-steps inside a task, with progress"
labels: [feature, area:checklist]
---

## Context

"Renew the passport" is really four things: get photos, fill in the form, find the
old passport, post it. Today those steps either go in `notes` as free text or
become four separate tasks that lose their connection to each other. This issue
adds a checklist: an ordered list of tickable items inside one task.

Checklist items are private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/Checklist/`, keyed by `TaskItem.Id`. The feature reads
the board to check that a task exists, but **never writes to it** - ticking every
item does not complete the task, and `TaskItem` does not gain a field.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Checklist/`
- `tests/Taskboard.Api.Tests/Features/Checklist/`

You own exactly these routes and no others:

```
GET    /tasks/{taskId}/checklist
POST   /tasks/{taskId}/checklist
PATCH  /tasks/{taskId}/checklist/{itemId}
DELETE /tasks/{taskId}/checklist/{itemId}
```

Every route carries the literal `checklist` segment - keep it that way, and do not
map a catch-all.

`ApiFactory.ResetBoard()` does **not** reset your store, and you may not change
that. Write tests that do not depend on execution order: give each test its own
task, created through `POST /api/tasks`, rather than assuming a seeded task's
checklist starts empty.

Get timestamps from the injected `TimeProvider`, never from `DateTime.UtcNow`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Checklist.ChecklistFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Checklist/ChecklistFeature.cs` exposes exactly one
public member:

```csharp
namespace Taskboard.Api.Features.Checklist;

internal static class ChecklistFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `GET /tasks/{taskId}/checklist`

Responds `200`. No paging - a checklist is capped at 50 items.

```json
{
  "taskId": "renew-passport",
  "items": [
    { "id": "0199...", "text": "Get photos", "checked": true, "checkedAt": "2026-09-11T09:00:00+00:00" },
    { "id": "0199...", "text": "Fill in the form", "checked": false, "checkedAt": null }
  ],
  "checkedCount": 1,
  "totalCount": 2,
  "percentComplete": 50
}
```

- Items are in insertion order.
- `percentComplete` is `checkedCount * 100 / totalCount`, an integer rounded
  **down**, and `0` when there are no items.

### `POST /tasks/{taskId}/checklist`

Body `{ "text": "Get photos" }`. `text` is required, trimmed, 1-200 characters.
Responds `201` with the created item and a
`Location: /api/tasks/{taskId}/checklist/{itemId}` header. `id` is server-generated
and unique across every checklist. A 51st item is a `409` with `code` =
`CONFLICT`.

### `PATCH /tasks/{taskId}/checklist/{itemId}`

Body carries `text`, `checked`, or both; at least one is required, and any other
field is a `400`. Responds `200` with the updated item. Setting `checked` to
`true` sets `checkedAt` to now; setting it to `false` clears it; setting it to its
current value leaves `checkedAt` untouched.

### `DELETE /tasks/{taskId}/checklist/{itemId}`

Responds `204`.

### Errors

An unknown `taskId` is `404` on every route. An unknown `itemId`, or an item that
belongs to a different task, is `404`.

## Acceptance criteria

- Three items posted in order come back from `GET` in that order, with
  `totalCount` = 3, `checkedCount` = 0 and `percentComplete` = 0.
- Checking one of three gives `percentComplete` = 33 (rounded down); checking all
  three gives 100.
- Checking an item sets a non-null `checkedAt`; patching `checked: true` again
  leaves that `checkedAt` unchanged; unchecking clears it.
- Checking every item does **not** change the task's `status` through
  `GET /api/tasks/{id}`.
- `PATCH` with `{}`, with an unknown field, or with `text` blank returns `400`.
- A missing, blank or 201-character `text` on `POST` returns `400`.
- The 51st item returns `409`.
- Patching or deleting an item under a different task id returns `404` and leaves
  the item unchanged.
- `DELETE` returns `204`; a second `DELETE` returns `404`; the item is gone from
  `GET`.
- Every route under `/api/tasks/no-such-task/checklist` returns `404` with
  `code` = `NOT_FOUND`.
- Tests live in `tests/Taskboard.Api.Tests/Features/Checklist/`, carry
  `[Collection(ApiCollection.Name)]`, cover each bullet above, and pass regardless
  of the order tests run in.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Checklist/`,
  `tests/Taskboard.Api.Tests/Features/Checklist/`, and the one registration line
  in `Program.cs`.
