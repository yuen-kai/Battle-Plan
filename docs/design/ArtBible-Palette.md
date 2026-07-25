# Battle Plan — Palette & Token Contract

**Direction: FIELD DAY.** Companion to `docs/design/ArtDirection.md`. That document explains why;
this one is the numbers. If the two disagree, this file wins.

This is the single source of truth for every colour, spacing, radius, size, type step and motion
constant in the project. Do not author a colour anywhere else — not in a `.uss` file, not in a
`.mat`, not as a `new Color(...)` in C#. If you need a value that is not here, add it here first.

**Supersedes the Night Range palette entirely.** Every colour value in this file changed. The token
*names* did not, with one deliberate exception listed in §11.

---

## 0. Which number goes where

Unity stores colour in three different spaces depending on where it lands, and getting this wrong is
the single most common way a palette rewrite looks broken on day one.

| Destination | Space | Which column to use |
|---|---|---|
| USS (`.uss`, UI Toolkit) | sRGB | The **USS value** column, verbatim |
| Material LDR colour (`_BaseColor`, `_Color`, `_FogColor`, `_ConeColor`, `_GlowColor` when not `[HDR]`) | sRGB | The **sRGB 0–1** column. This matches what the Inspector colour picker shows. |
| Material `[HDR]` colour (`_EmissionColor`, `_CoreColor`) | **Linear** | §6. Never paste an sRGB value into an HDR field. |
| C# `new Color(r,g,b,a)` assigned to a material property | **Linear** | The **Linear RGBA** column |
| C# `Color` used for UI Toolkit inline styles, `VisualElement.style.*` | sRGB | The **sRGB 0–1** column |
| Camera `backgroundColor`, `RenderSettings.ambient*`, `Light.color` in the scene YAML | sRGB | The **sRGB 0–1** column |
| Particle System `startColor` / Color-over-Lifetime gradient | sRGB in the editor | The **sRGB 0–1** column |

Alpha is never gamma-corrected. It is the same number everywhere.

---

## 1. Surface & environment ramp

### 1.1 The two anchors

Everything in FIELD DAY is positioned between these two.

| Token (= USS custom property) | Hex | USS value | sRGB 0–1 | Linear RGBA | Where it goes |
|---|---|---|---|---|---|
| `--bp-ink` | `#151A20` | `rgb(21, 26, 32)` | 0.0824, 0.1020, 0.1255 | 0.0075, 0.0103, 0.0144, 1 | Darkest value in the game. Body text, every drawn contour, scrim base, FX dark cores, the 4 px card edge, outline colour. |
| `--bp-table` | `#5E5346` | `rgb(94, 83, 70)` | 0.3686, 0.3255, 0.2745 | 0.1119, 0.0865, 0.0612, 1 | The tabletop the whole game sits on. Full-screen menu backgrounds, the deployment overlay, the join stage bed. Replaces --bp-void. |

### 1.2 UI surface ramp — **inverted: `--bp-deck-0` is the LIGHTEST**

This is the most important structural change in the rewrite and the one most likely to be misread.
On Night Range the ramp climbed from black to pale metal. On FIELD DAY it descends from paper to
ink. **The numbering is unchanged; the direction of travel is reversed.** Anywhere the old system
stepped *up* a rung to add weight, the new system steps *down*.

The rule for implementers is: **`0` is the extreme you brighten toward, `6` is the extreme you
darken toward.** Hover, press and emphasis all move toward `6`.

| Token (= USS custom property) | Hex | USS value | sRGB 0–1 | Linear RGBA | Where it goes |
|---|---|---|---|---|---|
| `--bp-deck-0` | `#FCF9F3` | `rgb(252, 249, 243)` | 0.9882, 0.9765, 0.9529 | 0.9734, 0.9473, 0.8963, 1 | Paper. Wells and plates: text-field fill, portrait plate, cooldown badge, scroller track, unit-card art area, dropdown input. |
| `--bp-deck-1` | `#F2EDE2` | `rgb(242, 237, 226)` | 0.9490, 0.9294, 0.8863 | 0.8879, 0.8469, 0.7605, 1 | Card stock. The default panel fill and every floating HUD widget body. |
| `--bp-deck-2` | `#E4DDCE` | `rgb(228, 221, 206)` | 0.8941, 0.8667, 0.8078 | 0.7758, 0.7231, 0.6172, 1 | Tray. Raised or grouped sub-surfaces, the HUD dock cluster. |
| `--bp-deck-3` | `#D2C9B6` | `rgb(210, 201, 182)` | 0.8235, 0.7882, 0.7137 | 0.6445, 0.5841, 0.4678, 1 | Hover. |
| `--bp-deck-4` | `#B6AB93` | `rgb(182, 171, 147)` | 0.7137, 0.6706, 0.5765 | 0.4678, 0.4072, 0.2918, 1 | Pressed, and the disabled surface. |
| `--bp-deck-5` | `#877E6C` | `rgb(135, 126, 108)` | 0.5294, 0.4941, 0.4235 | 0.2423, 0.2086, 0.1500, 1 | Strong divider, slider dragger hover, muted plate. |
| `--bp-deck-6` | `#3D372D` | `rgb(61, 55, 45)` | 0.2392, 0.2157, 0.1765 | 0.0467, 0.0382, 0.0262, 1 | Darkest UI surface. Reserved for an inverted chip or a dark tray on card stock. |

### 1.3 World / arena

The arena no longer borrows the UI ramp. Night Range shared one ramp between the HUD and the board
so they read as one designed object; FIELD DAY deliberately does not, because the whole point of the
direction is that the HUD is a **component sitting on** the board, made of a different material.
Three materials, three families:

- **Table** (`--bp-table`) — warm, mid, matte. What the game sits on. Menus and full-screen beds.
- **Board** (`--bp-ground-*`, `--bp-cover*`) — cool, pale, chalky. Painted concrete in sunlight.
- **Card** (`--bp-deck-*`) — warm, near-white, printed. Every panel and widget.

Warm/cool/warm against each other, at three separated values. That contrast is doing the work that a
drop shadow would do in a generic light theme.

| Token (= USS custom property) | Hex | USS value | sRGB 0–1 | Linear RGBA | Where it goes |
|---|---|---|---|---|---|
| `--bp-ground-a` | `#A8B3B8` | `rgb(168, 179, 184)` | 0.6588, 0.7020, 0.7216 | 0.3937, 0.4508, 0.4793, 1 | Board concrete, cell A. The **lighter** checker square. HSL-L 0.690, inside the builder's 0.62–0.70 deck window. |
| `--bp-ground-b` | `#9FAAB0` | `rgb(159, 170, 176)` | 0.6235, 0.6667, 0.6902 | 0.3467, 0.4020, 0.4342, 1 | Board concrete, cell B. The darker checker partner, one rung down. |
| `--bp-ground-hill` | `#8E999E` | `rgb(142, 153, 158)` | 0.5569, 0.6000, 0.6196 | 0.2705, 0.3185, 0.3419, 1 | Objective-zone deck. Darker than the checker so contested-cell FX have ground to read against. |
| `--bp-ground-line` | `#6E7A80` | `rgb(110, 122, 128)` | 0.4314, 0.4784, 0.5020 | 0.1559, 0.1946, 0.2159, 1 | Grid separator, drawn 0.04 wide, unlit. |
| `--bp-ground-paint` | `#E8E2D4` | `rgb(232, 226, 212)` | 0.9098, 0.8863, 0.8314 | 0.8070, 0.7605, 0.6584, 1 | Painted deck markings: lane chevrons, objective ring, spawn hatching. |
| `--bp-paint` | `#737C85` | `rgb(115, 124, 133)` | 0.4510, 0.4863, 0.5216 | 0.1714, 0.2016, 0.2346, 1 | Hover border on inputs, toggles and dropdowns. The mid drawn line. |
| `--bp-paint-hazard` | `#E39A12` | `rgb(227, 154, 18)` | 0.8902, 0.6039, 0.0706 | 0.7682, 0.3231, 0.0060, 1 | Hazard stripe yellow, paired with --bp-ink. |
| `--bp-cover` | `#66707A` | `rgb(102, 112, 122)` | 0.4000, 0.4392, 0.4784 | 0.1329, 0.1620, 0.1946, 1 | Cover block body (the vertical faces). |
| `--bp-cover-cap` | `#5E6871` | `rgb(94, 104, 113)` | 0.3686, 0.4078, 0.4431 | 0.1119, 0.1384, 0.1651, 1 | Cover top face. **Darker than the body in albedo** — see §1.4. |
| `--bp-cover-plate` | `#59626A` | `rgb(89, 98, 106)` | 0.3490, 0.3843, 0.4157 | 0.0999, 0.1221, 0.1441, 1 | Cover ID plate and recessed detail band. |
| `--bp-rail` | `#3A342C` | `rgb(58, 52, 44)` | 0.2275, 0.2039, 0.1725 | 0.0423, 0.0343, 0.0252, 1 | Board rail and posts. The frame around the diorama. |
| `--bp-sky` | `#CAD1D4` | `rgb(202, 209, 212)` | 0.7922, 0.8196, 0.8314 | 0.5906, 0.6376, 0.6584, 1 | Camera clear colour and backdrop beyond the rail. **The single largest area in frame**, so it takes the strictest saturation cap: HSL-S 10.4%. |
| `--bp-shadow` | `#3E4C55` | `rgb(62, 76, 85)` | 0.2431, 0.2980, 0.3333 | 0.0482, 0.0723, 0.0908, 1 | Cool shadow tint: contact shadows, vignette colour, ambient ground. |
| `--bp-fog` | `#C5C7C3` | `rgb(197, 199, 195)` | 0.7725, 0.7804, 0.7647 | 0.5583, 0.5711, 0.5457, 1 | Fog-of-war wash. Flat, chroma-free, slightly above the deck in value. |
| `--bp-fog-edge` | `#1E262E` | `rgb(30, 38, 46)` | 0.1176, 0.1490, 0.1804 | 0.0132, 0.0197, 0.0273, 1 | The crisp cell-quantised vision boundary. This line does the legibility work, not the fill — so it is near-ink, not a mid grey. See §7.4. |

### 1.4 World colour is authored in albedo and judged in rendered space

**This subsection governs every value in §1.3 and every contrast figure in §7 that involves a world
surface.** It does not apply to UI, which is unlit and where albedo *is* the rendered value.

> ## ⚠ The orientation transfer table is UNRESOLVED and must not be quoted yet
>
> A previous revision of this section published `1.000 / 0.705 / 0.237`. **Those numbers are
> withdrawn.** They were transcribed from constants in `Assets/Editor/ArenaBuilder.cs`, which their
> own author has since confirmed are **not reproducible from any method it can state**. There was
> never an independent cross-check; this document quoting the builder was not corroboration of it.
>
> **Nothing below the "what is settled" heading may be used to author a value until the probe in
> §1.4.4 has run.** The cover kit is unaffected and is *not* pending — see §1.4.5.

