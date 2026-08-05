# Ability Juice

A closed loop for making abilities hit harder, and a record of what it has found so far.

## The bar

An unlabelled three-frame strip of one of our abilities — wind-up, impact, aftermath — placed next
to the same three frames from a Clash Mini ability, where a stranger picks ours as the one that hits
harder.

Everything below exists to make that comparison cheap enough to run repeatedly, and honest enough
that its answer means something.

## Why a rig rather than eyeballing it

Ability VFX is judged in three frames out of sixty. Watching a match and forming an impression is
too slow to iterate on and too generous to trust — the eye forgives things the frame does not. So
the loop films the shipping code path at a fixed clock, cuts the three frames that decide the
question, and hands them to a critic who measures pixels.

The first round of that loop is why this document exists. Eight effects were built by eight people
who all believed they were building something dramatic. All eight were rejected, and four critics
independently found the same root cause, which nobody had suspected and which no amount of looking
would have surfaced.

## The luminance law

**The board floor sits at luminance 181–184 out of 255.** That leaves 73 levels of headroom upward
and 182 downward.

Clash Mini's board is mid-value saturated green at luminance ≈148, so a white core there gets both
value contrast and hue contrast for free. Ours gets neither. Every effect in round one spent its
whole budget brightening a surface that is already nearly white:

| Effect | What it achieved | Against |
| --- | --- | --- |
| Impact core | peak luminance 221, **zero clipped pixels** | floor at 182 |
| Shockwave front | **+14** luminance over the floor | floor tile-to-tile variation is **18** |
| Debris chunks | half within **21%** contrast of the floor | — |
| Aftermath burn mark | **27 points brighter** than the floor | it was meant to be a burn |
| Damage number (amber) | **7 levels** from the board | invisible in greyscale |

The rule this implies governs every effect on this board:

> Reach for **dark and opaque** before bright and additive. Additive glow is a garnish here, never
> the structure. If an effect's silhouette is not made of material darker than luminance 182, it
> does not have a silhouette.

Verify occlusion the way the critics did: find a tile seam under the effect and measure its
contrast. Clean floor seam contrast is ≈39; under genuinely opaque material it must fall below 8.

Three corollaries, each measured:

- **Saturation.** Our effects were desaturated too, so they had no hue contrast either. The impact
  core measured saturation 0.04 — *less* than the board beneath it, meaning it bleached hue out of
  the frame. Clash Mini's bright zones measure 0.38–0.90. Colour must be ≥0.45 where it appears.
- **Edges.** Our shockwave front ramped over 16px: ≈0.9 luminance per pixel. Clash Mini's dust
  silhouettes measure 49–91. Material edges want a 2–4 pixel step from `clip()`, not a `smoothstep`
  gradient. Soft falloff is for light only.
- **Additive cannot make orange here.** Measured while rebuilding the aftermath: additive red at 2×
  gives saturation 0.29 and at 16× reaches 0.62 only by *falling* to 1.11× board luminance. There is
  no additive value on this floor that is both bright and orange. Burning matter has to occlude.

## The pieces

An ability is not improved as one thing. It is decomposed into nine primitives, each of which can be
built, filmed alone on an empty board, and judged on its own merits.

| Piece | File | What it owns |
| --- | --- | --- |
| `windup` | `Assets/Scripts/VFX/AbilityWindup.cs` | anticipation and the ground telegraph |
| `impactcore` | `Assets/Scripts/VFX/ImpactCore.cs` | the single hardest frame |
| `impactdip` | `Assets/Scripts/VFX/ImpactDip.cs` | darkens the board so the flash has contrast |
| `shockwave` | `Assets/Scripts/VFX/ImpactShockwave.cs` | the expanding ground wave |
| `debris` | `Assets/Scripts/VFX/DebrisBurst.cs` | thrown matter |
| `hitreact` | `Assets/Scripts/VFX/HitReaction.cs`, `HitFlash.cs` | what the victim does about it |
| `camera` | `Assets/Scripts/VFX/ImpactCamera.cs`, `Camera/CameraEffects.cs` | shake, punch, hitstop |
| `aftermath` | `Assets/Scripts/VFX/Aftermath.cs` | what is still there afterwards |
| `numbers` | `Assets/Scripts/VFX/DamagePopup.cs` | the damage read |

Shared vocabulary — `WindupShape`, `AftermathKind`, `DamageTone`, the HDR helpers — lives in
`Assets/Scripts/VFX/AbilityJuice.cs`. All nine are purely local and visual: no authoritative state,
no `NetworkVariable`, no RPC. Everything is built from code at runtime, so no piece depends on a new
prefab or `.asset`.

