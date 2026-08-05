# Measured findings from round 1 — read this before round 2

Eight independent critics measured the captured frames pixel by pixel. All eight pieces were
rejected. Four of them, working separately, arrived at the same root cause. It is the most important
thing anyone has learned in this exercise.

## The luminance law

**Our board floor sits at luminance 181–184 out of 255.**

That leaves **73 levels of headroom upward** and **182 levels downward**.

Clash Mini's board is mid-value saturated green at luminance ≈148, so a white core there gets both
value contrast *and* hue contrast for free. Ours gets neither. Every effect built in round 1 spent
its entire budget brightening a surface that is already nearly white, and every one of them
disappeared.

Measured consequences from round 1:

| Effect | What it managed | Against |
| --- | --- | --- |
| Impact core | peak luminance 221, **zero clipped pixels** | floor at 182 |
| Shockwave front | **+14** luminance over the floor | floor tile-to-tile variation is **18** |
| Debris chunks | half of them within **21%** contrast of the floor | — |
| Aftermath burn mark | **27 points brighter** than the floor | it was supposed to be a burn |
| Damage number (yellow) | **7 levels** from the board | invisible in greyscale |

**The rule this implies, and it applies to every piece:**

> Reach for **dark and opaque** before reaching for bright and additive. Additive glow is a garnish
> on this board, never the structure. If an effect's silhouette is not made of material that is
> *darker* than luminance 182, it does not have a silhouette.

Concrete targets:
- Occluding material (dust, smoke, debris, scorch): **luminance 40–110**. A −80 to −140 delta.
- Verify occlusion the way the critics did: find a tile seam under your effect and check its
  contrast. Clean floor seam contrast is ≈39. Under genuinely opaque material it must drop below 8.
- Additive hot cores are still worth having, but only *inside* or *against* dark material, and they
  must genuinely clip to 255 in all three channels, not merely approach it.

## The saturation law

Our board is desaturated blue-grey. Round 1's effects were **also** desaturated, so they had no hue
contrast either. The impact core measured **saturation 0.04**; it was actually *less* saturated than
the board underneath it, meaning it bleached hue out of the frame rather than adding any.

Clash Mini's bright zones measure **0.38–0.90 saturation**.

> If an effect carries a colour, that colour must be **saturated ≥0.45** where it appears. A pale
> tint of the team colour is invisible. Let white appear only at the very centre of an energy core,
> and let saturated hue own everything around it.

## The hard-edge law

Every critic independently flagged softness.

- Our shockwave front ramps over 16px and decays over 20px: **≈0.9 luminance per pixel**.
- Clash Mini's dust silhouettes measure **49–91 luminance per pixel**.

> Silhouette edges must be a 2–4 pixel step, not a 20 pixel gradient. Use `clip()`/alpha cutoff, not
> smoothstep, for anything that is supposed to read as material. Save soft falloff for light only.

## The timing law

Three pieces peaked in the wrong place or held a frozen silhouette:

- Debris' ballistic apex landed **170ms after** the frame the strip cuts.
- The aftermath plume held a **fixed 2.68 × 2.53 cell footprint for two full seconds** with under
  0.06 cells of centroid drift. It never rose, leaned or broke up.
- The wind-up's telegraph was still on screen **half a second after the ability fired**, and its
  alarm colour *re-armed* after the event.

> Something must be visibly different in every frame. If your effect's bounding box and centroid are
> the same across 30 frames, it is a static decal on an opacity ramp, and it reads as one.

## The uniformity law

- Aftermath: one convex puff sprite stamped seven times at one scale and one flat value. Internal
  luminance spread inside a lobe measured **5.1**; Clash Mini's puffs measure **30–45**. It read as
  a bunch of grapes.
- Debris: 22 pieces all between 0.11 and 0.40 cells, evenly spaced around a hollow ring. It read as
  confetti.
- Impact core: four long needles on the cardinals, four short on the diagonals, perfect radial
  symmetry. It read as a lens flare.

> Build a **hierarchy**: one dominant mass, a few mid, a scattering of small, at a size ratio of at
> least 3:1. Break every symmetry. Shade every mass so it has a lit side and a dark side.

---

# Round 2 findings

