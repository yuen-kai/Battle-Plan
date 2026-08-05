---
name: Battle Plan
description: Tactical interface built from the board's own materials — deck paper, wall ink, the overlay blue for selection
colors:
  ground: "#CED6D9"
  surface: "#E4E9EB"
  surface-raised: "#F1F4F5"
  surface-hover: "#A8B3B8"
  surface-pressed: "#9FAAB0"
  inset: "#BFC8CB"
  warm-plate: "#E8E2D4"
  panel: "rgba(228, 233, 235, 0.90)"
  vignette-falloff: "#6F6455"
  edge: "rgba(110, 122, 128, 0.42)"
  edge-strong: "rgba(70, 82, 90, 0.68)"
  text: "#1F282E"
  copy: "#3E4C54"
  muted-copy: "#6A7880"
  accent-orange: "#F18F01"
  select-plate: "#3085EE"
  select-ink: "#FFFFFF"
  danger: "#C72E2A"
  team-friendly: "#2F43F5"
  team-enemy: "#F83547"
  hud-surface: "#1E272C"
  hud-warm: "#3A3529"
  hud-edge: "rgba(173, 199, 209, 0.26)"
  hud-text: "#EDF3F5"
sourceMaterials:
  Map_DeckA: "#A8B3B8"
  Map_DeckB: "#9FAAB0"
  Map_GridLine: "#6E7A80"
  Map_Cover: "#46525A"
  Map_CoverPlate: "#E8E2D4"
  Map_Void: "#5E5346"
  Map_WallCap: "#ADC7D1"
  MoveOverlayCell: "#3085EE"
typography:
  display:
    fontFamily: "OswaldCaps"
    fontSize: "46px"
    fontWeight: 400
    lineHeight: 1.05
    letterSpacing: "2px"
  title:
    fontFamily: "OswaldCaps"
    fontSize: "21px"
    fontWeight: 400
    lineHeight: 1.15
    letterSpacing: "1.5px"
  label:
    fontFamily: "OswaldCaps"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.2
    letterSpacing: "1.6px"
  body:
    fontFamily: "Jost"
    fontSize: "15px"
    fontWeight: 400
    lineHeight: 1.35
    letterSpacing: "0.2px"
  figures:
    fontFamily: "OswaldCaps"
    fontSize: "22px"
    fontWeight: 400
    lineHeight: 1.2
    letterSpacing: "1.6px"
rounded:
  sm: "0"
  md: "0"
  lg: "0"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "20px"
  xl: "32px"
components:
  button:
    backgroundColor: "{colors.surface-raised}"
    textColor: "{colors.text}"
    hoverBackgroundColor: "{colors.select-plate}"
    hoverTextColor: "{colors.select-ink}"
    typography: "{typography.label}"
    rounded: "{rounded.md}"
    padding: "12px 18px"
    height: "52px"
  button-primary:
    backgroundColor: "{colors.accent-orange}"
    textColor: "#180F02"
    typography: "{typography.label}"
    rounded: "{rounded.md}"
    padding: "12px 18px"
    height: "52px"
  button-selected:
    backgroundColor: "{colors.select-plate}"
    textColor: "{colors.select-ink}"
    leadingRule: "3px {colors.accent-orange}"
    note: "The chosen thing inverts. On paper that is a saturated blue plate with white ink."
    typography: "{typography.label}"
    rounded: "{rounded.md}"
    height: "52px"
  toy-panel:
    backgroundColor: "{colors.panel}"
    borderColor: "{colors.edge}"
    topBorderColor: "{colors.edge-strong}"
    rounded: "{rounded.lg}"
    padding: "24px"
  toy-readout:
    backgroundColor: "{colors.panel}"
    borderColor: "{colors.edge}"
    rounded: "{rounded.md}"
    padding: "12px 18px"
  status-chip:
    backgroundColor: "#121A24"
    borderColor: "{colors.edge}"
    textColor: "{colors.text}"
    rounded: "{rounded.sm}"
    height: "26px"
  modal:
    backgroundColor: "{colors.surface}"
    topRule: "3px {colors.accent-orange}"
    rounded: "{rounded.lg}"
    padding: "28px"
---

# Design System: Battle Plan

## Overview

**Creative North Star: "Clean Field Command"**