#### 1.4.1 What is settled, and does not depend on the measurement

**Albedo ladders belong in linear space; contrast checks belong in perceptual space.** This is
physics rather than a reading of anyone's constants, and it is the single most useful thing in this
section:

| Question | Space | Why |
|---|---|---|
| "Is my albedo step big enough to survive an orientation change?" | **Linear** | Albedo multiplies irradiance linearly. A step authored against a perceptual ratio when the physical gain is larger lands roughly **40% flatter in radiance than intended** — the same direction as the cover-cap failure. |
| "Will the player see these two surfaces as different?" | **Perceptual** (WCAG relative luminance, and therefore linearised before weighting) | Discrimination is a display-space question. |

Getting these backwards is not a rounding error; it is the error this section exists to prevent.

**A single ratio is the wrong shape for this.** Whichever one you publish alone, half its readers
will apply it to the wrong problem and be wrong in a predictable direction. Every figure below is
labelled with the space it is measured in and the question it answers.

Three structural facts also hold regardless of the measurement:

- **Side-on and facing-away are the same case.** Under the on-axis sun (§7.7 of `ArtDirection.md`)
  `N·L` is zero for both and both receive the same equator ambient. **The fairness ruling reduced
  four vertical cases to two, for free.** This is a real and unadvertised benefit of azimuth 90°.
- **Cast shadow is a bigger lever than orientation, and this is genuinely unobvious.** A *shadowed
  horizontal* face is darker than a *lit vertical* one under every candidate model below. A prop
  author needs the shadow row more than the orientation rows.
- **Mean-of-four-faces is the right quantity for a block**, and a named single face is the right
  quantity for a plane. A player reads a cover block as an average of its visible sides, not as one
  face, so a cover-cap decision should be taken against the mean. A deck decal should not.

#### 1.4.2 The derivation, so this can be re-run rather than looked up

State the method, not the result. The reason this section is in dispute at all is that a
good-looking number circulated without one — and a coincidence and a measurement are
indistinguishable from the outside.

Irradiance on a face with normal **N**, normalised to a lit horizontal face:

```
E(N)  =  Y_sun · 1.25 · max(0, N·L) · (1 − shadowStrength · inShadow)  +  Y_ambient(N.y) · 0.78
```

with, for the FIELD DAY rig:

- `L`, the vector to the sun, from Unity euler `(52, 90, 0)`: forward is
  `(sin·az·cos·el, −sin·el, cos·az·cos·el)` = `(+0.616, −0.788, 0)`, so `L = (−0.616, +0.788, 0)`.
- `N·L` = **0.788** horizontal, **0.616** vertical facing the sun (−X), **0** for +X and both ±Z.
- `Y_sun` = relative luminance of `#FFF7E8` = 0.9377, times intensity 1.25.
- `Y_ambient` from the trilight gradient, lerped on `N.y`: Sky at `N.y = 1`, Equator at `N.y = 0`.
- `shadowStrength` = 0.72, so a shadowed face keeps 28% of its direct term.

**Everything above is agreed. The single open question is how Unity reads the three gradient-ambient
colours** — as sRGB requiring linearisation, or as already-linear. That one choice produces:

| Face | Ambient read as **sRGB** | Ambient read as **linear** |
|---|---|---|
| Horizontal, lit | 1.000 | 1.000 |
| Vertical, sun-facing | 0.672 | 0.750 |
| Vertical, side-on **and** facing away | **0.165** | **0.289** |
| Horizontal, **shadowed** | 0.533 | 0.575 |
| Mean of the four verticals | 0.292 → **3.43×** | 0.404 → **2.47×** |

**The side-on row swings by 1.75×, which is larger than the entire horizontal-to-vertical effect the
table exists to describe.** That is why nothing here may be quoted yet.

#### 1.4.3 Where the circulating numbers came from

| Figure | Claim | Status |
|---|---|---|
| **2.4× / 2.50×** | horizontal against the **mean of four verticals** | **Plausible.** Provenance from the author, with a stated mechanism. Reproduces as 2.47× above — but *only* under the linear-ambient reading. |
| **1.42×** | horizontal against a **single sun-facing vertical** | A different question, not a different answer. Derived from the withdrawn 0.705. |
| **1.28×** | as 1.42× but **direct light only, no ambient** | Correct for what it measures; not useful for authoring, since nothing on the board is lit without ambient. |
| **`1.42^2.4 = 2.32`, "linear versus sRGB"** | — | **Withdrawn.** A numerical coincidence that survived because it was tidy. Provenance beats arithmetic that happens to land. |
| **0.705 / 0.237** | the shipped constants | **Withdrawn.** Match neither candidate; sit between them. |

This document's independent derivation reproduces **both** candidate columns to within 0.007, which
is worth more than agreeing with either: it localises the entire disagreement to the ambient
question and rules out an error anywhere else in the chain.

**This yields a falsifiable prediction.** The 2.50× mean-of-four figure is only reproducible under
the linear-ambient reading, which predicts a side-on value of **0.289**. If the probe reads ≈0.16,
then 2.50× falls with it and the mean is nearer 3.4×.

#### 1.4.4 The probe, and how to fill this table in

`Battle Plan → Art → Measure Irradiance Transfer` renders grey quads in four orientations off the
live scene with post-processing disabled and reads back the centre pixel. **It runs in the barrier.**
The two candidates are far enough apart that one reading is decisive.

| Face | sRGB candidate | Linear candidate | **MEASURED** |
|---|---|---|---|
| Horizontal, lit | 1.000 | 1.000 | *pending* |
| Vertical, sun-facing | 0.672 | 0.750 | *pending* |
| Vertical, side-on / away | 0.165 | 0.289 | *pending* |
| Horizontal, shadowed | 0.533 | 0.575 | *pending* |
| Mean of four verticals | 3.43× | 2.47× | *pending* |

Report, for the same two pixels, **the raw pre-tonemap framebuffer value and the final sRGB value**,
so the space each figure lives in is evidenced rather than asserted. Take the measured column as
authoritative, delete the two candidate columns, and delete this paragraph.

#### 1.4.5 The cover kit is not pending, and its ladder direction is correct

**The cover conclusion is invariant across every candidate from 0.674 to 0.800**, because the
decisive step — cap versus deck — is between **two horizontal faces**, so the orientation factor
appears on both sides and cancels. Measured on the shipped board the kit passes comfortably: deck
0.660, cap 0.802, lit body 0.265, shaded body 0.148, smallest step **0.14**. This is a
documentation-correctness problem, not a board-is-wrong problem, and no cover value is blocked.

> **The cap is lighter than the body here, and that is the opposite of what a dark board needs.
> Do not "fix" it back.** On a dark board, lighting and albedo push against each other: the cap
> catches the light, so authoring it dark is how you stop it blowing out. Here they push the *same*
> way — the board's whole thesis is that lightness is scarce, so the cap is allowed to be the one
> surface that spends it, and the albedo reinforces the lighting instead of fighting it. The rule is
> not "caps are dark" or "caps are light"; it is **check whether lighting and albedo are adding or
> cancelling before you set the step**, which is the same rule as everything else in §1.4.

Anything added to the board later — props, decals, character materials, ground FX — goes through the
same check before its values are trusted.

---

## 2. Team blue (friendly)

Blue is a contract, not a decoration. It means *yours* and it never means anything else. Never use
it for a neutral accent, a link, a hover, or a decorative gradient.

**Every `-hi` and `-deep` step is darker than the base.** On a light board, emphasis is ink. This
holds for all four colour families and it is the single rule that makes the Night Range value
inversion safe without renaming anything.

| Token (= USS custom property) | Hex | USS value | sRGB 0–1 | Linear RGBA | Where it goes |
|---|---|---|---|---|---|
| `--bp-blue` | `#0F5CB8` | `rgb(15, 92, 184)` | 0.0588, 0.3608, 0.7216 | 0.0048, 0.1070, 0.4793, 1 | Team blue identity: unit accent, selection ring, friendly lines, blue fills. |
| `--bp-blue-hi` | `#0B4A93` | `rgb(11, 74, 147)` | 0.0431, 0.2902, 0.5765 | 0.0033, 0.0685, 0.2918, 1 | High emphasis: blue text on light, hover fill. |
| `--bp-blue-deep` | `#08356B` | `rgb(8, 53, 107)` | 0.0314, 0.2078, 0.4196 | 0.0024, 0.0356, 0.1470, 1 | Pressed fill, and the burnt edge under a blue fill. |
| `--bp-blue-dim` | `#93A7BC` | `rgb(147, 167, 188)` | 0.5765, 0.6549, 0.7373 | 0.2918, 0.3864, 0.5029, 1 | Ghosted blue: out-of-play, unavailable, rule accent on a quiet row. |
| `--bp-blue-surface` | `#DCEAFA` | `rgb(220, 234, 250)` | 0.8627, 0.9176, 0.9804 | 0.7157, 0.8228, 0.9560, 1 | Tinted bed behind blue content. Pairs with --bp-blue-hi text. |
| `--bp-blue-wash` | `#0F5CB8` | `rgba(15, 92, 184, 0.16)` | 0.0588, 0.3608, 0.7216 | 0.0048, 0.1070, 0.4793, 0.16 | Blue at 0.16 over the deck: move range, friendly area. |
| `--bp-blue-line` | `#0F5CB8` | `rgba(15, 92, 184, 0.55)` | 0.0588, 0.3608, 0.7216 | 0.0048, 0.1070, 0.4793, 0.55 | Blue at 0.55: selected border, path ribbon, cell outline. |

---

## 3. Team red (enemy / adversarial / invalid)

| Token (= USS custom property) | Hex | USS value | sRGB 0–1 | Linear RGBA | Where it goes |
|---|---|---|---|---|---|
| `--bp-red` | `#E23B45` | `rgb(226, 59, 69)` | 0.8863, 0.2314, 0.2706 | 0.7605, 0.0437, 0.0595, 1 | Team red identity: enemy accent, threat, invalid. |
| `--bp-red-hi` | `#A8142A` | `rgb(168, 20, 42)` | 0.6588, 0.0784, 0.1647 | 0.3916, 0.0070, 0.0232, 1 | High emphasis: danger text on light, hover fill. |
| `--bp-red-deep` | `#780C1C` | `rgb(120, 12, 28)` | 0.4706, 0.0471, 0.1098 | 0.1878, 0.0037, 0.0116, 1 | Pressed fill, burnt edge under a red fill. |
| `--bp-red-dim` | `#C29AA0` | `rgb(194, 154, 160)` | 0.7608, 0.6039, 0.6275 | 0.5395, 0.3231, 0.3515, 1 | Ghosted red: eliminated health fill, spent threat. |
| `--bp-red-surface` | `#FBE0E3` | `rgb(251, 224, 227)` | 0.9843, 0.8784, 0.8902 | 0.9647, 0.7454, 0.7682, 1 | Tinted bed behind danger content. Pairs with --bp-red-hi text. |
| `--bp-red-border` | `#EFAEB6` | `rgb(239, 174, 182)` | 0.9373, 0.6824, 0.7137 | 0.8632, 0.4233, 0.4678, 1 | Border of a red-surface bed. |
| `--bp-red-wash` | `#E23B45` | `rgba(226, 59, 69, 0.16)` | 0.8863, 0.2314, 0.2706 | 0.7605, 0.0437, 0.0595, 0.16 | Red at 0.16: threat area, enemy range. |
| `--bp-red-line` | `#E23B45` | `rgba(226, 59, 69, 0.55)` | 0.8863, 0.2314, 0.2706 | 0.7605, 0.0437, 0.0595, 0.55 | Red at 0.55: threat outline, enemy path. |