Nine pieces rebuilt against everything above, filmed, and re-judged. **One passed, eight were
rejected.** Almost every round-1 gap was verifiably closed — the dust occludes, the core clips, the
palette is charred, the squash squashes — and the pieces failed on new, sharper problems. Two of
those are cross-cutting and matter more than any individual note.

## The phase law

Round one's effects were too quiet. Round two's are loud in the wrong frame. Four pieces
independently split their light and their material onto separate clocks:

| Piece | The light | The matter |
| --- | --- | --- |
| Impact core | full-size white core for **exactly 33ms**, −49% diameter on the next frame | the blue rim outlasts it and becomes the dominant mass, so the shipped panel is a donut |
| Debris | 346 clipped pixels at +0.00s | 1.69 cell² of mass at +0.23s, by which point **zero** hot pixels remain |
| Contrast dip | flash peaks at t=0 | dip is deepest at **+0.067s**, worth 2.7 luminance levels when it is actually needed |
| Aftermath | plume peaks around +0.5s | warm pixels bleached to cream throughout |

> An impact has exactly one money frame. Light, opaque material and colour must all peak on **the
> same frame**, and that frame must be held long enough to survive a 30 Hz sample — roughly 100ms,
> not 33ms. An effect whose brightest frame and whose heaviest frame are 200ms apart has no money
> frame at all.

Concretely: hold the peak for ~100ms before anything starts collapsing, and check that a single
frame simultaneously carries the clipped pixels, the opaque area, and the saturation.

## The bleaching law

The 8-bit capture bug is fixed and bloom now fires. That inverted the round-1 problem: pieces that
were too dim are now over-driven, and **the tonemapper destroys hue above the clip point.**

- The shockwave's `RimHue()` forces saturation ≥0.62, then drives intensity to 6.5–9.5. Measured
  saturation of the resulting rim: **0.011**. Not one pixel in the frame exceeds 0.45 saturation
  above luminance 200.
- The aftermath's ember core is authored at `(6.4, 3.8, 1.1)`. Red and green both clip, so it
  tonemaps to cream at **saturation 0.11**. Its genuinely saturated `(3.6, 0.5, 0.055)` survives
  only as a thin rim.
- The hit reaction drives the unit's albedo to 2.12×, 18% over the 1.8 bloom threshold. The halo
  eats 86% of the unit's black outline and 100% of its team colour.

Clash Mini's aftermath panels carry 1–5% of pixels that are both bright and saturated. Ours carry
**zero**.

> Saturated colour and clipping are mutually exclusive. Author hot colours so that **one channel
> clips and the others do not** — something like `(1.6, 0.45, 0.06)` rather than `(6.4, 3.8, 1.1)`.
> Keep the white-hot core tiny, a glint inside the colour rather than a replacement for it. If a
> layer must bloom, let it bloom in its hue.

## Two smaller repeats

**Frozen tails.** The shockwave holds a static mass from +0.333 to +0.533s with centroid drift under
0.017 cells per frame. The aftermath's ground mark locks bbox, centroid and darkest value to three
decimals from +1.20 to +1.63s, with **four consecutive byte-identical frames**. This is round one's
screensaver failure reappearing in the tail rather than the body.

**Machined symmetry.** The shockwave's outer boundary varies by 3 pixels across 453 rays — 0.20% of
radius — and its hot rim is a closed constant-width hoop present on 320 of 360 rays. It reads as a
washer. Deliberately breaking a silhouette costs nothing measurable: edge hardness is measured along
each ray's own normal, so raggedness does not soften the step.

---

# Round 3 findings

Eight pieces rebuilt. **Impact core and aftermath passed**, joining camera. Five rejected, and again
almost every named gap was verifiably closed — the impact core now holds full size for four frames
instead of one, the shockwave's machined circle is broken, the debris has a real money frame, the
hit reaction's unit keeps its black outline with clipping down from 12,272 pixels to one.

Three findings generalise.

## The colour-area law

This is round 3's headline, and it is a correction to how the luminance law was applied.

The luminance law said reach for dark and opaque before bright and additive. That was right, and it
is what produced every gain since. But it was over-applied: three pieces are now large, well-shaped,
correctly-occluding masses of **grey-brown**, with colour surviving only as a hairline.

