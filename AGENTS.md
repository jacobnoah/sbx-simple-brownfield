# AGENTS.md

Instructions for an AI agent working on one issue in this repository.

This is a **brownfield** repo: the API already works. Two features are
implemented, tested and documented, and your issue either adds a new one or fixes
something the existing code gets wrong.

You are one of several agents working on this repo at the same time. You cannot
see the others' work and they cannot see yours. Every rule below exists so that
your branch merges cleanly against theirs. Follow them literally.

## The domain

`Taskboard.Api` is a task-tracking API over two records in
`src/Taskboard.Api/Domain/`:

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

The enum is `TaskState`, not `TaskStatus`, because
`System.Threading.Tasks.TaskStatus` is in scope through implicit usings. The JSON
property is `status`, and enums cross the wire as camelCase names -
`"inProgress"`, not `1`.

Read and write them by injecting the stores into your endpoint handler:

```csharp
routes.MapGet("/due/tasks", (ITaskStore tasks, TimeProvider clock) =>
    tasks.Tasks.Where(task => task.DueOn < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)));
```

```csharp
public interface ITaskStore
{
    IReadOnlyList<TaskItem> Tasks { get; }   // ordered by Id, ordinal
    TaskItem? Find(string id);
    bool TryAdd(TaskItem task);
    bool TryReplace(TaskItem updated);
    bool Remove(string id);
}
```

`ITaskListStore` is the same shape over `TaskList`, and `TaskList.InboxId` is the
id of the list that always exists.

**The board is mutable, and shared.** That is the central difference from the
greenfield blueprints, and it has three consequences:

- A `TaskItem` is a record. Update one with `existing with { Title = ... }`
  followed by `TryReplace`. Never try to mutate one in place, and never mutate
  anything you get back from a store.
- Another feature may have written the task you are reading. Do not assume the
  seed is intact at request time; look tasks up by id and handle a `null`.
- State your feature *invents* - a comment thread, a saved filter - lives in your
  own folder, keyed by `TaskItem.Id`. Do not try to add a field to `TaskItem`.

Anything that needs the current time injects `TimeProvider` and calls
`GetUtcNow()`. Do not call `DateTime.UtcNow` or `DateTimeOffset.Now` in a
handler - a test cannot control them.

### The seed

`Store/SeedData.cs` holds four lists and twenty tasks. Due dates are **relative
to the day the process starts**, anchored on `SeedData.Today`, so that "overdue"
and "due today" never go stale. Assert against offsets from `SeedData.Today`,
never against a literal date. `tests/Taskboard.Api.Tests/StoreTests.cs` locks the
seed's shape - counts by status, by priority, and by due-date bucket - so you can
rely on those numbers.

---

## 1. What you own

Your issue is one of two kinds, and it says which.

**A new feature.** You own exactly two folders, named after it:

```
src/Taskboard.Api/Features/<Name>/         implementation
tests/Taskboard.Api.Tests/Features/<Name>/ tests
```

`<Name>` is PascalCase and is given to you in the issue. Create both folders. Put
every file you write inside them.

**A bug in an existing feature.** You own the two folders of the feature named in
the issue - `Features/Tasks/` and `tests/.../Features/Tasks/`, say - and you may
change existing files inside them, including existing tests. You do not create a
new feature folder, and you do not touch a different feature's folder.

Either way: do not create or change files anywhere else in the repo, with the one
exception in rule 2.

You do not need to touch any `.csproj`. Both projects glob their sources, so a
new `.cs` file under your folder is picked up automatically.

### Route ownership

Every feature is mounted under the same route group, so route space is divided up
in advance. Your issue names the routes you own. Map those and nothing else.

