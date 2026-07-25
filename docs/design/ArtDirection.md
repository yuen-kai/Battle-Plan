# Battle Plan — Art Direction

**Codename: FIELD DAY.** Supersedes the Night Range direction in full.

This document is a contract. It is written so that an implementer can build from it without asking a
follow-up question. Where it gives a number, use that number. Where it bans something, the ban is
load-bearing. Where it disagrees with `DESIGN.md`, `PRODUCT.md`, `Overview.md` or
`ArtDirection-WiringGuide.md`, this document wins — and §12 lists every such case explicitly.

Colour, spacing, radii, sizes, type steps and motion constants are **not** in this document. They
live in `docs/design/ArtBible-Palette.md`, which is the machine-readable half of the contract. This
document references tokens by name and never restates a value, so the two files cannot drift.

> `ArtDirection-WiringGuide.md` is **superseded and stale**. It describes a uGUI/TextMeshPro
> interface that does not exist; all UI is UI Toolkit. Do not build from it. Delete it once the
> redesign lands.

**Contents:** §1 direction · §2 references · §3 typography · §4 palette rules · §5 UI and HUD ·
§6 iconography · §7 world and arena · §8 projectiles · §9 VFX · §10 audio · §11 characters ·
§12 what this overrides · §13 risks · §14 definition of done · §15 anti-patterns ·
§16 production route · §17 changelog

---

## 1. The direction, in one sentence

> **Battle Plan is a sunlit tactical diorama: a pale, chalky board of painted concrete under a warm
> afternoon sun, on a warm tabletop, where the pieces are small and saturated, the interface is
> printed card sitting on the table, and everything that matters is drawn in ink.**

### 1.1 Why this reads as production-ready rather than as a bright prototype

Bright games look cheap for one reason: at high key there is no darkness to hide imprecision in, so
every soft edge, every unmotivated gradient and every accidental value collision is visible. The
usual response is to add glow and gloss, which makes it worse, because on a light background glow
subtracts contrast instead of adding it.

FIELD DAY takes the opposite position, and it is the whole thesis: **on a bright board, darkness is
the scarce resource and therefore the expensive-looking one.** Ink is what the direction spends. A
hairline ink contour around a saturated shape, a hard-edged cast shadow with no blur, a 4-pixel ink
edge under a card, a crisp 0.12-unit separator between cells — these are cheap to author, they are
absolutely precise, and precision is what reads as production value. Nothing in this direction
relies on a soft edge to look finished.

Four structural commitments follow from that, and together they are what separates this from a
theme swap:

1. **Three materials, never blurred into each other.** A warm tabletop, a cool painted board, and
   warm printed card. Every surface in the game belongs to exactly one of the three, and they are
   separated by temperature as well as by value, so they hold apart even when their luminances are
   close.
2. **Saturation is budgeted by screen area**, so the board is colourful in aggregate while the
   ground stays quiet. This is §4.2 and it is the central palette decision.
3. **Every effect is a silhouette first.** Opaque, contoured, and sized to its rules footprint.
   Nothing in the game reads by glow. Seven objects in the entire product are permitted to bloom.
4. **Colour never carries meaning alone.** Team identity is colour *and* shape, always, everywhere.

### 1.2 What the player should feel

Picking up a well-made game and finding the pieces already set out. Inviting rather than grim,
precise rather than cute. The tone is a good boardgame table in afternoon light — you want to reach
into it — and then, for about six seconds an execution phase, small violent things happen on it and
you flinch. The gap between those two registers is the game's personality, and it only exists
because the resting state is calm and bright.

---

## 2. References — what we take, and what we leave

### 2.1 `ReferenceImages/MapVibe.jpg` — XCOM 2

The most useful reference in the folder for the board, and the one with the most misleading
lighting. **It is a dusk scene**: a dark blue-grey street under cyan worklight, luminous edge lines
glowing against darkness, one saturated red dropship. The user has now twice stated that dark
lighting is the problem, so **the value key and the lighting of this image are explicitly not the
target** — this is a correction to the standing note that treats MapVibe as the world north star.

What transfers, and it is the single richest source in this document:

- **The ground is a graphic surface, not a texture.** Yellow lane stripes, white crosswalk hatching,
  a hexagonal paving inlay, a blue tile section, kerbs that define lanes. Almost none of the read
  comes from material detail; nearly all of it comes from *painted markings*. FIELD DAY takes this
  wholesale and reinterprets it as paint on sunlit concrete rather than light in the dark. §7.3.
- **The plan spline.** A neutral white path line, waypoint diamonds, and a bracket at the
  destination. That is exactly the language for our planning path, and §9.4 adopts it.
- **Cover reads from its top face.** At a steep camera you see tops, so the tops carry the
  silhouette. §7.4 makes cover caps a distinct, lighter material for this reason alone.
- **Saturation restraint.** Only the vehicle and the interface lines are saturated; the entire
  environment is near-neutral. That is the saturation budget, already demonstrated.

### 2.2 `ReferenceImages/VisualVibe.jpg` — Bullet Echo

Governs combat readability, not mood. Take: bold unbroken silhouettes, unambiguous team state, a
vision cone that reads instantly as a region rather than as a light. Leave: the darkness, the
vignette-heavy framing, and the noir palette. The user named this image specifically when explaining
that dark lighting made the game feel too serious.

### 2.3 `ReferenceImages/CharacterDesign.webp` — Tactical Breach Wizards

The closest match to what the project already has and the reason §11 is a light-touch section. Flat
matte characters, three or four value blocks each, zero material storytelling, silhouettes that hold
at 40 pixels tall. Our existing `Char_*` materials already sit at smoothness 0.12 with zero
metallic, which is correct — most of §11 confirms rather than changes.

### 2.4 `ReferenceImages/AbilityDesign.png` — Clash Mini

Governs the impact frame. Take: the willingness to make an ability briefly enormous, the hard-edged
opaque shapes, the clarity of a chunky ring on a bright board. Leave: the fantasy vocabulary and
the particle density.

### 2.5 The bright-tactics field

`Into the Breach` for the discipline of a legible grid where every threat is drawn as a shape
rather than an effect. `Advance Wars: Re-Boot Camp` for saturated pieces on a quiet ground and for
proving that bright does not mean weightless. `Bad North` for a cool desaturated ground under a warm
sun with tiny saturated figures on it — the closest single reference to the target frame.
`Wargroove` and `Unicorn Overlord` for card-stock interface sitting on a board. `Mario + Rabbids`
for how far the impact frame can go on a bright board without losing the read.

---

## 3. Typography

### 3.1 The three faces

| Role | Face | Files |
|---|---|---|
| Display and interface | **Saira Condensed** | `Assets/Fonts/SairaCondensed/SairaCondensed-ExtraBold UI SDF.asset`, `SairaCondensed-SemiBold UI SDF.asset` |
| Body copy | **Rubik** | already in `Assets/Fonts/` |
| Numerals | **Cascadia Code** | already in `Assets/Fonts/` |

Saira Condensed carries the direction. Condensed grotesques read as instrumentation and signage,
which is the counterweight that stops card stock and orange from reading as a children's boardgame.
It is the discipline in the room.

Numerals are monospaced without exception. A turn timer whose digits shift width as it counts down
looks broken, and every HP and cooldown readout in the HUD updates in place.

### 3.2 Availability — closed

Both Saira Condensed SDF assets exist on disk with the full required glyph set verified present,
including `·`, `—`, `…` and `×`, on a single 1024² atlas each with static population. **The
silent-fallback risk previously recorded here is retired.** A build cannot drop a glyph.

### 3.3 The scale

The full type scale — size, face, weight, tracking, line height and case for all eleven steps —
is in `ArtBible-Palette.md` §9.5. It is unchanged from Night Range.

### 3.4 Rules

- **Never set body copy in Saira Condensed.** Condensed faces below 15px lose counter clarity.
  Saira is for labels, titles and numbers-adjacent chrome; Rubik is for anything a player reads as
  a sentence.
- **Uppercase gets tracking, lowercase does not.** Every uppercase step in the scale carries
  positive tracking; the body steps carry none.
- **One type step per hierarchy level per screen.** If a screen needs a fourth level, it needs
  fewer things on it.
- **No text is ever drawn over a moving model.** Nameplates, damage numbers and status text live on
  HUD widgets, not in world space. This survives from Night Range unchanged and is not negotiable —
  it is the rule that keeps a bright, busy board legible.
- **Minimum on-screen size is 10px** (`--bp-type-micro`), and only for uppercase Saira SemiBold
  with tracking. Nothing smaller ships.

---

## 4. Palette rules

The values are in `ArtBible-Palette.md`. This section is the law governing how they are used.

### 4.1 Three materials

Every surface in the game belongs to exactly one family. Mixing them is the fastest way to make the
frame incoherent.

| Family | Tokens | Temperature | What it is |
|---|---|---|---|
| **Table** | `--bp-table` | Warm, mid | What the game sits on. Full-screen menu beds, the deployment overlay. |
| **Board** | `--bp-ground-*`, `--bp-cover*`, `--bp-rail`, `--bp-sky`, `--bp-fog*` | Cool, pale | Painted concrete in sunlight. |
| **Card** | `--bp-deck-*`, `--bp-panel`, `--bp-dock` | Warm, near-white | Printed components. Every panel and widget. |

Warm / cool / warm at three separated values. **This is what holds the HUD off the board**, and it
is the answer to the question of what colour a panel should be on a bright map: not a darker
version of the board, and not the same white as everything else, but a *different material* that a
physical game would also have used. Components sit on boards; they are allowed to be card when the
board is concrete.

### 4.2 The saturation budget — how a colourful board keeps its signals

The central tension of this direction: a toy diorama wants saturated colour everywhere, and a
tactics board needs saturation to mean something. Night Range resolved it by reserving saturation
entirely for information, which works on a dark board and produces a grim one.

FIELD DAY resolves it by allocating saturation **by screen area rather than by category**:

> **The larger a thing is on screen, the less saturated it is allowed to be.**

The board ends up colourful in aggregate — many small saturated objects on a quiet ground — which is
precisely what a painted diorama, a wargaming table, Bad North and Into the Breach all look like.
The ground is always the quietest element in the frame, and because it is quiet, a 90%-saturated
orange marker on it is unmissable without being loud.

The numeric table, the per-token measurements and the two enforcement rules are in
`ArtBible-Palette.md` §8. The rule most likely to be violated by someone adding set dressing:

> **No environment or prop material may use a hue within 25° of team blue (212.7°) or team red
> (356.4°) above 30% saturation.** Props live in the warm band 30–60° or the cool band 140–190°.

### 4.3 The ink contour law

This is the mechanism that makes everything else work, and it exists because of a fact that cannot
be tuned away: a mid-value light ground and a saturated mid-value hue have similar luminance by
definition. Measured against `--bp-ground-a`, team red reaches only 1.98:1 and the amber signal
1.17:1; blue scrapes 3.02:1 against the lighter checker square but falls to 2.73:1 against the
darker one. Darkening the ground until they all pass means abandoning the bright board; desaturating
the signals until they pass means abandoning the signals.

> **No saturated shape ever touches the deck without ink between them.** Every projectile, overlay,
> telegraph, ability shape, marker, unit base and FX element carries a `--bp-ink` contour. The
> contour carries the contrast — **8.17:1** against cell A and **7.37:1** against the darker cell B,
> *at full pixel coverage* — and the fill carries the identity. How wide, and what the width
> assumes about the pipeline, is **§4.3.1**.

**The law covers painted deck markings too, and this was a gap.** Five markings were authored as
flat washes carrying their own contrast and none of them reach it: spawn rank washes at 1.17:1 and
1.15:1, the hill pad perimeter at **1.15:1**, its inset border at 1.84:1, and lane paint at 1.60:1.
The hill pad is the worst of these and the most consequential — the King of the Hill boundary is the
single most important edge on the board, and it was drawn at 1.15:1.

They fail for the same reason as everything else in this section: they were authored as UI overlays,
where a wash sits under a panel and only has to tint, rather than as paint on a lit deck where it
has to hold an edge by itself. Same trap as the cover cap, wearing a different hat.

> **Every painted deck marking carries a 0.043-wide `--bp-ink` keyline on its outer edge.** The fill
> keeps whatever value the design wants — 1.15:1 is fine, and a soft wash is often what the marking
> should look like — because the keyline carries the contrast. This is not a new rule; it is the ink
> contour law applied to paint, and it converts five value-contrast problems into one contour
> problem the document already knows how to solve.

The keyline was 0.04 and is now **0.043**. It is a *static* contour — the camera does not move
during a match, so a deck marking holds one sub-pixel phase for the whole game and has to survive
the unluckiest one. 0.043 is the width at which it does. §4.3.1 carries the derivation.

Team-coloured rank *lines* are already covered as saturated shapes and need no separate treatment.

**The contour takes whichever of ink or paper wins against its host.** Ink is the overwhelming
default because the board, the fog wash and the card stock are all light. But the contour's job is
separation, not darkness, so on the three genuinely dark hosts — the rail `--bp-rail`, cover bodies
`--bp-cover`, and the `--bp-focus-dark` case in UI — it inverts to `--bp-deck-0` at the same width.
An ink line on a cover block is not a subtle contour; it is nothing at all. This is the same logic
that governs the focus ring in palette §5.1, applied to the world.

Two consequences worth stating plainly, because they will feel counterintuitive to anyone who has
worked on a dark game:

- **Emphasis moves toward ink, not toward light.** Every `-hi` and `-deep` step in the palette is
  darker than its base. Hover darkens, press darkens further, focus rings are ink.
- **Recess is depth, not emphasis, and depth does not invert.** A well, tray or socket is darker
  than its container on a light theme exactly as it was on a dark one. See `ArtBible-Palette.md`
  §11.3.

### 4.3.1 How wide a contour is, and what that assumes

**The old answer was "at least 2 screen pixels", and it was a UI number wearing world clothes.**
2px is `--bp-bw`, the emphasised-border token, where it is a hairline on a 300px card — 0.7% of its
host. Carried into the arena unchanged, it lands on objects four pixels across. A 2px rim per side
turns the bullet into an 8.1px object that is **59% ink by silhouette area**: a dark blob with a
coloured speck, which breaks §4.4's colour contract in the act of obeying §4.3. Nor was the rule
ever actually honoured out here — the deck keyline three paragraphs above it was 0.04, which is
1.5px, and every projectile shell was 0.75px. *Not one world contour this document has ever
specified met its own stated minimum.* **2px stays in the UI, where it is correct, and leaves the
world.**

The replacement is stated in screen pixels and derived from coverage, because that is what a contour
is: a partial-coverage edge, not a proportion of the thing it surrounds. **A contour's required
width is set by the display, not by the size of its host.** A 0.78px ink line is equally visible on
a 4px bullet and on a 99px cell, and the only reason to make it wider on the larger object would be
style, not legibility.

**Screen scale.** At §7.1's camera — pitch θ 73.1°, height h 24.4, vertical FOV 60° — the scale at
screen centre for a plane at height y is

```
s = H · sin θ / ( 2 · (h − y) · tan(FOV/2) )   =   895 / (24.4 − y)   px per world unit at H = 1080
```

Scale linearly with vertical resolution. Extents lying along the camera's depth axis are
additionally foreshortened by sin θ = 0.957; that is 4% and is ignored here.

| Plane | y | px/unit @1080p | @1440p |
|---|---|---|---|
| Deck — markings, overlays, ground FX | 0 | **36.7** | 48.9 |
| Projectile flight (`CombatFX` impact height) | 0.6 | **37.6** | 50.1 |
| Unit chest — above-ground FX, beams | 1.5 | **39.1** | 52.1 |
| Cover cap | 2.06 | **40.1** | 53.4 |

For orientation, using the right plane for each: a grid cell is **99px**, a unit base **48px**, a
bullet in flight **4.1px**. Reading a projectile off the deck row instead is a 2% error and reading
a beam off it is a 6% one — small, but the row is free, so use it.

**Coverage.** A sub-pixel contour does not draw ink; it darkens a pixel toward ink in proportion to
the fraction α it covers. MSAA resolves in linear, so the composite is linear. Against the two deck
squares:

| Ink coverage α | vs cell A `#A8B3B8` | vs cell B `#9FAAB0` |
|---|---|---|
| 1.00 (solid) | 8.17:1 | 7.37:1 |
| 0.80 | 3.36:1 | 3.24:1 |
| **0.78** | **3.17:1** | **3.07:1** |
| 0.75 | 2.93:1 | 2.84:1 |
| 0.50 | 1.78:1 | 1.76:1 |
| 0.375 | 1.49:1 | 1.48:1 |