---

## 4. Signal colours

### 4.1 Orange — "act now"

The token is still called `--bp-amber` because 21 rules reference it and a rename is not worth the
churn, but the value is now a saturated toy orange rather than a warning amber. It marks the thing
the player should do next: the primary action, the commit control, the objective, the live timer.
It is `#F08A14` — this is the Toy Shelf's Pogo Orange, reinstated and pushed from 62% to 88%
saturation so it survives on a bright deck.

`--bp-amber-hover` is new. It exists because a single token cannot be both a 4.5:1 text colour on
card stock and a pleasant hover fill; splitting them costs one new name and one changed rule.

| Token (= USS custom property) | Hex | USS value | sRGB 0–1 | Linear RGBA | Where it goes |
|---|---|---|---|---|---|
| `--bp-amber` | `#F08A14` | `rgb(240, 138, 20)` | 0.9412, 0.5412, 0.0784 | 0.8714, 0.2542, 0.0070, 1 | Act now. Primary action fill, commit control, the objective, the live timer. |
| `--bp-amber-hover` | `#D2760B` | `rgb(210, 118, 11)` | 0.8235, 0.4627, 0.0431 | 0.6445, 0.1812, 0.0033, 1 | Primary-button hover fill. A modest darken that keeps ink label text legible. |
| `--bp-amber-hi` | `#96500A` | `rgb(150, 80, 10)` | 0.5882, 0.3137, 0.0392 | 0.3050, 0.0802, 0.0030, 1 | High emphasis: amber text on light, primary-button hover fill. |
| `--bp-amber-deep` | `#5E3103` | `rgb(94, 49, 3)` | 0.3686, 0.1922, 0.0118 | 0.1119, 0.0307, 0.0009, 1 | Pressed fill, and the burnt 3-side edge under the primary button. |
| `--bp-amber-surface` | `#FDEFD6` | `rgb(253, 239, 214)` | 0.9922, 0.9373, 0.8392 | 0.9823, 0.8632, 0.6724, 1 | Tinted bed behind act-now content. |
| `--bp-amber-border` | `#F0C47A` | `rgb(240, 196, 122)` | 0.9412, 0.7686, 0.4784 | 0.8714, 0.5520, 0.1946, 1 | Border of an amber-surface bed. |
| `--bp-amber-wash` | `#F08A14` | `rgba(240, 138, 20, 0.16)` | 0.9412, 0.5412, 0.0784 | 0.8714, 0.2542, 0.0070, 0.16 | Amber at 0.16: objective footprint, ability area. |

### 4.2 Green — "confirmed"

UI only. Green never appears on the board. A green thing in the arena would be read as a third team.

| Token (= USS custom property) | Hex | USS value | sRGB 0–1 | Linear RGBA | Where it goes |
|---|---|---|---|---|---|
| `--bp-green` | `#0C8A52` | `rgb(12, 138, 82)` | 0.0471, 0.5412, 0.3216 | 0.0037, 0.2542, 0.0844, 1 | Confirmed, ready, locked in. UI only; never a world colour. |
| `--bp-green-hi` | `#076B41` | `rgb(7, 107, 65)` | 0.0275, 0.4196, 0.2549 | 0.0021, 0.1470, 0.0529, 1 | Success text on light. |
| `--bp-green-surface` | `#DCF3E7` | `rgb(220, 243, 231)` | 0.8627, 0.9529, 0.9059 | 0.7157, 0.8963, 0.7991, 1 | Tinted bed behind success content. |
| `--bp-green-border` | `#98D7B9` | `rgb(152, 215, 185)` | 0.5961, 0.8431, 0.7255 | 0.3140, 0.6795, 0.4851, 1 | Border of a green-surface bed. |

---

## 5. Text, lines, scrims, focus

| Token (= USS custom property) | Hex | USS value | sRGB 0–1 | Linear RGBA | Where it goes |
|---|---|---|---|---|---|
| `--bp-text` | `#1A2128` | `rgb(26, 33, 40)` | 0.1020, 0.1294, 0.1569 | 0.0103, 0.0152, 0.0212, 1 | Primary text, headings, numerals. |
| `--bp-text-2` | `#3E4852` | `rgb(62, 72, 82)` | 0.2431, 0.2824, 0.3216 | 0.0482, 0.0648, 0.0844, 1 | Secondary text, supporting copy. |
| `--bp-text-3` | `#566069` | `rgb(86, 96, 105)` | 0.3373, 0.3765, 0.4118 | 0.0931, 0.1170, 0.1413, 1 | Tertiary text, labels, units, disabled copy. |
| `--bp-text-on-accent` | `#1B1206` | `rgb(27, 18, 6)` | 0.1059, 0.0706, 0.0235 | 0.0110, 0.0060, 0.0018, 1 | Text and icon tint on a saturated fill (amber primary). |
| `--bp-line-1` | `#151A20` | `rgba(21, 26, 32, 0.16)` | 0.0824, 0.1020, 0.1255 | 0.0075, 0.0103, 0.0144, 0.16 | Ink at 0.16: quiet divider, disabled border, inner rule. |
| `--bp-line-2` | `#151A20` | `rgba(21, 26, 32, 0.52)` | 0.0824, 0.1020, 0.1255 | 0.0075, 0.0103, 0.0144, 0.52 | Ink at 0.52: the standard drawn border on every card and control. |
| `--bp-line-3` | `#151A20` | `rgba(21, 26, 32, 0.78)` | 0.0824, 0.1020, 0.1255 | 0.0075, 0.0103, 0.0144, 0.78 | Ink at 0.78: strong divider, emphasised border, checkbox rest border. |
| `--bp-edge-dark` | `#151A20` | `rgba(21, 26, 32, 0.92)` | 0.0824, 0.1020, 0.1255 | 0.0075, 0.0103, 0.0144, 0.92 | Ink at 0.92: the 4 px structural bottom edge. This is the card thickness. |
| `--bp-panel` | `#F2EDE2` | `rgba(242, 237, 226, 0.96)` | 0.9490, 0.9294, 0.8863 | 0.8879, 0.8469, 0.7605, 0.96 | Card stock at 0.96 over the live board. Floating HUD widget fill. |
| `--bp-dock` | `#E4DDCE` | `rgba(228, 221, 206, 0.97)` | 0.8941, 0.8667, 0.8078 | 0.7758, 0.7231, 0.6172, 0.97 | Tray at 0.97. Grouped HUD clusters that need to read as one component. |
| `--bp-scrim` | `#151A20` | `rgba(21, 26, 32, 0.66)` | 0.0824, 0.1020, 0.1255 | 0.0075, 0.0103, 0.0144, 0.66 | Ink at 0.66. Modal scrim over a bright board. |
| `--bp-focus-light` | `#151A20` | `rgb(21, 26, 32)` | 0.0824, 0.1020, 0.1255 | 0.0075, 0.0103, 0.0144, 1 | Focus ring FOR LIGHT-SURFACED controls. Ink. |
| `--bp-focus-dark` | `#151A20` | `rgb(21, 26, 32)` | 0.0824, 0.1020, 0.1255 | 0.0075, 0.0103, 0.0144, 1 | Focus ring FOR SATURATED OR DARK fills. Paper. |
| `--bp-transparent` | `#000000` | `rgba(0, 0, 0, 0.00)` | 0.0000, 0.0000, 0.0000 | 0.0000, 0.0000, 0.0000, 0.00 | Explicit no-fill. |

### 5.1 The focus tokens — decision

**`--bp-focus-light` is redefined to ink and keeps its name.** 22 rules reference it and 2
reference `--bp-focus-dark`; renaming would touch every screen for no functional gain, and leaving
a pale ring on a pale board is an accessibility regression rather than a cosmetic one.

The names are not lies under the new reading. Read them as naming **the host, not the ring**:

- `--bp-focus-light` — the ring for a control on a **light** surface. Ink.
- `--bp-focus-dark` — the ring for a control on a **dark or saturated** fill. Also ink, because ink
  is what actually wins on `#F08A14` (6.96:1 versus 2.39:1 for paper).

Both currently resolve to `--bp-ink`. The two-token split is retained rather than collapsed so that
a genuinely dark-filled control can diverge later without a migration. Minimum focus-ring contrast
anywhere in the product is **6.96:1**, on the primary button; maximum is 16.65:1 on an input.

No rename pass is needed. If one is ever done for clarity, the target names are
`--bp-focus-on-light` and `--bp-focus-on-dark`.

---

## 6. HDR emissive values

Bloom threshold is **1.8** and bloom intensity is **0.5** (see ArtDirection §7.6). That is a
deliberate ceiling: on a bright board, glow does not read as energy, it reads as fog. Almost nothing
should cross the threshold.

**Only these seven things in the entire game are allowed to bloom.** Everything else — every
projectile body, every ability shape, every ground overlay, every unit accent — is opaque colour
with an ink contour and no emission at all. If you find yourself adding an eighth row here, the
effect is probably solving a silhouette problem with light, which does not work at high key.

| What | Base sRGB | Stops | `_EmissionColor` / `_CoreColor` (LINEAR — paste this) | Peak luminance | Passes bloom threshold 1.8 | Shader |
|---|---|---|---|---|---|---|
| Muzzle flash core, 2 frames | `#FFF0D2` | +2.0 | `4.000, 3.485, 2.578, 1` | 3.53 | yes | BP_FXUnlit, Additive |
| Impact flash frame, 1 frame | `#FFFFFF` | +1.6 | `3.031, 3.031, 3.031, 1` | 3.03 | yes | BP_FXUnlit, Additive |
| Sniper charge core | `#FFD24C` | +2.2 | `4.595, 2.961, 0.332, 1` | 3.12 | yes | BP_FXParticle, Additive |
| Beam core, blue | `#7FB6F5` | +1.8 | `0.739, 1.629, 3.180, 1` | 1.55 | **no** | BattlePlan/EnergyBeam, _CoreColor |
| Beam core, red | `#FF9AA1` | +1.8 | `3.482, 1.125, 1.241, 1` | 1.63 | **no** | BattlePlan/EnergyBeam, _CoreColor |
| Objective ring, contested | `#F08A14` | +1.4 | `2.300, 0.671, 0.018, 1` | 0.97 | **no** | BattlePlan/GroundGlow, Additive ring only |
| Deployment mark sweep | `#FFF6E4` | +1.2 | `2.297, 2.117, 1.782, 1` | 2.13 | yes | BP_FXUnlit, Additive |