| Area                 | Routes reserved                                                                                                                | Status      |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------------ | ----------- |
| `area:tasks`         | `/tasks`, `/tasks/{taskId}`, `/tasks/{taskId}/complete`, `/tasks/{taskId}/reopen`                                              | implemented |
| `area:lists`         | `/lists`, `/lists/{listId}`, `/lists/{listId}/tasks`                                                                           | implemented |
| `area:due`           | `/due/tasks`, `/due/summary`                                                                                                   | to build    |
| `area:comments`      | `/tasks/{taskId}/comments`, `/tasks/{taskId}/comments/{commentId}`                                                             | to build    |
| `area:bulk`          | `/bulk/tasks`                                                                                                                  | to build    |
| `area:stats`         | `/stats/board`, `/stats/lists/{listId}`                                                                                        | to build    |
| `area:search`        | `/search`                                                                                                                      | to build    |
| `area:tags`          | `/tasks/{taskId}/tags`, `/tasks/{taskId}/tags/{tag}`, `/tags`, `/tags/{tag}/tasks`                                             | to build    |
| `area:checklist`     | `/tasks/{taskId}/checklist`, `/tasks/{taskId}/checklist/{itemId}`                                                              | to build    |
| `area:assignees`     | `/tasks/{taskId}/assignee`, `/assignees`, `/assignees/{assignee}/tasks`                                                        | to build    |
| `area:time-tracking` | `/tasks/{taskId}/time-entries`, `/tasks/{taskId}/time-entries/{entryId}`, `/time-tracking/summary`                             | to build    |
| `area:reminders`     | `/tasks/{taskId}/reminders`, `/tasks/{taskId}/reminders/{reminderId}`, `/reminders/due`, `/reminders/{reminderId}/acknowledge` | to build    |
| `area:snooze`        | `/tasks/{taskId}/snooze`, `/snooze/overdue`                                                                                    | to build    |
| `area:dependencies`  | `/tasks/{taskId}/dependencies`, `/tasks/{taskId}/dependencies/{dependsOnId}`                                                   | to build    |
| `area:recurrence`    | `/tasks/{taskId}/recurrence`, `/tasks/{taskId}/recurrence/next`                                                                | to build    |
| `area:export`        | `/export/tasks`, `/export/lists`                                                                                               | to build    |
| `area:import`        | `/import/tasks`                                                                                                                | to build    |
| `area:trash`         | `/trash`, `/trash/{taskId}`, `/trash/{taskId}/restore`                                                                         | to build    |
| `area:pins`          | `/pins`, `/pins/{taskId}`                                                                                                      | to build    |
| `area:views`         | `/views`, `/views/{viewId}`, `/views/{viewId}/tasks`                                                                           | to build    |
| `area:duplicate`     | `/tasks/{taskId}/duplicate`                                                                                                    | to build    |
| `area:calendar`      | `/calendar`                                                                                                                    | to build    |
| `area:kanban`        | `/kanban`, `/kanban/tasks/{taskId}/move`                                                                                       | to build    |
| `area:links`         | `/tasks/{taskId}/links`, `/tasks/{taskId}/links/{linkId}`, `/links`                                                            | to build    |
| `area:focus`         | `/focus`, `/focus/suggestions`                                                                                                 | to build    |

Two rules follow, and breaking either one breaks somebody else's feature rather
than your own:

- **Never map a route another area reserves**, even if your issue would read more
  naturally with it. In particular, `area:tasks` already owns `/tasks` and
  `/tasks/{taskId}`; a new feature that hangs off a task must add a literal
  segment of its own, the way `/tasks/{taskId}/comments` does.
- **Never map a catch-all.** A route like `/{id}` or `/tasks/{*rest}` swallows
  routes you cannot see.

If you think you need a reserved route, that is a stop condition - see rule 8.

## 2. The one file you may touch outside your folders

`src/Taskboard.Api/Program.cs`, and only inside the marked block:

```csharp
// ---- FEATURE REGISTRATION ----
// Agents: add exactly one call below.
// Keep alphabetical. Do not restructure.
private static void RegisterFeatures(IEndpointRouteBuilder app)
{
    Features.Lists.ListsFeature.Register(app);
    Features.Tasks.TasksFeature.Register(app);
}
// ---- END FEATURE REGISTRATION ----
```

If you are building a new feature you add **exactly one line**, in alphabetical
position among the lines already there:

```csharp
    Features.Due.DueFeature.Register(app);
```

Write the fully-qualified `Features.<Name>.<Name>Feature` form shown above. Do
**not** add a `using` directive at the top of the file - the whole point of the
qualified form is that one line is all you touch. Change nothing else in the
file.

If you are fixing a bug, you touch this file not at all: the feature is already
registered.

Several agents editing the same one line of the same file is the whole reason for
the alphabetical rule. Deterministic ordering means git resolves most of these
automatically, and a human resolves the rest in seconds.

## 3. Read-only shared files