`--bp-ink` is 0.0100 linear, cell A 0.4403, cell B 0.3925; ratios are WCAG `(L+0.05)` form. Re-run
it rather than quoting it. **α ≥ 0.78 is where a contour clears 3.0:1 on both squares**, and it is
also where the contour resolves darker than a *blue* fill (3.02:1 on A, 2.73:1 on B) — blue being
the one team colour that would otherwise out-darken its own outline.

**Phase.** Coverage is conserved under sub-pixel position but not concentrated: a 0.78px rim sitting
inside one pixel gives one pixel at 3.07:1, and the same rim straddling a boundary gives two pixels
at ~1.5:1. More samples make that coverage *accurate*; they do not make it *aligned*. This is
geometric and no pipeline setting removes it, which is what splits the rule in two:

> **The contour floor.**
>
> - **Moving contour** — projectiles, travelling FX, anything whose screen position changes by a
>   pixel or more per frame: **≥ 0.78 screen pixels of ink per side.** It visits every sub-pixel
>   phase within a few frames and the eye integrates the result, so the conserved coverage is the
>   figure that matters. **= 0.021 world units on the projectile plane.**
> - **Static contour** — deck markings, telegraphs, overlays, the fog boundary, any shape holding a
>   screen position longer than ~0.2 s: **≥ 1.56 screen pixels per side.** It is stuck at whatever
>   phase it landed on for the whole match, so the worst phase — coverage split in half across two
>   pixels — must still clear 3.0:1. **= 0.043 world units on the deck plane.**
> - **The cap, both classes.** A contour may never exceed **one quarter of its host's narrowest
>   silhouette dimension** per side, and ink may never exceed **45% of the contoured silhouette's
>   area**. The two forms agree by construction: on a typical slug, r = W/3 is where ink becomes the
>   majority of the object, and the cap is set one step back from that.
> - **When the cap falls below the floor, the shape cannot carry a contour.** That is any host
>   narrower than **3.1px moving** (0.083 world) or **6.2px static** (0.17 world). Do not thin the
>   contour to fit. Either widen the host past the threshold, or author it in a value that clears
>   3.0:1 against the deck unaided and carry its identity somewhere else — which is exactly what
>   §8.2's ink lance does.
>
> A contour may exceed the floor for style. It may never exceed the cap.

Worked, for the four projectiles at 37.6px/unit. All four take their shell from the **widest drawn
profile** — the cap ring or rib where there is one, not the barrel — because sizing off the body
would drop the grenade's contour onto its own hazard band and z-fight it while enclosing nothing:

| | Widest profile | in px | Floor | Cap (W/4) | Contour diameter | Ink share |
|---|---|---|---|---|---|---|
| Bullet | 0.11 | 4.1 | 0.021 | 0.0275 | **0.152** | 37% |
| Sniper | 0.09 | 3.4 | 0.021 | 0.0225 | **0.132** | 35% |
| Grenade | 0.34 | 12.8 | 0.021 | 0.085 | **0.382** | 22% |
| Canister | 0.48 | 18.1 | 0.021 | 0.120 | **0.522** | 14% |

The shell is **0.021 radial and 0.030 axial** on all four — anisotropic, because the axial figure
falls out of authoring `localScale` at two decimals. That is left alone: 0.030 is 1.1px, comfortably
over the floor, and the law binds on the *thinnest* rim anywhere on the silhouette, which is the
radial one. The sniper lance is the narrowest contoured host in the game and clears the pinch
between floor and cap by 7%; the bullet, by 31%. Those two are the only shapes anywhere near it,
which is why the cap has never bitten before and why it is written down now.

**The law assumes anti-aliasing, and is void without it.** A sub-pixel contour exists only if the
pipeline can compute fractional coverage; at one sample per pixel it is binary, so the line dashes
along its own length and crawls frame to frame as the projectile moves. That is not a thin contour,
it is a different and worse artefact. Coverage samples per display pixel, linearly, are
`n = renderScale × √MSAA`:

| Tier | Render scale | MSAA | n | Samples across a 0.78px rim |
|---|---|---|---|---|
| **High** — `High.asset`, the shipped default | 2.00 | 8 | 5.66 | **4.4** |
| Base template — `URP.asset` | 2.00 | 2 | 2.83 | **2.2** |
| Medium — `Medium.asset`, today | 1.00 | 1 | 1.00 | **0.78 — fails** |
| Medium — with the planned 2× | 1.00 | 2 | 1.41 | **1.10** |
| Medium — at 4× | 1.00 | 4 | 2.00 | 1.56 |
| Low — `Low.asset` | 0.37 | 1 | 0.37 | **0.29 — out of scope** |

> **Every contour must be at least one sample pitch wide (`w · renderScale · √MSAA ≥ 1`); two is the
> target.** High meets the target with room. Raising Medium to 2× moves it from 0.78 to 1.10 and is
> what makes the contour law *valid* on that tier rather than merely specified — it is the minimum
> acceptable setting, not a comfortable one; 4× is where it stops being marginal.

**Low is outside the contour law and must not be used to argue for wider contours anywhere.** At
render scale 0.37 the bullet's whole body is 1.5 render pixels before any contour is added. No width
authored in this document can fix that, and fattening contours globally to chase it would wreck the
default tier to help the one that has already traded fidelity for frame rate.

### 4.4 The team colour contract

Unchanged in substance from Night Range; only the values moved.

- **Blue is yours. Red is theirs.** Never for anything else — not a link, not a hover, not a
  decorative accent, not a neutral highlight.
- **Colour never carries team identity alone.** Friendly markers are **chevrons**; hostile markers
  are **bars**. Every range overlay, path node, threat indicator, card accent and world marker obeys
  this. Direct luminance contrast between the two team colours is 1.52:1 — deliberately low, because
  pushing them apart would make one team systematically weaker against the deck — so the shape
  pairing is not a nicety, it is the primary channel for anyone with a colour-vision deficiency.
  The full colour-vision analysis is in `ArtBible-Palette.md` §7.2.
- **Orange (`--bp-amber`) means "act now."** The primary action, the commit control, the objective,
  the live timer, the dodge alert. If orange appears on something the player is not being asked to
  act on, it is a bug.
- **Green (`--bp-green`) means "confirmed," and it is UI-only.** Green never appears on the board. A
  green object in the arena reads as a third team.

### 4.5 Where colour may be authored

Only in `Assets/UI/Shared/TacticalToyboxTokens.uss` and in material assets whose values come from
`ArtBible-Palette.md`. **No `new Color(...)` literal for anything on screen.** `FXPalette.cs`
already centralises the runtime constants and must be updated to the new values in the same commit
as the token file; there are also hardcoded `Color` values catalogued in
`ArtSurface-Inventory.md` §8.4 that must be routed through it.

---

## 5. UI and HUD system (UI Toolkit)

Content, element names, string lengths and screen inventory come from `UI-ContentInventory.md`. This
section is the visual system only, and it does not change a single element name.

### 5.1 The material story

Every panel, widget, modal and card in Battle Plan is **printed card stock lying on a surface**. It
has a face, a drawn outline, and a visible thickness. It does not float, it does not glow, and it
does not have a soft shadow.

Three devices produce that, and they are the whole depth language:

| Device | Value | Rule |
|---|---|---|
| **Face** | `--bp-panel` (card stock at 0.96) or `--bp-dock` (tray at 0.97) | Never below 0.95 alpha. Below that, terrain competes with text. |
| **Drawn outline** | `--bp-bw-hair` 1px in `--bp-line-2` on all four sides | Every card. This is the printed edge. |
| **Thickness** | `--bp-edge` 4px in `--bp-edge-dark` on the bottom only | Every card. This is what separates it from the board. |

The 4px bottom edge is `DESIGN.md`'s "chunky bottom edge" reinstated. Night Range reduced it to 2px
on the argument that a tactics HUD should read as an instrument; the user has now said twice that
reading as an instrument is the problem, so it comes back — at 4px rather than the original 6px,
because 6px is right on a 52px menu button and swamps a 24px HUD chip, whereas 4px reads as card
thickness at both sizes.

**On separation.** On a light board, card stock and terrain are close in luminance and no fill value
fixes that. Separation is carried by the outline and the edge, not by value contrast. This is the
same mechanism as §4.3 and it is deliberate that the UI and the effects language solve their
shared problem the same way.

**Optional, for the four widgets that sit over the busiest terrain** — phase readout, unit cards,
commit control, dock: a hard cast shadow. UI Toolkit has no `box-shadow`, so this is a sibling
`VisualElement` behind the card, offset **+4px Y, +3px X**, same border radius, filled
`--bp-ink` at **0.28 alpha**, **zero blur**. Class `.card__shadow`. It must be a hard-edged solid
shape. A blurred shadow is banned by §15 and would undo the whole material story.

### 5.2 Interaction states

Deltas from rest. Structure never changes between states — only fill, border and offset — so a
control never reflows.

| State | Fill | Border | Other |
|---|---|---|---|
| Rest | per component | `--bp-line-2` 1px + `--bp-edge-dark` 4px bottom | — |
| Hover | one rung down the ramp (`--bp-deck-1` → `--bp-deck-3`) | `--bp-paint` | `--bp-t-fast`, `--bp-ease-out` |
| Pressed | two rungs down (`--bp-deck-4`) | unchanged | bottom edge 4px → 2px, content shifts +2px Y. The card is pushed into the table. |
| Focused | unchanged | `--bp-focus-light` at `--bp-bw-focus` 3px | Ring is ink on every host. Min contrast 6.96:1. |
| Selected | `--bp-blue-surface` | `--bp-blue-line`, left rule `--bp-bw-rule` in `--bp-blue` | Plus the friendly chevron. Never colour alone. |
| Disabled | `--bp-deck-4` | `--bp-line-1` | Text `--bp-text-3`. **No opacity change** — opacity lets terrain through and makes disabled text unreadable over a bright board. |

The pressed state is the important one: reducing the bottom edge from 4px to 2px while shifting
content down 2px makes the card physically depress. It costs two properties and it is the single
most satisfying interaction in the interface.

### 5.3 Component anatomy

Structure is unchanged from Night Range. Only values moved.

**Button.** Height `--bp-target-lg`; `--bp-target` in compact layouts, `--bp-target-phone` on phone.
Radius `--bp-r-md`. Padding `0 --bp-s4`. Label `--bp-type-label`, uppercase.
- *Primary*: fill `--bp-amber`, label `--bp-text-on-accent`, three-side border `--bp-amber-deep`,
  bottom edge `--bp-amber-deep` at `--bp-edge`. Hover fill `--bp-amber-hover`; pressed
  `--bp-amber-deep`. One primary per screen region, ever.
- *Secondary*: fill `--bp-deck-1`, label `--bp-text`, border `--bp-line-2`.
- *Ghost*: fill `--bp-transparent`, border `--bp-line-2`, label `--bp-text-2`.
- *Text*: no fill, no border, label `--bp-text-2`; hover adds a 1px `--bp-line-3` underline.
- *Destructive*: fill `--bp-red-surface`, label `--bp-red-hi`, border `--bp-red-border`. Never a
  solid red fill with text on it — see the banned pairings table.

**Panel.** Fill `--bp-panel`, radius `--bp-r-lg`, padding `--bp-s5` (`--bp-s4` compact), outline and
edge per §5.1. Title `--bp-type-title`, uppercase, with `--bp-s4` beneath it.

**Modal.** Panel anatomy at `--bp-r-lg`, padding `--bp-s6` (`--bp-s5` on phone), over a `--bp-scrim`
full-screen bed. Enters with `--bp-t-slow` and `--bp-ease-snap` — it lands on the table.

**Tabs.** Row of text buttons; the active tab carries a 2px `--bp-amber` underline at
`--bp-bw`. **This underline stays 2px and does not take `--bp-edge`** — it is a state indicator, not
a card edge, and thickening it would make it compete with the real edges around it.

**Checkbox.** 20px box, radius `--bp-r-sm`, fill `--bp-deck-0`, border `--bp-line-3` 1px. Checked:
fill `--bp-blue`, `check` glyph tinted `--bp-deck-0`. Label `--bp-type-body` at `--bp-icon-gap`.

**Dropdown.** Input height `--bp-target-lg`, fill `--bp-deck-0`, border `--bp-line-2`, radius
`--bp-r-md`, `chevron-down` at `--bp-icon-sm` tinted `--bp-text-3`. Popup is a card with the full
panel anatomy; the selected row carries `--bp-blue-surface` and a left rule.

**Text input.** Height `--bp-target-lg`, fill `--bp-deck-0`, border `--bp-line-2`, radius
`--bp-r-md`, padding `0 --bp-s3`, text `--bp-type-body`. Hover border `--bp-paint`. Focus ring per
§5.2. The join-code field uses `--bp-type-num-lg` in Cascadia Code with `+0.12em` tracking.

**Status line.** A `--bp-bw-rule` left rule plus a tinted bed: success `--bp-green-surface` /
`--bp-green-hi`, danger `--bp-red-surface` / `--bp-red-hi`, act-now `--bp-amber-surface` /
`--bp-amber-hi`. Text `--bp-type-body-sm`. Always paired with the matching icon — colour never
carries the state alone.

**Unit card.** Card anatomy at `--bp-r-md`. A 64px portrait socket on the left at `--bp-deck-2`
(a recess, therefore *darker* than the card face — see §4.3). Name `--bp-type-subtitle`; HP as
`--bp-type-num` over a 6px health track, fill `--bp-blue` or `--bp-red`, track `--bp-deck-3`.
State line `--bp-type-micro`, uppercase. Selected adds the left rule and the friendly chevron.
Enemy cards use the red family and the hostile bar.

**Crew slot.** 40px avatar socket at `--bp-deck-2`, name `--bp-type-body`, role
`--bp-type-body-sm` in `--bp-text-3`. Empty slots show a 1px dashed `--bp-line-2` outline and no
fill.

**Phase readout.** 62px card, phase name in `--bp-type-title` uppercase, timer in
`--bp-type-num-lg`. The timer's colour is the only thing in the HUD that changes with urgency:
`--bp-text` above 10s, `--bp-amber-hi` from 10s, and from 5s it also pulses opacity 1.0 → 0.72 at
2 Hz. Never red — red means the enemy.

**Commit control.** The primary button, plus a state line beneath it. Waiting on the opponent shows
`--bp-green-surface` with `--bp-green-hi` text and the `check` glyph; the button becomes disabled
rather than disappearing, so the layout never reflows at the most tense moment in the round.

**Results panel.** A modal over `--bp-scrim`, with the board still visible behind. Outcome in
`--bp-type-display` uppercase — `--bp-blue-hi` for victory, `--bp-red-hi` for defeat,
`--bp-text-2` for a draw.

### 5.4 Motion

Durations, easings and their USS keywords are in `ArtBible-Palette.md` §9.6. Two rules:

- **Widgets appearing use `--bp-ease-snap`.** A game component placed on a table settles; it does
  not fade in. This is the direction's motion signature.
- **Nothing in the HUD animates during the execution phase** except the timer and damage feedback.
  The board is the show for those six seconds.

Note for implementers: USS has no `cubic-bezier()`. The three easing tokens are keywords. If a curve
you want has no keyword, USS cannot express it — drive it from C# rather than hunting for syntax.

### 5.5 Screens

Layouts, element names and copy are governed by `UI-ContentInventory.md`; this is the visual
treatment only. All four screens sit on `--bp-table`, so the menus read as the tabletop and the
match reads as the board on it.

- **Title.** Wordmark in `--bp-type-hero`. A single vertical stack of four buttons, one primary
  (Play). Everything on card stock over the table.
- **Match setup.** A 520px operating column of cards on the left, the stage image on the right, both
  on the table bed. Tabs per §5.3.
- **Crew selection.** A grid of unit options as cards, the selected crew as slots, the map preview
  in a `--bp-deck-0` plate. Confirm is the primary button.
- **Game HUD.** Floating card-stock widgets over a full-bleed board. **The camera must render
  full-bleed** — its viewport rect currently reserves 63px top and 121px bottom at 1080p for two
  HUD bars this direction removes. Widgets: phase readout top-centre, friendly unit cards bottom,
  enemy cards top-right, controls dock top-left, commit control bottom-right.
- **Responsive.** The `compact`, `narrow`, `phone` and `short` classes are unchanged. On `phone`,
  control heights go to `--bp-target-phone` and panel padding drops one step. Card anatomy — outline
  and 4px edge — never changes with breakpoint; it is the identity.

---

## 6. Iconography

### 6.1 The system

The project has a coherent 14-icon set at `Assets/UI/Shared/Icons/` — 24×24 viewBox, `fill="none"`,
`stroke="#fff"`, round caps and joins, tinted at runtime through
`-unity-background-image-tint-color`. **Keep it. Do not restart.** Two changes only:

1. **`stroke-width` 2.2 → 2.0** in all 14 files. One attribute per file; it aligns the set exactly
   with Lucide's grid so stock and hand-authored glyphs are indistinguishable.
2. **Keep round caps and joins.** They are the bridge between the hard-surface board and the rounded
   characters, and they are what the set already does well.

**System rules.** 24×24 grid. 2px stroke. 2px minimum optical padding — nothing touches the viewBox
boundary. No fills except where a glyph is definitionally solid (the `play` triangle). Rounding
comes from `stroke-linejoin="round"`, never from an explicit rounded path. One visual weight per
icon. Colour is **always** applied by USS tint and never baked into the SVG; every file ships
`stroke="#fff"`. All 19 icon tint rules are already tokenised.

### 6.2 The list

Derived strictly from `UI-ContentInventory.md`. **No icon is needed that does not already exist**,
with one exception.

| Icon | Use | Exists |
|---|---|---|
| `play`, `controls`, `settings`, `credits` | Title menu | ✅ |
| `back` | Match setup ▸ Title | ✅ |
| `create`, `join` | Match setup tabs | ✅ |
| `elimination`, `hill`, `flag` | Mode options (`flag` permanently disabled) | ✅ |
| `player`, `bot` | Opponent options | ✅ |
| `fog` | Fog-of-war checkbox | ✅ |
| `check` | Checkbox; Crew ▸ Confirm | ✅ |
| `flip-card` | Unit-card flip affordance | ✅ (a card affordance, not a grid glyph) |
| `ability-flare` | Ability-mode card vignette | ✅ (a texture, exempt from the grid) |
| `chevron-down` | Dropdown arrow | ❌ **add** |

Exactly **one** new glyph. Quit deliberately has no icon.

### 6.3 Production route

**Author in-house SVG on the existing grid; pull from Lucide only where a stock glyph is already
the right answer.** The set exists and is good, the game-specific glyphs (`hill`, `elimination`,
`fog`, `flip-card`) have no stock equivalent that means the right thing, and a 17-glyph set does not
justify a dependency.

**Do not generate icons with an image model**, even though `generate_image` is configured. Generated
icons have inconsistent stroke weights and off-grid geometry, and at 24px that is the single most
obvious tell of AI-assisted UI work. §16 covers where generation *is* right.

- **Lucide** — <https://lucide.dev>. Licence **ISC**; a Feather-derived subset is MIT. Both require
  the notice to be preserved, so if any Lucide path is used, add
  `Assets/UI/Shared/Icons/LICENSE-lucide.txt` with the upstream `LICENSE` verbatim.
- `chevron-down` is a straight lift: `<path d="m6 9 6 6 6-6"/>`.
- Wrapper, changing only `stroke-width` to `2`:
  ```svg
  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><!-- paths --></svg>
  ```
- Vector Graphics importer: **Texture Size 96** (4× the display box) and **Preserve Viewport** on.

---

## 7. World / arena

### 7.1 The measurements you are designing against

Unchanged from Night Range. Every one of these is a gameplay value read from shipped code.

| Fact | Value | Source |
|---|---|---|
| Cell size | **2.7 world units** | `GameLoop.cellSize` |
| Board | 15 × 10 cells = **40.5 × 27** | `GridSystem.ColumnCount/RowCount` |
| Board centre | `(18.9, 0, 12.15)` | derived |
| Camera | pitch **73.1°**, height **24.4**, at `(18.9, 24.4, 3.4)` blue / `(18.9, 24.4, 21.0)` red, FOV 60 | `GameLoop.cameraPositions` |
| Wall cells | 18, mirrored about column 7 | `GameLoop.wallLayout` |
| Wall block | 2.7 × **2.0** × 2.7, origin y 1.0, plus a 2.754 × 0.12 cap at y 2.0 | `Prefabs/Map/Wall.prefab` |
| Wall collider | convex octagonal mesh, trims 0.4 per corner | `Wall.prefab` |
| Grid cell | outer Plane 2.7 (separator), inner Plane 2.5 at y 0.01 | `Prefabs/Map/GridCell.prefab` |
| King of the Hill | cols 6–8 × rows 3–6 = 12 cells → an **8.1 × 10.8** pad at `(18.9, 0, 12.15)` | `GameLoop.KingOfTheHillCells` |
| Unit footprint | 1.3 diameter; capsule r 0.65, h 3.12 | `Prefabs/Units/Unit.prefab` |
| **1 unit-width** | **1.3 world units** = 0.48 cells | derived; used throughout §9 |

### 7.2 What the shipped daylight board already gets right

The user likes the board that ships today, and it is the starting point rather than the enemy. What
is genuinely good about it, and survives:

- **The floor is light** (sRGB 0.58–0.66) so dark figures and dark effects read against it.
- **The floor is cool and low-chroma**, which is already most of the saturation budget.
- **Wall caps read pale against dark wall bodies**, which is exactly the right read at a 73° camera —
  though note §7.4: that read comes from *orientation*, not from a pale albedo, and the distinction
  matters more than it sounds.
- **Hazard stripes** give the board graphic punch and establish a painted-marking language.
- **The sun's elevation is good.** A steep-but-not-vertical mid-afternoon key that throws readable
  shadows without flattening the cover tops. The *azimuth* is a separate matter and has been
  corrected — see §7.7.

What holds it back, and what "elevate to production quality" means concretely:

1. **Post-processing does not render at all.** `renderPostProcessing` is false on the Game camera,
   so bloom, tonemapping and vignette are silently absent. This is the highest-impact single fix in
   the redesign.
2. **Tonemapping is ACES**, which desaturates saturated primaries and milks highlights — the two
   things this direction depends on most.
3. **The lighting is cool everywhere.** A warm-white sun at 1.25 over a cool floor with cool ambient
   reads as overcast morning, not sunshine. Sunlight needs a warm key against a cool fill.
4. **No temperature separation between board and backdrop.** Backdrop 0.50–0.66 against floor
   0.58–0.69 means the board edge does not exist.
5. **The checker delta is 0.08**, effectively invisible, and there is no separator line.
6. **Nothing casts a contact shadow**, so units and cover float.
7. **Team blue is a hot violet `#2F43F5`**, which reads as a default rather than a decision.

### 7.3 Floor treatment

- Retire `Map_FloorDark*`. Retarget `Map_FloorDay` → `Map_DeckA` and `Map_FloorDayAlt` →
  `Map_DeckB`, URP/Lit: `_BaseColor` `--bp-ground-a` / `--bp-ground-b`, **`_Metallic` 0**,
  `_Smoothness` **0.12**, `_SpecularHighlights` **off**, `_EnvironmentReflections` **off**.
- **Nothing in the arena is metallic.** Not the floor, not cover, not the rail, not the props. A
  painted diorama is plaster and plastic. This one rule kills more accidental ugliness than any
  other in this section, because a specular highlight on a bright board is indistinguishable from
  an effect.
- **Checker**: `Map_DeckB` where `(col + row) % 2 == 1`. One rung apart is enough now that a
  separator line exists.
- **Separator line**: narrow the `Inner` child's `localScale` from `0.9259` to **`0.9556`**, giving
  a **0.12-unit** line between cells. Material `Map_GridLine`: `_BaseColor` `--bp-ground-line`,
  `_Metallic` 0, `_Smoothness` 0.05, emission black. On a light board the line is *darker* than the
  fill — the inversion from Night Range is correct and intended.
- **Painted markings** — this is the MapVibe transfer and it is where the board gets its character.
  Flat non-colliding decal quads at y 0.02, URP/Lit Transparent, `_Smoothness` 0.02:
  - Lane chevrons in `--bp-ground-paint` at 0.75 alpha, every 5 cells along both long edges.
  - L-shaped corner ticks at cell intersections along columns 0, 7, 14 and rows 0, 4, 9.
  - Two 0.35-wide hazard bands in `--bp-paint-hazard` with `--bp-ink` edging, running the full 40.5
    width along rows 0 and 9 (the deployment ranks).
  - A hexagonal paving inlay across the central 5 × 4 cells at 0.35 alpha, `--bp-ground-b`.
  - **Rule**: a decal is never saturated above the prop budget, never animated, and never occupies a
    full cell face — otherwise it will be mistaken for an overlay.

### 7.4 Cover kit

**The one rule that must not be broken: every wall cell blocks line of sight by rule, so every wall
prop must read as a full, opaque sight blocker.** No half-height crates, no shot-through railings,
no see-through fences. Variety comes from silhouette detail, never from height.

Four variants sharing footprint 2.7 × 2.7, height 2.0, the existing convex octagonal MeshCollider,
and the same material set. Rotate in 90° steps, distribute pseudo-randomly by cell coordinate.

| Prop | Form | Dimensions |
|---|---|---|
| `Cover_Block` | Bevelled block, 0.06 chamfer on all top edges | 2.7 × 2.0 × 2.7, cap 2.754 × 0.12 at y 2.0 |
| `Cover_Container` | 7 vertical ribs, 0.08 deep × 0.22 wide, on each long face | same |
| `Cover_Stack` | Two blocks with a 0.10 recessed shadow gap at y 1.05 | same total |
| `Cover_Pillar` | Chamfered top (0.25 × 45°), recessed 0.9 × 0.5 plate on two faces | same |

Materials: body `Map_Cover` = `--bp-cover`, `_Metallic` 0, `_Smoothness` 0.18. Cap `Map_CoverCap` =
`--bp-cover-cap`, `_Metallic` 0, `_Smoothness` 0.22. ID plate `Map_CoverPlate` = `--bp-cover-plate`,
`_Metallic` 0.10, `_Smoothness` 0.28, **non-emissive**.

**The cap is what makes cover readable from a 73° camera, where you see tops and almost nothing
else.** Read the values in rendered space, not in the picker. **Measured on the shipped board**:
deck **0.660**, cap **0.802**, lit body **0.265**, shaded body **0.148** — four separated steps,
smallest **0.14**. Cover reads twice over: once as a dark mass against a light deck, again as a
cap-to-body step that gives it form.

> **`--bp-cover-cap` is darker than `--bp-cover` in the picker and lighter on screen. This is
> correct, and it is the opposite of what a dark board wants — do not "fix" it back.** The cap is
> horizontal and the body is vertical, so the cap collects materially more irradiance; authoring it
> pale *as well* — the intuitive move, and what an earlier draft did — drives it above the deck and
> makes cover the brightest object in frame, on a board whose whole thesis is that lightness is the
> scarce resource. The general rule is in Palette §1.4.5: **check whether lighting and albedo are
> adding or cancelling before you set the step.** Here they add; on a dark board they cancel.
>
> ⚠ **The exact orientation ratio is currently withdrawn and pending a probe** (Palette §1.4). The
> cover kit is **not** blocked by that: the decisive comparison is cap against deck, both horizontal,
> so the disputed factor appears on both sides and cancels. The measured figures above stand.

The cap also cannot go much darker than specified. Push it far enough toward the body and the
lighting cancels the albedo exactly: cap and body land on the same pixel value and the piece reads
as a hole in the deck rather than a solid.

Budget: ≤ 240 tris per prop, ≤ 3 materials each, no LODs (18 instances, fixed camera), no textures.

**Build with ProBuilder, not `generate_model`.** All four must share an exact 2.7 × 2.7 footprint, a
2.0 height and the existing collider, and all three are gameplay values. See §16.

### 7.5 Board rail and backdrop

The cheapest, highest-impact addition in the document. The board is currently a plane floating in
colour.

- **Rail**: a closed bevelled frame around the 40.5 × 27 perimeter. Cross-section 0.60 wide × 0.40
  tall, top chamfered 0.08, material `Map_Rail` = `--bp-rail`, `_Metallic` 0, `_Smoothness` 0.20.
  A dark warm frame against the pale cool board — it is the picture frame, and it is what makes the
  diorama read as an object rather than a viewport.
- **Corner posts**: 0.50 × 1.20 × 0.50 at the four corners, `Map_Rail`.
- **Spawn ranks**: two 40.5 × 3.0 painted bands just outside rows 0 and 9, `--bp-blue-wash` at the
  blue end and `--bp-red-wash` at the red end, over the deck material. Painted, not emissive. This
  gives each player an instant "my side / their side" read on a symmetric board.
- **Backdrop**: camera `Clear Flags = Solid Color`, background `--bp-sky`, **no skybox** — retire
  the `skyboc.mat` placeholder. Beyond the rail, three staggered rings of out-of-play silhouette
  blocks at y −0.8, 6–22 units out, heights 1.5–5.0, material `Map_Backdrop` = `--bp-sky` darkened
  two rungs, `_Smoothness` 0.05. Pure parallax; keep them below the rail's top line so they never
  read as cover. Budget ≤ 40 blocks, one material, static-batched.

### 7.6 Hill / objective marker (King of the Hill only)

- The 12 pad tiles get `Map_DeckHill` — `--bp-ground-hill` with a 0.08 `--bp-ground-paint` inset
  border on the pad perimeter. **The hill goes darker than the checker, not lighter.** Darkness is
  the resource with headroom on this board, and a darker centre also gives contested-cell effects
  something to read against — which matters more here than anywhere else on the deck, because this
  is the cell that will be covered in FX at the moment it decides the match.
- The pad is recessed **0.05** in Y so it reads as a physical platform.
- A perimeter groove 0.10 wide at y 0.03 carries control state through the existing `GameLoop` hill
  overlay (`BattlePlan/GroundGlow`). **On a bright board this runs in Multiply, not Additive** —
  see §9.2:

| State | `_CompositeMode` | `_GlowColor` | `_Intensity` | `_PulseSpeed` | `_PulseAmount` |
|---|---|---|---|---|---|
| No control | 2 (Multiply) | `--bp-ground-line` | 1.0 | 0 | 0 |
| Blue control | 2 (Multiply) | `--bp-blue` | 1.0 | 0.5 | 0.20 |
| Red control | 2 (Multiply) | `--bp-red` | 1.0 | 0.5 | 0.20 |
| Contested | 0 (Additive) | `--bp-amber` ×1.4 | 1.6 | 2.0 | 0.45 |

Contested is the one hill state allowed to be additive, because it is the one that means *act now*.
Everything else darkens.

- Four corner posts at the pad corners, 0.25 × 1.60 × 0.25, `Map_Rail`, with a 0.20 × 0.05 painted
  band at y 1.40 in the controlling team's colour.

### 7.7 Lighting rig

**One directional light over a gradient ambient.** Authoritative source is
`Assets/Editor/ArenaBuilder.cs`, which *aims* the sun and *asserts* everything else against recorded
values rather than writing them, so a rebuild can never flatten the lighting.

| Light | Type | Colour | Intensity | Rotation | Shadows |
|---|---|---|---|---|---|
| `Key Sun` | Directional | `#FFF7E8` — sRGB (1.00, 0.97, 0.91) | **1.25** | **(52, 90, 0)** | **On**, Soft, strength **0.72**, bias 0.05, normal bias 0.4 |

Ambient: **Gradient.** Sky (0.72, 0.84, 0.91), Equator (0.43, 0.61, 0.70), Ground (0.20, 0.29, 0.35),
Intensity **0.78**. Reflection intensity **0.60**. Camera clear colour is **`--bp-sky`**.

> **The clear colour is cited as a token and never as a literal.** A previous revision wrote
> "(0.55, 0.74, 0.82) = `--bp-sky`", and the equality was false — the literal was the shipped clear
> colour and the token was `#9CC6DC`, two different blues asserted as one. **The palette wins**, as
> it does everywhere; restating a palette value inline is what let them drift apart, so this section
> no longer does it. `--bp-sky` has since been retuned (Palette §11.8): at 47.8% saturation and
> 12.1° from team blue it broke the hue law, and it is the largest area in frame.

#### Azimuth 90° is a fairness requirement, not a look preference

**Do not move the sun off the board's long axis.** The reasoning outranks aesthetics and must survive
this document:

The camera flips **180° in yaw between seats** (`GameLoop.cameraPositions`). The sun does not flip —
it is a world object. So an off-axis sun throws every cover shadow toward one player and away from
the other: one seat reads its approach cells over clean deck while the other reads the same cells
through shadow. **That is a competitive asymmetry produced by lighting**, and it is invisible in
review because no single screenshot contains both seats.

Putting the key on the long axis (azimuth 90°, shadows travelling along +X) makes the shadow
*perpendicular* to the flip axis. Both seats then see shadows running sideways across the screen,
identical in length and identical in relation to their line of approach. It is the only azimuth
where the two seats are the same.

The previously shipped `(50, −30, 0)` is off-axis and carries exactly this bias. An earlier draft of
this section preserved it, on the grounds that the shipped board was the thing the user liked. That
was the wrong test: it was read from the scene rather than evaluated, and the arena implementer had
already corrected it. **The correction stands and the reasoning is now recorded here so nobody
re-derives a prettier diagonal in six months and quietly biases every match.**

