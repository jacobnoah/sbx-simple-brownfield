---
title: "Calendar: tasks laid out day by day over a date range"
labels: [feature, area:calendar]
---

## Context

A calendar client wants to draw a week or a month and put each task on the day it
is due. `GET /tasks?dueBefore=` returns a flat, paged list that the client then has
to bucket itself, and it cannot show empty days. This issue adds a day-by-day
calendar projection.

The feature is pure projection. It injects `ITaskStore`, `ITaskListStore` and
`TimeProvider`, holds no state, and knows about no other feature.

Read `AGENTS.md` before starting, especially "The seed". The rules there override
anything you would otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Calendar/`
- `tests/Taskboard.Api.Tests/Features/Calendar/`

You own exactly one route: `GET /calendar`. Do not map anything else, and do not
map a catch-all.

Get "today" from the injected `TimeProvider`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Calendar.CalendarFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Calendar/CalendarFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Calendar;

internal static class CalendarFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `GET /calendar?from=<date>&to=<date>&listId=<string>&includeClosed=<bool>`

Responds `200`:

```json
{
  "from": "2026-09-11",
  "to": "2026-09-17",
  "taskCount": 6,
  "days": [
    { "date": "2026-09-11", "isToday": true, "tasks": [ { "id": "reply-to-landlord", "...": "..." } ] },
    { "date": "2026-09-12", "isToday": false, "tasks": [] }
  ]
}
```

- `from` defaults to today; `to` defaults to `from + 6`. Both are inclusive ISO
  dates. A malformed date, `from` after `to`, or a range of more than 62 days is a
  `400`.
- `days` has **one entry for every date in the range**, in order, including days
  with no tasks.
- A task appears on the day equal to its `dueOn`. Undated tasks never appear.
- By default only open tasks appear. `includeClosed=true` also includes `done` and
  `cancelled` tasks; any value other than `true` or `false` is a `400`.
- `listId` is optional and must be a known list (`404` otherwise).
- Within a day, tasks are ordered by `priority` descending, then `id` ordinal.
- `taskCount` is the number of tasks across all days.
- `isToday` is `true` for exactly the entry equal to today, if it is in range.

## Acceptance criteria

Assert dates as offsets from `SeedData.Today`.

- `GET /api/calendar` returns `from` = today, `to` = today+6, seven `days`,
  `taskCount` = 6, and `isToday` = `true` only on the first entry.
- Today's entry lists `reply-to-landlord`, `review-pull-requests`, `buy-milk` in
  that order (high, high, low).
- today+1 and today+4 are present with empty `tasks`; today+2 has
  `renew-car-insurance`, today+3 `book-dentist`, and today+5 `draft-q3-report`.
- `?from=today-1&to=today-1` returns only `pay-council-tax`; with
  `&includeClosed=true` it returns `pay-council-tax` then `water-the-plants`.
- `?from=today-10&to=today&listId=home` returns `taskCount` = 2 (`fix-leaking-tap`
  and `pay-council-tax`) and `isToday` on the last entry only.
- `?from=today&to=today+62` (63 days) returns `400`; `?from=today+1&to=today`
  returns `400`; `?from=yesterday` returns `400`; `?includeClosed=maybe` returns
  `400`; `?listId=no-such-list` returns `404`.
- Setting a task's `dueOn` to today+4 through `PATCH /api/tasks/{id}` makes it
  appear on that day.
- Tests live in `tests/Taskboard.Api.Tests/Features/Calendar/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, and cover each bullet above.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Calendar/`,
  `tests/Taskboard.Api.Tests/Features/Calendar/`, and the one registration line in
  `Program.cs`.