| Measurement | Ours | Clash Mini |
| --- | --- | --- |
| Shockwave strip, bright-and-saturated pixels | **0.068%** | 0.265% min, 1.65% median |
| Debris impact panel, saturated material | **0.53%** | 2.1–2.4% on comparably desaturated boards |
| Debris aftermath panel | **0.00%** | 6.7–14.7% |
| Shockwave strip mean saturation | 0.166 | 0.262–0.796 |
| Death and Pogo effect material | 0.08 and 0.20 | 0.38–0.90 |

The shockwave critic ran the decisive test: **desaturating our strip changes nothing.** The effect
loses no information when the colour is thrown away, because there is almost no colour in it.

> Saturated colour must occupy **area**, not a rim. Target 1.5–3% of the panel as bright-and-
> saturated material. This is not a brightness problem and it does not need anything enlarged: the
> fix is to tint the interior of masses that already exist, taking the grey-to-colour ratio from
> roughly 8:1 to 2:1.

## The interior-lighting law

Our masses are flat cut-outs. Now that their silhouettes are right, the inside is what fails.

| Measurement | Ours | Clash Mini |
| --- | --- | --- |
| Shockwave dust, eroded-core luminance std | **13.0** (round 2: 13.4) | 30–45 |
| Debris cloud, local 9×9 contrast | **1.6–1.9** | 5.8–16.9 |

> Every lobe needs a directional term: the face toward the blast sitting 60–80 levels above the face
> away from it. A hard silhouette around a flat fill reads as a paper cut-out, and improving the
> outline further will not change that.

## Hue determines available luminance

The shockwave builder argued a saturated blue cannot be bright, so the hot band had to be warm. The
critic tested it and the argument is true only for the hue that was chosen:

- At hue 240°, saturation 0.55 caps at luminance 125.
- At hue **190°, the same saturation reaches luminance 208**; at 180°, 225.
- **Ten of seventeen** Clash Mini strips carry bright saturated pixels in the 160–270° cool band, at
  median hue 176–181° and peak luminance 220–231.

Our cool pixels sit at hue 200–238°, the one arc where the ceiling is real, and top out at L154.

> If a cool colour needs to read bright, move it toward 185–195° rather than abandoning it.

## A rig fix, not a builder fix

Two critics found the strip picker cutting the wrong frames — the hit reaction was cut at
+0.133/+0.267/+0.700s while its entire event lives between 0 and +0.133s, and the debris was cut one
frame past its money frame. Scoring on brightness and bulk change alone drifts onto slow motion like
a recovery spring. The picker now scores the three quantities the critics actually measure — blown-
out area, opaque material darker than the board, and saturated colour — and `hitreact` has an
explicit timing override. Both shots now cut exactly where the critics said they should.

## The vocabulary is not wired to the game

The impact core passed and is called from exactly one place in the repository: the capture rig. The
real grenade's flash measures 0.09 cells because no ability fires `ImpactCore.Spawn`. The same is
true of most of the set. Primitives passing in isolation ship nothing until the five abilities
compose them.

---

# Rounds 4–5, and the blind test

Round 5 ended with a blind A/B. Three sheets, each stacking one of our three-frame strips against a
Clash Mini strip of the same three moments, order randomised, labels withheld. A critic who had seen
none of this work was asked one question: which one hits harder.

**They picked Clash Mini in all three. We have not met the bar.**

Two things about that result are worth more than the result itself.

The margin was not uniform. On the death they were *certain*. On the grenade and the Pogo landing
they were only *fairly confident*, and on the landing they volunteered this:

> B's impact frame here is the best single frame B produces anywhere on these sheets, and **it beats
> A's corresponding frame on craft.** It has hard-edged cyan blade shards radiating along the ground
> plane in six-plus directions, which is a real shockwave front with direction rather than a soft
> bloom; roughly a dozen discrete debris chunks with individual silhouettes; and visible light spill
> brightening the surrounding floor tiles. A's impact frame, by contrast, is a featureless white
> ball. It's a cheat: the flash hides the event entirely.

B was ours. So the individual craft is arriving. What is missing is three specific things, and they
are the same three in every sheet.

## The amplitude law

Our effects change **12–16%** of the impact panel. Clash Mini's change **30–39%**.

> Area is force. An effect that is better made but a third the size loses, every time. This is the
> one place where "err large" from the original brief was still not large enough.