Paste the **linear** column into the `_EmissionColor` / `_CoreColor` field, or set it from C# with
`mat.SetColor("_EmissionColor", new Color(r, g, b, 1))` — `Color` values passed to `SetColor` are
already linear and are **not** converted.

To recompute: `linear = sRGBToLinear(baseColour) * pow(2, stops)`.

---

## 7. Contrast verification

> **Instrument.** Every ratio here is WCAG 2.1: channels are **linearised first**, then weighted
> `0.2126 R + 0.7152 G + 0.0722 B`, then `(L_hi + 0.05) / (L_lo + 0.05)`.
>
> **This is not the same as luma.** Applying those weights to *gamma-encoded* sRGB values without
> linearising gives a number that looks like relative luminance, is systematically too high in the
> midtones, and will not reproduce any ratio in this table. `--bp-ground-a` is relative luminance
> **0.440** and luma **0.694** — a gap wide enough to move a token in or out of a window on its own.
> If a validator disagrees with this table, check the linearisation step before the value.

Computed on the composited sRGB result where a token carries alpha, so it is measured as it lands
rather than as authored. Rows marked **raw tokens** are the uncomposited pair and are informational
only; §7.4 carries the composited figure. `AA` = 4.5:1 body text. `AA-lg` / non-text = 3.0:1.

| Foreground | Background | Composited fg | Ratio | Need | Verdict | Use |
|---|---|---|---|---|---|---|
| `--bp-text` | card stock `#F2EDE2` | `#1A2128` | **13.92** | 4.5 | pass | Body copy on every panel |
| `--bp-text` | paper `#FCF9F3` | `#1A2128` | **15.47** | 4.5 | pass | Input text, plate labels |
| `--bp-text` | tray `#E4DDCE` | `#1A2128` | **12.02** | 4.5 | pass | Text in a dock cluster |
| `--bp-text-2` | card stock `#F2EDE2` | `#3E4852` | **7.98** | 4.5 | pass | Supporting copy |
| `--bp-text-2` | paper `#FCF9F3` | `#3E4852` | **8.87** | 4.5 | pass | Supporting copy in a well |
| `--bp-text-3` | card stock `#F2EDE2` | `#566069` | **5.50** | 4.5 | pass | Labels, units, captions |
| `--bp-text-3` | paper `#FCF9F3` | `#566069` | **6.11** | 4.5 | pass | Labels in a well |
| `--bp-text-3` | tray `#E4DDCE` | `#566069` | **4.75** | 4.5 | pass | Labels in a dock cluster |
| `--bp-text` | panel over deck `#EFEAE0` | `#1A2128` | **13.56** | 4.5 | pass | Panel at 0.96 over ground A |
| `--bp-ink` | table `#5E5346` | `#151A20` | **2.33** | 4.5 | banned pair | Ink on the tabletop — must NOT be used |
| `--bp-deck-0` | table `#5E5346` | `#FCF9F3` | **7.13** | 3.0 | pass | Paper card on the tabletop (surface separation) |
| `--bp-deck-1` | table `#5E5346` | `#F2EDE2` | **6.42** | 3.0 | pass | Card stock on the tabletop |
| `--bp-red-hi` | card stock `#F2EDE2` | `#A8142A` | **6.43** | 4.5 | pass | Danger text |
| `--bp-red-hi` | red bed `#FBE0E3` | `#A8142A` | **6.03** | 4.5 | pass | Danger text on its own bed |
| `--bp-green-hi` | card stock `#F2EDE2` | `#076B41` | **5.64** | 4.5 | pass | Success text |
| `--bp-green-hi` | green bed `#DCF3E7` | `#076B41` | **5.65** | 4.5 | pass | Success text on its own bed |
| `--bp-amber-hi` | card stock `#F2EDE2` | `#96500A` | **5.22** | 4.5 | pass | Act-now text |
| `--bp-amber-hi` | amber bed `#FDEFD6` | `#96500A` | **5.36** | 4.5 | pass | Act-now text on its own bed |
| `--bp-amber-deep` | card stock `#F2EDE2` | `#5E3103` | **9.39** | 4.5 | pass | Act-now text, maximum emphasis |
| `--bp-blue-hi` | card stock `#F2EDE2` | `#0B4A93` | **7.45** | 4.5 | pass | Friendly text |
| `--bp-blue-hi` | blue bed `#DCEAFA` | `#0B4A93` | **7.12** | 4.5 | pass | Friendly text on its own bed |
| `--bp-text-on-accent` | amber fill `#F08A14` | `#1B1206` | **7.35** | 4.5 | pass | Primary button label |
| `--bp-text-on-accent` | amber hover `#D2760B` | `#1B1206` | **5.58** | 4.5 | pass | Primary button label, hover |
| `--bp-deck-0` | blue fill `#0F5CB8` | `#FCF9F3` | **6.16** | 4.5 | pass | Paper label on a blue fill |
| `--bp-deck-0` | red fill `#E23B45` | `#FCF9F3` | **4.04** | 4.5 | FAIL | Paper label on a red fill |
| `--bp-deck-0` | green fill `#0C8A52` | `#FCF9F3` | **4.19** | 4.5 | FAIL | Paper label on a green fill |
| `--bp-deck-0` | table `#5E5346` | `#FCF9F3` | **7.13** | 4.5 | pass | Paper text on the tabletop |
| `--bp-focus-light` | card stock `#F2EDE2` | `#151A20` | **14.98** | 3.0 | pass | Focus ring on a light control |
| `--bp-focus-light` | paper `#FCF9F3` | `#151A20` | **16.65** | 3.0 | pass | Focus ring on an input |
| `--bp-focus-light` | hover `#D2C9B6` | `#151A20` | **10.64** | 3.0 | pass | Focus ring on a hovered control |
| `--bp-focus-dark` | amber fill `#F08A14` | `#151A20` | **6.96** | 3.0 | pass | Focus ring on the primary button |
| `--bp-line-2` | card stock `#F2EDE2` | `#7F7F7D` | **3.44** | 3.0 | pass | Standard border (composited) |
| `--bp-line-3` | card stock `#F2EDE2` | `#46484B` | **7.86** | 3.0 | pass | Strong border (composited) |
| `--bp-paint` | paper `#FCF9F3` | `#737C85` | **4.04** | 3.0 | pass | Hover border on an input |
| `--bp-blue` | ground A `#A8B3B8` | `#0F5CB8` | **3.02** | 3.0 | pass | Team blue line on the board |
| `--bp-red` | ground A `#A8B3B8` | `#E23B45` | **1.98** | 3.0 | FAIL | Team red line on the board |
| `--bp-amber` | ground A `#A8B3B8` | `#F08A14` | **1.17** | 3.0 | FAIL | Objective marking on the board |
| `--bp-ink` | ground A `#A8B3B8` | `#151A20` | **8.17** | 3.0 | pass | Every FX contour on the board — **at full pixel coverage**. Most contours are sub-pixel; see `ArtDirection.md` §4.3.1. |
| `--bp-fog` | ground A `#A8B3B8` | `#C5C7C3` | **1.26** | 1.1 | raw tokens | Fog **tokens**, uncomposited. The composited figure is the one that matters — see §7.4. |
| `--bp-fog-edge` | fog wash `#C5C7C3` | `#1E262E` | **12.06** | 3.0 | raw tokens | Vision boundary, uncomposited. See §7.4. |
| `--bp-blue` | team red `#E23B45` | `#0F5CB8` | **1.52** | 3.0 | FAIL | Blue vs red, direct |
| `--bp-red` | signal amber `#F08A14` | `#E23B45` | **1.69** | 1.4 | hue-separated | Red vs amber, direct |
| `--bp-blue` | signal amber `#F08A14` | `#0F5CB8` | **2.57** | 3.0 | FAIL | Blue vs amber, direct |

**Cover is deliberately absent from this table.** Cover against deck is an albedo comparison across
an orientation boundary, so an albedo contrast figure for it would be actively misleading. §1.4.5
verifies it on measured rendered values instead — deck 0.660, cap 0.802, lit body 0.265, shaded body
0.148, smallest step 0.14 — and that conclusion is invariant across the open question in §1.4.

### 7.1 The pairs that do not reach 3.0:1, and why that is correct

Two saturated fills sit close in luminance to the deck: red at 1.98:1 and amber at 1.17:1. Blue
scrapes past at 3.02:1, but only against cell A — against the darker cell B it drops below the line,
so it is governed by the same law. **This is not fixable by tuning and it is not a defect.** A mid-value light ground and a
saturated mid-value hue have similar luminance by definition; the only escapes are to darken the
ground until it is no longer a bright board, or to desaturate the signals until they are no longer
signals. Both destroy the direction.

The resolution is structural and it is a hard law of FIELD DAY:

> **No saturated shape ever touches the deck without ink between them.** Every projectile, overlay,
> telegraph, ability shape, marker, unit base and FX element carries a `--bp-ink` contour. The
> contour carries the contrast — **8.17:1 against cell A**, 7.37:1 against the darker cell B — and
> the fill carries the identity.

**Both those figures are for a contour covering a whole pixel, which most of them do not.** A
contour is a partial-coverage edge and resolves in proportion to the fraction it covers; the width
rule, the coverage→contrast table and the anti-aliasing the whole thing depends on are
`ArtDirection.md` **§4.3.1**, which is the single normative source. Do not restate a width here.

This is how every printed board game, and Into the Breach and Advance Wars specifically, keep
saturated pieces legible on a light board. It also means the effects language and the palette are
solving the same problem with the same device, which is why §9 of the art direction is built on
contours rather than on glow.

The governing number for any coloured element on the board is therefore the **ink-versus-deck**
row, not the fill-versus-deck row.

### 7.2 Blue versus red

Direct luminance contrast between `--bp-blue` and `--bp-red` is **1.52:1**. That is deliberately
low — pushing them apart in luminance would mean one team's colour is systematically weaker against
the deck, which is unfair rather than merely ugly.

Separation is carried by hue (212.7° versus 356.4°, 144° apart) and by the mandatory shape pairing
in ArtDirection §4: **friendly is always a chevron, hostile is always a bar.** Colour never carries
team identity alone, at any point, in any element.

Colour-vision check: under deuteranopia blue holds and red shifts to a dark ochre, separating them
further in luminance; under protanopia red darkens substantially, again increasing separation;
under tritanopia blue reads teal and red reads pink. All three remain distinguishable, and the shape
contract covers achromatopsia.

### 7.3 Banned pairings

These combinations are computable, plausible, and must never ship. They are listed so a reviewer can
grep for them.

