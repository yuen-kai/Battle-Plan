# Art redesign — integration checklist

Maintained by the coordinator. This is the running list of work that must happen in the leased
Unity verification barrier, once all writers have handed off. Items arrive here from worker
handoffs and from findings made along the way.

Nothing in this file should be actioned by a writer. The barrier has one owner.

## FIFTH BARRIER PASS — expected final

Pass four (230/232) was blocked by the colour-space tripwire correctly firing: `Material.GetColor`
returns sRGB, not linear, so `HitFlash.IsContourValue` compared a gamma measurement against a linear
ceiling and matched nothing. Fixed, and a **second instance of the same root cause** was found in
the lift itself while fixing the first — the ramp was interpolating from a gamma start point toward
a linear target, three stops too pale at rest.

| Check | Assert |
| --- | --- |
| **Contour predicate, corrected** | `HitFlashContourEditModeTests`, all 3 green: `Char_Black` ≤ 0.028 linear, `Char_CoatNavy` > 0.028, ceiling inside the 0.0247–0.0339 gap. **The contour test must still fail if fed the raw gamma reading** — confirm this by inspection of the test body, not by breaking it. |
| **Rim held at rest value** | Under flash, rim reads exactly 0.1640 (or renders it, rather than approximating) — it is not written at all. |
| **Rim-to-puck contrast, revised expectation** | Earlier figures of 3.5:1/3.8:1 used display values in the WCAG formula, not relative luminance, and are superseded. On proper relative luminance (matching the 2.082:1 rest measurement), expect **≈6–8:1** under flash. |
| **Victim peak, revised expectation** | The lift-ramp fix means peaks come down from pass 4's measurement, **not a regression**: expect ≈0.67 (bullet), ≈0.745 (Area Lock), down from 0.703/0.761. |
| **Area Lock impact — judgement** | Prediction: resolves to reading as two events **without touching any lever**, because the frame was missing a *value* (rim collapsed into the body) rather than missing contrast. If it still reads as one event, the diagnosis is contour **width** in screen pixels at the tactical camera, not `DarkeningFlashStartDiameter` or `MaxFlashLift` — a §4.3.1 floor question, not a brightness one. |
| **Menu: Sniper title piece pinned** | 10 of 17 slots were on embedded pre-redesign materials (pure black / pure white, invisible to the saturation guard which checks chroma, not value) because every mesh name the piece uses appears twice in `Sniper.prefab` under two subtrees with different materials. Explicit pins should now resolve all slots; confirm the title sniper no longer reads as a black cut-out. |
| **Menu: header contrast** | `Assemble your crew` measured ≈1.5:1 against `--bp-table`, surfaced by the new background. Diagnosis routed to UI, fix pending — check whether it landed. |

## FOURTH BARRIER PASS — completed, 230/232, blocked on the colour-space tripwire

Pass three was 229/229 green and verified the target laser and portraits, but did not close: the hit
flash was lifting the ink contour along with the body, and the Area Lock ground disc never became
legible. Both are fixed, along with the menu scenes' 3D layer, which had never entered the direction.

