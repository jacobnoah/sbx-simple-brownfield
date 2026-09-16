---
title: "Links: attach reference URLs to a task"
labels: [feature, area:links]
---

## Context

"Review the open pull requests" means nothing without the pull request, and
"Renew the car insurance" wants the insurer's renewal page. People paste URLs into
`notes` today, where nothing validates them and nothing can find them again. This
issue adds typed links on tasks, and a way to find every task linking to a site.

Links are private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/Links/`, keyed by `TaskItem.Id`. The feature reads the
board, but **never writes to it**. It never fetches a URL.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Links/`
- `tests/Taskboard.Api.Tests/Features/Links/`

You own exactly these routes and no others:

```
GET    /tasks/{taskId}/links
POST   /tasks/{taskId}/links
DELETE /tasks/{taskId}/links/{linkId}
GET    /links?host=<string>&limit=<int>&offset=<int>
```

Every task route carries the literal `links` segment - keep it that way, and do not
map a catch-all.

`ApiFactory.ResetBoard()` does **not** reset your store, and you may not change
that. Write tests that do not depend on execution order: attach links to tasks the
test creates, and use a host unique to the test (for example
`<random>.example.com`) when asserting on `GET /links`.

Get timestamps from the injected `TimeProvider`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Links.LinksFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Links/LinksFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Links;

internal static class LinksFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### A link

```json
{
  "id": "0199...",
  "taskId": "review-pull-requests",
  "url": "https://github.com/org/repo/pulls",
  "host": "github.com",
  "title": "Open PRs",
  "addedAt": "2026-09-11T10:00:00+00:00"
}
```

- `url` is required, trimmed, at most 2000 characters, and must parse as an
  **absolute** `http` or `https` URI with a host. Anything else - relative,
  `ftp:`, `javascript:`, `mailto:` - is a `400`. It is stored as
  `Uri.AbsoluteUri`, so `HTTPS://Example.com/a` is stored as
  `https://example.com/a`.
- `host` is derived from the URL, lower-case, never supplied.
- `title` is optional, trimmed, at most 100 characters; when absent or blank it
  defaults to `host`.
- `id` and `addedAt` are server-generated.

### Behaviour

- `POST /tasks/{taskId}/links` responds `201` with the link and
  `Location: /api/tasks/{taskId}/links/{id}`. A URL whose normalised form is already
  linked on that task is a `409`. A task holds at most 20 links; the 21st is a
  `409`.
- `GET /tasks/{taskId}/links` responds `200` with an array ordered by `addedAt`
  then `id`.
- `DELETE /tasks/{taskId}/links/{linkId}` responds `204`, or `404` when unknown or
  on another task.
- `GET /links?host=` responds `200` with `Page<T>` of
  `{ "link": {...}, "task": TaskItem }` for every link on that host, across every
  task **still on the board**, ordered by `addedAt` then link `id`. `host` is
  required and compared case-insensitively; it matches the host exactly, not
  subdomains. Paging goes through `PageRequest.From`.

An unknown `taskId` is `404` on every task route.

## Acceptance criteria

- Posting `HTTPS://Example.com/a` returns `201` with `url` =
  `"https://example.com/a"`, `host` = `"example.com"`, `title` = `"example.com"`
  and a `Location` header.
- Posting the same URL again on the same task, in any casing of scheme or host,
  returns `409`; posting it on a different task returns `201`.
- `/relative`, `ftp://example.com`, `javascript:alert(1)`, `mailto:a@b.c`, a
  2001-character URL, a missing `url`, and a 101-character `title` each return
  `400`.
- The 21st link on one task returns `409`.
- Links come back from `GET` in the order added.
- `GET /api/links?host={unique host}` returns links from two different tasks with
  their live `TaskItem`s, `total` = 2; the same host in upper case matches; a
  subdomain of it does not.
- After `DELETE /api/tasks/{id}` on one of those tasks, `total` drops to 1.
- A missing `host` returns `400`.
- `DELETE` of a link returns `204`; repeating it, or deleting it under another task
  id, returns `404`.
- Every route under `/api/tasks/no-such-task/links` returns `404`.
- Tests live in `tests/Taskboard.Api.Tests/Features/Links/`, carry
  `[Collection(ApiCollection.Name)]`, cover each bullet above, and pass regardless
  of the order tests run in.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Links/`,
  `tests/Taskboard.Api.Tests/Features/Links/`, and the one registration line in
  `Program.cs`.
