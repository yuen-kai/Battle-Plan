---
name: Battle Plan
description: Friendly tactical setup presented as a shelf of animated tabletop pieces
colors:
  toybox-ink: "#1B1927"
  playmat: "#2D2E43"
  playmat-raised: "#3A3B52"
  sticker-cream: "#F5ECD8"
  pogo-orange: "#F2A54A"
  toy-teal: "#55B29D"
  sky-info: "#84D3E6"
  tomato: "#E9685D"
  muted-copy: "#BDB6C9"
  title-periwinkle: "#7698BD"
  roster-mauve: "#AA8BAA"
  join-clay: "#B07F72"
typography:
  display:
    fontFamily: "Rubik"
    fontSize: "38px"
    fontWeight: 700
    lineHeight: 1.05
    letterSpacing: "-0.5px"
  title:
    fontFamily: "Rubik"
    fontSize: "20px"
    fontWeight: 700
    lineHeight: 1.15
  body:
    fontFamily: "Rubik"
    fontSize: "15px"
    fontWeight: 400
    lineHeight: 1.35
  label:
    fontFamily: "Rubik"
    fontSize: "13px"
    fontWeight: 700
    lineHeight: 1.2
  mono:
    fontFamily: "Cascadia Code"
    fontSize: "20px"
    fontWeight: 700
    lineHeight: 1.2
rounded:
  sm: "6px"
  md: "10px"
  lg: "14px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "20px"
  xl: "32px"
components:
  button-primary:
    backgroundColor: "{colors.pogo-orange}"
    textColor: "{colors.toybox-ink}"
    typography: "{typography.label}"
    rounded: "{rounded.md}"
    padding: "10px 16px 12px"
    height: "52px"
  button-secondary:
    backgroundColor: "{colors.playmat-raised}"
    textColor: "{colors.sticker-cream}"
    typography: "{typography.label}"
    rounded: "{rounded.md}"
    padding: "10px 16px 12px"
    height: "52px"
  button-selected:
    backgroundColor: "{colors.toy-teal}"
    textColor: "{colors.toybox-ink}"
    typography: "{typography.label}"
    rounded: "{rounded.md}"
    padding: "10px 16px 12px"
    height: "52px"
  toy-panel:
    backgroundColor: "{colors.playmat}"
    textColor: "{colors.sticker-cream}"
    rounded: "{rounded.lg}"
    padding: "24px"
  toy-readout:
    backgroundColor: "{colors.playmat}"
    textColor: "{colors.sticker-cream}"
    rounded: "{rounded.md}"
    padding: "12px 16px"
  status-chip:
    backgroundColor: "{colors.playmat-raised}"
    textColor: "{colors.sticker-cream}"
    rounded: "{rounded.sm}"
    height: "28px"
---

# Design System: Battle Plan

## Overview

**Creative North Star: "The Tactical Toy Shelf"**

Battle Plan should feel like opening a well-loved strategy game and finding its pieces already acting out the next match. Rounded low-poly characters, off-axis poses, and occasional goofy animation supply the charm. The interface behaves like the printed play surface around them: solid, concise, and easy to operate.

The world is playful without becoming childish. It refuses both realistic military simulation and generic neon command-terminal styling. Tactical Toybox owns the menu screens, screen-like overlays, and top gameplay readouts. Unit and ability cards remain a deliberately isolated gameplay subsystem until their interaction model is redesigned.

**Key Characteristics:**
- Real in-world character models behind reserved transparent UI regions.
- Chunky, tactile controls with firm outlines and short offset depth.
- Mixed-case, plain-language labels with very little supporting copy.
- Warm orange decisions, teal confirmation, and tomato error states.
- One animated character moment per screen, never motion everywhere.

## Colors

Use the Playmat family for solid operating surfaces, Sticker Cream for readable text and paper pieces, Pogo Orange for the primary decision, Toy Teal for selected or confirmed state, Sky Info for neutral information, and Tomato for danger or invalid state. Title Periwinkle, Roster Mauve, and Join Clay are scene-stage backdrops chosen for silhouette contrast; they do not replace semantic UI colors. Inactive controls remain visibly neutral rather than desaturated into illegibility.

**The Character Color Rule.** Saturated colors identify a decision or state. They do not decorate empty space.

## Typography

Use Rubik as the sole interface family, with Regular for body text and Bold for controls and headings. Its softened geometry should echo the rounded models without imitating children’s lettering. Keep Cascadia Code only for relay codes, counts, timers, and compact technical values.