The cost is real but small. A perpendicular key gives shadows that run exactly horizontally in screen
space, which is more graphic and less "photographic" than a diagonal. On a *diorama* that reads as a
feature rather than a compromise — it is how raking light falls across an architectural model — so
the fairness constraint and the look happen not to conflict here. If they ever did, fairness wins.

One artefact to watch: at exactly 90° the shadows are parallel to the grid rows, so a cover shadow
lands squarely on the cells of its own row and can be mistaken for a painted marking. **Resolve this
by material, never by rotating the sun.** Shadows are cool (`--bp-shadow`, via the §7.8 split
toning); deck paint is warm (`--bp-ground-paint`). Warm-versus-cool separates them at a glance while
the geometry stays fair.

#### Elevation 52° and shadow strength 0.72

Elevation sets shadow length at `2.0 / tan(52°)` = **1.56 m**, which is **0.58 of a 2.7 m cell**.
That is long enough to read "full height" instantly and short enough that a shadow never fills the
neighbouring cell — so a shadow can never be misread as occupancy. It also holds the cap plane at
`cos(38°)` = 0.79 of full irradiance, keeping the top of a cover piece visibly lit even at a dark
albedo, which is what stops the kit reading as a hole punched in the deck.

Shadow strength **0.72**, not 1.0: on a bright board a pure-black shadow reads as a hole. But cast
shadows are the **primary sight-blocker cue** on this board, so this value is load-bearing rather
than polish — do not soften it for looks.

#### ~~Warmer key, sky fill, bounce~~ — REJECTED, NOT THE SPEC

> **Everything in this sub-heading describes a rig that was considered and rejected. The rig in
> force is the single-light table above.** Nothing below specifies anything. It is kept only so the
> decision is not re-taken.
>
> *(General hygiene, not a fix: withdrawal prose reads like specification when skimmed, so anywhere
> this document records a rejected option it gets a struck heading and a banner like this one.)*

~~An earlier draft specified a warmer, brighter key (`#FFE9C4` at 1.55) plus a cool sky fill and a
warm bounce~~, to push the board from overcast toward afternoon. **That is withdrawn.** The rendered value
of every board surface is calibrated against this exact rig (palette §1.4); adding two lights and
raising the key by 24% would invalidate the entire cover ladder, and re-deriving it is not worth a
colour-temperature preference.

**Grade for warmth instead of relighting for it.** §7.8's White Balance and Split Toning deliver
warm highlights and cool shadows at zero cost to the value ladder, which is where that adjustment
belongs anyway.

URP shadow settings for the Game scene: Max Distance **60**, Cascades **2** (split 0.15), Depth Bias
1.0, Normal Bias 1.0, Soft Shadows **Medium**.

Additional lights: muzzle-flash and impact point lights pooled and capped at **4 concurrent**
(§8.5).

### 7.8 Post-processing stack

**Edit `Assets/Settings/GameDaylight Volume Profile.asset` in place.** The Game scene already
references it, so there is no rewiring, and the original values are recoverable from git. Deltas
from what ships today:

| Override | Currently | **FIELD DAY** | Why |
|---|---|---|---|
| **Tonemapping** | ACES | **Neutral** | ACES desaturates saturated primaries and milks highlights. This direction lives on saturated opaque colour surviving to the frame buffer. |
| **Bloom · threshold** | 1.15 | **1.8** | Nothing in the environment may bloom. Only the seven HDR values in the palette §6 cross it. |
| **Bloom · intensity** | 0.35 | **0.5** | Hard ceiling. Above 0.5 the core of a hot effect goes soft even at a high threshold, and a soft core on a bright board reads as fog. |
| **Bloom · scatter** | 0.62 | **0.55** | Tighter, so the little bloom there is stays attached to its source. |
| **Bloom · clamp** | 65472 | **8** | Stops one stray HDR pixel from blowing a hole in the frame. |
| **Bloom · HQ filtering** | off | **on** (off on the Low tier) | — |
| **Color Adjustments · post exposure** | +0.12 | **0.0** | With Neutral tonemapping, +0.12 clips the card stock. |
| **Color Adjustments · contrast** | +6 | **+14** | The board needs the ink to bite. |
| **Color Adjustments · saturation** | +1 | **+8** | Pushes the small saturated pieces without touching the low-chroma ground much, because there is little chroma there to push. |
| **White Balance** | absent | **Temperature +10, Tint 0** | Sunlight. Raised from +6 in the second revision: the key light stays at its recorded near-neutral colour (§7.7), so the whole warm shift now happens here. |
| **Split Toning** | absent | **Shadows `#3E5B73`, Highlights `#FFEFD2`, Balance +12** | Cool shadows, warm highlights. The single biggest "sunny" lever in the stack, and — with the key held neutral — the *only* thing separating this board from an overcast one. Tune this pair before ever touching the sun. |
| **Vignette** | 0.025 | **Colour `--bp-shadow`, Intensity 0.16, Smoothness 0.45, Rounded off** | Just enough to say "a lit table". Above 0.25 it starts to read as serious. |
| **Film Grain** | 0 | **Off** | UI Toolkit composites *after* post, so grain reaches only the 3D half of the frame and visibly splits one screen into two media. |
| **Chromatic Aberration** | absent | **No** | It smears the 0.12-unit separator, which the whole board is built on. |
| **Depth of Field** | absent | **No** in gameplay | Menu scenes may use Bokeh. |
| **Motion Blur / Lens Distortion / Panini** | absent | **No** | — |

⚠ **`renderPostProcessing` is false on the Game camera.** None of the above renders until that is
true. Verify it first; everything else in this section is wasted otherwise.

### 7.9 Fog of war

Night Range's strongest argument was "hidden is simply unlit." On a bright board that is gone, and
the replacement has to survive the fact that **most of the board is fogged most of the time**.

**The treatment: unpainted board.** Hidden cells render as a flat, chroma-free wash slightly
*lighter* than the deck, with a crisp cell-quantised boundary line. The parts of the diorama not in
play are simply not painted in yet.

| Element | Property | Value |
|---|---|---|
| Fill | `_FogColor` | `--bp-fog` at alpha **0.68** |
| Boundary | `_BoundaryColor` | `--bp-fog-edge` at alpha **0.85** |
| Boundary falloff | `_EdgeSoftness` | **0.06** |
| Legacy multiplier | `_BoundaryOpacity` | **1.0** — see the warning below |
| Composite | `_CompositeMode` | **1 (Alpha)** |

**The boundary is a static contour and takes §4.3.1's static floor: 1.56 screen pixels, 0.043 on
the deck plane.** `_EdgeSoftness 0.06` controls the falloff, not the width, and the two are not
interchangeable — the drawn width is currently unspecified in this table and must be measured off
the shader before anyone claims the boundary meets the floor. The 3.98:1 and 3.39:1 figures below
are full-coverage values and only hold once it does.

**Alpha lives on the colours; `_BoundaryOpacity` is not a second alpha.** An earlier revision listed
`--bp-fog-edge` at 0.85 *and* `_BoundaryOpacity` 0.55 as though both were the boundary's opacity.
They are not the same knob, and specifying both invites an implementer to multiply them. The alpha
channel of `_BoundaryColor` is authoritative; leave `_BoundaryOpacity` at 1.0.

Composited over `--bp-ground-a` in linear, which is how the shader actually blends
(Palette §7.4 carries the working):

| Reads | Ratio | Need |
|---|---|---|
| Fill against unfogged deck | **1.18:1** | ≥1.1, deliberately low |
| Boundary against the fill | **3.98:1** | ≥3.0 |
| Boundary against unfogged deck | **3.39:1** | ≥3.0 |

⚠ An earlier revision quoted **1.38:1** and **4.07:1** here. Both were raw token pairs computed as if
fog were opaque, and they disagreed with this document's own contrast table. Composited at the
specified alphas the old tokens delivered 1.20:1 and **2.85:1** — the boundary missing the threshold
that this section's entire argument rests on. `--bp-fog-edge` moved from a mid grey to a near-ink to
fix it.

**The edge does the work, not the fill.** That inversion is the whole design and it is why this
beats the alternatives:

- **Darkening hidden cells** uses the luminance axis, which §4.3 has already spent on the ink
  contour, and because most of the board is fogged it would make the frame majority-dark — the dark
  board again, by the back door.
- **A literal fog or cloud overlay** is soft-edged, which fights every other edge in the game, and
  it obscures the terrain that the shipped fog design requires to stay visible.
- **A blueprint or grid treatment** is legible but reads as a different game's UI laid over the
  board.
- **Desaturation plus a hard edge** uses the *chroma* axis, which is free; never makes the fogged
  region the heaviest thing on screen; preserves terrain silhouette exactly as the mechanic
  requires; and is diorama-native — unpainted board is a real physical read.

Two implementation notes:

- `BattlePlan/FogOverlay` is **already alpha-blended** (`Blend SrcAlpha OneMinusSrcAlpha`,
  hardcoded). The earlier claim that it reads as additive brightening was wrong, and this correction
  holds. **But the conclusion drawn from it — "a colour change, not a shader change" — was also
  wrong, and in a way that would have shipped an anti-pattern.** Two defects, both now fixed:
  - The shader had **one colour**. Unpainted board needs two: a pale fill and a dark boundary.
    `_BoundaryOpacity` only scaled the alpha of that single colour, so the boundary this section
    specifies would have rendered as a *paler band of the wash* rather than a darker line.
    `_BoundaryColor` has been added.
  - The boundary term **ran backwards**: `alpha = _FogColor.a * lerp(_BoundaryOpacity, 1.0, edgeFade)`
    with `edgeFade` 0 on the seam and 1 inside, so the wash faded *out* at its own edge. Raising
    `_BoundaryOpacity` 0.32 → 0.55 as an earlier revision instructed would have softened the feather
    rather than drawn a line — producing anti-pattern 17, soft-edged fog, by following the
    instruction literally. The term is inverted.

  Worth recording as a general point: *"the blend mode is already right"* is not the same as *"the
  shader can express this"*, and only the second one licenses "colour change only".
- The overlay is a **floor tile at y 0.05**; walls are 2.0-tall geometry above it and are not
  overlaid at all, so terrain keeps full colour and reads *more* strongly against the pale wash.
  That is the desired outcome — terrain is always visible by rule. If in play the walls read as
  highlighted rather than merely present, the fallback is to desaturate wall materials in fogged
  cells via a rendering-layer pass, but do not build that until it is observed.

### 7.10 Y-order contract

Unchanged. Every ground-plane element, so parallel implementers stop z-fighting each other.

| y | Element |
|---|---|
| 0.000 | Grid cell separator plane |
| 0.010 | Grid cell face (`Inner`) |
| 0.020 | Painted deck decals |
| 0.030 | Hill perimeter groove |
| 0.040 | Move-range / ability-range overlay cells |
| 0.050 | Fog overlay tiles (**existing, do not move**) |
| 0.060 | Vision cones (`VisionConeVisual.groundOffset`, **existing**) |
| 0.080 | Impact shockwave rings (`ImpactShockwave`, **existing**) |
| 0.090 | Ability telegraphs and dodge markers |
| 0.110 | Plan path edges and nodes |
| 0.130 | Unit contact shadow blob |

Anything new picks a free slot and is added to this table in the same change.

---

## 8. Projectiles

**Do not touch any collider.** `Shooting.cs` uses a projectile-radius `SphereCast` for line of
sight, so the bullet's `SphereCollider` radius is a gameplay value. Meshes, materials and trails
only.

The governing change from Night Range: **projectiles are opaque contoured objects, not glowing
ones.** On a light board an emissive bullet below the bloom threshold is just a pale dot, and above
it is a smear. Every projectile now reads as a small dark-edged solid.

### 8.1 Bullet — blue and red

Fired by Commander, PogoRider, Shotgunner, Soldier. Speeds 2–5 cells/s.

- **Geometry**: replace the 0.5-diameter sphere with the built-in **Capsule** at `localScale
  (0.11, 0.21, 0.11)`, `rotation (90, 0, 0)` — a 0.11 × 0.42 slug aligned to travel. Zero new art,
  and a slug reads as directional where a ball does not.
- **Material** `BulletBlue.mat` / `BulletRed.mat`, URP/Lit: `_BaseColor` `--bp-blue` / `--bp-red`,
  **`_EmissionColor` black — emission off**, `_Metallic` 0, `_Smoothness` 0,
  `_SpecularHighlights` off.
- **Contour**: a second Capsule child at `localScale (0.152, 0.24, 0.152)`, material `FX_Contour`
  (`BattlePlan/FXUnlit`, `_CompositeMode` 1, `_RimMode` 1, `--bp-ink`, cull Front). This is the ink
  outline that makes the bullet legible over pale concrete, and it is the difference between the
  slug reading and vanishing.
  **The shell constant, for all four projectiles: widest drawn profile + 0.021 radial, + 0.030
  axial.** It is an offset, never a ratio, and it is taken from the widest drawn profile rather than
  the body — see §4.3.1 for both, and for why the radial figure moved from 0.020. On the bullet the
  two coincide: 0.11 + 2(0.021) = 0.152 across, 0.42 + 2(0.030) = 0.48 long.
- **Tracer**: `TrailRenderer`, time **0.10 s**, width 0.09 → 0.0, `alignment: View`,
  `textureMode: Stretch`, colour gradient `--bp-blue-hi` / `--bp-red-hi` → transparent at 0.7 alpha,
  `shadowCastingMode: Off`. Note the `-hi` values: the tracer must be *darker* than the deck.
- **Peak footprint**: **0.32 unit-widths** long. Bullets stay small so abilities can be huge.

### 8.2 Sniper shot

50 damage at 15 cells/s = 40.5 units/second, 0.68 units per frame at 60fps. A short projectile will
strobe; the length is a technical requirement.

- **Geometry**: Capsule at `localScale (0.09, 0.68, 0.09)` rotated to travel — a 0.09 × **1.36**
  lance, half a cell long. Contour child at `localScale (0.132, 0.71, 0.132)`, taking §8.1's shell
  constant: 0.09 + 2(0.021) across, 1.36 + 2(0.030) long. **Not 1.4×.** An earlier draft said "1.4×
  per §8.1", which happens to land within a rounding step on the diameter and gives 1.90 on the
  length — a lance 34% too long. The shell is a fixed offset; a ratio applied to a 15:1 shape does
  not survive its own long axis.
- **Material** `SniperSuperBullet.mat`: `_BaseColor` `--bp-ink`, `_EmissionColor` black. **The
  sniper round is the one projectile authored in ink** — it is the one that one-shots, and on a
  bright board the most alarming thing you can draw is a black lance. Team identity comes from the
  trail. This also makes it the one projectile that does not depend on its contour to be seen: at
  0.09 it is the narrowest host on the board and sits within 7% of §4.3.1's cap, so a shape that
  clears the deck unaided is the robust choice there and not only the dramatic one.
  ⚠ The shell is still worth keeping, but **not** because the body needs outlining — because the
  body is `URP/Lit` and the shell is unlit, so under the §7.7 key the lit lance lifts off ink while
  the shell stays at it. The shell is the only part of the sniper round actually at `--bp-ink`.
  That is reasoned, not measured; treat it as a claim to verify in the same rendered-space probe
  that palette §1.4 is waiting on, and do not restate the lit body's value as a contrast figure
  until someone has read it off a frame.
- **Tracer**: time **0.18 s**, width 0.16 → 0.0, gradient `--bp-blue-hi` / `--bp-red-hi` →
  transparent.
- **Target-lock laser** (the 2 s pre-shot, `Shooting.targetLockDuration`): `BeamVFX` at
  `coreWidth 0.03`, `glowWidth 0.20`, **`_CompositeMode` 1 (Alpha)** at 0.88, colour `--bp-red-deep`,
  `Pulse(1.4, 0.5)`. A dark line, not a glowing one — Alpha rather than Multiply because it is drawn
  at unit height and will cross cover (§9.2.1). Plus a `GroundGlow` disc under the marked
  unit: 1.6 diameter, `_RingWidth 0.30`, `_CompositeMode` 2, `_GlowColor` = the caster's team
  colour, `_PulseSpeed 1.5`. It is a threat, not an attack, and must never out-read Area Lock.
- **Peak footprint**: **1.05 unit-widths**.

### 8.3 Grenade

- **Geometry**: an 8-sided cylinder, 0.30 diameter × 0.42 tall, 0.34 × 0.06 cap ring, 0.10 sphere
  fuse dot on top. Keep the existing `SphereCollider`.
- **Contour**: `0.382 × 0.48`, §8.1's shell constant off the **0.34 cap ring**, not the 0.30 body.
  Off the body it would be 0.342 — landing on the cap ring and the hazard band, z-fighting both and
  enclosing neither. This is the case that makes "widest drawn profile" a rule rather than a
  preference.