| Never | Ratio | Use instead |
|---|---|---|
| Any text on a `--bp-red` fill | 4.04:1 with paper | `--bp-red-surface` bed with `--bp-red-hi` text |
| Any text on a `--bp-green` fill | 4.19:1 with paper | `--bp-green-surface` bed with `--bp-green-hi` text |
| Paper or `--bp-deck-0` on a `--bp-amber` fill | 2.39:1 | `--bp-text-on-accent` |
| `--bp-ink` on `--bp-table` | 2.33:1 | `--bp-deck-0` |
| `--bp-text-3` on `--bp-deck-3` or darker | below 4.5:1 | `--bp-text-2` |
| Any `-hi` token as a fill behind `--bp-text` | varies | the `-surface` token |

**The ink-on-table row is not an exception to the contour law — it is the contour law stated
properly.** §4.3 of `ArtDirection.md` reads as though the contour is `--bp-ink` by definition, because
every bed it was written against is light. The general form is:

> **A drawn edge carries the contrast, and which colour carries it depends on the bed.** Ink on the
> light board, paper on the dark table. The device is the drawn edge; the value is whichever end of
> the ramp is far from the bed.

On `--bp-table` the dark family cannot carry it, and that is arithmetic rather than taste. The bed's
relative luminance is 0.0901, so solving the WCAG ratio for a 3.0:1 pass on the darker side needs
luminance **−0.0033** — negative. Ink reaches 2.33:1 and pure black, the ceiling for *any* dark
contour, reaches **2.80:1**. This is not a near miss to tune; it is proof that the dark family is the
wrong tool on this bed. `--bp-deck-0` measures **7.13:1**.

Two consequences worth stating so they are not re-derived:

- A contour on `--bp-table` is `--bp-deck-0`. This is what the menu-scene contour uses; see
  `MenuSceneBuilder.cs`.
- The contour's graded pairing is **contour-to-ground**, as in the §7 row for board FX contours. Its
  contrast against the *object* it outlines is a legibility observation, not a threshold. Paper runs
  14.26:1 against `Char_Black` down to 2.86:1 against `Char_Skin`, and no single colour clears 3.0:1
  against both this bed and that skin — so the worst contrast anywhere along a menu figure's edge
  still improves from ink's 2.33:1 to 2.86:1.

---

### 7.4 Composited figures — fog

A token pair is not a contrast figure when the top layer carries alpha. Fog is the case where this
matters most, and an earlier revision quoted the raw pair as though it were the result. Composited
in **linear** over `--bp-ground-a`, which is how the shader actually blends:

| Layer | Token | Alpha | Composited | Reads against | Ratio | Need |
|---|---|---|---|---|---|---|
| Fill | `--bp-fog` | **0.68** | `#BCC1C0` | unfogged deck | **1.18:1** | ≥1.1, deliberately low |
| Boundary | `--bp-fog-edge` | **0.85** | — | the fog fill | **3.98:1** | ≥3.0 |
| Boundary | `--bp-fog-edge` | **0.85** | — | unfogged deck | **3.39:1** | ≥3.0 |

This is why `--bp-fog-edge` moved from a mid grey `#535A60` to a near-ink `#1E262E`. At 0.85 alpha
the old token composited to **2.69:1** against the fill — a boundary that fails the threshold its
own section says is doing all the work. Two alternatives were on the table and both are weaker:
`--bp-shadow` at 0.85 reaches only 3.08:1, and holding the old token at alpha 1.0 reaches 3.84:1 but
buys it by making the line opaque, which costs the wash its transparency for no gain.

**The fog boundary is an ink contour and takes the ink treatment**, exactly like every other
boundary on the deck (§4.3 of `ArtDirection.md`). That is the consistent answer rather than a
special case, and it clears both thresholds with margin.

## 8. Saturation budget

> **Instrument: HSL, not HSV.** Every saturation and lightness figure in this section is **HSL**.
> `Color.RGBToHSV` is the natural thing to reach for in Unity and it is the **wrong instrument** —
> the two disagree by up to 19 points *in both directions*, so a validator built on it does not
> merely drift, it inverts. It reads `--bp-ink` as 34% where HSL gives 21%, inventing a breach, and
> reads the old `--bp-sky` as 29% where HSL gave 48%, hiding the one token that genuinely broke the
> rule. Converting: `S_hsl = S_hsv · V / (1 − |2L − 1|)` where `L = V(1 − S_hsv/2)`.
>
> Lightness in this section is likewise **HSL L**, the midpoint of the max and min channels — not
> HSV value (the max channel alone) and not relative luminance. The three coincide exactly on a
> neutral grey and diverge by up to 0.25 once a hue is present, which is why the deck window read
> as satisfied while the palette was grey and became ambiguous the moment it was tinted.

The central tension of this direction: a toy diorama wants saturated colour everywhere, and a
tactics board needs saturation to mean something. FIELD DAY resolves it by allocating saturation
**by screen area rather than by category**.

> **The larger a thing is on screen, the less saturated it is allowed to be.**

The board is colourful in aggregate — many small saturated objects on a quiet ground — which is
exactly what a painted diorama looks like, and what Bad North, Into the Breach and a physical
wargaming table all look like. The ground is always the quietest element in the frame.

| Class | Typical screen area | Max saturation | Examples |
|---|---|---|---|
| Ground plane, backdrop, sky | > 25% of frame | **12%** | `--bp-sky` at 10%, `--bp-ground-a` at 10%, `--bp-fog` at 3% |
| Panels, large structures, cover | 5–25% | **20%** | `--bp-cover` at 9%, `--bp-deck-1` at 38% lightness-driven, effectively near-neutral |
| Props, set dressing, crates, vegetation | 1–5% | **45%** | No current prop reaches the cap. `--bp-sky` is **not** a prop — it is the backdrop, and it is governed by the row above. |
| Characters, unit local colour | < 1% each | **70%** | Existing `Char_*` materials all sit below this |
| Signals, team accents, FX, UI action | < 0.5%, or brief | **95%** | `--bp-amber` 88%, `--bp-blue` 85%, `--bp-red` 74% |

Two enforcement rules follow:

1. **No environment or prop material may use a hue within 25° of `--bp-blue` (212.7°) or
   `--bp-red` (356.4°) above 30% saturation.** This is what keeps a colourful board from eating the
   team contract. The permitted warm band for props is 30–60°, and the permitted cool band is
   140–190°.
2. **`--bp-paint-hazard` (85% saturation) is the one exception**, permitted because hazard stripes
   are small, always paired with `--bp-ink`, at hue 39° which is a signal-adjacent warm, and they
   are static set dressing that never moves. If they read as competing with the amber signal in
   play, desaturate them to 60% rather than changing the amber.
3. **The band is chosen by the screen area of the MATERIAL, not of the object containing it.** The
   table above is indexed by "typical screen area", and the rule as previously written permitted
   reading a small material into a large object's band. It does not mean that. A 52%-saturation amber
   pad on a hero-scale title figure occupying 72% of frame height stays in the **character** row at
   70%, because the pad itself is a fraction of a percent of frame — it does not move to the 5–25%
   row at 20% merely because the figure it is painted on is that large. The opposite reading would
   force every close-up prop to desaturate its own details into mud, which inverts the intent: the
   budget exists so that *large flat areas* stay quiet, and a detail is not a large flat area.
   Measure the material's own coverage.

Measured saturation of every governed token:

| Token | Hex | Hue | Saturation | Lightness |
|---|---|---|---|---|
| `--bp-blue` | `#0F5CB8` | 213° | **85%** | 39% |
| `--bp-red` | `#E23B45` | 356° | **74%** | 56% |
| `--bp-amber` | `#F08A14` | 32° | **88%** | 51% |
| `--bp-green` | `#0C8A52` | 153° | **84%** | 29% |
| `--bp-ground-a` | `#A8B3B8` | 199° | **10%** | 69% |
| `--bp-ground-b` | `#9FAAB0` | 201° | **10%** | 66% |
| `--bp-ground-hill` | `#8E999E` | 199° | **8%** | 59% |
| `--bp-cover` | `#66707A` | 210° | **9%** | 44% |
| `--bp-cover-cap` | `#5E6871` | 208° | **9%** | 41% |
| `--bp-cover-plate` | `#59626A` | 208° | **9%** | 38% |
| `--bp-deck-1` | `#F2EDE2` | 41° | **38%** | 92% |
| `--bp-table` | `#5E5346` | 32° | **15%** | 32% |
| `--bp-paint-hazard` | `#E39A12` | 39° | **85%** | 48% |
| `--bp-sky` | `#CAD1D4` | 200° | **10%** | 81% |
| `--bp-fog` | `#C5C7C3` | 90° | **3%** | 77% |
| `--bp-ground-paint` | `#E8E2D4` | 42° | **30%** | 87% |

---

## 9. Non-colour tokens

### 9.1 Spacing — 4 px base

| Token | Value | Use |
|---|---|---|
| `--bp-s1` | 4px | Icon-to-label, chip inner padding, tight stack |
| `--bp-s2` | 8px | Control inner padding, list-row gap |
| `--bp-s3` | 12px | Standard gap between related controls |
| `--bp-s4` | 16px | Card inner padding, group separation |
| `--bp-s5` | 24px | Panel padding, section separation |
| `--bp-s6` | 32px | Modal padding, screen gutter |
| `--bp-s7` | 48px | Screen-level separation, hero spacing |

### 9.2 Radii — softened toward the Toy Shelf

Night Range used 2 / 4 / 8 on the argument that a tactics board is an instrument. The user has now
twice said that reading as an instrument is the problem. Reinstated toward `DESIGN.md`'s 6 / 10 / 14:

| Token | Night Range | **FIELD DAY** | Toy Shelf original | Use |
|---|---|---|---|---|
| `--bp-r-sm` | 2px | **4px** | 6px | Chips, health track, badges, inline tags |
| `--bp-r-md` | 4px | **8px** | 10px | Buttons, inputs, cards, HUD widgets |
| `--bp-r-lg` | 8px | **14px** | 14px | Panels, modals, the deployment card |

`--bp-r-lg` is reinstated exactly. `--bp-r-sm` stays tighter than the Toy Shelf's 6px because at
12–16px chip heights a 6px radius is a third of the height and the chip stops reading as a
rectangle, which breaks the shape-pairing contract that carries team identity.

### 9.3 Border widths

| Token | Value | Use |
|---|---|---|
| `--bp-bw-hair` | 1px | The standard drawn border on every card and control, with `--bp-line-2` |
| `--bp-bw` | 2px | Emphasised border, selected state. **UI only** — this token was the source of the world-space "2px contour" rule that `ArtDirection.md` §17.4 withdrew. World contours are §4.3.1's and are not expressed in border tokens. |
| `--bp-bw-rule` | 3px | Left rule on a status line or callout |
| `--bp-bw-focus` | 3px | Focus ring |
| `--bp-edge` | **4px** | **New.** The structural bottom edge — the card's thickness. `--bp-edge-dark`. |

