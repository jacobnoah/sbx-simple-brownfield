---
title: "Time tracking: log time against tasks and summarise it"
labels: [feature, area:time-tracking]
---

## Context

There is no record of how long anything took. This issue lets a caller log time
entries against a task and read back a summary for a date range, by task and by
list.

Time entries are private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/TimeTracking/`, keyed by `TaskItem.Id`. The feature
reads the board, but **never writes to it**.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/TimeTracking/`
- `tests/Taskboard.Api.Tests/Features/TimeTracking/`

You own exactly these routes and no others:

```
GET    /tasks/{taskId}/time-entries
POST   /tasks/{taskId}/time-entries
DELETE /tasks/{taskId}/time-entries/{entryId}
GET    /time-tracking/summary?from=<date>&to=<date>&listId=<string>
```

Every task route carries the literal `time-entries` segment - keep it that way,
and do not map a catch-all.

`ApiFactory.ResetBoard()` does **not** reset your store, and you may not change
that. Write tests that do not depend on execution order: create a fresh list
through `POST /api/lists` and fresh tasks on it through `POST /api/tasks`, and
assert the summary with `?listId=` set to that list.

Get "today" and timestamps from the injected `TimeProvider`, never from
`DateTime.UtcNow`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.TimeTracking.TimeTrackingFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/TimeTracking/TimeTrackingFeature.cs` exposes exactly
one public member:

```csharp
namespace Taskboard.Api.Features.TimeTracking;

internal static class TimeTrackingFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### An entry

```json
{
  "id": "0199...",
  "taskId": "draft-q3-report",
  "minutes": 45,
  "loggedOn": "2026-09-11",
  "note": "First pass at the numbers.",
  "createdAt": "2026-09-11T10:02:00+00:00"
}
```

- `minutes` is required, an integer 1-1440.
- `loggedOn` is optional and defaults to today. It may not be after today.
- `note` is optional, trimmed, up to 200 characters; blank becomes `null`.
- `id` and `createdAt` are server-generated.

### Behaviour

- `POST /tasks/{taskId}/time-entries` responds `201` with the entry and a
  `Location: /api/tasks/{taskId}/time-entries/{id}` header. Time may be logged on
  a task of any status.
- `GET /tasks/{taskId}/time-entries` responds `200` with
  `{ "taskId": "...", "totalMinutes": 90, "entries": [...] }`, entries ordered by
  `loggedOn` then `createdAt` then `id`.
- `DELETE /tasks/{taskId}/time-entries/{entryId}` responds `204`, or `404` when the
  entry is unknown or belongs to a different task.
- `GET /time-tracking/summary` responds `200`:

  ```json
  {
    "from": "2026-09-05",
    "to": "2026-09-11",
    "totalMinutes": 135,
    "byTask": [{ "taskId": "draft-q3-report", "minutes": 90 }],
    "byList": [{ "listId": "work", "minutes": 135 }]
  }
  ```

  - `from` and `to` are inclusive dates. `to` defaults to today and `from` to
    `to` minus 6 days. `from` after `to`, or a range longer than 366 days, is a
    `400`.
  - `listId` is optional; when present it must be a known list (`404` otherwise)
    and restricts the summary to tasks **currently** on that list.
  - `byTask` is ordered by `minutes` descending then `taskId`; `byList` the same
    by `listId`. A task's list is its **current** `listId`.
  - Entries for tasks no longer on the board are excluded from every figure.

An unknown `taskId` is `404` on every task route.

## Acceptance criteria

- Logging 30 and 60 minutes against one task gives `totalMinutes` = 90 on its
  `GET`, and the entries come back in `loggedOn` order.
- An entry with no `loggedOn` reports today (`SeedData.Today` in tests is the same
  day the clock reports).
- `minutes` of `0`, `1441`, missing or non-integer; a `loggedOn` of tomorrow; and a
  201-character `note` each return `400`.
- The summary for a fresh list, with entries on two of its tasks, reports correct
  `totalMinutes`, `byTask` in descending-minutes order, and a single `byList` row.
- An entry logged 8 days ago is excluded from the default range and included with
  `?from=` set to 8 days ago.
- `?from=` after `?to=`, and a 367-day range, return `400`; `?listId=no-such-list`
  returns `404`.
- Moving a task to another list through `PATCH /api/tasks/{id}` moves its minutes
  to that list in `byList`.
- Deleting a task through `DELETE /api/tasks/{id}` removes its minutes from the
  summary.
- Deleting an entry under the wrong task id returns `404`.
- Tests live in `tests/Taskboard.Api.Tests/Features/TimeTracking/`, carry
  `[Collection(ApiCollection.Name)]`, cover each bullet above, and pass regardless
  of the order tests run in.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/TimeTracking/`,
  `tests/Taskboard.Api.Tests/Features/TimeTracking/`, and the one registration
  line in `Program.cs`.
