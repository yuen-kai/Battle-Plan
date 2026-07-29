# Commander Rework Spec — "Smoke Screen"

Status: **Shipped.** The Commander is a normal roster pick with Smoke Screen, a 2-round cooldown,
and the miss-allowance combat profile. The eligibility gate this spec was written to enforce has
been lifted and its acceptance criteria are met (§17). The spec is kept as the design record for
why Smoke Screen behaves the way it does; sections describing rules (§§1–12, 14) are current, and
§§13 and 17 record what was verified.

**Economy update (July 22, 2026):** the former one-use baseline was replaced by authoritative
per-kit round cooldowns. Smoke Screen starts a **2-full-round cooldown** after a valid post-dodge
activation. Historical references to one-use charges are superseded by §§2, 10, 13, 16, and 17.

**Roster update (July 28, 2026):** crews are **five units and repeats are allowed**, so a side may
field more than one Commander. This is intended and is not treated as a balance risk (§14). The
original spec assumed three distinct units per crew; wherever that assumption appears below, five
with repeats is the current rule.

## 0. Why this exists (evidence)

Pre-rework baseline, verified against source when this spec was written:

- The Commander had **no functional ability**. Its concept scripts were fully commented out
  (`Assets/Scripts/Abilities/Reroute.cs`, `Assets/Scripts/Abilities/ActivateAbility.cs`), the
  `Commander.prefab` carried no `Ability`-derived component, and `Commander.asset` advertised a
  dead `abilityName: Reroute` with `selectAbilitySquare: 0`, `uses: 1`, and the lowest weapon
  `damage: 12`. Result: fielding it is a strictly dominated, move-only trap pick that violates
  "everyone is broken, equally."
- The Commander was nonetheless **fully selectable** while broken, which is what originally motivated
  temporary eligibility and distinct-roster gates. Both were lifted once Smoke Screen shipped.
- The original "reroute allies mid-battle" fantasy required **mid-execution replanning**
  (`Reroute.cs` paused `Time.timeScale` and reopened `PlanMovement.StartPlanning` during execution).
  That directly breaks the plan → dodge → execute cadence and is disallowed.

The converged recommendation from the completed gameplay and systems-balance audits was to **gate the
Commander and rework it as a plan-committed "Smoke Screen" support ability.** Both audits converge on
Smoke Screen as the primary proposal (with a "Rally Beacon" concept as the documented fallback). This
spec adopts Smoke Screen within the shipped strength-based cooldown economy.

## 1. Player purpose

Give the Commander a clear support identity: **temporarily deny a lethal sightline** so a team can
reposition, retreat, or break an otherwise unanswerable long-range threat (Sniper `Area Lock` /
`Grenade` lanes, fog one-shot lanes). It is the "recover a bad plan / enable a play" tool the support
role promises, delivered through positioning and prediction rather than direct damage.

Design pillars it serves: readable commitments, meaningful counterplay, comeback potential, and
"everyone is broken, equally" — the Commander trades personal damage for map control.

## 2. Fit with plan → dodge → execute

Smoke Screen must resolve **entirely from the plan-time commitment, with no mid-execution replanning**.
It slots into the existing flow used by all live abilities:

1. **Planning.** The player toggles the Commander's card from Move to Ability (`UnitCardElement`
   `MOVE` / `ABILITY` modes) and commits a target area. The target is captured from the committed
   plan exactly like other targeted abilities: `GameLoop.CollectAbilityActivations` derives the
   ability square from the plan (`square = !selectAbilitySquare || plan.Count < 2 ? plan[0] : plan[^1]`)
   and `SanitizeAbilityPlan` validates it server-side.
2. **Dodge window.** At execution start, `GameLoop.RunDodgePhase` **telegraphs** every activation to
   both clients (`ShowAbilityTelegraphClientRpc`) and grants dodge windows. Smoke is a placement, not a
   struck target, so it does **not** itself alert dodgers (`responseRange = 0`); other activations in
   the same round still telegraph and grant dodges as today.
3. **Execution.** After the dodge window, a surviving valid activation starts its server-authored
   cooldown through `identity.TryStartAbilityCooldown()`. Abilities then run via
   `unit.GetComponent<Ability>().ExecuteAbility(square, data.abilityRadius)`. Smoke's occlusion is
   applied for the round's combat window and then removed.

No new phase, no pause, no second planning pass.

## 3. Legal target, shape, range, duration

Fixed behavior (requirements):