`--bp-edge` reinstates the Toy Shelf's chunky bottom edge at 4px rather than its original 6px. 6px
is right for a 52px menu button and too heavy for a 24px HUD chip; 4px reads as card thickness at
both sizes. The seven rules that currently set `border-bottom-width: var(--bp-bw)` alongside
`border-bottom-color: var(--bp-edge-dark)` should move to `var(--bp-edge)`.

### 9.4 Control sizes

| Token | Value | Use |
|---|---|---|
| `--bp-target-sm` | 32px | Icon-only secondary control, in-panel stepper |
| `--bp-target` | 44px | Standard control height, compact layouts |
| `--bp-target-lg` | 52px | Primary control, menu button, input |
| `--bp-target-phone` | 56px | Any control on the `phone` breakpoint |
| `--bp-icon` | 24px | Standard icon |
| `--bp-icon-sm` | 18px | Inline icon inside a label or chip |
| `--bp-icon-gap` | 10px | Icon-to-label gap |
| `--bp-scroll` | 12px | Scroller width |

### 9.5 Type scale

Saira Condensed for display and interface, Rubik for body, Cascadia Code for numerals. All SDF
assets confirmed present on disk with the full glyph set including `·`, `—`, `…` and `×`.

| Token | Size | Role | Face | Weight | Tracking | Line height | Case |
|---|---|---|---|---|---|---|---|
| `--bp-type-hero` | 56px | Title-screen wordmark | Saira Condensed | ExtraBold | +0.04em | 1.00 | UPPER |
| `--bp-type-display` | 34px | Screen title, result banner | Saira Condensed | ExtraBold | +0.03em | 1.05 | UPPER |
| `--bp-type-title` | 22px | Panel title, modal title | Saira Condensed | SemiBold | +0.02em | 1.15 | UPPER |
| `--bp-type-subtitle` | 17px | Section heading, unit name | Saira Condensed | SemiBold | +0.01em | 1.20 | Title |
| `--bp-type-body` | 15px | Body copy, control labels | Rubik | Regular | 0 | 1.45 | Sentence |
| `--bp-type-body-sm` | 13px | Secondary copy, help text | Rubik | Regular | 0 | 1.40 | Sentence |
| `--bp-type-label` | 12px | Field label, tab, caption | Saira Condensed | SemiBold | +0.06em | 1.20 | UPPER |
| `--bp-type-micro` | 10px | Status micro-copy, badge | Saira Condensed | SemiBold | +0.08em | 1.10 | UPPER |
| `--bp-type-num-lg` | 26px | Turn timer, big score | Cascadia Code | SemiBold | 0 | 1.00 | — |
| `--bp-type-num` | 18px | HP, ammo, cooldown | Cascadia Code | SemiBold | 0 | 1.00 | — |
| `--bp-type-num-sm` | 12px | Inline numeral in a chip | Cascadia Code | Regular | 0 | 1.00 | — |

### 9.6 Motion

| Token | Value | Use |
|---|---|---|
| `--bp-t-instant` | 80ms | Press feedback, checkbox tick |
| `--bp-t-fast` | 120ms | Hover, focus ring, tint change |
| `--bp-t-base` | 180ms | Panel state change, tab switch, card flip |
| `--bp-t-slow` | 280ms | Screen transition, modal entry |
| `--bp-t-flash-in` | 90ms | Damage flash rise |
| `--bp-t-flash-out` | 220ms | Damage flash fall |
| `--bp-ease-out` | `ease-out-circ` | Anything entering or settling |
| `--bp-ease-in` | `ease-in-circ` | Anything leaving |
| `--bp-ease-snap` | `ease-out-back` | Anything that should land like a physical piece |

**These are keywords, not curves, and that is a correction rather than a preference.** Unity USS
`transition-timing-function` accepts only the built-in easing keywords; it has **no
`cubic-bezier()` function**. The Night Range table declared all three tokens as `cubic-bezier(...)`,
which made them unparseable and therefore unreferenceable — a defect that survived the whole of
Night Range because nothing consumed them. The three keywords above are blessed as the intended
values, matching what `BattlePlan.uss` already approximates:

| Token | Intended curve | Keyword | Fidelity |
|---|---|---|---|
| `--bp-ease-out` | fast departure, long settle | `ease-out-circ` | Very close. Circ decelerates harder than quint at the tail, which suits a component coming to rest. |
| `--bp-ease-in` | slow departure, fast exit | `ease-in-circ` | Very close. |
| `--bp-ease-snap` | overshoot then settle | `ease-out-back` | Close. `ease-out-back`'s overshoot is fixed and not tunable; the intended 56% cannot be dialled in. |

**If you need a curve with no keyword equivalent, USS cannot express it — do not go looking.**
Drive it from C# with a `ValueAnimation` or a coroutine over `AnimationCurve` instead, and say so at
the call site. This applies to any bespoke overshoot amount, any two-stage curve, and any
spring simulation.

On FIELD DAY, `--bp-ease-snap` is the default for HUD widgets appearing, because a game component
placed on a table settles rather than fades.

---

## 10. Paste-ready USS

Replace the entire contents of `Assets/UI/Shared/TacticalToyboxTokens.uss` with this block.

**It declares 180 names.** That is all 164 currently in use except `--bp-void` (see §11.1), plus 17
new ones. Verify after pasting:

```sh
rg -o 'var\((--[a-z0-9-]+)' -r '$1' Assets/UI -g '*.uss' -g '*.uxml' --no-filename | sort -u > /tmp/used.txt
rg -o '^\s+(--[a-z0-9-]+):' -r '$1' Assets/UI/Shared/TacticalToyboxTokens.uss | sort -u > /tmp/def.txt
comm -13 /tmp/def.txt /tmp/used.txt    # must be empty
```

