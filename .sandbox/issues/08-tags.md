---
title: "Tags: free-form labels on tasks, and browsing by tag"
labels: [feature, area:tags]
---

## Context

A task belongs to exactly one list, and that is the only way to group tasks.
"Everything to do with the car" or "anything waiting on a phone call" cuts across
lists, and there is nowhere to record it. This issue adds tags: short labels a
task can carry any number of, and a way to browse the board by them.

Tags are private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/Tags/`, keyed by `TaskItem.Id`. The feature reads the
board to check that tasks exist, but **never writes to it** - `TaskItem` does not
gain a field.

Read `AGENTS.md` before starting, especially "The domain". The rules there
override anything you would otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Tags/`
- `tests/Taskboard.Api.Tests/Features/Tags/`

You own exactly these routes and no others:

```
GET    /tasks/{taskId}/tags
PUT    /tasks/{taskId}/tags
PUT    /tasks/{taskId}/tags/{tag}
DELETE /tasks/{taskId}/tags/{tag}
GET    /tags
GET    /tags/{tag}/tasks?limit=<int>&offset=<int>
```

`/tasks` and `/tasks/{taskId}` belong to `area:tasks`. Your task routes are safe
because each carries the literal `tags` segment - keep it that way, and do not map
a catch-all.

The store lives in your own folder; a `static readonly ConcurrentDictionary` is
the usual answer. `ApiFactory.ResetBoard()` does **not** reset it and you may not
change that. Write tests that do not depend on execution order - use tag names
unique to each test (a short random suffix works) rather than assuming the store
starts empty.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Tags.TagsFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Tags/TagsFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Tags;

internal static class TagsFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### Tag format

A tag is trimmed and lower-cased, then must match `^[a-z0-9][a-z0-9-]{0,29}$`.
Anything else is a `400`. A task carries at most **10** tags; a task's tags are a
set, reported in ordinal order.

### Behaviour

- `GET /tasks/{taskId}/tags` responds `200` with
  `{ "taskId": "buy-milk", "tags": ["errand", "quick"] }`. A task with no tags
  returns an empty array.
- `PUT /tasks/{taskId}/tags` with `{ "tags": ["Quick", "errand", "quick"] }`
  **replaces** the task's tags with the normalised, de-duplicated set and responds
  `200` with the same shape as `GET`. An empty array clears them. More than 10
  distinct tags is a `400` and changes nothing.
- `PUT /tasks/{taskId}/tags/{tag}` adds one tag and responds `200` with the full
  set. Adding a tag the task already has is a no-op `200`. Adding an eleventh tag
  is a `409` with `code` = `CONFLICT`.
- `DELETE /tasks/{taskId}/tags/{tag}` responds `204`, or `404` when the task does
  not carry that tag.
- `GET /tags` responds `200` with every tag in use, ordinal order, each with the
  number of tasks carrying it: `[{ "tag": "errand", "taskCount": 2 }]`.
- `GET /tags/{tag}/tasks` responds `200` with `Page<TaskItem>` of the tasks
  carrying the tag, ordered by `id`, paged through `PageRequest.From`. A tag no
  task carries returns an empty page, not a `404`.
- Every `/tasks/{taskId}/...` route returns `404` with `code` = `NOT_FOUND` when
  `ITaskStore.Find` does not know the task.
- A task that has been deleted from the board is invisible to `GET /tags` and
  `GET /tags/{tag}/tasks`, even though your store still holds its tags.

## Acceptance criteria

- `PUT /api/tasks/buy-milk/tags` with `{"tags":["Quick"," errand ","quick"]}`
  returns `200` and `tags` = `["errand","quick"]`; a following `GET` agrees.
- `PUT /api/tasks/buy-milk/tags/urgent-ish` adds a tag; repeating it returns `200`
  and the set is unchanged.
- An eleventh tag via `PUT .../tags/{tag}` returns `409`; eleven tags via
  `PUT .../tags` returns `400`; neither changes the stored set.
- `-bad`, `has space`, `under_score` and a 31-character tag each return `400`.
- `DELETE` returns `204`, and a second `DELETE` of the same tag returns `404`.
- Tagging `buy-milk` and `order-printer-ink` with the same tag makes
  `GET /api/tags` report `taskCount` = 2 for it and
  `GET /api/tags/{tag}/tasks` return exactly those two tasks, `total` = 2.
- After `DELETE /api/tasks/order-printer-ink`, the same calls report 1.
- Every task route under `/api/tasks/no-such-task/tags` returns `404`.
- Nothing in the feature mutates a `TaskItem` or anything returned by a store.
- Tests live in `tests/Taskboard.Api.Tests/Features/Tags/`, carry
  `[Collection(ApiCollection.Name)]`, cover each bullet above, and pass regardless
  of the order tests run in.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Tags/`,
  `tests/Taskboard.Api.Tests/Features/Tags/`, and the one registration line in
  `Program.cs`.
