---
title: "Export: download the board as CSV or NDJSON"
labels: [feature, area:export]
---

## Context

The only way to get data out of the board is to page through `GET /tasks` as JSON.
Nobody can open that in a spreadsheet. This issue adds a streaming-friendly export
of tasks and lists in CSV and newline-delimited JSON.

The feature is pure projection. It injects `ITaskStore` and `ITaskListStore`, holds
no state, and knows about no other feature.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Export/`
- `tests/Taskboard.Api.Tests/Features/Export/`

You own exactly two routes: `GET /export/tasks` and `GET /export/lists`. Do not map
anything else, and do not map a catch-all.

These endpoints return raw text, not a JSON object, so they return an `IResult`
(`Results.Text(...)` or `Results.Stream(...)` are fine). Errors are still thrown as
`AppException` and still come back as `application/problem+json`.

Write CSV yourself against the base class library - there is no CSV package and you
may not add one. For NDJSON, serialise each row with the application's own JSON
options so enums stay camelCase: inject
`IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>` and use its
`SerializerOptions`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Export.ExportFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Export/ExportFeature.cs` exposes exactly one public
member:

```csharp
namespace Taskboard.Api.Features.Export;

internal static class ExportFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### `GET /export/tasks?format=<csv|ndjson>&listId=<string>&status=<status>`

- `format` is optional, defaults to `csv`, and is case-insensitive. Anything else
  is a `400`.
- `listId` is optional and must be a known list (`404` otherwise).
- `status` is optional, a camelCase `TaskState` name (`400` otherwise).
- Rows are ordered by `id` ordinal. No paging - the whole matching set is
  exported.

**CSV** (`Content-Type: text/csv; charset=utf-8`,
`Content-Disposition: attachment; filename="tasks.csv"`):

```
id,title,notes,listId,status,priority,dueOn,createdAt,updatedAt,completedAt
archive-old-invoices,Archive old invoices,,work,todo,low,,2026-08-12T00:00:00.0000000+00:00,2026-08-12T00:00:00.0000000+00:00,
```

- Exactly that header, in that column order.
- Every record, header included, ends with `\r\n` (RFC 4180).
- Enums as camelCase names. `dueOn` as `yyyy-MM-dd`. Timestamps with the
  round-trip `"O"` format and invariant culture. `null` is an empty field.
- A field containing a comma, a double quote, `\r` or `\n` is wrapped in double
  quotes, with each embedded double quote doubled. No other field is quoted.
- A field that begins with `=`, `+`, `-` or `@` is prefixed with a single `'`
  before quoting is decided, so a spreadsheet never evaluates it as a formula.

**NDJSON** (`Content-Type: application/x-ndjson; charset=utf-8`,
`Content-Disposition: attachment; filename="tasks.ndjson"`): one `TaskItem`
serialised as compact JSON per line, each line ending with `\n`, and no enclosing
array.

### `GET /export/lists?format=<csv|ndjson>`

The same rules over lists, with columns `id,name,createdAt,taskCount` and filename
`lists.csv` / `lists.ndjson`. `taskCount` is the number of tasks currently on the
list.

## Acceptance criteria

- `GET /api/export/tasks` returns `200`, `text/csv`, the attachment header, the
  exact header line, and 20 data rows - 21 `\r\n`-terminated records.
- The first data row is `archive-old-invoices` and matches the seeded task field
  for field, with empty `notes`, `dueOn` and `completedAt`.
- `?status=done` returns 3 data rows; `?listId=inbox` returns 3; `?listId=nope`
  returns `404`; `?status=later` and `?format=xml` return `400` as
  `application/problem+json`.
- A task created through `POST /api/tasks` with title `Say "hi", then leave` is
  exported with its title as `"Say ""hi"", then leave"`.
- A task titled `=SUM(A1)` is exported as `'=SUM(A1)`, and one titled `-1 point`
  as `'-1 point`.
- A task whose notes contain a newline is exported as one quoted field, and the
  row still parses back to the right number of columns with a small RFC 4180
  reader written in the test.
- `?format=NDJSON` returns 20 lines, each of which deserialises to a JSON object
  whose `status` is a camelCase string, not a number.
- `GET /api/export/lists` returns 4 data rows, and `taskCount` for `home` is 7.
- Tests live in `tests/Taskboard.Api.Tests/Features/Export/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` before each
  test that depends on the seed, and cover each bullet above.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Export/`,
  `tests/Taskboard.Api.Tests/Features/Export/`, and the one registration line in
  `Program.cs`.