**The interface is made of the board.** Every colour in it is read out of the arena's own materials
rather than chosen to sit beside them: panels are the deck lightened, hairlines are the grid line,
ink is the cover walls, portrait plates are the bone caps on top of those walls, the frame falls off
to the table the board stands on, and selection is the same blue the game already paints a reachable
cell with. The `sourceMaterials` block in the front matter is the list, and it is checkable — those
are asset names in `Assets/Materials`.

That last one carries the most weight. The board already had a colour meaning "you can act here" and
the interface already needed one meaning "this is chosen". Making them the same colour means a
player learns the language once.

**There are two halves to the palette, and the split is deliberate.** The front end is built from
the deck; the in-match HUD is built from the walls. The reason is the board itself: the deck is
`#A8B3B8`, the two HUD bands are opaque by necessity because the camera is shrunk to the gap between
them, and bands made of the deck against the deck would erase the boundary the whole layout depends
on. The map already contained the answer — it has a dark end. The HUD re-declares the same tokens
from `Map_Cover`, `Map_WallCap` and `Map_Void` at the top of `GameHUD.uss`; every rule in that file
is written once and works in either half. Delete that block and the HUD moves onto the deck palette
— nothing else needs to change.

**The reference is Overwatch 1, and the borrowing is structural, not literal.** What was taken is
the *system*: cool translucent chrome over a lit stage, structure drawn in light rather than in
boxes, a single warm accent reserved for the objective, an inverted plate for the chosen thing, and
condensed capitals for chrome with a geometric sans for prose. That system is the point, and it is
what every rule below implements.

What was deliberately **not** taken is the costume. The accent is Battle Plan's own Pogo Orange,
not the reference's brand orange. The display face is a cut of Oswald, not an impression of Big
Noodle Too. The health ramp keeps this game's green-amber-red rather than adopting a flat white
bar. The HUD keeps its own silhouette — two reserved bands and a dock of crew cards — because this
is a simultaneous-turn tactics game where five units are planned at once, not a shooter with one
hero and an ultimate meter. If a change would only make sense as an imitation of a specific
Overwatch screen, it does not belong here.

The world behind it stays the project's own: rounded low-poly characters with goofy poses. That
contrast is deliberate — a warm, readable cast staged inside cool, disciplined chrome.

**Key Characteristics:**
- Translucent near-black panels over lit 3D stages, never opaque grey boxes.
- Square corners everywhere. Where a shape is not square it is a circle, a hexagon, or a trapezoid
  cut at an angle.
- Structure drawn in light: one-pixel white hairlines at low alpha, brighter along the top edge.
- One warm accent, the wordmark orange, for the objective and the next action.
- The chosen thing inverts to a white plate with dark ink.
- Condensed capitals for chrome; a geometric sans for anything that is a sentence.

## Colors

Front-end chrome is the deck walked up toward white until type can sit on it, translucent, so the
menus are a tint over the game rather than plates on a background. `--toy-surface-hover` is
`Map_DeckA` unmodified and `--toy-surface-pressed` is `Map_DeckB`: touch a control and it becomes
the floor exactly. The frame falls off to `Map_Void`, the table. Elevation is lightness alone;
nothing casts a shadow. The match HUD inverts all of this and is described in the overview.

**The One Warm Thing Rule.** Pogo Orange `#F18F01` is the only warm hue in the chrome — which mirrors
the board, where the bone plates and the table are the only warm things on an otherwise cool
surface. It marks the objective, the next action, and state, and is never a large fill except on the
primary button. As *text* on the deck it drops to `#995700`; the fill value only reaches 2.2:1 there.

**The Inversion Rule.** Selection and hover do not tint — they invert. A chosen row, tab, tile or
slot becomes `#3085EE`, the board's reachable-cell blue, with white ink, plus an orange rule on its
leading edge to distinguish "chosen" from "pointed at". This is the loudest state the palette has
and it is spent only on the thing the player has actually picked. Anything that inverts must flip
three properties together: its background, its label colour, and its icon tint. On the HUD's wall
palette the same rule produces a pale `Map_WallCap` plate with dark ink — the direction follows the
chrome, the behaviour does not change.

**The Structure-In-Line Rule.** Borders are `Map_GridLine` at low alpha, not grey literals. The board
draws a cell boundary with exactly this colour, so a panel edge and a grid edge are the same mark at
two scales. Over a translucent panel a fixed grey changes meaning with whatever the world is doing
behind it, while a wash of the grid line always darkens what it lies on. The top edge of a plate is
stronger than the other three, which is the entire depth model.

