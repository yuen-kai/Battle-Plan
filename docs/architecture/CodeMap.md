# Battle Plan Code Map

Use this document as the first stop for code work. It records responsibility boundaries and the main runtime paths so a task can begin with a few targeted reads instead of another repository-wide survey.

The code remains the source of truth. Update this map in the same change whenever a major responsibility moves or a core flow changes.

## Architecture at a glance

Battle Plan is a Unity 6000.3 project using Netcode for GameObjects. The server owns match state, validation, movement, abilities, damage, cooldowns, stuns, walls, and smoke. Clients own planning input and presentation, then submit orders to the server.

| Area | Primary location | Responsibility |
| --- | --- | --- |
| Match orchestration | `Assets/Scripts/GameManager/GameLoop.cs` | Match setup, phases, order collection, server validation, dodge windows, execution, win conditions, cooldown progression, fog, smoke, and permanent wall state |
| Planning | `Assets/Scripts/GameManager/PlanMovement*.cs`, `PathSelection.cs`, `PlanningPaths.cs` | Local unit selection, movement routes, ability targets, previews, commit/unlock state, and plan serialization |
| Grid and maps | `Assets/Scripts/GameManager/GridSystem.cs`, `Assets/Scripts/Map/` | Coordinate conversion, grid traversal, range/line queries, map definitions, active wall cells, and wall presentation |
| Units and combat | `Assets/Scripts/Units/` | Unit identity, health, movement, shooting, bullets, animation, replicated stun/cooldown state, and `UnitData` |
| Abilities | `Assets/Scripts/Abilities/` | One component per ability plus shared execution/interruption behavior in `BaseAbility.cs` |
| Networking | `Assets/Scripts/Multiplayer/` | Session lifetime, transport/relay, spawning helpers, reconnect state, and local multiplayer tooling |
| UI | `Assets/Scripts/Menus/` | Title/join/roster screens, HUD, unit cards, planning feedback, and battle reports |
| Presentation | `Assets/Scripts/VFX/`, `Assets/Scripts/Camera/` | Reusable gameplay VFX, camera effects, audio, mouse-to-world queries, and visibility-facing effects |
| Tutorial and development tools | `Assets/Scripts/Tutorial/`, `Assets/Scripts/GameManager/*Studio.cs`, `Dev*.cs`, `CharacterSandbox.cs` | Tutorial flow, deterministic developer input, E2E drivers, sandboxes, and capture tools |
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

## Ability lifecycle and stun interruption

Every ability derives from `Ability` in `BaseAbility.cs` and implements `ExecuteAbility`.

- `GameLoop` calls `Ability.RunAbility`, which tracks the running coroutine.
- An ability calls `BeginInterruptibleAbilityAction` when it enters the portion that a stun should cancel. This also pauses ordinary shooting.
- `Unit.ApplyStun` calls `Ability.InterruptForStun`, which stops the tracked coroutine and runs `OnAbilityInterrupted` cleanup.
- A normal finish calls `CompleteInterruptibleAbilityAction`, which releases the ability-owned shooting pause.
- Long-lived effects must override `OnAbilityInterrupted` when stopping the coroutine alone would leave spawned state, movement state, or VFX behind.

Do not add parallel “is interrupted” polling flags to individual abilities. The coroutine lifecycle is the interruption mechanism.

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
- `Health`: authoritative damage and death.
- One concrete `Ability` component when the unit has an active ability.
- `AnimationHandler` and presentation components as needed.

Static tuning lives in `UnitData` assets under `Assets/UnitStats/`. Prefab wiring lives under `Assets/Prefabs/Units/`; projectile prefabs live under `Assets/Prefabs/Projectiles/`. When changing a character, inspect its ability script, `UnitData` asset, unit prefab, and relevant projectile prefab together.

Important `UnitData` targeting fields include `selectAbilitySquare`, `selectAbilityDirection`, `abilityFixedDistance`, `abilitySquareRange`, `abilityRadius`, `responseRange`, and `responseDistLine`. Planning and server sanitation both depend on these values.

## Grid, walls, smoke, and visibility

- `GridSystem` owns coordinate conversion and reusable grid math.
- `MapCatalog.Active` exposes the current `MapDefinition`; `GameLoop.wallLayout` is its live wall-cell set.
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
| Planning interaction or target validation | Matching `PlanMovement` partial | `PathSelection.cs`, `GameLoop.SanitizeAbilityPlan` |
| Grid/path rule | `GridSystem.cs` | Planning validation and server sanitation callers |
| New or changed ability | Concrete file in `Abilities/` | `BaseAbility.cs`, unit prefab, `UnitData`, planning preview |
| Stun behavior | `Unit.cs`, `BaseAbility.cs` | Ability-specific `OnAbilityInterrupted` cleanup |
| Weapon cadence or targeting | `Shooting.cs` | `UnitData`, projectile prefab, `Bullet.cs` |
| Bullet collision/damage | `Bullet.cs` | Both bullet creation paths in `Shooting.cs` |
| Character tuning | `Assets/UnitStats/*.asset` | Unit prefab and balance/editor checks |
| Wall or map behavior | `GameLoop.cs`, `GridSystem.cs`, `Assets/Scripts/Map/` | Wall prefab and map definitions |
| HUD/card feedback | `GameHUDController.cs`, `UnitCardElement.cs` | `PlanMovement` callers |
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