| Check | Assert |
| --- | --- |
| **Hit flash — contour held at ink** | The rim's darkest-pixel-per-column under flash must be **exactly** its 0.164 rest value, not approximately. It is no longer written at all, so **any** movement means the predicate missed. Rim-versus-puck contrast must **exceed** its 2.33:1 rest figure — expect ≈3.5:1 bullet, ≈3.8:1 Area Lock. Assert the direction, not just the number: the contour must now *strengthen* during the flash, where the defect made it weaken to 1.88:1. |
| **Colour-space tripwire** | The new edit-mode test must pass. Its failure mode is silence — if the assumption breaks, the predicate matches nothing, the flash still looks plausible, and the defect returns with no symptom. |
| **Area Lock disc legible** | The deck immediately outside the victim's puck at the peak frame: expect **0.55–0.62**, down from 0.788. Naive multiply says 0.552 and the tonemap's higher gain at low values will lift it, so **treat a drop of at least 0.17 as the assertion**, not the absolute figure. The disc was never missing — it was occluded by the victim's own 1.35 opaque base slab sitting above the telegraph plane. |
| **Do not raise `MaxFlashLift`** | The victim is at the ceiling of what brightening can buy: the tonemap shoulder means +0.056 albedo delivers roughly +0.03 rendered, and full lift collapses every material on the unit to identical paper. If the impact frame still reads as one event, the lever is `DarkeningFlashStartDiameter`. |
| **Menu scenes** | `Battle Plan → Art → Verify Menu Scenes` (writes nothing, its log is the diff), then `Build Menu Scenes`, then `Verify` again — must report **0 value(s) would change** on all three. Every piece-placement line must be a Log, not a Warning; a warning means a piece clips the 2% frame margin. Then re-run `Build Arena` and confirm the clear colour now reports a write rather than a warning, with sun and ambient still reporting no drift. |
| **Menu scenes — Play** | Title screen pieces have Animators, so the builder's logged rectangles are rest-pose bounds. A generic clip can carry a root curve and a swinging limb can exceed the rest silhouette. Confirm both in the same session. |
| **Judgement — impact frame** | With the disc now opening at 2.2 and holding full coverage, does the Area Lock impact read as **two events**? |

## THIRD BARRIER PASS — completed, 229/229 green, did not close

Second pass was 219/219 green with the projectile regression confirmed fixed. It stopped on the
tracer being lighter than the deck and on a missing portraits test. Both are fixed, plus two live
FIELD DAY violations found along the way. This pass verifies those four and closes out.

| Check | Assert |
| --- | --- |
| **Bullet tracer darker than deck** | Re-measure off a live frame; expect **0.750–0.765** against deck **0.788**. The model tracks the previous measurement to within 0.014 but the margin on the mean is only 0.038. **Report the leading half of the streak separately from the whole** — the nearly transparent tail drags the mean toward the deck by construction, and the head is what a player reads. |
| **Portraits test** | `UnitPortraits`, ten cases, all green. |
| **Target laser — monotonic direction** | Sample mid-lock and full lock: expect ≈**0.72** and ≈**0.70** against 0.788. **Darker at both, and darker at full lock than mid-lock.** The defect was that emphasis ran *backwards* — loudest when the threat was newest — so a single static reading would not catch a reversal. |
| **Target laser — composite state** | On a spawned Sniper: `_CompositeMode` 1 **together with** `_SrcBlend` 5 and `_DstBlend` 10. Mode passing alone is exactly the false positive that hid this for the whole redesign — the mode picks a shader branch, the blend factors are separate render state. |
| **Hit flash — legal brightening** | It stays a *brightening* flash deliberately; do not report that as unconverted. Peak frame: victim mean ≈**0.86** on a bullet hit, ≈**0.92** on Area Lock. Confirm **the ink contour is still continuous at both poles** — that contour surviving is the entire reason it was not inverted. Confirm **no bloom attributable to the victim**: the flash now writes no emission at all, so any glow on a unit body during the impact frame means something else is emitting. On the frame after, re-measure at rest and confirm it matches the pre-hit value, proving the property block cleared. |
| **Human judgement — one frame, both effects** | Area Lock impact with its ground disc going to ink *under* a victim lifting to 0.92. Do the two read as separate events or one smudge? Lever if not: `MaxFlashLift`. |
| **Human judgement — ordering** | Area Lock and the target lock visible together. §8.2 requires the target lock never out-reads Area Lock. Per-pixel the target lock is marginally darker (0.701 vs 0.719) and on a bright board darker is more emphatic, so the ordering survives on *extent* — Area Lock is 0.34 wide against 0.20, carries an ink contour at 1.24×, and adds a licensed HDR core. A luminance sample cannot settle this. **Which does the eye go to?** Lever if the target lock wins: `LockAlphaFull` toward 0.75 — **not** the hue, which §8.2 fixes. |

