# Battle Plan Code Map

Use this document as the first stop for code work. It records responsibility boundaries and the main runtime paths so a task can begin with a few targeted reads instead of another repository-wide survey.

The code remains the source of truth. Update this map in the same change whenever a major responsibility moves or a core flow changes.

## Architecture at a glance

Battle Plan is a Unity 6000.3 project using Netcode for GameObjects. The server owns match state, validation, movement, abilities, damage, cooldowns, stuns, walls, and smoke. Clients own planning input and presentation, then submit orders to the server.

| Area | Primary location | Responsibility |
| --- | --- | --- |
| Match orchestration | `Assets/Scripts/GameManager/GameLoop.cs` | Match setup, phases, order collection, server validation, dodge windows, execution, win conditions, cooldown progression, fog, smoke, and permanent wall state |
| Game modes | `MatchOptions.cs`, `GameLoop.cs`, `EscortSeries.cs` | Which mode is live, and the objective rules each one adds to the round loop |
| Planning | `Assets/Scripts/GameManager/PlanMovement*.cs`, `PathSelection.cs`, `PlanningPaths.cs` | Local unit selection, movement routes, ability targets, previews, commit/unlock state, and plan serialization |
| Grid and maps | `Assets/Scripts/GameManager/GridSystem.cs`, `Assets/Scripts/Map/` | Coordinate conversion, grid traversal, range/line queries, map definitions, active wall cells, and wall presentation |
| Units and combat | `Assets/Scripts/Units/` | Unit identity, health, movement, shooting, bullets, animation, replicated stun/cooldown state, and `UnitData` |
| Abilities | `Assets/Scripts/Abilities/` | One component per ability plus shared execution/interruption behavior in `BaseAbility.cs` |
| Networking | `Assets/Scripts/Multiplayer/` | Session lifetime, transport/relay, spawning helpers, reconnect state, and local multiplayer tooling |
| UI | `Assets/Scripts/Menus/` | Title/join/roster screens, HUD, unit cards, planning feedback, and battle reports |
| Presentation | `Assets/Scripts/VFX/`, `Assets/Scripts/Camera/` | Reusable gameplay VFX, camera effects, audio, mouse-to-world queries, and visibility-facing effects |
| Tutorial and development tools | `Assets/Scripts/Tutorial/`, `Assets/Scripts/GameManager/*Studio.cs`, `Dev*.cs`, `Sandbox*.cs`, `Assets/Scripts/Menus/SandboxPanel.cs` | Tutorial flow, deterministic developer input, E2E drivers, the sandbox, and capture tools |
| Editor checks | `Assets/Scripts/**/Editor/` | Edit-mode tests and editor-only validation close to the systems they cover |

`GameLoop.cs` is an intentional central controller. Add match-wide flow there rather than creating another manager. Smaller input, presentation, math, or component-owned behavior belongs beside the system that owns it.

## Round flow

1. Multiplayer/session code establishes the match and `GameLoop` spawns walls and units.
2. `GameLoop` enters `planning` and starts `PlanMovement.StartPlanning` on the local client.
3. `PathSelection` edits movement routes. `PlanMovement` handles unit/card selection, ability targeting, previews, and commit state.
4. Plans travel as `PathsDict`. The tuple is `(abilityMode, path)`: movement plans contain a route; ability plans contain the caster cell and, when required, the selected target cell.
5. The server sanitizes submitted movement and ability plans in `GameLoop` before accepting them.
6. Abilities that threaten a response window enter `dodging`; eligible units receive a short movement-only planning session.
7. `GameLoop` enters `executing`, starts movement, and invokes each ability through `Ability.RunAbility`.
8. Execution waits for shooting, abilities, overlap resolution, and return-fire windows before the next round or match result.

The stable phase labels are `planning`, `dodging`, `executing`, and `idle`.

## Game modes

`MatchOptions.gameMode` selects the mode and is replicated, so both peers agree before either
validates anything. Every mode runs the same round flow above; a mode adds an arbitration step after
step 8 and nothing else.

| Mode | Arbitration | State |
| --- | --- | --- |
| Elimination | The post-loop `EndGame` resolves a survivor or a simultaneous-wipe draw | none |
| King of the Hill | `ResolveKingOfTheHillRound` advances a control streak | `replicatedHillControl` |
| Escort the President | `ResolveEscortRound` reads each president into a standing and asks `EscortSeries.ResolveLeg` | `replicatedEscortState` + the static `EscortSeries` |

Enum ids are a wire contract. Id 2 was Capture the Flag and is retired — an older peer can still put
it on the wire, so `MatchOptions.Sanitized` must keep folding unknown ids back to Elimination, and a
new mode takes the next free id rather than reusing a retired one.

### Escort the President