- **Trigger:** Commander card set to Ability during planning; target committed at plan time.
- **Legal target:** a single grid cell within targeting range of the Commander's planned end cell.
  Uses the existing square-targeting path (`selectAbilitySquare = true`), bounded by `abilitySquareRange`.
- **Shape:** a compact **area** of smoke centered on the target cell (radius in cells, `abilityRadius`),
  occupying whole grid cells so occlusion is deterministic and readable. (Fallback shape: a fixed-length
  wall segment perpendicular to the caster→target line — see §14.)
- **Occlusion rule:** while active, the smoked cells **block shot line-of-sight that passes through
  them** for both teams (see §9). It does **not** block movement.
- **Duration:** bounded to the round's execution/combat window; smoke clears at end of round. It must
  **not** persist across rounds or create a permanent safe zone. Exact seconds are tunable (§12).
- **Range/radius/duration are tunables**, not fixed rules (§12). What is fixed: single committed target,
  cell-quantized area, symmetric shot-LoS blocking, movement not blocked, non-persistent.

Invalid input handling: an out-of-range or unreachable target is clamped/rejected by the existing
`SanitizeAbilityPlan` path (consistent with other abilities); if no valid target exists the activation
is dropped and the cooldown is **not** started.

## 4. Authoritative timing (resolution order)

Server-authoritative, deterministic order within a round:

1. Both plans committed → server sanitizes (`SanitizeAbilityPlan`).
2. `CollectAbilityActivations` gathers activations (including Smoke) from committed paths.
3. `RunDodgePhase` telegraphs all activations publicly; threatened units (from other abilities) get
   dodge windows; dodging cancels the dodger's own ability plan and activations are re-collected.
4. `ExecuteMoves` runs all committed/dive movement.
5. **Smoke is thrown, and applies where it lands:** the Commander pitches a canister that arcs to the
   committed cell over `Smoke.ThrowSeconds` (0.35s), and `TryRegisterSmokeFootprint` runs on impact.
   The throw is deliberately short, so in practice the cloud is present for the line-of-sight and
   target-acquisition checks that matter; the sliver of clear sight the flight leaves open is the
   accepted cost of having the cloud a player can see and the occluder that stops a shot begin at the
   same moment. Registration is open for the whole execution window rather than one synchronous
   pre-movement batch, because each Commander's screen now deploys on its own canister's impact.
6. Auto-combat resolves (units acquire nearest enemy with clear LoS and fire).
7. Smoke is removed at end of round; `Ability.ResetForRespawn()` clears any transient smoke state on
   respawn without restoring the use count.

Determinism requirement: given identical committed plans and seed, Smoke placement, occlusion, and the
resulting shots/damage must be identical regardless of `Time.timeScale`, framerate, or allies' path
lengths.

## 5. Public telegraph

- At execution start, **both players** see a telegraph of the smoke area, consistent with the existing
  telegraph model (`ShowAbilityTelegraphClientRpc`, orange area marker) — the counterplay window is the
  point, so the placement is never hidden from the opponent.
- The deployment has a visible cause: a canister (`Assets/Prefabs/Projectiles/SmokeCanister.prefab`)
  arcs from the Commander to the target cell and the cloud blooms where it lands, rather than the
  screen appearing from nowhere. Planning previews the same arc, since `Smoke.BuildPlannedPath`
  samples the throw the ability actually flies.
- When the smoke becomes active, a clearly readable **smoke volume VFX** occupies the affected cells for
  both teams. It must read as "sightline blocked here" at a glance and be visually distinct from walls
  and from the fog-of-war shroud.
- The Commander's own position is exposed by casting (it must be within `abilitySquareRange` of the
  target), consistent with §7 counterplay.

## 6. Counterplay / dodge interaction

- **Symmetric:** smoke blocks **both** teams' shots through it (§9), so it cannot be used as a one-way
  firing window. This is the primary counterplay lever.
- **Route around / push through:** because movement is not blocked, an enemy can move around the cloud,
  or push **into/through** it to short range where units on either side of the cloud (or inside it) can
  reacquire LoS and fire. Smoke rewards spacing, not a guaranteed save.
- **Punish the commitment:** the Commander must expose itself to place the smoke and has weak direct
  damage despite standard-tier HP, so a committed smoke is a readable tell the opponent can punish
  next round.
- **Dodge phase:** Smoke does not grant a dodge alert against itself (`responseRange = 0`), matching its
  nature as a placement. It does not interfere with dodge windows generated by other activations that
  round. If a Commander is itself threatened by an enemy ability that round, its normal dodge option
  applies and (per existing rules) dodging cancels the Commander's own Smoke plan.

