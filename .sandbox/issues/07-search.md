---
title: "Search: ranked full-text search across tasks and lists"
labels: [feature, area:search]
---

## Context

`GET /tasks?q=` does a plain substring filter over titles and notes and returns
the matches in whatever order `sort` asks for. There is no relevance: a task
whose title *is* the word you typed ranks no higher than one that mentions it in
passing in its notes, and lists cannot be searched at all. This issue adds a
ranked search endpoint.

The feature is pure projection. It injects `ITaskStore` and `ITaskListStore`,
holds no state, and knows about no other feature.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Search/`
- `tests/Taskboard.Api.Tests/Features/Search/`

You own exactly one route: `GET /search`. Do not map anything else, and do not
map a catch-all. Do not reuse or reference the `q` filter in `Features/Tasks/` -
the ranking below is its own implementation.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Search.SearchFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Search/SearchFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Search;

internal static class SearchFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `GET /search?q=<string>&kind=<task|list>&limit=<int>&offset=<int>`

Responds `200` with `Page<T>` where the item type is local to your feature:

```json
{
  "items": [
    { "kind": "task", "id": "renew-passport", "title": "Renew the passport", "score": 60 }
  ],
  "total": 1,
  "limit": 25,
  "offset": 0
}
```

`title` is the task's title or the list's name.

### Terms

- `q` is required. It is trimmed, must be 1-100 characters, and is split on
  whitespace into **terms**. Terms are compared case-insensitively and duplicates
  are collapsed. More than 5 distinct terms is a `400`.
- The **words** of a title or name are the runs of ASCII letters and digits in it
  - "Draft the Q3 report" has the words `draft`, `the`, `q3`, `report`.

### Scoring

Each term scores against a candidate by the **best** rule it satisfies:

| Rule                                         | Score |
| -------------------------------------------- | ----- |
| A word of the title/name equals the term      | 30    |
| A word of the title/name starts with the term | 20    |
| The title/name contains the term anywhere     | 10    |
| The task's notes contain the term (tasks only) | 5     |

A candidate is a match only when **every** term scores above zero. Its `score` is
the sum of its term scores. Tasks of every status are searchable.

- `kind` is optional and restricts results to `task` or `list`; any other value is
  a `400`.
- Ordered by `score` descending, then `kind` (`list` before `task`), then `id`
  ordinal ascending.
- Paging goes through `PageRequest.From`.

## Acceptance criteria

- `?q=renew` returns `total` = 2: `renew-car-insurance` then `renew-passport`,
  both with `score` = 30.
- `?q=renew passport` returns only `renew-passport`, with `score` = 60 - a task
  matching one term but not the other is excluded.
- `?q=PASS` returns `renew-passport` with `score` = 20 (case-insensitive, prefix
  of a word).
- `?q=drawer` returns `renew-passport` with `score` = 5 (notes only).
- `?q=work` returns the `work` list with `kind` = `"list"` and `score` = 30, and
  `?q=work&kind=task` returns `total` = 0.
- `?q=re` returns `total` = 7 in exactly this order: `draft-q3-report`,
  `read-onboarding-docs`, `renew-car-insurance`, `renew-passport`,
  `reply-to-landlord`, `review-pull-requests` (each 20), then `pay-council-tax`
  (5, from its notes).
- `?q=renew renew` scores the same as `?q=renew` - duplicate terms collapse.
- A task created through `POST /api/tasks` is findable immediately.
- These each return `400` with `code` = `BAD_REQUEST`: a missing `q`, a blank `q`,
  a `q` over 100 characters, six distinct terms, `kind=widget`, and an
  out-of-range `limit` or negative `offset`.
- Tests live in `tests/Taskboard.Api.Tests/Features/Search/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, and cover each bullet above.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Search/`,
  `tests/Taskboard.Api.Tests/Features/Search/`, and the one registration line in
  `Program.cs`.
