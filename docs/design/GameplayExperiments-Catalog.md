# Battle Plan Gameplay Experiment Dossier

> **Status:** Discovery catalog, July 2026.  
> **Scope:** Dossier only. This document does not change gameplay, rank a first wave, or claim that
> any unrun experiment has succeeded.  
> **Authority:** Current code and serialized assets win when older design prose disagrees.

## Executive ruling

Battle Plan should remain a game of decisive, legible, simultaneous positional reads. A bad read
should create a losing exchange, but ordinary frontal fire should not erase a unit at first contact.
The winner should be determined by the better sequence of decisions rather than the first collision.

The working hypothesis combines structure with a bounded lethality budget:

- center ordinary standard-target kills on **3 ± 1 magazines after explicit provisional miss
  allowances**, with faster specialist flanks and telegraphed one-use abilities;
- protect hidden simultaneous planning, fog, telegraphed counterplay, asymmetric range bands, and
server authority;
- lengthen the decision arc through pacing, objectives, ability economy, positioning, and clearer
feedback;
- test competing directions rather than choosing them by intuition;
- treat bot results as screening evidence and human play as the authority on strategic depth.

This catalog intentionally includes contradictory experiments. “Viable” means worth testing, not
approved for production. Deferred ideas remain visible with their blockers. Falsification arms are
included to challenge a pillar, not as disguised recommendations.

## Dossier at a glance

The registry contains **59 experiment families**:

- 6 evidence, determinism, authority, and topology foundations;
- 12 pacing, planning, team-size, match-structure, and objective experiments;
- 5 ability-economy and counterplay experiments;
- 7 isolated unit/ability breakpoint banks;
- 8 spawn, wall, cover, hill, and sightline experiments;
- 7 bot-policy experiments;
- 9 UI, tactical-readability, art, and VFX experiments;
- 5 audio-foundation and feedback experiments.

The explicit opposing directions are: more cover versus open rotations; starting charges versus
late recharge; role-shaped lethality versus blanket scalar softening; 2v2 versus 4v4; consecutive
versus cumulative KOTH; Pogo stat reductions versus more visible counterplay; Sniper four/three/two
shot breakpoints; and rich versus restrained information.

## 1. Current ground truth

### 1.1 Protected baseline

The baseline for every experiment in this dossier is:

- a **15 × 10** logical and visual board (`GridSystem.ColumnCount = 15`,
`GridSystem.RowCount = 10`);
- **18 symmetric wall cells**; movement blocks each full cell, while a convex octagonal physics
collider trims 0.4 world units from each corner for bullet-sized diagonal peeks;
- a centered **six-cell hill**, columns 6–8 and rows 4–5;
- production spawns distributed evenly across rows 0 and 9, with a two-column edge inset when
space permits;
- exactly **five distinct units per team**, controlled by `RosterRules.UnitsPerPlayer`;
- a five-unit eligible roster: Soldier, Shotgunner, Pogo Rider, Sniper, and Commander;
- fog of war enabled unless an experiment explicitly makes fog the single variable;
- the current Commander **Smoke Screen**: one use, target range 3, a real 3 × 3 footprint, and
one-round sight/fire denial without changing movement or wall data. A segment is blocked only
when it crosses a cloud cell interior; shooter and target endpoint cells are deliberately exempt.
- the current combat prototype: **80 glass / 120 standard / 160 tank HP**, with ordinary standard
target outcomes centered on **3 ± 1 provisional miss-adjusted magazine equivalents**.

The current uncommitted map expansion, chamfered wall collider, Smoke implementation, and
miss-allowance combat profile are protected work. Experiments use this working-tree snapshot as the
control; they do not silently replace it.

Primary sources:

- `Assets/Scripts/GameManager/GridSystem.cs`
- `Assets/Scripts/GameManager/GameLoop.cs`
- `Assets/Scripts/GameManager/RosterRules.cs`
- `Assets/Scripts/Abilities/Smoke.cs`
- `Assets/UnitStats/*.asset`

### 1.2 Implemented match rules

- The planning deadline budget is `5 seconds × the largest living team`: nominally **25 seconds at
5v5**, dropping by five seconds per loss to **5 seconds at 1v1**. Both valid submissions may end
planning early;
the server currently accepts through `endTime + 1`, so these are client deadlines rather than
strict phase maxima.
- An ability replaces that unit’s normal movement order for the round.
- Every current unit starts with **one ability use**. Uses do not recharge.
- Eliminated units remain dead in both supported modes. The reusable respawn lifecycle is preserved
but no current mode opts into it.
- Elimination has no round cap. A team wipe ends the match.
- KOTH requires **three consecutive sole-control rounds**.
- A full-team wipe remains terminal in KOTH; simultaneous wipes draw.
- Grenade and Area Lock can create a dodge phase. A submitted dodge replaces the unit’s original
order and cancels that unit’s own planned ability.
- Pogo, Shield Rush, and Smoke currently have no dodge response range.

### 1.3 Live unit and ability values

These are the serialized values that experiments must use as the control:

**Soldier**

- 120 HP; move 3; vision 5.
- Target range 4; bullet range 8.
- 10 damage × 5-round magazine = 50 theoretical clean-magazine damage.
- Grenade: target range 5, radius 1.6, 80 damage, response range 3, dive range 2. A straight
two-cell dive clears the blast from its center for every current unit collider; one step or a
turned two-step path does not.
- One ability use.

**Shotgunner**

- 160 HP; move 3; vision 3.
- Target range 2; bullet range 3.
- 8 damage × 10-round burst = 80 theoretical clean-magazine damage, with 25° spread.
- Shield Rush: fixed distance 3; no dodge response.
- One ability use.

**Pogo Rider**

- 120 HP; move 5; vision 7.
- Target and bullet range 7.
- 12 damage × 3-round magazine = 36 frontal or 72 theoretical backstab damage.
- Backstab multiplier 2; jump range 5; no dodge response.
- One ability use.

**Sniper**

- 80 HP; move 3; vision 7.
- Target range 5; bullet range 14.
- 50 damage × 1-round magazine; two-second target lock; 2.5-second reload.
- Area Lock target-anchor range 99, line response, and flat damage **130**, independent of the rifle.
- One ability use.

**Commander**

- 120 HP; move 3; vision 6.
- Target range 5; bullet range 7.
- 8 damage × 5-round magazine = 40 theoretical clean-magazine damage.
- Smoke Screen: target range 3; nine-cell footprint; no dodge response.
- One ability use.

The deterministic base metric is
`ceil(target HP / effective landed damage per hit) / magazine size`. A partial lethal magazine is
fractional and overkill does not transfer. The provisional design hit-conversion allowances are
Commander/Soldier 80%, Pogo 5/6, Shotgunner 60% at intended close range, and Sniper 90%. These are
not measured accuracy. Against a 120 HP standard target, allowance-adjusted outcomes are Commander
3.75 magazines, Pogo 4 frontal / 2 rear, Shotgunner 2.5 point-blank bursts, Sniper 3.33, and
Soldier 3: 3.1 on average. Spread, movement, walls, smoke, target selection, and physics determine
actual conversion and must replace these allowances after instrumented playtests.

### 1.4 Grounded diagnosis

The user-visible symptoms are short matches, insufficient strategic depth, and uneven ability
power/counts. Current implementation supports several plausible causes:

1. **Small action budget magnifies every death.** In 5v5, the first death removes one fifth of a
  team’s future movement, fire, vision, and ability options. The miss-allowance profile is intended
  to make that loss follow sustained exposure rather than routine first contact.
2. **Late planning is more compressed, not less.** The client deadline budget falls from 15 to 5
  seconds as pieces disappear, even though each late decision becomes more consequential.
3. **Ability texture expires early.** Every kit has one match-long use, so later rounds can collapse
  into movement plus auto-fire.
4. **KOTH has opposite pacing failure modes.** Three uncontested controls can finish quickly, while
  repeated contests can reset the streak indefinitely.
5. **Some counterplay is asymmetric for accidental reasons.** Grenade and Area Lock offer a dodge
  response; Pogo’s wall-ignoring flank and backstab do not.
6. **The current bot is not a neutral balance oracle.** It tends to collapse range bands and sends
  every KOTH unit toward the hill.
7. **Clarity may be suppressing perceived depth.** Order completeness, the movement cost of using
  an ability, dodge consequences, objective stakes, damage cause, and audio feedback are weak or
   absent.

These are hypotheses. There is no current empirical match-duration or first-blood baseline.

### 1.5 Verified experiment hazards

Several implementation details can invalidate otherwise reasonable A/B tests:

- `Shooting.FireBullet` uses unseeded `UnityEngine.Random.Range`; bullets resolve through runtime
physics and collision callbacks.
- Wall movement remains full-cell, but direct-fire, projectile, Grenade, Area Lock, and vision-cone
physics use the 0.4-unit chamfered collider; a geometry experiment must not change that collider
silently.
- Shooting opportunity is coupled to execution timing: unrelated movement or ability duration can
change how long other units remain allowed to shoot.
- The dodge phase uses one round-wide maximum dive range and one maximum time value, so an unrelated
activation can enlarge another threat’s response budget.
- Normal plan RPCs have no explicit match/round/phase epoch or serialized payload bound.
- Human plans have no explicit same-cell/crossing arbitration equivalent to the bot’s conservative
route reservations.
- Weapon acquisition uses Euclidean world range but now requires the target cell to be observable
under the Manhattan, wall-clipped fog model. Its wall clearance is derived from the selected bullet
prefab's sphere radius, so projectile scale/collider experiments also change acquisition.
- The compact dev spawn set is asymmetric and places a blue unit at `(7,5)`, which is already a hill
cell. It is suitable only for targeted mechanics after correction, not for balance evidence.
- The checked-in PvP E2E requires a second MPPM client. A July 20, 2026 attempt could not complete
because only the host was connected.
- `AudioManager` has serialized sources but no active service logic, and no gameplay code emits SFX.
The Game scene’s looped play-on-awake music is a functioning static baseline and must be preserved.

### 1.6 Documentation drift

Older prose contains values and features that are not authoritative:

- the fog/damage implementation documents preserve the superseded one-clean-magazine profile;
- older Commander material describes Reroute or an unavailable Commander; the live eligible
Commander uses Smoke Screen;
- some historical docs describe the pre-expansion board.

`docs/GAME_DESIGN.md` and this dossier describe the current working-tree profile. Historical
documents remain evidence of earlier decisions, not runtime authority. Reroute remains outside this
dossier’s scope.

### 1.7 Existing evidence

- This dossier was audited on **July 20, 2026** against repository HEAD
`9b5e73eaba81d6a6a6e845615f4245060c31efb4` plus the then-uncommitted working tree. Because that
working tree has no immutable revision, the current asset values are restated in §1.3 and must be
re-read before any experiment is implemented.
- An earlier same-day broker report recorded **zero compile errors** and **102/102 EditMode tests
passed** after the map/Smoke work. No durable Unity test-job identifier was retained, and the suite
was not rerun while authoring this dossier; treat the result as context, not attributable evidence.
- A July 20 full PvP runtime E2E attempt did not complete because no MPPM second client was active.
- No N-match production-parity baseline, human depth study, or seeded replay dataset exists.

The historical green report is neither an immutable regression artifact nor balance evidence.

## 2. Shared evaluation contract

### 2.1 Two fixtures, two purposes

**Production-parity balance fixture**

- Current 15 × 10 board, 18 walls, six-cell hill, production spawns, fog, and 5v5 baseline.
- Mirrored rosters and side swaps.
- Used for pacing, balance, map, ability, and bot-outcome evidence.

**Compact mechanics fixture**

- Small/fast deterministic arrangements for a single rule, collision, telegraph, or authority check.
- Must be symmetric and off-hill before it is treated as a reusable fixture.
- Never used to infer match duration, roster power, first blood, or map balance.

### 2.2 Isolation rules

For every clean experiment:

1. Change one primary variable.
2. Pin mode, map, wall layout, hill, spawn set, roster, roster slots, fog, planning policy, and seed
  schedule unless one is the declared variable.
3. Mirror rosters and swap sides.
4. Run the deterministic/edit-mode gate before runtime sampling.
5. Declare the independent analysis unit before running. For bot A/B, it is normally a **seed
  block** containing treatment/control and both side assignments (plus matched roster-slot swaps
   when relevant). For human A/B, it is the participant or stable participant-pair block. Rounds are
   nested observations, not independent samples.
6. Use 30 independent seed blocks only as an initial variance/bug-screening pilot, not a universal
  proof threshold. Continue or stop using a pre-registered precision/power rule tied to the
   experiment’s minimum practically important effect.
7. Do not pool Elimination and KOTH.
8. Do not pool bot and human evidence.
9. Reject a combat conclusion based on one match or one highlight.