## 7. Fog / information behavior

- Placement is public via the telegraph (both players see the committed area), so Smoke introduces no
  hidden information for the caster.
- **Smoke blocks both shot and fog line-of-sight.** A fog ray whose interior crosses an active smoke
  cell cannot reveal cells behind it; the same rule is applied authoritatively by
  `UpdateUnitVisibilityForClient` and locally by the client fog overlay. Viewer and target endpoint
  cells remain clear, matching the close-range push-through rule for shots.
- Bots must treat placement from **public telegraph + their own fog/last-known memory only**, never
  omniscient data (see §11), matching `BotPlayer`'s stated fairness constraint.

## 8. Friendly / enemy effects

- The effect is **symmetric and team-agnostic**: any shot whose line passes through an active smoke cell
  is blocked, regardless of shooter team. There is no friendly-only or enemy-only variant in v1.
- Smoke deals **no damage** and applies **no buff/debuff** to units. Its only effect is shot-LoS
  occlusion for the duration.
- It does not block movement, abilities' travel (e.g., a Pogo jump or Shield rush path), or grenade
  arcs' *placement*; only the **shot/target-acquisition line-of-sight** test is affected. (Grenade
  explosion LoS already uses the same Walls-mask raycast — see §9 for the shared mechanism.)

## 9. Implementation-neutral mechanism note (feasibility)

Shooting and damage already gate on a line-of-sight physics query against the **`Walls`** layer:

- `Shooting.lineOfSight` → projectile-radius
  `Physics.SphereCast(..., LayerMask.GetMask("Walls", enemyTeam))`, after authoritative team
  visibility is confirmed.
- `Grenade.ExplodeGrenade` and `AreaLock` LoS checks use the same `Walls` mask; `Bullet` collides with
  walls.

So Smoke can be realized by introducing a **temporary occluder that the existing shot-LoS test treats as
blocking** for the round (e.g., transient collider(s) on the same layer the raycasts already query, or
an equivalent addition to the shared LoS test) and removing it at end of round — **without permanently
mutating map geometry** (`GameLoop.wallLayout`) and without changing pathfinding
(`GridSystem.FindPath`). Engineering owns the exact structure; the requirement is only that "a shot line
crossing an active smoke cell fails the same LoS check that walls fail."

## 10. Two-round cooldown

Smoke inherits the shipped server-authored cooldown economy:

- `Commander.asset abilityCooldownRounds: 2`; every deployed Commander starts ready.
- A valid post-dodge activation starts a two-round cooldown. The activation round does not consume
  one of those full rounds: Smoke used in R1 is blocked in R2 and R3, then ready in R4.
- Invalid and dodge-cancelled plans do not start cooldown.
- KOTH casualties remain dead. The preserved generic respawn lifecycle clears transient
  coroutine/VFX state but never resets or pauses cooldown progress.

## 11. Bot candidate-generation & scoring

**Implemented.** `BotPlayer.TryChooseSmokeCenter` / `ScoreSmokeCenter` handle the support case, using
`BotSmokeAlly` records and `GameLoop.ActiveSmokeCells`; the generic offensive path in
`TryChooseAbility` still covers the other kits. The requirements the evaluator was built against:

The bot evaluator must add, for Smoke:

1. **Candidate generation (fog-bounded):** enumerate smoke cells that lie on the line between a
   threatening enemy (from current fog observation *or* last-known memory) and a vulnerable ally (its
   current or planned end cell) — i.e., placements that would block an incoming lethal shot LoS. Must use
   only the bot's legitimate knowledge, never omniscient positions.
2. **Scoring:** value ≈ expected ally damage/deaths prevented (weight high-lethality lanes: Sniper
   `Area Lock` 130, `Grenade` 80) **minus** value denied to the bot's own shots through the same cloud
   (symmetry cost) **minus** Commander self-exposure risk at the required cast position. Do not place
   smoke that blocks the bot's own winning shot.
3. **Constraints:** respect `CanUseAbility`, the one-ability-per-team-round budget, and deterministic
   tie-breaking consistent with existing helpers (`CompareCells`, index/round-robin ordering) so bot
   behavior is reproducible under a fixed seed.
4. **Fallback:** if no placement has positive value, the Commander plans a normal move (no forced,
   wasteful smoke) — the bot should not start a cooldown for a zero-value cloud.

## 12. Tunable handoffs (systems-balance owns values)

