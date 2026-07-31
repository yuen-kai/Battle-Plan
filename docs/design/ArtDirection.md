# Battle Plan — Art Direction Spec

> Companion doc to `docs/GAME_DESIGN.md` ("legibility over realism" is still the law).
> References live in `ReferenceImages/`:
> - `VisualVibe.jpg` — **Bullet Echo**: the mood, lighting, and arena treatment.
> - `CharacterDesign.webp` — **Tactical Breach Wizards**: how characters are modeled, shaded, and given identity.
> - `AbilityDesign.png` — **Clash Mini**: how big and juicy ability moments should feel.
>
> Everything here is buildable in the current stack: Unity 6000.3.1f1, URP 17.3, Linework Lite
> outlines, uGUI + TMP, no VFX Graph. New shaders/materials/scripts referenced below already
> exist in the repo (see §7 asset manifest and `docs/design/ArtDirection-WiringGuide.md`).

---

## 1. The One-Sentence Look

**A dark, noir tactical arena where light is information** — near-black blue environment, units
that read as flat-shaded toy-soldier silhouettes with neon team accents, and ability moments that
briefly own the whole screen with white-hot, blooming energy.

The three references map cleanly onto the three layers of the game:

| Layer | Reference | What we steal |
|---|---|---|
| **Arena / lighting / mood** | Bullet Echo | Darkness as the default; vision cones as light shafts; saturated team glow against desaturated navy; HUD as thin white line-art |
| **Characters** | Tactical Breach Wizards | Matte flat-shaded low-poly, 2–3 value color blocks, identity from one big accessory per unit, zero gloss, soft rim/outline separation |
| **Ability FX** | Clash Mini | Oversized white-core beams and bursts, anticipation → impact frame → aftermath, hit-flash, shockwave rings, camera shake (already have it) |

Why this fits Battle Plan specifically: fog of war is shipped
(`docs/design/FogOfWar-and-DamageRebalance.md`, Deliverable 1). A Bullet Echo-style dark arena makes
fog feel *native* instead of bolted on — hidden isn't "a gray overlay on a sunny board", hidden is
simply **dark**, and visible is **lit**.

---

## 2. Palette (single source of truth)

Environment is desaturated and dark; only gameplay-meaningful things get saturation. Nothing in
the environment may be more saturated than a team color.