## SECOND BARRIER PASS — completed, 219/219 green

The first barrier passed everything except a projectile regression. These are the additional checks
for the follow-up pass. Edit mode unless stated; one short Play session at the end.

**Run `Battle Plan ▸ FX ▸ Build Projectile Prefabs` first.** The projectile prefabs are unchanged on
disk until it runs, so every projectile assertion below fails before that.

| Check | Assert |
| --- | --- |
| **Projectile team materials** | For each of `BulletBlue`, `BulletRed`, `SniperSuperBlue`, `SniperSuperRed`: the material the runtime would assign `Is.SameAs` the authored material on the renderer it actually repaints. Find that renderer the same way the runtime does — `GetComponentInChildren<Renderer>()` returns the **root TrailRenderer**, not the body. `Is.SameAs` compares the asset reference, so a duplicate with an identical name still fails. |
| **Projectile body material** | `Mesh` child's `sharedMaterial` is non-null and correct per prefab. Body `renderQueue == 2000`, `_ZWrite == 1`, RenderType `Opaque`; contour `BattlePlan/FXUnlit` at queue 3045, `_Cull == 1`. Body queue strictly below contour queue — the inverted-hull outline depends on the body writing depth. |
| **Contour world scale** | `Contour.lossyScale / root.lossyScale`, tol 1e-4: bullet `(0.152, 0.24, 0.152)`, sniper `(0.132, 0.71, 0.132)`, grenade `(0.382, 0.24, 0.382)`, canister `(0.522, 0.43, 0.522)`. |
| **Builder idempotency** | Run the projectile builder twice, `git diff` empty on run 2. `SniperSuperBlue` must gain its override after run **1**, not run 2. |
| **Portraits are live** | Per unit: `data.unitSprite` non-null, path is `Assets/Images/Portraits/{name}.png`, does **not** contain `Portrait_`, and **`rect.size == (512, 384)`**. That last line is load-bearing — it fails under the sprite bug, under the NPOT bug, and if anyone repoints to the old art, all three of which leave a file present. Importer: type `Sprite`, mode `Single`, npotScale `None`. Test must live in `Assets/Scripts/GameManager/Editor/` — the UI test asmdef has empty references and cannot see `UnitData`. |
| **Retired generator** | `Battle Plan ▸ Regenerate Map Preview` no longer in the menu. |
| **Map preview test** | `CharacterSelectionPreviewMatchesExpandedMap` gone; `CharacterSelectionPreviewShipsCapturedBoardArt` passes. |
| **No green on the board** | Health fill is team-coloured, not `#5CF28C`. It is a **world-space** canvas inside `Unit.prefab`, so it is in the 3D scene. |
| **Rendered confirmation (Play)** | One frame with a bullet mid-flight over the deck: continuous dark rim on all sides of the slug. The state checks prove the technique *can* work, not that the rim is unbroken at the capsule poles. Then judge the three post-swap FX expectations, which could not be assessed on the first pass. |

Note `SilhouetteSheet.png` had the same NPOT defect and was resampling its five 256 px cells into
misalignment. Fixed — the sheet will re-import at true size, so it will look slightly different from
the one already reviewed. The owner's ruling that Commander and Soldier do **not** collide stands
regardless and is not reopened.

## Barrier procedure

1. Wait for all writers, collect handoffs, close the batch.
2. Freeze writes under `Assets/`, `Packages/`, `ProjectSettings/`.
3. Refresh and import once. Await domain reload. Require zero relevant compile errors.
4. Run the build scripts in the order given below.
5. Run the queued serialized checks and edit-mode tests.
6. One Play session, batching every runtime verification. Capture screenshots.
7. Stop Unity, release the freeze.

Any Unity-relevant write after step 3 begins makes the evidence stale and restarts the barrier.

