# Art Direction — Wiring Guide

> **STATUS (July 2026): steps 0–5 are DONE and verified** (E2E suite 34/34 pass, screenshots in
> `Assets/Screenshots/e2e/`). Step 6 (UI restyle) remains. Notes on what shipped:
> - Fog of war landed in parallel; `FogOverlayCell.mat` was deepened (`#010204` @ 78%) for the
>   dark arena, and `ImpactShockwave` rings sit at y 0.08 above the fog tiles (y 0.05).
> - Vision cones are on the base `Unit.prefab`, tinted per allegiance in `Unit.SetupVisionCone`,
>   length = `visionRange × cellSize` capped at 10 — mood lighting; the fog overlay is the
>   mechanic's source of truth.
> - Game camera now clears to solid `Ink` (no skybox) with flat dark ambient, so the board
>   floats in darkness instead of over the grass skybox.
> - Checkerboard is applied to the 90 scene GridCell instances (prefab default = Map_FloorDark).

Original plan below, kept for reference. Order is by impact-per-minute.

## 0. Sanity check after import
Let Unity refresh and compile. Expect zero errors: the 4 new scripts under `Assets/Scripts/VFX/`
reference nothing outside UnityEngine, and the 3 shaders under `Assets/Shaders/` are
self-contained URP HLSL. Quick smoke test: drop a Quad in a scene, assign
`Assets/Materials/FX/FX_GroundGlow.mat` — you should see a soft red disc glow.

## 1. Post-processing swap (biggest single-step mood change)
- In `Game.unity`, on the `Color Grade` prefab instance's Volume component, override the profile
  to `Assets/Settings/GameNoir Volume Profile.asset` (scene-level override; menus keep the old
  profile). It adds Bloom (threshold 1 — only HDR FX bloom) and Vignette; keeps ACES.
- Directional Light: color `#AFC4E8`, intensity ~0.4. Keep soft shadows.
- Verify camera has post-processing enabled (it does today).

## 2. Dark arena
- `Assets/Prefabs/Map/GridCell.prefab`: swap material `GridCell` → `Map_FloorDark`.
  For the checkerboard, either have `GridSystem` assign `Map_FloorDarkAlt` on
  `(col + row) % 2 == 1` at spawn (one-line material pick), or bake alternating prefab variants.
- `Assets/Prefabs/Map/Wall.prefab`: `Wall` → `Map_WallDark`.
- `GridCellOutline`: raise value to ~`#3D4A66` at ~35% alpha — on a dark floor the outline must
  be lighter than the fill.
- Re-check planning overlays (`MoveOverlayCell`, `AttackOverlay`, `AOEoverlay`,
  `AbilityRangeCell`) for contrast against the dark floor; expect alpha bumps.

## 3. Team glow accents
- In `Assets/Scripts/Units/Unit.cs` `SetTeamIndicators`, swap the assigned materials to
  `TeamBlueGlow` / `TeamRedGlow` (either change the `teamMaterials` list on each unit prefab, or
  keep prefabs and just repoint the list entries — the glow mats keep the same base colors, add
  HDR emission + matte smoothness).
- Matte pass on characters: for every material in `Models and stuff/Materials/`:
  `_Smoothness ≤ 0.15`, `_Metallic 0`, specular highlights off (ArtDirection §4.1).
- Base `Unit.prefab`: delete the capsule MeshRenderer + MeshFilter (keep CapsuleCollider).

## 4. Vision cones
- Add a child `VisionCone` (empty GO, MeshFilter + MeshRenderer + `VisionConeVisual`) to
  `Unit.prefab` at local position (0,0,0), rotation identity.
- Assign `Assets/Materials/FX/FX_VisionCone.mat`. Blocking layers default to `Walls`.
- Suggested: `viewAngle 70`, `viewDistance = targetRange × cellSize` per unit (Sniper wider
  distance, Shotgunner shorter/wider), `groundOffset 0.06` (above future fog quads).
- Color per allegiance from the local player's POV: friendly `#8CCFFF` a≈0.28, enemy `#FF5A70`
  a≈0.22 (call `SetColor` from a small hook in `Unit.OnNetworkSpawn` using `IsOwner`).
- When fog of war ships, disable/enable enemy cones with unit visibility.

## 5. Ability FX upgrades (per ability, all local-visual — keep existing RPC flow)
- **AreaLock.cs**: replace `CreateLaserLine` / `ShowLaserClientRpc` line construction with
  `BeamVFX.Create(transform, start, end, red)` + `beam.Pulse()`. Replace `AnimateLaserRush*`
  with `yield return beam.Rush(rushDuration)`. Replace `CreateExplosionEffect` with
  `ImpactShockwave.Spawn(targetPos, red, 3f)` + `HitFlash.FlashTarget(target, 0.18f, 1.5f)`.
  Keep the existing wall raycast for the beam end point and all RPC entry points.
- **Shooting.cs (Target Lock)**: same swap, but thin — `coreWidth 0.03`, `glowWidth 0.2`, no
  rush; add a `FX_GroundGlow` quad (RingWidth 0.35, PulseSpeed 1.5) under the marked unit.
- **Grenade.cs**: on detonation add `ImpactShockwave.Spawn(pos, alarmYellow, aoeRadius)`; flash
  each damaged unit via `HitFlash.FlashTarget`. Fuse blink: pulse `_EmissionColor` on the
  grenade material toward `#FFC400`, frequency ramping with remaining fuse.
- **Shield.cs**: `ImpactShockwave.Spawn(unitPos, blue, 1.5f, 0.3f)` on activation.
- **Pogo.cs**: shockwave + small `CameraEffects` shake on landing; optional short `BeamVFX`
  streak during flight.
- **Bullet impacts / deaths**: `HitFlash.FlashTarget(victim)` wherever `Health.TakeDamage`
  applies on each peer; on death, flash then scale-down tween before despawn.
- Fix `BulletRed.mat` (currently has blue values) and give both bullet materials HDR emissive
  base so tracers bloom.

## 6. UI restyle (independent, lowest urgency)
Per ArtDirection §6 — dark panels, line-art icons, health-bar chip effect, damage number pops.
No assets prepared yet; treat as a separate pass.

## Known tuning knobs
- Bloom too strong → lower intensity (0.9 → 0.5) before touching FX colors.
- Beams invisible on low-end → `BeamVFX` falls back to `Sprites/Default` automatically if the
  shader is missing from a build; add the three BattlePlan shaders to
  **Project Settings → Graphics → Always Included Shaders** for builds (materials in
  `Assets/Materials/FX/` already reference them, which normally suffices).
- Vision cone raycasts: 40 rays × 6 units × per-frame is fine on PC; drop `rayCount` to 24 for
  mobile.