A match is a *series* of legs rather than a single board. Leg 1 gives the host crew a president, leg 2
gives the opponent crew one, and a 1-1 split runs a decider that arms both. A leg is won by
extraction, by killing the president, by wiping the defence, or by the defence surviving
`EscortSeries.RoundsPerLeg` — which is a bound so a leg cannot run forever, not a pressure clock. The
decider is settled by whichever president covered more ground the moment either of them resolves.

- `EscortSeries` is a static session in the mould of `TutorialSession`/`SandboxSession`: a leg boundary
  reloads the Game scene, so the bookkeeping has to outlive the `GameLoop` that recorded it. The crew
  each side fields changes when the president changes hands, so the board is rebuilt rather than
  patched. The host owns it; clients follow through `SyncFromServer` off `replicatedEscortState`.
- Every leg rule, the extraction geometry, and the payload spawn layout are pure static functions
  there, covered by `EscortSeriesEditModeTests`.
- The president is never picked. He is substituted into the middle crew slot at spawn time by
  `GameLoop.ResolveFieldedCatalogIndex`, so `RosterRules` validation and `ConfigureTeam` are
  untouched and his `UnitData` stays `unavailableForRoster`. `GameLoop.GetPresident` finds him by his
  `PresidentialRecall` component rather than by slot index.
- A wiped crew is a leg result rather than a match result, so the round loop's elimination break is
  skipped for this mode and arbitration gets the round first.
- The bot does not walk its whole crew at one objective here. `BotPlayer.ResolveEscortRoles` splits
  it: the president plans first and runs his own route, his crew screens the cell he committed to,
  and a defending crew takes the ground between him and his zone (`BuildEscortInterceptTargets`).
  `WouldAbandonHill` is King of the Hill only. The defence reads the president's position out of
  `BotKnowledge`, never off the board, and guards the zone until it has seen him.

## Planning layout

`PlanMovement` is one Unity `MonoBehaviour` split into partial files. `PlanMovement.cs` retains the filename-matching component and its stable `.meta` GUID.

| File | Contents |
| --- | --- |
| `PlanMovement.cs` | Unity lifecycle plus planning-session, timer, lock-in, unlock, submission, and commit orchestration |
| `PlanMovement.AbilityTargeting.cs` | Ability target input, validation reasons, feedback, target previews, path previews, and preview cleanup |
| `PlanMovement.Routes.cs` | Route hit-testing, team discovery, occupied destination handling, and route trimming |
| `PlanMovement.UnitSelection.cs` | Unit cards, board selection, movement/ability mode switching, and selected-unit overlays |
| `PlanMovement.Visuals.cs` | Route ribbons, lane mapping, range overlays, outlines, and planning visual lifecycle |
| `PlanningPaths.cs` | `PathsDict` network serialization |
| `PathSelection.cs` | Mouse drag lifecycle and incremental movement-route editing |

Keep new planning behavior in the matching partial. Shared session state can remain in `PlanMovement.cs`; avoid adding another planning manager.

## Sandbox

An editor-only tool, not a game mode: `Battle Plan/Sandbox` enters Play mode, stands up a loopback
host and drops onto a board the designer composes from inside the running match. It is a real match —
same round loop, same planning, same server validation — with four things added.

| Piece | Responsibility |
| --- | --- |
| `SandboxSession.cs` | The setup: two `SandboxCrew`s (characters, cells, immortality), plus every rule about them as a pure function. Edit-mode tested |
| `SandboxDirector.cs` | Host-side runtime: keeps the panel on the live HUD, holds the HUD in its sandbox presentation, owns the board-edit drag |
| `SandboxPanel.cs` | The in-game panel (`Assets/UI/Game/SandboxPanel.uxml`), docked inside the HUD's own tree so it picks like the HUD does |
| `SandboxLauncher.cs` | The menu item, the loopback launch, and setup persistence in `EditorPrefs` |

- **Both crews are the designer's.** `PlanMovement` runs over two control groups rather than one team
  (`DualControl`), the enemy card strip becomes a second dock, and one lock-in submits both. The
  submit RPC only ever accepts the sender's own team, so the opponent's half of the session is left
  on `SandboxSession` and read back server-side as planning closes. Anything unplanned holds.
- **Crew composition is on the cards**, one per fielded unit on either strip. Pressing a
  character's portrait offers every other character for that slot or takes it off the board; an add
  tile closes each strip. The portrait sits inside the card's own press, which orders the ability, so
  that press is claimed and stopped at the portrait.
- **Any character can be fielded**, the president included. The roster names exactly who is on the
  board, and `SetupUnitsAndCards` skips the `RosterRules` eligibility rule for a sandbox board rather
  than standing an eligible substitute in for a restricted pick. Substitution was tried and dropped:
  the roster is built on the join screen before any catalog is loaded, so there was nothing to test
  eligibility against and a restricted pick reached spawning unchecked — and it lied to everything
  that names a unit from its roster slot, the cards and the battle report included. What every crew
  still has to do, sandbox or not, is name a catalog entry that has a model.