## Build scripts to run

Order matters — the arena defines the surfaces that the effects sit on and read against.

| # | Step | Owner | Notes |
| --- | --- | --- | --- |
| 1 | `Battle Plan/Art/Build Arena` on `Assets/Scenes/Game.unity` | arena | `Assets/Editor/ArenaBuilder.cs`. Expect `Deck constraint holds` in the console, no `[Arena]` errors, and the sun log reporting a 1.56 m shadow at 0.58 of a cell. **A `Left as found` warning means the scene environment has drifted from the recorded daylight values — investigate before trusting the look.** |
| 2 | `Apply Material Palette` | arena | Writes the 18-row `MapPalette` table to the materials. `ValidateDeckConstraint` errors out if any deck row exceeds 12% saturation or if `Map_DeckA`/`Map_DeckB` leave the 0.62–0.70 window. |
| 3 | `Apply Post Stack` | arena | **Corrected after the barrier — this row was stale.** Writes **`GameDaylight`** in place; `GameDiorama Volume Profile.asset` does not exist and was deleted. Actual values are Neutral tonemapping, **contrast +14, saturation +8** (this row previously had the two transposed), bloom threshold 1.8 / intensity 0.5 / clamp 8, vignette retained, grain and fringing and defocus and motion blur off. Matches `ArtDirection` §7.8. Verify White Balance and Split Toning appear as genuinely new overrides with `overrideState=true`, not silently no-oping. |
| 4 | `Apply URP Shadow Settings` | arena | — |
| 4b | `Battle Plan → Art → Measure Irradiance Transfer` | arena | **Blocks publication of palette §1.4.** Renders 0.50-grey unlit quads in four orientations off the live scene with post explicitly disabled and reads back centre pixels. Decides how Unity reads gradient-ambient colours: a side-on factor near **0.28** means linear, near **0.16** means authored sRGB. The two candidates are far enough apart that one reading is decisive. This term is dominant — it swings the side-on row by 1.75×, more than the entire horizontal-to-vertical effect the table describes. |
| 5 | `Battle Plan ▸ FX ▸ Bake Sprite Masks` | vfx | `Assets/Editor/BattlePlanFXMaskBaker.cs`. **Expect `T_FX_MuzzleStar` and `T_FX_Scorch` to change from what is on disk — those two are corrections, not drift, and should be accepted.** The other five bake out visually equivalent. After this first bake the script is the source of truth. |
| 6 | `Battle Plan ▸ FX ▸ Build FX Prefabs` | vfx | `Assets/Editor/BattlePlanFXPrefabBuilder.cs`. Creates `Assets/Resources/FX/` and the nine prefabs `FXAssets` resolves by path. Wholly generated and referenced only by path, so rebuilt from scratch each run. |
| 7 | `Battle Plan ▸ FX ▸ Build Projectile Prefabs` | vfx | Attaches `ProjectileFX`. Opens prefabs with `LoadPrefabContents` and saves back to the same asset — **never deletes or recreates**, so the `DefaultNetworkPrefabs.asset` GUIDs survive. |

The FX builder captures a text description of every collider's type, dimensions, centre and **lossy
scale** before mutating, compares afterwards, and **refuses to save** if anything moved. The lossy
scale check is the load-bearing one: `Shooting.cs` line of sight is a projectile-radius `SphereCast`,
so scaling the wrong transform silently changes what a weapon can shoot past.

Both builders are idempotent. Verify by running each twice and confirming an identical `git diff` on
the second run.

The builder no longer rebuilds the lighting rig. It aims the sun only (52° elevation, 90° azimuth) and
*asserts* ambient, clear colour and sun intensity rather than overwriting them, so no run can flatten
the daylight setup the direction rests on.

## Codename retirement — Night Range → FIELD DAY

