---
title: "Snooze: push a task's due date out, one task or every overdue task"
labels: [feature, area:snooze]
---

## Context

Rescheduling through `PATCH /tasks/{taskId}` means working out the new date on the
client, and clearing a backlog of overdue tasks means one request each. This issue
adds snooze: "not today, ask me again in three days", for one task or for
everything that is overdue.

The feature holds no state of its own. It reads and writes the board through
`ITaskStore`, and knows about no other feature.

Read `AGENTS.md` before starting, especially "The domain" and "The seed". The
rules there override anything you would otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Snooze/`
- `tests/Taskboard.Api.Tests/Features/Snooze/`

You own exactly two routes: `POST /tasks/{taskId}/snooze` and
`POST /snooze/overdue`. Do not map anything else, and do not map a catch-all.
`PATCH /tasks/{taskId}` belongs to `area:tasks`; do not call into
`Features/Tasks/` - write the updated record yourself with `with` and
`TryReplace`.

Get "today" and timestamps from the injected `TimeProvider`, never from
`DateTime.UtcNow`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Snooze.SnoozeFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Snooze/SnoozeFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Snooze;

internal static class SnoozeFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `POST /tasks/{taskId}/snooze`

Body carries **exactly one** of:

- `{ "days": 3 }` - an integer 1-365. The new `dueOn` is `base + days`, where
  `base` is the task's current `dueOn` when that is today or later, and **today**
  otherwise (an overdue or undated task is snoozed from today, not from its stale
  date).
- `{ "until": "2026-09-20" }` - an ISO date strictly after today and no more than
  365 days ahead. The new `dueOn` is that date.

Both, neither, or any other field is a `400`.

Responds `200`:

```json
{ "previousDueOn": "2026-09-02", "task": { "id": "fix-leaking-tap", "dueOn": "2026-09-14", "...": "..." } }
```

- `updatedAt` is set to now. Nothing else on the task changes.
- A task that is `done` or `cancelled` cannot be snoozed: `409` with
  `code` = `CONFLICT`, and nothing is written.
- An unknown task is `404`.

### `POST /snooze/overdue`

Body `{ "days": 1, "listId": "home" }`. `days` is required, 1-365. `listId` is
optional and must name a known list (`404` otherwise).

Snoozes every **open** task whose `dueOn` is before today (on `listId`, when
given), by the same rule as above - which for an overdue task always means
`today + days`. Responds `200` with
`{ "snoozed": ["file-expenses", "fix-leaking-tap", "pay-council-tax"], "dueOn": "2026-09-12" }`,
ids in ordinal order. Nothing overdue is `200` with an empty array.

## Acceptance criteria

Assert dates as offsets from `SeedData.Today`.

- `fix-leaking-tap` (due today-9) with `{"days":3}` ends due today+3, and
  `previousDueOn` is today-9.
- `book-dentist` (due today+3) with `{"days":2}` ends due today+5.
- `buy-milk` (due today) with `{"days":1}` ends due today+1.
- `archive-old-invoices` (no due date) with `{"days":2}` ends due today+2, and
  `previousDueOn` is `null`.
- `{"until": today+10}` sets exactly that date; `until` of today, or 366 days
  ahead, is a `400`.
- `{}`, `{"days":0}`, `{"days":366}`, and `{"days":1,"until":...}` each return
  `400`.
- `mail-birthday-card` (done) and `cancel-gym-membership` (cancelled) each return
  `409` and keep their original `dueOn` and `updatedAt`.
- The snoozed task's `updatedAt` changes and its `status`, `priority`, `listId`
  and `createdAt` do not.
- `POST /api/snooze/overdue` with `{"days":1}` returns exactly
  `["file-expenses","fix-leaking-tap","pay-council-tax"]`, each then due today+1,
  and `cancel-gym-membership` is untouched.
- The same call with `"listId":"home"` returns
  `["fix-leaking-tap","pay-council-tax"]`; a second identical call returns `[]`.
- `"listId":"no-such-list"` returns `404`; `POST /api/tasks/no-such-task/snooze`
  returns `404`.
- Tests live in `tests/Taskboard.Api.Tests/Features/Snooze/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, and cover each bullet above.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Snooze/`,
  `tests/Taskboard.Api.Tests/Features/Snooze/`, and the one registration line in
  `Program.cs`.