- **Every board change goes through the round loop.** `GameLoop.RequestSandboxRebuild` sets a flag
  the loop serves at the next round boundary, so a rebuild never lands mid-execution; a finished
  match is served immediately instead, since no loop is left to serve it. Reset, Clear crews and
  every roster edit are all that one path.
- **Board-edit mode** suspends planning input and drags a unit between squares
  (`GameLoop.SandboxPlaceUnitAtCell`). The setup follows the unit, so the square it is dropped on is
  the square a rebuild puts it back on. `SandboxSession.IsBoardEditLive` is the single gate: armed,
  and between rounds — so leaving the mode on never costs the designer a dodge.
- **The dodge window works the same way.** It is open-ended, one seat is handed every alerted unit
  on the board (`OpenSandboxDodgeWindow`), and one confirm answers for both crews
  (`PlanMovement.dodgeCommitAvailable`, `GameHUDController.ShowDodgeCommitReady`). A dive has no
  lock/unlock protocol — the server takes one and closes the window — so that chip is a plain submit,
  and pressing it with nothing drawn is how a dodge is declined. The opponent's dives ride
  `SandboxSession`'s dodge slot and are taken by `AcceptDodgeResponse`, which is also what keeps a
  team that was never alerted from registering a response it had nothing to give.

The bot is frozen (`BotFrozenUnits`) and, in the sandbox, silent: it must not enter a stand-still
dodge for the crew the designer is about to dive, because the first response closes the window.

## Ability lifecycle and stun interruption

Every ability derives from `Ability` in `BaseAbility.cs` and implements `ExecuteAbility`.

- `GameLoop` calls `Ability.RunAbility`, which tracks the running coroutine.
- An ability calls `BeginInterruptibleAbilityAction` when it enters the portion that a stun should cancel. This also pauses ordinary shooting.
- `Unit.ApplyStun` calls `Ability.InterruptForStun`, which stops the tracked coroutine and runs `OnAbilityInterrupted` cleanup.
- A normal finish calls `CompleteInterruptibleAbilityAction`, which releases the ability-owned shooting pause.
- Long-lived effects must override `OnAbilityInterrupted` when stopping the coroutine alone would leave spawned state, movement state, or VFX behind.

Do not add parallel “is interrupted” polling flags to individual abilities. The coroutine lifecycle is the interruption mechanism.

A stun is not the only way an ability ends early. `Ability.CancelForDisplacement` stops one whether or
not it reached an interruptible window; a round starts every ability on the same frame, so one that
has not begun has declared nothing interruptible and the stun path would pass over it. An ability that
cancels its own crew's orders declares `CancelsAlliedOrders`, which makes the round resolve it first,
and hands the spent charge back through `GameLoop.RefundAbilityCharge`. `PresidentialRecall` is the
only one, and the only caller.

`PresidentialRecall` commits every ally's cell, cancellation and refund up front, then animates the
arrival: a beckon beat, then one staggered arc per ally over `AbilityTrajectory.SampleLob`, colliders
off in flight the way `ShatterLeap` does it. Landing is what applies each ally's guard. The whole
rally is under a second and `OnAbilityInterrupted` lands anyone still airborne, so the round resolves
on the same cells whether or not the animation finishes.

Planning previews ask an ability for optional path points through `BuildPlannedPath`. Runtime effect code remains in the concrete ability.

## Shooting and projectile flow

`Shooting` owns target acquisition, line-of-sight checks, firing cadence, ammunition, and bullet creation.

1. The server resolves a shot and creates the authoritative `Bullet`.
2. `FireBulletClientRpc` creates non-authoritative visual copies on other peers.
3. `Bullet` advances with transform velocity and swept collision queries. It gates damage and area impact behind `isAuthoritative`; every peer still handles local tracer movement and impact presentation.
4. Per-shot exceptions must travel through both creation paths so host and clients see the same collision behavior.

Suppressing Fire is the current example of a narrow collision exception: its bullets ignore only the selected adjacent wall instance. Ordinary bullets and later walls keep normal collision behavior.

## Unit composition and data

A networked unit prefab normally combines:

- `Unit`: replicated team, roster slot, cooldown, and stun state.
- `Movement`: routes, rotation, locomotion, and transition back to shooting.
- `Shooting`: weapon cadence, targeting, and projectile creation.
- `Health`: authoritative damage and death, plus the round-scoped damage guard `GameLoop` clears at
  the round boundary alongside move-speed boosts. `GuardOrbVisual` is its indicator: an additive
  `BattlePlan/GuardOrb` shell parented to the unit, so host-side fog suppression reaches it with the
  rest of the unit's renderers.