Structural probes such as 2v2 and 4v4 cannot be perfectly one-variable because they require matched
spawn and UI wrappers. They must declare that limitation rather than claiming clean attribution.

### 2.3 Metric definitions

**Outcome coding and inference**

- For a **team-local/focal treatment**—for example, one roster receives a unit-stat variant—declare
the focal team before the run and code its win = 1, draw = 0.5, and loss = 0. The paired outcome
effect is the focal treatment-minus-control score difference within the same seed/side block,
averaged across side assignments. Also report raw win/draw/loss proportions.
- For a **symmetric global treatment**—for example, both teams use a planning, KOTH, or global-HP
rule—there is no “treatment team” and no treatment-perspective win estimand. Estimate
treatment-minus-control differences only for neutral match metrics such as duration, meaningful
rounds, contests, or choice ratings. Use W/D/L solely for separately declared side/roster-bias
guardrails.
- For continuous/count outcomes, pre-register the primary estimand—normally the mean within-block
treatment-control difference—and its minimum practically important effect. Median and IQR may
describe raw distributions but do not replace an effect estimate.
- Report 95% confidence intervals clustered/bootstrap-resampled at the independent seed block or
participant block. Never treat rounds, units, or repeated matches from one participant as
independent.
- Counterbalanced human repeated-measures studies use participant/pair-cluster intervals or an
explicitly specified mixed-effects model. Missing sessions and protocol violations are reported;
they are not silently dropped.

**Match duration**

- Primary: authoritative round number when the match result is set.
- Secondary: real seconds only at `Time.timeScale = 1`; fast-forward wall-clock is not comparable.

**Meaningful-round ratio**

- A round is meaningful if at least one unit dies, HP changes, an ability charge is consumed, or
KOTH status/controller/streak/score changes, including Empty↔Contested transitions.
- Report meaningful completed rounds and meaningful ÷ total completed rounds. A zero-round match is
invalid for this ratio and reported separately, never divided by zero.

**Time to first blood**

- First round where any alive state changes from true to false.
- If both teams lose a unit in that round, first blood is **tied**; do not credit either team.

**Kills per round**

- Total deaths ÷ total rounds, plus the share of rounds with at least one death.

**Unit lifespan**

- Rounds survived per unit and per archetype; death ends that unit’s participation in current modes.

**Early-alpha rate**

- Among matches with a unique first-blood team, report its win probability and confidence interval.
Report simultaneous-first-blood frequency separately; tied cases remain in all other match
metrics but are not assigned to either side for this estimand.
- Also report unit and total-HP differential after the first lethal exchange.

**Ability usage and value**

- Uses consumed ÷ uses available, per kit.
- Outcome value is measured through controlled allowed/suppressed or variant cohorts, not by
correlating voluntary use with wins.

**Dodge usage and efficacy**

- Submitted dodge paths ÷ dodge windows offered.
- Share of threatened units that avoid the telegraphed damage, plus original orders/abilities
canceled by dodging.

**KOTH contest and progression**

- Contested completed rounds ÷ all eligible completed KOTH rounds, plus Empty, Controlled, and
Contested shares, streak/score progression, control flips, and rounds to win.

**Comeback and lead changes**

- Elimination lead: living-unit differential, with total HP as a tie-break.
- KOTH lead: controller or score lead.
- A comeback is a win by a team that was previously behind under the declared definition.

**Roster and side win rate**

- Measured only with side swaps and mirrored slot assignments. Report paired outcome-score
differences and raw win/draw/loss proportions with 95% intervals.
- A side effect whose interval exceeds the pre-registered practical-bias margin indicates
positional, fixture, or policy bias before it indicates unit balance.

### 2.4 Human strategic-depth rubric

Bots can verify rules and produce controlled distributions. They cannot establish fun or depth.
Human sessions should ask:

- Did each player face at least **two non-obvious, meaningful choices per round**?
- Could the player read the state and predict likely consequences?
- Was counterplay visible and available before a lethal event?
- Did a deficit permit a credible recovery without erasing the value of the earlier win?
- Did fog conceal uncertainty fairly, without UI, VFX, or audio leaking hidden state?
- Did added options produce strategic choice, or merely bookkeeping and cognitive load?

Use 5–8 participants only as a **formative pilot** to refine instructions, identify failure modes,
and estimate variance. A confirmatory depth claim needs a pre-registered participant/pair-level
estimand, practical-effect threshold, precision/power rule, counterbalanced arm order, and 95%
clustered interval. Pair the rubric with the quantitative match record.

### 2.5 Target hypotheses

These targets are design hypotheses, not observed facts:

- **4–8 minutes** per match at normal time scale;
- **5–9 meaningful rounds**;
- first blood usually in **round 2 or 3**;
- at least **two non-obvious choices per player per round**;
- lethal exchanges remain decisive when positioning is clearly lost.

An experiment is not automatically good because it lengthens matches. It must improve decision
quality without creating idle rounds, opaque bookkeeping, unavoidable ability spam, or muddy TTK.

### 2.6 Status vocabulary

- **Prerequisite:** evidence or authority infrastructure required to test later arms credibly.
- **Viable experiment:** coherent enough to test; not approved for production.
- **Deferred:** worth preserving, but blocked by a rule, fixture, ownership, or scope decision.
- **Falsification:** deliberately challenges a protected direction and is expected to lose.
- **Excluded:** not a valid arm because it is incoherent with the problem or bundles variables.

### 2.7 Shared decision rule

Before implementation, each arm must pre-register its primary estimand, analysis unit, minimum
practically important effect, guardrail harm margins, tie/missing-data rules, and stopping rule. An
arm survives only if:

1. deterministic, authority, fog-disclosure, and regression gates pass;
2. its 95% interval supports the pre-registered practical improvement rather than merely a favorable
  point estimate;
3. guardrail intervals exclude unacceptable side, slot, roster, accessibility, performance, and
  strategic-depth harm;
4. the effect is estimated across independent seed/participant blocks instead of pseudo-replicated
  rounds or one highlight match;
5. the change can be removed through its stated rollback boundary without taking unrelated work
  with it.

A longer match with fewer meaningful decisions fails. A more readable match that leaks hidden state
fails. A statistically stronger bot that is less fair or less understandable to humans fails.

## 3. Experiment registry

The registry is intentionally non-ranked. Dependencies describe what must exist for valid evidence;
they are not a hidden first-wave recommendation.

### Evidence, authority, and comparability

#### EXP-A01 — Per-round gameplay telemetry

**Status:** Prerequisite

- **Problem / defense:** Match length, first blood, snowball, charge value, and comeback are not
recorded. A neutral telemetry layer is required before balance claims can be compared.
- **Arms / primary variable:** Infrastructure only: structured snapshots at each planning boundary
and one machine-readable match result. No gameplay arm.
- **Controls & evidence:** Record the same fields in production-parity bot and PvP runs. Telemetry is
server-authored; client logs are supplemental.
- **Metrics:** All metrics in §2.3, plus mode, map ID, roster, slot, side, seed, result reason, and
topology.
- **Gate / protocol:** A scripted match emits one record per round; totals reconcile with unit HP,
alive state, remaining uses, hill state, and `LastMatchResult`.
- **Dependencies / conflicts:** None. Required by every quantitative experiment.
- **Owners / files / rollback:** QA + gameplay engineering;
`GameLoop.cs`, `DevInput.cs`, `DevBotE2ETest.cs`. Dev/editor-guarded logger removable as one unit.

#### EXP-A02 — Seeded combat and ordered event digest

**Status:** Prerequisite

- **Problem / defense:** Unseeded spread and physics make identical plans diverge. Reproducibility is
necessary to isolate a one-variable change.
- **Arms / primary variable:** A dedicated server gameplay RNG keyed by match, round, stable unit,
and shot; an ordered digest of movement, shots, damage, deaths, and abilities. Physics
determinism is measured separately rather than assumed.
- **Controls & evidence:** Presentation RNG, camera shake, and VFX must not perturb gameplay draws.
- **Metrics:** Replay equality, residual physics divergence, and event-order equality.
- **Gate / protocol:** Repeat one fixed-speed scenario five times. Planning hashes and event digests
must match. If 1× and fast-forward diverge, all batches use one pinned speed.
- **Dependencies / conflicts:** Enables EXP-A04 and every combat/stat arm.
- **Owners / files / rollback:** Gameplay + multiplayer + QA; `Shooting.cs`, `Bullet.cs`,
`GameLoop.cs`, `DevInput.cs`. Remove the RNG adapter/digest to restore current behavior.

#### EXP-A03 — Production-parity and compact fixture separation

**Status:** Prerequisite

- **Problem / defense:** The compact dev spawns are asymmetric and include a hill cell, so they
cannot support balance conclusions.
- **Arms / primary variable:** Keep compact scenarios for mechanics; add a production-parity
balance fixture. Correct compact spawns to be rotationally symmetric and off-hill.
- **Controls & evidence:** Production fixture uses current 15 × 10 geometry, production spawns,
current mode rules, and explicit rosters.
- **Metrics:** Spawn symmetry, initial visibility, path length, first legal contact, and fixture ID.
- **Gate / protocol:** Pure checks prove every spawn is in bounds, open, off-hill, reachable, and
mirrored. A balance report is rejected if it came from the compact fixture.
- **Dependencies / conflicts:** Required before any runtime balance sample.
- **Owners / files / rollback:** Level + gameplay + QA; `GameLoop.cs`, `DevE2ETest.cs`,
`BotExplorationEditModeTests.cs`. Keep both fixture definitions independently reversible.

#### EXP-A04 — Mirrored bot self-play runner

**Status:** Prerequisite

- **Problem / defense:** Current bot E2E covers roughly one autonomous round and cannot provide
paired, side-swapped match distributions.
- **Arms / primary variable:** Dev-only bot-vs-bot control for both logical teams, with AB/BA sides,
roster-slot swaps, seed schedule, and telemetry.
- **Controls & evidence:** The bot sentinel remains a logical participant, never an NGO client or
network owner.
- **Metrics:** Paired outcome delta, side bias, planner hash, matches/minute, and all §2.3 metrics.
- **Gate / protocol:** Seeds 0–29 with A-blue/B-red, then B-blue/A-red. Repeat when rosters differ.
Report paired distributions, not raw one-sided win rate.
- **Dependencies / conflicts:** EXP-A01, EXP-A02, EXP-A03, and non-degenerate bot policies.
- **Owners / files / rollback:** Bot AI + gameplay + QA; `BotPlayer.cs`, `GameLoop.cs`,
`DevBotE2ETest.cs`. Dev-only runner can be deleted without production impact.

#### EXP-A05 — Plan and dodge RPC epoch/bounds hardening

**Status:** Prerequisite

- **Problem / defense:** Plan payloads have no explicit round/phase identity and no early entry/path
bounds. Stale or oversized traffic can invalidate both authority and experiment results.
- **Arms / primary variable:** Command envelopes include match, round, and phase epoch; bound unit
actions and path lengths before allocation; preserve existing sanitation.
- **Controls & evidence:** Sender maps to one logical human team. Bot contributions never arrive as
RPCs. Legal current-epoch commands remain behaviorally identical.
- **Metrics:** Rejection reason, duplicate-final rate, payload size, and quorum contribution.
- **Gate / protocol:** Reject stale, future, wrong-phase, foreign-unit, dead-unit, duplicate,
non-finite, excess-unit, and excess-path payloads without mutating authoritative plans.
- **Dependencies / conflicts:** Supports every networked runtime experiment.
- **Owners / files / rollback:** Multiplayer + gameplay + QA; `PlanMovement.cs`, `GameLoop.cs`,
`GameplayNetworkEditModeTests.cs`. Envelope checks are separable from movement rules.

#### EXP-A06 — Two-client PvP/Relay verification topology

**Status:** Prerequisite

- **Problem / defense:** Full PvP E2E is currently blocked without an active MPPM client; host-only
evidence cannot prove remote ownership, fog, timing, or cue behavior.
- **Arms / primary variable:** Local MPPM host/client and authenticated Relay host/client fixtures;
bot topology remains one NGO client with two logical teams.
- **Controls & evidence:** PvP always has two clients and two seats; third clients are rejected.
Unit count never changes client capacity.
- **Metrics:** Connected clients, seat/team mapping, observer visibility, state hashes, disconnect
outcome, and stale-command rejection.
- **Gate / protocol:** Complete one Elimination and one KOTH match in local PvP; run the authority
matrix again through Relay when credentials are available.
- **Dependencies / conflicts:** EXP-A05. Reconnect remains out of scope unless separately chartered.
- **Owners / files / rollback:** Multiplayer + QA; `NetworkHandler.cs`, `DevE2ETest.cs`,
Relay tests. Test harness only.

