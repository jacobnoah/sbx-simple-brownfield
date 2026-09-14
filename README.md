# Taskboard API

A task-tracking API that is **already built**. Two features - tasks and lists -
are implemented, tested and documented, over a seeded board of four lists and
twenty tasks. It exists to be cloned as a sandbox repo where several agents add
features to, and fix bugs in, code they did not write.

This is the brownfield counterpart to the greenfield blueprints: nothing here is
a blank slate, and the seeded issues are about changing working code rather than
filling in an empty `Features/` folder.

If you are an AI agent working in this repo, read [AGENTS.md](./AGENTS.md) first.

## Requirements

- .NET SDK 10.0.401 or newer (pinned in `global.json`, `rollForward: latestFeature`)

## Restore, test, lint

```sh
dotnet restore Taskboard.slnx
dotnet test Taskboard.slnx
dotnet format Taskboard.slnx --verify-no-changes
```

All three pass on a fresh clone. Keep it that way.

- `dotnet test` builds first, and the build treats every compiler and analyzer
  warning as an error - so it is the correctness half of the gate.
- `dotnet format --verify-no-changes` is the style half, driven entirely by
  `.editorconfig`. Run `dotnet format Taskboard.slnx` without the flag to fix.

Note that a green suite does **not** mean the code is correct. Some seeded issues
describe behaviour the current tests do not cover, and the fix for those starts
with a test that fails.

Package versions are pinned exactly and committed as `packages.lock.json`. To
prove a restore is reproducible, use `dotnet restore Taskboard.slnx --locked-mode`.

## Run

```sh
dotnet run --project src/Taskboard.Api
```

Listens on <http://localhost:5090> (see `Properties/launchSettings.json`).

```sh
curl http://localhost:5090/health
curl http://localhost:5090/api/tasks
curl http://localhost:5090/api/lists
```

Interactive API docs are at <http://localhost:5090/scalar>.

Feature endpoints are mounted under the `App:FeatureRoutePrefix` prefix, `/api`
by default.

## The domain

Two records, in `src/Taskboard.Api/Domain/`:

```csharp
public sealed record TaskItem(
    string Id,              // "renew-passport" - stable slug, used in URLs
    string Title,
    string? Notes,
    string ListId,          // always a real list; never null
    TaskState Status,       // Todo | InProgress | Blocked | Done | Cancelled
    TaskPriority Priority,  // Low | Normal | High | Urgent
    DateOnly? DueOn,        // null when the task has no due date
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record TaskList(string Id, string Name, DateTimeOffset CreatedAt);
```

The enum is `TaskState` rather than `TaskStatus` because
`System.Threading.Tasks.TaskStatus` is in scope through implicit usings. The JSON
property is still `status`, and enums cross the wire as camelCase names -
`"inProgress"`, not `1`.

### The board is mutable

Unlike the greenfield blueprints, whose catalogue is immutable, this API creates,
updates and deletes. Features read and write through two stores:

```csharp
public interface ITaskStore
{
    IReadOnlyList<TaskItem> Tasks { get; }   // ordered by Id
    TaskItem? Find(string id);
    bool TryAdd(TaskItem task);
    bool TryReplace(TaskItem updated);
    bool Remove(string id);
}
```

`ITaskListStore` is the same shape over `TaskList`. Both are in-memory, hold a
lock around a dictionary, and are seeded from `Store/SeedData.cs` at process
start. A `TaskItem` is a record: updating one means `existing with { ... }`
followed by `TryReplace`, never mutation in place.

That mutability has two consequences worth planning for:

- The test host is shared, so one test's writes are visible to the next. Call
  `ApiFactory.ResetBoard()` at the top of every test that depends on the seed.
- Two features can both write the same task. Keep state your own feature invents
  in your own folder, keyed by `TaskItem.Id`.

### The seed

Four lists - `inbox`, `home`, `work`, `errands` - and twenty tasks, of which:

| Slice                                 | Count |
| ------------------------------------- | ----- |
| Total                                 | 20    |
| Open (neither done nor cancelled)     | 16    |
| Done                                  | 3     |
| Cancelled                             | 1     |
| Open with no due date                 | 3     |
| Open and overdue                      | 3     |
| Open and due today                    | 3     |
| Open and due within the next 7 days   | 3     |
| Open and due later than that          | 4     |

Due dates are **relative to the day the process starts**, not literal dates, so
"overdue" and "due today" stay meaningful however long the blueprint sits on the
shelf. `SeedData.Today` is the anchor; tests assert against offsets from it and
never against a hard-coded date. `StoreTests.cs` locks every count in the table
above, so a change to the seed fails loudly.

Anything that needs the current time injects `TimeProvider`. Nothing calls
`DateTime.UtcNow` in a handler.

## Implemented endpoints

All paths are relative to `/api`.

### Tasks - `src/Taskboard.Api/Features/Tasks/`