## The environment law

> **A's explosion lights the room; B's does not.** In A's third frame the whole arena is lit by the
> event — floor tiles wash orange, a red rim bleeds to the arena border. In B, the floor tiles two
> cells from the blast are the same grey they were in frame one. The world does not acknowledge that
> anything happened.

Every effect in this set is a thing that appears *on* the board. None of them change the board.
Round 4's contrast dip was an attempt at this and was correctly cut for being a fake — the real
version is light spilling outward from the effect onto tiles, walls and units it did not touch.

## The persistence law

**All three of our aftermath panels measure 0.000% bright-and-saturated. Not one of Clash Mini's
seventeen references goes below 0.105%, and their median is 0.440%.**

On the blunter test — how much of panel 3 differs from panel 1 — Clash Mini changes a median of
41.6% of pixels with a minimum of 14.4%. Ours change 5.8%, 15.9% and 0.00%.

> **Their aftermath frame is their biggest frame. Ours is their smallest.** An ability that leaves
> nothing behind did not happen, however good its impact frame was. Every piece should be re-timed
> so its third beat is its largest, not its quietest.

## A blend-mode rule that cost a round

The wind-up regressed from 0.86% bright-and-saturated to **0.000%** by switching its charge from
opaque fill to additive glow. The reason is arithmetic and worth stating once, permanently:

The board's darkest channel is **172**. Additive blending can only raise channels. So for any
additive layer on this board,

> saturation ≤ 1 − 172/255 = **0.324**

Against a 0.45 bar, **additive light on this board cannot be saturated at any intensity, at any
hue.** The measured ceiling in the shipped frame was 0.344. This is the round-1 luminance law in its
strictest form, and it applies to every piece.

The corollary settles a hue argument that ran for two rounds: warm *is* available. 42 of 51
reference panels put their bright-saturated pixels at hue 46–67°, and our own aftermath (hue 40°,
2.24%) and grenade (hue 34°, 2.47%) clear the bar on this exact board. The question was never
warm-versus-cool; it was opaque-versus-additive.

## Direction from the game's owner, which overrides the above where they conflict

- **Contrast is not the problem.** The `impactdip` piece has been deleted. The luminance law still
  holds as craft guidance — dark opaque material reads on this board and additive light largely does
  not, and that is what produced round 2's real gains — but do not spend further effort manufacturing
  contrast by darkening the board. Make the effects themselves read.
- **The death effect and the Pogo landing are the same effect, and it is confusing.** `Health` fires
  `ImpactShockwave.Spawn(pos, teamColor, 2.2f, 0.5f)` when a unit dies; `Pogo` fires
  `ImpactShockwave.Spawn(pos, teamColor, 1.8f, 0.4f)` when the rider lands. Same shape, same colour,
  same size, opposite meanings. A player watching a round cannot tell a unit dying from a rider
  arriving. These two must become unmistakably different reads.
- Every other critic finding stands and should be worked as written.

## Settled disputes

- **Cell pitch.** Both parties were right. At the impactcore camera one 2.7-unit cell is ~218px in
  the 1024px capture and ~109px on the 512px strip panel. Always state which image a pixel figure
  refers to.
- **Lift.** Screen pixels govern, not cells: under a 73° camera a world unit of height projects to
  ~21–30px while a cell spans ~200px horizontally. "0.8 cells of lift" was not a meaningful ask.
- **The camera is done.** Measured against the references, Clash Mini's own camera moves a median of
  8.96px with 0.036° of roll, and six of seventeen strips are motionless. Ours now exceeds that by
  an order of magnitude. Further amplitude is spending on an axis the reference barely uses.

## What was actually right

Do not re-litigate these; they cost a round to establish:

- Scale is no longer the problem for the wind-up (4.4 cells), shockwave (2.9 cells), aftermath
  (2.8 cells) or the damage number (too big, if anything). Stop enlarging.
- The camera impulse has **no ramp-in** and returns to its base pose with **exactly zero drift**.
  That shape is correct.
- Panels either side of the impact core are clean: the flash arrives from nothing and leaves
  nothing. Correct for an isolated primitive.
- The hit reaction's knockback distance (1.1 world units) and tilt (22°) are real and roughly right
  in magnitude. The problem is that a blown-out tint hides them.