The five abilities they compose into are Grenade (Soldier), Pogo (PogoRider), Smoke (Commander),
Shield Rush (Shotgunner) and Area Lock (Sniper).

## The rig

### Capture — `Assets/Scripts/GameManager/AbilityFilmStudio.cs`

Editor-only. Runs the real `Ability.ExecuteAbility` coroutine on a real spawned unit in the real
Game scene, so what lands on disk is the shipping code path with the shipping shaders, lighting and
post stack.

Determinism comes from `Time.captureDeltaTime`: every rendered frame advances game time by exactly
one step regardless of how long the readback took, so the same revision always produces the same
frame at the same timestamp and two revisions can be diffed frame for frame.

Three details it took a bug each to learn:

- **The camera target must be a float texture.** URP copies the camera target's descriptor for its
  own intermediate colour buffer, so an 8-bit target silently clamps the whole pre-tonemap pipeline
  at 1.0. Filming into one meant nothing could clip to white and Bloom, whose threshold is 1.8,
  never fired in a single captured frame. The camera now renders to `DefaultHDR` and the result is
  resolved down only for the JPEG readback.
- **Render scale must be forced to 1.** The High quality tier renders the board at 2×; left on, that
  scaling is applied again on the way into a camera target texture and the frame lands in a corner.
- **Dev mode must stay on.** It is what parks the round in planning instead of resolving out from
  under the shoot. Only `Time.timeScale` is dropped to 1.

The studio camera is parented to the board camera, so a camera shake shows up in the captured frames
exactly as a player would see it.

```csharp
DevInput.StartBotMatch(6f);          // wait for GameLoop.currentPhase == "planning"
AbilityFilmStudio.CapturePrimitives("r3");   // or CaptureAll / CaptureEverything
// poll AbilityFilmStudio.Finished
```

Output: `Captures/AbilityJuice/shots/<tag>/<piece>/f0000.jpg…` plus a `frames.csv` of timestamps.

### Compositing — `Tools/abilityjuice/juice.py`

```
juice.py strips <tag>        # find the impact frame, cut wind-up/impact/aftermath, build the strip
juice.py contact <tag> <shot> # whole timeline with timestamps, for diagnosing timing
juice.py refstrips           # build Clash Mini strips from mined wind-up/impact/aftermath triples
juice.py compare <tag>       # blind A/B sheets, ours against a reference, order randomised
juice.py reveal <tag>        # the answer key
```

The impact frame is found automatically: frames are scored by a hot-core detector (99.9th percentile
brightness) blended with bulk change against the pre-roll baseline, so it works for a flash and for
smoke alike. The other two panels are cut at fixed offsets either side.

Blind pairing is seeded from `sha256(tag/shot)`, so a re-run does not reshuffle a sheet the critic
already reasoned about, but the order is unguessable without the key.

### Reference

`Captures/AbilityJuice/reference/` holds 77 frames mined from Clash Mini gameplay footage with
`yt-dlp` + `ffmpeg`, sorted into `windup/`, `impact/`, `aftermath/` and `general/` and described
per-file in `MANIFEST.md`.

Frames were not scrubbed by hand. The miner profiles each video for the fraction of near-white
pixels per frame, which spikes about 9× above baseline exactly when a VFX flash happens — mean luma,
by contrast, moves less than 1% and is useless for this. Each spike is checked against a
scene-change detector to reject editing cuts, then three frames are pulled at **−0.28s, the peak,
and +0.38s**. So `windup/X.jpg`, `impact/X.jpg` and `aftermath/X.jpg` are one cast at three moments,
cut on the same envelope our own rig cuts on. 17 of them form complete triples.

Automated filters removed pillarboxing, menus, loading screens, title cards and near-duplicates, and
every survivor was then reviewed by eye on a contact sheet — cheap image statistics cannot reliably
tell a game board from a menu, which an earlier uncurated pass proved by ranking a chest-opening
animation as the strongest reference in the set. `juice.py refstrips` therefore treats the reviewed
top-level folders as authoritative and only falls back to `_staging/cand` if they are empty.

Clash Royale was available as a fallback and turned out to be unnecessary: its gameplay footage
frames the board too small to be useful, while the Clash Mini material was plentiful.

The strongest single reference is `cm-everyability-22`: a pink AoE telegraph snapped to whole tiles
with victims still standing on them, a pure-white sphere with a saturated magenta rim, then a wide
orange scorch disc with damage numbers and rising smoke. It is the exact three-beat structure our
nine pieces are built to produce.

**The Supercell formula, as observed across the set:**

