# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users

Players who want quick, readable tactical matches. The current build is desktop-first with keyboard and mouse; menu layouts and focus behavior should also prepare for controller, console, and touch use.

## Product Purpose

Battle Plan is a multiplayer tactics game in which players assemble a five-unit fireteam, issue simultaneous hidden orders, and watch those plans resolve. Setup should move players from the title screen to a readable match configuration and roster with minimal friction.

## Positioning

The defining mechanism is simultaneous planning: prediction, positioning, abilities, fog of war, and a short execution phase matter more than real-time input speed.

## Operating Context

The non-gameplay flow is Title Screen → Create or Join Match → Assemble Fireteam → Game. Matches support AI or player opponents, Elimination or King of the Hill, optional fog of war, and Unity Relay for remote players.

## Capabilities and Constraints

- Built in Unity 6000.3.1f1 with UI Toolkit and Netcode for GameObjects 2.7.
- `RosterRules.UnitsPerPlayer` is five; roster options and selected slots are generated at runtime.
- Menu presentation models must be scene-local decoration with no `NetworkObject`, gameplay scripts, colliders, or authority.
- Existing UXML element names, scene routes, controller contracts, and responsive root classes must remain stable.
- Gameplay HUD, combat logic, balance, and the current cooldown/Lock In work are outside the first non-gameplay redesign pass.

## Brand Commitments

- Keep the name **Battle Plan**.
- The rounded low-poly characters are intentionally whimsical and goofy. Their poses and animation should carry the personality instead of realistic military imagery.
- Reuse the old in-world menu camera presentation where it strengthens that character.
- Use direct labels and remove unnecessary explanatory or fake-terminal text.
- The supplied reference images guide tactical readability and energetic feedback, but the project’s actual character models remain the visual authority.

## Evidence on Hand

- Visual references in `ReferenceImages/`.
- Existing portraits in `Assets/Images/Portraits/`.
- Existing posed and animated title models in `Assets/Scenes/Title Screen.unity`.
- Existing posed Soldier and Pogo Rider in `Assets/Scenes/HomeScreen.unity`.
- Historical goofy JoinGame staging is visible in `Assets/Images/Unity_g0I8iFX1Y3.png`.
- Current UI Toolkit screens and editor contract tests live under `Assets/UI/`.

## Product Principles

1. Let the characters be funny; keep controls clear.
2. Show only the information needed for the next decision.
3. Keep match setup fast and immediately understandable.
4. Pair color with shape, text, or icon so state never depends on color alone.
5. Keep decorative menu presentation isolated from networking and gameplay.

## Accessibility & Inclusion

Keep visible keyboard/controller focus, readable contrast, 52px primary targets, non-color state cues, and layouts that remain usable at narrow and short aspect ratios. Continuous character motion must not obscure controls or prevent progress.