- Targeting range (`abilitySquareRange`), smoke radius/shape size (`abilityRadius` or fixed segment
  length), and duration (seconds within the combat window).
- Fog-occlusion readability and performance under overlapping screens; the v1 rule is active.
- Commander base stats: the miss-allowance profile uses a light pistol (`damage: 8`) and
  `maxHealth: 120`; keep weapon output subordinate to Smoke utility. `visionRange` remains 6.
- Ability economy knob — `abilityCooldownRounds` (the field formerly serialized as `uses`), kept as
  one server knob.
- Any self-exposure constraint (e.g., minimum cast distance) if telegraph alone proves too weak/strong.

## 13. Deterministic acceptance scenarios

Each is setup → action → observable result, scriptable via the dev harness (`DevInput` / bot sims) and
edit-mode tests; no reliance on human feel.

**Roster (current rules — the original eligibility-gate scenarios are retired):**

1. **Selectable:** the Commander appears in character select and any slot can be filled with it.
2. **Repeats accepted:** a roster is valid when it holds `RosterRules.UnitsPerPlayer` (5) eligible
   indices, including duplicates; a five-Commander roster (`[0,0,0,0,0]`) is accepted server-side.

**Reworked ability:**

3. **Blocks a lethal lane:** Sniper with clear LoS to an ally; Commander commits Smoke on the cell
   between them → during execution the Sniper cannot acquire/hit the ally (0 damage to that ally); the
   ally survives.
4. **Symmetric:** in the same setup, an ally trying to shoot the enemy **through** the smoke also fails
   to hit (no one-way firing window).
5. **Push-through counterplay:** an enemy that moves into/through the smoke to short range **can** fire
   again (LoS restored at close range / inside the cloud edge).
6. **No mid-execution replanning:** committing Smoke opens **no** planning UI during execution; the
   round resolves from the plan-time commitment; `Time.timeScale` stays 1 during execution.
7. **Public telegraph:** at execution start both clients render the smoke-area telegraph and then the
   smoke volume; neither is hidden from the opponent.
8. **Cooldown:** Commander uses Smoke in R1 → its card shows **ready in 2 rounds**; it is unavailable
   in R2 and R3, then ready in R4. If eliminated in KOTH, it remains dead while the cooldown still
   advances at ordinary round boundaries.
9. **Non-persistent:** smoke is gone at the start of the next round's combat (no lingering occluder,
   no residual collider on the `Walls`-queried LoS).
10. **Draw non-regression:** a simultaneous full-team wipe still resolves as an explicit draw
    (`GetWinnerTeamIndex` null → draw copy), unaffected by Smoke.
11. **Bot determinism:** a scripted bot fielding the Commander (test roster) places Smoke to protect a
    threatened ally, at most once per round, using only fog/last-known info, and produces identical
    placement given the same seed.
12. **Stacking is legal:** overlapping screens from multiple Commanders in one round resolve without
    residue — each cloud registers on its own canister impact and all clear at end of round.

## 14. Balance guardrails

**Multi-Commander crews are supported, not a risk.** Repeats are allowed, so a side may stack
Commanders and stagger several Smoke Screens in a round. That is an accepted, symmetric strategy:
every screen blocks both teams, costs its caster's round and self-exposure, and clears at end of
round, so stacking buys area denial by giving up damage. No per-unit repeat cap is planned.

- **No permanent safe zone:** duration is bounded to the round and each Commander carries its own
  two-round cooldown, so even a stacked crew cannot hold a lane indefinitely.
- **Symmetry enforced:** never ship an enemy-only occlusion; it must block both teams equally. This
  is what keeps stacking self-limiting — a smoke-heavy crew blinds itself just as much.