### Pacing, match structure, and objectives

Check a box below to select that experiment for implementation and testing.
Each **Details** field gives the plain-language version; the arms and protocol below it define the
actual controlled comparison.

#### EXP-B01 — Late-match planning floor

- [ ] **Select EXP-B01**

**Status:** Viable experiment

- **Details:** Keep the current scaling timer, but stop it from shrinking below the selected floor.
Early 5v5 planning gets 25 seconds and both players may submit early; only late-game planning
and 1v1 rounds gain more thinking time.
- **Problem / defense:** The most consequential 1v1 rounds get a five-second client deadline budget,
plus the current server acceptance grace. A floor can improve decision quality without changing
early 5v5 pacing or lethality.
- **Arms / primary variable:** Effective minimum **5 / 8 / 10 seconds**; keep
`5 × largest living team` and early dual-submit.
- **Controls & evidence:** Human-required; bots plan immediately and cannot validate thinking time.
- **Metrics:** Timeout/no-op rate, revisions, unused seconds, decision rubric, duel rounds, total
match time.
- **Gate / protocol:** Pure timer checks for 3/2/1/0 living units; counterbalanced formative human
pilot, then participant-block sampling under §2.2, balanced across surviving matchups.
- **Dependencies / conflicts:** Pin the selected floor as a control in every later experiment.
- **Owners / files / rollback:** Gameplay + balance + QA; `GameLoop.cs`. One timer helper/value.

#### EXP-B02 — Planning-time policy

- [ ] **Select EXP-B02**

**Status:** Viable experiment

- **Details:** Compare the live 25/20/15/10/5-second countdown with a predictable 25-second countdown in
every round, regardless of survivors. This asks whether consistency improves planning or merely
adds idle time late in a match.
- **Problem / defense:** Living-team scaling is only one cadence model. A fixed shared budget may
improve predictability without giving peers different deadlines.
- **Arms / primary variable:** Largest-living-team baseline versus a fixed shared **25-second**
client deadline budget in every living-team state. An own-team deadline is excluded because it
needs a separate asymmetric-deadline protocol and fairness ruling.
- **Controls & evidence:** Keep planning floor fixed. Human evidence is primary.
- **Metrics:** Usable seconds, early submit, timeout, idle time, workload, match duration, and
perceived fairness.
- **Gate / protocol:** In both arms, start the server-time deadline only after the same two-client
readiness barrier, and send both peers one deadline. Verify 25 seconds at every team size in the
fixed arm, versus 25/20/15/10/5 in the live formula arm, with the same server acceptance grace.
- **Dependencies / conflicts:** EXP-A05/A06. Conflicts with changing team size in the same run.
- **Owners / files / rollback:** Gameplay + multiplayer + UI; `GameLoop.cs`,
`GameHUDController.cs`. Configurable formula defaults to current.

#### EXP-B03 — Explicit fixed spawn-slot assignment

- [ ] **Select EXP-B03**

**Status:** Viable experiment

- **Details:** Show that roster slot one spawns left, slot two center, and slot three right, mirrored
from each player’s perspective. Players can deliberately place a tank, flanker, or ranged unit in
a lane without changing the current spawn coordinates.
- **Problem / defense:** Roster order already maps to left/center/right spawn slots, but the choice is
opaque. Labeling it exposes an existing strategic decision at very low systems risk.
- **Arms / primary variable:** Unlabeled control versus perspective-correct left/center/right slot
labels and map preview.
- **Controls & evidence:** Spawn coordinates and roster order do not change.
- **Metrics:** Slot comprehension, first-contact source, slot survival, selection time, and roster
intent recall.
- **Gate / protocol:** Every roster permutation maps slot `i` to the documented mirrored spawn on
both teams; opponent picks remain hidden.
- **Dependencies / conflicts:** Precedes free deployment and team-size probes.
- **Owners / files / rollback:** UI + level + gameplay; `CharacterSelectionUIController.cs`,
`SelectedSlot.uxml`. Presentation-only rollback.

#### EXP-B04 — Free deployment zone

- [ ] **Select EXP-B04**

**Status:** Deferred

- **Details:** Add a hidden pre-match deployment step where each player places their five units in
approved back-row cells. Placements reveal only when the match begins, creating opening mind games
while the server rejects overlaps, walls, and illegal cells.
- **Problem / defense:** Player-chosen placement could diversify openings, but it introduces a new
phase, validation, disclosure, timing, and spawn-safety rules.
- **Arms / primary variable:** Fixed labeled slots versus hidden placement within approved back-row
cells. Raw arbitrary coordinates are never client-authoritative.
- **Controls & evidence:** Keep team size, map, walls, stats, and planning time fixed.
- **Metrics:** Opening-path diversity, first-blood variance, deployment time, illegal selections,
and strategic-depth rubric.
- **Gate / protocol:** Requires an approved zone/visibility specification and server validation for
ownership, uniqueness, bounds, walls, overlap, and epoch.
- **Dependencies / conflicts:** EXP-B03, EXP-A05/A06. Not a quick map tweak.
- **Owners / files / rollback:** Gameplay + multiplayer + UI + level; `GameLoop.cs`,
character selection and deployment UI. Fixed slots remain the rollback profile.

#### EXP-B05 — Team-size structural probe

- [ ] **Select EXP-B05**

**Status:** Deferred

- **Details:** Run complete 4v4, 5v5, and 6v6 versions with size-matched spawns, cards, planning
budgets, and network limits. This tests whether fewer units produce cleaner reads or more units
create the formations and combined-arms play the current matches lack.
- **Problem / defense:** 2v2 may sharpen reads; 4v4 may create formations and combined arms. Either
may instead worsen first-blood severity or cognitive load.
- **Arms / primary variable:** **4 / 5 / 6 units per player**, with 5v5 protected as the default.
- **Controls & evidence:** This is explicitly multi-variable: each size needs matched symmetric
spawns, a fixed planning allowance for comparison, dynamic cards, bounded payloads, and roster
coverage. Human evidence is mandatory.
- **Metrics:** Actions lost at first death, choices/round, timeout, charge volume, wipe rate,
simultaneous contacts, clutter, match duration, and workload.
- **Gate / protocol:** Full Elimination/KOTH, bot/PvP, fog, authority, UI, performance, and path
conflict gates before pilot seed blocks and confirmatory sampling under §2.2 for each size.
- **Dependencies / conflicts:** EXP-A04/A06, EXP-B03, occupancy ruling. Conflicts 2v2 vs 4v4.
- **Owners / files / rollback:** Cross-discipline; `RosterRules.cs`, `GameLoop.cs`, bots, menus,
UXML/USS. Restore all size-dependent surfaces atomically to 3.

#### EXP-B06 — Shooting-window decoupling

- [ ] **Select EXP-B06**

**Status:** Deferred pending telemetry

- **Details:** Give weapons an explicit firing opportunity that does not grow when an unrelated unit
takes longer to move or animate an ability. Identical combat plans should therefore produce the
same shot count whether another unit makes a short move or a long Pogo/Shield action.
- **Problem / defense:** Unrelated movement or ability duration may extend other units’ fire
opportunity. This can masquerade as damage or mobility balance.
- **Arms / primary variable:** Current execution-coupled window versus an explicit per-round fire
budget or per-weapon cycle.
- **Controls & evidence:** First measure frequency and outcome impact; do not refactor blind.
- **Metrics:** Permitted shots, reload opportunities, damage, deaths, and result changes attributable
only to unrelated long actions.
- **Gate / protocol:** Fixed scenario with and without a long unrelated Pogo/Shield/ability action;
treatment permits identical shot counts.
- **Dependencies / conflicts:** EXP-A01/A02. Changes every weapon’s effective value.
- **Owners / files / rollback:** Gameplay engineering + balance; `GameLoop.cs`, `Shooting.cs`.
Policy switch restores current coupling.

#### EXP-B07 — KOTH consecutive hold target

- [ ] **Select EXP-B07**

**Status:** Viable experiment

- **Details:** Keep the current “hold without interruption” KOTH rule but require 2, 3, 4, or 5
consecutive sole-control rounds. Contested or empty rounds still reset progress; only the amount
of sustained control needed to win changes.
- **Problem / defense:** Three uncontested controls can resolve quickly; a larger target may create
comeback windows without changing scoring semantics.
- **Arms / primary variable:** Consecutive target **2 / 3 / 4 / 5**; 3 is control and 2 is a
short-match falsification arm.
- **Controls & evidence:** Keep hill geometry, no-respawn policy, charges, cover, and scoring type
fixed.
- **Metrics:** Rounds to win, contests, streak resets, control flips, elimination endings, and human
“progressless” rating.
- **Gate / protocol:** Pure transition tests for every target; paired KOTH seed blocks under §2.2
with Pogo-center and no-Pogo strata.
- **Dependencies / conflicts:** Map/hill geometry must remain fixed. Separate from cumulative score.
- **Owners / files / rollback:** Gameplay + balance; `GameLoop.cs`, HUD copy. Restore target 3.

#### EXP-B08 — Consecutive versus cumulative KOTH scoring

- [ ] **Select EXP-B08**

**Status:** Viable experiment

- **Details:** Compare today’s reset-on-contest streak with a score where every sole-control round is
permanently banked. Test both scoring meanings at matched targets so the result distinguishes
“durable progress” from simply requiring more points.
- **Problem / defense:** Consecutive control creates dramatic resets; cumulative control gives every
successful hold durable value. Both can produce strategic tension in different ways.
- **Arms / primary variable:** A two-factor matrix: scoring semantics **consecutive / cumulative** ×
target **3 / 4 / 5**. Consecutive-to-3 is the live control; cumulative-to-3 is retained only as the
matched-threshold short-match falsification cell.
- **Controls & evidence:** No-respawn policy, charges, hill, and walls stay fixed. Estimate threshold
effects within each scoring semantic, semantic effects at matched thresholds, and their interaction;
do not attribute a cumulative-4/5 result to semantics alone.
- **Metrics:** Scoring rounds, scoreless rounds, lead changes, wins from two points behind, match
duration, and contest/reposition decisions.
- **Gate / protocol:** Atomic score transition tests; paired KOTH seed blocks under §2.2 by roster
stratum, reporting both factors and interaction.
- **Dependencies / conflicts:** Requires score UI/replication. Conflicts consecutive vs cumulative.
- **Owners / files / rollback:** Gameplay + multiplayer + UI; `GameLoop.cs`,
`GameHUDController.cs`. Consecutive state remains the rollback profile.

#### EXP-B09 — KOTH-only survivor healing

- [ ] **Select EXP-B09**

**Status:** Viable experiment

- **Details:** At the KOTH round boundary, restore 25% or 50% of each living unit’s missing health.
Dead units remain eliminated, charges never return, and Elimination receives no healing. The test
asks whether battered survivors can contest again without erasing deaths.
- **Problem / defense:** Survivors currently carry all damage forward. A between-round KOTH heal may
support repeated contests without weakening the permanence of a lost unit.
- **Arms / primary variable:** Heal **0 / 25 / 50% of missing HP** for living units at the KOTH
round boundary.
- **Controls & evidence:** KOTH only; dead units are not revived; charges and scoring remain
unchanged.
- **Metrics:** HP restored, changed hit breakpoints, next-round deaths, control recovery, comeback,
rounds, and duration.
- **Gate / protocol:** Pure clamp/dead/terminal checks; paired seed blocks under §2.2 with
damage-erased and Shotgunner-outcome guardrails.
- **Dependencies / conflicts:** Conflicts with the no-healing attrition direction; never combined
with finite lives or charge restoration initially.
- **Owners / files / rollback:** Gameplay + balance; `Health.cs`, `GameLoop.cs`. KOTH-gated hook,
amount 0 restores baseline.

#### EXP-B10 — Opt-in finite KOTH respawn lives

- [ ] **Select EXP-B10**

**Status:** Deferred

- **Details:** Opt KOTH into the preserved respawn lifecycle with a limited per-unit or team stock. A
casualty returns while stock remains and stays dead after it is exhausted; a complete team wipe
still ends the match immediately.
- **Problem / defense:** The no-respawn baseline makes every loss permanent. A finite respawn budget
tests whether a small number of returns improves objective play without creating unlimited
attrition.
- **Arms / primary variable:** No respawn versus per-unit or team-pool respawn lives, with the two
stock models tested separately; full-team wipe remains terminal unless a later ruling explicitly
changes it.
- **Controls & evidence:** Consecutive scoring, HP on respawn, charges, and map stay fixed.
- **Metrics:** Respawns, exhausted-life deaths, return-to-hill time, wipe endings, comeback, rounds,
and objective wins.
- **Gate / protocol:** Requires a ruling on pool ownership. Pure tests cover simultaneous deaths,
zero-life casualties, and exactly one terminal result.
- **Dependencies / conflicts:** Separate from cumulative score and KOTH healing.
- **Owners / files / rollback:** Director + gameplay + multiplayer + UI; `GameLoop.cs`,
`Health.cs`. Disable the KOTH respawn policy and remove life state to restore permanent casualties.