```
src/Taskboard.Api/Program.cs            (except the block above)
src/Taskboard.Api/Domain/**
src/Taskboard.Api/Store/**
src/Taskboard.Api/Configuration/**
src/Taskboard.Api/Contracts/**
src/Taskboard.Api/Errors/**
src/Taskboard.Api/Routing/**
src/Taskboard.Api/appsettings*.json
src/Taskboard.Api/Properties/**
src/Taskboard.Api/Taskboard.Api.csproj
src/Taskboard.Api/Features/<every feature that is not yours>/**
tests/Taskboard.Api.Tests/ApiFactory.cs
tests/Taskboard.Api.Tests/StoreTests.cs
tests/Taskboard.Api.Tests/GlobalUsings.cs
tests/Taskboard.Api.Tests/SmokeTests.cs
tests/Taskboard.Api.Tests/Taskboard.Api.Tests.csproj
tests/Taskboard.Api.Tests/Features/<every feature that is not yours>/**
Directory.Build.props
Directory.Packages.props
Taskboard.slnx
global.json
NuGet.config
.editorconfig
```

Do not edit these. If your issue seems to require a change to one of them,
**stop, leave it unchanged, implement everything you can without it, and say so
explicitly in your PR description** - what you needed, why, and what you did
instead. Do not edit it and mention it afterwards. A shared-file edit from one
agent breaks every other agent's branch.

`Store/SeedData.cs` is on that list for a reason. A bug fix that needs different
seed data is not a bug fix - the seed is the fixture every other agent's tests
assert against. Create the rows your test needs through the API instead.

This includes `Directory.Packages.props`. **Do not add, remove, or change a
package.** Every version is pinned exactly with NuGet bracket notation
(`[10.0.12]` means that version and no other) and committed to
`packages.lock.json`. If your feature needs a package that is not already
referenced, stop and say so in the PR; use the base class library in the
meantime.

Available to you: everything in the `Microsoft.AspNetCore.App` shared framework
and the base class library. For tests, `xunit.v3` and
`Microsoft.AspNetCore.Mvc.Testing`. The Scalar API reference is already wired up -
you do not need to reference or configure it, only to annotate your endpoints as
described in rule 5.

**Do not relax the gate.** Do not edit `.editorconfig`, do not add
`#pragma warning disable`, do not add `[SuppressMessage]`, and do not set
`NoWarn`. If an analyzer is fighting you, that is a signal about your code.

## 4. Feature shape

`src/Taskboard.Api/Features/<Name>/<Name>Feature.cs` contains an internal static
class with **exactly one** public entry point. `Features/Lists/ListsFeature.cs`
is the shortest complete example in the repo - read it before you start.

```csharp
// src/Taskboard.Api/Features/Archive/ArchiveFeature.cs
using Microsoft.Extensions.Options;

using Taskboard.Api.Configuration;
using Taskboard.Api.Domain;
using Taskboard.Api.Errors;
using Taskboard.Api.Store;

namespace Taskboard.Api.Features.Archive;

internal static class ArchiveFeature
{
    public static void Register(IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/archive/{taskId}", (string taskId, ITaskStore tasks) =>
        {
            var task = tasks.Find(taskId);

            if (task is null)
            {
                throw AppException.NotFound($"No task '{taskId}'.");
            }

            return task;
        })
        .WithName("ArchiveGet");
    }
}
```

Note the `using` order: `System.*` first, then one group per root namespace in
alphabetical order, blank line between groups - so `Microsoft.*` comes **before**
`Taskboard.*`. `.editorconfig` sets `dotnet_separate_import_directive_groups`,
and `dotnet format` will fail the build if you get it wrong. Run
`dotnet format Taskboard.slnx` to fix it.

Rules:

- The class is `internal static`, named `<Name>Feature`, in namespace
  `Taskboard.Api.Features.<Name>`. `internal` is deliberate - `Program.cs` is in
  the same assembly, and the analyzers flag needlessly public types in an
  executable.
- One public member: `public static void Register(IEndpointRouteBuilder routes)`.
  Everything else in your folder is `private` or `internal`.
- Map endpoints onto the builder you are handed. It is already scoped to the
  configured route prefix (`/api` by default), so map `"/due/tasks"`, not
  `"/api/due/tasks"`. Never build your own `WebApplication`, never add global
  middleware, never call `Run`.
- Split freely into other files inside your own folder (`DueService.cs`,
  `DueContracts.cs`, ...). Only `Register` is the public surface.
