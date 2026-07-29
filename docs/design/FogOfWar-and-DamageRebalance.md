# Design Spec: Fog of War + Weapon Damage Rebalance

> **Status: partly shipped, partly superseded — not a source of truth.**
> **Deliverable 1 (fog of war) shipped** and its vision model is still accurate, with one exception:
> the Sniper ships at `visionRange` **7**, not the 8 proposed in §1.1. The Phase 2 enhancement
> list in §1.8 is still open work.
> **Deliverable 2 (damage rebalance) was never shipped as written** and is superseded by the
> miss-allowance profile in `docs/GAME_DESIGN.md` §4/§9. Do not implement any number below §"Deliverable 2".
> For current combat values read `Assets/UnitStats/*.asset`; for current rules read `docs/GAME_DESIGN.md`.

**Scope:** Two features for Battle-Plan (Unity 6000.3.1f1, Netcode for GameObjects, host-based 1v1).
**Author role:** Design/interpretation lead. A coding agent implements from this doc.

---

## 0. Codebase facts this design is grounded in

These were verified by reading the code; the implementer should not re-litigate them.

| Fact | Where |
|---|---|
| Grid is 15×10 cells (x: 0–14, y: 0–9), `cellSize = 2.7` | `GridSystem.ColumnCount/RowCount`, `GameLoop.cellSize`, `GameLoop.gridBounds` |
| 18 static wall cells in `GameLoop.wallLayout` (a `static HashSet<Vector2Int>`); walls never change during a match | `GameLoop.wallLayout`, spawned in `GameLoop.StartGame()` on layer `"Walls"` |
| Round loop: planning timer → `ExecuteMoves()` → shooting loop, all in `GameLoop.GameLoopTemp()` (server coroutine) | `GameLoop.GameLoopTemp()` |
| Both players plan **simultaneously**; plan path visuals are **purely local** (`Instantiate` in `PlanMovement`/`PathSelection`, never networked). Enemy plans are already invisible — fog does not need to hide them | `PlanMovement.InitializeVisuals()`, `PathSelection.AddPathSectionVisual()` |
| Unit prefab has a `NetworkTransform` → **positions replicate to every client by default**. Hiding renderers alone would still leak positions in network traffic | `Assets/Prefabs/Units/Unit.prefab` |
| The game is **host-based**: one player runs the server (`NetworkManager.StartHost()` in `JoinGameUIHandler` / `RelayService`). The server cannot be network-hidden from itself | `Assets/Scripts/Menus/JoinGameUIHandler.cs`, `Assets/Scripts/Multiplayer/RelayService.cs` |
| Health is synced by ClientRpc (`Health.SetHealthClientRpc`), **not** a `NetworkVariable`. NGO drops object-scoped RPCs for clients that cannot see the object → must be converted for fog correctness (§1.6) | `Assets/Scripts/Units/Health.cs` |
| Death = `NetworkHelper.Instance.SetActive(gameObject, false)` (a broadcast ClientRpc), not a despawn — same RPC-drop hazard | `Health.TakeDamage()` |
| Server-side auto-targeting already does physics LoS raycasts vs `"Walls"` + enemy-team layer, capped at `targetRange * cellSize` | `Shooting.lineOfSight()`, `Shooting.FindNearestEnemy()` |
| Sniper's weapon target-lock laser is rendered from `NetworkVariable`s on the sniper's own object (`isLaserEnabled`, `laserStartPos`, …) → automatically hidden/resynced with `NetworkShow/NetworkHide` | `Shooting.cs` laser NetworkVariables |
| Area Lock's telegraph is `ShowLaserClientRpc` **on the sniper's own NetworkObject** → will be dropped for a client the sniper is hidden from (§1.7) | `AreaLock.ShowLaserClientRpc()` |
| ~~The **ability activation flow is currently disabled**~~ — **no longer true.** Ability selection is live end to end: plans carry a target square, `SanitizeAbilityPlan` validates them, and `CollectAbilityActivations` / `RunDodgePhase` / `RunAbility` execute them. `ActivateAbility.cs` and `Reroute.cs` remain commented out as history and are not part of the path. The §1.7 ability–fog rules describe shipped behavior, not future behavior | `GameLoop.CollectAbilityActivations`, `SanitizeAbilityPlan`, `RunDodgePhase` |
| Overlay pattern to reuse: `GridSystem.DisplayGridRange(pos, dist, prefab)` draws a **Manhattan diamond** of instantiated tile prefabs | `GridSystem.DisplayGridRange()` |
| Per-team perspective: each client's `TeamCamera` is placed by `InitializeCameraPositionClientRpc(teamIndex)`; team index = index into `GameLoop.allTeamUnits` | `GameLoop.InitializeCameraPosition()` |
| Project convention: gameplay logic preferentially added to `GameLoop.cs`; grid math helpers live in `GridSystem.cs` | `.cursor/rules/unity-csharp-conventions.mdc` |

