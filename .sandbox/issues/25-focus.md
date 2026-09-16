---
title: "Focus: plan up to five tasks for the day, with suggestions"
labels: [feature, area:focus]
---

## Context

Sixteen open tasks is too many to look at first thing in the morning. This issue
adds a daily focus plan: up to five tasks chosen for a given day, plus a suggestion
endpoint that proposes five from what is overdue, due and urgent.

Plans are private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/Focus/`, keyed by date. The feature reads the board,
but **never writes to it** - completing a task does not remove it from a plan.

Read `AGENTS.md` before starting, especially "The seed". The rules there override
anything you would otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Focus/`
- `tests/Taskboard.Api.Tests/Features/Focus/`

You own exactly these routes and no others:

```
GET    /focus?date=<date>
PUT    /focus?date=<date>
GET    /focus/suggestions
```

Do not map a catch-all.

`ApiFactory.ResetBoard()` does **not** reset your store, and you may not change
that. `PUT` replaces a whole day's plan, so a test that begins by `PUT`ting the plan
it needs is independent of every other test.

Get "today" from the injected `TimeProvider`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Focus.FocusFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Focus/FocusFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Focus;

internal static class FocusFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `date`

On `GET /focus` and `PUT /focus`, `date` is optional and defaults to today. It must
be an ISO date no more than 30 days before or after today; anything else is a
`400`.

### `GET /focus`

Responds `200`:

```json
{
  "date": "2026-09-11",
  "limit": 5,
  "doneCount": 1,
  "items": [
    { "position": 1, "task": { "id": "fix-leaking-tap", "...": "..." } }
  ]
}
```

- `items` are in the order they were planned, positions 1-based and contiguous.
- `task` is the live `TaskItem`. A planned task no longer on the board is omitted
  and positions close up.
- `doneCount` counts items whose task is now `done`.
- A day with no plan returns `items: []`, not a `404`.

### `PUT /focus`

Body `{ "taskIds": ["fix-leaking-tap", "pay-council-tax"] }` replaces the plan for
that date and responds `200` with the `GET` shape.

- `taskIds` is required and holds 0-5 entries; an empty array clears the plan.
- A duplicate id, or more than 5, is a `400`.
- An id that is not on the board, or whose task is `done` or `cancelled`, is a
  `422` with `code` = `UNPROCESSABLE_ENTITY` and `details.taskIds` listing every
  offending id. Nothing is written.

### `GET /focus/suggestions`

Responds `200` with `{ "date": today, "taskIds": [...] }`, **without saving
anything**. Candidates are open tasks. Take, in this order, until five are chosen:

1. Tasks with `dueOn <= today`, ordered by `dueOn` ascending, then `priority`
   descending, then `id`.
2. Remaining open tasks ordered by `priority` descending, then `dueOn` ascending
   with undated tasks last, then `id`.

## Acceptance criteria

- `GET /api/focus/suggestions` returns exactly `fix-leaking-tap`, `file-expenses`,
  `pay-council-tax`, `reply-to-landlord`, `review-pull-requests`, and
  `GET /api/focus` for a day with no plan is still empty afterwards.
- After completing `fix-leaking-tap`, `file-expenses` and `pay-council-tax` through
  `POST /api/tasks/{id}/complete`, suggestions are `reply-to-landlord`,
  `review-pull-requests`, `buy-milk`, `renew-passport`, `renew-car-insurance`.
- `PUT /api/focus` with three ids returns them in order with positions 1-3, and a
  following `GET` agrees.
- Completing one of them makes `doneCount` = 1 and leaves it in the plan.
- After `DELETE /api/tasks/{id}` on a planned task, it is omitted and positions
  close up.
- Plans for different dates are independent: a plan `PUT` for today+1 does not
  appear for today.
- Six ids, or a duplicate id, return `400`. `mail-birthday-card` (done) together
  with `no-such-task` returns `422` listing both, and the existing plan is
  unchanged.
- `?date=` 31 days ahead, 31 days back, or malformed returns `400`.
- `PUT` with `{"taskIds":[]}` clears the plan.
- Tests live in `tests/Taskboard.Api.Tests/Features/Focus/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, cover each bullet above, and pass regardless of
  the order tests run in.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Focus/`,
  `tests/Taskboard.Api.Tests/Features/Focus/`, and the one registration line in
  `Program.cs`.