- **Material** `Grenade.mat`: `_BaseColor` `--bp-cover`, `_Metallic` 0, `_Smoothness` 0.15, with a
  0.34 × 0.08 band in `--bp-paint-hazard` edged in `--bp-ink`. The fuse dot is a separate material,
  `_EmissionColor` = the muzzle-flash HDR value scaled by the blink curve — one of the few things
  permitted to bloom.
- **Fuse blink**: emission multiplier 0 → 1 → 0 on a square-ish curve, ramping **2 Hz → 9 Hz**
  across the 1 s arc. Drive with a `MaterialPropertyBlock`, never a material instance.
- **Arc trail**: time 0.35 s, width 0.05 → 0, `--bp-ink` at 0.30 alpha → transparent. An ink arc on
  a pale board tells you exactly where it is going.
- **Retired**: the current green grenade `#00CA41`. Green is a UI colour and a green object on the
  board breaks the palette contract.

### 8.4 Smoke canister

- **Geometry**: keep the cylinder, 0.45 diameter × 0.80 tall, plus two 0.48 × 0.05 cap ribs.
- **Contour**: `0.522 × 0.86`, §8.1's shell constant off the **0.48 ribs**, not the 0.45 body.
- **Material** `SmokeCanister.mat`: `_BaseColor` `--bp-deck-5`, `_Metallic` 0, `_Smoothness` 0.18,
  one 0.46 × 0.06 `--bp-paint-hazard` band edged in ink.
- **Trail**: none in flight. It is not a weapon and should not read as one.

### 8.5 Muzzle flash and impact

- **Muzzle flash**: an additive Quad, 0.55 units, four-point star, `BattlePlan/GroundGlow` in disc
  mode, `_CompositeMode` **0 (Additive)**, `_GlowColor` = the muzzle-flash HDR value from palette
  §6, `_EdgeSoftness 0.7`, `_Intensity 2.2`. Life **0.06 s**, scale 0.55 → 0.80, billboarded. This
  is one of the seven things allowed to bloom — it lasts two frames, which is what buys it the
  exemption.
  Plus a pooled `Point Light`: range 3.0, intensity 6, colour = team, life **0.05 s**. **Cap at 4
  concurrent** — the Shotgunner fires 10 pellets at 0.01 s intervals and will otherwise blow the URP
  additional-light budget in one burst. Below the cap, skip the light and keep the quad.
- **Bullet impact**: a Shuriken burst of **5** sparks — speed 3–6, lifetime 0.12–0.22 s, size 0.05,
  gravity 2.0, colour over lifetime team `-hi` → `--bp-ink` → transparent, Renderer Mode
  **Stretched Billboard**, Speed Scale 0.06. Note the direction of the gradient: sparks *darken* as
  they die rather than fading to white. Plus `ImpactShockwave.Spawn(point, teamColor, 0.40f, 0.18f,
  withLightPop: false)` and `HitFlash.FlashTarget(victim, 0.10f, 1.4f)`.
- **Wall impact**: 4 sparks, no shockwave, plus 3 debris particles (size 0.06, gravity 3,
  **`--bp-cover-cap`**, lifetime 0.5 s). Note the colour: debris chipped off a cover block must be
  *lighter* than the block, not the block's own value, or it is invisible against the thing it came
  off. Same reason the spark gradient here terminates at `--bp-deck-0` instead of `--bp-ink` — the
  contour inverts on a dark host (§4.3, §9.2.1).

---

## 9. VFX language

### 9.1 The doctrine

Unchanged. Every combat moment has three beats. No exceptions, no ability with only one.

| Beat | Duration | What it does |
|---|---|---|
| **Anticipation** | **0.25 – 0.50 s** | Something charges, contracts, or darkens. This is what makes the moment readable in advance, and it is where the existing `delayForDodge` and telegraph windows already are — they are anticipation timers that are currently invisible. |
| **Impact frame** | **0.05 – 0.10 s** (3–6 frames) | The whole thing goes 2–5× wider, the victim flashes, the camera shakes. This is the entire trick. |
| **Aftermath** | **0.30 – 0.80 s** | Expanding ring, sparks, decaying mark. Never past 1.4 s total. |

### 9.2 Juice on a bright board — how the punch is produced

Night Range produced impact with white-hot cores against darkness and a bloom threshold of 1.0.
**That entire mechanism is dead.** On a bright board, additive brightening subtracts contrast; a
white core on pale concrete is a hole, not an explosion.

The replacement, in order of preference. Every one of them is silhouette-first and opaque.

| # | Pattern | When | How |
|---|---|---|---|
| **1** | **Dark shape, ink contour** | Default. Telegraphs, threat regions, paths, the sniper lance, any effect that persists longer than 0.2 s | An opaque dark or deeply saturated shape with a hard ink edge at §4.3.1's static floor — 1.56px, 0.043 on the deck plane. Highest contrast, most reliable read. |
| **2** | **Saturated shape, ink contour** | Ability bodies, rings, shockwaves, team-coloured elements | An opaque saturated shape with a hard ink edge. More playful; the ink is what stops it dissolving into a colourful background. |
| **3** | **Bright shape, ink contour** | The impact frame only, and only for single-target moments where context is unambiguous | Reserved. This is the heroic option and it is the least readable, so it never persists past 0.1 s. |

Plus three supporting devices that carry most of the actual weight:

- **The darkening flash.** The inverse of a white flash. At the impact frame, a multiply-blended
  disc briefly darkens the ground under the blast. On a bright board this is the single most
  effective impact device available, and it is the thing that most reads as "expensive".
- **Hard contact shadows.** Every effect element that sits on or near the ground casts a hard-edged,
  zero-blur shadow shape in `--bp-shadow`. Nothing floats.
- **Colour, not luminance.** Saturation shifts read strongly against a 10%-chroma deck. An effect
  can go from `--bp-amber` to `--bp-red` and be felt without changing brightness at all.

**Shaders — the tooling already exists.** The four `BattlePlan/*` shaders plus `BP_FXParticle` and
`BP_FXUnlit` all already expose `_CompositeMode` (`0` Additive, `1` Alpha, `2` Multiply) alongside
`_SrcBlend` / `_DstBlend`, backed by `BP_Composite` in `BP_FXComposite.hlsl`. `BP_FXUnlit`
additionally exposes `_RimMode` (`0` Bright Rim, `1` Dark Rim). **No new shader files are needed.**
Proposals for `FX_OpaqueCore.shader` and `FX_DarkContour.shader` describe capabilities that are
already shipped as modes; adding them would duplicate `BP_FXUnlit` and `BP_FXParticle`.

Blend state per mode — set both the `_CompositeMode` and the blend pair, they are independent:

| Mode | `_CompositeMode` | `_SrcBlend` | `_DstBlend` |
|---|---|---|---|
| Additive | 0 | 1 (One) | 1 (One) |
| Alpha | 1 | 5 (SrcAlpha) | 10 (OneMinusSrcAlpha) |
| **Multiply** | 2 | **2 (DstColor)** | **0 (Zero)** |

Per-shader disposition:

| Shader | FIELD DAY default | Notes |
|---|---|---|
| `BattlePlan/GroundGlow` | **Multiply** for every telegraph, range ring and hill state; **Additive** only for the contested hill and the muzzle flash | The workhorse. Multiply turns it into a darkening contact ring, which is what a bright board wants. |
| `BattlePlan/EnergyBeam` | **Alpha** for the beam body, **Additive** for the thin core only | A near-ink beam with a hot filament. Alpha rather than Multiply because the beam is drawn above the ground plane — see §9.2.1. |
| `BattlePlan/VisionCone` | **Multiply** | Currently additive, which is invisible on a light floor and semantically wrong — a vision cone should tint and slightly darken, reading as *observed*, not *illuminated*. This is a material change, not a shader rewrite. |
| `BattlePlan/FogOverlay` | **Alpha** (already) | §7.9. **Not** colour-only — needed `_BoundaryColor` added and the boundary term inverted. |
| `BP_FXParticle` | Alpha; Multiply for dark debris | — |
| `BP_FXUnlit` | Alpha with `_RimMode 1` (Dark Rim) | The contour workhorse. |

⚠ **`BP_FXParticle` and `BP_FXUnlit` are not in Always Included Shaders.** If either is referenced
by string or only from a material, it strips from a build and renders magenta. Register both.

### 9.2.1 Multiply over dark surfaces — the ruling

Multiply darkens by multiplication, so over an already-dark host it does nothing: the Area Lock beam
crossing a cover block, an impact landing on the ink rail, any ground effect that reaches a cover
footprint. The question put to this document was whether a fallback to alpha should try to *look the
same* as the multiply form, or whether it is acceptable for an effect to read differently over cover
than over open deck.

**Neither, because the question contains a false choice.** The thing that must be constant is not
appearance and not blend mode — it is **meaning**. A player must be able to answer "what is this and
which cells does it cover" identically in both cases. Rendering is free to differ in service of that.

But there is a failure mode worse than either option, and it is the one to legislate against:

> **An effect must never change appearance along its own length or across its own area.** A beam that
> is multiply over deck and alpha where it crosses cover has a visible seam at the cover boundary,
> and the player will read that seam as *information* — as the beam doing something different there.
> A rendering artefact that looks like a game rule is worse than either an inconsistent effect or an
> invisible one.

So the rule is **per-instance, not per-pixel**, and it resolves structurally rather than at runtime:

| Effect sits | Composite | Because |
|---|---|---|
| **On the ground plane** (y ≤ 0.13, the §7.10 Y-order band) | **Multiply permitted** | It only ever composites against deck, painted markings and the fog wash. All are light. Telegraphs, range overlays, the hill groove, contact shadows, vision cones, ground rings. |
| **Above the ground plane** | **Alpha, with an ink contour** | At a 73° camera it *will* overlap cover, units, the rail and the HUD in screen space even when it does not touch them in world space. Beams, cores, shields, smoke, debris. |
| **Point flashes** — footprint ≤ 1 cell **and** life ≤ 0.15 s | **Additive permitted** | See below. |

**The point-flash exemption, and why §9.2.1 governs anyway.** The muzzle flash and the impact flash
stay **Additive**, which is what §6 and §8.5 already say and what is shipped. That is not an
exception grudgingly carved out of this rule — the rule was written too broadly and needed the
qualifier.

The seam argument above is about *extent and dwell*. It applies to an Area Lock beam because that is
a twenty-cell object standing still for three seconds, where a boundary is large enough to locate and
static enough to study. A muzzle flash is a sub-cell quad alive for 0.05 s: there is no seam to read,
because there is no time to read one and nothing to compare it against. And a flash's job is a
brightness spike rather than a shape, so the thing additive is bad at — holding an edge on a pale
deck — is not being asked of it. Over cover, additive is *more* visible rather than less, which is
the opposite failure mode from multiply and is why the two rules differ.

So: **§9.2.1 governs, and §6 and §8.5 are instances of it rather than deviations from it.** The line
is the ≤ 1 cell **and** ≤ 0.15 s test, both conditions required. Anything that fails either — a
grenade detonation ring, a beam, a persistent telegraph — takes the plane rule above. The choice is
isolated behind `FXPalette.FlashComposite`; it stays Additive.

This is why the `EnergyBeam` body and the sniper target-lock are specified Alpha above, correcting an
earlier draft that had them Multiply. **It needs no runtime mode switching at all** for the two cases
that would certainly have hit the problem, which is a better answer than a fallback.

Two consequences:

- **The look barely changes.** Over a pale deck, alpha at 0.85 in a near-ink colour and a multiply
  pass are close to indistinguishable. The real difference is that multiply preserves the painted
  markings underneath and alpha covers them — which is a reason to keep ground effects on multiply,
  and no reason at all to want it above the ground plane.
- **Where an effect genuinely lands *on* a dark host** — a bullet impact on a cover face — alpha
  alone is not enough either, because an ink contour is invisible there too. The contour inverts to
  `--bp-deck-0` per §4.3, and the debris takes `--bp-cover-cap` rather than `--bp-cover`. An effect
  is allowed to look different on a wall than on the floor; a wall is visibly a different object, so
  the difference reads as material response rather than as a rule.

**Bloom intensity must not exceed 0.5** at any quality tier. Above that the core of a hot effect
softens even at a high threshold, and a soft core on a bright board reads as fog rather than energy.

### 9.3 The footprint rule

Unchanged.

> **Ground rings match the ability's rules footprint exactly. Vertical and volumetric elements may
> reach 1.35× the rules footprint at the impact frame, and nothing exceeds that at any time.**

A player must be able to read the danger zone off the effect. A grenade whose flash is twice its
blast radius is not juicy, it is a lie. 1 unit-width = 1.3 world units; 1 cell = 2.08 unit-widths.

| Event | Rules footprint | Peak on-screen | Unit-widths |
|---|---|---|---|
| Bullet impact | point | 0.80 dia | **0.6** |
| Muzzle flash | point | 0.80 dia | **0.6** |
| Sniper shot in flight | point | 1.36 long | **1.05** |
| Shield Rush | 1 cell (self) | 2.70 dia ring | **2.1** |
| Pogo launch / landing | point | 3.20 / 4.40 dia ring | **2.5 / 3.4** |
| Unit death | 1 cell | 2.60 dia ring | **2.0** |
| Area Lock beam, armed | 1 cell wide × ≤ 14 long | 0.34 wide | **0.26 wide** |
| Area Lock rush peak | same | 1.70 wide, 6.40 dia burst | **1.3 wide / 4.9 burst** |
| Grenade | 1.6-cell radius = 4.32 world | 8.64 dia ring (exact), 2.20 dia core | **6.6 ring / 1.7 core** |
| Smoke Screen | 3 × 3 cells = 8.1 world | 8.10 dia (exact) | **6.2** |

Only two effects exceed 5 unit-widths and both have a rules footprint that large. That is the point.

### 9.4 Per-ability specs

Timings are unchanged from Night Range; the visual treatment of each beat is rebuilt for high key.
All built with the existing helpers — `BeamVFX`, `ImpactShockwave.Spawn`, `HitFlash.FlashTarget`,
`SmokeScreenVisual.Create`, `VisionConeVisual`, `CameraEffects.CameraShakeClientRpc` — plus Shuriken
and Line/Trail renderers. **No VFX Graph.** All FX are local-visual; the RPC flow is untouched.

**Smoke Screen** (Commander, 3×3 cells, one execution round).
- *Anticipation, 0.35 s*: the canister arcs in, fuse blinking at 6 Hz; ground telegraph is a
  `GroundGlow` ring, 8.10 diameter, `_RingWidth 0.14`, **Multiply**, `--bp-cover`,
  `_PulseSpeed 2.0`.
- *Impact, 0.08 s*: a darkening disc — `GroundGlow`, **Multiply**, `--bp-shadow`, scale 0 → 4.05
  over 0.08 s; `ImpactShockwave.Spawn(pos, --bp-cover, 4.05f, 0.50f)`.
- *Aftermath, 0.60 s in, then holds*: `SmokeScreenVisual` billows to full over 0.6 s — 9 puff cards
  3.2–4.2 units, `--bp-deck-4` at alpha 0.78, **opaque enough to actually obscure**, height 2.6,
  roll 8°/s. On a bright board smoke must be a solid pale mass with a soft top and a hard ground
  contact, not a translucent haze. Dissipates over 0.8 s at round end.

**Pogo** (PogoRider, 1 s ballistic jump up to 6 cells).
- *Anticipation, 0.30 s*: crouch squash via the Animator (y 0.86, xz 1.10, `--bp-ease-in`); a
  `GroundGlow` ring contracting 3.2 → 0.8 diameter, `_RingWidth 0.25`, **Multiply**, team colour.
- *Launch, 0.06 s*: `ImpactShockwave.Spawn(pos, teamColor, 1.6f, 0.28f)`; 8 dust particles, size
  0.3–0.6, `--bp-ground-line`, lifetime 0.45 s.
- *Flight, 1.0 s*: `TrailRenderer`, time 0.22 s, width 0.18 → 0, team `-hi` → transparent, plus a
  travelling hard shadow blob on the ground beneath — the shadow is how the player tracks the arc.
- *Landing*: `ImpactShockwave.Spawn(pos, teamColor, 2.2f, 0.38f)`; 12 dust;
  `CameraShakeClientRpc(0.18f, 0.06f)`. Sync the ring to the existing `HoldReturnFireWindow`.
- *Backstab hit*: `HitFlash.FlashTarget(target, 0.16f, 2.0f)` plus a three-line slash decal, 1.1
  units, `--bp-ink` at 0.85, **Alpha**, life 0.18 s, random roll.