---

# Deliverable 1 — Fog of War

## 1.1 Vision model

**Hybrid: per-unit vision radius (Manhattan diamond) ∩ grid line-of-sight blocked by `wallLayout`, unioned per team.**

- **Radius shape: Manhattan diamond**, matching movement range and `GridSystem.DisplayGridRange`. Everything else in this game (move range, ability square range in the commented `ActivateAbility.HandleMouseClick`) is Manhattan — consistency beats a marginally rounder circle.
- **LoS: grid-based (supercover/Bresenham line over `wallLayout`)**, *not* physics raycasts. Rationale:
  - `wallLayout` is a plain static `HashSet<Vector2Int>` available identically on server and clients → deterministic, cheap, and lets clients compute their own team's vision locally with zero information leak (a client only needs its *own* units' positions, which it already has).
  - Physics raycasts vs the `"Walls"` layer would also work but couple vision to collider sizes and can't run before wall objects spawn; keep raycasts for *bullets* (existing behavior), grid-LoS for *vision*.
- **Team visibility = union** of every living friendly unit's visible cell set. A unit contributes vision from the grid cell returned by `GridSystem.ConvertToGridCoords(unit.transform.position)` (works mid-move and mid-Pogo-flight).

**LoS rule (precise):** cell `T` is visible from viewer cell `V` iff `ManhattanDist(V,T) <= visionRange` **and** no cell strictly between `V` and `T` on the supercover line is in `wallLayout`. Endpoints are excluded from the wall check (a unit adjacent to a wall sees the wall cell; units can never stand in walls — `PathSelection.ValidMove` forbids it). For the supercover line, when the ideal line passes exactly through a cell corner, treat vision as blocked only if **both** adjacent cells are walls (permissive diagonals — feels fair on a map this small).

### Per-unit vision ranges

Add `public int visionRange` to `UnitData` (new `[Header("=== VISION PARAMETERS ===")]` section) and set per asset. Constraint honored everywhere: **`visionRange >= targetRange`** so a unit never auto-fires at an enemy its own team can't see (server auto-targeting is per-unit raycast LoS which is always a subset of team vision under this constraint).

| Unit | `targetRange` (current) | **`visionRange` (new)** | Rationale |
|---|---|---|---|
| Soldier | 4 | **5** | Duelist baseline; sees slightly past his engagement range. |
| Shotgunner | 2 | **3** | Initiator with the shortest sight — must push blind into fog, which is his job; makes Shield Rush entries a real gamble for the enemy. |
| Sniper | 7 | **8** *(shipped as 7)* | Controller — the team's eyes. Manhattan sight on a 15×10 board covers one lane but not both deep flanks; wall occlusion (18 sparse cover cells across three lanes) still carves shadows to rotate through. Deliberately NOT map-wide. The shipped asset uses 7 and the Sniper's `targetRange` dropped to 5, so the vision-exceeds-reach constraint still holds. |
| PogoRider | 7 | **7** | Scout/flanker; equal to weapon range. Jump behind lines doubles as vision denial-breaking. |
| Commander | 5 | **6** | Support awareness; sees more than he can shoot so Override (when re-enabled) has information to work with. |

The map is small (max Manhattan corner-to-corner ≈ 17, spawn lines 9 rows apart), so fog's strategic value comes primarily from **wall shadows and the short-vision units**, not from vast unexplored space. These numbers are the primary tuning knob — see §3.

## 1.2 What is hidden vs revealed (defaults)

