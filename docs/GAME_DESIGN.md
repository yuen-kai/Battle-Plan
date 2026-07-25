# Battle Plan — Game Design & Architecture Doc

> **This is a living document.** It is the canonical orientation doc for anyone (human or AI agent)
> working on Battle Plan. If you change gameplay, stats, networking, or architecture, **update this
> file in the same change**. Last full audit: July 2026 (against branch `Improvements`, Unity 6000.3.1f1).

---

## 1. Game Concept

Battle Plan is a **1v1 simultaneous-turn positional strategy game** ("auto-battler with drawn
movement paths"). Both players secretly plan movement paths for their five-unit squad on a shared
grid during a timed **planning phase**. When time expires, the round **executes simultaneously**:
all units follow their paths, then automatically acquire targets and shoot once they stop. A player
wins by eliminating all enemy units.

Design shorthand from the original pitch: *"Chess, but simultaneous, with guns"* / *"Frozen Synapse
but simpler, with special abilities"* / *"XCOM but multiplayer and simultaneous"* / *"Valorant but
you control big-picture movement, not the characters"*.

- **Platform target:** PC + Mobile (currently developed/tested on PC in-editor).
- **Audience:** 12–24, teens/young adults.
- **Match structure:** repeated plan → execute rounds until one team is wiped out.
- **Implemented July 2026:** planning-phase move-or-ability choice with an opponent "dodge/dive"
  response window at execution start (§2), **fog of war** (§6), Commander Smoke Screen, and the
  current combat profile (§4, §9).
- **Planned but not currently functional** (see §8): additional game modes such as escort.
  Reroute is retained only as design history; Smoke Screen is the Commander's live ability.

The original 1-sheet pitch lived in `BattlePlan 1 Sheet Pitch.txt` (deleted from working tree, in
git history) and a pitch-level roster summary lives in `Overview.md` at the repo root. **Treat both
as historical**: several stats there are stale. §4 below has the verified current numbers.

## 2. Core Gameplay Loop (as implemented)

The authoritative flow is `GameLoop.GameLoopTemp()` in `Assets/Scripts/GameManager/GameLoop.cs`,
a server-side coroutine started from `OnNetworkSpawn` on the host. Per round:

1. **Planning phase**
   - Server computes a shared deadline: `max(12s, 6s × largest living team size)`, producing
     30/24/18/12/12 seconds for teams of 5/4/3/2/1.
   - `StartPlanningClientRpc` starts `PlanMovement.StartPlanning` on every client. Each client
     drag-draws per-unit paths with the mouse (`PathSelection`): click your unit's cell to start,
     drag through adjacent cells (4-directional, no diagonals, no walls, no revisits, max
     `UnitData.moveDist` cells). Unit ability cards (bottom UI) select/switch units; clicking
     another of your units on the board also selects it.
   - Players can **Lock In** early, then unlock and continue editing while the planning window is
     open. The server derives their team from the RPC sender, sanitizes every submission, and
     accepts only the current round and commit revision. Once both teams submit, a three-second
     undo window preserves early execution without making accidental locks permanent. Unlocked
     plans still submit automatically at the deadline.
   - **Move OR ability (per unit):** clicking the already-selected unit's card toggles it into
     ability mode (card turns yellow) — an exclusive choice: an ability unit stays put this round.
     In ability mode the range overlay shows `abilitySquareRange` and a click picks the target
     square (Manhattan diamond; wall cells disallowed except for line abilities, where the square
     is a direction anchor). Self-targeted abilities (Shield) need no square. Units without a
     compiled `Ability` component cannot toggle; all five current roster units have one.
     The plan rides in `PathsDict` as `(true, [startCell, targetSquare])`.
   - At deadline each client sends its `PathsDict` via `SendPathsToServerRpc`; the server
     re-validates every path (ownership, grid-snapping, adjacency, bounds, walls, moveDist cap)
     and every ability plan (`SanitizeAbilityPlan`: component present, range, bounds, walls —
     invalid ability plans degrade to stay-put moves).
1.5. **Ability dodge phase** (start of execution, when applicable — `GameLoop.RunDodgePhase`)
   - Every activation is telegraphed to both clients (`ShowAbilityTelegraphClientRpc`:
     runtime-generated AOE disc for `abilityRadius > 0`, caster→square line for
     `responseDistLine` abilities, or a single/multi-cell square outline otherwise — Smoke's
     footprint and zero-radius point-target abilities like the Pogo Rider's Jump).
   - The server computes threatened enemies per activation: within `responseRange` cells of the
     target square (`Helper.GetObjectsInRange`), or of the line (`GetUnitsInRangeOfLine`).
     Threatened units get their `UnitCanvas/Alert` icon enabled network-wide.
   - If anyone is threatened, that client gets a dive-planning window
     (`timeDivePerUnit × alerted count`): `StartDodgePlanningClientRpc` reuses
     `PlanMovement.StartPlanning` restricted to the alerted units with `diveRange` as the step
     cap. Click an alerted unit (board selection — cards don't map here) and draw a short path.
   - A submitted dive (server-sanitized, `diveRange` cap) **replaces that unit's planned move**
     (executed at `diveSpeed` via `Movement.StartMovement(path, dive: true)`) and **cancels its
     own ability plan** if it had one. Declining to dodge keeps the original plan.
   - Dev mode: the window waits indefinitely for `DevInput.SubmitDodge()` (queue dives with
     `DevInput.SetDodgePath(team, unit, col,row, ...)`); `DevInput.Dump()` shows
     `phase=dodging` plus who may dive.
2. **Execution phase**
   - `ExecuteMoves` starts `Movement.MoveToCells` on every unit simultaneously (server-side,
     positions replicated to clients via `NetworkTransform`). Units with no submitted path stand
     still; ability units stay put; dodgers run their dive path at `diveSpeed`.
   - Ability coroutines (`Ability.ExecuteAbility`) start right after movement begins; the round
     waits for `CheckStillShooting()` **and** all abilities to finish (`runningAbilities`).
     Dodge telegraphs are cleared before movement and abilities begin.
   - When a unit finishes its path, `Movement.transitionToShooting()` flips it into
     `Shooting.InitiateShooting()`: auto-acquire the **nearest observable enemy with line of sight**
     (projectile-radius `Physics.SphereCast` against `Walls` + enemy-team layers, within
     `targetRange`), rotate to face it, then fire bullets with spread on a `timeBetweenShots`
     cadence, reloading after each magazine. Snipers additionally have a visible "target lock"
     laser that charges for `targetLockDuration` before the shot.
   - Bullets (`Bullet.cs`) are server-simulated physics projectiles; on hitting an enemy they apply
     `damage` (× `backstabMultiplier` if the impact came from behind, per `backstabAngle`), then
     despawn. `Health` replicates HP and alive-state as **NetworkVariables** (`currentHealth`,
     `isAlive`) so they survive fog hide/show; at ≤0 HP the server deactivates the unit (one frame
     after the final state flush), clients deactivate on the `isAlive` change, its friendly card is
     disabled, and the public enemy status card updates.
   - The server loop waits until no unit is `stillShooting` (each unit stops after its current
     magazine/bullets resolve once `OrderAllowShooting(false)` is issued), then starts the next
     planning phase.
   - Weapons are held free while anyone is still moving, so a unit that walks into a rifle is shot
     at the whole way in. An ability that sets its caster down arrives all at once and can do so
     after the last walker has stopped, so a landing also holds them free for
     `GameLoop.AbilityLandingReturnFireSeconds` (`Pogo` calls `HoldReturnFireWindow` as it comes
     down) — otherwise the rider would empty a magazine into units already ordered to cease fire.
3. **Win condition** — the round loop runs `while` both teams have ≥1 active unit
   (`teamSize(team) > 0`, tag-based). When it exits, `EndGame` → `EndGameClientRpc` shows
   "You win!/You lose!" plus *Play Again* (both players must accept; reloads `HomeScreen`) and
   *Main Menu* (disconnects; loads `Title Screen`).

**Design history:** abilities were originally meant to activate mid-execution (the commented-out
`ActivateAbility.cs` pipeline). That was reworked (July 2026) into the implemented model above:
the move-or-ability choice happens **during planning**, and the enemy dodge/response window runs
**at the start of execution**. `ActivateAbility.cs` remains commented out as a historical
reference; the live pipeline is in `GameLoop` (`RunDodgePhase`, `CollectAbilityActivations`,
`SanitizeAbilityPlan`, `RunAbility`) and `PlanMovement.AbilitySelection`. `Reroute.cs` remains
disabled history because a mid-execution re-plan conflicts with plan-then-execute; the Commander
now uses Smoke Screen.

## 3. Grid & Coordinate System

- Logical grid of **15 × 10 cells** (columns x = 0..14, rows y = 0..9), origin at the
  **bottom-left**. The July 2026 horizontal expansion keeps the 10-row engagement depth while
  creating distinct left, center, and right lanes: center-to-edge is now seven cells, so flanks
  and rotations require real commitments instead of every unit covering most of the board.
- `GameLoop.cellSize = 2.7` world units per cell. `GameLoop.gridBounds` is a `Rect` from (0,0) to
  `(14,9) * cellSize` (+0.1 slack), used for bounds checks.
- Conversions in `GridSystem`: `GetNearestGridCell(Vector3)` snaps world → cell-center world pos
  (y=0); `ConvertToGridCoords(Vector3)` → integer `Vector2Int`; `GameLoop.gridCoordToWorld` inverse.
- **Walls:** `GameLoop.wallLayout` is a static `HashSet<Vector2Int>` of 18 sparse cover cells,
  mirrored across column 7 and the map's horizontal center line. The layout breaks major
  cross-map lanes and sightlines without sealing any region. Wall prefabs are
  network-spawned there at match start and block movement (client path validation), shooting
  line-of-sight (`Walls` physics layer), and grenade splash. Movement still treats each wall as a
  full cell, while its convex octagonal physics collider trims 0.4 world units from each corner.
  This lets a bullet-sized projectile diagonally peek one corner without weakening straight faces;
  the same physical corner tolerance applies to Grenade and Area Lock rays.
- **Spawns:** production spawns are generated from `RosterRules.UnitsPerPlayer`, distributed across
  the back rank with a two-column edge inset when space permits (blue row 0, red row 9).
  `GameLoop.TESTING = true` switches to compact mid-map anchors plus generated fallback cells so
  automated combat still starts immediately at any supported roster size.
- Movement is 4-directional cell-to-cell; distances in unit stats are in **cells** (ranges are
  multiplied by `cellSize` where world units are needed).
- `GridSystem.DisplayGridRange(pos, dist, prefab)` instantiates a Manhattan-diamond overlay of
  prefab tiles (used for the move-range overlay; a reusable pattern for any tile-based overlay).

## 4. Unit Roster — verified current stats

Source of truth: `Assets/UnitStats/*.asset` (ScriptableObjects of `UnitData`), collected in
`Assets/UnitStats/AllUnits.asset` (`UnitDatabase`). **Roster index order matters** (character
select and TESTING auto-assign use it): **0 = Commander, 1 = PogoRider, 2 = Shotgunner,
3 = Sniper, 4 = Soldier**. The active TESTING rosters contain all five units in scenario-specific
orders; the configured roster size comes only from `RosterRules.UnitsPerPlayer`.

| Stat | Commander | PogoRider | Shotgunner | Sniper | Soldier |
|---|---|---|---|---|---|
| Role (pitch) | Support | Mobility/flanker | Initiator/tank | Area control | All-rounder |
| **Damage/bullet** | **8** | **12** | **8** (per pellet) | **50** | **10** |
| Max health | **120** | **120** | **160** | **80** | **120** |
| Magazine | 5 | 3 | 10 (one burst) | 1 | 5 |
| Time between shots (s) | 0.3 | 0.5 | 0.01 | 0 | 0.3 |
| Reload (s) | 2 | 1 | 2 | 2.5 | 2 |
| Spread (±deg) | 3 | 3 | 25 | 0.5 | 3 |
| Bullet speed (cells/s) | 3 | 5 | 2 | 15 | 3 |
| Target range (cells) | 5 | 7 | 2 | 5 | 4 |
| **Vision range (cells)** | **6** | **7** | **3** | **7** | **5** |
| Bullet range (cells) | 7 | 7 | 3 | 14 | 8 |
| Move dist (cells) | 3 | 5 | 3 | 3 | 3 |
| Move speed (cells/s) | 1 | 2 | 1 | 1 | 1 |
| Backstab multiplier | 1 | **2** | 1 | 1 | 1 |
| Target lock (s) | 0 | 0 | 0 | **2** (+0.3 find) | 0 |
| Ability | Smoke Screen | Pogo (jump) | Shield Rush | Area Lock | Grenade |
| Ability cooldown (full rounds) | 2 | 2 | 2 | 4 | 3 |

Notes (miss-allowance profile, July 2026 — see §9 for rationale and tuning levers):
- Vision ranges are Manhattan distances used by fog of war (§6); every unit's `visionRange` is
  at least its `targetRange`, and authoritative target acquisition additionally rejects enemies
  outside the team's visible cells.
- The Shotgunner's magazine of 10 with 0.01s between shots effectively fires a **10-pellet burst**
  (up to 80 landed damage point-blank), spread ±25°, then reloads 2s. A 120 HP standard target
  requires 15 landing pellets, or 1.5 perfect bursts; the provisional miss allowance raises that
  to 2.5 fired-burst equivalents.
- Sniper: 1-round magazine, 2s visible lock-on laser before each shot, 2.5s reload — 50 damage
  per hit (**three hits against a 120 HP standard unit; four against the Shotgunner**). The lock
  laser force-reveals the sniper to the victim's team through fog (§6).
- Clean landed-magazine equivalents vs 120 HP are Commander 3, Pogo 3.33 frontal / 1.67 backstab,
  Shotgunner 1.5 point-blank bursts, Sniper 3, and Soldier 2.4. Applying the provisional
  weapon-specific miss allowances yields 3.75 / 4 / 2 / 2.5 / 3.33 / 3 respectively.

**Ability behavior as implemented** (server-side `Ability` subclasses in `Assets/Scripts/Abilities/`,
invoked by the planning-phase move-or-ability pipeline in `GameLoop` — see §2):
- Every ability starts ready. A valid activation starts its configured cooldown only after the
  dodge phase, so a dodge-cancelled or invalid plan does not pay the cost. Cooldown `N` blocks the
  next `N` complete rounds and becomes ready for planning in the following round. The server ticks
  all other positive cooldowns once at round end; death does not reset or pause them.
- `Grenade` (Soldier): lobbed arc to a chosen square over 1s, AoE (`abilityRadius` 1.6 cells) with
  wall-blocked splash, flat **80** damage (serialized on the component/`Soldier.prefab`, not from
  UnitData). A unit at the blast center can clear every current unit collider with a straight
  two-cell dive, but not with one step; a turned two-step path remains inside the blast. It kills
  the 80 HP Sniper exactly and leaves a full-HP standard unit at 40.
- `Shield` (Shotgunner): activates a child "Shield" object for 3s that blocks shots from the
  front. Shield state is a **NetworkVariable** so a unit revealed mid-shield renders correctly.
- `Pogo` (PogoRider): 1s ballistic jump to any square within `abilitySquareRange` 6, ignoring
  terrain (collider disabled mid-flight), then resumes shooting. Deliberately does NOT
  force-reveal through fog — jump-behind-lines is the marquee fog play.
- `AreaLock` (Sniper): snaps to nearest cell, projects a laser line toward a chosen square for up
  to 3s; the first enemy crossing the ray takes a flat **130** damage via an animated beam rush.
  The square is a direction anchor rather than a destination, so the sniper's own cell — which
  names no direction — is excluded from the range overlay and refused by client and server
  (`UnitData.CanTargetOwnCell`). Abilities that land on their square may still target their own.
  Ability damage is deliberately independent from the Sniper rifle's direct-fire damage.
  Force-reveals the caster to enemy teams for the ability window (its object-scoped ClientRpcs
  would otherwise be dropped for clients it's hidden from, and bots use the same reveal deadline
  for targeting).
- `Smoke` (Commander): creates a public 3×3 screen for the execution round. Interior crossings
  block direct shots, projectiles, Area Lock, and fog sightlines without blocking movement.

## 5. Script Architecture Map

Everything lives in `Assets/Scripts` with **no namespaces** (project convention — see
`.cursor/rules/unity-csharp-conventions.mdc`). Networked classes are NGO `NetworkBehaviour`s.

### GameManager/
| Script | Responsibility |
|---|---|
| `GameLoop.cs` | **The intentionally-monolithic central controller** (singleton `GameLoop.Instance`, server-driven). Owns: `TESTING` flag, dev-mode flags (`devMode`, `devSpeedMultiplier`, `currentPhase`), grid constants (`cellSize`, `gridBounds`, `wallLayout`), spawns, team setup (`allTeamUnits`, `allTeamUnitObjects`, tags/layers `BlueTeam`/`RedTeam`), the round loop, path + ability-plan sanitization (`SanitizePaths`, `SanitizeAbilityPlan`), the ability dodge phase (`RunDodgePhase`, telegraphs, alert icons), public enemy-card status snapshots, **fog of war** (server visibility loop with `NetworkShow/NetworkHide` + host visual suppression, `ForceReveal` table, client-local pooled 150-tile fog overlay — §6), per-team camera placement, overlay text/flash UI RPCs, end-game UI. **Prefer adding methods here over new manager classes** (project rule). |
| `DevInput.cs` | **Dev/agent no-mouse input API** (static; everything gated on `GameLoop.devMode`, which is opt-in at runtime — a human pressing Play gets the normal manual flow). `StartMatch()` turns dev mode on and hosts (the MPPM clone auto-joins via `TempMppmAutoJoin`); `SetPath`/`SetAbility`/`SetDodgePath` queue plans as grid cells; `SubmitPlans()`/`SubmitDodge()` end the (otherwise indefinite) dev planning/dodge waits; `SetSpeed` fast-forwards via `Time.timeScale`; `Dump()` snapshots phase/units/HP. Also hosts the editor-only `DevAutoHost` runner. |
| `BotPlayer.cs` | Server-only, fog-bounded planning for every unit on the bot team. The bot intentionally spends at most one unit ability per planning phase regardless of roster size; this is a match rule/balance budget, not a roster-capacity limit. |
| `DevE2ETest.cs` | **The checked-in end-to-end gameplay test** (editor-only). Menu item *Battle Plan ▸ Run End-To-End Test* (or `DevE2ETest.Run()` in Play mode) drives a full 2-client match through movement, illegal-move rejection, configured ability scenarios, dodge phases, damage/death, speed control, and a win condition, logging `[E2E][PASS/FAIL]` per step; poll `DevE2ETest.Report` for the aggregate. Screenshots land in `Assets/Screenshots/e2e/`. |
| `PlanMovement.cs` | Client-side planning-phase controller (singleton). Collects per-unit paths into a `PathsDict` (a network-serializable `Dictionary<GameObject,(bool abilityFlag, List<Vector3> path)>`), manages unit selection via cards, move-range/path visuals, countdown text, and reversible early Lock In. |
| `PathSelection.cs` | Mouse path-drawing mechanics: start/extend/undo path, client-side validation (adjacency, bounds, walls, moveDist), path node/edge visuals. |
| `GridSystem.cs` | Static grid math helpers + `DisplayGridRange` overlay instantiation. Also the pure fog vision math shared by server and clients: `HasGridLineOfSight` (supercover line vs `wallLayout`, permissive corners) and `ComputeVisibleCells` (Manhattan diamond ∩ LoS, clamped to the 15×10 board). |

### Units/
| Script | Responsibility |
|---|---|
| `UnitData.cs` | ScriptableObject holding *all* per-unit tunables (combat, movement, ability params). One asset per unit in `Assets/UnitStats/`. |
| `UnitDatabase.cs` | ScriptableObject list of `UnitData` (`AllUnits.asset`) — roster indices come from here. |
| `Unit.cs` | Per-unit team presentation plus the server-written ability cooldown counter. A valid post-dodge activation starts the configured cooldown; round-end ticks are authoritative and death/respawn does not reset it. |
| `Movement.cs` | Server-side coroutine movement along cell paths (+dive variant), rotation, hands off to `Shooting` when done. |
| `Shooting.cs` | Server-side auto-combat: nearest-enemy acquisition requires authoritative team visibility, then uses a projectile-radius `SphereCast` (Walls + enemy layer, `targetRange`) so a lock is only possible where the real bullet fits; target-lock laser (NetworkVariables replicate laser to clients; a lock **force-reveals the shooter to the victim's team** through fog), firing with spread, reload cycle, `stillShooting` handshake with GameLoop. All setup is in `OnNetworkSpawn` so laser state re-applies after a fog `NetworkShow`. |
| `Bullet.cs` | Server-side projectile: applies damage + backstab check on enemy collision, despawns on any hit / max range / 8s lifetime. Bullets are always network-visible (a tracer out of fog is an intended "you're being shot from over there" cue). |
| `Health.cs` | HP + alive tracking as server-written **NetworkVariables** (resync on fog `NetworkShow`) + world-space health bar (billboarded to team camera); health changes also ask `GameLoop` to refresh the opponent's public status card. On death the server deactivates the root one frame after the state flush (keeps tag-based `teamSize` correct), clients deactivate via the `isAlive` callback, and the friendly unit card is disabled. |
| `AnimationHandler.cs` | Thin wrapper mapping logical states ("Moving"/"Aiming"/"Shoot"/"Idle") to Animator states. |

### Abilities/
| Script | Responsibility |
|---|---|
| `BaseAbility.cs` | `abstract class Ability : NetworkBehaviour` with `ExecuteAbility(square, radius)` coroutine. |
| `Grenade/Shield/Pogo/AreaLock/Smoke.cs` | Implementations (see §4). Live; invoked by `GameLoop.RunAbility` when a unit's plan is flagged as an ability (§2). |
| `ActivateAbility.cs` | **Entirely commented out — historical.** The original mid-execution activation pipeline; superseded (July 2026) by the planning-phase choice + execution-start dodge window living in `GameLoop` (§2). |
| `Reroute.cs` | **Entirely commented out — historical.** Smoke Screen replaced the abandoned Commander re-plan. |

### Multiplayer/
| Script | Responsibility |
|---|---|
| `NetworkHandler.cs` | Host-side connection watcher: when `ConnectedClients.Count == 2`, assigns TESTING rosters and loads `Game` scene directly (TESTING), else loads `HomeScreen` (character select). **Exactly 2 clients are required to start a match; a 3rd never triggers anything.** |
| `NetworkHelper.cs` | Static/singleton NGO utilities: `Spawn` (with ownership/parenting), `Despawn` (with delay), network-wide `SetActive`, height-offset position sync, `CleanupAllNetworkObjects` before scene changes. |
| `RelayService.cs` | `RelayManager`: Unity Relay allocation/join (websocket transport) + anonymous UGS auth; used only when `GameLoop.TESTING == false`. |

### Camera/, Menus/, misc
| Script | Responsibility |
|---|---|
| `Camera/Mouse.cs` | Static mouse→world raycasts through the team camera (`GetGridCellUnderMouse`, layer-filtered object picking). |
| `Camera/CameraEffects.cs` | Camera shake + full-screen team-colored flash ClientRpcs. |
| `Camera/AudioManager.cs` | Stub (empty Start/Update). |
| `Menus/TitleScreen.cs` | Title screen buttons (play → `JoinGame` scene, settings/credits/controls panels, quit). |
| `Menus/JoinGameUIHandler.cs` | Create/Join game UI. In TESTING: bypasses Relay, plain `StartHost()`/`StartClient()` (localhost). Otherwise Relay host + join-code flow. |
| `Menus/CharacterSelection.cs` | `HomeScreen` roster pick (`RosterRules.UnitsPerPlayer` generated slots per player), server collects both, then loads `Game`. Bypassed in TESTING. |
| `Menus/CardHandler.cs`, `UnitCardContainer.cs` | Unit/ability card UI widgets (image/text/button/interactable/disabled state). |
| `Menus/QualityScript.cs`, `Uisoundeffects.cs` | Graphics quality dropdown; UI SFX. |
| `Helper.cs` | Misc statics: collider height offset, ranged tag queries, GameObject creation. Also contains a `TempMcpBootstrap` editor-only class (MCP-for-Unity bridge auto-start — dev tooling, safe to delete later). |

Scenes: `Title Screen` → `JoinGame` (has NetworkManager; host/join) → `HomeScreen` (character
select; skipped in TESTING) → `Game` (the match; contains GameManager object with
GameLoop/PlanMovement/PathSelection, grid plane, UI canvas, team camera rig).

Key prefabs: `Prefabs/Units/*` (networked unit prefabs referenced by UnitData), `Prefabs/Map/Wall`,
`Prefabs/Map/GridCell`, `Prefabs/Projectiles/*`, `Prefabs/Visuals/*` (path nodes/edges, move
overlay cell `MoveOverlayCell`, attack overlay, ability indicators), `Prefabs/UI/*` (cards).
Physics layers that matter: `BlueTeam`, `RedTeam`, `Walls`, `Grid`, `PathNode`, `Projectile`,
`BlueShield`, `RedShield`.

## 6. Networking & Session Architecture

- **Stack:** Netcode for GameObjects 2.7.0, client-hosted (host = server + player 1). Transport is
  UTP; in production mode traffic goes through **Unity Relay** with WebSockets (`RelayManager`),
  with anonymous Unity Services auth and a 6-character join code. In `GameLoop.TESTING` mode Relay
  is bypassed entirely — direct `StartHost()`/`StartClient()` on localhost.
- **Authority model:** server-authoritative. All gameplay simulation (movement, targeting, bullets,
  damage, abilities) runs only on the server; most unit scripts literally disable themselves on
  non-server peers (`enabled = false`). Clients render replicated state, draw plans, and submit
  paths via `ServerRpc`, which the server re-validates. UI updates flow down via `ClientRpc`s.
- **Match start requires exactly 2 connected clients** (`NetworkHandler.OnClientConnected`).
  The host alone sits waiting; the match auto-starts the moment client #2 connects.
- **`GameLoop.TESTING` (compile-time const, currently `true`) changes:**
  1. Join flow: no Relay/join codes — Create Game = local host, Join Game = local client.
  2. Character select skipped: complete five-unit rosters are auto-assigned in scenario-specific
     orders and `Game` loads immediately at 2 connections.
  3. Spawns: mid-map, adjacent (combat from round 1) instead of opposite back ranks.
  4. Planning waits indefinitely for explicit `DevInput.SubmitPlans()` so automation is not
     wall-clock limited; normal play uses the 12–30 second adaptive deadline and early Lock In.
- **Per-team view:** each client's camera rig (`teamCameraParent`) is positioned by
  `InitializeCameraPositionClientRpc` to one of two fixed poses — blue looks north, red looks
  south, so each player sees the board from their own side.
- **Fog of war (implemented July 2026, spec in `docs/design/FogOfWar-and-DamageRebalance.md`,
  as-applied plan in `docs/design/FogOfWar-ImplementationPlan.md`):**
  - **Vision model:** per-unit Manhattan `visionRange` (§4) ∩ grid line-of-sight over
    `wallLayout` (`GridSystem.HasGridLineOfSight`, permissive corners), unioned per team from
    current authoritative positions. Terrain/walls are always visible; only units and their
    effects are hidden. Dead units contribute no vision.
  - **Authority:** a server loop in `GameLoop` (0.15s tick, skips work when no unit changed cell)
    calls `NetworkObject.NetworkShow/NetworkHide(clientId)` per enemy unit — a hidden enemy is
    absent from that client's spawn table entirely (no transform or NetworkVariable traffic).
    **Host special case:** NGO can't hide from the server, so the host's view suppresses
    presentation only (`Renderer.forceRenderingOff` + `UnitCanvas` canvas toggle — preserves
    Shield/laser/variant renderer states); host memory inherently has all data (accepted for
    client-hosted).
  - **Client overlay:** each client darkens out-of-vision cells with a pooled 150-tile overlay
    (`Prefabs/Visuals/FogOverlayCell`, no collider, Ignore Raycast layer), computed locally from
    its own units only — zero server traffic, zero leak.
  - **Public combat status:** enemy cards always show roster identity, current/max HP, alive state,
    and authoritative ability readiness/cooldown rounds. These snapshots remain live through fog
    by design, but contain no positions or planned orders.
  - **Fog-piercing by design:** ability telegraphs + dodge alerts (the counterplay window),
    grenade projectile/explosion, bullets. **Force reveals:** sniper weapon lock (to the victim's
    team, lock + 0.5s), Area Lock (to enemy clients for the ability window). Pogo jumps do NOT
    reveal. Health/alive/Shield state are NetworkVariables so hide/show resyncs correctly
    (a unit that died while hidden never appears as a zombie).
- **How to run/test locally (verified):**
  1. Open the project in the Unity editor. The startup scene for a manual run is
     `Assets/Scenes/JoinGame.unity` (or start from `Title Screen` and click Play).
  2. You need a second client. **Multiplayer Play Mode (MPPM)** is installed for this: activate a
     virtual player (Window ▸ Multiplayer ▸ Multiplayer Play Mode, or the Players toolbar
     dropdown). Note: in Unity 6000.3 the MPPM implementation lives in the built-in
     `UnityEditor.MultiplayerModule` (`Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode`);
     the `com.unity.multiplayer.playmode` package folder itself only carries documentation.
     Virtual players share the main editor's play-mode state: entering Play in the main editor
     puts the virtual player in Play too, in the same scene.
  3. In the main editor press Play in `JoinGame` and click **Create Game** (with TESTING=true this
     starts a plain localhost host). In the virtual player's window click **Join Game**.
     The match loads automatically once both are connected.
  4. Scripted alternative (used by automated tests/agents): enter Play mode, then call
     `DevInput.StartMatch()` in the main editor (turns on `GameLoop.devMode` and hosts; the MPPM
     clone auto-joins via `TempMppmAutoJoin`). Dev mode is opt-in per session — it is never on
     when a human just presses Play. Drive rounds with `DevInput.SetPath`/`SetAbility`/
     `SubmitPlans`/`SetDodgePath`/`SubmitDodge` and observe with `DevInput.Dump()`.
- **Automated end-to-end regression test:** menu *Battle Plan ▸ Run End-To-End Test* (script:
  `Assets/Scripts/GameManager/DevE2ETest.cs`). Enters Play mode, runs a full scripted match
  (movement + illegal-move rejection + Grenade/AreaLock/Shield + dodge phases + damage/death +
  win condition), and logs `[E2E][PASS/FAIL]` per assertion; poll `DevE2ETest.Report` for the
  aggregate result. **Run this after any gameplay/networking change** (e.g. the fog-of-war and
  damage-rebalance work, §9). Requires the MPPM virtual player to be active.
- End of match: *Play Again* requires both clients' consent (server counts `PlayAgainServerRpc`
  calls, then despawns everything and reloads `HomeScreen` for a fresh loop). *Main Menu*
  disconnects that client (host shutdown despawns everything).

## 7. Guiding Design Principles

Distilled from the pitch + how the systems are built; future changes (including the fog-of-war and
damage work) should be checked against these:

1. **Positioning is the whole game.** Players command *where units go*, never their aim or trigger.
   All combat outcomes should trace back to positioning decisions: angles, cover, ranges, flanks.
   Anything that rewards twitch reflexes over planning is off-identity.
2. **Simultaneous hidden planning is the core tension.** Both players commit plans at once and
   watch them collide. Systems should protect the "read your opponent" mind-game — information you
   have during planning is a resource. (This is precisely why fog of war fits — now implemented,
   §6: it makes the *information* dimension explicit instead of giving both players perfect
   knowledge.)
3. **Punish repeated exposure, preserve counterplay.** Ordinary fire in its intended range should
   defeat a standard unit in roughly **3 ± 1 magazines after a miss allowance**. Point-blank
   specialist fire, a successful backstab, or a telegraphed cooldown-gated ability can resolve faster;
   weak frontal fire into a tank can take longer. Players get time to read an exchange, but
   repeatedly losing position remains fatal.
4. **Asymmetric roles, rock-paper-scissors ranges.** Every unit is strong in one band (Shotgunner
   ≤2 cells, Soldier mid, Sniper long) and weak elsewhere. Buffs/nerfs should sharpen these bands,
   not flatten them.
5. **Legibility over realism.** Grid cells, Manhattan ranges, visible lasers, telegraphed
   grenades — players must be able to reason about exactly what will happen. New systems (fog
   included) should be tile-crisp and rule-simple, not analog/fuzzy.
6. **Counterplay windows.** The pitch promises the opponent a chance to respond (dodge/dive) to
   abilities — now implemented as the execution-start dodge phase (§2). Don't design new
   one-sided "gotchas" with no telegraph — the Sniper's 2s lock laser is the model.
7. **Server authority, thin clients.** All rules run on the host; clients only visualize and
   submit intents. Keep it that way for anti-cheat and simplicity.

## 8. Known Issues / Rough Edges (observed July 2026)

Code-level (from reading, not speculation):
- ~~The entire ability activation pipeline is disabled.~~ **Implemented July 2026** as the
  planning-phase move-or-ability choice + execution-start dodge window (see §2). The Commander
  now uses Smoke Screen; `Reroute.cs` and `ActivateAbility.cs` remain commented-out history.
- ~~Server path sanitizer doesn't check walls.~~ Fixed: `SanitizePaths` now rejects wall cells
  server-side, matching the client's `PathSelection.ValidMove`.
- ~~`Health.TakeDamage` reads `currentHealth` immediately after `SetHealthClientRpc` (host-inline
  RPC dependency).~~ Fixed July 2026: health/alive are server-written NetworkVariables (fog
  prerequisite).
- `GameLoop.getWinner()` maps a team name to a client by dictionary insertion-order index —
  fragile but correct for the current 2-team flow.
- Per-frame `GameObject.FindGameObjectsWithTag` in `CheckStillMoving`/`CheckStillShooting`/
  `teamSize` (contradicts the project's own convention; fine at 6 units, worth caching someday).
- ~~`Shooting` uses `Start()` (not `OnNetworkSpawn`) for its server gate.~~ Fixed July 2026
  (moved to `OnNetworkSpawn` so fog `NetworkShow` re-runs setup; `enemyTeam` resolves lazily
  because team tags are assigned after spawn). `Grenade.damage` (now 80) is still serialized on
  the component/`Soldier.prefab` instead of `UnitData`.
- A re-shown (fog) unit re-runs client `OnNetworkSpawn`, but one-time setup RPCs
  (`SetGroupLayerClientRpc`) are not replayed — harmless today because clients don't rely on
  enemy team layers; would need networked team identity if that changes.
- `GridSystem.gridWidth/gridHeight` (15×10) are dead serialized fields kept in sync for Inspector
  clarity; the authoritative size comes from `GridSystem.ColumnCount/RowCount` and
  `GameLoop.gridBounds`.
- `AudioManager.cs` is an empty stub.
- Pitch/Overview.md drift: Sniper's "Target Lock" is implemented as `targetLockDuration`, only
  PogoRider has a backstab bonus, and Overview still labels the now-eligible Smoke Commander
  unavailable. §4 and the serialized assets are authoritative.

Historical verification: `DevE2ETest` passed 33/33 on July 17 2026 against the pre-fog build and
34/34 after the original fog/damage rebalance. For the July 20 miss-allowance profile, Unity
compiled with zero console errors and all **111/111 EditMode tests passed**, including three
combat-breakpoint tests, four wall-corner targeting/physics tests, and two Grenade center-escape
tests. A two-client E2E attempt was stopped before gameplay because the required MPPM virtual
player was inactive (host listening with one connected client). Area Lock's runtime 120-HP Pogo
kill and the higher-TTK 12-round chase cap therefore remain pending. Baseline
pre-rework screenshots: `Assets/Screenshots/baseline-pretest/`; historical E2E screenshots:
`Assets/Screenshots/e2e/`.

## 9. Fog of War + Combat Rebalance (updated July 2026)

The design spec (vision model, hiding mechanism, damage rationale) lives in
`docs/design/FogOfWar-and-DamageRebalance.md`; the as-applied reconciliation against the ability
rework, exact edits, and verification plan live in `docs/design/FogOfWar-ImplementationPlan.md`.
Current behavior is documented in §4 (stats), §6 (networking/fog), and §5 (code map). The earlier
one-clean-magazine profile in the historical fog documents is superseded by the July 20
miss-allowance profile.

The deterministic base metric is **landed magazine equivalents**:
`ceil(target HP / effective damage per hit) / magazine size`. A partial lethal magazine remains
fractional and overkill does not transfer. To reserve room for misses before telemetry exists, the
profile divides that result by explicit design hit-conversion allowances: Commander/Soldier 80%,
Pogo 5/6, Shotgunner 60% at intended close range, and Sniper 90%. These are provisional design
allowances, **not measured accuracy**; fired/landed outcomes must replace them after playtests.

Current health tiers are **80 glass / 120 standard / 160 tank**. Clean magazine damage is
Commander 40, Pogo 36 frontal / 72 rear, Shotgunner 80 point-blank, Sniper 50, and Soldier 50.
Against a standard target the provisional miss-adjusted equivalents are 3.75 / 4 / 2 / 2.5 /
3.33 / 3, averaging 3.1. Area Lock remains a separate flat 130-damage, four-round-cooldown
exception: it kills a standard unit but leaves the 160-HP Shotgunner at 30 HP. Grenade remains 80.

Tuning levers flagged for playtesting: observed fired-vs-landed magazine inflation by weapon,
Shotgunner pellet damage 8→7 if point-blank conversion is oppressive, Pogo backstab 2→1.5 if rear
attacks lack answerability, and execution-window reload opportunities before changing base damage.