**Shield Rush** (Shotgunner, 3 s frontal block).
- *Anticipation, 0.25 s*: the shield face scales 0.2 → 1.0 in Y with `--bp-ease-snap`; a
  `GroundGlow` ring contracting 2.7 → 1.4, **Multiply**, team colour.
- *Impact, 0.06 s*: `ImpactShockwave.Spawn(unitPos, teamColor, 1.5f, 0.30f)`.
- *Hold*: `Shield.mat` `_BaseColor` = `--bp-blue` at alpha **0.28**, `_Smoothness` 0.2, plus a
  **0.040 ink rim** on the shield face perimeter (`BP_FXUnlit`, `_RimMode 1`) — §4.3.1's static
  floor of 1.56px at the unit-chest plane. The rim is what makes a translucent plate read on a
  bright board; without it the shield disappears.
- *Blocked shot*: 6 sparks at the hit point; `ImpactShockwave.Spawn(hit, teamColor, 0.28f, 0.16f,
  withLightPop: false)`; shield alpha 0.28 → 0.62 over 0.06 s, back over 0.14 s.
- *Down, 0.24 s*: alpha and rim fade to 0.

**Area Lock** (Sniper, ≤ 3 s armed line, flat 130 damage, 4-round cooldown). **The flagship. This
should be the thing in the trailer.**
- *Arm, up to 3 s*: `BeamVFX.Create(transform, start, end, --bp-red)` with `coreWidth 0.05`,
  `glowWidth 0.34`, body **Alpha at 0.90**, core **Additive**; `beam.Pulse(1.1f, 0.30f)`. A dark red
  line scored across a pale board with a thin hot filament inside it. **Alpha, not Multiply**: this
  beam is the canonical case for §9.2.1 — it is drawn at unit height across the full board and will
  cross cover on most maps, and a beam that thins out where it passes a block reads as the block
  interrupting it. Ground telegraph: a
  `GroundGlow` disc at the centre of **every cell the ray crosses** — diameter 1.2,
  `_RingWidth 0.20`, **Multiply**, `_PulseSpeed 1.6`. Per-cell discs, never a continuous strip: the
  game is tile-crisp and the telegraph must be too.
- *Rush, 0.28 s*: `beam.rushWidthMultiplier = 5.0f;` then `yield return beam.Rush(0.28f)`.
- *Impact frame, 0.08 s*: `HitFlash.FlashTarget(victim, 0.18f, 2.0f)`; a **darkening flash** —
  `GroundGlow`, Multiply, `--bp-ink`, scale 0 → 3.2, alpha 0.55 → 0 across the frame;
  `ImpactShockwave.Spawn(hitPos, --bp-red, 3.2f, 0.55f)`; an 18-spark burst (speed 6–14, lifetime
  0.15–0.35 s, stretched billboard); a bright core at pattern 3 for **3 frames only**;
  `CameraShakeClientRpc(0.28f, 0.14f)`.
- *Lethal only*: a **0.06 s hitstop** at `Time.timeScale = 0.15f`. ⚠ §13 — `Time.timeScale` is
  shared with `DevInput.SetSpeed`; the hitstop must capture and restore the prior value and must be
  skipped entirely when `GameLoop.devMode` is true.
- *Aftermath, 0.45 s*: `beam.FadeOut(0.25f)`; ground discs fade over 0.45 s.

**Grenade** (Soldier, 1 s arc, 1.6-cell radius, 80 damage).
- *Anticipation, 1.0 s (the arc)*: the landing-cell telegraph — a `GroundGlow` disc, diameter
  **8.64 (exact rules footprint)**, `_RingWidth 0.16`, **Multiply**, `--bp-amber-deep`,
  `_PulseSpeed` ramping 1.2 → 4.0 across the fuse. This doubles as the dodge telegraph, so it is
  both the juice and the counterplay.
- *Impact frame, 0.08 s*: a **darkening disc** first — Multiply, `--bp-ink`, scale 0 → 4.32,
  alpha 0.6 → 0 — then a saturated opaque core sphere at `--bp-amber` with an ink contour, scale
  0 → 2.20; `ImpactShockwave.Spawn(pos, --bp-amber, 4.32f, 0.55f)`; 24 sparks + 10 debris chunks
  (size 0.08, gravity 4, `--bp-cover`); `HitFlash.FlashTarget` on every victim;
  `CameraShakeClientRpc(0.30f, 0.12f)`.
- *Aftermath, 0.80 s*: a dust puff — 12 particles, size 1.6 → 2.6, `--bp-deck-3`, alpha 0.45 → 0,
  lifetime 1.2 s, rise 0.4 units/s. Plus a scorch decal, 4.0 diameter, `--bp-ink` at 0.35 alpha,
  fading to 0 over 3 s. **The scorch must be gone before the next planning phase** so it never reads
  as terrain.

### 9.5 Non-ability moments

| Moment | Spec |
|---|---|
| **Unit spawn** | Fade in over 0.35 s, staggered 0.06 s per unit; a `GroundGlow` ring expanding 0 → 1.4 over 0.30 s, Multiply, team colour. |
| **Unit selected** | Selection outline (§11.4) fades in over 0.12 s; a `GroundGlow` disc under the unit, 1.55 diameter, `_RingWidth 0.18`, Multiply, `--bp-blue`, `_PulseSpeed 0.8`. |
| **Path drawn** | The MapVibe plan-spline read. Edge = a 0.10-wide `--bp-ink` strip at 0.70 alpha. Node = a 0.30 `--bp-blue` **chevron** at y 0.11 with an ink contour. **Destination = a 0.42 `--bp-amber` diamond**, ink-contoured. Neutral line, blue waypoints, amber commitment. |
| **Move-range overlay** | Cell quad at y 0.04, `--bp-blue-wash` fill with a 0.06 `--bp-blue-line` inset border. Ability range uses `--bp-amber-wash`; threat uses `--bp-red-wash` / `--bp-red-line`. Friendly overlays carry chevron corner ticks, hostile carry bar ticks. |
| **Dodge alert** | The threat outline (§11.4) at `--bp-amber`, blinking 2 Hz, plus a `GroundGlow` ring 2.0 diameter, `_RingWidth 0.22`, **Additive** (this is an act-now signal), `_PulseSpeed 2.0`. |
| **Bullet whizz-past** | No visual. Audio only — a visual would clutter. |
| **Unit death** | `HitFlash.FlashTarget(unit, 0.10f, 2.5f)`; then over 0.22 s scale `(1,1,1) → (1.25, 0.08, 1.25)` with `--bp-ease-in`; simultaneously `ImpactShockwave.Spawn(pos, teamColor, 1.3f, 0.35f)` and 10 sparks; then despawn. **No corpse, no ground stain** — a dead unit's cell must never read as occupied. |
| **Phase transition** | `hud-flash` per §5, plus a **deck pulse**: the `Map_GridLine` material's `_BaseColor` lerps toward `--bp-ink` and back over 0.45 s at the start of Execution. One global material property, one coroutine, and it darkens rather than brightens. |
| **Round end** | Nothing. The absence of a beat is what makes the execution beat land. |
| **Win / lose** | On the final kill: `CameraShakeClientRpc(0.35f, 0.10f)` and a 0.10 s hitstop (same `Time.timeScale` caveat). Then over 0.6 s the vignette lerps 0.16 → 0.34 while the results scrim fades in. The board stays visible behind the results panel. |

### 9.6 Fog interaction

Telegraphs and beams crossing fogged cells still read — they queue at Transparent+30 or later while
the fog overlay sits at y 0.05 — which matches the shipped fog design where bullets and telegraphs
deliberately pierce fog. Multiply-blended effects over the pale fog wash read *better* than they do
over the deck, because the wash is lighter. Vision cones render over the fog overlay at
`groundOffset 0.06`.

---

## 10. Audio direction brief

**This section documents what has shipped, not what is wanted.** The audio implementer has
regenerated the full set against the toy-diorama direction: **82 clips — 76 SFX and 6 music loops.**
Nothing from the Night Range register survives. Where this section gives a musical or synthesis
parameter, that parameter is in the build today.

The clips are **procedurally synthesised from a parameterised toolkit**, so the entire set rebuilds
in roughly 15 seconds. That changes what this brief should be: sonic character is specified in terms
the toolkit can act on — key, interval, envelope, primitive, duration — rather than as prose about
mood. A described mood is a hope; a specified interval is a contract.

### 10.1 Sonic identity

> **Struck wood on a table. Small, dry, pitched, and satisfying. A tidy workshop rather than a
> machine shop.**

**Primitives.** The five synthesis primitives the whole SFX set is built from:

| Primitive | Character | Used for |
|---|---|---|
| **Marimba** | Pitched, warm, short decay, soft mallet attack | Melody, confirmations, positive states |
| **Woodblock** | Unpitched-ish, hard attack, near-zero sustain | Ticks, taps, path nodes, UI presses |
| **Plastic-knock** | Hollow, mid-bodied, slight ring | Impacts, body hits, game-piece contact |
| **Plucked string** | Bright attack, pitched, medium decay | Bass, arming, tension |
| **Pitched pop** | Short sine-ish transient with a defined pitch | Weapons |

**Music.** The score is in **D major**, moving **I–IV–vi–V**, and it resolves — which is the single
biggest departure from Night Range's unresolving F natural minor bed. Instrumentation: a plucked
bouncing bass, warm major pads, a marimba melody, and a light wooden kit on sixteenths.

**Tempo is 84 BPM and stays there.** That is deliberate, not an oversight: the entire interface is
quantised to its 1/8 note (178 ms), so every UI sound lands on the score's grid, and that contract
was judged worth keeping. The lift into a brighter register comes from **sixteenth-note motion**
rather than from a faster tempo.

> **My read, offered rather than requested:** 84 BPM is the right call and I would not revisit it.
> A brighter tone at the same tempo is a *lighter* feel; a faster tempo would be an *agitated* one,
> and Battle Plan's planning phase is contemplative by design. If the score ever feels sluggish, the
> fix is more sixteenth activity in the kit and a busier bass, not a tempo change — and both of
> those are available inside the existing quantisation. Revisit only if playtesting says planning
> feels slow, and even then try doubling the kit's subdivision first.

**SFX.** Every sound is short, dry, and pitched. Almost no reverb — the board is a table. Reverb
send at 8% on combat only; UI completely dry.

- **Weapons are pitched pops with plastic bodies**, not noise cracks over a sub drop. A weapon
  should sound like a small mechanism, not like a firearm.
- **Impacts are wood and plastic.** Body hits are a padded plastic-knock. No gore, no wet layer.
- **Elimination is a game piece toppling off the board** — a falling three-note figure and a settle.
  This is the strongest single idea in the shipped set, because it states the diorama premise in
  audio: the unit was never a person, it was a piece.
- **The error tone is a gentle descending major second.** Not a gated buzz. Players hear this often
  and it must never punish.
- **Ambience is a warm room**, not HVAC hum.

### 10.2 Mix hierarchy

**Held to as specified and unchanged.** Master target **−14 LUFS integrated**, true peak
−1.0 dBTP. Buses `Master` → `Music`, `Sfx`, `Ui`, `Ambience`, plus one `Duck` send.

| Priority | Layer | Target peak |
|---|---|---|
| 1 | Ability impact, unit death | −6 dBFS. **Ducks every other bus by 4 dB for 220 ms**, 40 ms attack, 180 ms release. |
| 2 | Dodge alarm, urgent timer | −9 |
| 3 | Player's own weapon fire | −12 |
| 4 | UI commit / confirm | −14 |
| 5 | Enemy weapon fire | −15 |
| 6 | Movement, servos | −20 |
| 7 | UI hover / tick | −24 |
| 8 | Music bed | −18 planning, −14 execution |
| 9 | Ambience | −30 |

**Spatialisation, held as specified.** UI and music are 2D. Everything on the board is 3D at
`spatialBlend 0.65`, Linear rolloff, min distance **4**, max **40** world units — sized to the
40.5 × 27 board seen from 24.4 up. Never full 3D: at this camera a fully-spatialised board sounds
distant and thin.

### 10.3 Emotional arc across a match

Rewritten for the bright register. The shape of the arc is unchanged — the *colour* of each beat is
not.

| Phase | Music | Everything else |
|---|---|---|
| Deploying crews | A rising major arpeggio on marimba, 2.4 s | Ambience fades in over 2 s |
| Planning | Bass and pads, marimba melody sparse. **No kit.** | A soft 1/8 woodblock tick from T−10 s; the tick doubles in rate and rises a major third at T−5 s |
| Lock in | Bed holds | A two-stage wooden latch, resolving upward |
| Dodge window | Low-passed to 400 Hz; the melody drops out | Alarm pulse at 2 Hz, pitched rather than buzzed; all buses duck 6 dB |
| Executing | Filter opens over 0.4 s; the wooden kit enters on the downbeat with sixteenths; music +4 dB | Weapons, impacts, footsteps |
| A kill | — | Full duck, the topple figure, then 220 ms of relative silence. **The silence is the payoff.** |
| Round end | A three-note resolve **upward** onto the tonic | — |
| Victory | The full I–IV–vi–V with the marimba melody doubled an octave up, 3.5 s | — |
| Defeat | The same progression, unhurried, ending on vi rather than I — unresolved but not sour, 3.0 s | — |

Defeat deliberately stays in the major key. A minor-key loss sting on a bright toy board reads as
punishment; ending on the relative minor reads as "not this time."

### 10.4 SFX character reference

76 clips, all 48 kHz, mono except music and ambience. Import **Decompress On Load** under 0.5 s,
**Compressed In Memory** above, **Streaming** for music only.

The full identifier list is `AudioCueId.cs` — it is authoritative and this table does not own naming.
What follows is the character contract per class, expressed in toolkit terms.

| Class | Primitive | Envelope | Pitch behaviour |
|---|---|---|---|
| UI hover | Woodblock, filtered | 5 ms attack, 35 ms decay | Fixed, low |
| UI press | Woodblock | 2 ms attack, 55 ms decay | Fixed |
| UI confirm | Marimba | 4 ms attack, 200 ms decay | Rising perfect fifth |
| UI error | Marimba, two notes | soft attack | **Descending major second.** Never harsh. |
| UI back | Marimba | — | Descending perfect fourth |
| Path node | Woodblock | 2 ms attack, 40 ms decay | **+40 cents per waypoint**, resetting when the path resets. **The signature sound of the game** — drawing a five-cell route should feel like winding something up. |
| Commit lock | Plucked string + plastic-knock | two-stage | Resolving to the tonic |
| Timer tick | Woodblock | 60 ms | Quantised to the 1/8. Urgent variant +3 dB, +a major third. |
| Weapon fire | Pitched pop + plastic body | 1 ms attack, 90–130 ms decay | One fixed pitch per archetype, spread across the D major triad so overlapping fire is consonant |
| Shotgunner burst | One dense pitched pop cluster | 420 ms | **One clip for the burst, never ten.** 10 pellets at 0.01 s intervals would be mush and a voice blowout. |
| Sniper lock | Plucked string, rising | **exactly 2000 ms** to match `targetLockDuration` | Rising through the dominant |
| Impact, body | Plastic-knock, padded | 80 ms | Low, fixed |
| Impact, surface | Plastic-knock, hard | 90 ms | Higher, with a short debris tail |
| Elimination | Marimba + plastic-knock | ~700 ms | **A falling three-note figure and a settle.** No scream, no gore. |
| Footstep | Woodblock, soft | 70 ms | 4 round-robin variants, ±3% pitch |
| Ambience | Warm room tone | loop | — |

### 10.5 Implementation notes

- Voice cap **32**. Weapons Priority 128, UI 100, footsteps 200, ambience 250.
- Everything is **client-local presentation**. Play from the existing `ClientRpc` entry points;
  never add a networked audio path.
- Bursts are spawn-limited: one clip per burst, never per bullet.
- **Accessibility**: every cue in the "you must act now" class has a visual twin in §5 and §9.
  Neither channel is ever the only one.

### 10.6 Cue registry — closed

`Assets/Scripts/Audio/AudioCueId.cs` is the authoritative naming registry, and **all 77 cues now
have real call sites**, up from 66. The eleven previously missing — footsteps, dive, grenade throw
and fuse, pogo launch, smoke toss, shield block, surface impact, invalid path, ability-ready and
relay-code — are wired. **The gap list previously recorded in this section is closed.** Bus names
agree exactly: `Music`, `Sfx`, `Ui`, `Ambience`.

---

## 11. Character material treatment

**The meshes are locked.** No remodelling, no accessory resizing, no new props. Everything below is
materials, shading and outlines on the existing FBXs in `Assets/Models and stuff/` and the five
prefabs in `Assets/Prefabs/Units/`.

This is the lightest-touch section in the document, because the existing characters are already
close to right — matte, low-smoothness, few value blocks. The direction change mostly *helps* them.