#### EXP-B11 — Elimination survivor healing

- [ ] **Select EXP-B11**

**Status:** Falsification only

- **Details:** Restore 25% or 50% of missing health to living units between Elimination rounds, with
no revives. This deliberately tests the likely-wrong idea that longer firefights create more
strategy rather than weakening the value of damage and positioning.
- **Problem / defense:** Healing may lengthen Elimination, but it is expected to erase attrition and
weaken the consequence of losing position.
- **Arms / primary variable:** No healing versus one between-round heal of **25% or 50% of missing
HP**, as separate arms.
- **Controls & evidence:** Elimination only; never mid-combat; no revives.
- **Metrics:** Damage erased, no-kill rounds, reload cycles, match duration, and human
decisiveness/depth scores.
- **Gate / protocol:** Run only after the KOTH heal can be isolated. Escalate to the director only if
human depth improves materially without muddy exchanges.
- **Dependencies / conflicts:** Challenges the decisive-attrition identity.
- **Owners / files / rollback:** QA + balance; Elimination-gated hook in `GameLoop.cs` using a
clamped `Health.cs` server method. Amount 0/default-off removes the treatment.

#### EXP-B12 — Miss-allowance lethality profile

- [ ] **Select EXP-B12**

**Status:** Implemented prototype; verification and tuning

- **Details:** Validate the role-shaped **80 / 120 / 160 HP** profile with clean magazine damage of
Commander 40, Pogo 36 frontal / 72 rear, Shotgunner 80, Sniper 50, and Soldier 50. Ordinary
standard-target attack modes must remain between two and four provisional miss-adjusted magazine
equivalents and center near three.
- **Problem / defense:** The prior control averaged about 1.4 landed magazines per frontal kill,
making routine first contact disproportionately terminal. A universal scalar would preserve the
old 4:1 health spread and produce inconsistent breakpoints, so health compression and role-specific
magazine budgets move together as one coherent profile.
- **Arms / primary variable:** Current role-shaped miss-allowance profile; the pre-allowance
6/10/6/40/8 damage profile; and a strict normalized magazine-damage control. Do not mix individual
values across profiles during the initial comparison.
- **Controls & evidence:** Keep cadence, magazine size, reload, spread, range, movement, and ability
charges fixed. Keep Area Lock at flat 130 and Grenade at 80.
- **Metrics:** Landed and fired magazines per kill, reloads before lethal hit, first blood, focus
fire, damage without kills, match duration, meaningful rounds, and human positioning payoff.
- **Gate / protocol:** Deterministic asset and breakpoint tests must pass first. Then use paired seed
and participant blocks from §2.2, segmented by weapon and range. The provisional 80% / 5/6 / 60% /
90% allowances must never be reported as observed accuracy.
- **Dependencies / conflicts:** Execution-window duration can grant extra reload cycles and must be
measured before attributing pacing changes solely to damage.
- **Owners / files / rollback:** Balance + gameplay + QA; `Assets/UnitStats/*.asset`,
`AreaLock.cs`, and `CombatBalanceEditModeTests.cs`. Restore weapon damage to Commander 6, Pogo 10,
Shotgunner 6, Sniper 40, and Soldier 8 to reproduce the pre-allowance profile; keep the current HP
tiers and flat Area Lock 130.

### Ability economy and counterplay

#### EXP-C01 — Per-kit starting charges

- [ ] **Select EXP-C01**

**Status:** Viable experiment

- **Details:** Change one class at a time from its current single starting charge to zero or two.
This can reveal whether a powerful ability should be rarer, whether a utility ability deserves
repeat use, and whether players actually spend the additional charge.
- **Problem / defense:** Universal `uses = 1` assumes abilities of very different value deserve the
same economy. Per-kit count can preserve strong effects while varying frequency.
- **Arms / primary variable:** For **one kit at a time**, starting uses **0 / 1 / 2**. All other kits
remain at 1.
- **Controls & evidence:** No recharge or respawn restoration in the same arm. Human recall and
bookkeeping are explicit risks.
- **Metrics:** Uses available/consumed/stranded, damage or prevention per charge, win conversion,
mid/late ability activity, and charge comprehension.
- **Gate / protocol:** Independent clamped server ledgers and HUD values; per-kit/mode seed blocks
and human reserve-timing evidence follow §2.2.
- **Dependencies / conflicts:** Charge visibility/lifecycle authority. Conflicts starting count vs
recharge cadence.
- **Owners / files / rollback:** Balance + gameplay; the selected `Assets/UnitStats/*.asset`.
Restore that asset to 1.

#### EXP-C02 — Single late recharge

- [ ] **Select EXP-C02**

**Status:** Viable experiment

- **Details:** Keep one starting charge, then grant one additional charge at the start of round 4 or
5 if that unit has room to store it. A player who hoarded the original charge receives no extra
stock beyond the cap, preserving scarcity while reintroducing abilities late.
- **Problem / defense:** One-use abilities can leave later rounds mechanically plain. One
predictable recharge creates a late decision spike while preserving early scarcity.
- **Arms / primary variable:** No recharge versus one recharge at round **4 or 5**, once per unit,
stored cap 1.
- **Controls & evidence:** Starting uses stay 1; respawn grants nothing; test one ability type at a
time. Offensive recharges are treated more skeptically than utility recharges.
- **Metrics:** Eligible/received/used recharge, second-cast value, unused recharge, ability-decided
matches, round count, and player awareness.
- **Gate / protocol:** Idempotent server transition and visible owner countdown/state; mirrored seed
blocks and a human reserve-tension check follow §2.2.
- **Dependencies / conflicts:** Separate from EXP-C01 and C03.
- **Owners / files / rollback:** Gameplay + balance + UI; `Unit.cs`, `GameLoop.cs`. Remove the
single grant hook.

#### EXP-C03 — Charge restored on respawn-enabled KOTH death

- [ ] **Select EXP-C03**

**Status:** Falsification only

- **Details:** If KOTH is separately opted into respawning, restore at most one spent charge the
first time a unit returns for use next round; later deaths restore nothing. This bounded version
tests whether respawned units need their identity back without allowing endless Grenades or Area
Locks.
- **Problem / defense:** Restoring a charge on respawn could keep KOTH ability-rich, but it rewards
death and can repeat Grenade 80 or Area Lock 130 indefinitely.
- **Arms / primary variable:** Baseline no restore versus at most one restored charge per unit per
match, cap 1, available the following round.
- **Controls & evidence:** One kit at a time; scoring, HP, healing, and starting uses fixed.
- **Metrics:** Deaths followed by restored casts, repeat deaths, deliberate-exposure proxy,
damage/control per restored charge, and ability-decided hill steps.
- **Gate / protocol:** Human KOTH evidence required; reject if death becomes an economy strategy or
restored casts dominate objective progress.
- **Dependencies / conflicts:** Requires a separately approved respawn-enabled KOTH arm. Conflicts
with starting count and late recharge.
- **Owners / files / rollback:** QA + balance; one bounded grant in `Unit.cs`, invoked only from
`GameLoop.RespawnEliminatedUnits`, with card replication through the existing use-update seam.
Default-off preserves the current no-respawn rule.

#### EXP-C04 — Per-alerted-unit dodge range isolation

- [ ] **Select EXP-C04**

**Status:** Prerequisite before response tuning

- **Details:** Internally calculate each alerted unit’s legal dive distance from only the threats
affecting that unit, while leaving today’s shared deadline unchanged. Players should see no
difference with current equal values; the change prevents future range tuning on one ability from
silently enlarging another ability’s dodge.
- **Problem / defense:** The shared maximum range/time makes a future response-stat test
non-isolated. All live abilities currently use dive range 2 and three seconds, so the live
shared/per-unit comparison is numerically degenerate until heterogeneous response values exist.
- **Arms / primary variable:** Range-isolation prerequisite: for each alerted unit, assign the
maximum dive range among threats actually affecting that unit while preserving the current global
deadline formula exactly: maximum activation `timeDivePerUnit` × largest alerted-team count,
shared by both teams. A per-team summed deadline is a separate gameplay-policy arm, not part of
range isolation.
- **Controls & evidence:** First prove equivalence with all live values. Then use synthetic
heterogeneous **range** fixtures solely to prove isolation. Only after that may one Grenade
response range, Area Lock corridor, or dive range become a gameplay arm. Heterogeneous
`timeDivePerUnit` values require a separately specified global-versus-per-team deadline experiment.
- **Metrics:** Offered/answered windows, phase duration, legal submissions, canceled original
orders, damage avoided, and ability conversion.
- **Gate / protocol:** Live-value fixture produces unchanged ranges and the identical global
deadline. Synthetic heterogeneous-range fixtures prove unrelated activations cannot change a
unit’s range, while multiple threats on one unit aggregate by maximum rather than sum.
- **Dependencies / conflicts:** EXP-A05; bot and human receive identical budgets.
- **Owners / files / rollback:** Gameplay + multiplayer + bot + QA; `GameLoop.cs`,
`PlanMovement.cs`, `BotPlayer.cs`. Restore shared maximum as one rollback.

#### EXP-C05 — Pogo landing counterplay

- [ ] **Select EXP-C05**

**Status:** Viable experiment

- **Details:** Compare the current generic destination marker with a Pogo-specific landing/
trajectory cue, a brief reveal on landing, or a real dodge response. Each arm leaves Pogo’s
movement and damage untouched so the test isolates whether clearer counterplay is enough.
- **Problem / defense:** Pogo can jump through walls and threaten a 72-damage clean backstab
magazine. Its target
square already receives the shared public generic ability marker, but Pogo has no dodge response
and the marker does not explain wall-ignoring movement or rear-angle danger.
- **Arms / primary variable:** Existing generic marker control; enhanced Pogo-specific landing/
trajectory cue; brief landing reveal; a Pogo-specific response window. Each is a separate arm
with all Pogo stats fixed.
- **Controls & evidence:** Do not combine telegraph/reveal with a mobility, range, or backstab nerf.
- **Metrics:** Flank attempts, adaptation of facing/formation, backstab conversion, response success,
Pogo survival, and fairness/depth rubric.
- **Gate / protocol:** Fog-safe payload exposes no more location data than the existing target-square
telegraph unless the reveal arm explicitly authorizes it; damage and movement remain unchanged.
Human evidence is mandatory.
- **Dependencies / conflicts:** EXP-C04 budget isolation for response-window arms. Conflicts Pogo
stat tuning vs added counterplay.
- **Owners / files / rollback:** Gameplay + VFX + multiplayer; `Pogo.cs`, `GameLoop.cs`. Remove the
presentation/response hook, leaving stats untouched.

### Unit and ability breakpoint banks

Each bank below is a family of isolated arms. A slash-separated list is not permission to change
those fields together.

#### EXP-D01 — Soldier and Grenade breakpoint bank

- [ ] **Select EXP-D01**

**Status:** Viable experiment

- **Details:** Tune exactly one Soldier field per run: rifle damage, magazine, targeting range, or
one Grenade field such as damage, radius, or throw range. The goal is to establish a dependable
all-rounder benchmark without accidentally changing both its gun and ability together.
- **Problem / defense:** Soldier is the mid-band reference and deals 50 per clean magazine:
2.4 landed or 3 allowance-adjusted magazines against a 120 HP standard unit. Grenade remains a
separate 80-damage, telegraphed exception.
- **Arms / primary variable:** Weapon damage **8 / 10 / 12**; magazine 4/5/6; target range 3/4/5;
Grenade damage **60 / 80 / 100**; radius **1 / 1.6 / 2 / 3**; Grenade target range **4 / 5 / 6**.
Each field is a separate arm; Grenade 100 is an explicit one-shot stress arm.
- **Controls & evidence:** Change one field. Grenade damage, radius, and response remain separate.
- **Metrics:** Soldier hit conversion, first blood, kills/range, Grenade targets/dodges/damage,
roster win contribution, and match pacing.
- **Gate / protocol:** Deterministic hit-count and footprint checks; mirrored seed blocks and human
dodge-vs-eat decisions follow §2.2.
- **Dependencies / conflicts:** HP banks and EXP-C04 budget isolation. Soldier should remain the
all-rounder rather than absorbing a specialist identity.
- **Owners / files / rollback:** Balance + gameplay; weapon/range/magazine in
`Assets/UnitStats/Soldier.asset`, Grenade radius/range in the same asset, and serialized damage on
the `Grenade` component in `Assets/Prefabs/Units/Soldier.prefab`. Restore the one tested field.