- **Three-layer value structure, with identity only in the rim.** Pure-white core, saturated
  chromatic rim, soft tinted haze. The core is white *regardless of the ability* — fire, ice and
  arcane all blow out to the same white. Hue carries the identity: gold/orange for physical and
  fire, cyan for ice and energy, magenta for arcane and death.
- **Everything writes to the floor, twice.** An ability lights the ground before it fires and stains
  it after, and on a grid both snap to whole tiles rather than drawing a circle.
- **Hard-edged geometric shapes, never particle mush.** Four-point star sparks, crisp rings, clean
  silhouettes. Even smoke is stylised as chunky rounded puffs.
- **Timing is a spike, not a swell.** Telegraph ≈0.3s, blowout one or two frames, decay 0.3–0.5s,
  total life under a second, with no slow ramp on either side.
- **Readability is never sacrificed.** Effects are loud but brief; unit silhouettes and health bars
  stay legible through them, and blasts overflow the board edge rather than being clipped.

### Progress page

`Captures/AbilityJuice/index.html` polls `state.json` and shows every piece's current strip, status,
critic verdict and thumbnail history. `Tools/abilityjuice/progress.py` is its only writer.

```
cd Captures/AbilityJuice && python3 -m http.server 8777
```

## The loop

For each piece, per round:

1. A **builder** with fresh context implements it, reading `BUILDER_BRIEF.md` and
   `MEASURED_FINDINGS.md`. Builders never run Unity — one process holds the editor lease.
2. The broker compiles once, films once, and cuts strips.
3. A **critic** with fresh context opens the actual frames, holds them against Clash Mini, measures,
   names the single biggest remaining gap, and returns `PASS` or `REJECT`.
4. The gap goes back to the builder verbatim.

Builders own disjoint files so they can run in parallel in one working tree. Shaders are namespaced
per piece (`BP_<Piece>*.shader`) so no two builders can collide.

The critics are the load-bearing part. Told to be harsh and given the frames, they measured tile-seam
contrast, tracked ballistic apexes against ground shadows, fitted camera transforms across frames,
and counted clipped pixels. Every quantified finding in this document came from a critic, not from a
builder's own account of their work.

## State

**Round 0 — baseline.** All five abilities filmed from the running game. The grenade's impact was a
pink smudge a third of a cell wide; the pogo landing was invisible; the aftermath panel was
indistinguishable from the wind-up panel.

**Round 1 — eight primitives built and filmed. All eight rejected.** Findings are in
`MEASURED_FINDINGS.md` and summarised above. Strips are in `Captures/AbilityJuice/strips/r1/`.

Five rounds have run. The headline result is at the bottom of this section; the short version is
that the bar has **not** been met, the margin is now narrow on two of three abilities, and the three
reasons are measured and named.

**Round 2 — nine pieces rebuilt, filmed and judged. One passed, eight rejected.** Strips are in
`Captures/AbilityJuice/strips/r2/`; the cross-cutting findings are in the round 2 section of
`MEASURED_FINDINGS.md`.

| Piece | Verdict | The gap now |
| --- | --- | --- |
| `camera` | **PASS** | none a camera change can close |
| `impactcore` | reject | the core holds full size for 33ms, so the shipped panel is a donut |
| `debris` | reject | light and matter peak 230ms apart, so no frame carries both |
| `impactdip` | reject | worth 2.7 luminance levels on the frame that matters |
| `shockwave` | reject | a machine-perfect circle at saturation 0.153 |
| `aftermath` | reject | zero pixels both bright and saturated; the fire is cream |
| `hitreact` | reject | the flash blooms away the unit's own outline and team colour |
| `windup` | reject | the two wind-up panels tint the identical nine cells |
| `numbers` | reject | 83% of the badge is chrome; the digit is smaller than Clash Mini's |

The important thing about that table is that almost every round-1 gap is **verifiably closed**. The
dust occludes (a named tile seam went from 46.9 contrast to 4.6), the core clips (158,228 pixels at
pure white where there were none), the debris is charred and hierarchical, the squash squashes, the
badge survives greyscale. The pieces failed on new and much more specific problems, which is what
the loop is for.

The two that generalise — the **phase law** (light, matter and colour must peak on the same frame,
held ~100ms not 33ms) and the **bleaching law** (over-driving HDR clips two channels and destroys
hue, so author hot colours to clip one channel only) — are the round-2 equivalent of the luminance
law and should be read before any further work.

**Round 3 — impact core and aftermath passed**, joining camera. The contrast dip was deleted on the
owner's instruction that contrast is not the problem, and a `death` piece was added because the
owner reported that a unit dying and a Pogo rider landing looked identical — they did, almost
literally: `Health` fired `ImpactShockwave(2.2f, 0.5f)` and `Pogo` fired `(1.8f, 0.4f)`, both in
team colour.