Use mixed case by default. Uppercase is reserved for very short status tokens. Headings should be clearly larger than labels without becoming cinematic title cards.

**The Plain Words Rule.** If a label already names the action, do not add a sentence that repeats it.

## Layout

Wide screens divide into one opaque operating surface and one transparent character stage. Text and controls never sit directly over moving models. The existing `compact`, `narrow`, `phone`, and `short` classes convert these two zones into a vertical flow; model stages crop to a shallow mascot band or disappear before controls become cramped.

Spacing follows the documented five-step rhythm. Primary targets use the 52px component height and increase to 56px at phone width.

## Elevation & Depth

Depth is structural and toy-like. Interactive pieces use a 6px dark bottom edge rather than broad ambient shadows. Pressed controls visually settle into a 3px edge. World models provide real depth, so UI plates remain solid and restrained.

**The One Stage Rule.** Each screen has one depth-bearing character stage. Nested floating panels do not compete with it.

## Shapes

Controls use the medium radius, strong silhouettes, and a 5px Sky Info focus frame on the top and side edges. The thicker frame makes focus distinct from cream selected borders without replacing Toy Teal selection. Large plates use the large radius. Pills are limited to small status tokens. The recurring Plan Path connects round waypoint markers to a square destination only where it communicates the current route or next action.

## Components

### Buttons
- **Shape:** Chunky rectangular controls using the medium radius and a firm bottom edge.
- **Primary:** Pogo Orange with Toybox Ink text; one primary action per decision group.
- **Hover / Focus:** Cream border; hover lightens the surface and press collapses the bottom edge.
- **Selected:** Toy Teal with dark text so selection remains distinct from the primary action.

### Cards / Containers
- **Corner Style:** Large radius with a dark keyline.
- **Background:** Opaque Playmat for all reading surfaces; Sticker Cream is reserved for a single leading plate.
- **Shadow Strategy:** No soft card shadows. Panels use a structural dark bottom edge.
- **Internal Padding:** The large spacing step by default, reduced on compact layouts.

### Inputs / Fields
- **Style:** Toybox Ink fill, reserved 3px border, medium radius, and large readable values.
- **Focus:** Border changes to Sticker Cream without changing width.
- **Error / Disabled:** Tomato copy names recovery; disabled controls keep readable Muted Copy and lose their deep bottom edge.

### Navigation
- Use direct verbs, custom 24px monochrome icons, and visible selected tabs. Back controls name the destination when space allows.

### Plan Path

The signature path uses an orange circular start, a short straight segment, and a square destination. It appears once per wide screen as a compact route cue, never as a decorative background pattern.

## Implementation Contract

`TacticalToyboxTokens.uss` is the implementation source for semantic colors, spacing, shape, control targets, depth, type sizes, and timing. `TacticalToybox.uss` imports those tokens and provides the component library. Screen styles define composition and staging only.

Attach `.toybox-ui` to the smallest visual subtree that should inherit the component library. Menu screens apply it to their screen root. Gameplay applies it only to the top HUD and screen-like overlays so the unit and ability cards keep their existing interaction contract.

Canonical components:
- `.button` with `--primary`, `--selected`, `--flat`, and `--danger` variants.
- `.toy-panel` with `--cream` and `--compact` variants.
- `.toy-readout` with `__label` and `__value` parts.
- `.toy-chip` with `--selected`, `--info`, and `--danger` variants.
- `.text-field`, `.toggle`, `.modal`, `.status-line`, and `.plan-path`.

Every interactive component defines default, hover, active, focus, disabled, and selected states where applicable. Sky Info is always focus. Toy Teal is always selection or confirmation. Pogo Orange is always the primary decision. Tomato is always danger or invalid state.

Load order is legacy reset, Tactical Toybox component library, then the screen stylesheet. A screen stylesheet may place and size a component, but it must not redefine semantic colors or core interaction states.

## Do's and Don'ts

### Do:
- **Do** use the project’s actual rounded models and existing humorous poses.
- **Do** reserve opaque reading surfaces around every label, input, and status message.
- **Do** pair color with an icon, outline, check, count, or text state.
- **Do** let Pogo Rider carry the strongest recurring motion.

### Don't:
- **Don't** use realistic soldiers, photographic portraits, or military-simulation severity.
- **Don't** use glass, neon edge glows, purple gradients, soft SaaS cards, or fake terminal jargon.
- **Don't** activate gameplay prefabs, networking components, colliders, or authoritative scripts for menu decoration.
- **Don't** let continuous animation overlap controls or become required feedback.
