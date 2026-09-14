---
title: "Bug: GET /tasks?sort=due puts undated tasks first instead of last"
labels: [bug, area:tasks]
---

## Context

`GET /tasks?sort=due` is documented - in `README.md`, in the endpoint's OpenAPI
description, and on the `TaskSort.Due` member itself - as ordering by due date
ascending with **tasks that have no due date last**. It does the opposite: the
three seeded tasks with a null `dueOn` come back at the top of the page, ahead of
everything that is actually overdue.

`sort=priority` has the same defect in its secondary key. It is documented as
"most urgent first, then soonest due, then id", and within a priority band the
undated tasks come first rather than last.

The practical effect is that the first page of the sort meant to surface what is
most urgent is filled with tasks that have no deadline at all.

## Reproduce

```sh
dotnet run --project src/Taskboard.Api
curl 'http://localhost:5090/api/tasks?sort=due&limit=5'
```

Observed - the first three items are the undated tasks:

```
archive-old-invoices, read-onboarding-docs, unsubscribe-newsletters,
fix-leaking-tap, cancel-gym-membership
```

Expected - dated tasks first, soonest due leading:

```
fix-leaking-tap, cancel-gym-membership, submit-timesheet,
mail-birthday-card, file-expenses
```

The same call with `sort=priority` shows `archive-old-invoices` (low, undated)
ahead of `buy-milk` (low, due today) in the low band.

## Why the existing tests miss it

`TasksTests.ListSortsDatedTasksByDueDateAscending` passes today because it sends
`dueBefore` alongside `sort=due`, and `dueBefore` filters undated tasks out
before the sort ever runs. The assertion is therefore made against a set that
contains no null `dueOn` at all. That test is not wrong, but it is not enough -
extend it or add one beside it that sorts the whole board.

## Scope

You own the Tasks feature:

- `src/Taskboard.Api/Features/Tasks/`
- `tests/Taskboard.Api.Tests/Features/Tasks/`

You may change existing files in those two folders, including existing tests. The
defect is in the sort in `src/Taskboard.Api/Features/Tasks/TaskQuery.cs`.

Nothing outside those two folders may change. In particular:

- **Do not** change `Program.cs` - the feature is already registered, and this
  issue adds no registration line.
- **Do not** change `Store/SeedData.cs`. The seed is the fixture every other
  agent's tests assert against, and it already contains three undated tasks
  precisely so this case is coverable.
- **Do not** change `Domain/**`, `Store/**`, `Routing/**`, `Errors/**`,
  `Contracts/SharedContracts.cs`, `Configuration/AppConfig.cs`,
  `Directory.Packages.props`, `.editorconfig`, `ApiFactory.cs`, `StoreTests.cs`,
  `SmokeTests.cs`, or `Features/Lists/`.

Do not add NuGet packages. Do not change the documented contract to match the
code - the documented behaviour is the correct one; fix the code.

Read `AGENTS.md` before starting.

## Expected behaviour

`sort=due`:

1. Tasks with a `dueOn`, ascending by that date.
2. Then tasks with no `dueOn`.
3. Ties within either group broken by `id`, ordinal ascending.

`sort=priority`:

1. `priority` descending - `urgent`, `high`, `normal`, `low`.
2. Within a band, the `sort=due` order above, undated last.
3. Ties broken by `id`, ordinal ascending.

Nothing else changes: `sort=id`, `sort=created` and `sort=title` keep their
current behaviour, filtering is untouched, `total` still counts matches before
paging, and an unrecognised `sort` is still a `400`.

## Acceptance criteria

- The first commit on the branch adds a test that fails against the current code
  for the reason above. The fix follows it.
- `GET /api/tasks?sort=due` returns all 20 tasks with the 17 dated ones first in
  ascending `dueOn` order, and the three undated ones - `archive-old-invoices`,
  `read-onboarding-docs`, `unsubscribe-newsletters` - last, in that order.
- The first item is `fix-leaking-tap` and the last is
  `unsubscribe-newsletters`.
- Two tasks are due on the same day more than once in the seed, so the `id`
  tiebreak is asserted at least once - `pay-council-tax` before
  `water-the-plants`.
- `GET /api/tasks?sort=priority` returns `pay-council-tax` then `renew-passport`
  first (both `urgent`), and within the `low` band places
  `archive-old-invoices`, `read-onboarding-docs` and `unsubscribe-newsletters`
  after every dated low-priority task.
- Combining the fixed sort with a filter still works: `?sort=due&status=todo`
  returns 13 items with the undated ones last.
- Paging still reports `total` as the count before paging: `?sort=due&limit=3`
  returns `total` = 20 with 3 items.
- `TasksTests.ListSortsDatedTasksByDueDateAscending` still passes, whether you
  leave it as it is or strengthen it. No existing test is deleted or weakened.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Tasks/` and
  `tests/Taskboard.Api.Tests/Features/Tasks/`.