#### EXP-D02 — Shotgunner and Shield Rush breakpoint bank

- [ ] **Select EXP-D02**

**Status:** Viable experiment

- **Details:** Independently vary Shotgunner durability, pellet damage, close-range envelope, burst
size, or one Shield Rush property. This identifies whether the class succeeds because it earns a
risky close approach or simply overwhelms opponents through health and burst volume.
- **Problem / defense:** 160 HP plus range-2 burst and a frontal shield may be either the necessary
cost of closing distance or a low-decision stat check.
- **Arms / primary variable:** HP **120 / 160 / 200**; damage 6/8/10; target range 1/2/3;
magazine 8/10/12; Shield Rush distance **2 / 3 / 4**; speed **1 / 1.5 / 2 cells/s**; duration
**2 / 3 / 4 seconds**. Each field is a separate arm.
- **Controls & evidence:** Never buff range while testing durability or shield value.
- **Metrics:** Damage taken while closing, kills at range 0–2, shield-intercepted damage, flank
deaths, survival, and roster win contribution.
- **Gate / protocol:** Hit-count, rush destination, wall stop, frontal protection, and duration
checks; mirrored seed blocks and human frontal-vs-flank comprehension follow §2.2.
- **Dependencies / conflicts:** Cover density strongly changes Shotgunner value; stratify by layout
rather than pooling maps.
- **Owners / files / rollback:** Balance + gameplay; `Shotgunner.asset`, `Shield.cs`, Shotgunner
prefab. Restore one value.

#### EXP-D03 — Pogo mobility and range bank

- [ ] **Select EXP-D03**

**Status:** Viable experiment

- **Details:** Change only one of Pogo’s move distance, jump distance, gun range, bullet range, or
magazine size in each arm. The test separates “reaches every important cell too easily” from
“threatens too much once there,” including whether round-one hill access is healthy.
- **Problem / defense:** Pogo combines the highest move and target range, reaches the hill from the
center spawn in round one, and ignores walls during its jump.
- **Arms / primary variable:** Move **4 / 5 / 6**; target range 5/7/8; bullet range 5/7/9; jump
range **4 / 5 / 6**; magazine 2/3/4. One field per arm.
- **Controls & evidence:** Backstab multiplier and landing counterplay remain fixed.
- **Metrics:** Turn-one hill reach, route distance saved, flank attempts, first control, first blood,
range-band kills, and Pogo roster win contribution.
- **Gate / protocol:** Enumerate walk and wall-ignoring jump reach separately. Run baseline,
cover-dense, and open-layout strata because walls constrain every unit except jumping Pogo.
- **Dependencies / conflicts:** Conflicts isolated stat tuning vs EXP-C05 counterplay. A bundled
mobility/range/backstab nerf is excluded.
- **Owners / files / rollback:** Balance + gameplay; `PogoRider.asset`, `Pogo.cs`. Restore one field.

#### EXP-D04 — Pogo backstab value

- [ ] **Select EXP-D04**

**Status:** Viable experiment

- **Details:** Leave all movement and targeting unchanged and vary only rear-hit damage from 1.5× to
2.5×. Players still have the same opportunities to create or prevent a flank; only the reward for
successfully attacking from behind changes.
- **Problem / defense:** Backstab is a formation/facing mind game. The current theoretical
12 × 3 × 2 = 72 damage makes a rear attack twice as lethal as frontal fire without one-magazine
standard-target kills; an 80-HP Sniper survives one perfect rear magazine with 8 HP.
- **Arms / primary variable:** Backstab multiplier **1.5 / 2 / 2.5** with all mobility, range, and
response rules fixed.
- **Controls & evidence:** Run separately from enhanced landing cue/reveal; the existing generic
target-square telegraph remains present. Preserve the flanker role.
- **Metrics:** Backstab attempts, successful rear-angle checks, damage and kills, facing adaptation,
Pogo survival, and perceived answerability.
- **Gate / protocol:** Pure angle/damage breakpoints; mirrored strata and human formation play
follow §2.2.
- **Dependencies / conflicts:** Map approach angles and cover affect backstab frequency; pin layout.
- **Owners / files / rollback:** Balance; `PogoRider.asset`, `Bullet.cs` tests. Restore multiplier 2.

#### EXP-D05 — Sniper direct damage and Area Lock decoupling

- [ ] **Select EXP-D05**

**Status:** Decoupling implemented; direct tuning viable

- **Details:** First stop Area Lock damage from automatically changing with the rifle. Then test
Sniper shot breakpoints independently from Area Lock damage, warning corridor, duration, and
reach, allowing a strong control ability without requiring a one-shot basic weapon—or vice versa.
- **Problem / defense:** The live Sniper is a 50-damage three-shot against 120 HP, while Area Lock
remains a flat 130. The identities are now independently tunable.
- **Arms / primary variable:** Test direct damage **30 / 50 / 60** for four/three/two-shot standard
breakpoints. Separately test Area Lock damage **100 / 130 / 160**, response corridor
**0.5 / 0.9 / 1.5 cells**, active duration **2 / 3 / 4 seconds**, or anchor range **14 / 99**.
Visual-width arms are presentation-only: the generic warning line is 0.15 world units and the
armed BeamVFX glow is 0.5, while the damage check is a zero-width physics ray.
- **Controls & evidence:** Direct and Area Lock never move together. The 60 value is a deliberate
two-shot arm, not a documentation fix.
- **Metrics:** Direct shots/kill, lock completion, first blood, exposure while locking, Area Lock
alerts/dodges/kills, lane denial, and roster wins.
- **Gate / protocol:** Direct/ability breakpoints, wall/smoke termination, reveal, warning/beam
widths, zero-width damage ray, 0.9-cell control corridor, and dodge checks; per-arm seed and human
telegraph evidence follow §2.2.
- **Dependencies / conflicts:** Sightline length must be pinned. Conflicts Sniper four/three/two
shot breakpoints.
- **Owners / files / rollback:** Balance + gameplay; direct/response/anchor fields in
`Assets/UnitStats/Sniper.asset`, damage/duration in `AreaLock.cs`, generic warning line in
`GameLoop.ShowAbilityTelegraphClientRpc`, and armed beam in `BeamVFX.cs`. Restore direct 50,
response corridor 0.9, duration 3, anchor 99, and flat Area Lock damage 130.

#### EXP-D06 — Commander weapon and Smoke bank

- [ ] **Select EXP-D06**

**Status:** Viable experiment

- **Details:** Tune Commander’s weak rifle separately from Smoke’s cast range, footprint, or
lifetime. This reveals whether the Commander needs more personal combat value, stronger team
utility, or simply a more usable version of the current 3 × 3 one-round cloud.
- **Problem / defense:** Commander has 40 theoretical magazine damage, making Smoke its primary
roster value. Smoke’s worth depends heavily on static cover and lane length.
- **Arms / primary variable:** Weapon damage **6 / 8 / 10**; magazine **3 / 5 / 7**; cadence
**0.2 / 0.3 / 0.4 seconds**; Smoke target range **2 / 3 / 4**; footprint
**1 × 1 / 3 × 3 / 5 × 5**; lifetime **1 / 2 execution rounds** only after the one-round baseline
is fully verified. Each field is a separate arm.
- **Controls & evidence:** Weapon and Smoke never change together. Smoke remains non-damaging,
public, non-walk-blocking, and outside wall/path/physics data. Segment crossings are blocked but
shooter/target endpoint cells remain exempt. Reroute is out of scope.
- **Metrics:** Commander damage, uses stranded at death, sight/fire segments denied, successful
crossings, hill cells covered, Sniper mitigation, and roster win contribution.
- **Gate / protocol:** Weapon breakpoints; exact footprint/inset target bands; smoke line-segment,
endpoint exemption, fog, bullet, and cleanup tests; cover-stratified seed blocks and human
readability follow §2.2.
- **Dependencies / conflicts:** Smoke value is inversely related to wall cover. Protected current
Smoke remains control.
- **Owners / files / rollback:** Balance + gameplay; weapon/range/cadence in
`Assets/UnitStats/Commander.asset`, footprint in `Smoke.cs`, and registration/lifetime cleanup in
`GameLoop.cs`. Restore one value; never overwrite unrelated protected work.

#### EXP-D07 — Weapon/fog envelope alignment

- [ ] **Select EXP-D07**

**Status:** Implemented baseline; stat-alignment alternatives remain optional

- **Details:** The baseline now requires a target to be in authoritative visible cells before
acquisition. If range/vision identities need retuning later, compare granting enough vision or
reducing weapon range without removing that fair-information guard.
- **Problem / defense:** Euclidean weapon range can include diagonal cells outside Manhattan fog
vision. The visibility predicate prevents Pogo/Commander from acting on enemies their team cannot
see.
- **Arms / primary variable:** Preserve firepower by increasing vision (Commander 6→7, Pogo 7→9)
versus preserve fog by lowering target range (Commander 5→4, Pogo 7→5); one unit/field per arm,
with the implemented visible-target predicate fixed in every arm.
- **Controls & evidence:** Pin map/cover because walls alter visible area.
- **Metrics:** Hidden acquisitions, observable-cell fraction, firing contacts, first blood,
range-band damage, and human prediction of sight.
- **Gate / protocol:** Enumerate every lattice offset and require zero targetable-but-unobserved
cells under the alignment arm; mirrored seed blocks follow §2.2.
- **Dependencies / conflicts:** Fair-information ruling. Do not conflate vision buffs with range
nerfs.
- **Owners / files / rollback:** Gameplay + balance; `Shooting.cs`, `GridSystem.cs`,
`Commander.asset`, `PogoRider.asset`. Restore one field/predicate.

### Spatial experiments

#### EXP-E01 — Same-size authoritative map selector

- [ ] **Select EXP-E01**

**Status:** Prerequisite for multiple layouts

- **Details:** Add a server-selected map profile ID so several immutable 15 × 10 wall/hill/spawn
layouts can coexist safely. This does not itself change the battlefield; it prevents alternate-map
tests from mutating shared static geometry or disagreeing between clients.
- **Problem / defense:** Directly mutating static `wallLayout` risks stale state and gives clients no
stable map identity.
- **Arms / primary variable:** Bounded map ID selecting immutable 15 × 10 layout snapshots; current
Industrial layout remains default.
- **Controls & evidence:** Server locks ID/version/hash before spawn; clients never submit geometry.
- **Metrics:** Manifest equality, open-cell connectivity, wall/path/fog agreement, and reset leakage.
- **Gate / protocol:** Every layout is in bounds, connected, spawn/hill-safe, and identical on host
and remote before planning.
- **Dependencies / conflicts:** EXP-A05/A06. First layout experiment varies walls only.
- **Owners / files / rollback:** Level + gameplay + multiplayer; `MatchOptions.cs`, `GameLoop.cs`,
`GridSystem.cs`. Remove alternate profiles and keep current layout.

#### EXP-E02 — Spawn depth and lateral placement

- [ ] **Select EXP-E02**

**Status:** Viable experiment

- **Details:** Compare today’s back-row lanes with either one-row-forward spawns or wider outer
spawns. Units and walls remain identical, so the test measures how starting depth and lane spacing
change first contact, hill access, and opening variety.
- **Problem / defense:** Fixed production spawns may create repetitive openings. Placement can tune
contact timing without changing board size or stats.
- **Arms / primary variable:** Current `(2,0),(7,0),(12,0)`; forward row-1 staging; wider
`(1,0),(7,0),(13,0)`, each with rotational red mirrors.
- **Controls & evidence:** Walls, hill, roster order, planning, and stats fixed.
- **Metrics:** Path to hill, first ability influence, first contact/blood, slot survival, lane-use
entropy, and duration.
- **Gate / protocol:** Every slot is open, off-hill, mirrored, connected, and has documented walk,
ability, and sightline distances; arm/slot seed blocks follow §2.2.
- **Dependencies / conflicts:** Explicit slot labels. Forward spawns may let Smoke/Shield affect the
hill immediately.
- **Owners / files / rollback:** Level + gameplay; spawn profile in `GameLoop.cs`. Restore production
set.

#### EXP-E03 — Cover relocation and density directions

- [ ] **Select EXP-E03**

**Status:** Viable experiment

