---
title: "Pins: keep a short, ordered list of tasks at the top"
labels: [feature, area:pins]
---

## Context

Whatever the sort order, the few tasks someone actually cares about this week get
lost among the rest. This issue adds pins: a short, user-ordered list of tasks
kept to hand.

Pins are private to this feature: an in-memory store held inside
`src/Taskboard.Api/Features/Pins/`. The feature reads the board, but **never writes
to it**.

Read `AGENTS.md` before starting. The rules there override anything you would
otherwise infer from this issue.

## Scope

Create exactly these folders and put every file you write inside them:

- `src/Taskboard.Api/Features/Pins/`
- `tests/Taskboard.Api.Tests/Features/Pins/`

You own exactly these routes and no others:

```
GET    /pins
PUT    /pins/{taskId}
DELETE /pins/{taskId}
DELETE /pins
PUT    /pins
```

Do not map a catch-all.

There is one pin list for the whole board, capped at 10, and
`ApiFactory.ResetBoard()` does **not** clear it. Start every test with
`DELETE /api/pins`. Tests in the shared collection run one at a time, so that is
safe.

The pin list is read and rewritten concurrently. Adding a pin, the 10-pin cap check
and a reorder must be atomic with respect to each other.

Get timestamps from the injected `TimeProvider`.

The **only** permitted edit to a shared file is the single registration line in
`src/Taskboard.Api/Program.cs`:

```csharp
    Features.Pins.PinsFeature.Register(app);
```

placed in alphabetical position inside the `FEATURE REGISTRATION` block. No other
file outside your two folders may change - in particular not `Domain/**`,
`Store/**`, `Directory.Packages.props`, `Contracts/SharedContracts.cs`,
`Configuration/AppConfig.cs`, `Routing/**`, `Errors/**`, `.editorconfig`,
`ApiFactory.cs`, `StoreTests.cs`, `SmokeTests.cs`, or any other feature's folder.
If you believe you need such a change, stop and say so in the PR.

Do not add NuGet packages.

## Interface

`src/Taskboard.Api/Features/Pins/PinsFeature.cs` exposes exactly one public member:

```csharp
namespace Taskboard.Api.Features.Pins;

internal static class PinsFeature
{
    public static void Register(IEndpointRouteBuilder routes);
}
```

### Pruning

A pin whose task is no longer on the board is **pruned** - removed from the pin
list - at the start of every request to this feature, before anything else happens.
A pruned pin does not count towards the cap, and positions close up.

### Behaviour

- `GET /pins` responds `200` with the pins in order:

  ```json
  [ { "position": 1, "pinnedAt": "...", "task": { "id": "renew-passport", "...": "..." } } ]
  ```

  `position` is 1-based and contiguous. `task` is the live `TaskItem`.
- `PUT /pins/{taskId}` appends the task to the end and responds `201` with its
  entry. Pinning an already-pinned task is a `200` that changes nothing - not its
  position and not its `pinnedAt`. An 11th pin is a `409` with `code` =
  `CONFLICT`. An unknown task is a `404`.
- `DELETE /pins/{taskId}` responds `204`, or `404` when the task is not pinned.
- `DELETE /pins` removes every pin and responds `204`, even when there were none.
- `PUT /pins` with `{ "taskIds": ["c", "a", "b"] }` reorders the pins and responds
  `200` with the new list. The array must be exactly the current pinned ids in some
  order - no missing id, no extra id, no duplicate - otherwise `409` with
  `details.pinned` listing the current ids, and nothing changes.

## Acceptance criteria

- Pinning `renew-passport`, `buy-milk` and `draft-q3-report` returns `201` each,
  and `GET /api/pins` returns them in that order with positions 1, 2, 3.
- Pinning `buy-milk` again returns `200` and leaves the order and its `pinnedAt`
  unchanged.
- The 11th distinct pin returns `409`.
- `DELETE /api/pins/buy-milk` returns `204` and positions close up to 1, 2;
  repeating it returns `404`.
- `PUT /api/pins` with the three ids reversed returns them reversed; a missing id,
  an extra id, or a duplicate returns `409` and the order is unchanged.
- After `DELETE /api/tasks/renew-passport`, `GET /api/pins` no longer lists it, and
  a new pin can be added even if the list had been full.
- Pinned tasks reflect live data: `PATCH`ing a pinned task's title through
  `/api/tasks/{id}` shows the new title in `GET /api/pins`.
- `PUT /api/pins/no-such-task` returns `404`.
- `DELETE /api/pins` returns `204` and empties the list.
- Tests live in `tests/Taskboard.Api.Tests/Features/Pins/`, carry
  `[Collection(ApiCollection.Name)]`, call `ApiFactory.ResetBoard()` and
  `DELETE /api/pins` at the start of each test, and cover each bullet above.
- `dotnet test Taskboard.slnx` and
  `dotnet format Taskboard.slnx --verify-no-changes` both pass.
- The diff touches only `src/Taskboard.Api/Features/Pins/`,
  `tests/Taskboard.Api.Tests/Features/Pins/`, and the one registration line in
  `Program.cs`.