Arena is done (`ArenaBuilder.cs`, `Battle Plan ▸ Art ▸`, prefix `[Arena]`). VFX is done — the
builder became `BattlePlanFXPrefabBuilder.cs` under `Battle Plan ▸ FX ▸`, deliberately on a neutral
path rather than `GameDiorama`, since that is the arena's profile name and could churn again.

| Item | Owner | Status |
| --- | --- | --- |
| All VFX references — builder, `FXAssets`, `FXPalette`, `ProjectileFX`, `MatchEndFX` | vfx | **done** |
| `Assets/UI/Shared/*.uss` header comments | ui | in flight with the swap; one occurrence still reported in `TacticalToyboxTokens.uss` |
| `docs/design/ArtDirection.md` line 3 (codename) and line 787 (retired profile path; the replacement is `Assets/Settings/GameDiorama Volume Profile.asset`) | art director | pending |

Note: references to Night Range **inside changelog sections** of `ArtBible-Palette.md` and this file
are correct and should stay. A changelog naming what it superseded is doing its job.

## To measure during the Play session

| Item | Fallback if it reads badly |
| --- | --- |
| Two-tone shockwave overdraw on the **grenade** ring specifically. It is 8.64 units across, over three cells, and the dark twin doubles transparent overdraw. Small impacts are trivial. | Skip the dark ring above ~3 units of radius, keeping it on small impacts where the edge does the most work. Deliberately left as a measured number rather than a guessed threshold. |

## Scene edits

| Item | Why | Verify |
| --- | --- | --- |
| ~~Enable post-processing on the `Game.unity` cameras~~ | **CLOSED — no action needed.** An earlier pass reported this disabled. Verified directly against the scene YAML: `m_RenderPostProcessing: 1` at `Assets/Scenes/Game.unity` line 13920. It is on, and was independently confirmed by the arena implementer's live measurement. | — |
| **Camera viewport rect → full-bleed** | The current rect `x: 0, y: 0.112, width: 1, height: 0.83` reserves 63 px top and 121 px bottom at 1080p for the two HUD bars the redesign removes. Set to **`x: 0, y: 0, width: 1, height: 1`**. Serialized scene change only; nothing overrides it at runtime. **Independently corroborated:** `ArtDirection` §5.5 states the same 63/121 reservation, derived separately from the serialized rect, so the guard asserts the documented target rather than one person's arithmetic. | Run `GameSceneCameraRendersFullBleedBehindFloatingHud`. It parses `Game.unity` and currently fails naming the exact fields; it passes once the scene is edited. |
| Attach the audio manager, remove superseded scene AudioSources | Music currently plays from AudioSources wired directly onto scene objects. The dead `AudioManager` stub was rehabilitated into a prefab-facing façade with `uiSource` / `ambienceSource` rather than replaced, so existing references survive. | Per the audio handoff. |

## Audio import

| Item | Notes |
| --- | --- |
| Import the new `Assets/Audio/**` tree | 82 clips (76 SFX, 6 music loops) plus the AudioMixer asset. All carry hand-written `.meta` files with stable GUIDs and per-clip import settings, so the import should be uneventful. |
| Compile check | 9 new scripts under `Assets/Scripts/Audio/`, plus 11 touched gameplay files carrying call sites. |
| Verify the narrow relay | Shield-block and wall-impact route through a deliberately constrained relay on `GameLoop` carrying a position and a two-value enum, because bullet collisions resolve server-side and despawn in the same frame. Confirm it has not been widened into a general "play any sound" channel. |
| Verify fog silence | Footsteps are derived from replicated transforms rather than movement hooks, so a fog-hidden unit is silent because it is never spawned locally. Confirm no positional cue plays for an unseen enemy. |

## Asset and settings fixes