- Do not reference another feature's namespace, and do not expose types another
  feature could take a dependency on. If your feature needs behaviour that
  `Features/Tasks/` already implements - completing a task, say - reimplement it
  against `ITaskStore` to match the documented semantics. Do not call into it.

## 5. Conventions

**Errors.** Throw `AppException` from `Taskboard.Api.Errors`, or one of its static
factories (`AppException.BadRequest`, `.NotFound`, `.Conflict`,
`.Unprocessable`, ...). Never throw a bare `Exception`, and never hand-write an
error response body. The registered `IExceptionHandler` turns an `AppException`
into an RFC 9457 `application/problem+json` response carrying the right status
and a `code` extension member; anything else becomes an opaque 500 with its
message stripped.

Do not return `Results.NotFound()`, `Results.BadRequest()` or
`Results.Problem()` from a handler - they bypass the shared error shape. Throw
instead. `Results.Created(...)` and `Results.NoContent()` are fine.

**Configuration.** Inject `IOptions<AppConfig>` into your endpoint handler. Never
inject `IConfiguration`, never call `Environment.GetEnvironmentVariable`, and
never read `appsettings.json` yourself. If you need a new setting, you cannot add
one - see rule 3 and use a `private const` in your own folder.

**Paging.** Any endpoint returning a page uses `Page<T>` from
`Taskboard.Api.Contracts` and parses its parameters with `PageRequest.From`:

```csharp
var page = PageRequest.From(limit, offset, options.Value);

return page.Apply(matched);
```

That gives you the shared `limit`/`offset` validation and the rule that `total`
is the count *before* paging. Do not roll your own.

**Contracts.** Keep your request and response types inside your own folder. Add
to `Contracts/SharedContracts.cs` only if two or more features need the same type
- and since you cannot see the other features, in practice that means never. Use
the existing `Page<T>`, `TaskItem` and `TaskList` where they fit - they are
already the shared types, so do not redefine them.

**Validation.** Validate everything that comes off the wire - route values, query
string, body - and convert failures with
`AppException.BadRequest(message, details)`. A JSON body that must tell "field
absent" apart from "field explicitly null" is bound as `JsonElement` and read
property by property; `Features/Tasks/TaskContracts.cs` shows the pattern. Do not
trust bound parameters to be in range just because they bound.

**State.** Keep any state your feature invents private to it - a `static readonly
ConcurrentDictionary` in your own folder is the usual answer, and concurrency
safety matters because these endpoints mutate. Do not add a service to the root
DI container in `Program.cs`, do not use a global, and do not write to a file
another feature might also touch.

**OpenAPI.** Every endpoint you map appears automatically in the Scalar API
reference at `/scalar`, so document it as you go:

```csharp
routes.MapGet("/due/tasks", Handler)
    .WithName("DueTasks")             // see the naming rule below
    .WithSummary("List tasks that are overdue or coming up.")
    .Produces<Page<TaskItem>>()
    .ProducesProblem(StatusCodes.Status400BadRequest);
```

**Endpoint names must be globally unique across the whole application**, and a
collision is not a local failure - it throws at startup and takes down every
other feature's tests too:

```
System.InvalidOperationException: Duplicate endpoint name 'List' found on
'HTTP: GET /api/bbb' and 'HTTP: GET /api/aaa'.
Endpoint names must be globally unique.
```

Since you cannot see what the other agents have named their endpoints, prefix
every name with your feature: `<Feature><Action>` in PascalCase - `DueTasks`,
`CommentsCreate`, `BulkApply`. Never use a bare verb or noun such as `List`,
`Get` or `Create`. The implemented features already hold `TasksList`,
`TasksCreate`, `TasksGet`, `TasksPatch`, `TasksDelete`, `TasksComplete`,
`TasksReopen`, `ListsList`, `ListsCreate`, `ListsGet`, `ListsRename`,
`ListsDelete`, `ListsTasks` and `Health`.

**Style.** Match the surrounding code: file-scoped namespaces, braces always,
`using` directives outside the namespace and sorted, explicit accessibility
modifiers. `.editorconfig` enforces all of this and `dotnet format` will tell
you.

## 6. Tests

Put tests in `tests/Taskboard.Api.Tests/Features/<Name>/`, in namespace
`Taskboard.Api.Tests.Features.<Name>`. Join the shared collection so you reuse the
one in-memory host rather than booting your own:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Taskboard.Api.Tests.Features.Archive;