- **Parity, not oppression:** no roster's side-adjusted win rate should exceed ~60% or fall below
  ~40% (align with the audits' >60% rejection guardrail); the Commander must be neither a trap pick
  nor a must-pick.
- **Single-variable rollout:** measure Smoke on a build where the other prerequisites
  (path/FPS-invariant fire budget, per-ability counterplay) are already controlled; do not bundle
  Smoke tuning with the ability-economy A/B or Grenade-radius experiment.
- **Rollback:** if Smoke proves dominant, tune `abilityRadius`, `abilitySquareRange`, or
  `abilityCooldownRounds` on `Commander.asset` — one asset, no system changes.

## 15. Dependencies

- **Character select** (`CharacterSelectionUIController`: `BuildRosterOptions`, `SelectUnit`,
  `ConfirmSelectionServerRpc`) — the Commander is offered like any other unit; rosters are validated
  on count and index eligibility only, and repeats (including multiple Commanders) are accepted.
- **Ability activation pipeline** (`GameLoop.CollectAbilityActivations`, `SanitizeAbilityPlan`,
  `RunDodgePhase`, `ShowAbilityTelegraphClientRpc`, `StartAbilityCooldowns`, `ExecuteMoves`).
- **Shared shot-LoS mechanism** (`Shooting.lineOfSight`, `Grenade`/`AreaLock` Walls-mask raycasts,
  `Bullet`) — the occluder must be honored by this test.
- **Respawn/economy** (`Unit`, `Health.RespawnAt`, `GameLoop` respawn path, `Ability.ResetForRespawn`).
- **Bot** (`BotPlayer.TryChooseSmokeCenter` / `ScoreSmokeCenter` / `CreatePlanningContribution`) —
  support-ability evaluator.
- **Assets** (`Commander.asset`, `Commander.prefab`, `AllUnits.asset`) — carry the Smoke `Ability`
  component and the `abilityName`/targeting fields; keep each `.cs`/`.asset`/`.prefab` paired with
  its `.meta`.
- **Docs/UI copy** (`Overview.md`, `UnitCardElement`).

## 16. UI copy

- **Ability name:** rename `Commander.asset abilityName` from the dead "Reroute" to **"Smoke Screen"**;
  it surfaces in the selection card (`CharacterSelectionUIController` `unit-option-ability`) and the HUD
  unit card (`UnitCardElement`).
- **Card economy text:** `UnitCardElement.RefreshAbilityState` shows `"Smoke Screen · ready"`, then
  `"Smoke Screen · ready in 2 rounds"` / `"ready in 1 round"`; the ability rail shows `READY` or
  `COOLDOWN · 2R`.
- **Tooltip / short primer line (target copy):** "Smoke Screen — block a sightline so allies can move
  or retreat. Blocks both teams; doesn't stop movement."
- **Telegraph label (optional):** neutral "Smoke" marker; no per-perspective urgency (it is not a
  dodge-me threat).

## 17. Definition of Done — met

The gate that kept the Commander unavailable has been lifted. What it required, and what satisfies it:

- [x] **Server implementation:** authoritative `Smoke` `Ability` that commits at plan time, applies
      symmetric shot-LoS occlusion for the round via the shared LoS mechanism, resolves with no
      mid-execution replanning, starts its two-round cooldown only after dodge, and clears at end
      of round (§§2–4, 9, 10).
- [x] **VFX / telegraph:** public area telegraph at execution start, `SmokeCanister.prefab` arcing to
      the committed cell, and a fog-correct smoke volume distinct from walls and fog (§5).
- [x] **Bot evaluator:** `BotPlayer.TryChooseSmokeCenter` / `ScoreSmokeCenter` — fog-bounded candidate
      generation scored on lanes denied minus own shots lost, deterministic under seed, respecting the
      one-ability-per-round budget (§11).
- [x] **UI contract:** the Commander is roster-eligible, `abilityName` reads "Smoke Screen", and
      authoritative cooldown copy is wired (§16).
- [x] **Tests pass:** `SmokeScreenEditModeTests` covers footprint, LoS block and symmetry, and the
      Commander asset/prefab/canister wiring; `GameplayNetworkEditModeTests` and
      `CombatBalanceEditModeTests` cover roster/catalog contracts, cooldowns, and draw
      non-regression; `AbilityPathPreviewEditModeTests` covers the planning preview.

Remaining Commander work is tuning, not gating: the §14 parity band still wants win-rate evidence
from real matches, which depends on match telemetry rather than on anything in this spec.

## Assumptions

- Smoke Screen is the converged proposal (verified: both completed audits recommend gating the Commander
  and reworking it; the gameplay audit's `GAMEPLAY-02` names Smoke Screen as primary with Rally Beacon as
  fallback). If later evidence favors Rally Beacon, this spec's cadence, telegraph, and bot-fairness
  framing still apply; only §§3, 8–9 change.
- v1 blocks both **shot and fog line-of-sight** using the same interior-crossing smoke geometry (§7).
- The Commander keeps a light pistol for self-defense but is defined by utility; final stats are
  systems-balance's call (§12).
- "Eligible roster" = all five characters — Commander, Pogostick rider, Shotgunner, Sniper, Soldier —
  and a crew is five picks with repeats allowed.