```css
:root {
    /* ======================================================================
       Battle Plan — FIELD DAY token contract.
       Source of truth: docs/design/ArtBible-Palette.md
       Do not author a colour anywhere else in the project.

       Two rules that are easy to get wrong on a light board:
       1. The deck ramp is INVERTED from Night Range. --bp-deck-0 is now the
          LIGHTEST surface and --bp-deck-6 the darkest. Emphasis moves toward
          ink, so every -hi and -deep step is DARKER than its base.
       2. --bp-void is gone. Use --bp-table for full-screen beds and --bp-ink
          for the darkest value.
       ====================================================================== */

    /* --- Ink and table — the two anchors ----------------------------------- */
    --bp-ink: rgb(21, 26, 32);
    --bp-table: rgb(94, 83, 70);

    /* --- UI surface ramp — 0 is the LIGHTEST, 6 the darkest ---------------- */
    --bp-deck-0: rgb(252, 249, 243);
    --bp-deck-1: rgb(242, 237, 226);
    --bp-deck-2: rgb(228, 221, 206);
    --bp-deck-3: rgb(210, 201, 182);
    --bp-deck-4: rgb(182, 171, 147);
    --bp-deck-5: rgb(135, 126, 108);
    --bp-deck-6: rgb(61, 55, 45);

    /* --- World / arena — consumed by materials, mirrored here as the single source ---- */
    --bp-ground-a: rgb(168, 179, 184);
    --bp-ground-b: rgb(159, 170, 176);
    --bp-ground-hill: rgb(142, 153, 158);
    --bp-ground-line: rgb(110, 122, 128);
    --bp-ground-paint: rgb(232, 226, 212);
    --bp-paint: rgb(115, 124, 133);
    --bp-paint-hazard: rgb(227, 154, 18);
    /* Cover cap is DARKER than the body in albedo and LIGHTER on screen. Not a typo:
       see Palette §1.4 for the orientation transfer and its derivation. */
    --bp-cover: rgb(102, 112, 122);
    --bp-cover-cap: rgb(94, 104, 113);
    --bp-cover-plate: rgb(89, 98, 106);
    --bp-rail: rgb(58, 52, 44);
    --bp-sky: rgb(202, 209, 212);
    --bp-shadow: rgb(62, 76, 85);
    --bp-fog: rgb(197, 199, 195);
    --bp-fog-edge: rgb(30, 38, 46);

    /* --- Team blue (friendly) ---------------------------------------------- */
    --bp-blue: rgb(15, 92, 184);
    --bp-blue-hi: rgb(11, 74, 147);
    --bp-blue-deep: rgb(8, 53, 107);
    --bp-blue-dim: rgb(147, 167, 188);
    --bp-blue-surface: rgb(220, 234, 250);
    --bp-blue-wash: rgba(15, 92, 184, 0.16);
    --bp-blue-line: rgba(15, 92, 184, 0.55);

    /* --- Team red (enemy / adversarial / invalid) -------------------------- */
    --bp-red: rgb(226, 59, 69);
    --bp-red-hi: rgb(168, 20, 42);
    --bp-red-deep: rgb(120, 12, 28);
    --bp-red-dim: rgb(194, 154, 160);
    --bp-red-surface: rgb(251, 224, 227);
    --bp-red-border: rgb(239, 174, 182);
    --bp-red-wash: rgba(226, 59, 69, 0.16);
    --bp-red-line: rgba(226, 59, 69, 0.55);

    /* --- Signal orange — act now. Token name stays --bp-amber. ------------- */
    --bp-amber: rgb(240, 138, 20);
    --bp-amber-hover: rgb(210, 118, 11);
    --bp-amber-hi: rgb(150, 80, 10);
    --bp-amber-deep: rgb(94, 49, 3);
    --bp-amber-surface: rgb(253, 239, 214);
    --bp-amber-border: rgb(240, 196, 122);
    --bp-amber-wash: rgba(240, 138, 20, 0.16);

    /* --- Green — confirmed / ready. UI only, never a world colour. --------- */
    --bp-green: rgb(12, 138, 82);
    --bp-green-hi: rgb(7, 107, 65);
    --bp-green-surface: rgb(220, 243, 231);
    --bp-green-border: rgb(152, 215, 185);

    /* --- Text -------------------------------------------------------------- */
    --bp-text: rgb(26, 33, 40);
    --bp-text-2: rgb(62, 72, 82);
    --bp-text-3: rgb(86, 96, 105);
    --bp-text-on-accent: rgb(27, 18, 6);
    /* The mirror of --bp-text-on-accent: light text for the few labels that draw straight
       onto --bp-table with no panel behind them. Values are the --bp-deck-0 and --bp-deck-2
       rungs. 7.13:1 and 5.54:1 on table. The on-panel text/text-2 step of 0.57 is
       unreachable here — 0.57 x 7.13 = 4.06:1, under the floor — so the secondary takes the
       rung with headroom and the type scale carries the hierarchy. Never use on a panel. */
    --bp-text-on-table: rgb(252, 249, 243);
    --bp-text-on-table-2: rgb(228, 221, 206);

    /* --- Lines, edges, scrims, focus --------------------------------------- */
    --bp-line-1: rgba(21, 26, 32, 0.16);
    --bp-line-2: rgba(21, 26, 32, 0.52);
    --bp-line-3: rgba(21, 26, 32, 0.78);
    --bp-edge-dark: rgba(21, 26, 32, 0.92);
    --bp-panel: rgba(242, 237, 226, 0.96);
    --bp-dock: rgba(228, 221, 206, 0.97);
    --bp-scrim: rgba(21, 26, 32, 0.66);
    --bp-focus-light: rgb(21, 26, 32);
    --bp-focus-dark: rgb(21, 26, 32);
    --bp-transparent: rgba(0, 0, 0, 0.00);

    /* --- Spacing — 4 px base ------------------------------------------- */
    --bp-s1: 4px;
    --bp-s2: 8px;
    --bp-s3: 12px;
    --bp-s4: 16px;
    --bp-s5: 24px;
    --bp-s6: 32px;
    --bp-s7: 48px;

    /* --- Shape — softened toward the Toy Shelf values ------------------- */
    --bp-r-sm: 4px;
    --bp-r-md: 8px;
    --bp-r-lg: 14px;
    --bp-bw-hair: 1px;
    --bp-bw: 2px;
    --bp-bw-rule: 3px;
    --bp-bw-focus: 3px;
    --bp-edge: 4px;

    /* --- Control sizes -------------------------------------------------- */
    --bp-target-sm: 32px;
    --bp-target: 44px;
    --bp-target-lg: 52px;
    --bp-target-phone: 56px;
    --bp-icon: 24px;
    --bp-icon-sm: 18px;
    --bp-icon-gap: 10px;
    --bp-scroll: 12px;

    /* --- Type scale ------------------------------------------------------ */
    --bp-type-hero: 56px;
    --bp-type-display: 34px;
    --bp-type-title: 22px;
    --bp-type-subtitle: 17px;
    --bp-type-body: 15px;
    --bp-type-body-sm: 13px;
    --bp-type-label: 12px;
    --bp-type-micro: 10px;
    --bp-type-num-lg: 26px;
    --bp-type-num: 18px;
    --bp-type-num-sm: 12px;

    /* --- Motion ----------------------------------------------------------- */
    --bp-t-instant: 80ms;
    --bp-t-fast: 120ms;
    --bp-t-base: 180ms;
    --bp-t-slow: 280ms;
    --bp-t-flash-in: 90ms;
    --bp-t-flash-out: 220ms;
    /* USS has no cubic-bezier(). These must stay keywords. See Palette §9.6. */
    --bp-ease-out: ease-out-circ;
    --bp-ease-in: ease-in-circ;
    --bp-ease-snap: ease-out-back;

    /* ======================================================================
       Legacy aliases. All 73 --toy-* names below have ZERO var() consumers
       anywhere in Assets/UI as of this writing — verify with:
         rg -o 'var\((--toy-[a-z0-9-]+)' Assets/UI -g '*.uss' -g '*.uxml'
       They are retained only because UIToolkitAssetSmokeTests asserts that
       four of them are still declared. Delete the block and those four
       assertions together, in one commit. Do not add a new --toy-* name.
       ====================================================================== */
    --toy-ink: var(--bp-ink);
    --toy-playmat: var(--bp-deck-1);
    --toy-playmat-raised: var(--bp-deck-2);
    --toy-playmat-hover: var(--bp-deck-3);
    --toy-playmat-pressed: var(--bp-deck-4);
    --toy-playmat-quiet: var(--bp-deck-1);
    --toy-cream: var(--bp-deck-0);
    --toy-cream-copy: var(--bp-text-2);
    --toy-cream-border: var(--bp-line-2);
    --toy-cream-wash: var(--bp-line-1);
    --toy-body-copy: var(--bp-text-2);
    --toy-dark-copy: var(--bp-text-on-accent);
    --toy-orange: var(--bp-amber);
    --toy-orange-hover: var(--bp-amber-hover);
    --toy-orange-pressed: var(--bp-amber-deep);
    --toy-orange-copy: var(--bp-amber-hi);
    --toy-teal: var(--bp-green);
    --toy-teal-hover: var(--bp-green-hi);
    --toy-teal-pressed: var(--bp-green-hi);
    --toy-sky: var(--bp-blue);
    --toy-tomato: var(--bp-red);
    --toy-tomato-hover: var(--bp-red-hi);
    --toy-tomato-pressed: var(--bp-red-deep);
    --toy-tomato-copy: var(--bp-red-hi);
    --toy-muted: var(--bp-text-3);
    --toy-muted-dark: var(--bp-deck-4);
    --toy-divider: var(--bp-line-2);
    --toy-disabled-surface: var(--bp-deck-4);
    --toy-disabled-border: var(--bp-line-1);
    --toy-shade: var(--bp-scrim);
    --toy-panel: var(--bp-panel);
    --toy-transparent: var(--bp-transparent);

    --toy-space-xs: var(--bp-s1);
    --toy-space-sm: var(--bp-s2);
    --toy-space-md: var(--bp-s3);
    --toy-space-lg: var(--bp-s5);
    --toy-space-xl: var(--bp-s6);
    --toy-space-control-x: var(--bp-s4);
    --toy-space-control-top: 14px;
    --toy-space-control-bottom: 14px;
    --toy-space-control-active-top: 15px;
    --toy-space-control-active-bottom: 13px;
    --toy-space-panel: var(--bp-s5);
    --toy-space-panel-compact: var(--bp-s4);
    --toy-space-modal: var(--bp-s6);
    --toy-space-modal-phone: var(--bp-s5);

    --toy-radius-sm: var(--bp-r-sm);
    --toy-radius-md: var(--bp-r-md);
    --toy-radius-lg: var(--bp-r-lg);

    --toy-target-status: 24px;
    --toy-target-compact: var(--bp-target);
    --toy-target: var(--bp-target-lg);
    --toy-target-phone: var(--bp-target-phone);
    --toy-icon-size: var(--bp-icon);
    --toy-icon-gap: var(--bp-icon-gap);
    --toy-scroll-size: var(--bp-scroll);

    --toy-stroke-thin: var(--bp-bw-hair);
    --toy-stroke: var(--bp-bw);
    --toy-depth-rest: var(--bp-edge);
    --toy-depth-pressed: var(--bp-bw);
    --toy-depth-panel: var(--bp-edge);
    --toy-depth-modal: var(--bp-edge);
    --toy-focus-width: var(--bp-bw-hair);
    --toy-focus-emphasis: var(--bp-bw-focus);

    --toy-type-display: var(--bp-type-display);
    --toy-type-title-lg: 28px;
    --toy-type-title: var(--bp-type-title);
    --toy-type-body-lg: 16px;
    --toy-type-body: var(--bp-type-body);
    --toy-type-label: var(--bp-type-label);
    --toy-type-caption: var(--bp-type-label);
    --toy-type-mono: var(--bp-type-num);

    --toy-motion-fast: var(--bp-t-fast);
}
```

---

## 11. Changelog — Night Range → FIELD DAY

### 11.1 Token names — the only breaking change

**Removed: `--bp-void`.** The name was an outright lie on a bright board and its three consumers all
want the new tabletop tone, not the darkest ink. Three call sites, all a one-word substitution:

| File | Line | Rule | Change to |
|---|---|---|---|
| `Assets/UI/Game/GameHUD.uss` | 841 | `.deployment-overlay` | `var(--bp-table)` |
| `Assets/UI/Join/JoinGame.uss` | 4 | `.join-screen` | `var(--bp-table)` |
| `Assets/UI/Join/JoinGame.uss` | 351 | `.join-stage` | `var(--bp-table)` |

Two C# doc comments also name it and should be updated for accuracy, though neither affects
behaviour: `Assets/Scripts/VFX/MatchEndFX.cs:27` and `Assets/Scripts/VFX/ScorchDecal.cs:5`. Both
should now read `--bp-shadow #3E4C55` and `--bp-ink #151A20` respectively.

**No other token was renamed or removed.** All 163 remaining names keep their spelling.

### 11.2 Tokens added — 17, all additive

`--bp-ink`, `--bp-table`, `--bp-edge`, `--bp-amber-hover`, `--bp-ground-a`, `--bp-ground-b`,
`--bp-ground-hill`, `--bp-ground-line`, `--bp-ground-paint`, `--bp-cover`, `--bp-cover-cap`,
`--bp-cover-plate`, `--bp-rail`, `--bp-sky`, `--bp-shadow`, `--bp-fog`, `--bp-fog-edge`.

Two of these — `--bp-ground-hill` and `--bp-cover-plate` — landed in the **second** revision, after
reconciliation against `Assets/Editor/ArenaBuilder.cs`, which also retuned the cover pair. See §11.7.

### 11.3 Rules that should change even though nothing breaks

Cases where a token still resolves but now resolves to the wrong thing for its job. None produce an
unstyled element, so all are safe to land in a follow-up commit.

#### Emphasis and fill

| File | Line | Current | Should be | Why |
|---|---|---|---|---|
| `GameHUD.uss` | 401 | `.hud-controls-button:active` → `--bp-deck-0` | `--bp-deck-3` | Pressed must darken. `--bp-deck-0` is now the lightest surface, so the button currently brightens on press. |
| `GameHUD.uss` | — | `.hud-controls-button:hover` → `--bp-deck-2` | `--bp-deck-3` | The button sits on `--bp-dock`, which is `--bp-deck-2` at 0.97. Hover therefore collapses to a 0.007 luminance delta and is invisible. |
| `BattlePlan.uss` | 213 | `.button--primary:hover` → `--bp-amber-hi` | `--bp-amber-hover` | `--bp-amber-hi` is tuned as a 5.22:1 text colour and is too dark for a hover fill. |

#### Recesses — depth does not invert

| File | Line | Rule | Should be | Renders today as |
|---|---|---|---|---|
| `GameHUD.uss` | 588, 744 | `.unit-card__portrait` → `--bp-deck-0` | `--bp-deck-2` | A 0.949 well inside a 0.855 card |
| `CharacterSelectionToybox.uss` | 223 | `.unit-option__portrait` → `--bp-deck-0` | `--bp-deck-2` | A 0.949 well inside a 0.849 option |
| `CharacterSelectionToybox.uss` | 423 | `.selected-slot__portrait` → `--bp-deck-0` | `--bp-deck-2` | 0.949 on 0.949 — completely flat |

**The rule these four share, which will recur every time someone adds a surface:**

> **Emphasis inverts. Depth does not.**
>
> Emphasis — hover, press, selection, disabled — moves *toward ink* on a light theme, and the ramp
> handles that correctly across 25 emphasis steps, 13 disabled surfaces and every text input.
> **"Recessed" is a depth cue, not an emphasis cue.** A well, a tray, an inset plate or a portrait
> socket is darker than the surface containing it on a light theme exactly as it was on a dark one,
> because it is shadowed rather than emphasised. Pick the recess value by stepping *down* the ramp
> from its container, never by reaching for `--bp-deck-0`.