| Method   | Route                        | Notes                                                   |
| -------- | ---------------------------- | ------------------------------------------------------- |
| `GET`    | `/tasks`                     | Filter, sort and page. Returns `Page<TaskItem>`.         |
| `POST`   | `/tasks`                     | Id derived from the title, suffixed `-2` on a clash.     |
| `GET`    | `/tasks/{taskId}`            |                                                          |
| `PATCH`  | `/tasks/{taskId}`            | Omitted field = unchanged; explicit `null` clears it.    |
| `DELETE` | `/tasks/{taskId}`            | `204`.                                                   |
| `POST`   | `/tasks/{taskId}/complete`   | `409` if already done.                                   |
| `POST`   | `/tasks/{taskId}/reopen`     | Done or cancelled only; clears `completedAt`.            |

`GET /tasks` accepts `status`, `priority`, `listId`, `q`, `dueBefore`, `sort`,
`limit` and `offset`. `sort` is one of `id` (default), `created`, `due`,
`priority` or `title`; **tasks with no due date sort last** under `due` and
`priority`.

### Lists - `src/Taskboard.Api/Features/Lists/`

| Method   | Route                     | Notes                                                          |
| -------- | ------------------------- | -------------------------------------------------------------- |
| `GET`    | `/lists`                  | Returns `Page<TaskListSummary>` with task counts.               |
| `POST`   | `/lists`                  | Id derived from the name. `409` on a clash.                     |
| `GET`    | `/lists/{listId}`         |                                                                 |
| `PATCH`  | `/lists/{listId}`         | Rename. The id never changes.                                   |
| `DELETE` | `/lists/{listId}`         | Moves its tasks to `?reassignTo=` (default `inbox`), then `204`. |
| `GET`    | `/lists/{listId}/tasks`   | `status`, `limit`, `offset`.                                    |

The `inbox` list always exists and cannot be deleted.

## Configuration

Settings bind from the `App` section and are validated at startup - an invalid
value fails fast with a described error rather than surfacing later. Defaults
live in both `appsettings.json` and the `AppConfig` property initialisers, so the
app still starts if the file is missing.

| Setting                    | Default   | Notes                                       |
| -------------------------- | --------- | ------------------------------------------- |
| `App:FeatureRoutePrefix`   | `/api`    | Must start with `/`, must not end with `/`  |
| `App:DefaultPageSize`      | `25`      | 1-1000                                      |
| `App:MaxPageSize`          | `100`     | 1-1000, and `>= DefaultPageSize`            |
| `App:MaxRequestBodyBytes`  | `1048576` | 1 KiB - 100 MiB, applied to Kestrel         |

Override per the usual ASP.NET Core precedence, e.g. `App__DefaultPageSize=50` as
an environment variable.

## Errors

Every failure returns an RFC 9457 `application/problem+json` body. Alongside the
standard members there is a `code` extension carrying a stable, machine-readable
error code, and an optional `details` extension carrying structure:

```json
{
  "title": "Bad Request",
  "status": 400,
  "detail": "limit must be between 1 and 100.",
  "instance": "GET /api/tasks",
  "code": "BAD_REQUEST",
  "details": { "limit": 500, "maxPageSize": 100 }
}
```

## Layout

```
Taskboard.slnx                    solution
global.json                       SDK pin + `dotnet test` runner opt-in
Directory.Build.props             shared build settings and the warnings-as-errors gate
Directory.Packages.props          central package management, exact version pins
.editorconfig                     drives dotnet format
src/Taskboard.Api/
  Program.cs                      composition root + the feature registration block
  Domain/TaskItem.cs              the task record and its enums
  Domain/TaskList.cs              the list record
  Domain/Slug.cs                  title -> kebab-case id
  Store/ITaskStore.cs             read/write access to tasks
  Store/ITaskListStore.cs         read/write access to lists
  Store/InMemoryTaskStore.cs      dictionary behind a lock, plus the test reset hook
  Store/InMemoryTaskListStore.cs  the same, for lists
  Store/SeedData.cs               the four lists and twenty tasks
  Configuration/AppConfig.cs      settings, defaults, validation
  Contracts/SharedContracts.cs    contracts shared by two or more features
  Errors/AppException.cs          the one exception type features throw
  Errors/AppExceptionHandler.cs   maps exceptions to ProblemDetails
  Routing/FeatureRoutes.cs        the route group features register against
  Routing/PageRequest.cs          validated limit/offset paging
  Features/Tasks/                 the tasks feature
  Features/Lists/                 the lists feature
tests/Taskboard.Api.Tests/
  ApiFactory.cs                   shared in-memory test host + ResetBoard()
  StoreTests.cs                   locks in the seed's invariants
  SmokeTests.cs                   boots the app, hits /health and /scalar
  Features/Tasks/                 the tasks feature's tests
  Features/Lists/                 the lists feature's tests
.sandbox/                         labels and issue seeds used to provision the sandbox repo
```

New feature work lives entirely under `src/Taskboard.Api/Features/<Name>/` and
`tests/Taskboard.Api.Tests/Features/<Name>/`. Bug work lives inside the feature
folder that owns the defect. Everything else is shared and effectively read-only
- see AGENTS.md for the one exception.