- **Details:** First move the existing 18 walls without changing their count, then separately try
denser and more open boards. The opposing arms test whether strategy comes from staged
cover-to-cover advances or from wider rotations and longer power lanes.
- **Problem / defense:** More cover may create layered approaches; less cover may create rotation
freedom. Wall count and placement must not be confused.
- **Arms / primary variable:** Sequence: (1) relocate at fixed **18 walls**; then (2) dense
`+4/+8`; then (3) open `−2/−4`. Dense and open are opposing arms, not a blend.
- **Controls & evidence:** Spawns, hill, sightline target, stats, mode, and the 0.4-unit wall-corner
chamfer stay fixed.
- **Metrics:** Route diversity, cover-to-cover movement, kills by range band, first blood,
Area Lock length, Smoke value, backstabs, KOTH closure, and roster wins.
- **Gate / protocol:** 180° rotational plus intended horizontal/vertical symmetry, full
connectivity, no blocked spawn/hill, and documented maximum lane spans; close/long-range roster
strata follow §2.2.
- **Dependencies / conflicts:** Conflicts more cover vs open rotations. Pogo and Shotgunner may gain
from dense cover; Sniper and Smoke may gain from openness.
- **Owners / files / rollback:** Level + balance; alternate layout snapshots. Current 18-wall set is
immutable control.

#### EXP-E04 — Fixed-six-cell hill shape

- [ ] **Select EXP-E04**

**Status:** Viable experiment

- **Details:** Keep exactly six objective cells and the same center, but rearrange them into the
current rectangle, a diagonal split, or a vertical diamond. This changes approach angles and how
many hill cells one ability covers without changing total scoring capacity.
- **Problem / defense:** Shape controls approach angles and ability coverage independently of total
hill capacity. Shape should be tested before size.
- **Arms / primary variable:** Current 3 × 2 rectangle; six-cell diagonal split
`{(5,4),(6,4),(7,4),(7,5),(8,5),(9,5)}`; six-cell vertical diamond
`{(7,3),(6,4),(7,4),(7,5),(8,5),(7,6)}`, subject to wall validation.
- **Controls & evidence:** Exactly six cells, same centroid, walls/spawns/scoring fixed.
- **Metrics:** Contest rate, units committed, control flips, ability coverage percentage, first
control, and rounds to win.
- **Gate / protocol:** In-bounds, rotational symmetry, connectivity/reachability, no walls, and
maximum hill cells covered by one Smoke/Grenade/Area Lock; paired KOTH seed blocks follow §2.2.
- **Dependencies / conflicts:** Precedes hill-size changes.
- **Owners / files / rollback:** Level + balance; hill profile in `GameLoop.cs`. Restore 3 × 2 set.

#### EXP-E05 — Hill size

- [ ] **Select EXP-E05**

**Status:** Deferred until EXP-E04

- **Details:** After shape is understood, compare a tight two-cell center, the current six cells,
and a broad ten-cell ridge. A smaller hill concentrates fights; a larger one permits split
occupation and multi-angle contests but may be easier for whole teams to enter.
- **Problem / defense:** A tighter hill can make contests decisive; a wider hill can create
multi-front occupation. Size also changes ability coverage and unit capacity.
- **Arms / primary variable:** Two-cell center, six-cell control, and ten-cell 5 × 2 ridge, only
after fixed-six shape evidence.
- **Controls & evidence:** Shape family, scoring, spawns, walls, and stats fixed within a size arm.
- **Metrics:** Contest/empty rate, units on hill, single-ability coverage, control flips, and match
duration.
- **Gate / protocol:** Every cell is mirrored, reachable, and open; no single ability should
accidentally cover the entire treatment unless that is the declared hypothesis.
- **Dependencies / conflicts:** KOTH scoring and team size alter capacity; never change them in the
same run.
- **Owners / files / rollback:** Level + balance + UI; map profile. Six-cell baseline remains default.

#### EXP-E06 — Hill-approach shoulder cover

- [ ] **Select EXP-E06**

**Status:** Viable experiment

- **Details:** Relocate the same four near-hill walls to sit one, two, or three Manhattan cells from
the objective. Close shoulders provide immediate staging cover; deep shoulders create a longer,
more exposed commitment lane while the total 18-wall budget stays fixed.
- **Problem / defense:** Current hill rows are open, turning entry into a shooting gallery.
Symmetric shoulder cover can create a readable staging/commit decision.
- **Arms / primary variable:** Replace only the current four-cell shoulder set
`{(5,3),(9,3),(5,6),(9,6)}`. Test Manhattan distance 1
`{(5,4),(9,4),(5,5),(9,5)}`, distance 2 control (current set), and distance 3
`{(4,3),(10,3),(4,6),(10,6)}` while preserving the other 14 wall cells.
- **Controls & evidence:** Fixed total wall count through relocation for the first arm; hill shape
and sightline caps unchanged.
- **Metrics:** Shoulder occupancy/dwell, damage while crossing, cover-to-hill commitments, ability
use from cover, first blood/control, and retake rate.
- **Gate / protocol:** Verify all listed cells are open, symmetric, and at the declared minimum
Manhattan distance; audit Shield destinations, Smoke-to-hill coverage, Grenade checks, Pogo
landings, and counter-sightlines; roster strata follow §2.2.
- **Dependencies / conflicts:** Dense shoulders can over-favor range-2 Shotgunner or jumping Pogo.
- **Owners / files / rollback:** Level + balance; layout profile. Restore current near-hill walls.

#### EXP-E07 — Maximum sightline caps

- [ ] **Select EXP-E07**

**Status:** Viable for 7/9; five-cell arm deferred as a density confound

- **Details:** Rearrange 18 walls so uninterrupted lanes top out around seven or nine cells, while
retaining at least one deliberate ranged lane. A five-cell cap would require extra walls and is
therefore a separate dense-map experiment rather than a clean relocation.
- **Problem / defense:** Full-length rows/columns amplify Area Lock and long-range acquisition.
Capped but readable power lanes may preserve ranged identity with more counterplay.
- **Arms / primary variable:** Maximum uninterrupted sightline **7 / 9 cells** through fixed-18 wall
relocation. A five-cell cap is a separate structural arm requiring at least 20 walls before even
satisfying horizontal rows, so it cannot be presented as fixed-density relocation.
- **Controls & evidence:** Wall count/path distance are fixed for 7/9. The deferred five-cell arm
must declare added density/connectivity as confounds.
- **Metrics:** Shots/kills at 0–2, 3–4, 5, and 6–7 cells; Area Lock effective length; route use;
corner fights; first blood and match pacing.
- **Gate / protocol:** Compute maximum row/column spans and representative Area Lock beam ends;
ranged/close roster strata follow §2.2. Reject any layout whose claimed cap is geometrically
impossible at its declared wall count.
- **Dependencies / conflicts:** Conflicts Sniper power lanes vs cover safety.
- **Owners / files / rollback:** Level + balance; layout profile. Restore current spans.

#### EXP-E08 — Pogo turn-one reach safety

- [ ] **Select EXP-E08**

**Status:** Viable experiment

- **Details:** Keep Pogo’s round-one hill landing legal but alter nearby wall placement so defenders
have zero, one, or two immediate counter-sightlines onto it. This tests whether fast objective
access is fair when it carries a visible positional risk.
- **Problem / defense:** Pogo can reach the hill on round one. The design question is whether that is
a risky mobility edge or a free objective.
- **Arms / primary variable:** Keep the same range-5 hill landing and relocate walls at fixed count
to expose that landing to **0 / 1 / 2** defender round-one counter-sightlines.
- **Controls & evidence:** Spawns, hill, Pogo stats, score, and non-Pogo shortest path distances stay
fixed. Moving the hill/spawn beyond range 5 is removed from this map arm; jump-range 4/5/6 belongs
to EXP-D03.
- **Metrics:** Turn-one hill entries, walk-vs-jump choice, survival through next exchange, first
control, backstabs, and Pogo roster wins.
- **Gate / protocol:** Compute walk/direct-jump sets and all three non-Pogo shortest paths; each
profile must preserve those paths and produce its declared defender counter-sightline count.
- **Dependencies / conflicts:** EXP-C05 distinguishes geometry from telegraphed counterplay.
- **Owners / files / rollback:** Level + balance; spawn/layout profile. Restore baseline geometry.

### Bot behavior experiments

#### EXP-F01 — Role-aware Elimination positioning

- [ ] **Select EXP-F01**

**Status:** Viable experiment

- **Details:** Make each bot seek an endpoint suited to its weapon instead of simply closing on the
nearest sighting: Shotgunner closes, Soldier holds mid-range, and Sniper preserves a lane. This
makes bot balance samples exercise the intended range identities.
- **Problem / defense:** All classes chase nearest sightings and tend to stop adjacent, erasing
Sniper/Shotgunner range identities and distorting balance samples.
- **Arms / primary variable:** Current shortest pursuit; preferred effective-range/LoS endpoints;
then a separate visible-lethal-lane penalty.
- **Controls & evidence:** Only visible/remembered legal observations; no hidden-state prediction.
- **Metrics:** Effective-range occupancy, confirmed lethal exposure, first volley, damage, survival,
and planner hashes.
- **Gate / protocol:** Deterministic role scenarios and hidden-state invariance; paired self-play
seed blocks follow §2.2.
- **Dependencies / conflicts:** EXP-A04. Human difficulty/readability still requires humans.
- **Owners / files / rollback:** Bot AI; `BotPlayer.cs` and deterministic tactics tests. Disable
score terms to restore pursuit.

#### EXP-F02 — Focus-fire commitment cap

- [ ] **Select EXP-F02**

**Status:** Viable experiment

- **Details:** Let the bot assign one, two, or three attackers to the same visible enemy before
choosing movement endpoints. The cap tests whether coordinated kill conversion improves tactics
or creates wasteful overkill and predictable squad behavior.
- **Problem / defense:** Independent nearest-target behavior cannot intentionally convert damage to
kills or avoid overkill.
- **Arms / primary variable:** Commit at most **1 / 2 / 3** attackers to a visible target; current
independent behavior is control.
- **Controls & evidence:** Use current HP only if design rules make it visible; otherwise use public
max HP and identity.
- **Metrics:** Kill conversion, damage past lethal threshold, target switches, exchange ratio,
survivors, and win rate.
- **Gate / protocol:** Endpoint choices make the assigned visible target the expected auto-fire
target without adding manual aim; test 1/2/3 living allies.
- **Dependencies / conflicts:** EXP-A04 and an enemy-HP visibility ruling.
- **Owners / files / rollback:** Bot AI + gameplay review; `BotPlayer.cs`. Remove squad assignments.

#### EXP-F03 — Per-class ability conservation

- [ ] **Select EXP-F03**

**Status:** Viable experiment

- **Details:** Keep the bot’s existing class-specific legality and Smoke/Shield/Pogo logic, but
require a minimum projected value before spending a one-use ability. Higher thresholds make bots
hold charges for stronger damage, prevention, positioning, or objective opportunities.
- **Problem / defense:** The current selector is already class-aware: it has dedicated Smoke
scoring, Shield engagement checks, Pogo hill-abandon checks, and shape-based ordering. It still
returns the first eligible candidate in that ordering without a shared projected-value threshold.
- **Arms / primary variable:** Current class-aware heuristic control versus the same heuristic plus
a threshold of **0 / 0.5 / 1 projected unit value**. A later arm may compare revised per-class
value functions with the selected threshold fixed.
- **Controls & evidence:** No ability/stat/charge changes. Include Commander and Pogo in mirrored
rosters.
- **Metrics:** Use round, unused charge at death, damage/prevention, friendly shots denied by Smoke,
survival/objective swing, and win rate.
- **Gate / protocol:** Preserve current Smoke/Shield/Pogo legal checks; no hidden-plan read, no
second use, deterministic ties; per-class seed blocks follow §2.2.
- **Dependencies / conflicts:** EXP-A04. Bot cleverness does not prove human fairness.
- **Owners / files / rollback:** Bot AI + balance; `BotPlayer.TryChooseAbility`. Remove the projected
threshold/value layer to restore the current class-aware selector.

#### EXP-F04 — KOTH squad allocation

- [x] **Select EXP-F04**

**Status:** Viable experiment

- **Details:** Assign only one, three, or all five bot units to occupy/contest the hill while the rest
stage, flank, or cover approaches. A separate match-point rule can trigger an all-in response when
the opponent is one control step from victory.
- **Problem / defense:** Every bot unit rushes/holds the hill regardless of controller, streak,
occupancy, threats, or whether one occupant is sufficient.
- **Arms / primary variable:** Hill quota **1 / 2 / 3**; separately test all-in only when the public
enemy streak reaches match point.
- **Controls & evidence:** Hill geometry/scoring fixed; decisions use only public hill state and
legal sightings.
- **Metrics:** Control rounds, contest-before-loss, streak resets, clustering, damage, deaths,
surviving units, and objective wins.
- **Gate / protocol:** Empty/friendly/contested/enemy-match-point states produce declared quotas;
unseen enemy changes cannot alter assignments.
- **Dependencies / conflicts:** EXP-A04; team size fixed.
- **Owners / files / rollback:** Bot AI; `BotPlayer.cs`. Return all units to the current hill targets.

