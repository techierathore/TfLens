# TfLens — project session memory (all harnesses)

<!-- This file is COMMITTED (unlike CLAUDE.md, which is gitignored and imports it).
     OpenCode and Codex auto-load AGENTS.md; Claude Code loads it through CLAUDE.md's
     @AGENTS.md import. Keep everything in here harness-neutral — Claude-only
     material (permissions, tool preference) lives in app-claude-md-tmpl.md. -->

## Required reading before any code change
ALWAYS read and follow:
- **docs/TfLens-Coding-Standards.md** — strict compliance for every line of code you write or modify.
- **docs/TfLens-Architecture.md** — respect module boundaries.
- **PROJECT-STATUS.md** — for current phase & next-step context.

## Hard rules (non-negotiable — the harness enforces #1)

1. **Git is manual — agents NEVER run `git` or `gh`.** Not to commit, and not to read (`status`/`log`/`diff`/`grep`/`blame`). All harnesses enforce this mechanically: Claude Code via `.claude/settings.json` and `block-git.sh`; OpenCode via `opencode.jsonc` plus its plugin bridge; Codex via `.codex/rules/techieflow.rules` plus `.codex/hooks.json`. A blocked git call is the policy working, not an obstacle to route around. Evidence for status updates / "what changed" = the checklist Requirements Status table + the working-tree files (+ mtimes) + a fresh `dotnet build` (`.tfcore/tasks/_status-update-gate.md`). The OWNER commits, in a separate terminal.
2. **Run the app yourself — the test harness is fully set up.** Headless Playwright + Chromium live in WSL; the Windows/MAUI dotnet bridge is rung #4 of the build ladder; MAUI Android/iOS/Mac Catalyst are driven over the Appium bridge (`core-config.yaml → runtimeVerification.appium`). NEVER ask the owner to boot the app, run a build, or execute a command — "can't run on Linux/WSL", "it targets Windows", "it's MAUI", "Playwright needs a GUI", "the dependent service is down" are BANNED excuses (`.tfcore/tasks/_smoke-test-policy.md`). Asking the owner is the LAST resort, only after the build ladder + `verify-phase §3a` escalation genuinely fail — and even then you still run the test yourself once they reply.
3. **Native-head automation binds to the app's own window.** Drive a MAUI head only through a session attached to the app under test (Windows: launched PID → its top-level window handle; Android/iOS/Catalyst: the app's package/bundle id), interact element-by-element via `AutomationId`, and NEVER inject global keyboard/mouse input — it lands in whatever window happens to have focus, not the app (`verify-phase.md §3b`).

## Project basics
- Stack: .NET 10 (LTS), Blazor Server (Interactive Server), TrBlazeUI, SQLite via Dapper + Microsoft.Data.Sqlite, Serilog. No TechieRag (no AI features).
- Field-prefix convention: `obj` prefix on instance fields (e.g. `private readonly ILogger<X> objLogger;`) — PER-PROJECT day-1 decision (2026-08-26); recorded in Coding Standards §"Fields, Parameters, Locals", which is authoritative.
- Test naming: short PascalCase, NO underscores. Full scenario in XML `<summary>` doc.
- TfLens-specific invariants (BRD-30..36, BRD-89): figures are computed at request time and never written back into a stream table; live and backfilled never pool; first-pass / gate distribution / escape rate never pool across `project_type`; `n < 3` renders `insufficient data (n=…)`; `cost_usd` never pools across harness; there is NO flag, query parameter or toggle that relaxes any of this. `tf-metrics.sh` is the parity oracle and is never edited to match the app.
- TfLens never writes to any GitHub repository (GET only, contents-read PAT). Secrets come only from PascalCase env vars (`TfLensGitHubToken`, `TfLensAuthUser`, `TfLensAuthPasswordHash`).

## Requirement ID prefixes used in this repo
- `REQ-UI-*` — UI work, routed to /trblazeui
- `REQ-FN-*` — backend, routed to /flow-master
- `REQ-RAG-*` — AI/RAG, routed to /techierag (none expected in TfLens)
- `REQ-NFR-*` — non-functional

Always tag work with its REQ ID in the checklist's Requirements Status **Remarks** cell (e.g. `[REQ-UI-007] settings form validation added`). Agents never git-commit (Hard rule 1); the owner's own manual commits use the same `[REQ-*]` tags.

## Verification
After every implementation phase, the verifier runs and writes per-REQ verdicts into the
owning checklist's Requirements Status table (the single source of truth — no dated
docs/qa files). PROJECT-STATUS.md is updated after EVERY phase (mandatory gate).
Library issues go in the owning library's feedback file — docs/TfLens-TrBlazeUI-Feedback.md
or docs/TfLens-TechieRag-Feedback.md (one file per library; each goes to its own team) —
never silently worked around.

## Slash-command syntax (READ ME if a `/agent *command` invocation fails)

**Codex:** use repository skills such as `$techieflow-build`, `$techieflow-verify`, and `$techieflow-refresh-status`, or ask for the skill by name in plain language. Skills load the canonical `.tfcore/tasks/*.md` workflow. When a task calls for fan-out, delegate to the registered `tf_builder`, `tf_test_writer`, `tf_explorer`, `trblazeui`, or `techierag` Codex role and wait for its result. Defining a role alone does not authorize delegation; the user request or applicable skill must call for it. Project config and hooks require repository trust; review changed hooks with `/hooks`.

**Claude Code** registers TechieFlow-native agents under the path-derived namespace `TechieFlow:agents:<name>`. The short `/<agent>` form does NOT always resolve. When in doubt use the full form:

| Agent | Claude Code | OpenCode |
|-------|-------------|----------|
| analyst | `/TechieFlow:agents:analyst` (or `/analyst` if it resolves) | `/flow-analyst` |
| architect | `/TechieFlow:agents:architect` | `/flow-architect` |
| flow-master | `/TechieFlow:agents:flow-master` | `/flow-master` |
| verifier | `/TechieFlow:agents:verifier` | `/flow-verifier` |
| trblazeui | `/trblazeui` (NuGet-deployed to `.claude/commands/trblazeui.md`) | `/trblazeui` |
| techierag | `/techierag` (NuGet-deployed to `.claude/commands/techierag.md`) | `/techierag` |

If `/trblazeui` or `/techierag` is missing: run `dotnet build` (the NuGet target writes `.claude/commands/<lib>.md` directly), then restart the harness. Older app repos may instead hold the legacy `.claude/<lib>.md`, which `update-framework.sh` shims forward — but nothing creates that legacy path in a current app, so never treat its absence as "the library is not deployed". Resolve a persona as `.claude/commands/<lib>.md` → `.claude/<lib>.md` → `.<lib>/<Lib>-AI-Reference.md`.

Under Claude Code, if `/flow-master *render-workflow-docs <App>` returns "Unknown command", use `/TechieFlow:agents:flow-master *render-workflow-docs <App>` instead. (Under OpenCode `/flow-master` is the registered agent name — there is no fuller form to fall back to.)

After the agent is loaded, every TechieFlow-native agent (analyst, architect, flow-master, verifier) accepts `*command args` style invocations. trblazeui and techierag are free-form personas — normally `flow-master *build-phase <App>` calls them as sub-agents, but you can also drive them directly with prompts like `Implement REQ-UI-* from docs/TfLens-Checklist.md to match the mockups in docs/mockups/.`