**Round 4 — the death/landing separation was solved.** The critic: *"Yes — they are now unmistakably
different. 180° of hue apart, 72 levels of value apart, and it survives greyscale and thumbnail. A
player will not confuse these."* Everything else was rejected on colour area.

**Round 5 — the blind test.** Three sheets, each pairing one of our strips against a Clash Mini
strip of the same three moments, order randomised, labels withheld, judged by a critic who had seen
none of the work.

### The result

**Clash Mini won all three.** The bar is not met.

The margin was not uniform. On the death the critic was *certain*. On the grenade and the Pogo
landing they were only *fairly confident*, and on the landing they said our impact frame **beats the
reference on craft** — hard-edged shards radiating along the ground with real direction, a dozen
discrete debris chunks, and light spill, against what they called a "featureless white ball" and "a
cheat" on Clash Mini's side.

So the craft is arriving. Three things are missing, and they are the same three in every sheet.
They are written up in full in `MEASURED_FINDINGS.md` and are the entire agenda for round 6:

1. **Amplitude.** Our effects change 12–16% of the impact panel; Clash Mini's change 30–39%.
2. **The environment never reacts.** Their explosion lights the room. Ours appears *on* the board
   without changing it — floor tiles two cells from the blast are unchanged.
3. **Persistence.** All three of our aftermath panels measure 0.000% bright-and-saturated; not one
   of seventeen references goes below 0.105%. Their third frame is their biggest; ours is our
   smallest.

### To resume

```
# in Unity: Play → DevInput.StartBotMatch(6f) → AbilityFilmStudio.CapturePrimitives("r6") → Stop
#           then AbilityFilmStudio.Capture("r6", "pogo", "grenade") → Stop
python3 Tools/abilityjuice/juice.py strips r6
python3 Tools/abilityjuice/juice.py compare r6 --only grenade pogo death   # blind sheets
python3 Tools/abilityjuice/juice.py reveal r6                              # key, after judging
```

Each piece's current gap is on the live progress page and in its round-5 verdict. The one regression
to fix first is the wind-up, which fell from 0.86% bright-and-saturated to 0.000% by switching its
charge from opaque fill to additive glow — its geometry and ramp are correct and should be kept.

### A rig note worth keeping

Two bugs in the capture rig cost most of a round each, and both were invisible until a critic
measured something impossible:

- The 8-bit render target clamped the pre-tonemap pipeline, so bloom never fired and nothing could
  clip. Round 1's "the core never overexposes" finding was partly an artefact of the rig, not the
  effect.
- A static field initializer in `AbilityWindup` read `QualitySettings.activeColorSpace`, which Unity
  forbids during `AddComponent`. The throw killed the capture coroutine mid-sequence, so the parent
  never resumed and the whole shoot hung on one bad primitive with no error surfaced in the rig's
  own log. `CaptureSequence` now guards the fire and tick callbacks so a throwing effect costs its
  own shot and nothing else.

When a measurement looks impossible, suspect the instrument before the subject.

### Known follow-ups

- **Composites are untouched.** The five abilities still do not call any of the nine primitives.
  Wiring them is the next phase, and it is where the actual bar gets tested — the primitives are
  only the vocabulary.
- **New shaders are not in Always Included Shaders.** `Shader.Find` resolves in the editor, so
  captures are unaffected, but the fifteen new `BP_*` shaders will be stripped from a player build.
  `ProjectSettings/GraphicsSettings.asset` needs them before shipping.
- **`HitReaction.PlayDeath` and `HitReaction.Play` are not called by anything.** `Health` still only
  calls `HitFlash`. Until `Health.OnHealthChanged` and `OnAliveChanged` are wired, victims will not
  react in a real match.
- **`ImpactCamera.Hitstop` is not called in production**, only by the rig. Wiring it means writing
  `Time.timeScale` on a networked peer, which needs a gameplay owner.
- **The damage number's typeface is LiberationSans.** It is the only real `TMP_FontAsset` in the
  project; Jost and Oswald are UI Toolkit `FontAsset`s, which a TextMeshPro renderer cannot use.
  Getting Supercell weight needs someone to author a condensed TMP font asset.
- **The board itself is the deepest problem.** Every critic ran into it. `docs/design/ArtDirection.md`
  specifies an Ink `#0B0E17` clear and a dark deck; the shipped scene has a pale grey-blue floor at
  luminance 182 and a light grey camera clear. `impactdip` buys contrast back locally, one hit at a
  time. Fixing the deck's value would make every effect in the game land harder for free, and it is
  outside the scope of ability work.