- One concrete `Ability` component when the unit has an active ability.
- `AnimationHandler` and presentation components as needed.

Static tuning lives in `UnitData` assets under `Assets/UnitStats/`. Prefab wiring lives under `Assets/Prefabs/Units/`; projectile prefabs live under `Assets/Prefabs/Projectiles/`. When changing a character, inspect its ability script, `UnitData` asset, unit prefab, and relevant projectile prefab together.

Important `UnitData` targeting fields include `selectAbilitySquare`, `selectAbilityDirection`, `abilityFixedDistance`, `abilitySquareRange`, `abilityRadius`, `responseRange`, and `responseDistLine`. Planning and server sanitation both depend on these values.

## Grid, walls, smoke, and visibility

- `GridSystem` owns coordinate conversion and reusable grid math.
- `MapCatalog.Active` exposes the current `MapDefinition`; `GameLoop.wallLayout` is its live wall-cell set.
- Objective cells are drawn on the deck by `GameLoop.ObjectiveOutline`, shared by the hill pad and the
  escort extraction zones. It outlines a cell set; it owns no gameplay rule.
- `CoverVariant` chooses a wall's visual form. It does not own gameplay blocking.
- `GameLoop` owns permanent wall removal and the cell-to-wall-instance registry.
- Smoke is server-authored denial state, not a physical wall. It affects visibility and bullet-path checks without entering wall pathfinding.

Prefer cell-based rules for planning and validation. Use physics when the actual projectile or collision shape matters.

## Networking rules

- Server-authoritative components derive from `NetworkBehaviour`.
- Network-dependent setup belongs in `OnNetworkSpawn`/`OnNetworkDespawn`.
- Mutating gameplay begins on the server or through a `MethodNameServerRpc`.
- Client-only presentation uses `MethodNameClientRpc` or replicated state callbacks.
- Team index and roster slot are logical gameplay identity; NGO ownership identifies the controlling connection. Do not substitute one for the other.
- Visual copies may simulate locally, but damage and lasting state changes stay authoritative.

## Where to make common changes

| Change | Start here | Also inspect |
| --- | --- | --- |
| Round phases, win rules, dodge windows | `GameLoop.cs` | `DevInput.cs`, relevant editor checks |
| Game mode rule or a new mode | `MatchOptions.cs`, then the mode's own arbitration in `GameLoop.cs` | `EscortSeries.cs` for the objective-series pattern, `JoinGameUIController`, `GameHUDController`, `BotPlayer.GetStrategicTargets` |
| Planning interaction or target validation | Matching `PlanMovement` partial | `PathSelection.cs`, `GameLoop.SanitizeAbilityPlan` |
| Grid/path rule | `GridSystem.cs` | Planning validation and server sanitation callers |
| New or changed ability | Concrete file in `Abilities/` | `BaseAbility.cs`, unit prefab, `UnitData`, planning preview |
| Stun behavior | `Unit.cs`, `BaseAbility.cs` | Ability-specific `OnAbilityInterrupted` cleanup |
| Weapon cadence or targeting | `Shooting.cs` | `UnitData`, projectile prefab, `Bullet.cs` |
| Bullet collision/damage | `Bullet.cs` | Both bullet creation paths in `Shooting.cs` |
| Character tuning | `Assets/UnitStats/*.asset` | Unit prefab and balance/editor checks |
| Wall or map behavior | `GameLoop.cs`, `GridSystem.cs`, `Assets/Scripts/Map/` | Wall prefab and map definitions |
| HUD/card feedback | `GameHUDController.cs`, `UnitCardElement.cs` | `PlanMovement` callers |
| Sandbox behaviour | `SandboxSession.cs` for a setup rule, `SandboxPanel.cs` for a control | `SandboxDirector.cs`, the sandbox block in `GameLoop.cs`, `SandboxEditModeTests` |
| Session/reconnect issue | `NetworkHandler.cs`, `ReconnectSession.cs`, `ReconnectGrace.cs` | `GameLoop` phase/state restoration |

## Working and verification agreement

For ordinary feature work, the current handoff requirement is a successful Unity script compile with zero relevant console errors. The user handles feature testing unless they explicitly request automated or play-mode verification.

Do not enter Play mode merely to validate a code change. If runtime verification is requested later, follow the editor-lease and minimal-Play-mode rules in `AGENTS.md`.

## Keeping this useful

Update this document when:

- a major class is split, merged, or renamed;
- ownership moves between systems;
- the round, planning, ability, projectile, wall, smoke, or networking flow changes;
- a new persistent data or prefab location becomes part of normal feature work.

Do not add every helper or every concrete ability. Record stable boundaries and high-value entry points.
