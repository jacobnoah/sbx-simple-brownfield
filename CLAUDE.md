# CLAUDE.md

@AGENTS.md

`AGENTS.md` above is the rulebook for every code change in this repo: what a
branch may touch, the feature shape, conventions, tests, and stop conditions.
Nothing below overrides it. This file only adds what a Claude Code session needs
on top: commands, the GitHub backlog, and the multi-agent workflow.

## Commands

Requires .NET SDK 10.0.401 or newer (`global.json`). Run from the root of the
checkout or worktree you are working in.

```sh
dotnet restore Taskboard.slnx --locked-mode      # reproducible restore
dotnet test Taskboard.slnx                       # build (warnings are errors) + tests
dotnet format Taskboard.slnx --verify-no-changes # style gate; drop the flag to fix
dotnet run --project src/Taskboard.Api           # http://localhost:5090, docs at /scalar
```

One test class, for a quick check between tasks:

```sh
dotnet test --project tests/Taskboard.Api.Tests --filter-class "Taskboard.Api.Tests.Features.Tasks.TasksTests"
```

## Backlog: GitHub issues

The backlog is GitHub issues on `jacobnoah/sbx-simple-brownfield`, and review is
GitHub pull requests. There is no Azure DevOps board. Use the `gh` CLI.

```sh
gh issue list --state open --limit 100 --json number,title,labels,assignees
gh issue view <n> --json number,title,body,labels,state,assignees,comments,closedByPullRequestsReferences
gh pr list --state all --limit 100 --json number,headRefName,baseRefName,state,mergeable,reviewDecision
gh pr create --base main --head <branch> --title "<title> (#<n>)" --body-file <file>
```

Labels carry the routing:

- `feature` or `bug` is the kind of work (AGENTS.md rule 1).
- `area:<name>` is the owning area, matching the route ownership table in AGENTS.md.

Issue bodies have Context, Scope, and the exact routes and folders. Where an issue
and AGENTS.md disagree, AGENTS.md wins. `.sandbox/issues/` holds the seed text the
issues were created from; GitHub is authoritative.

Never merge a PR or close an issue by hand. A human reviews and merges.

## Multi-agent workflow (Contract-First Development)

This repo is used to practise running several agents at once. The `cfd-*` skills
drive it, and the unit of work is **one issue**:

```text
issue #4  ->  branch agent/tasks/4-due-sort-nulls  ->  worktree worktrees/api  ->  PR to main
```

Units never stack and never see each other. Each branches from `main` and targets
`main`, exactly as AGENTS.md rule 7 describes.

Worktrees are **one per layer**, not one per issue: `worktrees/fe`,
`worktrees/api`, `worktrees/db`, and `worktrees/infra` if something ever needs it.
A layer worktree is created the first time an issue needs it, and holds one
issue's branch at a time. So layers run in parallel, and the issues inside a layer
run one after another — the first is `active`, the rest are `queued`, and
`/cfd-publish` frees the worktree for the next one.

Today this repo is API-only: no frontend, no database, no deploy code. Every
`area:*` label maps to `api`, so `worktrees/api` is the only worktree that gets
created and the five open issues run one at a time. The mapping lives under
`layers` in `.rpi/contract-first.yaml`; add areas and paths there when a layer
grows real code.

| Command | What it does |
|---|---|
| `/cfd-setup [issue]` | Plans one issue, or every available open issue. Run it again to create the layer worktrees and cut each branch. |
| `/cfd-work [issue]` | Runs inside a layer worktree: research, then plan, then implement, one stage per run. |
| `/cfd-publish [issue]` | Reviews finished units, pushes them, opens a PR per issue, and moves each layer worktree on to its next queued issue. |
| `/cfd-check [issue]` | Merges finished units into a throwaway preview, runs the tests, starts the API, and probes the routes. |
| `/cfd-sync [issue] [--apply]` | Shows where every unit stands, and merges `main` back into open PRs after a merge. |

With no issue number, each command acts on every issue it applies to.

CFD state lives in `.rpi/` in the main checkout. It is gitignored and never
committed, because AGENTS.md lets a branch touch only its own folders.

## Things that bite in parallel

- Every new feature adds one line to the FEATURE REGISTRATION block in `Program.cs`.
  The second and later PRs to merge will conflict there. The fix is mechanical:
  keep every line, sorted alphabetically.
- Endpoint names are global. An unprefixed name like `List` throws at startup and
  fails every feature's tests, not just yours.
- The test host and the board are shared. Call `ApiFactory.ResetBoard()` at the
  top of every test that relies on the seed.
- Two issues that own the same feature folder (two `area:tasks` bugs, say) are not
  safe to run in parallel. `/cfd-setup` flags them, and the second one waits.
- Two issues on the same layer share one worktree, so they never run at the same
  time whatever folders they own. Only issues on different layers run side by side.