`--bp-deck-0` is the *lightest* surface, which makes it right for a text input (a lit page you write
on) and wrong for a portrait socket (a hole you drop a piece into). Those two things wanted the same
token on a dark board and want opposite ends of the ramp on a light one.

These are fixed in the consumers rather than in the palette. Re-tuning `--bp-deck-0` to satisfy
three wells would break the 38 rules where the inversion is already correct.

#### The structural edge

**8 declarations** pair `border-bottom-width` with a heavier bottom border. **7 are structural card
edges** and move from `var(--bp-bw)` to `var(--bp-edge)`: `GameHUD.uss:475`,
`TacticalToybox.uss:25`, `TacticalToybox.uss:310`, `BattlePlan.uss:134`, `BattlePlan.uss:161`,
`BattlePlan.uss:448`, `CharacterSelectionToybox.uss:150`.

**The eighth, `JoinGame.uss:94`, must not change.** It pairs with `--bp-amber` rather than
`--bp-edge-dark` and is the active tab's accent underline, not a card edge. It stays at
`var(--bp-bw)` / 2px. An accent underline is a state indicator; thickening it to 4px would make it
compete with the real card edges around it.

### 11.4 Values changed — everything

Every one of the 62 colour tokens has a new value. There is no partial migration and no token
retained its Night Range value. The three non-colour changes are `--bp-r-sm` 2→4px,
`--bp-r-md` 4→8px and `--bp-r-lg` 8→14px.

### 11.5 Unchanged

Spacing scale, border widths (`--bp-bw-hair`, `--bp-bw`, `--bp-bw-rule`, `--bp-bw-focus`), all
control sizes, the entire type scale, and all six motion durations. The `--toy-*` legacy block keeps
all 73 names.

**The three easing tokens changed, but as a bug fix rather than a direction change.** They were
declared as `cubic-bezier(...)`, which USS cannot parse, so they were unusable throughout Night
Range. They are now keywords. See §9.6.

### 11.6 Section-by-section

| Section | Status |
|---|---|
| §0 Which number goes where | Extended: added Particle System and scene-YAML rows. No contradictions with the previous version. |
| §1 Surface & environment ramp | **Rewritten.** Ramp inverted, split into anchors / UI ramp / world. 15 world tokens are new. §1.4 (rendered space vs albedo) added in the second revision. |
| §2 Team blue | Values rewritten; structure and names identical. Cyan `#4CC7FF` → true blue `#0F5CB8`. |
| §3 Team red | Values rewritten; structure and names identical. `#FF4D5E` → `#E23B45`. |
| §4 Signal colours | Values rewritten. `--bp-amber-hover` added. Green darkened for light-board legibility. |
| §5 Text, lines, scrims, focus | **Inverted.** Text is now ink on paper. Line tokens are ink-alpha rather than paint-alpha. §5.1 focus decision is new. |
| §6 HDR emissive | **Rewritten.** Threshold 1.0 → 1.8, intensity capped at 0.5, and the table shrank from every FX element to seven. |
| §7 Contrast verification | **Fully recomputed.** All 43 pairings were dark-on-light and are now light-on-dark. §7.1–7.3 are new. |
| §8 Saturation budget | **New section.** Did not exist on Night Range. |
| §9 Non-colour tokens | Radii changed (2/4/8 → 4/8/14) and `--bp-edge` added. The three easing tokens changed from unparseable `cubic-bezier()` declarations to USS keywords — a bug fix, not a direction change. Everything else identical. Was §8. |
| §10 Paste-ready USS | **Rewritten.** Was §9. |
| §11 Changelog | **New section.** |
| Old §10 "What this file does not cover" | Folded into §0 and the ArtDirection cross-references. |

### 11.7 Second revision — reconciliation with `ArenaBuilder.cs`

The first FIELD DAY draft specified world colour from a colour picker. `Assets/Editor/ArenaBuilder.cs`
had already measured what those albedos actually render as, and the measurement invalidated part of
the spec. **The builder is correct and the palette moved to it, not the other way round.** Changes:

| Token | Was | Now | Why |
|---|---|---|---|
| `--bp-ground-a` | `#9FAAB0` | `#ABB6BB` | Swapped with `ground-b` so **A is the lighter** square, matching the builder's `Map_DeckA` / `Map_DeckB` ordering. Side benefit: blue-on-deck rises 2.73 → 3.13:1 and now passes. |
| `--bp-ground-b` | `#ABB6BB` | `#9FAAB0` | The other half of the swap. |
| `--bp-ground-hill` | — | `#8E999E` | **Added.** The hill was pointed at `ground-b`; the builder takes it a further rung down so contested-cell FX have somewhere to read. |
| `--bp-cover` | `#46525A` | `#66707A` | Raised to the builder's measured body value. |
| `--bp-cover-cap` | `#C6CED2` | `#5E6871` | **The substantive correction.** The cap was authored pale on the intuition that it catches the sun. It does — 1.42× the lit body face — so authoring it pale *as well* drove it above the deck and made cover the brightest object on a board whose thesis is that lightness is scarce. |
| `--bp-cover-plate` | — | `#59626A` | **Added.** The ID plate was pointed at `--bp-ground-paint`, a near-white, which put a bright chip on the darkest mass in frame. |
| `--bp-fog` | `#BFC1BD` | `#C5C7C3` | Nudged up 0.03 to hold the 1.20–1.35 band against the now-lighter cell A. Was 1.14; now 1.22. |

Every new value sits within **0.006 HSL-L** of the builder's neutral grey it replaces, so the
*ladder* the builder enforces is untouched and only the hue is added. `DeckMidToneMin/Max`
(0.62–0.70) and `DeckMaxSaturation` (0.12) both still pass: `ground-a` is L 0.702 — **at the
ceiling, not over it, and worth a tolerance comment in the builder** — `ground-b` L 0.657, with
saturation 11% and 10%.

Contrast rows recomputed: blue-on-deck 2.73 → **3.13** (now passes), red 1.79 → **2.05**, amber
1.06 → **1.21**, ink 7.37 → **8.45**, fog-edge 3.86 → **4.11**. The two cover rows were **deleted**
rather than recomputed, because an albedo contrast figure across an orientation boundary is
misleading; §1.4 verifies cover in rendered space instead.

The general lesson, now written up as §1.4: **world colour is authored in albedo and judged in
rendered space**, and a ladder that crosses an orientation boundary means nothing until it is
checked. This is a world-surface rule only; UI is unlit and unaffected.

### 11.8 Third revision — validator failures and the withdrawn transfer table

Two build-failing validators were pointed at this file and fired on six of its values. Fixed at
source rather than by widening the windows.

| Token | Was | Now | Why |
|---|---|---|---|
| `--bp-sky` | `#9CC6DC` | `#CAD1D4` | **47.8% HSL saturation at 12.1° from team blue**, against a 30% cap for anything within 25° of a team hue and a 12% cap for the backdrop class. It is the largest area in frame and it is a camera clear colour rather than a material, so a palette-only sweep missed it. Now 10.4%. |
| `--bp-ground-a` | `#ABB6BB` | `#A8B3B8` | HSL-L 0.702 against the builder's 0.62–0.70 deck window. Now 0.690. |
| `--bp-fog-edge` | `#535A60` | `#1E262E` | Composited at 0.85 alpha the old value reached only 2.69:1 against the fill. See §7.4. |

Contrast rows moved with the deck: blue 3.13 → **3.02**, red 2.05 → **1.98**, amber 1.21 → **1.17**,
ink 8.45 → **8.17**.

**§1.4's orientation transfer table is withdrawn and pending a probe.** The `1.000 / 0.705 / 0.237`
figures were transcribed from `ArenaBuilder.cs` constants that their own author has since confirmed
are not reproducible from any stated method. This document quoting them was never an independent
check. §1.4 is rewritten around the derivation, the two candidate readings, and a measured column to
be filled in the barrier. **The cover kit is explicitly not blocked** — its decisive step is between
two horizontal faces, so the disputed factor cancels.

**Two measurement instruments are now declared, because both had been left implicit and both have a
wrong default.** §7 is WCAG relative luminance, which linearises before weighting — not luma on
gamma-encoded values, which runs high in the midtones. §8 is HSL, not HSV; `Color.RGBToHSV` is the
obvious tool and it inverts results, reading `--bp-ink` 13 points too saturated and the old
`--bp-sky` 19 points too clean.

Both citations inside the pasted `:root` block are now document-qualified (`Palette §9.6`,
`Palette §1.4`). An unqualified `§9.6` lands in a stylesheet reading as `ArtDirection` §9.6, which
is an unrelated section — a number that resolves cleanly to the wrong content is worse than one that
resolves to nothing, because it reads as verified.

### 11.9 Fourth revision — contour width leaves this file

`ArtDirection.md` §17.4 withdrew the "2 screen pixel" world contour minimum. Three consequences here:

- **§7's restatement of the contour law no longer carries a width.** Width is a screen-space
  coverage question, it depends on the camera and the quality tier, and it belongs in one place:
  `ArtDirection.md` §4.3.1. This file keeps the token, the ratio and the reasoning for why contours
  exist at all. Do not reintroduce a number here.
- **§7's 8.17:1 row is a full-coverage figure**, now labelled as one. A contour at §4.3.1's moving
  floor resolves at **3.17:1** against cell A and 3.07:1 against cell B, because it covers 78% of a
  pixel rather than all of it. Both clear the 3.0 threshold; neither is 8.17. The unqualified row
  had been read as the delivered contrast of every contour in the game, which it never was.
- **§13's `--bp-bw` no longer claims "minimum FX contour width."** That line is where the world rule
  came from: a UI border token, 0.7% of a card, applied to a bullet four pixels across. `--bp-bw` is
  UI only.

`--bp-ink` itself does not move, and neither does any other token. This revision changes no colour.

### 11.10 Fifth revision — two rules stated properly

Both of these existed as correct practice that the written rule permitted misreading. No token moves
and no ratio changes.

- **§7.3 — the contour law is bed-dependent.** A drawn edge carries the contrast; which colour
  carries it depends on the bed. Ink on the light board, paper on the dark table. §4.3 of
  `ArtDirection.md` reads as though the contour is `--bp-ink` by definition because every bed it was
  written against is light, and the ban on ink-over-table then reads as an exception to the law
  rather than an instance of it. Forced by arithmetic, not preference: a 3.0:1 pass on the dark side
  of `--bp-table` needs negative luminance, so pure black at 2.80:1 is the ceiling for the whole dark
  family. Recorded because the alternative fix — lightening the bed — would break §5.5's "the menus
  are the tabletop", which is load-bearing.
- **§8 — the saturation band follows the material's screen area, not its host's.** A small
  high-saturation detail on a large object stays in the small-area band. The previous wording let a
  52% amber pad on a hero-scale figure be read into the 20% panel band because the *figure* was that
  large, which would force every close-up prop to desaturate its details into mud. The budget exists
  to keep large flat areas quiet.