### 11.1 Shading law

Every `Assets/Materials/Characters/Char_*.mat`, without exception: `_Metallic` **0**,
`_Smoothness` **0.12**, `_SpecularHighlights` **off**, `_EnvironmentReflections` **off**. Already
true of every character material except `Char_Gunmetal`, which keeps a deliberate exception:
`_Metallic` **0.35**, `_Smoothness` **0.30**, `_SpecularHighlights` **on**. Weapons are the only
thing on a character allowed to catch a highlight, and that highlight is how you read which way a
unit is facing from a 73° camera.

Note this is a *reduction* from Night Range's 0.65 metallic. Under a warm sun on a bright board,
0.65 produced a mirror-bright weapon that competed with the effects.

### 11.2 Value blocks

Exactly **three** value bands per unit. If imported materials produce a fourth, collapse the two
nearest.

| Band | Role | Target luminance |
|---|---|---|
| **A** — dominant | Body, fatigues, coat | 0.10 – 0.24 linear |
| **B** — dark | Boots, gloves, harness, hair, weapon | 0.02 – 0.06 linear |
| **C** — accent | Team indicator, one non-team detail | chroma, see below |

Band A rises from Night Range's 0.05–0.14 because a figure that read as a dark silhouette against a
dark deck will read as a black blob against a pale one. Characters stay clearly darker than the
board — the deck is **0.440 linear** — but they are no longer near-black.

**Bands govern body masses, not every pixel.** A material occupying **≥ 15% of the figure's
silhouette area** must sit inside its band. Below 15% it is an accent and is exempt from the band —
but it must still **clear the deck by ≥ 0.10 linear in either direction**, because a small patch
that happens to match the ground is a hole in the figure regardless of how small it is. This is what
makes skin legal without demanding that a face be repainted to a uniform value.

> **Four materials failed these windows and are corrected below.** Three were 2–3.4× outside, which
> is not a rounding. `Char_White` at 0.820 rendered **0.38 above the deck**, making that unit lighter
> than its ground and inverting the read §11.5 calls "the entire read"; `Char_CloakGray` at 0.439 sat
> **0.001 from the deck's rendered value**, which is a cloak that disappears. Both were authored as
> if the board were dark. This is the same authored-versus-rendered trap as the cover cap, and it is
> the most consequential instance of it, because these are the units.

| Material | New `_BaseColor` | Band |
|---|---|---|
| `Char_Black` | `#23272D` | B — raised from `#20242A`, 0.017 → **0.020**, to reach the band floor |
| `Char_CoatNavy` | `#2C3849` | B |
| `Char_Gunmetal` | `#33383F` | B (metallic exception) |
| `Char_HairBrown` | `#4A3524` | B |
| `Char_OliveFatigues` | `#5C6650` | A |
| `Char_JumpsuitOrange` | `#8A6C42` | A |
| `Char_KhakiGear` | `#877C63` | A |
| `Char_Skin` | `#B58D6C` | **Accent** (smoothness 0.14) — 0.423 → **0.300**, clearing the deck by 0.14 |
| `Char_CloakGray` | `#757C85` | A — 0.439 → **0.199**. Was within 0.001 of the deck. |
| `Char_White` | `#84878A` | A ceiling — 0.820 → **0.241** |
| `Char_TealAccent` | `#3C7A82` | C, non-team cool |
| `Char_AmberPads` | `#A87F35` | C, non-team warm |
| `Char_TeamBase` | driven by `TeamBlue.mat` / `TeamRed.mat` | C, team |

**`Char_White` is no longer white, and the name should be read as a role rather than a colour** — it
is the lightest value a body mass may take. That is a real loss: white is a useful value and the
board no longer has it on a figure. It is the right trade anyway, because on a pale deck the
brightest thing in frame should never be a unit. If a genuinely white element is wanted, it is
available as an **accent** under the 15% rule — a helmet stripe or a collar at 0.82 is legal and
reads as a highlight, where a whole torso at 0.82 reads as a hole.

**Non-team accent rule.** `Char_AmberPads` shares a hue family with `--bp-amber` and stays legal
only because it sits well below the signal's saturation and is **never emissive**. Any new warm
character accent must clear the same bar. A bright saturated warm patch on a unit will be read as a
dodge alert and someone will lose a match over it.

### 11.3 Team accent and emission

- `TeamBlue.mat` → `_BaseColor` `--bp-blue`. `TeamRed.mat` → `--bp-red`. Both **non-emissive**,
  `_Smoothness` 0.12.
- `TeamBlueGlow.mat` / `TeamRedGlow.mat` → `_BaseColor` `--bp-blue` / `--bp-red`, **emission off**.
  On a bright board a team accent does not need to glow to be found, and an emissive accent below
  the 1.8 bloom threshold just looks washed out. **The accent reads by saturation and by its ink
  contour, not by light.**
- **Emissive area budget on a character: zero.** This is a tightening from Night Range's 6%. If a
  unit ever needs a glowing element, it must be justified against the seven-item bloom list in
  palette §6 and added there.

### 11.4 Outlines

`Assets/Free Outline Settings.asset` currently holds one Outline. Replace with three entries, in
order. **Linework Lite does not cap the outline list** — this is confirmed, and the single-entry
fallback previously recorded here is removed.

| # | Name | Rendering layer | Colour | Width | Occlusion | When |
|---|---|---|---|---|---|---|
| 1 | Silhouette | bit 2 (all units) | `--bp-ink` | **2.5** | off | Always. This is what keeps a matte figure off a pale deck, and on a bright board it matters more than it did on a dark one. |
| 2 | Selection | bit 4 | `--bp-blue` | **4.5** | off | The locally-selected unit during planning |
| 3 | Threat | bit 8 | `--bp-amber` | **4.5** | off | Alerted units during the dodge window, blinking at 2 Hz |

Rendering layers `SelectionOutline` (mask 4) and `ThreatOutline` (mask 8) are registered at list
indices 2 and 3. Note that mask 2's existing name, `SelectedUnit`, now reads backwards — it carries
the all-units silhouette. Renaming is not additive and may break name-based lookups; it has been
left alone deliberately.

### 11.5 Staying readable against the new environment

- The deck sits at **0.440 linear** and a Band A character at 0.10–0.24 resolves clearly *below* it.
  **The figure is now darker than its ground, where on Night Range it was lighter.** That inversion
  plus the 2.5px ink outline is the entire read, and it holds in fog, in smoke, and through an
  ability. This is the rule the four material corrections in §11.2 exist to protect: a unit lighter
  than its ground is not a cosmetic slip, it inverts the primary read of the board.
- **Contact shadow blob.** Keep one of the two existing ground quads on `Unit.prefab` (the
  1.35 × 0.04 × 1.35 child): retarget to an Unlit Transparent quad, `--bp-shadow` at **0.38** alpha,
  scale 1.2 units, y 0.13 per the Y-order contract, with a **hard edge, not a radial gradient**.
  A hard shadow is what sells a small object sitting on a table in sunlight; a soft gradient reads
  as a generic blob. The real directional shadow does most of the work; the blob guarantees ground
  contact inside smoke or another unit's shadow. **Delete the second (1.55 × 0.03) quad** — two
  overlapping blobs is why units currently look haloed.
- **Vision cones** (`FX_VisionCone.mat`, `VisionConeVisual`): **`_CompositeMode` 2 (Multiply)**,
  friendly `--bp-blue-surface`, enemy `--bp-red-surface`, `_ApexIntensity` 0.35, `viewAngle 70`,
  `groundOffset 0.06`. A multiply of a light tint darkens the floor a few percent and shifts its
  hue — the cone reads as *observed ground*, which is what it means, rather than as a light source,
  which it is not.
- **Capsule cleanup**: the base `Unit.prefab` still carries a capsule `MeshRenderer` + `MeshFilter`,
  disabled per variant. Delete both from the base prefab; keep the `CapsuleCollider`. No variant
  should be able to regress to capsule-look.

---

## 12. What this overrides

Read this before assuming something is a mistake. Every line is intentional.

1. **Night Range is retired in full.** The graphite-and-steel board, the `#05070B` void, the cyan
   `#4CC7FF` team blue, the bloom threshold of 1.0, the white-hot-core FX strategy, the "hidden is
   simply unlit" fog argument, and the dark HUD are all gone. The user stated three times that dark
   lighting made the game feel too serious.
2. **`DESIGN.md`'s "Tactical Toy Shelf" is substantially reinstated.** Night Range superseded it on
   the grounds that a serious board reads better; the user has said twice that a serious board is
   the problem. Reinstated: the warm-orange primary action (Pogo Orange, pushed to 88% saturation),
   the chunky structural bottom edge (at 4px rather than 6px), the 14px large radius exactly, and
   the card-on-table material story. **Still rejected**: the 6px sm radius (it breaks shape reading
   on 12–16px chips), and the original palette's overall saturation, which predates the saturation
   budget and would put chroma on the ground plane where it competes with signals.
3. **The shipped daylight board is the starting point, not the enemy.** Its sun angle is preserved
   exactly. Its floor family survives, desaturated from 22% to 10% chroma. What changes is the
   lighting rig's temperature separation, the post stack, the board edge, and the contact shadows.
4. **The legibility discipline from Night Range survives intact and is not up for renegotiation.**
   Opaque reading surfaces, colour always paired with shape, no text over moving models, effects
   sized to their rules footprint, team colour as a sacred contract. A bright board needs these
   more than a dark one, not less.
5. **`ArtDirection-WiringGuide.md` is stale.** It describes uGUI + TextMeshPro. All UI is UI
   Toolkit.
6. **The `FX_OpaqueCore` / `FX_DarkContour` shader proposals are declined as new files.** Both
   capabilities already ship as `_CompositeMode` and `_RimMode` on `BP_FXUnlit` and
   `BP_FXParticle`. Adding the files would duplicate working code. §9.2.
7. **The three easing tokens were unusable and are now keywords.** USS has no `cubic-bezier()`.
   This was wrong throughout Night Range.

---

## 13. Risks and cross-team coordination

| # | Risk | Owner | Mitigation |
|---|---|---|---|
| 1 | `renderPostProcessing` is false on the Game camera — none of §7.8 renders | Unity broker | Scene edit. Verify before evaluating any visual work. |
| 2 | Camera viewport rect reserves 63px top / 121px bottom for removed HUD bars | Unity broker | Set to full-bleed `(0, 0, 1, 1)`. |
| 3 | `Time.timeScale` hitstop collides with `DevInput.SetSpeed` | Gameplay | Capture and restore; skip entirely when `GameLoop.devMode`. |
| 4 | `BP_FXParticle` / `BP_FXUnlit` not in Always Included Shaders | Unity broker | Register both or they strip to magenta in a build. |
| 5 | Shotgunner's 10 pellets at 0.01 s blow the additional-light budget and the voice count | VFX + Audio | 4-light cap; one audio clip per burst. |
| 6 | Rendering layer mask 2 is named `SelectedUnit` but carries the all-units silhouette | Unity broker | Left alone deliberately; renaming risks name-based lookups. |
| 7 | Screenshot baselines and the `MapPreview.png` content hash will all diff | QA | Re-baseline **after** the visual pass lands, never during. |
| 8 | `--bp-void` removal leaves three unresolved `var()` if the palette lands without the consumer edit | UI | Both changes in one commit. Palette §11.1 lists the exact lines. |
| 9 | Four ramp-inversion consumer fixes (three recesses, one hover) | UI | Palette §11.3. None break a build; all three recesses render flat until fixed. |
| 10 | `FXPalette.cs` and the hardcoded `Color` values in `ArtSurface-Inventory.md` §8.4 still hold Night Range values | VFX | Update in the same commit as the token file, or the board and the HUD will disagree. |
| 11 | ~~Multiply-blended FX are invisible over already-dark surfaces~~ **Resolved — §9.2.1** | VFX | Resolved structurally rather than by runtime fallback: ground-plane effects may Multiply, above-ground effects use Alpha with an ink contour, and the contour inverts to paper on a dark host. The two cases that would certainly have hit it — the Area Lock beam and the target-lock laser — are re-specified Alpha. No mode switching needed. |
| 12 | Contested-hill additive is the only additive ground element | VFX | If it does not read against the pale deck, raise `_Intensity` before reaching for bloom. |
| 13 | Fogged walls keep full colour and may read as highlighted | Arena | Observe first. Do not build the rendering-layer desaturation pass pre-emptively. |
| 14 | `GraphicsSettings.defaultRenderPipeline` is null | Unity broker | Harmless today; anything reading the default pipeline gets null. |

---

## 14. Definition of done

The redesign is done when all of the following are observably true. Each is checkable, not
subjective.

**Palette and tokens**
1. `TacticalToyboxTokens.uss` declares all 178 names; no `var()` in `Assets/UI` is unresolved.
2. Zero colour literals in any `.uss` file other than the token block.
3. `FXPalette.cs` carries no Night Range value.

**Typography**
4. No body copy is set in Saira Condensed; no numeral is set in a proportional face.
5. No text is drawn in world space over a model.

**UI**
6. Every card carries a 1px `--bp-line-2` outline and a 4px `--bp-edge-dark` bottom edge.
7. Every focus ring is ink and measures at least 6.96:1 against its host.
8. Pressed states reduce the bottom edge to 2px and shift content +2px Y.
9. No recess is lighter than its container.
10. Disabled states change colour, never opacity.

**World**
11. Post-processing renders; the camera is full-bleed.
12. Nothing in the arena has non-zero `_Metallic`.
13. The board has a rail, and the backdrop is a different value from the deck.
14. Every one of the 18 wall cells reads as a fully opaque sight blocker.
15. Every unit and every cover prop casts a visible contact shadow.

**Effects**
16. Every saturated shape on the board carries a `--bp-ink` contour that meets §4.3.1: at least
    0.78 screen pixels per side if it moves, 1.56 if it holds, and never more than a quarter of the
    host's narrowest dimension.
17. MSAA is 2× or better on every quality tier above Low. Below that, §4.3.1 does not hold and no
    contour width in this document is valid.
18. No more than seven distinct emissive values exceed the 1.8 bloom threshold.
19. Bloom intensity is 0.5 or lower at every quality tier.
20. Every ability completes its three beats within 1.4 s.
21. No effect exceeds 1.35× its rules footprint at any frame.
22. Telegraphs remain readable through fog.

**Audio**
23. All 77 cues have call sites.
24. The mix hits −14 LUFS integrated, −1.0 dBTP.

**Accessibility**
25. Every team-coloured element is also shape-differentiated (chevron / bar).
26. Every "act now" audio cue has a visual twin.
27. Every text pairing in the contrast table passes its stated threshold.

---

## 15. Anti-patterns — what would make this look like an asset flip

1. **Soft drop shadows on UI.** The moment a panel gets a blurred shadow it becomes a web dashboard.
   Shadows are hard, offset, and un-blurred, or they do not exist.
2. **Glow as a substitute for silhouette.** If an effect is hard to see, it needs a darker contour
   or a bigger shape, never more bloom.
3. **Bloom above 0.5.** Non-negotiable.
4. **Gradients on UI fills.** Card stock is a flat colour. Every gradient in this interface is a
   mistake.
5. **Rounded everything.** 4 / 8 / 14 are the only radii. A 24px pill-shaped chip is not on-brand.
6. **Saturated ground.** The deck is capped at 12% chroma. A colourful floor eats every signal on
   top of it.
7. **A fourth material family.** Table, board, card. A fourth surface type breaks the story.
8. **Emissive characters.** Zero emissive area on a unit.
9. **Metallic anything in the arena.** A specular highlight on a bright board is indistinguishable
   from an effect.
10. **Text over the board.** Ever.
11. **Effects larger than their rules footprint.** It is a lie to the player and it costs matches.
12. **Green on the board.** It reads as a third team.
13. **Colour without shape.** The team contract is colour *and* shape, always.
14. **Film grain, chromatic aberration, or depth of field in gameplay.** Each one fights a thing the
    board is built on.
15. **Corpses, stains, or persistent scorch.** A dead unit's cell must read as empty immediately.
16. **AI-generated icons.** At 24px, inconsistent stroke weight is the most obvious tell there is.
17. **Soft-edged fog.** The fog boundary is crisp and cell-quantised, because the game is
    tile-crisp.
18. **A hero colour that is not orange.** One act-now signal. Adding a second means neither works.

---

## 16. Which production route to use

