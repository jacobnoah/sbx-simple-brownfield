---
title: "Dependencies: tasks that wait on other tasks, with cycle detection"
labels: [feature, area:dependencies]
---

## Context

`blocked` is a status with no explanation attached. "Clean the gutters" is blocked
on borrowing a ladder, but nothing records that, and nothing notices when the
thing it was waiting on gets done. This issue adds dependencies between tasks: a
directed graph in which a task can wait on others, which rejects cycles and
reports whether a task is ready to start.

Edges are private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/Dependencies/`. The feature reads the board, but
**never writes to it** - adding a dependency does not change any task's `status`.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Dependencies/`
- `tests/Taskboard.Api.Tests/Features/Dependencies/`

You own exactly these routes and no others:

```
GET    /tasks/{taskId}/dependencies
POST   /tasks/{taskId}/dependencies
DELETE /tasks/{taskId}/dependencies/{dependsOnId}
```

Every route carries the literal `dependencies` segment - keep it that way, and do
not map a catch-all.

`ApiFactory.ResetBoard()` does **not** reset your store, and you may not change
that. An edge one test adds between seeded tasks would still be there for the
next, and would turn an unrelated test's edge into a cycle. So **every test builds
its graph out of tasks it creates** through `POST /api/tasks`.

The graph is mutated concurrently: the cycle check and the insert must be atomic
with respect to each other, so two requests cannot together create a cycle that
neither would alone.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Dependencies.DependenciesFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Dependencies/DependenciesFeature.cs` exposes exactly
one public member:

```csharp
namespace Taskboard.Api.Features.Dependencies;

internal static class DependenciesFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `POST /tasks/{taskId}/dependencies`

Body `{ "dependsOn": "borrow-the-ladder" }` records that `taskId` waits on
`dependsOn`. Responds `201` with the same body as `GET` below and a
`Location: /api/tasks/{taskId}/dependencies` header.

| Case                                              | Response |
| ------------------------------------------------- | -------- |
| `taskId` unknown                                  | `404`    |
| `dependsOn` missing, blank, or not a string        | `400`    |
| `dependsOn` equals `taskId`                        | `400`    |
| `dependsOn` names no task on the board             | `422`, `code` = `UNPROCESSABLE_ENTITY` |
| the edge already exists                            | `409`    |
| the edge would create a cycle of any length        | `409`, with `details.cycle` listing the ids around it |
| `taskId` already waits on 20 tasks                 | `409`    |

### `GET /tasks/{taskId}/dependencies`

Responds `200`:

```json
{
  "taskId": "clean-the-gutters",
  "dependsOn": [
    { "id": "borrow-the-ladder", "status": "todo", "satisfied": false }
  ],
  "dependents": ["paint-the-fence"],
  "ready": false
}
```

- `dependsOn` is ordered by id. A dependency is `satisfied` when its task's status
  is `done`. Any other status - including `cancelled` - is unsatisfied.
- `dependents` are the ids of tasks that wait on this one, ordered by id.
- `ready` is `true` when every dependency is satisfied (so a task with none is
  ready).
- A task deleted from the board is dropped from both arrays, and from the cycle
  check, as if its edges did not exist.

### `DELETE /tasks/{taskId}/dependencies/{dependsOnId}`

Responds `204`, or `404` when that edge does not exist.

## Acceptance criteria

- `A` depending on `B` returns `201`; `GET` on `A` shows `B` unsatisfied and
  `ready` = `false`; `GET` on `B` shows `A` in `dependents`.
- Completing `B` through `POST /api/tasks/{id}/complete` makes `A` `ready`;
  cancelling it instead (`PATCH` status to `cancelled`) does not.
- With `A -> B` and `B -> C`, adding `C -> A` returns `409` and `details.cycle`
  contains `A`, `B` and `C`; the edge is not stored.
- `A -> A` returns `400`. A duplicate edge returns `409`. A `dependsOn` of
  `no-such-task` returns `422`.
- The 21st dependency on one task returns `409`.
- After `DELETE /api/tasks/{B}`, `A`'s `dependsOn` is empty and `ready` is `true`,
  and `C -> A` - which was a cycle through `B` - can now be added.
- `DELETE` of an edge returns `204`; repeating it returns `404`.
- Every route under `/api/tasks/no-such-task/dependencies` returns `404`.
- No task's `status` or `updatedAt` changes as a result of any call to this
  feature.
- Tests live in `tests/Taskboard.Api.Tests/Features/Dependencies/`, carry
  `[Collection(ApiCollection.Name)]`, build their graphs from tasks they create,
  cover each bullet above, and pass regardless of the order tests run in.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Dependencies/`,
  `tests/Taskboard.Api.Tests/Features/Dependencies/`, and the one registration
  line in `Program.cs`.
