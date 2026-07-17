---
name: improve-battle-plan
description: Orchestrates a bounded, evidence-driven Battle Plan improvement run across gameplay, balance, levels, UI/UX, art, audio, bots, multiplayer, tests, and release quality. Use only when explicitly invoked to find, implement, review, and verify cross-discipline improvements with one initial approval and one final user review.
disable-model-invocation: true
---

# Improve Battle Plan

Run a high-autonomy improvement cycle while protecting the current worktree and
the single shared Unity Editor. The top-level agent is always the orchestrator
and Unity broker.

Read [SCHEMAS.md](SCHEMAS.md) before dispatching specialists.

## Invocation contract

Invoke explicitly with a short brief, for example:

`/improve-battle-plan Prepare the strongest external-playtest candidate you can without adding a new mode.`

Infer sensible defaults from the brief and repository. The user should only
need to approve one compact charter at the start and review one evidence packet
at the end.

After initial approval:

- Continue autonomously through discovery, selection, implementation, review,
  repair, and verification.
- Send non-blocking progress updates only when they communicate meaningful
  results. Do not ask the user to manage the run.
- Ask again only for a hard stop: destructive or irreversible action, secrets
  or paid services, definitive permission failure, conflict with unrelated
  user changes, or a creative/rule choice that materially changes the approved
  outcome and has no safe deferral.
- When a risky choice is not essential, defer it and continue with safe work.

## Default autonomy budget

Unless the charter says otherwise:

- Target 3–6 coherent improvements spanning at least three disciplines.
- Use at most two implementation waves and two repair iterations.
- Implement high-confidence small/medium work. Defer large content,
  architecture, new modes, new characters, major rule changes, and destructive
  migrations.
- Preserve all unrelated and uncommitted work.
- Do not commit, push, create a PR, publish, spend money, or write to external
  services.
- Do not use a documentation mismatch as authority to change a game rule.
- Proactively search for no-cost third-party assets when they can beat generation
  or bespoke work. Integrate only assets with an explicit commercial-use license;
  prefer CC0/public-domain sources, record source/author/license/attribution, and
  quality-gate one asset before importing a set. "Free to download" is not a
  license.
- Keep Unity Play mode to one pre-scripted session when runtime evidence is
  required, and always leave Unity stopped.

## Phase 0 — Frame the run

1. Read applicable project rules and inspect current git status/diff without
   mutating it.
2. Inspect the smallest repository slice needed to understand the brief.
3. Ask `game-director` for a read-only north-star ruling:
   - intended player outcome;
   - protected pillars;
   - cuts/non-goals;
   - decisions requiring evidence.
4. Give that ruling and the brief to `game-producer`. Request:
   - scope and non-goals;
   - autonomy budget;
   - risks and stop conditions;
   - acceptance evidence;
   - likely disciplines and dependencies.
5. Present one compact charter:

   - **Outcome**
   - **Protected**
   - **Improvement budget**
   - **Non-goals**
   - **Verification**
   - **Hard-stop conditions**

6. Get one explicit approval. If the user adjusts it, update once and proceed.
   This is the only planned mid-work decision gate.

## Phase 1 — Establish a shared fact pack

After approval, create a concise evidence packet for every specialist:

- approved charter and north-star ruling;
- current branch/worktree state and paths already being edited;
- implemented modes, scenes, roster, and relevant build target;
- baseline tests, console state, and known runtime topology;
- authoritative code/assets versus uncertain documentation;
- constraints from Unity coordination and Play-mode rules.

Delegate baseline planning to `qa-playtest-engineer`. The top-level broker
executes Unity checks unless it explicitly grants that role the sole lease.

Baseline order:

1. Confirm editor instance, stopped state, compilation state, and MPPM topology
   before entering Play mode.
2. Check console errors.
3. Run serialized checks and Edit Mode tests.
4. Run only the smallest pre-scripted runtime smoke needed to establish current
   behavior. Use observable completion signals, never fixed sleeps.
5. Record what was not run and why.

If baseline failures are unrelated to the approved run, preserve them as known
conditions. Do not silently expand scope.

## Phase 2 — Parallel read-only discovery

Dispatch the relevant specialists in one parallel batch. For an across-the-board
run, include:

- `gameplay-designer`
- `systems-balance-designer`
- `level-designer`
- `ui-ux-designer`
- `art-director`
- `audio-designer`

Add `bot-ai-engineer`, `multiplayer-engineer`, `build-release-engineer`, or
`technical-art-vfx-animator` only when evidence indicates their domain matters.

Every specialist:

- is read-only during discovery;
- receives the same fact pack and approved charter;
- distinguishes fact, inference, and unknown;
- cites exact paths/symbols/assets or runtime evidence;
- returns the specialist-audit JSON from `SCHEMAS.md`;
- proposes measurable acceptance checks;
- does not use Unity MCP or edit files.

Do not force one implementation per discipline. Breadth belongs in discovery;
implementation is selected by impact, confidence, fit, and risk.

## Phase 3 — Verify and reconcile findings

1. Normalize all candidates to the common schema.
2. Send high-impact technical claims to the appropriate implementation owner
   for read-only verification:
   - gameplay rules/timing → `gameplay-engineer`
   - bot knowledge/decisions → `bot-ai-engineer`
   - ownership/Relay/NGO → `multiplayer-engineer`
   - shaders/VFX/performance → `technical-art-vfx-animator`
   - build/platform risk → `build-release-engineer`
3. Run paired rebuttals where findings interact:
   - gameplay ↔ balance;
   - level ↔ balance;
   - art ↔ UI/UX;
   - audio ↔ gameplay feedback;
   - multiplayer ↔ gameplay authority.
4. Resume the initial `game-director` with the verified dossier. Request a
   decisive creative ruling on conflicts, priorities, and deferrals.
5. Resume `game-producer` with that ruling. Request a dependency-ordered,
   file-owned implementation batch within the approved budget.

No candidate may enter implementation without:

- concrete evidence;
- high or explicitly accepted medium confidence;
- named owner and files;
- expected player value;
- rollback boundary;
- exact acceptance check.

## Phase 4 — Plan verification before writing

Have `qa-playtest-engineer` turn the selected batch into:

- edit-mode or pure-logic assertions;
- serialized asset checks;
- UI/visual screenshot requirements;
- multiplayer topology and role matrix;
- balance measurements and thresholds;
- one consolidated runtime script with observable completion.

Writers must receive their acceptance checks before editing.

## Phase 5 — Implement in owned waves

The producer assigns one owner per file and its `.meta`. Typical owners:

- gameplay, abilities, units, stats → `gameplay-engineer`
- bots → `bot-ai-engineer`
- NGO, Relay, connection flow → `multiplayer-engineer`
- UXML/USS/controllers/player copy → `ui-ux-designer`
- shaders, materials, VFX, animation → `technical-art-vfx-animator`
- map layout and level assets → `level-designer`
- audio system, mix, SFX/music wiring → `audio-designer`
- build/platform settings → `build-release-engineer`

Rules:

- Parallelize only disjoint files.
- Specialists are writer-only; they do not import, compile, test, build, or
  enter Play mode.
- Do not start Unity verification while any Unity-relevant writer is active.
- Keep changes narrow. Do not opportunistically clean adjacent code.
- Each writer returns the writer-handoff JSON from `SCHEMAS.md`.

## Phase 6 — Broker barrier and verification

After all writers finish:

1. Freeze writes under `Assets/`, `Packages/`, and `ProjectSettings/`.
2. Review every diff for scope, ownership, accidental generated files, secrets,
   and unrelated edits.
3. Refresh/import once and await compilation/domain reload.
4. Require zero newly introduced relevant console errors.
5. Run queued serialized checks and Edit Mode tests.
6. Run one consolidated Play session only if needed. Use high dev speed,
   phase/state dumps, E2E reports, screenshots, and a completion signal.
7. Exit Play immediately and leave Unity stopped.

Evidence after a Unity-relevant write is stale until this barrier completes.

## Phase 7 — Evaluate, repair, converge

Use an evaluator-optimizer loop:

1. QA scores acceptance checks as pass/fail with evidence.
2. Relevant designers review player-facing outcomes.
3. Relevant engineers review authority, determinism, lifecycle, and regression
   risk.
4. Compare results against the approved charter, not against newly invented
   scope.
5. If a failure is local and understood, run one focused repair wave, then
   restart the broker barrier.
6. Stop after two repair iterations or when evidence no longer improves.
   Preserve the last verified state and report unresolved items truthfully.

Do not use Bugbot or security-review unless the user explicitly requested that
review surface.

## Phase 8 — Final user review

Return one concise packet:

- **Verdict:** ready for review / partially complete / blocked.
- **Implemented:** player-facing outcomes grouped by discipline.
- **Evidence:** exact test counts, runtime scenarios, screenshots, console
  state, and observed thresholds.
- **Changed files:** grouped by owner.
- **Decisions:** rules or assumptions chosen during the run.
- **Deferred:** valuable work outside budget, with reason.
- **Residual risk:** unverified or failed checks.
- **Worktree state:** no claim of commit/push unless explicitly requested.
- **Final check:** the smallest manual actions the user should inspect.

Then stop. Do not continue polishing, commit, push, or open a PR without a new
request.