| Thing | Visible through fog? | Notes |
|---|---|---|
| Terrain, walls, grid | **Always visible** | Static map, no exploration fog. Fog only hides *units and their effects*. |
| Enemy unit models + health bars + team indicators | **Hidden** unless unit's cell is in your team's visible set | Enforced at the network layer (§1.5), not just renderers. |
| Enemy planned paths | **Never visible** (already true today — local-only visuals) | No change needed; do not network plan visuals. |
| Own units / own paths | Always visible | |
| Bullets/tracers | **Always visible** (Phase 1) | Bullets are separate spawned NetworkObjects. A tracer flying out of fog is a deliberate, readable "you're being shot from over there" cue and avoids auditing bullet visibility. Optional Phase-2 tightening in §1.8. |
| Muzzle flash / firing unit | Firing does **not** auto-reveal in Phase 1 | Tracers already leak approximate position; explicit reveal-on-fire is a Phase 2 enhancement. |
| Sniper weapon target-lock laser | Visible **iff the sniper is visible**, *except*: locking a unit **force-reveals the sniper to the target's team** for the lock duration (§1.7) | Being lit up must be dodgeable/readable — it's the stated counterplay ("must reposition to break LoS"). |
| Area Lock telegraph line | **Always visible to both teams** | The ability's design includes an enemy dodge-response window; an undodgeable invisible insta-kill line would be degenerate. The line also implicitly reveals the sniper's position — intended cost of using a map-control ultimate. |
| Grenade projectile, explosion, AoE/dodge indicators | **Always visible** | Same reasoning: the dodge-response phase exists to be reacted to. Note: you MAY throw a grenade into fogged cells (blind nade) — good anti-camping counterplay, no extra code needed. |
| Death of a hidden enemy | Revealed via kill feed side-effects only (unit card stays enabled for opponent — no change); the corpse/deactivation is seen when the cell next becomes visible | Handled correctly by the `NetworkVariable` conversion in §1.6. |

## 1.3 When visibility is computed / updated

**One rule for both phases: visibility is always derived from *current authoritative unit positions*, recomputed continuously on the server.** This resolves the simultaneous-planning tension cleanly:

- **During planning:** units are stationary, so "continuous" recomputation is static in practice. Each player sees enemy units that their team can see *from current (start-of-round) positions* — i.e. wherever enemies ended the previous round, if in vision. Enemy *plans* are never visible (already local-only). No snapshotting logic is needed; the same server loop just keeps running.
- **During execution:** units move along paths; the server loop recomputes every tick interval and enemies **pop in when entering vision and pop out when leaving**. Ambushes read exactly as intended: a Shotgunner rounding a wall corner materializes 3 cells away.
- **Recompute cadence:** server coroutine, every **0.15 s** (units move 1–2 cells/s except Pogo at 2 cells/s and dives at 4 cells/s → 0.15 s ≈ sub-cell resolution at worst case). Skip work when no unit changed grid cell since last tick (cache each unit's last `Vector2Int`).
- **No reveal linger in Phase 1** (a unit leaving vision hides immediately). Linger/ghost markers are Phase 2 (§1.8).

## 1.4 Rendering

Two independent layers:

1. **Fog tile overlay (client-local, cosmetic).** Each client darkens non-visible cells:
   - Reuse the `DisplayGridRange` pattern: a semi-transparent dark quad prefab per cell (create `fogOverlayCellPrefab`, styled like `moveOverlayCellPrefab`, alpha ≈ 0.45, y ≈ 0.15 — below plan visuals at 0.1+ but above ground; pick a y that doesn't z-fight with move overlays, e.g. 0.05, and verify in editor).
   - **Pool all 150 tiles once** at game start under a `"FogOverlay"` parent; per update just `SetActive` per cell. Do NOT instantiate/destroy per tick like `DisplayGridRange` does — this runs every tick, not once per selection.
   - The client computes its own team's visible set **locally** from its own units (found via ownership, same pattern as `PlanMovement.PopulateTeamCharacters()`) + `wallLayout` + the same static vision function. Zero server traffic, zero leak (inputs are all client-known).
2. **Enemy unit hiding (authoritative).** Enemy units not in your vision are hidden at the network layer (§1.5). Their renderers, health bars (`UnitCanvas`), and lasers disappear as a side effect of the NetworkObject being hidden — no per-renderer fiddling on remote clients.

## 1.5 Networking / authority — the core decision

**Server-authoritative per-client object visibility via NGO `NetworkObject.NetworkShow(clientId)` / `NetworkHide(clientId)`.**

Why not just disable renderers on clients? Because the Unit prefab has a `NetworkTransform`: a hidden-but-spawned enemy still streams its position to the client every tick — trivially readable by a cheater and wasted bandwidth. `NetworkHide` despawns the object *on that client only*; NGO stops replicating its transform and NetworkVariables to that client entirely. `NetworkShow` re-spawns it there with full current state (NetworkVariables resync automatically — this is why §1.6 matters).

**Server loop (in `GameLoop`, per project convention):**

```
every 0.15s while match running:
  for each team pair (viewerTeam, targetTeam), viewer != target:
    visible = ComputeTeamVisibleCells(viewerTeam)          // §1.1
    viewerClientId = key of allTeamUnits at index of viewerTeam
    for each unit in allTeamUnitObjects[targetTeamClientId]:
      if unit dead/null: skip
      shouldSee = visible.Contains(ConvertToGridCoords(unit.pos))
                  || forceRevealed(unit, viewerTeam)        // §1.7 overrides
      if viewerClientId == host's own clientId:
        SetUnitVisualsLocal(unit, shouldSee)               // renderer toggle, see below
      else if shouldSee != netObj.IsNetworkVisibleTo(viewerClientId):
        shouldSee ? netObj.NetworkShow(viewerClientId) : netObj.NetworkHide(viewerClientId)
```

**Host special case (unavoidable):** the host player's machine IS the server; NGO cannot `NetworkHide` from the server. For the host's local view, toggle `Renderer.enabled` on all child renderers **plus** the `UnitCanvas` (health bar) GameObject of enemy units. This is visual-only — the host machine inherently has all data (it *is* the authority); that's an accepted property of host-based games, not a new leak introduced by fog. Add a helper `SetUnitVisualsLocal(GameObject unit, bool visible)` in `GameLoop`. Do **not** use `SetActive(false)` on the unit root on the host — server logic (Movement/Shooting coroutines) runs on these objects.

**Initial state:** after `StartGame()` spawns units, run one visibility pass *before* the first planning phase begins so out-of-vision enemies are hidden from frame one (spawn rows are 9 apart; with the ranges in §1.1 nobody sees anybody at match start on the real spawn layout — note `TESTING=true` spawns are adjacent and will all be mutually visible, which is fine for testing).

**RPC-drop audit (things that break when an object is hidden from a client, and their fixes):** NGO does not deliver object-scoped ClientRpcs to clients for which the object is hidden, and ClientRpc-written state is NOT resynced on `NetworkShow`. Audit result:

| Call site | Risk | Required change |
|---|---|---|
| `Health.SetHealthClientRpc` / `SetHealthBarClientRpc` | Client re-shows a unit with a stale health bar forever | **Convert to `NetworkVariable<float> currentHealth`** (server-writable), update bar in `OnValueChanged` + on spawn. See §1.6. |
| `Health.TakeDamage` death path → `NetworkHelper.SetActiveClientRpc(obj, "", false)` | Client hidden at death-time re-shows a "zombie" unit | **Add `NetworkVariable<bool> isAlive`**; clients toggle the model in `OnValueChanged`/`OnNetworkSpawn`. Server still calls `NetworkHelper.Instance.SetActive` for its own side or sets the var only. See §1.6. |
| `Shooting` laser | none — already NetworkVariables, resync on show | none |
| `Shield.ToggleShieldClientRpc` | Shield toggled while hidden → stale shield visual on reveal (≤3 s window) | Low priority; acceptable Phase 1. Phase 2: `NetworkVariable<bool> shieldActive`. |
| `GameLoop.SetGroupLayerClientRpc`, `SyncHeightAdjustedPositionClientRpc` | Run once at setup, *before* the first hide pass → order the initial visibility pass **after** unit setup completes | Sequencing only; also note `NetworkTransform` re-syncs position on show anyway. |
| `AreaLock.ShowLaserClientRpc` etc. (on the sniper's object) | Telegraph invisible to a client the sniper is hidden from | **Force-reveal caster on ability start** (§1.7). |
| `NetworkHelper.SetActiveClientRpc(.., "UnitCanvas/Alert", ..)` dodge alerts (commented flow) | Alerts target the *defender's own units*, always visible to their owner; drop on the attacker's client is harmless | none |

`Unit.SetTeamIndicators` runs in `OnNetworkSpawn`, which re-fires on every `NetworkShow` → team colors self-heal. `Health.OnNetworkSpawn` re-finds the health bar → also self-heals once health is a NetworkVariable.

## 1.6 Required refactor: Health state → NetworkVariables

This is a **prerequisite** for correct `NetworkShow/NetworkHide` and should be its own commit:

- `Health.cs`: replace `private float currentHealth` + `SetHealthClientRpc` with `NetworkVariable<float> currentHealth` (default `WritePerm.Server`). Initialize on server in `OnNetworkSpawn` to `unitData.maxHealth`. Subscribe `OnValueChanged` on all clients to scale `healthFill` (same math as today). Keep `SetHealthBarClientRpc`'s max-health bar scaling by doing it locally in `OnNetworkSpawn` (every client has `unitData` on the prefab — no RPC needed at all).
- Add `NetworkVariable<bool> isAlive = new(true)`. `TakeDamage` (server): `currentHealth.Value -= damage; if <= 0 → isAlive.Value = false` plus existing server-side card-disable. All clients react to `isAlive.OnValueChanged` by deactivating the unit's visual root (and the owner disables its card as today via the existing server call — `DisableUnitCard` runs on `GameLoop.Instance` which is a scene object, always visible, so its RPCs are safe).
- `GameLoop.teamSize()` uses `FindGameObjectsWithTag`, which ignores inactive objects — verify the server still deactivates the unit GameObject server-side on death (it does today via `NetworkHelper.SetActive` running on the server too). Keep that server-side deactivation.

## 1.7 Ability & mechanic interactions (special cases)

Reminder: the activation flow is currently commented out (§0); implement these rules in the fog system now (they're cheap: a force-reveal override set) so they hold when abilities come back.

- **Force-reveal mechanism (shared):** server-side `Dictionary<GameObject, double> forceRevealUntil` in `GameLoop` (or per-team variant `Dictionary<(GameObject, ulong), double>`). The visibility loop treats a unit with `ServerTime < forceRevealUntil` as visible to the specified team(s). Public server API: `GameLoop.ForceReveal(GameObject unit, ulong toClientId, float seconds)`.
- **Sniper weapon Target Lock:** when `Shooting.InitiateShooting` on a *Sniper* begins its `targetLockDuration > 0` laser ramp against target `T`, server calls `ForceReveal(sniper, ownerOf(T), targetLockDuration + 0.5f)`. The laser NetworkVariables then replicate to the victim's client and render normally. Justification: Overview says the counterplay is "must reposition to break LoS" — impossible if you can't see the beam.
- **Area Lock:** on `AreaLock.ExecuteAbility` start, `ForceReveal(sniper, allEnemyClients, abilityTime + 1f)` **before** `ShowLaserClientRpc` fires (otherwise the RPC is dropped for hidden clients, §1.5). Telegraph stays globally visible per §1.2.
- **Grenade:** no fog changes. Grenade object is an independent NetworkObject (visible); blind-throws into fog are allowed and intended. `ExplodeGrenade` damage raycast is unaffected by fog (server sees all).
- **Pogo Jump:** no special case. Dynamic recompute (§1.3) reveals the rider while airborne through visible cells and re-hides on landing in fog. Combined with the backstab buff (§2), Jump-into-fog-behind-lines is the marquee fog play; do NOT force-reveal on landing (that's the reward).
- **Shield Rush:** no special case beyond the §1.5 stale-visual note.
- **Commander Override/Reroute:** re-planning UI is local; the rerouted allies are the commander's own (always visible to their owner). No fog interaction. When re-enabling `Reroute.cs`, ensure it doesn't broadcast ally paths (current commented code doesn't).
- **Dodge/response phase** (commented `ActivateAbility` flow): defenders move their own units — always visible to themselves. No change.
- **Auto-targeting consistency:** guaranteed by `visionRange >= targetRange` (§1.1); no change to `Shooting.FindNearestEnemy` needed. If vision ranges are later tuned below `targetRange`, add a team-visibility check there — flagged in code comment.

## 1.8 Scope & phasing

**Phase 1 (minimal, ship this):**
1. `UnitData.visionRange` + values in the five `Assets/UnitStats/*.asset` files (§1.1).
2. Static vision math in `GridSystem`: `HasGridLineOfSight(Vector2Int a, Vector2Int b)` (supercover vs `GameLoop.wallLayout`) and `ComputeVisibleCells(IEnumerable<(Vector2Int cell, int range)> viewers) → HashSet<Vector2Int>`. Pure functions, unit-testable, shared by server and client.
3. Server visibility coroutine in `GameLoop` (§1.5) incl. host renderer toggling, force-reveal table (§1.7), initial pass after `StartGame()`.
4. `Health` NetworkVariable conversion (§1.6). **Do this first.**
5. Client fog tile overlay, pooled, local-only (§1.4). New serialized field `fogOverlayCellPrefab` on `GameLoop` (it already owns overlay-ish refs and per-client UI).
6. Sniper force-reveal hooks in `Shooting` (target lock) and `AreaLock` (§1.7).

**Phase 2 (still open, in priority order):**
- **Last-known-position ghosts:** when an enemy leaves vision, leave a static translucent marker at its last seen cell until it's re-sighted or the round ends. Big usability win; client-local (client knows what it last saw).
- **Reveal linger:** 0.4 s grace before hiding a unit that left vision (reduces flicker at vision edges during execution).
- **Reveal-on-fire:** firing reveals the shooter's cell to the enemy team for 1 s (`ForceReveal` from `Shooting.FireBullet`).
- **Bullet visibility filtering:** hide bullets whose entire flight path is in fog.

**Pulled into Phase 1 and shipped:**
- `Shield` state is a `NetworkVariable<bool> shieldActive` (`Assets/Scripts/Abilities/Shield.cs`); it could not be deferred once Shield became reachable every round.
- `Health` uses `NetworkVariable<float> currentHealth` + `NetworkVariable<bool> isAlive`, replacing the fog-fragile ClientRpcs.

**Dropped:** planning-phase "threat memory" — units don't move between phases, so it adds nothing.

---

# Deliverable 2 — Weapon damage rebalance

> **Superseded — do not implement.** This deliverable aimed at "one clean magazine kills a standard
> unit." The project went the other way: `docs/GAME_DESIGN.md` §4/§9 targets **3 ± 1 magazines** with
> explicit miss allowances, so every proposed number below (Soldier 25, Shotgun 18, Sniper 110, Pogo
> 30, Commander 12, Area Lock 220) is wrong for the current build. What actually shipped:
>
> | Unit | `damage` | `targetRange` | `visionRange` | `maxHealth` |
> |---|---:|---:|---:|---:|
> | Soldier | 10 | 4 | 5 | 120 |
> | Shotgunner | 8 | 2 | 3 | 160 |
> | Sniper | 50 | 5 | 7 | 80 |
> | PogoRider | 12 (×2 backstab) | 7 | 7 | 120 |
> | Commander | 8 | 5 | 6 | 120 |
>
> Grenade deals 80 and Area Lock deals a flat 130 decoupled from the rifle. The rest of this section
> is kept only to record why the one-magazine target was rejected.

## 2.1 Values as they stood when this spec was written (now historical)

All from the `UnitData` ScriptableObjects in `Assets/UnitStats/*.asset` (fields defined in `Assets/Scripts/Units/UnitData.cs`), plus two ability constants in code:

| Unit (asset file) | `damage` | `timeBetweenShots` | `magazineSize` | `reloadTime` | `bulletSpread`° | `targetRange` | `bulletRange` | `backstabMultiplier` | `maxHealth` | `moveDist` |
|---|---|---|---|---|---|---|---|---|---|---|
| `Soldier.asset` | 10 | 0.3 | 5 | 2 | 3 | 4 | 8 | 1 | 100 | 3 |
| `Shotgunner.asset` | 10 | 0.01 | 10 | 2 | 25 | 2 | 3 | 1 | 200 | 3 |
| `Sniper.asset` | 70 | 0 | 1 | 2.5 | 0.5 | 7 | 14 | 1 | 50 | 3 (`targetLockDuration` 2) |
| `PogoRider.asset` | 15 | 0.5 | 3 | 1 | 3 | 7 | 7 | **2** | 100 | 8 |
| `Commander.asset` | 5 | 0.3 | 5 | 2 | 3 | 5 | 7 | 1 | 100 | 3 |

- Grenade: `public float damage = 50f` — hardcoded in `Assets/Scripts/Abilities/Grenade.cs` (line ~10), **not** in the asset.
- Area Lock: `float damageMultiplier = 2f` in `Assets/Scripts/Abilities/AreaLock.cs` → deals `unitData.damage × 2` = **140** currently.
- Damage application: `Bullet.OnCollisionEnter` → `Health.TakeDamage(damage [× backstabMultiplier])`.

**Current time-to-kill (all shots hitting a 100 HP target):**
- Soldier: 10 hits → 2 full magazines ≈ **4.4 s of sustained fire** (1.2 s mag + 2 s reload + 1.2 s). Exposure is barely punished.
- Shotgunner: burst of 10 pellets ≈ instant 100 → nominally 1 burst, but 25° spread means partial hits beyond point-blank → realistically 2 bursts.
- Sniper: 2 shots; cycle = 2 s lock + 2.5 s reload → **~6.5 s per kill**, 3 shots vs Shotgunner.
- Pogo: 7 frontal hits (3 magazines) / 4 backstab hits.
- Commander: 20 hits (non-threat, intended).

## 2.2 Proposed values

Design target: **being caught in the open for roughly one enemy magazine (~1 second of clean fire) kills a standard 100 HP unit.** Fog makes exposure informational; damage makes it lethal. Only `damage` fields change (plus Grenade's constant) — fire rates, ranges, magazines, and HP stay, keeping each unit's rhythm and identity intact.

| Weapon / source | Current | **Proposed** | Effect (vs 100 HP unless noted) |
|---|---|---|---|
| Soldier rifle (`Soldier.asset` `damage`) | 10 | **25** | 4 hits = kill inside one 5-round mag (~0.9 s of fire), with 1 round of miss-tolerance. TTK 4.4 s → **0.9 s**. |
| Shotgun pellet (`Shotgunner.asset` `damage`) | 10 | **18** | Full 10-pellet burst = 180: point-blank ambush deletes anything except a Shotgunner; at spread range, 6 pellets landing (realistic mid-range) still kills. Rewards the fog flank without making stray 2-pellet grazes oppressive. |
| Sniper rifle (`Sniper.asset` `damage`) | 70 | **110** | **One-shot kill** on every 100 HP unit; 2 shots vs Shotgunner (200). TTK 6.5 s → **2 s (one lock)**. The 2 s `targetLockDuration` laser (now with the §1.7 force-reveal) is the counterplay window. |
| Pogo burst (`PogoRider.asset` `damage`) | 15 | **30** | Frontal 3-round burst = 90: still *loses straight-up fights* (doesn't finish a full-HP target). Backstab (×2 = 60/shot): 2 shots kill, full backstab burst = 180. One backstab shot deletes a Sniper (50 HP). Jump-flank through fog is the intended lethal play. |
| Commander pistol (`Commander.asset` `damage`) | 5 | **12** | 9 hits; from irrelevant to "can finish wounded units", still clearly support. |
| Grenade (`Grenade.cs` `damage`) | 50 | **80** | Forces the dodge instead of being shrug-off-able; kills anything already tagged by ~1 Soldier hit. Not a one-shot on full HP — dodge phase remains meaningful, not mandatory-lethal. |
| Area Lock (`AreaLock.cs` `damageMultiplier`) | 2× → 140 | **keep 2×** → 220 | With the sniper buff it now genuinely "obliterates" (Overview's word) everything crossing the line, including a full-HP Shotgunner. No code change. |
| All `maxHealth` values | 100/200/50 | **unchanged** | HP spread (Sniper 50 / standard 100 / Shotgunner 200) is doing good identity work; changing damage alone keeps the matrix simple. |

**Resulting kill matrix (hits to kill, frontal):**

| Attacker ↓ / Target → | 100 HP (Soldier/Pogo/Cmdr) | Sniper (50) | Shotgunner (200) |
|---|---|---|---|
| Soldier (25) | 4 (0.9 s) | 2 (0.3 s) | 8 (2 mags) |
| Shotgun burst (18×10) | 1 burst ≥6 pellets | 1 burst ≥3 pellets | 2 bursts |
| Sniper (110) | **1** | **1** | 2 |
| Pogo (30 / 60 back) | 4 / 2 | 2 / 1 | 7 / 4 |
| Commander (12) | 9 | 5 | 17 |

## 2.3 Balance risks & tuning flags

1. **Sniper one-shot (highest risk).** With 8-cell vision, 7-cell range and 110 damage, any unit standing in an open sniper lane when movement stops simply dies — there is no mid-execution dodge once paths are committed. This is *intended* ("punishes poor positioning") but may feel brutal in early playtests. Fallback lever: **90 damage** (still one-shots Snipers/wounded units, two-shots 100 HP). Decide after playtest; ship 110 first since fog gives approach cover that didn't exist before.
2. **Shotgunner durability.** At 200 HP it's now the only unit that survives every single-source burst (sniper shot, shotgun burst, backstab burst). As the Initiator that's coherent, but watch for "unkillable brawler" — lever: 200 → 150 (dies to 2 sniper shots *or* one full point-blank mirror burst + 2 pellets).
3. **Pogo + fog synergy.** Jump (any distant tile, ignoring terrain) + backstab 60/shot + fog concealment is the strongest combo introduced here. Kept in check by 3-round mags and 100 HP (a spotted Pogo dies in 0.9 s to a Soldier). Watch win rates; lever: `backstabMultiplier` 2 → 1.75.
4. **Simultaneous-plan double-kills.** Higher lethality means more rounds where both players lose a unit in the crossfire. That's the tension of the genre (it rewards planning around corners/fog) — not a bug, but note it in playtest observations.
5. **Commander irrelevance.** 12 damage keeps him weak while Override is disabled (`Reroute.cs` commented out). If Override stays disabled for a while, he's a strictly worse pick; consider excluding him from the roster until then (out of scope here).
6. **Values the user may want to tune:** Sniper 110 vs 90 (risk 1), Shotgun pellet 18 (range 15–20), Grenade 80 (range 70–100), all `visionRange` values (§1.1 — especially Sniper 8 vs 7, Shotgunner 3 vs 4).

---

# 3. Implementation map (files & methods)

Ordered; steps 1–2 are prerequisites.

| # | File | Change |
|---|---|---|
| 1 | `Assets/Scripts/Units/Health.cs` | `currentHealth` → `NetworkVariable<float>`; add `NetworkVariable<bool> isAlive`; remove `SetHealthClientRpc`/`SetHealthBarClientRpc` in favor of `OnValueChanged` + local `OnNetworkSpawn` init (§1.6). |
| 2 | `Assets/Scripts/Units/UnitData.cs` | Add `public int visionRange` with tooltip + header. |
| 3 | `Assets/UnitStats/{Soldier,Shotgunner,Sniper,PogoRider,Commander}.asset` | `visionRange`: 5/3/8/7/6. `damage`: 25/18/110/30/12 (§2.2). Edit via Unity Inspector or careful YAML edit; **do not touch GUIDs/meta files**. |
| 4 | `Assets/Scripts/GameManager/GridSystem.cs` | Add `static bool HasGridLineOfSight(Vector2Int, Vector2Int)` (supercover vs `GameLoop.wallLayout`, permissive corners) and `static HashSet<Vector2Int> ComputeVisibleCells(IEnumerable<(Vector2Int, int)>)` (Manhattan diamond ∩ LoS, clamped to grid 0–14 × 0–9). |
| 5 | `Assets/Scripts/GameManager/GameLoop.cs` | Server: visibility coroutine (0.15 s tick) with `NetworkShow/NetworkHide` per enemy client + host-local `SetUnitVisualsLocal` renderer/UnitCanvas toggle; `forceRevealUntil` table + `ForceReveal(unit, clientId, seconds)`; initial pass after `StartGame()`; stop loop at `EndGame()` (re-show everything). Client: pooled 150-tile fog overlay updated from local team vision (`fogOverlayCellPrefab` serialized field); overlay active from `OnNetworkSpawn` until end-game UI. |
| 6 | `Assets/Scripts/Units/Shooting.cs` | Sniper target-lock hook: when `targetLockDuration > 0` lock begins on a new target, call `GameLoop.Instance.ForceReveal(gameObject, targetOwnerClientId, unitData.targetLockDuration + 0.5f)` (§1.7). |
| 7 | `Assets/Scripts/Abilities/AreaLock.cs` | At `ExecuteAbility` start, force-reveal caster to all enemy clients for `abilityTime + 1f` **before** `ShowLaserClientRpc` (§1.7). |
| 8 | `Assets/Scripts/Abilities/Grenade.cs` | `damage = 50f` → `80f`. |
| 9 | New prefab | `fogOverlayCellPrefab` (dark translucent cell quad, sized to `cellSize`), wired to `GameLoop` in the game scene. Needs `.meta` like any asset. |

**Testing notes:** `GameLoop.TESTING = true` uses adjacent spawns (everything mutually visible) and local host+client — flip to the real spawns to see fog do anything. Two-instance testing (host + client) is required to validate `NetworkShow/NetworkHide`; verify: (a) hidden enemy transforms stop updating on the client (breakpoint/log in a client-side observer, or NGO network stats), (b) health bars correct after re-show following damage taken while hidden, (c) death while hidden → unit absent when cell re-sighted, (d) host view hides enemies visually.

# 4. Assumptions made (stated, not blocking)

- 2 players / 2 teams exactly (`teamNames` is hardcoded to 2; the visibility loop is written pairwise and generalizes for free).
- Walls are the only vision blockers; units do NOT block vision (they don't block movement paths either).
- No exploration memory: terrain always visible, no "unexplored black" tier — a two-tier fog (visible / dimmed) fits the small fixed map.
- Spectators/late-joiners don't exist (relay capped at 2 connections).
- The ability activation flow will be re-enabled largely as written in the commented `ActivateAbility.cs`; §1.7 rules assume that shape.
