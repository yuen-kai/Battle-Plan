# Ability Juice — shared builder brief

Read this in full before writing code. Everything here is binding.

**Also read `MEASURED_FINDINGS.md` in this folder.** It records what eight critics measured in the
captured frames after round one, when all eight pieces were rejected. It contains the reason they
failed, and it applies to every piece regardless of which one you own. Reading it is not optional.

## The goal

An unlabelled three-frame strip of one of our abilities — wind-up, impact, aftermath — is placed
next to the same three frames from a Clash Mini ability. A stranger has to pick **ours** as the one
that hits harder. Nothing less counts.

You are building **one** of eight primitives that compose every ability. Yours will be filmed in
isolation on an empty board and judged on its own by a critic who has never seen your code, only
the frames.

## The project

Unity 6000.3.1f1, URP, Netcode for GameObjects. Turn-based tactics on a grid, viewed from a fixed
board camera.

| Fact | Value |
| --- | --- |
| Grid cell size | `GameLoop.cellSize` = **2.7** world units |
| Unit height | roughly **1.6–1.8** world units — a unit is smaller than one cell |
| Camera pitch | **73°** down, perspective, FOV 60 |
| Camera distance in capture | 9–21 world units from the action |
| Bloom | **ON**, threshold **1.8**, intensity 0.55, scatter 0.55 — anything above 1.8 blooms |
| Tonemapping | Neutral. Contrast +14, saturation +8 |
| Ground plane | y = 0. Fog overlay tiles at y = 0.05, shockwaves at y = 0.08 |

**Scale calibration, and this is the single most common way to get this wrong:** the baseline
grenade explosion uses 400 particles at size 0.2. At 2.7 units per cell that is 7% of a cell, and
in the captured frame it is an unreadable smudge. Effects must be sized in **cells**, not in
guesses. A serious explosion is **1.5–3 cells across**. Err large; the critic will tell us if it is
too much, and it never has been yet.

## Hard constraints

1. **No VFX Graph.** Particle System, custom shaders, LineRenderer, MeshRenderer, procedural
   meshes and code-driven transforms only.
2. **Purely local and visual.** Never touch authoritative state: no health, no grid position, no
   `NetworkVariable`, no RPC. You may move *visual child transforms*, never the networked root's
   authoritative position.
3. **Everything is created at runtime from code.** Do not depend on new prefabs or new `.asset`
   files — you cannot create the `.meta` files for them safely. Build meshes, materials and
   particle systems in C#. `Shader.Find` for shaders.
4. **Clean up after yourself.** Destroy spawned objects, destroy instantiated `Material`s in
   `OnDestroy`. Nothing may survive into the next round of planning.
5. **Never allocate per-frame in a loop** where it is avoidable, and never leave a coroutine
   running forever.
6. **Do not enter Play mode. Do not use Unity MCP tools. Do not run Unity.** Another process holds
   the editor lease. Write code; it will be compiled and filmed for you.

## Code conventions

- No namespaces anywhere in `Assets/Scripts`. Do not add one.
- Match surrounding style: 4-space indent, `PascalCase` methods, descriptive locals over
  abbreviations, `[SerializeField] private` for inspector-only fields.
- Comments explain intent and constraint, never mechanics. Do not narrate what a line does. Do not
  leave comments describing your change or its history.
- Use `MaterialPropertyBlock` when tinting shared unit materials — never write to
  `renderer.material` on a unit.

## What already exists that you may call

| API | What it does |
| --- | --- |
| `AbilityJuice.HotCore` | HDR white reserved for energy cores |
| `AbilityJuice.Alarm` | alarm yellow, the board's "respond now" colour |
| `AbilityJuice.Hot(color, intensity)` | scales a colour into HDR so it blooms |
| `TeamPalette.Friendly` / `.Enemy` / `.BrightForViewer(bool)` | team colours |
| `GameLoop.cellSize`, `GameLoop.gridCoordToWorld(Vector2Int)` | board maths |
| `GameLoop.Instance.TeamCamera` | the board camera |
| `Shader.Find("BattlePlan/GroundGlow")` | additive ground ring/disc. Props: `_GlowColor` (HDR), `_RingWidth` (0.02–1, 1 = filled disc), `_EdgeSoftness`, `_Intensity` (0–8), `_PulseSpeed`, `_PulseAmount` |
| `Shader.Find("BattlePlan/EnergyBeam")` | additive beam for LineRenderers. Props: `_GlowColor`, `_CoreColor` (both HDR), `_CoreWidth`, `_EdgeSoftness`, `_ScrollSpeed`, `_NoiseScale`, `_NoiseStrength`, `_Intensity` |

You may add **new** shaders, but only named `Assets/Shaders/BP_<YourPiece>*.shader`. Never edit an
existing shader — another builder may own it.

## Reference material

`Captures/AbilityJuice/reference/` holds real Clash Mini frames pulled from gameplay footage,
sorted into `windup/`, `impact/`, `aftermath/` and `general/`. **Look at them.** Read several
images before you write anything. Note specifically:

- how much of the frame an effect occupies
- how saturated the colour is, and where white appears
- how hard the edges are — Supercell effects have crisp, readable silhouettes, not soft haze
- how much *opaque* material there is, not just light

## The baseline you are replacing

`Captures/AbilityJuice/strips/r0/` holds the current state of all five abilities, captured from the
running game. Look at these too. They are weak: the grenade impact is a pink smudge a third of a
cell wide, the pogo landing is invisible, and the aftermath frame is indistinguishable from the
wind-up frame. This is the floor.

## Deliverable

Implement your assigned file's public API exactly as declared. **Do not change the signatures** —
the capture rig and four other files call them. Fill in the bodies, add whatever private classes,
`MonoBehaviour`s, meshes and shaders you need.

Your code must compile. You cannot test it, so be careful: no null-reference hazards, guard every
`Shader.Find`, guard every `GetComponent`, and make the effect degrade gracefully rather than throw
if something is missing.

Finish by reporting: what you built, the exact parameters you chose and why, the world-space size
of the effect in **cells**, and what you were unsure about.