**The Halo Rule.** Text sitting on the 3D stage with no plate under it takes a halo the same colour
as the paper. Display sizes only: a bloom the colour of the page does not sit behind label-sized
text, it eats it.

**The Team Pair.** Friendly `#2F43F5` and enemy `#F83547` mirror `TeamPalette.cs`, which also
paints unit bodies and tracers on the board. They are viewer-relative — the local crew is always
the friendly pair on both screens — and they are not the selection colour. Selection is the lighter
overlay blue `#3085EE`; the team blue is a deep ultramarine, and the two are far enough apart in
value that a selected card never reads as a friendly one. Changing either team value means changing
both files in the same edit.

**Health.** A green-amber-red ramp, so the bar reports condition and not only quantity. Thresholds
and colours live in `TeamPalette` and are mirrored as `--toy-health-*`. The bar is drawn in fixed
segments rather than as one continuous fill — that idea is borrowed, the colours are not.

## Typography

Two families, split by job.

**OswaldCaps** is the display face: condensed, and cut so that its lowercase codepoints render the
uppercase glyphs. Every heading, control, label, chip, counter and timer is set in it, and it
capitalises them without a single `ToUpper()` in a controller or a shouted string in a UXML. Always
letterspace it — condensed capitals clot into a grey mass without tracking, and the tokens
`--toy-track-display`, `--toy-track-title` and `--toy-track-label` exist so nobody has to guess.

**Jost** is the reading face, standing in for the Futura the reference uses. It carries every
sentence: descriptions, help text, status lines, tutorial copy. It is the `:root` default precisely
so prose can never be caught by the caps face by accident — the display face must always be opted
into.

Never set a paragraph in the display face. A wall of condensed capitals is not readable and no
shipping game ships one.

**The Plain Words Rule.** If a label already names the action, do not add a sentence that repeats it.

## Layout

Wide screens divide into one chrome surface and one character stage. The chrome hugs an edge — a
full-height left rail, a bottom dock, a top band — and dissolves into the stage through a scrim
rather than ending at a hard box edge. Text and controls never sit directly over moving models
without a scrim or vignette under them.

The existing `compact`, `narrow`, `phone` and `short` classes convert the two zones into a vertical
flow; character stages give up their space before controls become cramped.

Spacing follows the five-step rhythm. Primary targets use the 52px component height, rising to 56px
at phone width.

## Elevation & Depth

Flat. One hairline everywhere, a brighter top edge, a two-pixel focus ring, no offset bottom edge
and no ambient shadow. The only real depth in a frame belongs to the 3D stage behind the chrome.

**The One Stage Rule.** Each screen has one depth-bearing character stage. Nested floating panels do
not compete with it.

## Shapes

Every radius token is zero. The interface has no rounded rectangle in it.

Where a shape is not square:
- **Circles** are ability slots. A round frame, one glyph, a number over it while it recharges.
- **Hexagons** mark state — the pip on the objective banner, status glyphs.
- **Trapezoids** are banners and tabs. Flank a rectangle with `.toy-slant--left` and
  `.toy-slant--right` and it narrows downward; two fixed-width caps rather than one stretched
  image, because a stretched bevel changes its angle with the width of the element.

Section headings wear a three-pixel orange tick on their leading edge. That tick, repeated, is what
turns a stack of sections into a briefing instead of a form.

## Components

### Buttons
- **Shape:** Square-cornered plates, one-pixel hairline, condensed caps with tracking.
- **Hover:** Inverts to the white plate with dark ink; the icon tint flips with the label.
- **Primary:** Solid accent orange with near-black ink; one per decision group.
- **Selected:** The white plate plus a three-pixel orange leading rule.
- **Danger:** Keeps its red fill through hover — the invert means "this is the one", and a
  destructive action is not.
- **Focus:** A two-pixel white ring on dark plates, dark ink on light ones.

### Cards / Containers
- **Corner Style:** Square, one-pixel hairline, brighter along the top.
- **Background:** Translucent panel; the stage is meant to be visible through it.
- **Shadow Strategy:** None.
- **Accent:** A three-pixel rule on one edge when the container is keyed or active.

### Inputs / Fields
- **Style:** Near-black inset well, hairline, a three-pixel leading rule, values set large in the
  display face.