| Asset class | Route | Why |
|---|---|---|
| Cover kit, rail, posts, hill pad | **ProBuilder, in-editor** | All must share exact gameplay dimensions and the existing collider. Generation cannot hit a 2.7 × 2.7 × 2.0 footprint reliably. |
| Painted deck decals | **Authored quads + solid materials** | Flat colour on flat geometry. No texture pipeline needed. |
| Icons | **Hand-authored SVG on the 24×24 grid; Lucide for stock glyphs** | §6.3. Never generate. |
| Projectile meshes | **Unity primitives, scaled** | Capsules and cylinders. Zero new art. |
| VFX | **Shuriken + the six existing `BattlePlan/*` shaders** | No VFX Graph. The composite modes cover every pattern in §9.2. |
| Fonts | **Already on disk** | Nothing to source. |
| Audio | **The existing procedural toolkit** | Shipped; rebuilds in ~15 s. |
| Backdrop silhouette blocks | **ProBuilder, static-batched** | ≤ 40 blocks, one material. |
| Character meshes | **Locked. Do not touch.** | The one hard constraint on the whole redesign. |

**`generate_model`, `generate_image` and `import_model` are not used anywhere in this redesign.**
Every asset class above has a cheaper, more precise in-editor route, and all three tools require
paid external jobs plus a licence and provenance check that none of this work justifies. If a future
need arises — a hero prop, a marketing render — it needs explicit authorisation first, and providers
are currently unconfigured.

---

## 17. Changelog — Night Range → FIELD DAY

Section by section, so three mid-flight implementers can work out exactly what to redo. The palette
file carries its own changelog for token-level detail.

| § | Status | Detail |
|---|---|---|
| **1 Direction** | **Rewritten** | New codename and direction sentence. The "why production-ready" argument is inverted: darkness is now the scarce resource rather than the medium. |
| **2 References** | **Rewritten** | MapVibe re-read honestly as a dusk scene whose *lighting* is now rejected and whose *ground-graphics language* is adopted wholesale. Bright-tactics field replaces the noir field. |
| **3 Typography** | **Unchanged** | Same three faces, same eleven-step scale, same rules. §3.2's silent-fallback risk retired — assets confirmed on disk. |
| **4 Palette rules** | **Rewritten** | Three-material story is new. §4.2 saturation budget is new and is the central decision. §4.3 ink contour law is new. §4.4 team contract unchanged in substance; values moved. |
| **5 UI and HUD** | **Values rewritten, structure preserved** | Component anatomy, state list, screen list and element names all identical. What changed: the card-stock material story, the 4px structural edge, the optional hard cast shadow, and every colour. Radii 2/4/8 → 4/8/14. |
| **6 Iconography** | **Unchanged** | Same 14 icons, same one addition (`chevron-down`), same 2.0 stroke change, same ruling against generation. |
| **7 World / arena** | **Rewritten** | §7.1 measurements **unchanged**. §7.2 is new (what the shipped board gets right). Floor, cover, rail, backdrop, hill and lighting all re-specified. Post stack rewritten: ACES→Neutral, bloom 1.15/0.35→1.8/0.5. §7.9 fog is a new design. §7.10 Y-order **unchanged**. **§7.4 and §7.7 were corrected in a second revision — see §17.1.** |
| **8 Projectiles** | **Geometry unchanged, materials rewritten** | Every dimension, speed and collider note survives. Emission removed from every projectile; ink contour children added; tracers now darker than the deck. The sniper round is ink rather than white. Grenade green retired. |
| **9 VFX** | **Doctrine and timings unchanged, treatment rewritten** | §9.1 three beats, §9.3 footprint rule and table, and every per-ability timing are **identical**. §9.2 is entirely new: three patterns, the darkening flash, hard contact shadows, and the composite-mode table. Ground-plane effects moved from Additive to Multiply except the contested hill and the muzzle flash. **§9.2.1 is new in the second revision** and moves every above-ground effect to Alpha — see §17.1. |
| **10 Audio** | **Rewritten to document what shipped** | §10.2 mix hierarchy, spatialisation, voice cap and network constraints **held exactly as specified and unchanged**. Sonic identity, SFX character and emotional arc rewritten for the shipped wooden register: D major I–IV–vi–V, 84 BPM (deliberate), five named primitives, 82 clips. §10.6 gap list **closed** — all 77 cues wired. |
| **11 Characters** | **Lightly revised** | Meshes still locked. Band A luminance raised 0.05–0.14 → 0.10–0.24 because figures are now darker than their ground rather than lighter. `Char_Gunmetal` metallic 0.65 → 0.35. Character emission dropped from a 6% budget to **zero**. Outlines: three entries retained, colours changed, Linework Lite fallback **removed** (confirmed unnecessary). Contact shadow is now hard-edged. Vision cones moved to Multiply. |
| **12 Overrides** | **Rewritten** | Now records the Toy Shelf reinstatement and what of it is still rejected. |
| **13 Risks** | **Rewritten** | 14 items. Six carry over (hitstop, shader stripping, Shotgunner budget, baselines, layer naming, default pipeline); eight are new to this direction. |
| **14 Definition of done** | **Rewritten** | 27 checks, all observable. |
| **15 Anti-patterns** | **Rewritten** | 18 items. About half invert — "no soft glow on dark" becomes "no soft shadow on light". |
| **16 Production route** | **Unchanged in ruling** | Still ProBuilder, still hand-authored SVG, still no generation. Audio row added now that a toolkit exists. |
| **17 Changelog** | **New** | — |

### 17.1 Who must redo what

- **UI implementer.** Paste the new `:root`. Apply the `--bp-void` → `--bp-table` substitution at
  the three listed lines, the four ramp-inversion consumer fixes, the primary-button hover token,
  and the seven-rule `--bp-edge` swap (**not** `JoinGame.uss:94`). Nothing else in the stylesheets
  needs to change — component structure and element names are untouched.
- **Arena implementer.** Everything in §7 except §7.1 and §7.10, which are unchanged. The largest
  single item is the lighting and post stack; the highest-value item is the rail and backdrop.
  Note that the deck materials are a retarget of the existing `Map_FloorDay*`, not new assets.
- **VFX implementer.** §9.2 is the new brief; §9.1, §9.3 and every timing in §9.4 are unchanged, so
  the *scheduling* work survives and the *appearance* work does not. The composite modes you built
  are exactly the mechanism this needs — no new shader files. Also §8 materials, §11.4 outline
  colours and §11.5 contact shadow, plus `FXPalette.cs`.
- **Audio implementer.** Nothing. §10 now documents your build. The only open question is the tempo,
  and my recommendation is to leave it at 84.

### 17.2 Second revision — corrections after implementer review

Four changes, three of which are this document conceding to work that was already correct in the
tree. **Everything else in §17 stands.**

| # | Section | Change | Who acts |
|---|---|---|---|
| 1 | **§7.7 Lighting** | Sun corrected from `(50, −30, 0)` to **`(52, 90, 0)`**, with the competitive-fairness reasoning written in as the justification rather than the number alone. The warmer, brighter key and the two extra fill lights are **withdrawn**; the rig is one directional at 1.25 over the recorded gradient ambient, and warmth now comes from §7.8 grading. | Arena — **less work than before**, the shipped rig is now correct as-is |
| 2 | **§7.4 Cover, §7.6 Hill** | Cover values retuned to the builder's measured ladder. **`--bp-cover-cap` is now darker than `--bp-cover` in albedo and lighter on screen.** `--bp-ground-hill` and `--bp-cover-plate` added. New palette §1.4 explains the rendered-space transfer that forces this. | Arena, palette |
| 3 | **§9.2.1 (new)** | Ruling on multiply over dark surfaces. Ground-plane effects may Multiply; above-ground effects use Alpha with an ink contour; the contour inverts to paper on a dark host. The Area Lock beam and the target-lock laser move Multiply → Alpha. Risk 11 closes. | VFX |
| 4 | **§4.3** | Contour-inversion rule added: the contour takes whichever of ink or paper wins against its host. Deck contrast figures recomputed after the `ground-a`/`ground-b` swap. | VFX, palette |

**The process lesson, recorded because it will recur.** §7.7 previously said the sun rotation was
"preserved exactly." That was true of the scene YAML and false of the working tree, because the
arena implementer had already moved it for a reason — competitive fairness — that this document had
not considered and that outranks the aesthetic judgement it was overwriting. The same thing happened
with the cover cap, where the tree contained a *measurement* and this document contained an
*intuition*, and the intuition was wrong.

> Where the working tree has moved ahead of what was measured, check the implementer's reasoning
> before overwriting it. A value read from a shipped scene is evidence of what was, not a decision
> about what should be.

Both corrections improved the spec. The fairness constraint costs nothing aesthetically, and the
rendered-space rule caught a bug that would have made cover the brightest object on the board.

### 17.3 Third revision — validator failures and two rulings

Two build-failing validators fired on this document's own values. All fixed at source; the windows
stay strict. Palette §11.8 carries the token-level detail.

| # | Section | Change | Who acts |
|---|---|---|---|
| 1 | **§11 Characters** | Four materials failed their bands. `Char_White` 0.820 → **0.241**, `Char_CloakGray` 0.439 → **0.199**, `Char_Skin` 0.423 → **0.300**, `Char_Black` 0.017 → **0.020**. New rule: bands govern body masses ≥15% of silhouette area; smaller accents are exempt but must clear the deck by ≥0.10 linear. `Char_White` is now a role name, not a colour. | Character materials |
| 2 | **§4.3** | Contour law extended to **painted deck markings**, which had five gaps down to 1.15:1 on the hill pad. Every marking gets a 0.04 `--bp-ink` keyline; the fill keeps whatever value it wants. | Arena |
| 3 | **§7.9 Fog** | Rewritten. The old 1.38:1 / 4.07:1 figures were raw token pairs computed as if fog were opaque; composited they were 1.20 and **2.85**, the boundary missing its own threshold. `--bp-fog-edge` moves to near-ink for **3.98:1**. Alpha now unambiguously lives on `_BoundaryColor`, with `_BoundaryOpacity` pinned at 1.0. | VFX |
| 4 | **§7.9, §9.2** | The `FogOverlay` "colour change, not a shader change" conclusion was **wrong**: one colour where two were needed, and a boundary term that ran backwards, so following §7.9 literally would have produced anti-pattern 17. | VFX — already fixed |
| 5 | **§7.7** | Camera clear colour now cites `--bp-sky` and **no literal**. The parenthetical `(0.55, 0.74, 0.82) = --bp-sky` asserted an equality that was false. The palette wins. | Arena |
| 6 | **§9.2.1** | **Ruling: point flashes stay Additive.** The plane rule needed a scale-and-dwell qualifier (≤1 cell **and** ≤0.15 s), not an exception list. §6 and §8.5 are now instances of §9.2.1 rather than deviations from it. | VFX — no change to shipped behaviour |
| 7 | **Palette §1.4** | **The orientation transfer table is withdrawn** pending a measured probe. Do not author against it. The cover kit is explicitly not blocked. | Everyone — read before authoring a world value |

**Two process notes worth keeping.**

*A number's provenance outranks its tidiness.* The `1.42^2.4 = 2.32` derivation was accepted because
it was elegant, and it was a coincidence. It could not have been caught from the outside — only its
author knew what it had actually computed. That is the argument for publishing the method rather
than the constant, and it is why Palette §1.4 is now a derivation someone can re-run.

*Quoting a number back is not corroboration.* This document's `1.000 / 0.705 / 0.237` was read as an
independent check on `ArenaBuilder.cs` when it was a transcription of it. An invented cross-check is
worse than one unverified number, because it stops anyone looking.

### 17.4 Fourth revision — the contour width ruling

§4.3's "at least 2 screen pixels" and §8.1's own dimensions could not both be satisfied. The
implementer was right and the rule was wrong. **The 2px minimum is withdrawn from world space.**

| # | Section | Change | Who acts |
|---|---|---|---|
| 1 | **§4.3, §4.3.1 (new)** | The contour width is now a **screen-space coverage requirement** with three parts: a floor of **0.78px per side moving / 1.56px static**, a cap of **W/4** on the host's narrowest dimension, and an explicit "cannot carry a contour" case when the cap falls below the floor. §4.3.1 publishes the camera-to-pixel scale formula, the coverage→contrast table, and the per-tier sampling table. | VFX, arena, palette |
| 2 | **§4.3.1** | **The law now states that it assumes anti-aliasing.** Sub-pixel contours are partial-coverage edges and do not exist at one sample per pixel. Mandatory: `w · renderScale · √MSAA ≥ 1`; target 2. Medium at MSAA 1 fails at 0.78 and the planned raise to 2× brings it to 1.10 — valid, not comfortable. Low (render scale 0.37) is out of scope by construction. | Pipeline — the Medium raise is required, not optional |
| 3 | **§8.1–§8.4** | Shell constant **0.020 → 0.021 radial**, axial 0.030 unchanged. Contours become 0.152 / 0.132 / 0.382 / 0.522. The grenade's and canister's contours are documented here for the first time; both take the shell off the **widest drawn profile**, and it is the grenade that makes that a rule rather than a habit — sized off its 0.30 body the contour lands on its own 0.34 cap ring and hazard band, z-fighting both and enclosing neither. | VFX — one constant, four `localScale` values |
| 4 | **§8.2** | "Contour child at **1.4× per §8.1**" **deleted.** It lands within a rounding step on the 0.09 diameter and gives 1.90 on a 1.36 lance. A ratio does not survive a 15:1 aspect. | VFX |
| 5 | **§4.3, §4.3.1** | Deck keyline **0.04 → 0.043**, on the static floor. The camera does not move during a match, so a marking is stuck at one sub-pixel phase for the whole game and must survive the worst one. | Arena — five markings |
| 6 | **§4.3** | Three stale ratios corrected to the palette's current table: red **2.05 → 1.98**, amber **1.21 → 1.17**, blue **3.13 → 3.02** (and 2.73 against cell B, previously only "below the line"). Ink-vs-deck **8.45 → 8.17**. All four were the pre-`#A8B3B8` values that palette §11.8 had already superseded; §4.3 was simply never resynced. | Palette — already correct there |
| 7 | **§1.1, §9.2, §9.4, §14, Palette §7, Palette §13** | Six downstream restatements of "2px" retired. Palette §13 no longer lists "minimum FX contour width" as a use of `--bp-bw` — that line was the actual provenance of the error. §14 gains a check on MSAA and renumbers to 27. | Everyone |
| 8 | **§8.2** | ⚠ **Open, unmeasured.** "The sniper round is pure ink" is probably false in rendered space: the body is `URP/Lit` and lifts off ink under the key, while its unlit shell does not. Reasoned, not read off a frame. Bundle it into the palette §1.4 probe. | Nobody yet — do not author against it |
| 9 | **§7.9 Fog** | ⚠ **Open gap, newly visible.** The fog boundary is a static contour and now has a floor of 1.56px / 0.043, but §7.9 never specified a drawn width — only `_EdgeSoftness 0.06`, which is falloff, not width. Its 3.98:1 and 3.39:1 are full-coverage figures and are unverified until someone measures what the shader actually draws. | VFX — measure, then either state the width or fix it |

**Why the earlier draft was wrong, which is the useful half.** 2px is `--bp-bw`, the UI
emphasised-border token. Nothing carried it into the arena deliberately; §4.3 was written next to
§5 and the number came across with the vocabulary. In UI it is 0.7% of a card and invisible as a
constraint. In the arena it is applied to a bullet 4.1 pixels wide, where a 2px rim per side makes
the object **59% ink by area** — so the rule destroys §4.4's colour contract in the course of
obeying §4.3, which is how you can tell a rule has left the range it was reasoned for. Same shape of
error as reading §8.1's shell offset as a 1.4× ratio: followed faithfully, one step past the edge.

The clearest evidence that nobody was ever really applying it: **not one world contour in this
document has ever met it.** The 0.04 deck keyline is 1.5px. Every projectile shell was 0.75px. The
minimum was violated in the same section that declared it, three paragraphs apart, through three
revisions and two rounds of validators, because a rule stated in the wrong units cannot be checked
by looking at the values it governs.

The replacement is stated in the units the constraint actually lives in. **A contour's width is a
property of the display, not of its host** — 0.78px is equally visible on a 4px bullet and a 99px
cell, and nothing about the larger object asks for more ink. Once that is said plainly, the size
clause is not a special case for small shapes; it is the general rule, and the cap is the only part
that ever needed to know how big the host is.

**A third process note, in the same family as §17.3's two.**

*A rule stated in the wrong units is unfalsifiable.* "At least 2 screen pixels" governs world
geometry authored in world units, so no one could check it without doing camera trigonometry, and
so no one did — for three revisions. The fix was not a better number, it was publishing
`s = 895 / (24.4 − y)` next to it, which turns the rule into something a validator can run and an
artist can lose an argument to. **Before writing a threshold, check that the thing it constrains is
measured in the same units the threshold is written in.** Where it is not, ship the conversion in
the same paragraph, or expect the rule to be quietly ignored by everyone including its author.