| Item | Status | Notes |
| --- | --- | --- |
| Saira Condensed SDF assets, both weights | **done** | Generated at the spec'd paths. All required glyphs verified present, including `·`, `—`, `…`, `×`. Single 1024² atlas each, ~88% fill. Static population so a build cannot silently drop a glyph. |
| Rendering layers `SelectionOutline` (mask 4) and `ThreatOutline` (mask 8) | **done** | List indices 2 and 3. |
| `BattlePlan/*` shaders in Always Included Shaders | **partly done — action required** | `EnergyBeam`, `GroundGlow`, `VisionCone`, `FogOverlay` registered. **Add `FXParticle` and `FXUnlit`.** They are reachable via the material-to-prefab dependency chain from `Assets/Resources/FX/` and no `Shader.Find` targets them, so they should survive a build — but a prefab-builder mistake fails silently as magenta, and two lines is cheap insurance. |
| HDR on the URP assets | **no action needed** | Already enabled on Low, Medium, High, and the base template. |
| `Free Outline Settings.asset` bit values | **done** | Now Silhouette 2, Selection 4, Threat 8, list order unchanged so Threat draws on top. `layerMask 192` untouched. |
| **Raise MSAA on the Medium quality tier — to 4×, not 2×** | **action required — broker.** §4.3's anti-aliasing clause is `w · renderScale · √MSAA ≥ 1`, mandatory, target 2. **Render scale matters as much as MSAA and is easy to miss:** High is at render scale 2.0, so it gets 4.4 samples across the rim and is comfortable. Medium today gets **0.78 and fails outright**. The originally planned 2× brings it to 1.10 — which makes the law *valid* on that tier but marginal; **4× is where it stops being marginal.** Low sits at render scale 0.37 and is out of scope by construction — do **not** try to fix Low by fattening contours globally. |
| **Rename rendering layer mask 2, `SelectedUnit` → `AllUnits`** | **approved, action required** | `ProjectSettings/TagManager.asset`. Verified safe: nothing in `Assets/Scripts/` resolves rendering layers by name, and the only name lookups are `LayerMask.NameToLayer`, which reads the separate physics layer table. The mask drives the all-units Silhouette outline, so the current name is actively misleading. |

## Corrections to the art bible, ruled by the coordinator

| Item | Ruling |
| --- | --- |
| **Sun angle.** §7.6 states the shipped rotation `(50, −30, 0)` is "preserved exactly." | **Overruled — keep 52° elevation / 90° azimuth.** The camera flips 180° in yaw between seats, so an off-axis sun throws cover shadows toward one player and away from the other, letting one seat read its approach cells through shadow. Competitive fairness outranks aesthetics. Elevations differ by 2°; the disagreement is azimuth only. The bible said "preserved" because it read the shipped scene, not because it evaluated the change already made to it. |
| **"Delete the capsule for good" on `Unit.prefab`.** | **Do not act on literally.** There is no capsule `MeshRenderer` in this hierarchy. The base prefab's three renderers are `BasePuck`, `BasePuckRim`, and `VisionCone`; the built-in-resource GUID belongs to the ground puck. Deleting "the capsule" would delete the puck. |
| §10 palette verification snippet | **Fixed in place.** First `rg` was missing `-r '$1'` and reported all 79 consumed tokens as undefined even against a correct file. Verified: 79 consumed, 178 defined, 0 undefined. |

## Facts established the hard way — do not re-derive

| Fact | Why it matters |
| --- | --- |
| All five units nest real FBX models plus weapons (Commander+Pistol, Soldier+Rifle, Shotgunner+Shotgun, Sniper, PogoRider). | Counting `MeshFilter` in prefab YAML **understates** — variants store only overrides and nested instances store none of their source's components. Resolve `m_SourcePrefab` GUIDs instead. |
| `TeamRed.mat` is runtime-assigned, not dead. | `Unit.SetTeamIndicators` writes `teamMaterials[0]`/`[1]` onto children tagged `TeamIndicatorProp`, so it is referenced through a serialized list rather than by any renderer. |
| `VisionCone` is enabled on every unit variant with no `m_IsActive` overrides. | In edit mode no runtime fog code disables it, so any capture or bounding-box fit must hide it explicitly or the subject becomes a speck. |
| `Char_CloakGray` and `Char_KhakiGear` appear genuinely unused. | Flagged for cleanup, not deleted. |

