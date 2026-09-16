---
title: "Views: save a named filter and run it later"
labels: [feature, area:views]
---

## Context

"Urgent work that is not finished" is a filter someone rebuilds out of query-string
parameters every time they want it. This issue adds saved views: a named, stored
filter that can be run by id, including filters `GET /tasks` cannot express - more
than one status at once, and "due within N days".

Views are private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/Views/`. The feature reads the board, but **never writes
to it**.

Read `AGENTS.md` before starting, especially "The seed". The rules there override
anything you would otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Views/`
- `tests/Taskboard.Api.Tests/Features/Views/`

You own exactly these routes and no others:

```
GET    /views
POST   /views
GET    /views/{viewId}
DELETE /views/{viewId}
GET    /views/{viewId}/tasks?limit=<int>&offset=<int>
```

Do not map a catch-all. `GET /tasks` belongs to `area:tasks`; do not call into
`Features/Tasks/` - implement the filter below yourself.

`ApiFactory.ResetBoard()` does **not** reset your store, and you may not change
that. Give every view a test creates a name unique to that test (a short random
suffix works).

Get "today" from the injected `TimeProvider`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Views.ViewsFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Views/ViewsFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Views;

internal static class ViewsFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### A view

```json
{
  "id": "urgent-work",
  "name": "Urgent work",
  "filter": {
    "statuses": ["todo", "inProgress"],
    "priorities": ["high", "urgent"],
    "listId": "work",
    "dueWithinDays": 7,
    "text": "report"
  },
  "sort": "due",
  "createdAt": "..."
}
```

- `name` is required, trimmed, 1-60 characters. `id` is `Slug.From(name)`; a name
  with no letters or digits is a `400`, and an id already taken is a `409`.
- Every `filter` member is optional, and an omitted member does not filter.
  - `statuses` / `priorities`: non-empty arrays of camelCase enum names; a task
    matches when its value is **any** of them.
  - `listId`: must name an existing list **when the view is created**. If the list
    is deleted later, the view simply matches nothing.
  - `dueWithinDays`: integer 0-365. A task matches when it has a `dueOn` and
    `dueOn <= today + dueWithinDays` - overdue tasks included, undated tasks
    excluded.
  - `text`: 1-100 characters, matched case-insensitively as a substring of
    `title` or `notes`.
- `sort` is optional, one of `id` (default), `due` or `priority`. `due` is `dueOn`
  ascending with undated tasks last, then `id`. `priority` is priority descending,
  then the `due` order.
- Unknown members anywhere in the body are a `400`.

### Behaviour

- `POST /views` responds `201` with the view and `Location: /api/views/{id}`.
- `GET /views` responds `200` with every view, ordered by id.
- `GET /views/{viewId}` responds `200`, or `404`.
- `DELETE /views/{viewId}` responds `204`, or `404`.
- `GET /views/{viewId}/tasks` runs the filter against the live board **at request
  time** and responds `200` with `Page<TaskItem>`, paged through
  `PageRequest.From`. An unknown view is `404`.

## Acceptance criteria

Assert dates as offsets from `SeedData.Today`.

- A view with `statuses` `["todo","inProgress"]` and `priorities`
  `["high","urgent"]` returns `total` = 7: `draft-q3-report`, `fix-leaking-tap`,
  `pay-council-tax`, `renew-car-insurance`, `renew-passport`, `reply-to-landlord`,
  `review-pull-requests`, in that order.
- The same filter with `"sort":"priority"` returns `pay-council-tax` then
  `renew-passport` first.
- A view with `listId` `home` and `dueWithinDays` `0` returns exactly
  `fix-leaking-tap`, `pay-council-tax`, `water-the-plants`.
- A view with only `text` `"the"` and `sort` `due` places every undated match
  last.
- A view created before a task is added through `POST /api/tasks` includes that
  task when run afterwards - proving it filters live state.
- A second view with a name that slugs to the same id returns `409`.
- A missing `name`, an empty `statuses` array, `priorities` containing `"later"`,
  `dueWithinDays` of `366`, an unknown `listId`, `sort` of `"title"`, and an
  unknown field each return `400`.
- `GET`, `DELETE` and `/tasks` on `/api/views/no-such-view` each return `404`;
  `DELETE` on a real view returns `204`, and it is then gone from `GET /api/views`.
- Tests live in `tests/Taskboard.Api.Tests/Features/Views/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, cover each bullet above, and pass regardless of
  the order tests run in.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Views/`,
  `tests/Taskboard.Api.Tests/Features/Views/`, and the one registration line in
  `Program.cs`.