[Collection(ApiCollection.Name)]
public sealed class ArchiveTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private HttpClient NewClient()
    {
        _factory.ResetBoard();
        return _factory.CreateClient();
    }

    [Fact]
    public async Task UnknownTaskReturnsProblemDetailsNotFound()
    {
        using var client = NewClient();

        using var response = await client.GetAsync(
            new Uri("/api/archive/nope", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        Assert.Equal("NOT_FOUND", problem.GetProperty("code").GetString());
    }
}
```

Notes:

- **The board is mutable and the host is shared.** Call `ApiFactory.ResetBoard()`
  at the top of every test that depends on the seed, exactly as the helper above
  does. Tests in one collection run one at a time, so a reset is safe - but a
  test that skips it inherits whatever the previous test left behind.
- `using Xunit;` is already a global using - do not add it.
- Pass `TestContext.Current.CancellationToken` to async calls; xunit.v3 requires
  it and an analyzer will fail the build if you omit it.
- Request paths include the `/api` prefix, because the test drives the app from
  the outside.
- Do not edit `ApiFactory.cs`, `GlobalUsings.cs`, `StoreTests.cs`, or
  `SmokeTests.cs`. Do not add another `[CollectionDefinition]`.
- After a reset the seed is fixed, so you may assert against real ids such as
  `"buy-milk"` or `"renew-passport"`, and against the counts locked in
  `StoreTests.cs`. Assert dates as offsets from `SeedData.Today`, never as
  literals.
- Do not assert on routes belonging to another feature - you do not know what
  else exists.

**If your issue is a bug**, the first commit should be a test that fails for the
reason described in the issue. Then fix the code and watch it pass. If an
existing test asserted the wrong behaviour, update it and say so in the PR -
deleting it is not a fix.

## 7. Workflow

Work is organised by **layer** - `infra`, `db`, `api`, `fe` - and each issue's
`area:*` label decides its layer. Every issue for one layer is worked together as a
**batch**: one worktree (`worktrees/<layer>`), one branch, one session, one pull
request. Batches on different layers run in parallel, in their own worktrees.

1. Do not pick your own branch or base. The `cfd-*` skills cut them, and you work on
   the branch already checked out in your layer's worktree. It is named
   `agent/<layer>/<issues>`, the batch's issue numbers ascending and joined by `-`,
   for example `agent/api/1-2-3-4-5`. Its base is the branch of the nearest
   unmerged batch below it in foundation order (`infra`, `db`, `api`, `fe`), or
   the default branch when there is none.
2. Work the batch's issues one after another, in the order you were given: an
   issue that waits for another comes after it, then bugs in existing features
   before new features, then issue number. Finish one issue before starting the
   next. Rule 1 still applies per issue: being in the same batch never lets one
   issue edit another issue's folders.
3. Commit in small steps. **Every commit belongs to exactly one issue** and its
   message references that issue's number, e.g.
   `fix(tasks): sort undated tasks last (#4)`. Never mix two issues' files in one
   commit.
4. Before every commit, after finishing each issue, and before the PR is opened,
   both of these must pass:

   ```sh
   dotnet test Taskboard.slnx
   dotnet format Taskboard.slnx --verify-no-changes
   ```

   Fix your own code until they pass. Do not fix them by editing a shared file,
   suppressing an analyzer, adding a package, or skipping a test. A failure caused
   by an earlier issue in the batch is fixed in that issue's folders.
5. One pull request per batch, opened by `/cfd-publish`. It targets the batch's
   base, so pull requests **stack in foundation order**: a layer's PR targets the
   branch of the layer below it, and the lowest targets the default branch. A
   batch is never published before the batch below it. The description has one
   `Closes #n` line per issue, a section per issue saying what was built or fixed
   and the exact routes added, and - separately and prominently - anything any
   issue was blocked on by rules 2 or 3.
6. **Do not merge.** A human reviews and merges, bottom of the stack first.

## 8. Stop conditions

Stop and report rather than improvising if:

- you need a new NuGet package;
- you need to change a shared file beyond the one-line registration;
- you need a new configuration setting;
- you need to add a field to `TaskItem` or `TaskList`;
- you need to change `Store/SeedData.cs`;
- you need to register a service in the root DI container;
- your work appears to require editing another feature's folder;
- your feature appears to depend on another feature's private data or routes;
- the build, tests, or format check fail for a reason outside your own folders.

Reporting a blocker is a successful outcome. Silently working around one by
editing shared infrastructure is not.