## Open decisions

| Decision | Context |
| --- | --- |
| ~~Risk 11 — multiply effects over dark surfaces~~ | **CLOSED.** Resolved structurally on the Y-order contract: ground-plane effects (y ≤ 0.13) multiply, everything above uses alpha with a contour that inverts to paper on a dark host. No runtime mode switching. The governing law is that **an effect must never change appearance along its own length** — a seam at a cover boundary reads as information, which is worse than either an inconsistent or an invisible effect. |
| ~~Fog boundary~~ | **RULED AND APPLIED.** Neither offered option — `--bp-fog-edge` itself moved to near-ink `#1E262E`, giving 3.98:1 against the fill and 3.39:1 against unfogged deck at 0.85 alpha. A fog boundary is a contour and contours are ink. Alpha lives on `_BoundaryColor`; `_BoundaryOpacity` is pinned at 1.0 and is **not** a second alpha. Applied by the coordinator to both `Assets/Materials/Visuals/FogOverlayCell.mat` and the `BP_FogOverlay.shader` property default; verified no stale value remains. |
| ~~Additive flashes~~ | **RULED.** §9.2.1 governs but gained a qualifier rather than an exception list: **Additive is permitted for effects at or under one cell and 0.15 s.** The seam problem is about extent and dwell — it applies to a twenty-cell beam standing still, not a sub-cell quad alive for two frames. §6 and §8.5 are now instances rather than deviations. Nothing shipped changes; `FXPalette.FlashComposite` stays Additive. |
| **§4.3's 2 px contour minimum contradicts §8.1's own dimensions.** §8.1's explicit numbers give a **0.75 px** rim at 1080p (37.6 px per world unit at the §7.1 camera). Reaching 2 px needs a 0.106 radial shell, putting the bullet's contour at 0.216 against a 0.11 body — an outline wider than the projectile, reading as a dark blob with a coloured speck. A 2 px rim per side on a 4.1 px object leaves 0.1 px of fill, breaking §4.4's colour contract to satisfy §4.3's contour law. VFX's read, which I share: §8.1 is right and the 2 px minimum was reasoned for cell-scale shapes (a cell is 101 px at 1080p) and is being applied outside its domain. The law likely wants "2 px, or a fixed fraction of the shape's own width below some size". **Nothing changed pending the ruling.** | art director |
| **Grenade and smoke canister contours — ruled by the coordinator, open to review.** §4.3's contour law is categorical but §8.3/§8.4 do not ask for one. Ruled **yes**, using the measured shell constant from §8.1 so no dimension is invented. A rule applying to two of four projectiles is an inconsistency, not a rule. Trivially reverted if the art director disagrees. | art director (review) |
| Rename rendering layer mask 2? | It is named `SelectedUnit`, but the art direction assigns mask 2 to the all-units Silhouette outline, so the name now reads backwards. Renaming is not additive and may break name-based lookups. Left alone deliberately. |
| `GraphicsSettings.defaultRenderPipeline` is null | `Assets/Scripts/Renderer/URP.asset` is a dead template referenced by nothing. All three quality levels carry their own URP asset so runtime is fine, but anything reading the default pipeline gets null. |

## Settled by the project owner — do not reopen

| Item | Ruling |
| --- | --- |
| **§11.5 silhouette test, Commander vs Soldier.** The barrier judged them colliding — same cross topology, same arm bar height, differing only by a square head versus a domed head with a thin antenna. | **Owner ruled no collision: Commander already reads distinctly by his cap.** §11.5 passes. Do not remodel, re-pose, or otherwise "fix" these two silhouettes. The three other units were never in question. |
| **`MapPreview.png`** | **Keep the real GPU render of the board.** Replace the byte-comparison test with stable assertions (imports, 1080 × 720, not a uniform fill). The old code-drawn approximation is retired. |