#### EXP-F05 — Fog-legal memory horizon

- [x] **Select EXP-F05**

**Status:** Viable experiment

- **Details:** Allow bots to remember only legally observed enemies, clear memory when a searched
cell is empty, and expire stale sightings after two or four rounds. This replaces endless pursuit
of old information without granting knowledge a human player would not have.
- **Problem / defense:** Bots can chase stale sightings indefinitely yet fail to retain some legally
public reveal information.
- **Arms / primary variable:** Current memory; public-event ingestion; then separate expiry horizons
**2 / 4 rounds**.
- **Controls & evidence:** Event ingestion and expiry never change in the same arm. Non-line hidden
caster positions remain unknown.
- **Metrics:** False-pursuit rounds, cells explored, reacquisition latency, first contact, and
survival.
- **Gate / protocol:** Identical legal observations with different hidden positions produce
identical plans; observing the remembered cell empty clears immediately.
- **Dependencies / conflicts:** Public cue disclosure matrix.
- **Owners / files / rollback:** Bot AI + gameplay; `BotPlayer.cs`, reveal adapters. Restore current
memory.

#### EXP-F06 — Time-aware friendly route reservations

- [ ] **Select EXP-F06**

**Status:** Viable experiment

- **Details:** Compare reserving every cell a teammate will traverse, reserving destinations only,
and reserving cell/edge occupancy by execution time. Time-aware reservations permit safe
crossings while preventing same-time collisions and artificial detours.
- **Problem / defense:** Reserving every cell of earlier routes prevents crossings but overconstrains
later units and gives stable-ID order tactical weight.
- **Arms / primary variable:** Full-route reservation; destination-only; time-indexed cell-and-edge
reservation.
- **Controls & evidence:** Destination scoring and enemy uncertainty remain unchanged.
- **Metrics:** Same-cell intervals, edge swaps, route failures, detours, stay-put rate, displacement,
collision contacts, and planner cost.
- **Gate / protocol:** One/two/three allies, mixed speeds, input-order permutation, and 180° mirror
tests before runtime.
- **Dependencies / conflicts:** Human occupancy rule is separate; do not infer it from bot policy.
- **Owners / files / rollback:** Bot AI + gameplay review; `BotPlayer.cs`, `GridSystem.cs`. Restore
full-route `HashSet`.

#### EXP-F07 — Lethality-aware bot dodge

- [x] **Select EXP-F07**

**Status:** Viable experiment

- **Details:** Make a bot compare expected damage avoided with the cost of leaving the hill,
abandoning position, or canceling its own ability. It may deliberately keep an order against a
nonlethal threat and prioritize escape when the hit would kill.
- **Problem / defense:** Bots instantly maximize geometric clearance without weighing HP, expected
damage, hill loss, or cancellation of their own one-use action, overstating counterplay.
- **Arms / primary variable:** Clearance-first control versus expected damage avoided minus tactical
cost weights **0 / 0.5 / 1**.
- **Controls & evidence:** Only public activation data and own-team state; no hidden plans.
- **Metrics:** Damage avoided, deaths, distance moved, hill abandonment, ability cancellations,
legal submissions, and perfect-escape rate by threat.
- **Gate / protocol:** Lethal and nonlethal/match-point scenarios follow the configured weight with
stable ties; mirrored seed blocks follow §2.2.
- **Dependencies / conflicts:** EXP-C04 dodge budgets. Human response distributions remain
mandatory before tuning ability damage/radius.
- **Owners / files / rollback:** Bot AI + balance; `BotPlayer.cs`. Restore clearance ordering.

### UI, tactical readability, art, and VFX

#### EXP-G01 — Per-unit order completeness

- [ ] **Select EXP-G01**

**Status:** Viable experiment

- **Details:** Put a live summary on each unit card—holding, moving a specific number of cells,
waiting for an ability target, or locked in. Players can spot an unfinished order before
submitting instead of discovering an accidental hold during execution.
- **Problem / defense:** Stay-put defaults, completed moves, and an ability still awaiting a target
are not clearly distinguished. Players can submit accidental holds or incomplete abilities.
- **Arms / primary variable:** Per-card `HOLD / MOVE n / TARGET NEEDED / ABILITY LOCKED`; versus a
restrained aggregate ready count with badges only on exceptions.
- **Controls & evidence:** Planning rules and deadline unchanged; local plans only.
- **Metrics:** Time to identify unresolved unit, accidental holds, incomplete targets, wrong-unit
edits, and total planning time.
- **Gate / protocol:** Deterministic state-to-label tests; ≥90% human identification within two
seconds; screenshots at normal/compact sizes.
- **Dependencies / conflicts:** Rich per-card detail vs restrained exceptions.
- **Owners / files / rollback:** UI + gameplay; `UnitCard.uxml`, `UnitCardElement.cs`,
`PlanMovement.cs`. Remove added state projection.

#### EXP-G02 — Ability cost and charge clarity

- [ ] **Select EXP-G02**

**Status:** Viable experiment

- **Details:** When an ability is selected, show its remaining charges, range/shape, whether it
offers a dodge, and the fact that the caster will not make its normal move. The experiment
compares a compact warning with a richer selected-card detail strip.
- **Problem / defense:** Cards show remaining use but do not make the immediate cost—no normal move
this round—or the target shape/dodgeability obvious at the decision point.
- **Arms / primary variable:** Compact “stays this round” consequence; then a selected-card detail
strip for range, shape, and response; current card as control.
- **Controls & evidence:** Ability rules and counts fixed; exact values come from authoritative data,
not prose.
- **Metrics:** Charge-scope comprehension, unexpected movement cancellations, spent-ability
attempts, and decision time.
- **Gate / protocol:** 0/1/2-use card states, mode transitions, long labels, and responsive
screenshots; ≥90% correct comprehension.
- **Dependencies / conflicts:** Supports charge experiments without adding bookkeeping.
- **Owners / files / rollback:** UI; `UnitCard.uxml`, `UnitCardElement.cs`, HUD USS/tests.
Presentation-only removal.

#### EXP-G03 — Planning timer context

- [ ] **Select EXP-G03**

**Status:** Viable experiment

- **Details:** Show both the current countdown and the phase’s starting budget, optionally with the
number of local units whose orders are unresolved. This explains whether “5” means a short 1v1
phase or the final seconds of a longer round without revealing enemy plans.
- **Problem / defense:** The HUD shows a rounded number without explaining the total shared window
or local unresolved workload.
- **Arms / primary variable:** Countdown + phase-start total; then add local unresolved-order count.
Server deadline stays fixed.
- **Controls & evidence:** Never infer or reveal hidden enemy unit state.
- **Metrics:** Ability to predict deadline, incomplete timeout submissions, idle time, and urgency
accessibility.
- **Gate / protocol:** Render 25/20/15/10/5 and experimental floor cases; non-color urgent cue; ≥90%
deadline/workload comprehension.
- **Dependencies / conflicts:** Planning floor/policy experiments must use identical UI across rule
arms or explicitly declare the UI arm.
- **Owners / files / rollback:** UI + gameplay; `GameHUD.uxml/.uss`, `GameHUDController.cs`,
`PlanMovement.cs`. Remove context labels.

#### EXP-G04 — Explicit dodge decision

- [ ] **Select EXP-G04**

**Status:** Viable experiment

- **Details:** Give each alerted unit one clear row showing `UNRESOLVED`, `KEEP`, or `DIVE`, plus a
warning when diving will cancel that unit’s planned ability. Overlapping threats remain aggregated
into one decision for that unit rather than creating duplicate prompts.
- **Problem / defense:** Eligible units already flash and the overlay tells players to drag flashing
units. What remains unclear is that no action keeps the original order and a submitted dive
cancels that unit’s planned ability.
- **Arms / primary variable:** Existing flashing eligible units plus compact selected-unit
consequence; versus a per-alerted-unit `UNRESOLVED / KEEP / DIVE` ledger. Multiple threats
aggregate into one row for that unit.
- **Controls & evidence:** Threat set, geometry, deadline, and server validation unchanged.
- **Metrics:** Reaction time, wrong-unit edits, valid response rate, keep/dive comprehension,
ability cancellations, and damage avoided.
- **Gate / protocol:** Every and only server-alerted unit appears once even when multiple threats
overlap; KEEP submits no replacement; DIVE requires a legal path; ≥90% predicted outcome.
- **Dependencies / conflicts:** EXP-C04 and the audio dodge cue can run as separate
factorial arms, not bundled by default.
- **Owners / files / rollback:** UI + gameplay; `GameHUD`, `PlanMovement.cs`, targeted dodge RPC
presentation. Remove dodge-only strip/ledger.

#### EXP-G05 — Fog-safe preview and battlefield semantic language

- [ ] **Select EXP-G05**

**Status:** Viable experiment

- **Details:** Standardize the board language: cool shapes for the player’s intended actions, hot
shapes for confirmed incoming danger, a distinct cell pattern for Smoke, and dark uncertainty for
fog. Richer threat envelopes may appear only for information the player is already allowed to know.
- **Problem / defense:** Own intent, incoming threat, Smoke, and unknown fog lack a consistent
provenance language; friendly attack range and enemy danger both use warm hues.
- **Arms / primary variable:** Restrained shape/pattern semantics; then richer visible-unit threat
envelopes. Reserve cool treatment for own intent and hot treatment for confirmed danger. Treat
Smoke as a temporary cell-bounded obstruction distinct from ambient fog.
- **Controls & evidence:** Two authoritative snapshots differing only in hidden enemy coordinates
must render identical previews. Non-line telegraphs never expose a hidden caster.
- **Metrics:** Classification of own preview/threat/Smoke/fog, false certainty, target errors,
reaction time, and clutter.
- **Gate / protocol:** Hidden-state parity oracle; normal/grayscale/color-vision screenshots;
≥90% classification within three seconds.
- **Dependencies / conflicts:** Cue disclosure matrix; protected Smoke behavior unchanged. Conflicts
rich vs restrained information.
- **Owners / files / rollback:** UI + art + VFX + multiplayer; presentation model/materials and
`GameLoop` cue seam. Disable preview layer without touching Smoke/fog authority.

#### EXP-G06 — Public round recap and damage causality

- [ ] **Select EXP-G06**

**Status:** Viable experiment

- **Details:** Between rounds, briefly summarize visible damage, deaths, abilities, and KOTH changes,
optionally showing an in-world attack direction when the source was observable. The recap must
help a player adapt without exposing hidden enemy positions or plans.
- **Problem / defense:** Execution can remove a unit before the player understands why. A brief,
fog-safe recap can turn a loss into a learnable next-round adjustment.
- **Arms / primary variable:** Compact transition line; local fireteam rows; in-world
source-to-impact/direction marker when that source was observable. No blocking modal.
- **Controls & evidence:** Public objective state and own observed outcomes only; no hidden enemy
path, HP, position, or ability choice.
- **Metrics:** Correct cause attribution, one stated adaptation, recap reading time, false hidden
attacker inference, and inter-round delay.
- **Gate / protocol:** Public/team-private payload tests; local-perspective KOTH copy; visible and
hidden-source scenarios; ≥85% valid attribution without leaks.
- **Dependencies / conflicts:** Atomic round state and cue disclosure. Audio recap is optional and
follows the visual disclosure policy.
- **Owners / files / rollback:** Gameplay + multiplayer + UI + VFX; round-summary packet, HUD strip,
local effect. Remove the packet/presentation while keeping existing match state.

#### EXP-G07 — Roster comparison and scalable command dock

- [ ] **Select EXP-G07**

**Status:** Dynamic five-unit command dock and public enemy status rail implemented; comparison
treatment remains viable

- **Details:** Replace incomparable flavor prose with side-by-side mobility, durability, reach,
ability shape, response, and charge information. The command dock now generates its card count from
`RosterRules.UnitsPerPlayer` and scrolls horizontally when the full fireteam does not fit. The
read-only enemy rail uses the same roster count and keeps identity, current/max HP, alive state, and
remaining or spent match ability uses public without carrying positions or orders.
- **Problem / defense:** Character selection uses prose rather than comparable mobility,
durability, reach, shape, response, and charge information. The former fixed-card structure is no
longer a blocker for team-size experiments.
- **Arms / primary variable:** Normalized role bands versus exact public values; compact generated
cards versus tabs + one expanded card.
- **Controls & evidence:** Preserve the current public enemy roster/combat-status policy; positions
and planned orders stay hidden.
- **Metrics:** Pairwise stat comprehension, coherent roster rationale, selection time, skipped
orders, dock occlusion, focus order, and workload.
- **Gate / protocol:** Values derive from `UnitData` and server-authored live state; all five units
and responsive breakpoints; both generated card counts remain coupled to
`RosterRules.UnitsPerPlayer`.
- **Dependencies / conflicts:** Team-size probe for dynamic dock. Rich exact values vs restrained
role bands.
- **Owners / files / rollback:** UI + balance; character selection templates/controller and
`GameHUD`. Remove comparison rows while preserving runtime-generated roster slots.