- **Focus:** White ring, orange leading rule.
- **Toggles:** A square well that fills with the accent when it is on. It does not slide and it does
  not tick.
- **Sliders:** A shallow inset channel with a rectangular grip; the grip lights orange on hover and
  focus.

### Cinematic edges
`.toy-vignette`, `.toy-scrim-left` and `.toy-scrim-bottom` are absolutely-positioned masks that
settle the stage into the chrome. All three are **white images with their shape in alpha**, tinted
at the point of use through `--toy-scrim-tint` and `--toy-vignette-tint` — which is the hook that
lets one set of files serve both halves of the palette, and which is why the vignette can fall off
to the table on the front end and to the wall shadow in a match. A dark master could only ever be
darkened. `.toy-hatch`
is a diagonal texture at five percent, used to keep a large flat band from reading as an unpainted
letterbox. All four are decoration and always carry `picking-mode="Ignore"`.

### Plan Path
The signature route glyph — orange start pip, short run, stop — appears once per wide screen as a
compact cue, never as a decorative background pattern.

## Implementation Contract

`TacticalToyboxTokens.uss` is the implementation source for semantic colours, spacing, shape,
control targets, depth, type sizes, tracking and timing. `TacticalToybox.uss` imports those tokens
and provides the component library. Screen styles define composition and staging only.

The file names predate this theme and were kept deliberately: renaming them would touch every
`@import`, every `<ui:Style>` and every `.meta` in the UI folder for no visual gain. `.toybox-ui` is
the component-library scope, not a description of the look.

Attach `.toybox-ui` to the smallest visual subtree that should inherit the component library. Menu
screens apply it to their screen root. Gameplay applies it only to the top HUD and screen-like
overlays, so the unit and ability cards keep their existing interaction contract — which also means
those cards inherit their type from `:root` in `BattlePlan.uss` and must ask for the display face
explicitly.

USS cannot put a `var()` inside a `url()`, so the two font paths are written out at every call site.
There is no shorthand and there is no way around it.

Canonical components:
- `.button` with `--primary`, `--selected`, `--flat`, and `--danger` variants.
- `.toy-panel` with `--compact` and `--keyed` variants.
- `.toy-readout` with `__label` and `__value` parts.
- `.toy-chip` with `--selected`, `--info`, and `--danger` variants.
- `.text-field`, `.toggle`, `.toy-slider`, `.modal`, `.status-line`, `.section-label`, `.plan-path`.
- Decoration: `.toy-vignette`, `.toy-scrim-left`, `.toy-scrim-bottom`, `.toy-hatch`, `.toy-slant`.

Every interactive component defines default, hover, active, focus, disabled and selected states
where applicable. `--toy-focus` is always focus. `--toy-select` is always selection. Orange is
always the objective or the next decision. Red is always danger. Those are role names on purpose:
the repaint from dark chrome to paper changed every one of their values and moved no call site.

Load order is legacy reset, Tactical Toybox component library, shared Settings rows where used, then
the screen stylesheet. A screen stylesheet may place and size a component, but it must not redefine
semantic colours or core interaction states.

## Verification

`Battle Plan/Capture UI Theme Screenshots` renders every screen to `Captures/OverwatchPass/` without
entering Play mode. It stages the content a live match or a half-finished roster pick would have
produced, because the controllers that build that content do not run in edit mode. Use it before and
after any change to this system.

## Do's and Don'ts

### Do:
- **Do** let the lit stage show through the chrome.
- **Do** letterspace every piece of display type.
- **Do** invert on hover and add the orange rule on select.
- **Do** pair colour with an icon, outline, check, count, or text state.
- **Do** scrim or vignette anything that puts white text over a bright stage.

### Don't:
- **Don't** round a corner. Not once.
- **Don't** hard-code a colour in a screen sheet. The two palettes only stay in step because every
  rule reads a token; a literal is a rule that works in one theme and breaks in the other.
- **Don't** set a sentence in the display face, or uppercase a string by hand — the font does it.
- **Don't** spend orange on decoration; it is the objective and the next action only.
- **Don't** use glass, neon edge glows, gradients on plates, or soft SaaS cards.
- **Don't** add film grain or other photographic texture through post-processing. UI Toolkit
  composites after post, so it can only ever reach the 3D half of the frame and splits one screen
  into two media.
- **Don't** activate gameplay prefabs, networking components, colliders, or authoritative scripts
  for menu decoration.