| Token | Hex | Linear-ish RGB | Use |
|---|---|---|---|
| `Ink` | `#0B0E17` | (0.016, 0.022, 0.048) | Floor base, vignette, fog-of-war cells |
| `InkAlt` | `#111624` | (0.028, 0.038, 0.075) | Floor checker alternate (2×2 cell checker like Clash Mini, but dark) |
| `Slate` | `#232B3E` | (0.075, 0.105, 0.19) | Walls, cover, props |
| `SlateEdge` | `#3D4A66` | — | Wall top edges / trims, subtle |
| `TeamBlue` | `#38C8FF` | (0.16, 0.72, 1.0) | Friendly accents, outlines, beams, vision cones |
| `TeamBlueDeep` | `#2456F0` | — | Friendly cloth/armor blocks (non-emissive) |
| `TeamRed` | `#FF3B54` | (1.0, 0.14, 0.25) | Enemy accents, outlines, threat telegraphs |
| `TeamRedDeep` | `#C21B33` | — | Enemy cloth/armor blocks (non-emissive) |
| `HotCore` | `#FFFFFF` @ HDR ×4–8 | — | The center of every beam/explosion. Always white, never tinted |
| `Alarm` | `#FFC400` | (1.0, 0.72, 0.0) | Dodge alerts, grenade fuse, timers under 3s |
| `Heal` | `#4DFF7C` | — | Reserved (healing/pickup futures; Bullet Echo's +100 green) |

Rules:
- **Team color is sacred.** Blue-family hues only appear on friendly things, red-family only on
  enemy things. The camera flips per team, but `Unit.SetTeamIndicators` already assigns
  owner=blue / other=red — keep that contract everywhere (beams, cones, outlines, rings).
- **White is reserved for energy.** UI text is off-white (`#E8ECF5`); pure white at HDR intensity
  only exists inside FX cores so bloom picks out exactly the wow moments.
- **Yellow means "respond now".** It is already the ability-mode card color; extend it to dodge
  alerts and fuse blinks, nothing else.

---

## 3. Arena & Lighting (Bullet Echo layer)

Current state: white grid cells, gray walls, warm sun-like directional light, no bloom. Target:

1. **Kill the sun.** Directional light becomes a dim, cool fill: color `#AFC4E8`, intensity
   ~0.35–0.45, soft shadows kept. The board should feel like a night op, not noon.
2. **Dark checkerboard floor.** Alternate `Ink` / `InkAlt` per grid cell (the Clash Mini
   checker, dropped to near-black). Cell outlines go from black to `SlateEdge` at low alpha —
   in a dark arena the *lines* are lighter than the *fill*, inverting the current look.
   Materials ready: `Assets/Materials/FX/Map_FloorDark.mat`, `Map_FloorDarkAlt.mat`.
3. **Walls read as cover, not decoration.** `Slate` base (`Map_WallDark.mat`), slightly glossy
   top edge is enough. Walls block sniper lasers and vision — players must instantly parse them,
   so they get the *second*-lightest environment value after outlines.
4. **Vision cones are the hero light.** Every unit projects a soft additive light-shaft cone
   (shader `BP_VisionCone`, component `VisionConeVisual`) — white-blue for friendlies, red for
   spotted enemies, clipped by walls via raycast fan exactly like Bullet Echo's flashlights.
   Pre-fog these are pure vibe; when fog ships they *become* the vision mechanic's face.
5. **Bloom + vignette post.** New profile `Assets/Settings/GameNoir Volume Profile.asset`:
   ACES tonemapping, bloom (threshold 1 → only HDR FX bloom, intensity ~0.9), vignette 0.28,
   slight contrast+saturation lift, film grain kept subtle from the existing grade. The existing
   `Color Grade` prefab profile stays for menus; Game scene swaps to noir.
6. **Team glow accents.** `TeamIndicatorProp` renderers switch from flat Lit to emissive team
   materials (`TeamBlueGlow.mat` / `TeamRedGlow.mat`, emission ~×2) so squad membership is
   visible in the dark and catches bloom — Bullet Echo's glowing silhouette read, done with
   materials + the existing Linework outline instead of a custom rim shader.

Planning-phase overlays (move range, ability range, paths) stay as-is functionally but should be
re-tinted against the dark floor: move cells `TeamBlue` @ ~18% alpha, threat overlays
`TeamRed`/`Alarm`. Verify contrast after the floor darkens — alpha values tuned for a white board
will be nearly invisible on `Ink`.

---

## 4. Characters (Tactical Breach Wizards layer)

The FBX units in `Models and stuff/` are already low-poly and flat-colored — the TBW look is
mostly a **shading and palette discipline** pass, not a remodel:

1. **Matte everything.** All character materials: `_Smoothness ≤ 0.15`, `_Metallic 0`,
   `_SpecularHighlights` off. TBW characters have zero specular ping; gloss instantly reads
   "plastic toy render", which we only want on FX, never on people.
2. **Two-to-three value blocks per unit.** Each character = one dominant mid-value material
   (fatigues/coat), one dark (boots/gloves/harness ≈ `Slate`), one team-colored accent block
   (`TeamBlueDeep`/`TeamRedDeep` on the tagged `TeamIndicatorProp` meshes + glow variant details).
   Audit the Blender-exported materials to collapse near-duplicate colors into these blocks.
3. **One signature accessory carries the silhouette** (TBW's hat / goggles / riot shield trick).
   The roster already implies them — make each oversized (~120–140% realistic scale) so it reads
   from the 73° top-down camera, which mostly sees heads and shoulders:
   - **Soldier** — boonie/helmet + backpack radio antenna
   - **Shotgunner** — riot shield (already a cube child; replace with a chunky bevel-corner shield
     with a `CAUTION`-style decal, per the reference's rightmost character)
   - **Sniper** — long rifle + flat-brim hat, scope lens = tiny emissive team dot
   - **Pogo Rider** — the pogostick itself + aviator goggles
   - **Commander** — beret/greatcoat + glowing shoulder epaulets (team emissive)
4. **Faces are graphic, not sculpted.** Dark visor band or simple eyes-only decal, TBW style.
   No mouths, no noses. At this camera distance a face is 6 pixels — spend them on expression
   angle (eyebrow decal tilt), not anatomy.
5. **Outlines finish the look.** Linework Lite is already wired (`Free Outline Settings.asset`).
   Recommend: constant-width ~2px near-black (`#060810`) outline on **all** units at all times
   (rendering layer bit), with the existing cyan selection outline kept as the *planning*
   highlight. Dark outline + flat color + matte = the TBW cel read without a toon shader.
6. **Delete the capsule for good.** Base `Unit.prefab` still carries the capsule MeshRenderer
   (disabled per-variant). Remove the renderer from the base prefab (collider stays) so no
   variant can regress to capsule-look.

---

## 5. Ability FX (Clash Mini layer)

Doctrine, in priority order — every ability moment gets:

1. **Anticipation** (0.2–0.5s): something charges. Reuse the existing `delayForDodge` windows —
   they are *already* anticipation timers, just invisible ones. Give them a visual body:
   ground ring shrinking inward (`ImpactShockwave` reversed), caster glow ramp, rising whine SFX.
2. **Impact frame** (1–3 frames): the Clash Mini secret. At the hit instant: hit-flash the
   victim white (`HitFlash`), pop the FX to 2× width for ~0.08s, camera shake (exists —
   `CameraEffects.CameraShakeClientRpc`), optional 0.05s time-freeze on lethal hits.
3. **Aftermath** (0.3–0.8s): expanding shockwave ring (`ImpactShockwave.Spawn`), sparks,
   fading glow. Never linger past 1s — rounds are fast.

**Scale rule:** an ability's peak on-screen footprint should be 2–4 unit-widths. The Clash Mini
lightning fills a third of the board; our Area Lock beam should feel the same, while ordinary
bullets stay small so the contrast sells the wow.

Per ability (all buildable with `BeamVFX`, `ImpactShockwave`, `HitFlash`, `BP_GroundGlow`,
particle systems — no VFX Graph):

| Ability | Current visual | Target |
|---|---|---|
| **Sniper Area Lock** | Thin red `Sprites/Default` line, primitive-sphere burst | The flagship, modeled directly on `AbilityDesign.png`: idle = double-layer beam (`BeamVFX`: 0.08 white HDR core + 0.5 team-red soft glow) with slow pulse; on trigger = core swells to 0.35, jagged width noise along the line, victim hit-flash, shockwave ring + spark burst at impact, shake. Replaces both `CreateLaserLine` paths and `CreateExplosionEffect` in `AreaLock.cs` |
| **Sniper Target Lock** | White→red LineRenderer ramp | Thin `BeamVFX` at 25% intensity + pulsing lock diamond (GroundGlow quad) under the marked unit — a threat, not an attack; must never out-shine Area Lock |
| **Grenade** | Prefab arc + particle explosion | Keep arc; add `Alarm`-yellow blinking fuse glow accelerating over the timer, `BP_GroundGlow` disc pulsing on the landing cell (dodge telegraph doubles as anticipation), explosion gains white flash sphere frame + `ImpactShockwave` + shake |
| **Shield Rush** | Transparent cube toggles on | Shield materializes: `ImpactShockwave` ring at activation, hex/bevel shield face with fresnel-ish additive edge (Shield.mat upgrade or `BP_GroundGlow` on the shield quad), team-blue emissive rim; blocked-shot feedback = brief flash + spark at block point |
| **Pogo Jump** | Bare transform hop | Launch: crouch squash (Animator) + ground ring; flight: short `BeamVFX` streak trail; landing: `ImpactShockwave` + dust puff + small shake. Backstab hits get a `TeamRed` slash flash on the victim |
| **Commander Override** (when re-enabled) | none | Team-colored `BP_GroundGlow` pulse from Commander to each rerouted ally along the ground, allies hit-flash team-blue once when their path swaps |

**Bullets & deaths** (cheap, huge juice-per-effort):
- Muzzle flash: 2-frame additive quad + tiny point-light pop (Bullet Echo's firefight sparkle).
- Bullets: swap `BulletBlue/Red` to emissive HDR versions so tracers bloom (note `BulletRed.mat`
  currently contains *blue* values — bug, fix during the pass).
- Impacts: 3–5 spark particles + victim `HitFlash`.
- Death: unit hit-flashes white, outline flares, collapses with a scale-down + team-colored
  dissolve or shockwave — a Clash Mini "pop", not a ragdoll.

**Fog interaction (forward-compat):** all FX above are additive, queue 3000+, and fog is a cell
overlay at ~α0.45 — beams crossing fogged cells will still read, which matches the Phase-1 fog
design (bullets/telegraphs stay visible). Vision cones must render *under* units but *over* the
fog overlay: keep cone y-offset above the fog quads' y.

---

## 6. UI & HUD

- Keep uGUI/TMP. Restyle toward Bullet Echo's HUD: thin (2px) off-white line-art icons and
  frames on transparent dark panels (`Ink` @ 70%), no filled buttons in-match.
- Health bars: team-colored fill, 1px off-white border, damage shows a brief white "chip"
  segment before draining (impact-frame doctrine applied to UI).
- Ability cards: dark panel + line-art icon; ability-armed state keeps the yellow but as a
  glowing border, not a fill.
- Damage/heal numbers: TMP world-space pop (scale-in, drift up, fade) — `+100`-style, team/Alarm
  colored. (Not built yet; listed in wiring guide as follow-up.)
- Fonts: Cascadia Code is fine for the techy noir read; consider a heavier display weight for
  round banners ("EXECUTE", "VICTORY") later.

---

## 7. New Asset Manifest (already in repo, safe — nothing existing was modified)

| Asset | Purpose |
|---|---|
| `Assets/Shaders/BP_EnergyBeam.shader` | Additive beam: white HDR core + colored glow, scroll/pulse, for LineRenderers |
| `Assets/Shaders/BP_GroundGlow.shader` | Additive radial disc/ring for telegraphs, shockwaves, lock markers |
| `Assets/Shaders/BP_VisionCone.shader` | Soft light-shaft gradient for procedural vision cone meshes |
| `Assets/Scripts/VFX/BeamVFX.cs` | Two-layer beam builder + pulse/rush/fade API (drop-in for AreaLock/TargetLock lines) |
| `Assets/Scripts/VFX/VisionConeVisual.cs` | Procedural raycast fan cone mesh (Bullet Echo flashlight), purely local |
| `Assets/Scripts/VFX/ImpactShockwave.cs` | One-call expanding ring + light flash at a point |
| `Assets/Scripts/VFX/HitFlash.cs` | White material flash + scale punch on damage, MaterialPropertyBlock-based |
| `Assets/Materials/FX/FX_BeamBlue.mat`, `FX_BeamRed.mat` | Beam materials (HDR team colors) |
| `Assets/Materials/FX/FX_GroundGlow.mat` | Shared glow/ring material (tinted per-instance) |
| `Assets/Materials/FX/FX_VisionCone.mat` | Cone material |
| `Assets/Materials/FX/Map_FloorDark.mat`, `Map_FloorDarkAlt.mat`, `Map_WallDark.mat` | Dark arena set |
| `Assets/Materials/Teams/TeamBlueGlow.mat`, `TeamRedGlow.mat` | Emissive team accents |
| `Assets/Settings/GameNoir Volume Profile.asset` | Game-scene post: ACES + bloom + vignette + grade |

Wiring steps (scene/prefab edits that need the editor) live in
`docs/design/ArtDirection-WiringGuide.md`.
