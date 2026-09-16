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
drive it, and the unit of work is **one layer batch**: every selected issue that
belongs to a layer, worked together in that layer's worktree, on one branch, by
one Claude session, and published as one PR.

```text
/cfd-setup 1,2,3,4,5
  api issues #1-#5  ->  branch agent/api/1-2-3-4-5  ->  worktrees/api  ->  one PR to main
  fe issues (if any) ->  branch agent/fe/<issues>    ->  worktrees/fe   ->  one PR stacked on the api PR
```

Worktrees are **one per layer**: `worktrees/fe`, `worktrees/api`, `worktrees/db`,
and `worktrees/infra` if something ever needs it. `/cfd-setup` sorts the selected
issues on to layers by their `area:*` label, creates a worktree only for the layers
that got issues, and cuts one branch per layer. Layers run **in parallel**, one
terminal and one session each; inside a layer, the session works through all of its
issues, one commit per issue.

PRs **stack strictly in foundation order** — `infra`, `db`, `api`, `fe`. Each
layer's PR targets the branch of the layer below it, the lowest one targets `main`,
an upper layer is never published before the one below it, and the stack merges
bottom up. AGENTS.md rule 7 spells out the same workflow for the agent doing the
work; what a commit may *touch* is still exactly AGENTS.md rule 1.

Today this repo is API-only: no frontend, no database, no deploy code. Every
`area:*` label maps to `api`, so `worktrees/api` is the only worktree that gets
created, and all its issues are worked in that one session at the same time. The
mapping lives under `layers` in `.rpi/contract-first.yaml`; add areas and paths
there when a layer grows real code.

| Command | What it does |
|---|---|
| `/cfd-setup [issues]` | Plans the issues, or every available open issue. Run it again to sort them on to layers, create each layer's worktree, and cut one branch per layer. |
| `/cfd-work` | Runs inside a layer worktree, on every issue in its batch: research, then plan, then implement, one stage per run. |
| `/cfd-publish [issues]` | Reviews finished batches, pushes them bottom up, and opens one stacked PR per layer that closes all of its issues. |
| `/cfd-check [issues]` | Merges finished batches into a throwaway preview, runs the tests, starts the API, and probes the routes. |
| `/cfd-sync [issues] [--apply]` | Shows where every batch stands, and carries `main` up the stack after a merge. |

With no issue selector, each command acts on everything it applies to.

CFD state lives in `.rpi/` in the main checkout. It is gitignored and never
committed, because AGENTS.md lets a branch touch only its own folders.

## Things that bite in parallel

- Every new feature adds one line to the FEATURE REGISTRATION block in `Program.cs`.
  Inside a batch the session keeps it sorted; across PRs, the second to merge
  will conflict there. The fix is mechanical:
  keep every line, sorted alphabetically.
- Endpoint names are global. An unprefixed name like `List` throws at startup and
  fails every feature's tests, not just yours.
- The test host and the board are shared. Call `ApiFactory.ResetBoard()` at the
  top of every test that relies on the seed.
- Two issues that own the same feature folder (two `area:tasks` bugs, say) can share
  a batch, but `/cfd-setup` records `waitsFor` so the second is worked after the first.
- Issues in one batch share a tree. A bug fix can change behaviour a later feature's
  test reads, so bugs in existing features are worked first, and the full checks run
  after each issue.
