---
title: "Bug: DELETE /lists/{listId} orphans the list's tasks instead of reassigning them"
labels: [bug, area:lists]
---

## Context

`DELETE /lists/{listId}` is documented - in `README.md` and in the endpoint's own
OpenAPI description - as never deleting tasks: every task on the list moves to
`?reassignTo=`, which defaults to the inbox. The endpoint validates `reassignTo`
carefully and then never uses it. The list row is removed and its tasks are left
pointing at an id that no longer exists.

`TaskItem.ListId` is documented as "always a real list; never null". After one
delete that invariant is broken for every task that was on the deleted list, and
nothing in the API can put them right: `PATCH /tasks/{id}` with the old list id
is now a `400`, and there is no route that lists them.

## Reproduce

```sh
dotnet run --project src/Taskboard.Api

curl -s 'http://localhost:5090/api/lists' | jq '[.items[].taskCount] | add'
# 20

curl -s -X DELETE 'http://localhost:5090/api/lists/errands' -o /dev/null -w '%{http_code}\n'
# 204

curl -s 'http://localhost:5090/api/lists' | jq '[.items[].taskCount] | add'
# 16   <- the four errands tasks have vanished from every count

curl -s 'http://localhost:5090/api/tasks?listId=errands' | jq '.total'
# 4    <- but they still exist, still pointing at a list that is gone

curl -s 'http://localhost:5090/api/lists/errands/tasks' -o /dev/null -w '%{http_code}\n'
# 404  <- and there is no longer any way to reach them by list
```

`?reassignTo=home` behaves identically: the destination is checked and then
discarded.

## Why the existing tests miss it

`ListsTests.DeleteRemovesTheList` creates an empty list, deletes it and checks
that it is gone. No seeded task is ever on that list, so the reassignment path is
never exercised. `ListsTests.DeleteRejectsTheInboxAndUnknownDestinations` covers
the validation that does work and stops there.

## Scope

You own the Lists feature:

- `src/Taskboard.Api/Features/Lists/`
- `tests/Taskboard.Api.Tests/Features/Lists/`

You may change existing files in those two folders, including existing tests. The
defect is in the delete handler in
`src/Taskboard.Api/Features/Lists/ListsFeature.cs`.

Nothing outside those two folders may change. In particular:

- **Do not** change `Program.cs` - the feature is already registered, and this
  issue adds no registration line.
- **Do not** change `Store/**`. `ITaskStore` already has everything you need:
  read `Tasks`, build the updated record with `existing with { ListId = ... }`,
  and call `TryReplace`. Do not add a bulk-reassign method to the interface.
- **Do not** change `Store/SeedData.cs`, `Domain/**`, `Routing/**`, `Errors/**`,
  `Contracts/SharedContracts.cs`, `Configuration/AppConfig.cs`,
  `Directory.Packages.props`, `.editorconfig`, `ApiFactory.cs`, `StoreTests.cs`,
  `SmokeTests.cs`, or `Features/Tasks/`.

Do not add NuGet packages. Do not change the documented contract to match the
code, and do not "fix" this by deleting the tasks - the documented behaviour is
the correct one.

Read `AGENTS.md` before starting.

## Expected behaviour

`DELETE /lists/{listId}?reassignTo=<listId>`:

1. `404` when `listId` is unknown. Nothing is written.
2. `409` when `listId` is the inbox. Nothing is written.
3. `400` when `reassignTo` is unknown, or is the list being deleted. Nothing is
   written.
4. Otherwise: every task whose `listId` is the list being deleted moves to
   `reassignTo` - defaulting to `TaskList.InboxId` when the parameter is absent
   or blank - and only then is the list removed. The response is `204`.

A task that moves gets its `updatedAt` refreshed. Nothing else about it changes:
not its status, priority, due date, or `completedAt`. Tasks on other lists are
untouched.

The existing validation order is already correct - keep it. The failure cases
above must still write nothing at all, so a `400` for an unknown `reassignTo`
must not leave half the tasks moved.

Timestamps come from the injected `TimeProvider`. Note that the handler does not
currently take one; adding a parameter to it is inside your folder and therefore
allowed.

## Acceptance criteria

- The first commit on the branch adds a test that fails against the current code
  for the reason above. The fix follows it.
- Deleting `errands` returns `204`, and the four tasks that were on it -
  `buy-milk`, `cancel-gym-membership`, `mail-birthday-card` and
  `order-printer-ink` - all report `listId` = `"inbox"` afterwards.
- After that delete, `GET /api/tasks?listId=errands` returns `total` = 0, and the
  `taskCount` values from `GET /api/lists` sum to 20 - every task is still
  accounted for on some list.
- `GET /api/lists/inbox/tasks` then returns `total` = 7: its original three plus
  the four that moved.
- `?reassignTo=home` sends them to `home` instead, asserted the same way.
- A moved task keeps its `status`, `priority`, `dueOn` and `completedAt`. A test
  asserts this on `mail-birthday-card`, which is `done` with a `completedAt`, and
  on `cancel-gym-membership`, which is `cancelled`.
- A moved task's `updatedAt` is refreshed; a task on another list has its
  `updatedAt` unchanged.
- Deleting a list with no tasks on it still returns `204` and moves nothing.
- The failure cases still write nothing: after a `400` from
  `?reassignTo=no-such-list`, `GET /api/lists/work/tasks` still returns `total`
  = 6 and the list still exists. The same holds for `DELETE /api/lists/inbox`
  (`409`) and `DELETE /api/lists/no-such-list` (`404`).
- `?reassignTo=work` on `DELETE /api/lists/work` is still a `400`.
- Every existing test in `tests/Taskboard.Api.Tests/Features/Lists/` still
  passes. `StoreTests.EveryListHasAtLeastOneTask` and the rest of the shared
  suite still pass. No existing test is deleted or weakened.
- Tasks are read and written only through `ITaskStore`, lists only through
  `ITaskListStore`, and timestamps only through the injected `TimeProvider`.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Lists/` and
  `tests/Taskboard.Api.Tests/Features/Lists/`.