#### EXP-G08 — Environmental and unit glanceability

- [ ] **Select EXP-G08**

**Status:** Viable experiment

- **Details:** Test clearer cover/lane materials, stronger body-level team outlines, and small class
glyphs as separate visual treatments. Players should identify walkability, allegiance, and class
at a glance without changing geometry or revealing units hidden by fog.
- **Problem / defense:** Flat wall/grid materials under-communicate lanes and cover; allegiance
relies heavily on a small foot puck; class identity depends on distant silhouettes.
- **Arms / primary variable:** Material-only cover/lane edge treatment; separately, body-level team
outline; separately, class notches/glyph on selection/hover.
- **Controls & evidence:** Geometry, collision, fog, stats, and hidden visibility unchanged.
Allegiance remains the dominant channel and never relies on hue alone.
- **Metrics:** Cover/lane tracing, ally/enemy identification, class identification, wrong-team
selection, board occlusion, GPU time, and color-vision robustness.
- **Gate / protocol:** ≥90% cover/walkability, ≥98% allegiance, ≥85% class identification; hidden
units leave no outline/glyph.
- **Dependencies / conflicts:** Existing outline feature and performance budget. Environmental cues
must not prescribe one “correct” route.
- **Owners / files / rollback:** Art + VFX + UI; map materials, outline settings, unit presentation.
Material/presentation-only rollback.

#### EXP-G09 — On-board KOTH state and rich-vs-restrained density

- [ ] **Select EXP-G09**

**Status:** Viable experiment

- **Details:** Make the hill itself communicate neutral, contested, controlled, and match-point
states through patterns and brief transitions. Compare a restrained state-only treatment with a
richer preset to learn how much feedback helps before it obscures units and telegraphs.
- **Problem / defense:** Empty and contested hill states read similarly on the board, and the 0/3
stake is small HUD text. More feedback may clarify stakes or bury central fights.
- **Arms / primary variable:** Static patterns/notches for neutral, contested, controller, and
progress; transition-only pulses; then a controlled rich-versus-restrained presentation preset.
- **Controls & evidence:** Consume only public `HillControlState`; no occupant count or identity.
Semantic information remains identical between density arms.
- **Metrics:** Controller/streak/reset comprehension, unit tracking on hill, response time, input
errors, preference, GPU time, and reduced-motion comfort.
- **Gate / protocol:** ≥95% state and ≥90% streak recognition; world/HUD state match; occupants,
Smoke, paths, and telegraphs remain legible.
- **Dependencies / conflicts:** KOTH scoring arm copy must match its active rule. Conflicts rich vs
restrained feedback.
- **Owners / files / rollback:** VFX + UI + gameplay binding; hill overlay material/state. Restore
current GroundGlow presentation.

### Audio experiments

#### EXP-H01 — Audio service and mixer foundation

- [ ] **Select EXP-H01**

**Status:** Prerequisite for audio arms

- **Details:** Build the client-side plumbing for separate Master, Music, SFX, and UI volume control,
pooled/limited sound playback, and one test event. Existing background music keeps working; this
foundation makes later gameplay cues measurable and removable.
- **Problem / defense:** `AudioManager` has serialized sources but inert service logic, and no
gameplay code emits SFX. The scene’s play-on-awake loop is the functioning static-music control.
- **Arms / primary variable:** Infrastructure: Master/Music/SFX/UI buses, bounded voice playback,
independent volume, and one proof event.
- **Controls & evidence:** Client-side presentation only; no server gameplay branch waits on audio.
- **Metrics:** Bus routing, peak voices, allocations, true peak, and event coverage.
- **Gate / protocol:** SFX bus can mute without muting music; proof cue plays once; no per-play
`Find`/allocation; music import/memory policy reviewed.
- **Dependencies / conflicts:** None. Required by H02–H05.
- **Owners / files / rollback:** Audio + gameplay hookup; `AudioManager.cs`, audio prefabs/mixer.
Disable service/event hooks and route the existing serialized play-on-awake music as it is today;
do not delete the functioning static music baseline.

#### EXP-H02 — Phase cadence and planning urgency

- [ ] **Select EXP-H02**

**Status:** Viable experiment

- **Details:** Add short non-positional cues when planning, dodge, and execution begin, then
separately test a subtle final-seconds urgency layer. These sounds communicate public timing only
and must stop at the authoritative deadline without masking more important alerts.
- **Problem / defense:** One static music loop does not communicate planning, dodge, execution, or
deadline urgency.
- **Arms / primary variable:** Existing static loop control; phase stingers; then a final-seconds
low-density urgency layer.
- **Controls & evidence:** Global non-positional public timing only; no enemy plan/unit signal.
- **Metrics:** Phase recognition, submit-time distribution, deadline alignment, idle time, and
fatigue.
- **Gate / protocol:** ≥90% phase recognition with screen obscured; urgency stops exactly at server
deadline and never masks dodge/elimination.
- **Dependencies / conflicts:** H01; planning UI is a separate arm.
- **Owners / files / rollback:** Audio + gameplay; phase events in `GameLoop.cs`. Remove event hooks.

#### EXP-H03 — Threatened-player dodge alert

- [ ] **Select EXP-H03**

**Status:** Viable experiment

- **Details:** Play one unmistakable non-positional alert only for a human whose unit is eligible to
dodge. The board still shows where the threat is; audio exists to prevent the short response
window from being missed while attention is elsewhere.
- **Problem / defense:** The only real-time counterplay window is silent. A high-priority alert can
materially change whether a distracted player responds.
- **Arms / primary variable:** Silent control versus one non-positional “threatened—dodge” cue;
threat-specific secondary cues only in a later arm.
- **Controls & evidence:** Plays only for the already-targeted threatened human; the board telegraph
carries location.
- **Metrics:** Response rate/time, missed windows, recognition, masking, and false alerts.
- **Gate / protocol:** Highest SFX priority, ducks music, never voice-stolen in a worst-case burst,
and no cue on the non-threatened client.
- **Dependencies / conflicts:** H01 and cue disclosure; can be crossed with G04 only by an explicit
factorial test.
- **Owners / files / rollback:** Audio + gameplay; targeted dodge event. Remove one cue hook.

#### EXP-H04 — Combat and ability identity

- [ ] **Select EXP-H04**

**Status:** Viable experiment

- **Details:** Test sound families separately for hits/kills, each weapon class, and each scarce
ability commitment. Cues play only when the event is already observable, helping players parse a
simultaneous exchange without turning hidden enemies into positional audio beacons.
- **Problem / defense:** Fire, hits, kills, and scarce ability commitments are almost entirely
visual, weakening cause-and-effect in simultaneous execution.
- **Arms / primary variable:** Hit vs own-loss vs enemy-elimination cues; per-class weapon timbre;
per-ability commitment signature. Each cue family is tested separately.
- **Controls & evidence:** Cues play only where the event is already observable. No positional sound
from hidden or smoke-occluded sources.
- **Metrics:** Friendly/enemy elimination recognition, hit-vs-kill, class/ability recognition,
concurrent voices, fatigue, and fog leaks.
- **Gate / protocol:** ≥90% side/elimination recognition, weapon/ability above chance, no hidden
positional cue, priority elimination > impact > muzzle.
- **Dependencies / conflicts:** H01 and a Smoke/fog occlusion policy.
- **Owners / files / rollback:** Audio + gameplay + multiplayer; fire/health/ability callbacks.
Remove local emit hooks, preserving authority.

#### EXP-H05 — KOTH escalation and sparse-vs-rich mix

- [ ] **Select EXP-H05**

**Status:** Viable experiment

- **Details:** Give public hill gain, contest, loss, and match-point progression recognizable cues,
then compare a critical-events-only mix with a richer informational mix. The experiment asks how
much state can be heard before fatigue and masking outweigh clarity.
- **Problem / defense:** Public control changes and match point have little sonic weight, while
fully sonifying every event risks masking and fatigue.
- **Arms / primary variable:** Gain/contest/loss and 1→2→3 motif; then sparse critical-only versus
rich informational mix under the same event policy.
- **Controls & evidence:** KOTH state is public; no unit location added. Density changes no event
authorization.
- **Metrics:** Control/streak recognition, missed dodge/elimination cues, peak voices, true peak,
fatigue, and preference.
- **Gate / protocol:** State cues fire once on replicated change; critical cues remain audible in
worst-case bursts; sparse retains recognition while reducing load.
- **Dependencies / conflicts:** H01/H03. Conflicts rich vs restrained audio.
- **Owners / files / rollback:** Audio + gameplay; hill-state event/mixer snapshots. Default
snapshot restores baseline.

## 4. Conflict index

These are unordered cross-references; the registry entries contain the rationale and protocol:

- more cover ↔ open rotations: EXP-E03/E06/E07;
- starting charges ↔ late recharge: EXP-C01/C02;
- role-shaped miss-allowance lethality ↔ strict normalization or the prior high-lethality rollback:
  EXP-B12;
- 4v4 ↔ 6v6: EXP-B05, with 5v5 control;
- consecutive ↔ cumulative KOTH: EXP-B08’s matched-threshold two-factor matrix;
- Pogo stat tuning ↔ added counterplay: EXP-C05/D03/D04;
- Sniper four/three/two-shot breakpoints: EXP-D05 with Area Lock held independently;
- rich ↔ restrained information/feedback: EXP-G01–G09 and H02–H05.

## 5. Dependency edges (unordered)

Dependencies indicate validity constraints, not priority:

- EXP-A01 → any quantitative outcome claim;
- EXP-A02 + EXP-A03 → EXP-A04 and any seeded production-parity comparison;
- EXP-A05 + EXP-A06 → networked authority, remote visibility, or deadline claims;
- EXP-C04 → any heterogeneous dodge-range arm; time-budget variants require their own deadline
policy experiment;
- EXP-E01 → any alternate map profile;
- EXP-H01 → EXP-H02–H05;
- EXP-B03 plus dynamic roster/spawn/UI/authority gates → EXP-B05;
- the relevant EXP-F policy → bot screening only when that policy could bias the tested mechanic;
- deterministic/authority gates → runtime samples; human rubric → claims about fun or depth.

## 6. Registry disposition reminders

- Cumulative-to-3 appears only as EXP-B08’s matched-threshold falsification cell.
- Sniper 30/40/60 are explicit four/three/two-shot EXP-D05 arms.
- Pogo mobility, range, backstab, and counterplay are isolated in EXP-C05/D03/D04.
- Reroute, new characters, and new modes remain outside this dossier.
- Charge-on-death and Elimination healing remain labeled falsification arms.
- One match or a compact mechanics fixture never supports a balance conclusion.

## 7. Evidence limitations and review checklist

Before accepting any experiment conclusion, confirm:

- the baseline and treatment differ in exactly the declared variable, or the structural probe
declares why that is impossible;
- the code/asset values were re-read at implementation time;
- all deterministic checks passed before runtime;
- results include independent-block count, primary estimand, practical-effect/harm margins, 95%
clustered interval, tie/missing-data handling, seed schedule, mode, map, spawn set, roster, slots,
sides, fog, planning policy, and topology;
- bot findings are labeled screening evidence;
- human depth claims include participant count and the §2.4 rubric;
- no result relies on stale docs, unobserved runtime behavior, or hidden-information leakage;
- protected map, Smoke, and miss-allowance balance work were not overwritten;
- rollback is small enough to remove one candidate without removing unrelated work;
- Unity is stopped after any future runtime session.

## 8. Discipline provenance

This dossier reconciles read-only audits and rebuttals from:

- game direction and production;
- gameplay design and systems balance;
- level design;
- gameplay and multiplayer engineering;
- bot AI;
- QA/playtest design;
- UI/UX;
- art direction and technical art/VFX;
- audio design.

The disciplines did not unanimously endorse every proposal. Their disagreements are preserved in the
conflict index and in each entry’s controls, dependencies, and evidence limits.

## 9. Bottom line

Battle Plan now has a coherent miss-allowance numeric prototype, not evidence that the prototype
alone solves pacing. The implementation still points to a connected structural problem: three
pieces, shrinking late-game planning, one-use abilities, objective cadence, execution-window
coupling, and incomplete tactical feedback. The catalog leaves the remaining viable directions
unranked. Its prerequisite labels and dependency edges state what evidence a later experiment would
need to support a credible conclusion.