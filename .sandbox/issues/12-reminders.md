---
title: "Reminders: schedule a nudge on a task and poll for what is due"
labels: [feature, area:reminders]
---

## Context

A due date says when a task must be finished, not when someone wants to be told
about it. This issue adds reminders: a point in time attached to a task, and an
endpoint a client polls to find the reminders that have come due and not yet been
acknowledged.

Reminders are private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/Reminders/`, keyed by `TaskItem.Id`. The feature reads
the board, but **never writes to it**. Nothing is pushed anywhere - the feature
only answers polls.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Reminders/`
- `tests/Taskboard.Api.Tests/Features/Reminders/`

You own exactly these routes and no others:

```
GET    /tasks/{taskId}/reminders
POST   /tasks/{taskId}/reminders
DELETE /tasks/{taskId}/reminders/{reminderId}
GET    /reminders/due?until=<datetime>&taskId=<string>
POST   /reminders/{reminderId}/acknowledge
```

Every task route carries the literal `reminders` segment - keep it that way, and
do not map a catch-all.

`ApiFactory.ResetBoard()` does **not** reset your store, and you may not change
that. Write tests that do not depend on execution order: give each test its own
task through `POST /api/tasks`, and query `/reminders/due` with `?taskId=` set to
it.

"Now" comes only from the injected `TimeProvider`. Never call `DateTime.UtcNow` or
`DateTimeOffset.UtcNow`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Reminders.RemindersFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Reminders/RemindersFeature.cs` exposes exactly one
public member:

```csharp
namespace Taskboard.Api.Features.Reminders;

internal static class RemindersFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### A reminder

```json
{
  "id": "0199...",
  "taskId": "renew-passport",
  "remindAt": "2026-09-12T08:00:00+00:00",
  "message": "Photos first.",
  "createdAt": "2026-09-11T10:00:00+00:00",
  "acknowledgedAt": null
}
```

- `remindAt` is required, an ISO-8601 date-time **with an offset**, normalised to
  UTC on the way in. It must be strictly after now and no more than 365 days
  ahead.
- `message` is optional, trimmed, up to 200 characters; blank becomes `null`.
- A task holds at most 10 unacknowledged reminders; the 11th is a `409`.

### Behaviour

- `POST /tasks/{taskId}/reminders` responds `201` with the reminder and a
  `Location: /api/tasks/{taskId}/reminders/{id}` header.
- `GET /tasks/{taskId}/reminders` responds `200` with an array ordered by
  `remindAt` then `id`, acknowledged ones included.
- `DELETE /tasks/{taskId}/reminders/{reminderId}` responds `204`, or `404` when
  unknown or on a different task.
- `GET /reminders/due` responds `200` with an array of
  `{ "reminder": {...}, "task": TaskItem }`, holding every reminder that has
  `remindAt <= until`, is unacknowledged, and whose task is still on the board and
  **open** (neither `done` nor `cancelled`). Ordered by `remindAt` then `id`.
  `until` defaults to now and may be at most 30 days ahead; `taskId` optionally
  narrows it.
- `POST /reminders/{reminderId}/acknowledge` responds `200` with the reminder and
  `acknowledgedAt` set to now. Acknowledging twice is a `409`. An unknown id is a
  `404`.

An unknown `taskId` is `404` on every task route.

## Acceptance criteria

- `POST` with a `remindAt` one hour ahead returns `201`, a `Location` header, and
  `remindAt` in UTC even when sent with a `+02:00` offset.
- A `remindAt` in the past, one without an offset, one 366 days ahead, and a
  201-character `message` each return `400`.
- `GET /api/reminders/due?taskId={id}` returns nothing for a reminder one hour
  ahead, and returns it with `?until=` set two hours ahead.
- After acknowledging it, the same query returns nothing; acknowledging again
  returns `409`.
- A due reminder on a task completed through `POST /api/tasks/{id}/complete` no
  longer appears in `/due`; reopening the task brings it back.
- A due reminder on a task removed through `DELETE /api/tasks/{id}` no longer
  appears.
- `?until=` 31 days ahead returns `400`.
- The 11th unacknowledged reminder on one task returns `409`.
- Every route under `/api/tasks/no-such-task/reminders` returns `404`, and so does
  acknowledging an unknown reminder id.
- Tests live in `tests/Taskboard.Api.Tests/Features/Reminders/`, carry
  `[Collection(ApiCollection.Name)]`, cover each bullet above, and pass regardless
  of the order tests run in.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Reminders/`,
  `tests/Taskboard.Api.Tests/Features/Reminders/`, and the one registration line
  in `Program.cs`.