## Known non-regressions — do not chase these at the barrier

| Symptom | Explanation |
| --- | --- |
| The UXML contract script reports four missing element names. | False positive. They are negative assertions on deliberately-absent elements. Pre-existing, not caused by the redesign. |
| `rg` for `--bp-` tokens across `Assets` returns 81 consumed, not 79. | Two are not stylesheet consumers: a truncated prefix inside the easing guard test's `Does.StartWith("var(--bp-ease-")`, and `--toy-sky` inside the smoke test asserting the legacy alias is still declared. **The real consumed count is 79.** Tracking 81 as a token count will drift. |

## Verification

| Check | Notes |
| --- | --- |
| `UIToolkitAssetSmokeTests` | Asserts every UXML element name on all four screens. The UI worker owns any deliberate updates. |
| Full edit-mode test suite | Includes a `MapPreview.png` content-hash assert, a `JoinToyboxStage.png` 1579×885 dimension assert, and a no-raw-rgb rule over the token file. All three are expected to need updating for the redesign. |
| Re-baseline screenshots | `Assets/Screenshots/baseline-pretest/` and `e2e/` will all diff. Re-baseline **after** the visual pass lands, never during. |
| **Easing token resolution** | Silent-failure risk. If Unity fails to resolve `var()` in `transition-timing-function`, the declaration is dropped and the transition falls back to `ease` — motion still plays, nothing looks broken, and every curve in the product is quietly wrong. In the Play session, read it back off any button: `doc.rootVisualElement.Q(className: "button").resolvedStyle.transitionTimingFunction`. **Expect `EaseOutCirc`. If it logs `Ease`, the variable is not resolving** — revert the 22 declarations to literal keywords, a one-command substitution that restores correct motion and loses only the indirection. Cheaper pre-check: an unresolvable variable surfaces as a USS warning on reimport, so a clean import of the seven sheets is good evidence on its own. |
| **Post-swap FX expectations** | Falsifiable, from the VFX handoff. **Sniper beam:** a dark red line scored across pale concrete with a hard ink edge and a thin pale filament, *identical in appearance from muzzle to endpoint including where it crosses a cover block* — any change in value, width or brightness at a cover boundary means the alpha rule was undone. **Grenade:** the ground goes dark *first*, a filled ink disc opening over 0.08 s, with the amber core arriving into that hole — a pale flash means the order is reversed or the wrong core material is wired. **Tracer:** a small dark-edged slug with a trail *darker* than the deck; lighter than the floor means the wrong palette step. |
| Play session | Batch every runtime check into one session per the play-mode rule. Verify: all 18 wall cells read as opaque sight blockers; every ability's three beats complete within 1.4 s; effects never exceed 1.35× their rules footprint; telegraphs remain readable through fog; the Shotgunner burst does not exceed the 4-light pool; hitstop restores `Time.timeScale` and is skipped in dev mode. |

## Reference material to preserve

The user explicitly prefers the daylit board that already shipped. These are the reference and must
not be deleted during cleanup:

- `Assets/Materials/Map/Map_FloorDay.mat`, `Map_FloorDayAlt.mat`, `Map_WallDay.mat`,
  `Map_WallCap.mat`, `Map_Trim.mat`, `Map_HazardYellow.mat`, `Map_HazardBlack.mat`,
  `Map_GlowPool.mat`, `GridCell.mat`, `GridCellOutline.mat`, `Wall.mat` — all tracked and unmodified.
- `Assets/Settings/GameDaylight Volume Profile.asset` — intact, unmodified.
- `Map_Backdrop.mat` is the one modified original; recover with
  `git show HEAD:Assets/Materials/Map/Map_Backdrop.mat` if the pre-redesign values are needed.
- A snapshot of the superseded dark art bible sits at `/tmp/bp-artbible-snapshot/`.
