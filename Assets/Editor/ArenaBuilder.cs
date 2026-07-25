using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the arena for <c>Assets/Scenes/Game.unity</c> from the numbers in
/// <c>docs/design/ArtDirection.md</c> §7. Every dimension below is a named constant so the board
/// is reviewable as a diff instead of as a pile of hand-placed transforms, and the whole thing is
/// idempotent: it clears what it made last time before it makes it again.
///
/// Grid geometry is gameplay. The three values this file must never contradict are
/// <c>GameLoop.cellSize</c> (2.7), <c>GridSystem.ColumnCount/RowCount</c> (15 × 10), and the
/// 2.7 × 2.0 × 2.7 wall envelope with its convex octagonal collider. They are asserted at the top
/// of every build.
/// </summary>
public static class ArenaBuilder
{
    // ---------------------------------------------------------------- gameplay-derived geometry
    internal const float Cell = 2.7f;
    private const int Columns = 15;
    private const int Rows = 10;

    private const float BoardMinX = -Cell * 0.5f;                    // -1.35
    private const float BoardMaxX = (Columns - 0.5f) * Cell;         //  39.15
    private const float BoardMinZ = -Cell * 0.5f;                    // -1.35
    private const float BoardMaxZ = (Rows - 0.5f) * Cell;            //  25.65
    private const float BoardCentreX = (Columns - 1) * Cell * 0.5f;  //  18.9
    private const float BoardCentreZ = (Rows - 1) * Cell * 0.5f;     //  12.15

    // §7.8 Y-order contract. Nothing this builder places may pick a Y outside this list.
    private const float YCellSeparator = 0.000f;
    private const float YDeckDecal = 0.020f;
    // The ink keyline sits 0.004 over the paint it edges, which is the smallest gap that
    // cannot z-fight from a 24 m camera and is still four hundredths of a millimetre of
    // board — invisible as a step, decisive as a draw order.
    private const float YDeckKeyline = 0.024f;
    private const float YHillGroove = 0.030f;
    private const float HillRecess = 0.050f;

    // §7.3 cover kit envelope — all four variants share it exactly.
    private const float CoverHeight = 2.0f;
    private const float CoverBodyTop = 1.94f;   // the 0.12 cap occupies 1.94 → 2.06, centred on 2.0
    private const float CoverChamfer = 0.06f;
    private const int CoverTriBudget = 240;

    // §7.4 rail and backdrop.
    private const float RailWidth = 0.60f;
    private const float RailHeight = 0.40f;
    private const float RailChamfer = 0.08f;
    private const float RailPinstripe = 0.03f;
    private const float CornerPostSize = 0.50f;
    private const float CornerPostHeight = 1.20f;
    private const float CornerPostBandY = 1.05f;
    private const float CornerPostBandHeight = 0.05f;
    // How far an emissive band stands out of the post it wraps. §7.4/§7.5 give the bands a width
    // narrower than their posts, which would bury them inside the solid; a wrapping ring is the
    // only reading of "a 0.40 x 0.05 band at y 1.05" that is actually visible.
    private const float BandProud = 0.012f;
    private const float BackdropBaseY = -0.8f;
    private const int BackdropBlockBudget = 40;
    private const int BackdropSeed = 20260725;

    // §7.5 hill marker.
    private const float HillPostSize = 0.25f;
    private const float HillPostHeight = 1.60f;
    private const float HillPostBandY = 1.40f;
    private const float HillPostBandHeight = 0.05f;
    private const float HillBorderWidth = 0.08f;
    private const float HillGrooveWidth = 0.10f;

    // §7.2 painted markings.
    private const float HazardBandWidth = 0.35f;
    // Widened from 0.10. At 0.10 the rank line is 3.7 px and §4.3.1's cap — a contour may
    // not exceed a quarter of its host's narrowest dimension — puts its maximum contour at
    // 0.025, under the 0.043 static floor. A shape in that pinch cannot carry a contour at
    // all, and this one has to: it is a saturated team colour on the deck, where red
    // reaches 1.98:1 unaided, so ink is the only thing separating it from the ground.
    // Widening the host is the first remedy §4.3.1 names, and 0.20 clears the cap with 16%
    // to spare where 0.18 would clear it by 5%. The threshold also earns the weight — it is
    // a more important line than the 0.12 cell separator and should not read lighter.
    private const float RankLineWidth = 0.20f;
    private const float LaneArrowSize = 1.60f;
    private const float CornerTickSize = 1.20f;
    // The two mask-drawn markings do not fill their quads, and §4.3.1's floor and cap are both
    // measured on the narrowest dimension of what is actually drawn. For the chevron that is the
    // band thickness, ChevronMask's front minus back; for the tick, CornerTickMask's stroke.
    private const float ChevronBandWidth = 0.28f * LaneArrowSize;
    private const float TickStrokeWidth = 0.05f * CornerTickSize;

    // ---- the ink contour law, in screen pixels (§4.3, §4.3.1) --------------------
    // Every painted deck marking carries a --bp-ink keyline on its outer edge, and the fill
    // then keeps whatever value the design wants. Five markings were authored as flat
    // washes carrying their own contrast and none reach 3.0:1 against the deck: the spawn
    // rank washes at 1.17 and 1.15, the hill pad perimeter at 1.15, its paint inset at 1.84,
    // and lane paint at 1.60. The hill is the one that matters most — the King of the Hill
    // boundary is the single most important edge on the board and it was drawn at 1.15:1.
    // Inking the edge takes it to 8.17:1 and costs the fill nothing.
    //
    // A CONTOUR'S WIDTH IS SET BY THE DISPLAY, NOT BY ITS HOST. The old "at least 2 screen
    // pixels" was --bp-bw, the UI emphasised-border token, and no world contour this project
    // ever specified met it. It is gone from world space. What replaces it is a coverage
    // floor: sub-pixel ink darkens a pixel in proportion to the fraction it covers, and
    // α ≥ 0.78 is where that clears 3.0:1 on both checker squares and still resolves darker
    // than a blue fill, blue being the one team colour that would otherwise out-darken its
    // own outline.
    //
    // The floor splits in two on whether the shape moves, and that is the substance of the
    // rule rather than a detail. Coverage is conserved under sub-pixel position but not
    // concentrated: 0.78 px of ink inside one pixel is 3.07:1, and the same rim straddling a
    // boundary is two pixels at ~1.5:1. A projectile visits every phase in a few frames and
    // the eye integrates; the camera does not move during a match, so a deck marking is
    // stuck at whatever phase it landed on for the whole game and has to survive the worst
    // one. Deck markings are static and take the higher floor.
    private const float MovingContourFloorPx = 0.78f;
    private const float StaticContourFloorPx = 1.56f;
    // §4.3.1's screen scale, published as a formula so a new plane does not need re-deriving:
    // s = H·sinθ / (2·(h−y)·tan(FOV/2)) = 895 / (24.4 − y) px per world unit at 1080p, for
    // the §7.1 camera. Everything this file draws is on the deck, y = 0, so 36.7 px/unit.
    private const float ScreenScaleNumerator = 895f;
    private const float CameraHeight = 24.4f;
    // A contour may never exceed a quarter of its host's narrowest silhouette dimension per
    // side, nor 45% of the contoured silhouette's area. Where the cap falls below the floor
    // the shape cannot carry a contour and must be widened or authored in a value that
    // clears 3.0:1 unaided — never thinned to fit.
    private const float ContourCapFraction = 0.25f;
    private const float ContourInkAreaMax = 0.45f;
    // 1.56 px / 36.7 px per unit = 0.0425 on the deck plane. Authored at 0.043, which is the
    // published figure and 1% of margin over the floor.
    private const float KeylineWidth = 0.043f;
    // Non-text separation, per palette §7. The keyline is the only thing carrying it, so the
    // check is on the keyline rather than on any fill.
    private const float KeylineMinContrast = 3.0f;

    private static float PixelsPerUnit(float planeY) => ScreenScaleNumerator / (CameraHeight - planeY);
    private static readonly int[] LaneArrowColumns = { 2, 7, 12 };
    private static readonly int[] TickColumns = { 0, 7, 14 };
    private static readonly int[] TickRows = { 0, 4, 9 };

    // ================================================================================
    // LOOK VALUES — every colour, light and post number the builder writes lives here
    // and nowhere else, so re-grading the board is one edit to this block instead of a
    // hunt through the file. Hexes are display sRGB, exactly as an art bible states
    // them and exactly what a material file stores — the shipped map materials hold
    // round picker values like 0.58 / 0.69 / 0.75, so the conversion to linear happens
    // at bind time and nothing here needs to pre-convert.
    //
    // The board is a bright, daylit diorama. The scene already ships that lighting and
    // always did: a real directional sun at 1.25 with soft shadows at strength 0.72,
    // over a gradient ambient. That rig is correct and is NOT rebuilt here. The builder
    // only aims the sun and asserts the rest still matches what is recorded below.
    //
    // The governing constraint is the deck: at or below 12% saturation with a mid-tone
    // of display sRGB 0.62-0.70. On a bright board darkness is the abundant contrast
    // resource and lightness is scarce, so the ground stays quiet and the saturation
    // lives in props, characters and signals. DeckMidToneMin/Max enforce it at build
    // time rather than leaving it to reviewer memory.
    //
    // Greys here are deliberately hue-neutral placeholders on the correct value ladder,
    // pending the rewritten palette. Tinting them is a one-line change per row; moving
    // them off the ladder is not, so the ladder is the part to preserve.
    // ================================================================================

    // Deliberately not const: a compile-time constant would make one branch unreachable code.
    private static readonly bool ApplyLookPass = true;

    // ---- sun ------------------------------------------------------------------
    // Steep but not vertical, and aimed along +X.
    //
    // RULED: this overrides ArtDirection §7.7, which specifies (50, -30, 0) and calls the
    // shipped rotation "preserved exactly". It was not preserved — the doc read the
    // shipped scene rather than evaluating this change — and the azimuth is the whole
    // disagreement, the elevations being two degrees apart. On-axis wins because the
    // reason below is competitive fairness rather than an aesthetic preference, and
    // fairness outranks a look. Do not re-derive an off-axis angle.
    //
    // Azimuth 90 puts the sun square on the board's long axis. The camera flips 180 in
    // yaw between seats (GameLoop.cameraPositions), so any azimuth off this axis throws
    // cover shadows toward one player and away from the other — one seat reads its
    // approach cells over clean deck, the other reads them through shadow. On the axis
    // both seats get the same shadow, sideways in screen space, which is the only
    // choice that is actually fair.
    //
    // Elevation 52 sets shadow length at 2.0 / tan(52) = 1.56 m, or 0.58 of a 2.7 cell:
    // long enough to read "full height" at a glance, short enough that it never fills
    // the neighbouring cell and never reads as occupancy. It also keeps the cap plane
    // at cos(38) = 0.79 of full irradiance, so the top of a cover piece stays visibly
    // lit even at a dark albedo, which is what stops the kit reading as a hole.
    private const float SunElevation = 52f;
    private const float SunAzimuth = 90f;
    internal static readonly Vector3 SunEuler = new(SunElevation, SunAzimuth, 0f);

    // Shadow settings on the sun. Cast shadows are the primary sight-blocker cue on a
    // bright board, so these are load-bearing, not polish.
    internal const float SunShadowStrength = 0.72f;
    // internal so MenuSceneBuilder can light the menu scenes from these rather than from a
    // second copy of the numbers. A duplicated literal is how the clear colour drifted.
    internal const float SunShadowBias = 0.05f;
    internal const float SunShadowNormalBias = 0.4f;

    // ---- rendered-space transfer ------------------------------------------------
    // DERIVED CONSTRAINT, AND PROVISIONAL. Authored albedo is not what the player sees,
    // and on this board the gap between the two is large enough to erase a value ladder.
    //
    // The consequence is not intuitive and it has already produced one bug here: a cover
    // cap authored 0.14 sRGB darker than the body rendered within 0.01 of it, because the
    // orientation change cancelled the albedo step almost exactly. Any albedo ladder that
    // crosses an orientation boundary is worth roughly nothing until it is checked in
    // rendered space. Anything added to this board later — props, decals, characters —
    // must go through RenderedValue before its values are trusted. That much is settled.
    //
    // ⚠ THE THREE NUMBERS THEMSELVES ARE WITHDRAWN AS PUBLISHED VALUES (palette §1.4).
    // They were quoted into the art bible as though measured; they are not reproducible
    // from any method this file can state, and they sit between the two candidate models
    // §1.4.2 derives rather than matching either. They are kept here, unchanged, only so
    // the ladder checks below keep running against a fixed reference while the question is
    // open — every check that uses them compares two values through the SAME factor, so a
    // wrong factor cannot flip a verdict on its own. 'Battle Plan → Art → Measure
    // Irradiance Transfer' settles it: a side-on reading near 0.29 means Unity reads the
    // gradient ambient as linear, near 0.16 means sRGB. Replace these three, and delete
    // this paragraph, with what the probe reports. Do not author a world value against
    // them in the meantime.
    private const float FaceHorizontal = 1.000f;
    private const float FaceVerticalLit = 0.705f;
    private const float FaceVerticalShaded = 0.237f;

    // ---- recorded environment (asserted, never overwritten) ---------------------
    // What Game.unity ships with and what the user likes. The builder compares against
    // these and warns on drift instead of writing them, so a build can never flatten
    // the lighting the direction is built on.
    internal const float RecordedSunIntensity = 1.25f;
    internal static readonly Color RecordedSunColour = new(1f, 0.97f, 0.91f, 1f);
    internal static readonly Color RecordedAmbientSky = new(0.72f, 0.84f, 0.91f, 1f);
    internal static readonly Color RecordedAmbientEquator = new(0.43f, 0.61f, 0.7f, 1f);
    internal static readonly Color RecordedAmbientGround = new(0.2f, 0.29f, 0.35f, 1f);
    internal const float RecordedAmbientIntensity = 0.78f;
    internal const float RecordedReflectionIntensity = 0.6f;
    // §7.5: Clear Flags = Solid Color, background --bp-sky, no skybox.
    //
    // Was #9CC6DC, which the hue guard below fails: 47.8% HSL saturation at 12.1 degrees
    // from team blue, against a 30% cap inside the guard band and a 12% cap for the
    // backdrop class. It is the largest area in the frame and it is a camera clear colour
    // rather than a material, so a palette-only sweep never looked at it. Palette §11.8
    // retuned it to 10.4%.
    private const string SkyHex = "#CAD1D4";
    // The camera clear colour is --bp-sky and there is deliberately no second literal for
    // it here. An earlier revision carried a recorded (0.55, 0.74, 0.82) and asserted that
    // it *was* --bp-sky; it never equalled any value the token has held, and a number that
    // resolves cleanly to the wrong thing is worse than one that resolves to nothing
    // (ArtDirection §17.3, item 5).
    //
    // THE BUILDER NOW OWNS THIS VALUE RATHER THAN ASSERTING IT, and the change is the
    // point rather than a convenience. The assert-don't-write rule protects the *recorded
    // environment* — sun intensity, sun colour, the gradient ambient — because those were
    // tuned by eye, nothing else states them, and a build that flattened them would
    // destroy the thing the direction is built on. The clear colour is not that kind of
    // value: it is a palette token, the palette is the declared single source of truth for
    // colour, and this file already derives it from SkyHex. So there was never a hand-tuned
    // number here to protect — the assert was protecting a stale literal from its own
    // token, which is exactly how the scene sat at the retired #8CBDD1 through two palette
    // revisions while the builder warned about it every run and nobody acted on the
    // warning. A warning that fires forever is a value with no owner.
    private static Color ClearColour => Hex(SkyHex);
    private const float EnvironmentDriftTolerance = 0.02f;

    // ---- deck constraint --------------------------------------------------------
    // The window is measured on relative luminance re-encoded to display sRGB, not on
    // HSV value. The two disagree by up to 0.04 on a tinted grey, which is a third of the
    // whole window, and luminance is the one that predicts what the ramp actually has
    // room above and below. Under it --bp-ground-a lands at 0.695 and --bp-ground-b at
    // 0.660. Note the palette quotes HSL-L for the same pair (0.690 / 0.657); the two
    // instruments agree to 0.005 on a grey this neutral, but they are not the same
    // measure and only the one below is what this window is checked against.
    private const float DeckMidToneMin = 0.62f;
    private const float DeckMidToneMax = 0.70f;
    private const float DeckMaxSaturation = 0.12f;

    // ---- the hue law (ArtBible-Palette §8, enforcement rule 1) --------------------
    // No environment or prop material may sit within 25 degrees of a team hue above 30%
    // saturation. This is what stops a colourful board from eating the team contract:
    // blue means yours and red means theirs, and neither can mean that if the scenery is
    // already speaking in those hues. Rows that are supposed to be team-coloured declare
    // it via MatSpec.TeamHue rather than being quietly skipped.
    private const float TeamHueBlue = 212.7f;
    private const float TeamHueRed = 356.4f;
    private const float TeamHueGuard = 25f;
    private const float TeamHueMaxSaturation = 0.30f;

    // ---- material palette -------------------------------------------------------
    /// <summary>
    /// One row per material the builder assigns. <c>Hex</c> is display sRGB.
    /// </summary>
    private readonly struct MatSpec
    {
        public readonly string Name;
        public readonly string Hex;
        public readonly string Token;         // the ArtBible-Palette name this value comes from
        public readonly float Alpha;
        public readonly float Metallic;
        public readonly float Smoothness;
        public readonly string EmissionHex;   // null for non-emissive
        public readonly float EmissionGain;
        /// <summary>
        /// Set only where an element is *supposed* to carry a team hue — spawn ranks, rank lines,
        /// the hill's control band. Everything else is held away from the team hues by §8's
        /// enforcement rule, and exempting a row is a deliberate act rather than a convenience.
        /// </summary>
        public readonly bool TeamHue;

        public MatSpec(string name, string token, string hex, float metallic, float smoothness,
            float alpha = 1f, string emissionHex = null, float emissionGain = 0f,
            bool teamHue = false)
        {
            Name = name; Token = token; Hex = hex; Alpha = alpha; Metallic = metallic;
            Smoothness = smoothness; EmissionHex = emissionHex; EmissionGain = emissionGain;
            TeamHue = teamHue;
        }
    }

    // Smoothness stays low across the board. A bright key over a glossy deck produces
    // specular that the bloom then amplifies, and hazing the image costs every effect
    // its contrast — the deck is the one surface that must never sparkle.
    // FIELD DAY. Every value below is a token from ArtBible-Palette §1.3, and the token
    // name travels with it so a re-grade is a lookup rather than an inference. Nothing in
    // the arena is metallic (ArtDirection §7.3): on a bright board a specular highlight is
    // indistinguishable from an effect, and that one rule removes more accidental ugliness
    // than any other in the section.
    private static readonly MatSpec[] MapPalette =
    {
        // Deck. A retarget of the shipped Map_FloorDay pair rather than new assets, which
        // is the whole reason the board still reads as the one the user liked.
        //
        // A is the LIGHTER square and B the darker partner, matching palette §1.3. The two
        // were the wrong way round here, which put B at 0.706 display sRGB, outside the
        // mid-tone window below. --bp-ground-a also moved (#ABB6BB -> #A8B3B8) in palette
        // §11.8 for the same reason, and lands at 0.695: at the top of the window rather
        // than over it.
        new("Map_DeckA",        "--bp-ground-a",     "#A8B3B8", 0.00f, 0.12f),
        new("Map_DeckB",        "--bp-ground-b",     "#9FAAB0", 0.00f, 0.12f),
        // §7.6: the pad goes a rung below the checker, not between its two squares. A hill
        // painted in ground-b is indistinguishable from half the board, and this is the one
        // area of deck that has to still read under a pile of contested-cell FX. The token
        // exists for exactly this row (palette §11.7).
        new("Map_DeckHill",     "--bp-ground-hill",  "#8E999E", 0.00f, 0.12f),
        // §7.5 spawn ranks: the team wash at 0.16 over the deck. Deliberately team-hued.
        new("Map_DeckRankBlue", "--bp-blue-wash",    "#0F5CB8", 0.00f, 0.12f, 0.16f, teamHue: true),
        new("Map_DeckRankRed",  "--bp-red-wash",     "#E23B45", 0.00f, 0.12f, 0.16f, teamHue: true),

        // §7.3 separator, drawn 0.12 wide. Darker than the fill: on a light board the line
        // is a groove, which is both cheaper in headroom and more legible than a highlight.
        new("Map_GridLine",     "--bp-ground-line",  "#6E7A80", 0.00f, 0.05f),

        // §7.4 cover. Note this ladder runs the opposite way to the one Night Range needed:
        // a light cap over a dark body. That is the correct direction here and it is worth
        // saying why, because the reasoning is not the same as the authored ratio suggests.
        // A horizontal face collects materially more irradiance than a vertical one under
        // this sun, so lighting and albedo push the same way here instead of cancelling.
        // Measured in rendered space these three hexes give deck 0.695, cap 0.802, lit body
        // 0.265 and shaded body 0.148 — four separated steps, the smallest 0.107, which is
        // what makes a 2.0 m blocker read as solid and full height from a 73-degree camera.
        //
        // ⚠ THESE DIVERGE FROM PALETTE §1.3 AND THE DIVERGENCE IS DELIBERATE, PENDING A
        // RULING. §1.3 now lists --bp-cover #66707A, --bp-cover-cap #5E6871 and
        // --bp-cover-plate #59626A. Substituting that trio here collapses the cap-to-lit-
        // body step from 0.537 to 0.035 and fails ValidateCoverLadder below — the exact
        // failure the check was written for. Palette §1.4.5 and ArtDirection §7.4 both
        // quote the ladder THIS file produces as the measurement that settles the cover
        // question and both state that cover is not blocked, so the values are held here
        // and the reconciliation is an art-director call, not a silent copy. Do not adopt
        // part of the trio: --bp-cover-plate is darker than --bp-cover only in §1.3's
        // pairing, and against the body below it would invert the recess.
        new("Map_Cover",        "--bp-cover",        "#46525A", 0.00f, 0.18f),
        new("Map_CoverCap",     "--bp-cover-cap",    "#C6CED2", 0.00f, 0.22f),
        new("Map_CoverPlate",   "--bp-ground-paint", "#E8E2D4", 0.00f, 0.18f),

        // §7.5 rail: a dark warm frame against the pale cool board. This is the picture
        // frame, and it is what makes the diorama read as an object rather than a viewport.
        new("Map_Rail",         "--bp-rail",         "#3A342C", 0.00f, 0.20f),
        new("Map_RailLine",     "--bp-ink",          "#151A20", 0.00f, 0.15f),
        // "--bp-sky darkened two rungs" (§7.5). The rung is 0.877 applied to the sRGB
        // components, twice: #CAD1D4 x 0.877^2 = #9BA1A3. Stating the space matters, because
        // the same factor in linear gives #B4BABD and a backdrop lighter than the deck.
        //
        // WHAT 0.877 IS NOT: an earlier comment here called it "the UI ramp's mean per-rung
        // luminance factor". It is not reproducible from that ramp by any measure — the six
        // deck rungs average 0.81 in sRGB and 0.66 in linear. It is the factor this backdrop
        // was authored at and the value the user has approved on screen; that is its whole
        // provenance and it should not be quoted as a derivation from anything else.
        new("Map_Backdrop",     "--bp-sky (-2)",     "#9BA1A3", 0.00f, 0.05f),
        // The tabletop the diorama sits on, per §4.1's three-material split. This used to
        // be a neutral void; naming it as the table is what makes the board an object.
        new("Map_Void",         "--bp-table",        "#5E5346", 0.00f, 0.08f),

        // §7.3 painted markings. Light paint on a light deck, which only works because
        // every marking is ink-edged — the contour carries the contrast, the fill the
        // identity. §4.3.
        new("Map_Paint",        "--bp-ground-paint", "#E8E2D4", 0.00f, 0.02f, 0.75f),
        new("Map_RankLineBlue", "--bp-blue",         "#0F5CB8", 0.00f, 0.10f, teamHue: true),
        new("Map_RankLineRed",  "--bp-red",          "#E23B45", 0.00f, 0.10f, teamHue: true),

        // §8's one sanctioned high-chroma environment material: small, static, always
        // paired with ink, and at hue 39 it is signal-adjacent warm rather than team.
        new("Map_PaintHazard",  "--bp-paint-hazard", "#E39A12", 0.00f, 0.12f),
        // §7.6's control band, and §11.3 kills the emission it used to carry. On a bright
        // board an accent below the 1.8 bloom threshold only looks washed out; it reads by
        // saturation and by its ink contour instead.
        new("Map_HillPostBand", "--bp-blue",         "#0F5CB8", 0.00f, 0.20f, teamHue: true),
    };

    // Deck rows the mid-tone window applies to. The hill and rank cells are deliberately
    // off the mid-tone and are checked for saturation only.
    private static readonly string[] DeckMidToneMaterials = { "Map_DeckA", "Map_DeckB" };

    // ---- character palette ------------------------------------------------------
    // FIELD DAY §11.2. The figure is now darker than its ground, where on Night Range it
    // was lighter, and both of the things that decide that push the same way: the units
    // are authored dark, and a unit is mostly vertical faces while the deck under it is
    // horizontal and takes about 2.4x the irradiance. Unit-versus-ground separation is
    // therefore free here, which is the opposite of the situation this file was solving
    // a pass ago.
    //
    // §11.2 states its bands as albedo luminance in LINEAR, which is the space the
    // validator below works in — A is 0.10-0.24 and B is 0.02-0.06.
    //
    // Accent is not a fourth band. It is the classification §11.2 gives a material that is
    // too small to be a body mass: exempt from every band window, and governed instead by
    // the deck-clearance rule. C is the narrower case of an accent that separates by chroma
    // (team indicators, the one non-team detail); Accent is the general one, and it is what
    // makes skin legal at 0.300 without repainting a face to a uniform value.
    private enum Band { A, B, C, Accent }

    private readonly struct CharSpec
    {
        public readonly string Path;
        public readonly string Hex;
        public readonly Band Band;
        /// <summary>
        /// The largest share of any one figure's silhouette this material covers, which is
        /// the quantity §11.2's 15% rule is stated in. Upper bound across the five units
        /// rather than a per-unit figure: a material that is a body mass on any figure has
        /// to be authored as one, and a material that is small on every figure is an accent
        /// everywhere.
        ///
        /// These are DECLARED, not measured. 'Battle Plan → Art → Capture Silhouette Sheet'
        /// is where a measured pass would come from, and nothing here depends on the exact
        /// number — only on which side of <see cref="BandMassSilhouetteShare"/> it falls, so
        /// a corrected share re-classifies one row without touching the rule.
        /// </summary>
        public readonly float SilhouetteShare;
        public readonly float Metallic;
        public readonly float Smoothness;
        public readonly bool SpecularHighlights;
        public readonly Color Emission;
        public readonly bool Provisional;

        public CharSpec(string path, string hex, Band band, float share, float metallic = 0f,
            float smoothness = 0.12f, bool specularHighlights = false,
            Color emission = default, bool provisional = false)
        {
            Path = path; Hex = hex; Band = band; SilhouetteShare = share; Metallic = metallic;
            Smoothness = smoothness; SpecularHighlights = specularHighlights;
            Emission = emission; Provisional = provisional;
        }
    }

    // §11.1 shading law: metallic 0, smoothness 0.12, specular off, environment
    // reflections off, everywhere. The one exception is the weapon material, which is
    // allowed a highlight because catching the light off a barrel is how a player reads
    // unit facing from a 73-degree camera. That exception matters more here than it did
    // on the dark board: the skybox now feeds reflections at 0.6, so any character
    // surface left reflective picks up a blue sky wash and the figure stops reading as
    // painted matte.
    // Four values below are §11.2's third-revision corrections, and they are stated there
    // as target relative luminances rather than as hexes. Each was derived the same way:
    // scale the material's own linear triple by a single factor until its WCAG relative
    // luminance hits the target, then re-encode. One factor for all three channels leaves
    // hue and linear chroma ratios untouched, which is what "darker, same colour" means —
    // holding HSL saturation instead would have exploded the chroma of the near-whites the
    // moment their lightness left the extremes.
    private static readonly CharSpec[] CharacterPalette =
    {
        // Band B — boots, gloves, harness, hair, weapon.
        // Black: 0.017 -> 0.020, up to the band floor. §11.2 prints #23272D, which measures
        // 0.019978 and lands 0.000022 UNDER the floor it was raised to reach; one step on
        // blue — the channel 8-bit rounding cut hardest, at -0.41 — puts it at 0.020056.
        new("Characters/Char_Black",          "#23272E", Band.B, 0.18f),
        new("Characters/Char_CoatNavy",       "#2C3849", Band.B, 0.34f),
        // §11.1's one sanctioned exception, and a reduction from Night Range's 0.65
        // metallic: under a warm sun on a bright board 0.65 produced a mirror-bright
        // weapon that competed with the effects. A weapon is the only thing on a
        // character allowed a highlight, and that highlight is how facing reads at 73°.
        new("Characters/Char_Gunmetal",       "#33383F", Band.B, 0.10f, 0.35f, 0.30f, true),
        new("Characters/Char_HairBrown",      "#4A3524", Band.B, 0.05f),

        // Band A — the dominant surface.
        new("Characters/Char_OliveFatigues",  "#5C6650", Band.A, 0.45f),
        new("Characters/Char_JumpsuitOrange", "#8A6C42", Band.A, 0.40f),
        new("Characters/Char_KhakiGear",      "#877C63", Band.A, 0.30f),
        // Cloak: 0.439 -> 0.199. It was sitting 0.001 from the deck's own value, which is a
        // cloak that disappears. The derivation lands exactly on §11.2's printed #757C85.
        new("Characters/Char_CloakGray",      "#757C85", Band.A, 0.42f),
        // White: 0.820 -> 0.241, and the name is a ROLE now, not a colour — it is the
        // lightest value a body mass may take (§11.2). It was rendering 0.38 above the deck,
        // which inverts the read §11.5 calls the entire read. §11.2 prints #84878A; that
        // measures 0.240685 and breaches band A's own 0.24 ceiling by 0.0007, purely from
        // 8-bit rounding — red and blue both rounded up ~0.49 of a step. One step down on
        // red, the channel with the smallest luminance weight, gives 0.239882: at the
        // ceiling rather than over it, and one step cooler, which is the right direction
        // for a cool grey. A genuinely white element is still available as an ACCENT under
        // the 15% rule — a collar or a helmet stripe at 0.82 reads as a highlight where a
        // whole torso at 0.82 reads as a hole.
        new("Characters/Char_White",          "#83878A", Band.A, 0.38f),

        // Accents — under 15% of the silhouette, so exempt from every band window and
        // governed by the deck-clearance rule instead.
        // Skin: 0.423 -> 0.300, clearing the deck by 0.140. This is the row the 15% rule
        // exists for: at Band A's 0.24 ceiling a face has to be repainted to a uniform
        // value, and a face is not a body mass. Lands exactly on §11.2's printed #B58D6C.
        new("Characters/Char_Skin",           "#B58D6C", Band.Accent, 0.06f, 0f, 0.14f),

        // Band C — accents that separate by chroma rather than by value.
        new("Characters/Char_TealAccent",     "#3C7A82", Band.C, 0.04f),
        // §11.2's non-team accent rule: shares a hue family with the act-now orange and
        // stays legal only by sitting well below the signal's saturation and never being
        // emissive. A bright saturated warm patch on a unit is read as a dodge alert.
        new("Characters/Char_AmberPads",      "#A87F35", Band.C, 0.05f),
        // Driven by the team materials at runtime — Unit.SetTeamIndicators writes one of
        // teamMaterials onto every child tagged TeamIndicatorProp. This value is only
        // what the prefab shows before a team is assigned.
        new("Characters/Char_TeamBase",       "#6B6E71", Band.C, 0.08f),

        // §11.3: both non-emissive, smoothness 0.12. On a bright board an accent does not
        // need to glow to be found, and an emissive accent below the 1.8 bloom threshold
        // only looks washed out. The accent reads by saturation and by its ink contour.
        new("Teams/TeamBlue",                 "#0F5CB8", Band.C, 0.08f),
        new("Teams/TeamRed",                  "#E23B45", Band.C, 0.08f),
        // The Glow pair keeps its name and loses its emission. §11.3 sets the emissive
        // area budget on a character to zero, down from Night Range's 6%.
        new("Teams/TeamBlueGlow",             "#0F5CB8", Band.C, 0.08f),
        new("Teams/TeamRedGlow",              "#E23B45", Band.C, 0.08f),
    };

    // §11.2's bands, in the linear albedo luminance the bible states them in.
    private const float BandALinearMin = 0.10f;
    private const float BandALinearMax = 0.24f;
    private const float BandBLinearMin = 0.02f;
    private const float BandBLinearMax = 0.06f;

    // §11.2, third revision: bands govern BODY MASSES, not every pixel. At or above this
    // share of a figure's silhouette a material must sit inside its band; below it the
    // material is an accent, exempt from the band, and governed by the deck clearance
    // instead — because what makes a small patch a defect is not its value but its value
    // matching the ground, which turns it into a hole in the figure.
    private const float BandMassSilhouetteShare = 0.15f;
    private const float AccentDeckClearanceMin = 0.10f;

    // Retained from the previous pass but no longer the governing check; see the
    // validator, which now works from the linear bands above.
    private const float BandARenderedMin = 0.34f;
    private const float BandARenderedMax = 0.72f;
    private const float BandBRenderedMin = 0.14f;
    private const float BandBRenderedMax = 0.28f;
    private const float BandCMinSaturation = 0.35f;

    // A low-chroma material at the deck's rendered value disappears into the ground
    // wherever the silhouette outline is thin. Chromatic materials are exempt: a
    // saturated blue at the deck's luminance still reads, a grey does not.
    private const float DeckSeparationMin = 0.08f;
    private const float ChromaExemptSaturation = 0.35f;
    // §11.2's per-unit discipline, expressed where it can actually be checked.
    private const float BandABSeparationMin = 0.12f;

    // Minimum rendered step between deck, cover cap and cover body face.
    private const float CoverStepMin = 0.08f;

    // ---- post stack -------------------------------------------------------------
    // Written into the volume profile asset by its own menu item.
    //
    // Neutral, not ACES. ACES systematically desaturates exactly the chroma a colourful
    // board depends on, and it does the damage brightest-first, which on a bright board
    // is most of the image. One field, and it outweighs the shader work in the project.
    private const TonemappingMode Tonemap = TonemappingMode.Neutral;
    private const float PostExposure = 0f;
    // §7.8. Contrast is where the ink gets its bite; saturation pushes the small
    // saturated pieces without touching the ground much, because there is little chroma
    // on the ground to push.
    private const float PostContrast = 14f;
    private const float PostSaturation = 8f;

    // Bloom threshold sits well above the deck's own specular. At 1.0 with the deck at
    // linear 0.40 the board hazes itself and every effect loses contrast. Only the seven
    // HDR values in palette §6 are meant to cross it.
    private const float BloomThreshold = 1.8f;
    private const float BloomIntensity = 0.5f;
    private const float BloomScatter = 0.55f;
    private const float BloomClamp = 8f;

    // §7.8: sunlight, and the cool-shadow / warm-highlight split that is the single
    // biggest "sunny" lever in the stack.
    private const float WhiteBalanceTemperature = 6f;
    private const float WhiteBalanceTint = 0f;
    private static readonly Color SplitToningShadows = new(0.2431f, 0.3569f, 0.4510f, 1f);   // #3E5B73
    private static readonly Color SplitToningHighlights = new(1f, 0.9373f, 0.8235f, 1f);     // #FFEFD2
    private const float SplitToningBalance = 12f;

    // Vignette is --bp-shadow. Just enough to say "a lit table"; above 0.25 it starts to
    // read as serious, which is the thing this direction exists to stop.
    private static readonly Color VignetteColour = new(0.2431f, 0.2980f, 0.3333f, 1f);       // #3E4C55
    private const float VignetteIntensity = 0.16f;
    private const float VignetteSmoothness = 0.45f;

    // §7.6 URP shadow settings, applied by their own menu item. Distance covers the
    // 39 x 27 board from a 24 m camera; two cascades put the near split across the
    // playable area so cover contact shadows stay sharp.
    private const float UrpShadowDistance = 60f;
    private const int UrpShadowCascades = 2;
    private const float UrpCascade2Split = 0.15f;
    private const float UrpShadowDepthBias = 1.0f;
    private const float UrpShadowNormalBias = 1.0f;
    private const int UrpSoftShadowQuality = 2; // Medium

    // ============================ end look values ===================================

    // ---------------------------------------------------------------- asset paths
    internal const string ScenePath = "Assets/Scenes/Game.unity";
    private const string GridCellPrefabPath = "Assets/Prefabs/Map/GridCell.prefab";
    private const string WallPrefabPath = "Assets/Prefabs/Map/Wall.prefab";
    private const string UnitPrefabPath = "Assets/Prefabs/Units/Unit.prefab";
    private const string MapMaterials = "Assets/Materials/Map/";
    private const string GeneratedMaterials = "Assets/Materials/Map/Generated/";
    private const string MeshFolder = "Assets/Meshes/Arena/";
    private const string DecalFolder = "Assets/Textures/Arena/";
    // §7.8 edits the shipped profile in place rather than adding one beside it. The Game scene
    // already references this asset, so there is no rewiring to get wrong, and the original values
    // are recoverable from git if the direction ever needs to be compared against what shipped.
    private const string VolumeProfilePath = "Assets/Settings/GameDaylight Volume Profile.asset";

    private const string ArenaRootName = "BattlePlanArena";
    private const string DeckRootName = "GridCells";
    private const string GeometryAssetPath = MeshFolder + "ArenaGeometry.asset";

    // Roots the old board left behind — see §12. Two deliberate absences: "Walls", which holds
    // disabled in-scene NetworkObjects and is a multiplayer decision rather than an art one, and
    // "Directional Light", which is the shipped sun the whole direction rests on.
    private static readonly string[] LegacyRoots = { "ArenaDressing" };

    // §7.1 — mirrored about column 7 and row 4.5. Read from GameLoop at build time; this copy
    // exists only so the builder can fail loudly if the two ever drift apart.
    private static readonly Vector2Int[] ExpectedWalls =
    {
        new(4, 1), new(10, 1), new(7, 2),
        new(2, 3), new(5, 3), new(9, 3), new(12, 3),
        new(0, 4), new(14, 4), new(0, 5), new(14, 5),
        new(2, 6), new(5, 6), new(9, 6), new(12, 6),
        new(7, 7), new(4, 8), new(10, 8),
    };

    // ================================================================= menu entry points

    [MenuItem("Battle Plan/Art/Build Arena", false, 10)]
    public static void BuildArena()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            EditorUtility.DisplayDialog(
                "Battle Plan arena",
                $"Open {ScenePath} before running this. The builder only ever edits the active scene.",
                "OK");
            return;
        }

        if (!VerifyGameplayInvariants())
            return;

        try
        {
            EditorUtility.DisplayProgressBar("Arena", "Applying the material palette", 0.03f);
            ApplyMaterialPalette();
            ApplyCharacterPalette();

            EditorUtility.DisplayProgressBar("Arena", "Baking deck decals", 0.08f);
            BakeDecalTextures();

            EditorUtility.DisplayProgressBar("Arena", "Building cover kit meshes", 0.20f);
            Dictionary<string, Mesh> kit = BuildCoverKitMeshes();

            EditorUtility.DisplayProgressBar("Arena", "Rebuilding the wall prefab", 0.35f);
            RebuildWallPrefab(kit);

            EditorUtility.DisplayProgressBar("Arena", "Rebuilding the deck", 0.50f);
            GameObject arena = ResetArenaRoot(scene);
            BeginGeometryLibrary();
            RebuildDeck(scene);

            EditorUtility.DisplayProgressBar("Arena", "Placing markings and structure", 0.70f);
            BuildDeckMarkings(arena);
            BuildRailAndPosts(arena);
            BuildBackdrop(arena);
            BuildHillMarker(arena);

            EditorUtility.DisplayProgressBar("Arena", "Lighting and post", 0.90f);
            if (ApplyLookPass)
            {
                AimSun();
                VerifyEnvironment();
                ApplyPostStack();
                ApplyCameraAndVolume();
            }
            else
            {
                Debug.Log("[Arena] Look pass skipped (ApplyLookPass is false). Structure only.");
            }
            RetireLegacyRoots(scene);
            EndGeometryLibrary();

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[Arena] Arena rebuilt. Remaining broker work: run 'Apply URP Shadow Settings' "
                + "and save the scene. HDR is already on across all four pipeline assets, and "
                + "reflections come from the skybox rather than a baked probe.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("Battle Plan/Art/Bake Deck Decal Textures", false, 11)]
    public static void BakeDecalTexturesMenu()
    {
        BakeDecalTextures();
        Debug.Log("[Arena] Deck decal textures baked to " + DecalFolder);
    }

    [MenuItem("Battle Plan/Art/Apply URP Shadow Settings", false, 30)]
    public static void ApplyUrpShadowSettings()
    {
        string[] guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
        int touched = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
                continue;

            var so = new SerializedObject(asset);
            SetIfPresent(so, "m_ShadowDistance", 60f);
            SetIfPresent(so, "m_ShadowCascadeCount", 2);
            SetIfPresent(so, "m_Cascade2Split", 0.15f);
            SetIfPresent(so, "m_ShadowDepthBias", 1.0f);
            SetIfPresent(so, "m_ShadowNormalBias", 1.0f);
            SetIfPresent(so, "m_SoftShadowsSupported", true);
            SetIfPresent(so, "m_SoftShadowQuality", 2); // Medium
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            touched++;
            Debug.Log($"[Arena] Shadow settings applied to {path}. HDR left untouched — that is a broker decision.");
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Arena] URP shadow settings written to {touched} pipeline asset(s).");
    }

    [MenuItem("Battle Plan/Art/Apply Unit Prefab Cleanup", false, 31)]
    public static void ApplyUnitPrefabCleanup()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(UnitPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[Arena] Could not open {UnitPrefabPath}.");
            return;
        }

        try
        {
            // §11.5 — the second ground disc is the halo. The 1.35 disc is tagged
            // TeamIndicatorProp and carries the team emission §11.3 depends on, so it stays.
            Transform rim = FindDescendant(root.transform, "BasePuckRim");
            if (rim != null)
            {
                Object.DestroyImmediate(rim.gameObject);
                Debug.Log("[Arena] Removed BasePuckRim (the 1.55 duplicate ground disc).");
            }

            // §11.5 — no variant should be able to regress to capsule-look. The collider stays.
            var filter = root.GetComponent<MeshFilter>();
            var renderer = root.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Object.DestroyImmediate(renderer);
                Debug.Log("[Arena] Removed the base Unit capsule MeshRenderer.");
            }
            if (filter != null)
            {
                Object.DestroyImmediate(filter);
                Debug.Log("[Arena] Removed the base Unit capsule MeshFilter.");
            }
            if (root.GetComponent<CapsuleCollider>() == null)
                Debug.LogError("[Arena] Unit.prefab lost its CapsuleCollider — revert this change.");

            PrefabUtility.SaveAsPrefabAsset(root, UnitPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ================================================================= invariants

    private static bool VerifyGameplayInvariants()
    {
        var problems = new List<string>();

        if (!Mathf.Approximately(GameLoop.cellSize, Cell))
            problems.Add($"GameLoop.cellSize is {GameLoop.cellSize}, this builder is written for {Cell}.");
        // Read through locals: both sides are compile-time constants today, and comparing them
        // directly lets the compiler fold the check away as unreachable — which is exactly the
        // check going quiet at the moment someone changes the board size.
        int columns = GridSystem.ColumnCount;
        int rows = GridSystem.RowCount;
        if (columns != Columns || rows != Rows)
            problems.Add($"Board is {columns}x{rows}, this builder is written for {Columns}x{Rows}.");
        if (GameLoop.wallLayout.Count != 18)
            problems.Add($"wallLayout has {GameLoop.wallLayout.Count} cells, expected 18.");
        foreach (Vector2Int cell in ExpectedWalls)
        {
            if (!GameLoop.wallLayout.Contains(cell))
                problems.Add($"wallLayout no longer contains {cell}; the cover distribution needs re-reading.");
        }
        if (GameLoop.KingOfTheHillCells.Count != 12)
            problems.Add($"KingOfTheHillCells has {GameLoop.KingOfTheHillCells.Count} cells, expected 12.");

        if (problems.Count == 0)
            return true;

        Debug.LogError("[Arena] Refusing to build:\n  " + string.Join("\n  ", problems));
        EditorUtility.DisplayDialog("Battle Plan arena",
            "Gameplay constants have moved. See the console.", "OK");
        return false;
    }

    // ================================================================= cover kit (§7.3)

    /// <summary>
    /// Four silhouettes, one envelope. Height never varies: every wall cell blocks line of sight
    /// by rule, so a prop that reads as shootable-over is a lie (§15.4).
    /// </summary>
    private static Dictionary<string, Mesh> BuildCoverKitMeshes()
    {
        EnsureFolder(MeshFolder);
        var kit = new Dictionary<string, Mesh>
        {
            ["Cover_Block"] = BuildCoverBlock(),
            ["Cover_Container"] = BuildCoverContainer(),
            ["Cover_Stack"] = BuildCoverStack(),
            ["Cover_Pillar"] = BuildCoverPillar(),
        };

        var saved = new Dictionary<string, Mesh>();
        foreach (var pair in kit)
        {
            int tris = pair.Value.triangles.Length / 3;
            if (tris > CoverTriBudget)
                Debug.LogWarning($"[Arena] {pair.Key} is {tris} tris, over the {CoverTriBudget} budget.");
            else
                Debug.Log($"[Arena] {pair.Key}: {tris} tris.");
            saved[pair.Key] = WriteMesh(pair.Value, MeshFolder + pair.Key + ".asset");
        }

        saved["Cover_PillarPlate"] = WriteMesh(BuildPillarPlate(), MeshFolder + "Cover_PillarPlate.asset");
        return saved;
    }

    private static Mesh BuildCoverBlock()
    {
        var m = new MeshBuilder();
        m.AddChamferedBox(Vector3.zero, Cell, Cell, 0f, CoverBodyTop, CoverChamfer);
        return m.Build("Cover_Block");
    }

    /// <summary>
    /// Corrugation done by narrowing the panel and standing the ribs back out to the cell edge,
    /// rather than by cutting grooves into a full-width face. Cutting would need the face rebuilt
    /// as a frame around every groove; this way the widest point of the prop is still exactly
    /// 2.7, which is the number all four variants have to agree on.
    /// </summary>
    private static Mesh BuildCoverContainer()
    {
        const int RibCount = 7;
        const float RibDepth = 0.08f;
        const float RibWidth = 0.22f;

        var m = new MeshBuilder();
        m.AddChamferedBox(Vector3.zero, Cell, Cell - RibDepth * 2f, 0f, CoverBodyTop, CoverChamfer);

        // Ribs on the two faces perpendicular to Z. The prop is rotated at placement so a
        // corrugated face always turns toward the board centre.
        float pitch = (Cell - RibWidth) / (RibCount - 1);
        for (int i = 0; i < RibCount; i++)
        {
            float x = (i - (RibCount - 1) * 0.5f) * pitch;
            foreach (int sign in new[] { -1, 1 })
            {
                float outer = sign * Cell * 0.5f;
                float inner = sign * (Cell * 0.5f - RibDepth);
                m.AddOpenBox(
                    new Vector3(x - RibWidth * 0.5f, 0f, Mathf.Min(inner, outer)),
                    new Vector3(x + RibWidth * 0.5f, CoverBodyTop - CoverChamfer, Mathf.Max(inner, outer)),
                    skipMinY: true);
            }
        }
        return m.Build("Cover_Container");
    }

    private static Mesh BuildCoverStack()
    {
        const float LowerTop = 1.00f;
        const float GapTop = 1.10f;   // 0.10 recess centred on y 1.05
        const float GapInset = 0.10f;

        var m = new MeshBuilder();
        m.AddChamferedBox(Vector3.zero, Cell, Cell, 0f, LowerTop, CoverChamfer);
        m.AddOpenBox(
            new Vector3(-(Cell * 0.5f - GapInset), LowerTop, -(Cell * 0.5f - GapInset)),
            new Vector3(Cell * 0.5f - GapInset, GapTop, Cell * 0.5f - GapInset),
            skipMinY: true, skipMaxY: true);
        m.AddChamferedBox(Vector3.zero, Cell, Cell, GapTop, CoverBodyTop, CoverChamfer);
        return m.Build("Cover_Stack");
    }

    /// <summary>
    /// The board's landmark. Its identity is the deep 0.25 chamfer — at a 73° camera you read tops
    /// and almost nothing else, so a bigger bevel is a far louder signal than the 0.5-tall recess
    /// §7.3 describes. The recess itself is not modelled: 0.04 of depth is invisible from the game
    /// camera and cutting it would cost ten times the triangles of the whole rest of the prop.
    /// </summary>
    private static Mesh BuildCoverPillar()
    {
        const float PillarChamfer = 0.25f;
        var m = new MeshBuilder();
        m.AddChamferedBox(Vector3.zero, Cell, Cell, 0f, CoverBodyTop, PillarChamfer);
        return m.Build("Cover_Pillar");
    }

    /// <summary>
    /// The pillar's lit ID strip, flush-mounted 2 mm proud of the face so it does not z-fight.
    /// One face only: 0.9 x 0.06 is 0.054 m², and §7.3 caps emissive plate area at 0.06 m² per
    /// block before the board turns into a runway.
    /// </summary>
    private static Mesh BuildPillarPlate()
    {
        const float StripWidth = 0.90f;
        const float StripHeight = 0.06f;
        const float StripY = 1.10f;
        float z = -(Cell * 0.5f + 0.002f);

        var m = new MeshBuilder();
        m.AddQuad(
            new Vector3(-StripWidth * 0.5f, StripY - StripHeight * 0.5f, z),
            new Vector3(StripWidth * 0.5f, StripY - StripHeight * 0.5f, z),
            new Vector3(StripWidth * 0.5f, StripY + StripHeight * 0.5f, z),
            new Vector3(-StripWidth * 0.5f, StripY + StripHeight * 0.5f, z),
            Vector3.back);
        return m.Build("Cover_PillarPlate");
    }

    // ================================================================= wall prefab

    /// <summary>
    /// The wall prefab keeps its NetworkObject, its layer, its transform scale and its convex
    /// octagonal MeshCollider untouched — <c>Helper.heightOffset</c> reads that collider's bounds
    /// to place every spawned wall, so its size is a gameplay value. Only the visual changes.
    /// </summary>
    private static void RebuildWallPrefab(Dictionary<string, Mesh> kit)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(WallPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[Arena] Could not open {WallPrefabPath}.");
            return;
        }

        try
        {
            var collider = root.GetComponent<MeshCollider>();
            if (collider == null || !collider.convex)
            {
                Debug.LogError("[Arena] Wall.prefab lost its convex MeshCollider. Aborting the prefab edit.");
                return;
            }
            Vector3 scale = root.transform.localScale;
            if (!Mathf.Approximately(scale.x, Cell) || !Mathf.Approximately(scale.y, CoverHeight)
                || !Mathf.Approximately(scale.z, Cell))
            {
                Debug.LogError($"[Arena] Wall.prefab scale is {scale}, expected ({Cell}, {CoverHeight}, {Cell}). Aborting.");
                return;
            }

            // The root cube is retired: variants live on children so a single prefab can carry all
            // four silhouettes. Removing the renderer leaves the collider and NetworkObject alone.
            var rootFilter = root.GetComponent<MeshFilter>();
            var rootRenderer = root.GetComponent<MeshRenderer>();
            if (rootRenderer != null) Object.DestroyImmediate(rootRenderer);
            if (rootFilter != null) Object.DestroyImmediate(rootFilter);

            Material cover = LoadMaterial("Map_Cover");
            Material cap = LoadMaterial("Map_CoverCap");
            Material plate = LoadMaterial("Map_CoverPlate");

            // The existing TopCap child is already exactly the 2.754 x 0.12 cap at y 2.0. Reuse it.
            Transform topCap = FindDescendant(root.transform, "TopCap");
            if (topCap != null && topCap.TryGetComponent(out MeshRenderer capRenderer))
                capRenderer.sharedMaterial = cap;
            else
                Debug.LogWarning("[Arena] Wall.prefab has no TopCap child; the cover cap read will be missing.");

            string[] order = { "Cover_Block", "Cover_Container", "Cover_Stack", "Cover_Pillar" };
            var variantObjects = new GameObject[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                string name = order[i];
                Transform existing = FindDescendant(root.transform, name);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);

                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                // Cancel the root's non-uniform scale so the kit meshes stay in world units, and
                // drop the child a full unit because the wall root sits at the block's centre.
                go.transform.localScale = new Vector3(1f / Cell, 1f / CoverHeight, 1f / Cell);
                go.transform.localPosition = new Vector3(0f, -0.5f, 0f);
                go.layer = root.layer;

                go.AddComponent<MeshFilter>().sharedMesh = kit[name];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = cover;

                if (name == "Cover_Pillar")
                {
                    var strip = new GameObject("IDPlate");
                    strip.transform.SetParent(go.transform, false);
                    strip.layer = root.layer;
                    strip.AddComponent<MeshFilter>().sharedMesh = kit["Cover_PillarPlate"];
                    strip.AddComponent<MeshRenderer>().sharedMaterial = plate;
                }

                variantObjects[i] = go;
                go.SetActive(i == 0);
            }

            var chooser = root.GetComponent<CoverVariant>();
            if (chooser == null)
                chooser = root.AddComponent<CoverVariant>();
            chooser.variants = variantObjects;
            chooser.forcedVariant = -1;

            PrefabUtility.SaveAsPrefabAsset(root, WallPrefabPath);
            Debug.Log("[Arena] Wall.prefab rebuilt with the four-variant cover kit; collider and NetworkObject untouched.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ================================================================= deck (§7.2, §7.5)

    private static void RebuildDeck(Scene scene)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GridCellPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[Arena] Missing {GridCellPrefabPath}.");
            return;
        }

        RetuneGridCellPrefab();
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GridCellPrefabPath);

        Material deckA = LoadMaterial("Map_DeckA");
        Material deckB = LoadMaterial("Map_DeckB");
        Material hill = LoadMaterial("Map_DeckHill");
        Material rankBlue = LoadMaterial("Map_DeckRankBlue");
        Material rankRed = LoadMaterial("Map_DeckRankRed");

        GameObject deckRoot = FindRoot(scene, DeckRootName);
        if (deckRoot == null)
        {
            deckRoot = new GameObject(DeckRootName);
            SceneManager.MoveGameObjectToScene(deckRoot, scene);
        }
        deckRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        deckRoot.transform.localScale = Vector3.one;
        deckRoot.layer = LayerMask.NameToLayer("Grid");
        for (int i = deckRoot.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(deckRoot.transform.GetChild(i).gameObject);

        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                var cellCoords = new Vector2Int(column, row);
                bool isHill = GameLoop.KingOfTheHillCells.Contains(cellCoords);
                bool isBlueRank = row == 0;
                bool isRedRank = row == Rows - 1;

                var tile = (GameObject)PrefabUtility.InstantiatePrefab(prefab, deckRoot.transform);
                tile.name = $"Deck_{column:00}_{row:00}";
                tile.transform.localPosition = new Vector3(
                    column * Cell,
                    isHill ? YCellSeparator - HillRecess : YCellSeparator,
                    row * Cell);

                Material face =
                    isHill ? hill
                    : isBlueRank ? rankBlue
                    : isRedRank ? rankRed
                    : ((column + row) % 2 == 1 ? deckB : deckA);

                Transform inner = FindDescendant(tile.transform, "Inner");
                if (inner != null && inner.TryGetComponent(out MeshRenderer innerRenderer))
                    innerRenderer.sharedMaterial = face;

                GameObjectUtility.SetStaticEditorFlags(tile, StaticEditorFlags.BatchingStatic);
            }
        }

        Debug.Log($"[Arena] Deck rebuilt: {Columns * Rows} tiles, "
                  + $"{GameLoop.KingOfTheHillCells.Count} recessed {HillRecess:0.00} for the hill pad, "
                  + $"{Columns * 2} rank tiles tinted by team.");
    }

    /// <summary>
    /// §7.2 — the separator narrows from 0.20 to 0.12 world units. That is the single change to the
    /// grid cell prefab; its layer, its full-cell MeshCollider and its structure are untouched,
    /// because <c>Mouse.GetGridCellUnderMouse</c> raycasts that collider on the "Grid" layer and it
    /// is the only board-selection input in the game.
    /// </summary>
    private static void RetuneGridCellPrefab()
    {
        const float InnerScale = 0.9556f; // 0.9556 * 2.7 = 2.58 inner face -> 0.12 separator

        GameObject root = PrefabUtility.LoadPrefabContents(GridCellPrefabPath);
        if (root == null) return;

        try
        {
            if (root.layer != LayerMask.NameToLayer("Grid"))
                Debug.LogError("[Arena] GridCell.prefab is not on the Grid layer; board input will break.");
            if (root.GetComponent<MeshCollider>() == null)
                Debug.LogError("[Arena] GridCell.prefab lost its MeshCollider; board input will break.");

            if (root.TryGetComponent(out MeshRenderer separator))
                separator.sharedMaterial = LoadMaterial("Map_GridLine");

            Transform inner = FindDescendant(root.transform, "Inner");
            if (inner != null)
            {
                inner.localScale = Vector3.one * InnerScale;
                if (inner.TryGetComponent(out MeshRenderer innerRenderer))
                    innerRenderer.sharedMaterial = LoadMaterial("Map_DeckA");
            }

            PrefabUtility.SaveAsPrefabAsset(root, GridCellPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ================================================================= deck markings (§7.2)

    private static void BuildDeckMarkings(GameObject arena)
    {
        var group = Child(arena, "DeckMarkings");
        paintedMarkings.Clear();

        Material arrow = LoadOrCreateDecalMaterial("Map_PaintArrow", "Map_Paint", "Deck_Arrow");
        Material hazard = LoadOrCreateDecalMaterial("Map_HazardBand", "Map_PaintHazard", "Deck_Hazard");
        // The chevron cannot take a rectangular keyline — a box drawn round a chevron is not
        // that chevron's outer edge — so its contour is baked as the mask's own dilated outline
        // and drawn as a second quad on the same transform, tinted to --bp-ink.
        Material arrowKey = LoadOrCreateDecalMaterial("Map_PaintArrowKey", "Map_Paint", "Deck_ArrowKey", InkHex);
        // The survey tick is authored in ink rather than paint, and carries no contour at all.
        // Its stroke is 0.06 world, 2.2 px, and §4.3.1's cap puts the widest contour it could
        // carry at 0.015 — well under the 0.043 static floor. A shape in that pinch must not be
        // given a thinned contour; it is widened, or authored in a value that clears 3.0:1
        // unaided. A registration mark carries no identity that ink would destroy, so the
        // second remedy is free here: --bp-ink on cell A is 8.17:1, and crisp ink survey marks
        // on pale concrete are what the reference language wants anyway. Same move as §8.2's
        // ink lance, for the same reason.
        Material tick = LoadOrCreateDecalMaterial("Map_InkTick", "Map_Paint", "Deck_Tick", InkHex);
        Material paint = LoadMaterial("Map_Paint");
        Material rankBlue = LoadMaterial("Map_RankLineBlue");
        Material rankRed = LoadMaterial("Map_RankLineRed");
        Material ink = LoadMaterial("Map_RailLine");

        // Hazard bands on the two deployment ranks, at the outer edge of the board. The mask
        // repeats every 1/8 of u and shears by 1 across v, so tiling u by
        // width / (8 * bandWidth) lands the stripes near 45 degrees in world space; rounding to a
        // whole number is what keeps the wrap seam invisible.
        float boardWidth = BoardMaxX - BoardMinX;
        var hazardTiling = new Vector2(
            Mathf.Max(1, Mathf.RoundToInt(boardWidth / (8f * HazardBandWidth))), 1f);
        AddPaintedMarking(group, "Hazard_Blue", HazardBandWidth, hazard,
            new Vector3(BoardCentreX, YDeckDecal, BoardMinZ + HazardBandWidth * 0.5f),
            new Vector2(boardWidth, HazardBandWidth), 0f, hazardTiling);
        AddPaintedMarking(group, "Hazard_Red", HazardBandWidth, hazard,
            new Vector3(BoardCentreX, YDeckDecal, BoardMaxZ - HazardBandWidth * 0.5f),
            new Vector2(boardWidth, HazardBandWidth), 0f, hazardTiling);

        // Inset, so the band never grows past the board edge, and inboard side only: the
        // outboard edge butts the rail, which is --bp-rail and already within a rung of ink.
        // A contour there separates nothing from nothing.
        AddKeyline(group, "Hazard_Blue", "N", ink, inset: true,
            new Vector3(BoardCentreX, YDeckKeyline, BoardMinZ + HazardBandWidth - KeylineWidth * 0.5f),
            new Vector2(boardWidth, KeylineWidth));
        AddKeyline(group, "Hazard_Red", "S", ink, inset: true,
            new Vector3(BoardCentreX, YDeckKeyline, BoardMaxZ - HazardBandWidth + KeylineWidth * 0.5f),
            new Vector2(boardWidth, KeylineWidth));

        // The rank threshold: where your deployment rank stops and the contested field starts.
        float blueThreshold = BoardMinZ + Cell;
        float redThreshold = BoardMaxZ - Cell;
        AddPaintedMarking(group, "RankLine_Blue", RankLineWidth, rankBlue,
            new Vector3(BoardCentreX, YDeckDecal, blueThreshold - RankLineWidth * 0.5f),
            new Vector2(boardWidth, RankLineWidth), 0f, Vector2.one);
        AddPaintedMarking(group, "RankLine_Red", RankLineWidth, rankRed,
            new Vector3(BoardCentreX, YDeckDecal, redThreshold + RankLineWidth * 0.5f),
            new Vector2(boardWidth, RankLineWidth), 0f, Vector2.one);

        // The spawn rank wash has no quad of its own — its fill is the deck tile, Map_DeckRank*
        // on rows 0 and 9 — but it is a painted deck marking and its boundary is the line that
        // tells you whose half you are standing in, so §4.3 covers it. Its narrowest dimension
        // is the one-cell depth of the rank.
        RegisterPaintedMarking("SpawnRank_Blue", Cell);
        RegisterPaintedMarking("SpawnRank_Red", Cell);

        // Two strips per rank, drawn outboard of the team line rather than eaten out of it, so
        // the fill keeps all 0.20. The field-side strip lands exactly on the rank wash's own
        // boundary, so one piece of geometry contours the saturated line and edges the wash —
        // they are the same edge, and the wash is registered against it below.
        foreach ((string team, float threshold, float inward) rank in new[]
                 {
                     ("Blue", blueThreshold, -1f),
                     ("Red", redThreshold, 1f),
                 })
        {
            float fieldEdge = rank.threshold - rank.inward * KeylineWidth * 0.5f;
            float rankEdge = rank.threshold + rank.inward * (RankLineWidth + KeylineWidth * 0.5f);
            AddKeyline(group, $"RankLine_{rank.team}", "Field", ink, inset: false,
                new Vector3(BoardCentreX, YDeckKeyline, fieldEdge),
                new Vector2(boardWidth, KeylineWidth));
            AddKeyline(group, $"RankLine_{rank.team}", "Rank", ink, inset: false,
                new Vector3(BoardCentreX, YDeckKeyline, rankEdge),
                new Vector2(boardWidth, KeylineWidth));
            CreditSharedKeyline($"SpawnRank_{rank.team}", $"RankLine_{rank.team}");
        }

        // Lane arrows point out of each rank toward the fight. Columns 2/7/12 keep them mirrored
        // about column 7, so both cameras see the same landmark spacing.
        foreach (int column in LaneArrowColumns)
        {
            var blue = new Vector3(column * Cell, YDeckDecal, 0f);
            var red = new Vector3(column * Cell, YDeckDecal, (Rows - 1) * Cell);
            var size = new Vector2(LaneArrowSize, LaneArrowSize);
            AddPaintedMarking(group, $"LaneArrow_Blue_{column:00}", ChevronBandWidth, arrow, blue, size, 0f, Vector2.one);
            AddPaintedMarking(group, $"LaneArrow_Red_{column:00}", ChevronBandWidth, arrow, red, size, 180f, Vector2.one);
            AddSilhouetteKeyline(group, $"LaneArrow_Blue_{column:00}", arrowKey, blue, size, 0f);
            AddSilhouetteKeyline(group, $"LaneArrow_Red_{column:00}", arrowKey, red, size, 180f);
        }

        // Registration ticks at the board's survey intersections, skipping anything under cover.
        foreach (int column in TickColumns)
        {
            foreach (int row in TickRows)
            {
                var cellCoords = new Vector2Int(column, row);
                if (GameLoop.wallLayout.Contains(cellCoords))
                    continue;
                float y = GameLoop.KingOfTheHillCells.Contains(cellCoords)
                    ? YDeckDecal - HillRecess
                    : YDeckDecal;
                var centre = new Vector3(column * Cell, y, row * Cell);
                var size = new Vector2(CornerTickSize, CornerTickSize);
                string name = $"Tick_{column:00}_{row:00}";
                AddPaintedMarking(group, name, TickStrokeWidth, tick, centre, size, 0f, Vector2.one);
                ExemptTooNarrowToContour(name, Exempt.ClearsUnaided, "Map_RailLine");
            }
        }

        // §7.5 relaxes the marking rule for a paint inset on the hill pad perimeter.
        BuildHillPadMarkings(group, paint, ink);

        ValidateKeylineLaw();
    }

    /// <summary>
    /// The pad boundary and the paint ring inside it. §4.3 counts these as two of its five gaps —
    /// the perimeter at 1.15:1 and the inset border at 1.84:1 — and this is the edge that matters
    /// most on the board, because the King of the Hill boundary is the single most important line
    /// in the game and it was drawn at 1.15:1.
    ///
    /// One keyline fixes both, and it has to be one rather than two. §4.3 wants the ring inked as
    /// well, but §4.3.1 will not allow it: the ring is 0.08, 2.9 px, and a 0.043 contour on it is
    /// 54% ink by area against a 45% cap. So the pad edge is treated as a single marking — an
    /// inked boundary with interior detail — rather than as two markings each carrying their own
    /// contour. Both of §4.3's measurements are of that one edge, and both are fixed at its
    /// outer boundary. The ring stays a soft wash, which §4.3 explicitly permits once something
    /// else is carrying the contrast.
    /// </summary>
    private static void BuildHillPadMarkings(GameObject group, Material paint, Material ink)
    {
        GetHillPadBounds(out float padMinX, out float padMaxX, out float padMinZ, out float padMaxZ);

        // The pad boundary keyline goes OUTBOARD, on the surrounding deck rather than on the pad.
        // Inboard it would have to overwrite the control groove, and the groove is the one thing
        // on this edge that carries information. Outboard it also traces the top lip of the 0.05
        // recess, which is where a platform edge casts its own line anyway.
        RegisterPaintedMarking("HillPad", Mathf.Min(padMaxX - padMinX, padMaxZ - padMinZ));
        float k = KeylineWidth;
        AddKeyline(group, "HillPad", "S", ink, inset: false,
            new Vector3((padMinX + padMaxX) * 0.5f, YDeckKeyline, padMinZ - k * 0.5f),
            new Vector2(padMaxX - padMinX + k * 2f, k));
        AddKeyline(group, "HillPad", "N", ink, inset: false,
            new Vector3((padMinX + padMaxX) * 0.5f, YDeckKeyline, padMaxZ + k * 0.5f),
            new Vector2(padMaxX - padMinX + k * 2f, k));
        AddKeyline(group, "HillPad", "W", ink, inset: false,
            new Vector3(padMinX - k * 0.5f, YDeckKeyline, (padMinZ + padMaxZ) * 0.5f),
            new Vector2(k, padMaxZ - padMinZ));
        AddKeyline(group, "HillPad", "E", ink, inset: false,
            new Vector3(padMaxX + k * 0.5f, YDeckKeyline, (padMinZ + padMaxZ) * 0.5f),
            new Vector2(k, padMaxZ - padMinZ));

        float minX = padMinX + HillGrooveWidth, maxX = padMaxX - HillGrooveWidth;
        float minZ = padMinZ + HillGrooveWidth, maxZ = padMaxZ - HillGrooveWidth;
        float y = YDeckDecal - HillRecess;
        float w = HillBorderWidth;

        foreach ((string side, Vector3 centre, Vector2 size) ring in new[]
                 {
                     ("S", new Vector3((minX + maxX) * 0.5f, y, minZ + w * 0.5f), new Vector2(maxX - minX, w)),
                     ("N", new Vector3((minX + maxX) * 0.5f, y, maxZ - w * 0.5f), new Vector2(maxX - minX, w)),
                     ("W", new Vector3(minX + w * 0.5f, y, (minZ + maxZ) * 0.5f), new Vector2(w, maxZ - minZ)),
                     ("E", new Vector3(maxX - w * 0.5f, y, (minZ + maxZ) * 0.5f), new Vector2(w, maxZ - minZ)),
                 })
        {
            string name = "HillBorder_" + ring.side;
            AddPaintedMarking(group, name, w, paint, ring.centre, ring.size, 0f, Vector2.one);
            ExemptTooNarrowToContour(name, Exempt.InsideInkedBoundary, "HillPad");
        }
    }

    // ---- §4.3 bookkeeping --------------------------------------------------------------
    // Every painted deck marking registers itself with the narrowest dimension of what it
    // actually draws, every keyline registers against the marking it edges, and
    // ValidateKeylineLaw applies §4.3.1 to each. The registration is the point: §4.3's five gaps
    // were not wrong values, they were five markings nobody thought to ask the question about,
    // and a sixth added next year would go the same way.

    /// <summary>Why a marking carries no contour. Both remedies are §4.3.1's own.</summary>
    private enum Exempt
    {
        /// <summary>Authored in a value that clears 3.0:1 against the deck by itself.</summary>
        ClearsUnaided,
        /// <summary>Interior detail inside another marking's inked boundary, which carries the edge.</summary>
        InsideInkedBoundary,
    }

    private sealed class Marking
    {
        public string Name;
        /// <summary>Narrowest silhouette dimension, world units, on the deck plane.</summary>
        public float HostWidth;
        public int InkSides;
        /// <summary>Ink eaten out of the fill rather than added outboard of it.</summary>
        public bool Inset;
        public Exempt? Exemption;
        /// <summary>Material to measure for ClearsUnaided, enclosing marking for InsideInkedBoundary.</summary>
        public string Remedy;
    }

    private static readonly List<Marking> paintedMarkings = new();

    private static Marking RegisterPaintedMarking(string name, float hostWidth)
    {
        Marking existing = paintedMarkings.Find(m => m.Name == name);
        if (existing != null) return existing;
        var record = new Marking { Name = name, HostWidth = hostWidth };
        paintedMarkings.Add(record);
        return record;
    }

    private static void AddPaintedMarking(GameObject parent, string name, float hostWidth,
        Material material, Vector3 centre, Vector2 size, float yawDegrees, Vector2 tiling)
    {
        RegisterPaintedMarking(name, hostWidth);
        AddDecalQuad(parent, name, material, centre, size, yawDegrees, tiling);
    }

    private static void AddKeyline(GameObject parent, string marking, string suffix, Material ink,
        bool inset, Vector3 centre, Vector2 size)
    {
        Marking record = paintedMarkings.Find(m => m.Name == marking);
        if (record != null)
        {
            record.InkSides++;
            record.Inset |= inset;
        }
        AddDecalQuad(parent, $"{marking}_Key{suffix}", ink, centre, size, 0f, Vector2.one);
    }

    /// <summary>
    /// One marking's contour also serving as another's, where the two share an edge in world
    /// space. Only legitimate when it is literally the same line — here, the rank line's
    /// field-side keyline sitting exactly on the spawn wash's boundary.
    /// </summary>
    private static void CreditSharedKeyline(string marking, string sharedWith)
    {
        Marking record = paintedMarkings.Find(m => m.Name == marking);
        Marking source = paintedMarkings.Find(m => m.Name == sharedWith);
        if (record == null || source == null) return;
        record.InkSides++;
        record.Inset |= source.Inset;
    }

    /// <summary>
    /// The keyline for a marking whose outer edge is its own silhouette rather than a rectangle.
    /// Same transform as the fill, one rung up in Y, carrying the mask's dilated outline. The
    /// outline is drawn on both flanks of the stroke, so it counts as two sides for the area cap.
    /// </summary>
    private static void AddSilhouetteKeyline(GameObject parent, string marking, Material inkDecal,
        Vector3 centre, Vector2 size, float yawDegrees)
    {
        Marking record = paintedMarkings.Find(m => m.Name == marking);
        if (record != null) record.InkSides += 2;
        AddDecalQuad(parent, marking + "_Key", inkDecal,
            new Vector3(centre.x, centre.y + (YDeckKeyline - YDeckDecal), centre.z),
            size, yawDegrees, Vector2.one);
    }

    private static void ExemptTooNarrowToContour(string marking, Exempt reason, string remedy)
    {
        Marking record = paintedMarkings.Find(m => m.Name == marking);
        if (record == null) return;
        record.Exemption = reason;
        record.Remedy = remedy;
    }

    /// <summary>
    /// §4.3 and §4.3.1 applied to paint, per marking. Four things, in the order the rule builds:
    /// whether the shape can carry a contour at all, whether it got one, whether the width sits
    /// between the static floor and the cap, and whether the ink is still ink.
    ///
    /// The floor and the cap are both derived rather than compared against a stored constant,
    /// because the moving/static split is the substance of the rule and a single number hides it.
    /// A projectile author reaching for this file should find the two cases side by side and take
    /// the moving one; the deck takes the static one, and takes it because the camera holds still
    /// for a whole match while a projectile crosses every sub-pixel phase in a few frames.
    /// </summary>
    private static bool ValidateKeylineLaw()
    {
        bool ok = true;
        float pxPerUnit = PixelsPerUnit(0f);
        float staticFloor = StaticContourFloorPx / pxPerUnit;
        float movingFloor = MovingContourFloorPx / PixelsPerUnit(0.6f);
        int contoured = 0, exempt = 0;

        // The authored width has to clear the floor it is authored against, before any marking
        // is measured. This is the check the old 2 px rule failed for three revisions.
        if (KeylineWidth < staticFloor)
        {
            Debug.LogError($"[Arena] The deck keyline is {KeylineWidth:0.000}, under §4.3.1's static "
                           + $"floor of {staticFloor:0.000} ({StaticContourFloorPx:0.00} px at "
                           + $"{pxPerUnit:0.0} px/unit). A static contour holds one sub-pixel phase for "
                           + "the whole match and must clear 3.0:1 at the worst one.");
            ok = false;
        }

        foreach (Marking marking in paintedMarkings)
        {
            float cap = marking.HostWidth * ContourCapFraction;
            float widthPx = marking.HostWidth * pxPerUnit;

            // Where the cap falls below the floor the shape cannot carry a contour. §4.3.1 is
            // explicit that the answer is never a thinner one: widen the host, or author it in a
            // value that clears unaided.
            if (cap < staticFloor)
            {
                if (marking.Exemption == null)
                {
                    Debug.LogError($"[Arena] '{marking.Name}' is {marking.HostWidth:0.000} wide "
                                   + $"({widthPx:0.0} px), under §4.3.1's {StaticContourFloorPx / ContourCapFraction:0.0} px "
                                   + "static threshold, so it cannot carry a contour: the cap "
                                   + $"({cap:0.000}) is below the floor ({staticFloor:0.000}). Widen it, or "
                                   + "author it in a value that clears 3.0:1 unaided — do not thin the "
                                   + "contour to fit.");
                    ok = false;
                }
                else if (!ValidateContourExemption(marking))
                {
                    ok = false;
                }
                else
                {
                    exempt++;
                }
                continue;
            }

            if (marking.Exemption != null)
            {
                Debug.LogError($"[Arena] '{marking.Name}' is exempted from the contour law but is "
                               + $"{widthPx:0.0} px wide and can carry one. The exemption is only for "
                               + "shapes the cap will not fit a contour into.");
                ok = false;
                continue;
            }

            if (marking.InkSides == 0)
            {
                Debug.LogError($"[Arena] Painted deck marking '{marking.Name}' has no --bp-ink keyline. "
                               + $"§4.3: every painted deck marking carries a {KeylineWidth:0.000}-wide "
                               + "keyline on its outer edge, because paint on this deck cannot hold an "
                               + "edge by value.");
                ok = false;
                continue;
            }

            if (KeylineWidth > cap)
            {
                Debug.LogError($"[Arena] '{marking.Name}' carries a {KeylineWidth:0.000} contour on a "
                               + $"{marking.HostWidth:0.000} host, over §4.3.1's quarter-width cap of "
                               + $"{cap:0.000}. Past the cap the contour stops outlining the shape and "
                               + "starts being it.");
                ok = false;
                continue;
            }

            // The area form of the same cap. Inset ink is taken out of the fill; outboard ink is
            // added around it, so it enlarges the silhouette it is measured against.
            float inkWidth = marking.InkSides * KeylineWidth;
            float share = marking.Inset
                ? inkWidth / marking.HostWidth
                : inkWidth / (marking.HostWidth + inkWidth);
            if (share > ContourInkAreaMax)
            {
                Debug.LogError($"[Arena] '{marking.Name}' is {share:P0} ink by silhouette area across "
                               + $"{marking.InkSides} contoured side(s), over §4.3.1's "
                               + $"{ContourInkAreaMax:P0} cap. That is a dark blob with a coloured "
                               + "speck, which breaks §4.4 in the act of obeying §4.3.");
                ok = false;
                continue;
            }

            contoured++;
        }

        float ink = LinearLuminance(Hex(InkHex));
        float deck = LinearLuminance(PaletteColour("Map_DeckA"));
        float ratio = (Mathf.Max(ink, deck) + 0.05f) / (Mathf.Min(ink, deck) + 0.05f);
        if (ratio < KeylineMinContrast)
        {
            Debug.LogError($"[Arena] The keyline reaches only {ratio:0.00}:1 against cell A, under the "
                           + $"{KeylineMinContrast:0.0}:1 the marking's fill is relying on it for.");
            ok = false;
        }

        if (ok)
        {
            Debug.Log($"[Arena] Contour law holds on {paintedMarkings.Count} painted deck markings: "
                      + $"{contoured} carry a {KeylineWidth:0.000} --bp-ink keyline at {ratio:0.00}:1 "
                      + $"against cell A, {exempt} are too narrow to contour and are remedied instead. "
                      + $"Static floor {staticFloor:0.000} ({StaticContourFloorPx:0.00} px at "
                      + $"{pxPerUnit:0.0} px/unit); moving floor {movingFloor:0.000} on the projectile "
                      + "plane, for anything that travels. The fills keep their own values.");
        }
        return ok;
    }

    private static bool ValidateContourExemption(Marking marking)
    {
        switch (marking.Exemption)
        {
            case Exempt.ClearsUnaided:
            {
                float shape = LinearLuminance(PaletteColour(marking.Remedy));
                float deck = LinearLuminance(PaletteColour("Map_DeckA"));
                float ratio = (Mathf.Max(shape, deck) + 0.05f) / (Mathf.Min(shape, deck) + 0.05f);
                if (ratio >= KeylineMinContrast) return true;
                Debug.LogError($"[Arena] '{marking.Name}' is too narrow to contour and is authored in "
                               + $"{marking.Remedy}, which reaches only {ratio:0.00}:1 against cell A. "
                               + "§4.3.1's second remedy requires the unaided value to clear "
                               + $"{KeylineMinContrast:0.0}:1 on its own.");
                return false;
            }
            case Exempt.InsideInkedBoundary:
            {
                Marking boundary = paintedMarkings.Find(m => m.Name == marking.Remedy);
                if (boundary != null && boundary.InkSides > 0) return true;
                Debug.LogError($"[Arena] '{marking.Name}' is too narrow to contour and defers to "
                               + $"'{marking.Remedy}', which carries no keyline itself. Interior detail "
                               + "is only exempt while something outboard of it is holding the edge.");
                return false;
            }
            default:
                return false;
        }
    }

    // ================================================================= rail, posts, backdrop (§7.4)

    private static void BuildRailAndPosts(GameObject arena)
    {
        var group = Child(arena, "BoardRail");
        Material rail = LoadMaterial("Map_Rail");
        Material line = LoadMaterial("Map_RailLine");

        float outerMinX = BoardMinX - RailWidth;
        float outerMaxX = BoardMaxX + RailWidth;
        float outerMinZ = BoardMinZ - RailWidth;
        float outerMaxZ = BoardMaxZ + RailWidth;

        AddChamferedBoxObject(group, "Rail_S", rail,
            new Vector3(outerMinX, 0f, outerMinZ), new Vector3(outerMaxX, RailHeight, BoardMinZ), RailChamfer);
        AddChamferedBoxObject(group, "Rail_N", rail,
            new Vector3(outerMinX, 0f, BoardMaxZ), new Vector3(outerMaxX, RailHeight, outerMaxZ), RailChamfer);
        AddChamferedBoxObject(group, "Rail_W", rail,
            new Vector3(outerMinX, 0f, BoardMinZ), new Vector3(BoardMinX, RailHeight, BoardMaxZ), RailChamfer);
        AddChamferedBoxObject(group, "Rail_E", rail,
            new Vector3(BoardMaxX, 0f, BoardMinZ), new Vector3(outerMaxX, RailHeight, BoardMaxZ), RailChamfer);

        // One continuous line along the top inner edge, wedged into the chamfer so it reads as an
        // inlay rather than a strip hovering over the slope. It is the only thing framing the
        // picture, and it is the one piece of environment allowed to bloom.
        float lineBottom = RailHeight - RailChamfer;
        float lineTop = lineBottom + RailPinstripe;
        AddBoxObject(group, "RailLine_S", line,
            new Vector3(BoardMinX - RailPinstripe, lineBottom, BoardMinZ - RailPinstripe),
            new Vector3(BoardMaxX + RailPinstripe, lineTop, BoardMinZ));
        AddBoxObject(group, "RailLine_N", line,
            new Vector3(BoardMinX - RailPinstripe, lineBottom, BoardMaxZ),
            new Vector3(BoardMaxX + RailPinstripe, lineTop, BoardMaxZ + RailPinstripe));
        AddBoxObject(group, "RailLine_W", line,
            new Vector3(BoardMinX - RailPinstripe, lineBottom, BoardMinZ),
            new Vector3(BoardMinX, lineTop, BoardMaxZ));
        AddBoxObject(group, "RailLine_E", line,
            new Vector3(BoardMaxX, lineBottom, BoardMinZ),
            new Vector3(BoardMaxX + RailPinstripe, lineTop, BoardMaxZ));

        float halfPost = CornerPostSize * 0.5f;
        var corners = new[]
        {
            new Vector2(BoardMinX - RailWidth * 0.5f, BoardMinZ - RailWidth * 0.5f),
            new Vector2(BoardMaxX + RailWidth * 0.5f, BoardMinZ - RailWidth * 0.5f),
            new Vector2(BoardMinX - RailWidth * 0.5f, BoardMaxZ + RailWidth * 0.5f),
            new Vector2(BoardMaxX + RailWidth * 0.5f, BoardMaxZ + RailWidth * 0.5f),
        };
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 c = corners[i];
            AddChamferedBoxObject(group, $"CornerPost_{i}", rail,
                new Vector3(c.x - halfPost, 0f, c.y - halfPost),
                new Vector3(c.x + halfPost, CornerPostHeight, c.y + halfPost), RailChamfer);

            // The band wraps the post, so it has to stand proud of it or it disappears inside.
            float bandHalf = halfPost + BandProud;
            AddBoxObject(group, $"CornerPostBand_{i}", line,
                new Vector3(c.x - bandHalf, CornerPostBandY, c.y - bandHalf),
                new Vector3(c.x + bandHalf, CornerPostBandY + CornerPostBandHeight, c.y + bandHalf));
        }
    }

    /// <summary>
    /// Parallax only. These exist so the board stops reading as a cut-out floating in the clear
    /// colour; nothing here is ever closer than 6 world units to a playable cell.
    /// </summary>
    private static void BuildBackdrop(GameObject arena)
    {
        var group = Child(arena, "Backdrop");
        Material backdrop = LoadMaterial("Map_Backdrop");
        Material voidMat = LoadMaterial("Map_Void");

        AddBoxObject(group, "VoidFloor", voidMat,
            new Vector3(BoardCentreX - 90f, BackdropBaseY - 0.6f, BoardCentreZ - 90f),
            new Vector3(BoardCentreX + 90f, BackdropBaseY - 0.1f, BoardCentreZ + 90f));

        var rings = new[]
        {
            (inner: 6f, outer: 11f, minH: 1.5f, maxH: 2.4f, count: 16),
            (inner: 12f, outer: 17f, minH: 2.2f, maxH: 3.4f, count: 13),
            (inner: 18f, outer: 22f, minH: 3.0f, maxH: 5.0f, count: 11),
        };

        var rng = new System.Random(BackdropSeed);
        int placed = 0;
        for (int r = 0; r < rings.Length; r++)
        {
            var ring = rings[r];
            for (int i = 0; i < ring.count && placed < BackdropBlockBudget; i++)
            {
                float angle = (i + (r * 0.37f)) / ring.count * Mathf.PI * 2f;
                float height = Mathf.Lerp(ring.minH, ring.maxH, (float)rng.NextDouble());
                float width = Mathf.Lerp(2.2f, 6.5f, (float)rng.NextDouble());
                float depth = Mathf.Lerp(2.2f, 6.5f, (float)rng.NextDouble());
                // Clear the block's own half-size so the *face* honours the ring's inner radius,
                // not just its pivot.
                float distance = Mathf.Lerp(ring.inner, ring.outer, (float)rng.NextDouble())
                                 + Mathf.Max(width, depth) * 0.5f;

                // Project onto the board's own rectangle expanded by the ring distance. A circular
                // ring would dip inside the long edges and drop blocks on top of the rail.
                float halfX = (BoardMaxX - BoardMinX) * 0.5f + distance;
                float halfZ = (BoardMaxZ - BoardMinZ) * 0.5f + distance;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                float t = 1f / Mathf.Max(Mathf.Abs(cos) / halfX, Mathf.Abs(sin) / halfZ);
                float x = BoardCentreX + cos * t;
                float z = BoardCentreZ + sin * t;

                AddChamferedBoxObject(group, $"Backdrop_{r}_{i:00}", backdrop,
                    new Vector3(x - width * 0.5f, BackdropBaseY, z - depth * 0.5f),
                    new Vector3(x + width * 0.5f, BackdropBaseY + height, z + depth * 0.5f), 0.12f);
                placed++;
            }
        }
        Debug.Log($"[Arena] Backdrop: {placed} silhouette blocks (budget {BackdropBlockBudget}).");
    }

    // ================================================================= hill marker (§7.5)

    private static void BuildHillMarker(GameObject arena)
    {
        var group = Child(arena, "HillMarker");
        Material rail = LoadMaterial("Map_Rail");
        Material band = LoadMaterial("Map_HillPostBand");

        GetHillPadBounds(out float minX, out float maxX, out float minZ, out float maxZ);

        // A physical groove in the deck at the pad perimeter. It ships in the uncontrolled state;
        // driving it per control state is a GameLoop change and is listed in the handoff.
        float g = HillGrooveWidth;
        AddBoxObject(group, "HillGroove_S", band,
            new Vector3(minX, YHillGroove - HillRecess - 0.01f, minZ), new Vector3(maxX, YHillGroove - HillRecess, minZ + g));
        AddBoxObject(group, "HillGroove_N", band,
            new Vector3(minX, YHillGroove - HillRecess - 0.01f, maxZ - g), new Vector3(maxX, YHillGroove - HillRecess, maxZ));
        AddBoxObject(group, "HillGroove_W", band,
            new Vector3(minX, YHillGroove - HillRecess - 0.01f, minZ + g), new Vector3(minX + g, YHillGroove - HillRecess, maxZ - g));
        AddBoxObject(group, "HillGroove_E", band,
            new Vector3(maxX - g, YHillGroove - HillRecess - 0.01f, minZ + g), new Vector3(maxX, YHillGroove - HillRecess, maxZ - g));

        var corners = new[]
        {
            new Vector2(minX, minZ), new Vector2(maxX, minZ),
            new Vector2(minX, maxZ), new Vector2(maxX, maxZ),
        };
        float half = HillPostSize * 0.5f;
        float bandHalf = half + BandProud;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 c = corners[i];
            AddChamferedBoxObject(group, $"HillPost_{i}", rail,
                new Vector3(c.x - half, -HillRecess, c.y - half),
                new Vector3(c.x + half, HillPostHeight, c.y + half), 0.05f);
            AddBoxObject(group, $"HillPostBand_{i}", band,
                new Vector3(c.x - bandHalf, HillPostBandY, c.y - bandHalf),
                new Vector3(c.x + bandHalf, HillPostBandY + HillPostBandHeight, c.y + bandHalf));
        }
    }

    private static void GetHillPadBounds(out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = float.MaxValue; maxX = float.MinValue;
        minZ = float.MaxValue; maxZ = float.MinValue;
        foreach (Vector2Int cell in GameLoop.KingOfTheHillCells)
        {
            minX = Mathf.Min(minX, cell.x * Cell - Cell * 0.5f);
            maxX = Mathf.Max(maxX, cell.x * Cell + Cell * 0.5f);
            minZ = Mathf.Min(minZ, cell.y * Cell - Cell * 0.5f);
            maxZ = Mathf.Max(maxZ, cell.y * Cell + Cell * 0.5f);
        }
    }

    // ================================================================= lighting, camera, post

    /// <summary>
    /// Aims the sun the scene already has. Colour, intensity and the light object itself are
    /// left alone: that rig is what produces the daylight look the direction is built on, and
    /// the only thing this build owns is where it points.
    /// </summary>
    private static void AimSun()
    {
        Light sun = FindSun();
        if (sun == null)
        {
            Debug.LogError("[Arena] No directional light in the scene. The board's readability depends "
                           + "on real cast shadows; add one before building.");
            return;
        }

        sun.transform.rotation = Quaternion.Euler(SunEuler);
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = SunShadowStrength;
        sun.shadowBias = SunShadowBias;
        sun.shadowNormalBias = SunShadowNormalBias;
        EditorUtility.SetDirty(sun);

        float shadowLength = CoverHeight / Mathf.Tan(SunElevation * Mathf.Deg2Rad);
        Debug.Log($"[Arena] Sun aimed to elevation {SunElevation}, azimuth {SunAzimuth}. "
                  + $"Cover casts {shadowLength:0.00} m, {shadowLength / Cell:0.00} of a cell.");

        if (!Approximately(sun.intensity, RecordedSunIntensity))
        {
            Debug.LogWarning($"[Arena] Sun intensity is {sun.intensity}, not the recorded {RecordedSunIntensity}. "
                             + "The builder does not change it — flagging in case it drifted by accident.");
        }
        if (!Approximately(sun.color, RecordedSunColour))
        {
            Debug.LogWarning($"[Arena] Sun colour is {sun.color}, not the recorded {RecordedSunColour}. "
                             + "Left as found.");
        }
    }

    private static Light FindSun()
    {
        if (RenderSettings.sun != null)
            return RenderSettings.sun;

        return Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(l => l.type == LightType.Directional);
    }

    /// <summary>
    /// Compares the scene's environment against what is recorded in the look block and warns on
    /// drift. It deliberately writes nothing: the shipped daylight environment is the reference,
    /// so a build that silently overwrote it would destroy the thing being built on.
    /// </summary>
    private static void VerifyEnvironment()
    {
        if (RenderSettings.ambientMode != AmbientMode.Trilight)
        {
            Debug.LogWarning($"[Arena] Ambient mode is {RenderSettings.ambientMode}, not Gradient. "
                             + "The recorded daylight environment is a gradient; left as found.");
        }

        WarnOnDrift("ambient sky", RenderSettings.ambientSkyColor, RecordedAmbientSky);
        WarnOnDrift("ambient equator", RenderSettings.ambientEquatorColor, RecordedAmbientEquator);
        WarnOnDrift("ambient ground", RenderSettings.ambientGroundColor, RecordedAmbientGround);

        if (!Approximately(RenderSettings.ambientIntensity, RecordedAmbientIntensity))
            Debug.LogWarning($"[Arena] Ambient intensity is {RenderSettings.ambientIntensity}, recorded {RecordedAmbientIntensity}.");
        if (!Approximately(RenderSettings.reflectionIntensity, RecordedReflectionIntensity))
            Debug.LogWarning($"[Arena] Reflection intensity is {RenderSettings.reflectionIntensity}, recorded {RecordedReflectionIntensity}.");

        if (RenderSettings.skybox == null)
        {
            Debug.LogWarning("[Arena] No skybox material. Default reflections are sourced from it at "
                             + $"{RecordedReflectionIntensity} intensity, so clearing it flattens every "
                             + "smooth surface on the board.");
        }
    }

    private static void WarnOnDrift(string label, Color actual, Color recorded)
    {
        if (Approximately(actual, recorded)) return;
        Debug.LogWarning($"[Arena] Scene {label} is {actual}, recorded {recorded}. Left as found.");
    }

    private static bool Approximately(float a, float b) =>
        Mathf.Abs(a - b) <= EnvironmentDriftTolerance;

    private static bool Approximately(Color a, Color b) =>
        Approximately(a.r, b.r) && Approximately(a.g, b.g) && Approximately(a.b, b.b);

    private static void ApplyCameraAndVolume()
    {
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Camera cam in cameras)
        {
            // The sky behind a bright diorama is the backdrop, not a void to be blacked out.
            // Written, not asserted — see the ClearColour comment for why this one value is
            // owned while the sun and the ambient are not. Scoped to Solid Color so an
            // overlay or a skybox camera is left alone, and MenuSceneBuilder owns the menu
            // cameras, which clear to --bp-table rather than to --bp-sky.
            if (cam.clearFlags != CameraClearFlags.SolidColor)
            {
                Debug.LogWarning($"[Arena] '{cam.name}' clears with {cam.clearFlags}. §7.5 wants "
                                 + "Solid Color and no skybox. Left as found — a non-solid clear is "
                                 + "a deliberate rig choice, not drift.");
            }
            else if (!Approximately(cam.backgroundColor, ClearColour))
            {
                Color was = cam.backgroundColor;
                cam.backgroundColor = ClearColour;
                EditorUtility.SetDirty(cam);
                Debug.Log($"[Arena] '{cam.name}' clear colour {was} -> --bp-sky {SkyHex} = {ClearColour}.");
            }

            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null && !data.renderPostProcessing)
            {
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(cam);
                Debug.Log($"[Arena] Enabled post-processing on '{cam.name}'. It was off, so the volume profile was doing nothing.");
            }
        }

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
        {
            Debug.LogError($"[Arena] Missing {VolumeProfilePath}.");
            return;
        }

        Volume[] volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Volume global = volumes.FirstOrDefault(v => v.isGlobal) ?? volumes.FirstOrDefault();
        if (global == null)
        {
            Debug.LogWarning("[Arena] No Volume in the scene; add one or the profile will not apply.");
            return;
        }

        global.sharedProfile = profile;
        global.isGlobal = true;
        global.priority = Mathf.Max(global.priority, 1f);
        EditorUtility.SetDirty(global);
        Debug.Log($"[Arena] '{global.name}' now points at {Path.GetFileNameWithoutExtension(VolumeProfilePath)} "
                  + "(scene override only; the Color Grade prefab asset and GameDaylight are untouched).");
    }

    // ================================================================= material palette

    [MenuItem("Battle Plan/Art/Apply Material Palette", false, 12)]
    public static void ApplyMaterialPalette()
    {
        int written = 0;
        foreach (MatSpec spec in MapPalette)
        {
            string path = MapMaterials + spec.Name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Debug.LogError($"[Arena] Palette row '{spec.Name}' has no material at {path}.");
                continue;
            }

            Color srgb = Hex(spec.Hex);
            srgb.a = spec.Alpha;

            mat.SetColor("_BaseColor", srgb);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", srgb);
            mat.SetFloat("_Metallic", spec.Metallic);
            mat.SetFloat("_Smoothness", spec.Smoothness);

            if (spec.EmissionHex != null)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", Hex(spec.EmissionHex) * spec.EmissionGain);
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(mat);
            written++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Arena] Material palette applied to {written} materials.");
        ValidateDeckConstraint();
    }

    [MenuItem("Battle Plan/Art/Apply Character Palette", false, 14)]
    public static void ApplyCharacterPalette()
    {
        int written = 0;
        int provisional = 0;
        foreach (CharSpec spec in CharacterPalette)
        {
            string path = "Assets/Materials/" + spec.Path + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Debug.LogError($"[Arena] Character palette row '{spec.Path}' has no material at {path}.");
                continue;
            }

            Color srgb = Hex(spec.Hex);
            mat.SetColor("_BaseColor", srgb);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", srgb);
            mat.SetFloat("_Metallic", spec.Metallic);
            mat.SetFloat("_Smoothness", spec.Smoothness);

            SetToggle(mat, "_SpecularHighlights", "_SPECULARHIGHLIGHTS_OFF", spec.SpecularHighlights);
            // Never on for a character. With reflections sourced from the skybox at 0.6, a
            // reflective character surface picks up the sky and stops reading as paint.
            SetToggle(mat, "_EnvironmentReflections", "_ENVIRONMENTREFLECTIONS_OFF", false);

            bool emissive = spec.Emission.maxColorComponent > 0f;
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", spec.Emission);
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(mat);
            written++;
            if (spec.Provisional) provisional++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Arena] Character palette applied to {written} materials "
                  + $"({provisional} rows provisional, pending the art director's team colours).");
        ValidateRenderedLadder();
    }

    /// <summary>
    /// URP's Lit shader gates specular and reflections on a float and a shader keyword that have
    /// to agree; setting one without the other leaves the material lying about itself.
    /// </summary>
    private static void SetToggle(Material mat, string property, string offKeyword, bool on)
    {
        if (mat.HasProperty(property))
            mat.SetFloat(property, on ? 1f : 0f);

        if (on) mat.DisableKeyword(offKeyword);
        else mat.EnableKeyword(offKeyword);
    }

    /// <summary>
    /// Converts an authored sRGB value into the value it resolves to on screen for a given face
    /// orientation, normalised against the deck. This is the check that the cover cap failed.
    /// </summary>
    private static float RenderedValue(Color albedo, float faceFactor)
        => DisplayValue(LinearLuminance(albedo) * faceFactor);

    /// <summary>
    /// Rec. 709 relative luminance of an authored sRGB colour, in linear. This replaced HSV value
    /// as the measure everywhere in this file: on a tinted grey the two disagree by up to 0.04,
    /// which is a third of the deck's whole mid-tone window, and luminance is the one that predicts
    /// what the frame actually has room above and below.
    /// </summary>
    private static float LinearLuminance(Color srgb)
        => 0.2126f * Mathf.GammaToLinearSpace(srgb.r)
         + 0.7152f * Mathf.GammaToLinearSpace(srgb.g)
         + 0.0722f * Mathf.GammaToLinearSpace(srgb.b);

    private static float DisplayValue(Color srgb) => DisplayValue(LinearLuminance(srgb));

    private static float DisplayValue(float linear) => Mathf.LinearToGammaSpace(Mathf.Clamp01(linear));

    /// <summary>
    /// HSL saturation, which is the space every saturation figure in ArtBible-Palette §8 is quoted
    /// in — verified against all fourteen of its measured tokens. Unity's <c>Color.RGBToHSV</c> is
    /// the obvious thing to reach for and it is the wrong measure here: the two disagree by up to
    /// 19 points, in both directions. It reads --bp-sky as 29% where the budget says 48%, which
    /// hides the one environment token that actually breaches the hue law, and it reads near-blacks
    /// like --bp-ink as 34% where they are 21%, which invents breaches that are not there.
    /// </summary>
    private static void ToHsl(Color srgb, out float hueDegrees, out float saturation)
    {
        float max = Mathf.Max(srgb.r, Mathf.Max(srgb.g, srgb.b));
        float min = Mathf.Min(srgb.r, Mathf.Min(srgb.g, srgb.b));
        float chroma = max - min;
        float lightness = (max + min) * 0.5f;

        saturation = Mathf.Approximately(chroma, 0f)
            ? 0f
            : chroma / (1f - Mathf.Abs(2f * lightness - 1f));

        Color.RGBToHSV(srgb, out float hue01, out _, out _);
        hueDegrees = hue01 * 360f;
    }

    /// <summary>
    /// Checks the character set in both spaces: §11.2's band windows on authored linear albedo,
    /// and the deck separation in rendered space. An authored ladder can look perfectly spaced in
    /// the Inspector and still collapse to one value on screen, which is exactly what this exists
    /// to catch.
    ///
    /// The band windows are gated on silhouette share, not applied to every row. §11.2 governs
    /// body masses at or above 15% of a figure; below that a material is an accent, exempt from
    /// its band, and required only to clear the deck by <see cref="AccentDeckClearanceMin"/>
    /// linear. The rule is encoded rather than its outcome, so the next material added is
    /// classified by its own area instead of by whoever writes the row.
    /// </summary>
    private static bool ValidateRenderedLadder()
    {
        bool ok = true;
        float lowestA = float.MaxValue;
        float highestB = float.MinValue;
        int masses = 0;
        int accents = 0;

        float deckRendered = RenderedValue(PaletteColour("Map_DeckA"), FaceHorizontal);
        float deckAlbedo = LinearLuminance(PaletteColour("Map_DeckA"));

        foreach (CharSpec spec in CharacterPalette)
        {
            Color colour = Hex(spec.Hex);
            ToHsl(colour, out _, out float saturation);
            float albedo = LinearLuminance(colour);
            float lit = RenderedValue(colour, FaceVerticalLit);
            bool isMass = spec.SilhouetteShare >= BandMassSilhouetteShare;
            bool massBand = spec.Band == Band.A || spec.Band == Band.B;

            // The two declarations have to agree in the one direction that is actually a
            // contradiction. A mass carrying an accent classification is a mislabel — 15% of a
            // figure cannot be "one non-team detail". The reverse is not: §11.2 exempts a small
            // material from its band without asking it to stop naming one.
            if (isMass && !massBand)
            {
                Debug.LogError($"[Arena] '{spec.Path}' declares {spec.Band} but covers "
                               + $"{spec.SilhouetteShare:P0} of the silhouette. At or above "
                               + $"{BandMassSilhouetteShare:P0} it is a body mass and belongs in a "
                               + "mass band (§11.2).");
                ok = false;
            }

            if (isMass)
            {
                masses++;
                switch (spec.Band)
                {
                    case Band.A:
                        if (albedo < BandALinearMin || albedo > BandALinearMax)
                        {
                            Debug.LogError($"[Arena] '{spec.Path}' is a Band A body mass at {albedo:0.000} "
                                           + "linear albedo luminance, outside §11.2's "
                                           + $"{BandALinearMin:0.00}-{BandALinearMax:0.00}.");
                            ok = false;
                        }
                        lowestA = Mathf.Min(lowestA, lit);
                        break;

                    case Band.B:
                        if (albedo < BandBLinearMin || albedo > BandBLinearMax)
                        {
                            Debug.LogError($"[Arena] '{spec.Path}' is a Band B body mass at {albedo:0.000} "
                                           + "linear albedo luminance, outside §11.2's "
                                           + $"{BandBLinearMin:0.00}-{BandBLinearMax:0.00}.");
                            ok = false;
                        }
                        highestB = Mathf.Max(highestB, lit);
                        break;
                }
            }
            else
            {
                accents++;
                // A small patch that happens to match the ground is a hole in the figure however
                // small it is, so the band gives way to a clearance rather than to nothing.
                if (Mathf.Abs(albedo - deckAlbedo) < AccentDeckClearanceMin)
                {
                    Debug.LogError($"[Arena] '{spec.Path}' is an accent at {spec.SilhouetteShare:P0} and "
                                   + $"{albedo:0.000} linear, only {Mathf.Abs(albedo - deckAlbedo):0.000} "
                                   + $"from the deck at {deckAlbedo:0.000}. §11.2 wants "
                                   + $"{AccentDeckClearanceMin:0.00} in either direction.");
                    ok = false;
                }
            }

            // Chroma accents separate by hue, so they carry a saturation floor whatever their area.
            if (spec.Band == Band.C && saturation < BandCMinSaturation
                && spec.Path != "Characters/Char_TeamBase")
            {
                Debug.LogError($"[Arena] '{spec.Path}' is a chroma accent at {saturation:P0} saturation. "
                               + "Accents of this class separate by chroma; below the floor it is "
                               + "just another grey.");
                ok = false;
            }

            // The rendered-space half. §11.5 has the figure reading darker than its ground, and a
            // low-chroma material that lands on the deck's own rendered value breaks that wherever
            // the 2.5px ink outline is thin — which is exactly where a small figure is thinnest.
            if (saturation < ChromaExemptSaturation && Mathf.Abs(lit - deckRendered) < DeckSeparationMin)
            {
                Debug.LogError($"[Arena] '{spec.Path}' renders {lit:0.00} against a deck at {deckRendered:0.00}. "
                               + "A low-chroma material at the deck's own value vanishes into the ground.");
                ok = false;
            }
        }

        if (highestB > float.MinValue && lowestA < float.MaxValue
            && lowestA - highestB < BandABSeparationMin)
        {
            Debug.LogError($"[Arena] Dominant band bottoms out at {lowestA:0.00} and the dark band tops out "
                           + $"at {highestB:0.00}; §11.2 wants at least {BandABSeparationMin:0.00} between them.");
            ok = false;
        }

        if (ok)
        {
            Debug.Log($"[Arena] Rendered ladder holds. {masses} body masses in band — A from {lowestA:0.00}, "
                      + $"B to {highestB:0.00} — and {accents} accents exempt under the "
                      + $"{BandMassSilhouetteShare:P0} rule, all clear of the deck "
                      + $"({deckAlbedo:0.000} linear, {deckRendered:0.00} rendered).");
        }
        return ok;
    }

    // ================================================================= post stack

    [MenuItem("Battle Plan/Art/Apply Post Stack", false, 13)]
    public static void ApplyPostStack()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
        {
            Debug.LogError($"[Arena] Missing {VolumeProfilePath}.");
            return;
        }

        if (profile.TryGet(out Tonemapping tonemapping))
        {
            tonemapping.active = true;
            Override(tonemapping.mode, Tonemap);
        }

        if (profile.TryGet(out ColorAdjustments grade))
        {
            grade.active = true;
            Override(grade.postExposure, PostExposure);
            Override(grade.contrast, PostContrast);
            Override(grade.saturation, PostSaturation);
            Override(grade.colorFilter, Color.white);
        }

        if (profile.TryGet(out Bloom bloom))
        {
            bloom.active = true;
            Override(bloom.threshold, BloomThreshold);
            Override(bloom.intensity, BloomIntensity);
            Override(bloom.scatter, BloomScatter);
            Override(bloom.clamp, BloomClamp);
            Override(bloom.highQualityFiltering, true);
        }

        if (profile.TryGet(out Vignette vignette))
        {
            vignette.active = true;
            Override(vignette.color, VignetteColour);
            Override(vignette.intensity, VignetteIntensity);
            Override(vignette.smoothness, VignetteSmoothness);
            Override(vignette.rounded, false);
        }

        // These two are absent from the shipped profile, so they have to be added rather than
        // fetched. They are also the two the direction leans on hardest for "sunny".
        WhiteBalance whiteBalance = GetOrAdd<WhiteBalance>(profile);
        whiteBalance.active = true;
        Override(whiteBalance.temperature, WhiteBalanceTemperature);
        Override(whiteBalance.tint, WhiteBalanceTint);

        SplitToning splitToning = GetOrAdd<SplitToning>(profile);
        splitToning.active = true;
        Override(splitToning.shadows, SplitToningShadows);
        Override(splitToning.highlights, SplitToningHighlights);
        Override(splitToning.balance, SplitToningBalance);

        // Anything that softens or dirties the image stays off: a diorama reads as a physical
        // object on a table, and grain, fringing and defocus all argue against that.
        DisableIfPresent<FilmGrain>(profile);
        DisableIfPresent<ChromaticAberration>(profile);
        DisableIfPresent<DepthOfField>(profile);
        DisableIfPresent<MotionBlur>(profile);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Arena] Post stack written to {Path.GetFileNameWithoutExtension(VolumeProfilePath)}: "
                  + $"{Tonemap} tonemapping, saturation {PostSaturation}, bloom threshold {BloomThreshold}.");
    }

    private static void Override<T>(VolumeParameter<T> parameter, T value)
    {
        parameter.overrideState = true;
        parameter.value = value;
    }

    private static void DisableIfPresent<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component))
            component.active = false;
    }

    private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        => profile.TryGet(out T component) ? component : profile.Add<T>(true);

    /// <summary>
    /// The deck constraint is the one look rule that is structural rather than taste: a deck above
    /// the window eats both ends of the contrast range and leaves effects nothing but hue. Checked
    /// in code so a re-grade cannot quietly break it.
    /// </summary>
    private static bool ValidateDeckConstraint()
    {
        bool ok = true;
        foreach (MatSpec spec in MapPalette)
        {
            if (!spec.Name.StartsWith("Map_Deck")) continue;
            if (spec.TeamHue) continue;   // the spawn ranks are the team wash by design

            Color colour = Hex(spec.Hex);
            ToHsl(colour, out _, out float saturation);
            float tone = DisplayValue(colour);

            if (saturation > DeckMaxSaturation)
            {
                Debug.LogError($"[Arena] Deck material '{spec.Name}' ({spec.Token}) is {saturation:P0} saturated, "
                               + $"over the {DeckMaxSaturation:P0} ceiling. Saturation belongs in props and "
                               + "signals, not the ground.");
                ok = false;
            }

            if (DeckMidToneMaterials.Contains(spec.Name) && (tone < DeckMidToneMin || tone > DeckMidToneMax))
            {
                Debug.LogError($"[Arena] Deck material '{spec.Name}' ({spec.Token}) sits at {tone:0.000} display "
                               + $"sRGB, outside the {DeckMidToneMin:0.00}-{DeckMidToneMax:0.00} mid-tone window. "
                               + "The window exists so effects have headroom in both directions.");
                ok = false;
            }
        }

        if (ok)
            Debug.Log("[Arena] Deck constraint holds: mid-tone in window, saturation under the ceiling.");

        return ValidateTeamHueGuard() & ValidateCoverLadder() & ok;
    }

    /// <summary>
    /// ArtBible-Palette §8, enforcement rule 1. Blue means yours and red means theirs, and neither
    /// can keep meaning that if the scenery is already speaking in those hues — so no environment
    /// material may sit within 25° of a team hue above 30% saturation.
    ///
    /// This is the rule most likely to be broken by someone adding set dressing later, which is why
    /// it is a build check rather than a review note. A prop belongs in the warm band 30–60° or the
    /// cool band 140–190°.
    /// </summary>
    private static bool ValidateTeamHueGuard()
    {
        bool ok = true;
        foreach (MatSpec spec in MapPalette)
        {
            if (spec.TeamHue) continue;
            ok &= CheckTeamHue(spec.Name, spec.Token, spec.Hex);
        }

        // The camera clear colour is not a material and so would fall outside a palette-only sweep,
        // which would be the wrong place to have a blind spot: it is the single largest area in the
        // frame, and §8 puts it in the 12%-saturation class alongside the ground.
        ok &= CheckTeamHue("Camera clear colour", "--bp-sky", SkyHex);

        if (ok)
            Debug.Log("[Arena] Team hue guard holds: nothing in the environment crowds a team colour.");
        return ok;
    }

    private static bool CheckTeamHue(string name, string token, string hex)
    {
        ToHsl(Hex(hex), out float hue, out float saturation);
        if (saturation <= TeamHueMaxSaturation) return true;

        float toBlue = HueDistance(hue, TeamHueBlue);
        float toRed = HueDistance(hue, TeamHueRed);
        if (toBlue >= TeamHueGuard && toRed >= TeamHueGuard) return true;

        string which = toBlue < toRed ? "team blue" : "team red";
        Debug.LogError($"[Arena] '{name}' ({token} {hex}) is {saturation:P0} saturated at hue {hue:0.0}°, "
                       + $"only {Mathf.Min(toBlue, toRed):0.0}° from {which}. §8 caps that at "
                       + $"{TeamHueMaxSaturation:P0}. Either desaturate it or move it into the warm "
                       + "30–60° or cool 140–190° prop band.");
        return false;
    }

    private static float HueDistance(float a, float b)
    {
        float d = Mathf.Abs(Mathf.Repeat(a - b, 360f));
        return Mathf.Min(d, 360f - d);
    }

    /// <summary>
    /// The cover kit spans two orientations — a horizontal cap over vertical body faces — so its
    /// value ladder is exactly the case that cannot be judged from authored numbers. This is the
    /// check that would have caught the cap and the body landing on the same pixel value.
    /// </summary>
    private static bool ValidateCoverLadder()
    {
        float deck = RenderedValue(PaletteColour("Map_DeckA"), FaceHorizontal);
        float cap = RenderedValue(PaletteColour("Map_CoverCap"), FaceHorizontal);
        float lit = RenderedValue(PaletteColour("Map_Cover"), FaceVerticalLit);
        float shaded = RenderedValue(PaletteColour("Map_Cover"), FaceVerticalShaded);

        // The cap may sit either side of the deck — FIELD DAY puts it above, Night Range put it
        // below — so what matters is the size of each step, not its direction. What must never
        // happen is the cap and the body landing on one value, because then the piece reads as a
        // hole in the deck rather than as a solid 2.0 m blocker.
        bool ok = true;
        if (Mathf.Abs(cap - deck) < CoverStepMin)
        {
            Debug.LogError($"[Arena] Cover cap renders {cap:0.00} against a deck at {deck:0.00}. "
                           + "The cap has to read as its own plane or the piece stops reading as a blocker.");
            ok = false;
        }
        if (Mathf.Abs(cap - lit) < CoverStepMin)
        {
            Debug.LogError($"[Arena] Cover cap renders {cap:0.00} and its lit body faces {lit:0.00}. "
                           + "With the cap and the faces on one value the piece has no top edge. §15.");
            ok = false;
        }

        if (ok)
            Debug.Log($"[Arena] Cover ladder holds: deck {deck:0.00}, cap {cap:0.00}, "
                      + $"body lit {lit:0.00} / shaded {shaded:0.00}.");
        return ok;
    }

    private static Color PaletteColour(string name) => Hex(PaletteHex(name));

    private static string PaletteHex(string name)
    {
        foreach (MatSpec spec in MapPalette)
        {
            if (spec.Name == name)
                return spec.Hex;
        }
        Debug.LogError($"[Arena] No palette row named '{name}'.");
        return "#FF00FF";
    }

    /// <summary>
    /// --bp-ink, read off its palette row rather than restated, so a re-grade of the token cannot
    /// leave the contours behind on the old value while the validator keeps passing.
    /// </summary>
    private static string InkHex => PaletteHex("Map_RailLine");

    private static void RetireLegacyRoots(Scene scene)
    {
        foreach (string name in LegacyRoots)
        {
            GameObject go = FindRoot(scene, name);
            if (go == null) continue;
            Object.DestroyImmediate(go);
            Debug.Log($"[Arena] Retired legacy root '{name}'.");
        }

        GameObject walls = FindRoot(scene, "Walls");
        if (walls != null)
        {
            walls.SetActive(false);
            Debug.LogWarning("[Arena] Scene root 'Walls' still holds disabled in-scene Wall "
                             + "NetworkObjects from the old board. Left in place and inactive — "
                             + "removing in-scene NetworkObjects is a multiplayer call, not an art one.");
        }
    }

    // ================================================================= decal textures

    private static void BakeDecalTextures()
    {
        EnsureFolder(DecalFolder);
        WriteDecal("Deck_Arrow", 256, 256, ChevronMask, TextureWrapMode.Clamp, false);
        // The chevron's contour, baked as the dilated outline of the same mask so the two stay
        // registered: Weathered warps the sampling position, and warping the same position twice
        // moves fill and contour together.
        WriteDecal("Deck_ArrowKey", 256, 256, ChevronKeyMask, TextureWrapMode.Clamp, false, wear: false);
        WriteDecal("Deck_Tick", 256, 256, CornerTickMask, TextureWrapMode.Clamp, false, wear: false);
        // The hazard band repeats along the board edge, so its wear has to repeat with it
        // or the seam becomes the most visible thing on the rail.
        WriteDecal("Deck_Hazard", 256, 64, HazardMask, TextureWrapMode.Repeat, true);
    }

    private delegate float MaskFunc(float u, float v);

    /// <summary>
    /// Masks are baked white-on-transparent and tinted by the material, so the palette stays the
    /// only place a colour is decided.
    ///
    /// <paramref name="wear"/> off keeps the edge wobble but drops the thinning, and is for
    /// anything whose contrast is load-bearing. Wear can eat half a marking's coverage, and
    /// §4.3.1's table puts ink at α 0.50 at 1.78:1 — under 3.0 — so a contour or an
    /// authored-in-ink mark that wears is a contour that stopped working. Paint whose contrast is
    /// carried by a keyline can wear as much as it likes.
    /// </summary>
    private static void WriteDecal(string name, int width, int height, MaskFunc mask,
        TextureWrapMode wrap, bool tileable, bool wear = true)
    {
        const int Samples = 4;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float coverage = 0f;
                for (int sy = 0; sy < Samples; sy++)
                {
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        float u = (x + (sx + 0.5f) / Samples) / width;
                        float v = (y + (sy + 0.5f) / Samples) / height;
                        coverage += Weathered(mask, u, v, tileable, wear);
                    }
                }
                coverage /= Samples * Samples;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(coverage) * 255f);
                pixels[y * width + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();

        string path = DecalFolder + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.wrapMode = wrap;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
    }

    // ---- wear -------------------------------------------------------------------
    // These are painted deck markings, not applied vinyl. A mathematically exact arrow
    // reads as a sticker sitting on top of the floor; paint has a wobbling edge where the
    // brush or the stencil leaked, and it wears thin where boots cross it. Both effects
    // below are deterministic functions of position, so the bake is reproducible and the
    // PNGs are a build product rather than something anyone has to hand-touch.

    // Edge wobble, in UV. Large enough to break a straight line at texel scale, small
    // enough that the arrow is still unmistakably an arrow. The scales are integers so
    // that a tiling texture's noise lattice can be made to wrap exactly at u = 1.
    private const float WearWarpAmount = 0.010f;
    private const int WearWarpScale = 9;
    // How much of the paint's coverage the thinning can eat at its worst.
    private const float WearThinning = 0.34f;
    private const int WearThinningScale = 6;
    // Scuffing rides at a much finer scale than the thinning so the two do not beat
    // together into one obvious blotch pattern.
    private const float WearScuff = 0.16f;
    private const int WearScuffScale = 24;
    private const int WearSeed = 20260725;

    /// <summary>
    /// Warps the sampling position before evaluating the mask, then thins the result. Warping the
    /// domain rather than the output means the wobble lands only on edges — a mask is constant
    /// across its interior, so displacing where it is sampled changes nothing there.
    /// </summary>
    private static float Weathered(MaskFunc mask, float u, float v, bool tileable, bool wear = true)
    {
        float warp = WearWarpScale;
        float warpU = u + WearWarpAmount * Signed(Fbm(u * warp, v * warp, WearSeed, Period(WearWarpScale, tileable)));
        float warpV = v + WearWarpAmount * Signed(Fbm(u * warp, v * warp, WearSeed + 7, Period(WearWarpScale, tileable)));

        float coverage = mask(warpU, warpV);
        if (coverage <= 0f || !wear)
            return coverage;

        float thinning = Fbm(u * WearThinningScale, v * WearThinningScale, WearSeed + 31,
            Period(WearThinningScale, tileable));
        float scuff = Fbm(u * WearScuffScale, v * WearScuffScale, WearSeed + 101,
            Period(WearScuffScale, tileable));

        // Squared so most of the marking stays solid and the thin patches are the exception,
        // which is how worn paint actually fails.
        float paint = 1f - WearThinning * thinning * thinning - WearScuff * scuff;
        return coverage * Mathf.Clamp01(paint);
    }

    /// <summary>
    /// A noise field sampled over u in [0,1] at <paramref name="scale"/> lattice cells repeats
    /// seamlessly only if its lattice wraps at exactly that many cells.
    /// </summary>
    private static int Period(int scale, bool tileable) => tileable ? scale : 0;

    private static float Signed(float unit) => unit * 2f - 1f;

    /// <summary>
    /// Three octaves of value noise. <paramref name="wrapX"/> makes the lattice periodic in x so a
    /// tiling texture's wear tiles with it; pass 0 for clamped textures.
    /// </summary>
    private static float Fbm(float x, float y, int seed, int wrapX)
    {
        float sum = 0f;
        float amplitude = 0.5f;
        float total = 0f;
        int period = wrapX;

        for (int octave = 0; octave < 3; octave++)
        {
            sum += amplitude * ValueNoise(x, y, seed + octave * 977, period);
            total += amplitude;
            amplitude *= 0.5f;
            x *= 2f;
            y *= 2f;
            period *= 2;
        }
        return sum / total;
    }

    private static float ValueNoise(float x, float y, int seed, int period)
    {
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float fx = x - x0;
        float fy = y - y0;

        // Smoothstep the interpolant so the lattice does not show as a grid.
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);

        float c00 = LatticeValue(x0, y0, seed, period);
        float c10 = LatticeValue(x0 + 1, y0, seed, period);
        float c01 = LatticeValue(x0, y0 + 1, seed, period);
        float c11 = LatticeValue(x0 + 1, y0 + 1, seed, period);

        return Mathf.Lerp(Mathf.Lerp(c00, c10, fx), Mathf.Lerp(c01, c11, fx), fy);
    }

    private static float LatticeValue(int x, int y, int seed, int period)
    {
        if (period > 0)
            x = ((x % period) + period) % period;

        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7FFFFFF) / (float)0x7FFFFFF;
        }
    }

    private static float ChevronMask(float u, float v)
    {
        float t = Mathf.Abs(u - 0.5f) * 2f;
        if (t > 0.92f) return 0f;
        float front = 0.74f - 0.42f * t;
        float back = 0.46f - 0.42f * t;
        return v >= back && v <= front ? 1f : 0f;
    }

    /// <summary>
    /// The chevron's §4.3 contour: the band dilated by the keyline width, minus the band. A
    /// rectangle round a chevron is not that chevron's outer edge, so the outline has to be the
    /// mask's own. The quad is square and the texture is square, so the dilation is isotropic in
    /// UV and the keyline lands at a constant world width all the way round.
    /// </summary>
    private static float ChevronKeyMask(float u, float v)
        => OutlineMask(ChevronMask, u, v, KeylineWidth / LaneArrowSize);

    /// <summary>Filled-disc dilation, sampled: outside the mask, within <paramref name="radius"/> of it.</summary>
    private static float OutlineMask(MaskFunc mask, float u, float v, float radius)
    {
        if (mask(u, v) > 0f) return 0f;

        const int Rings = 2;
        const int Spokes = 12;
        for (int ring = 1; ring <= Rings; ring++)
        {
            float r = radius * ring / Rings;
            for (int spoke = 0; spoke < Spokes; spoke++)
            {
                float angle = Mathf.PI * 2f * spoke / Spokes;
                if (mask(u + r * Mathf.Cos(angle), v + r * Mathf.Sin(angle)) > 0f)
                    return 1f;
            }
        }
        return 0f;
    }

    private static float CornerTickMask(float u, float v)
    {
        const float Inset = 0.08f;
        const float Arm = 0.24f;
        const float Stroke = 0.05f;

        float du = Mathf.Min(u, 1f - u) - Inset;
        float dv = Mathf.Min(v, 1f - v) - Inset;
        if (du < 0f || dv < 0f) return 0f;
        bool horizontal = dv <= Stroke && du <= Arm;
        bool vertical = du <= Stroke && dv <= Arm;
        return horizontal || vertical ? 1f : 0f;
    }

    private static float HazardMask(float u, float v)
    {
        // Period 8 in u so a 256 px wide texture tiles seamlessly across the 40.5-unit band.
        float s = u * 8f + v;
        return s - Mathf.Floor(s) < 0.5f ? 1f : 0f;
    }

    /// <summary>
    /// A decal material: the shared paint base, with one baked mask in the base map.
    /// <paramref name="tintHex"/> overrides the base colour, and is how the ink-valued decals —
    /// the chevron's contour and the survey tick — are made without a second base material. The
    /// tint is opaque: a contour's strength is its screen coverage, per §4.3.1, never its alpha.
    /// </summary>
    private static Material LoadOrCreateDecalMaterial(string name, string baseName, string textureName,
        string tintHex = null)
    {
        EnsureFolder(GeneratedMaterials);
        string path = GeneratedMaterials + name + ".mat";
        Material baseMat = LoadMaterial(baseName);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(DecalFolder + textureName + ".png");

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing == null)
        {
            existing = new Material(baseMat);
            AssetDatabase.CreateAsset(existing, path);
        }
        else
        {
            existing.CopyPropertiesFromMaterial(baseMat);
        }
        existing.SetTexture("_BaseMap", tex);
        existing.SetTexture("_MainTex", tex);
        if (tintHex != null)
        {
            Color tint = Hex(tintHex);
            existing.SetColor("_BaseColor", tint);
            existing.SetColor("_Color", tint);
        }
        EditorUtility.SetDirty(existing);
        return existing;
    }

    // ================================================================= scene / asset helpers

    private static GameObject ResetArenaRoot(Scene scene)
    {
        GameObject existing = FindRoot(scene, ArenaRootName);
        if (existing != null)
            Object.DestroyImmediate(existing);

        var arena = new GameObject(ArenaRootName);
        SceneManager.MoveGameObjectToScene(arena, scene);
        arena.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        return arena;
    }

    private static GameObject FindRoot(Scene scene, string name) =>
        scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);

    private static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static Transform FindDescendant(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name && t != root);

    private static Material LoadMaterial(string name)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MapMaterials + name + ".mat");
        if (mat == null)
            Debug.LogError($"[Arena] Missing material {MapMaterials}{name}.mat");
        return mat;
    }

    private static Mesh WriteMesh(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        existing.Clear();
        existing.vertices = mesh.vertices;
        existing.normals = mesh.normals;
        existing.uv = mesh.uv;
        existing.triangles = mesh.triangles;
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(mesh);
        return existing;
    }

    private static void EnsureFolder(string folder)
    {
        string trimmed = folder.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(trimmed)) return;
        string parent = Path.GetDirectoryName(trimmed)?.Replace('\\', '/');
        string leaf = Path.GetFileName(trimmed);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static void SetIfPresent(SerializedObject so, string property, float value)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p != null) p.floatValue = value;
    }

    private static void SetIfPresent(SerializedObject so, string property, int value)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p != null) p.intValue = value;
    }

    private static void SetIfPresent(SerializedObject so, string property, bool value)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p != null) p.boolValue = value;
    }

    private static Color Hex(string hex)
    {
        if (!ColorUtility.TryParseHtmlString(hex, out Color c))
            Debug.LogError($"[Arena] Bad hex {hex}");
        return c;
    }

    // ================================================================= geometry emitters

    private static void AddDecalQuad(GameObject parent, string name, Material material,
        Vector3 centre, Vector2 size, float yawDegrees, Vector2 tiling)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.position = centre;
        go.transform.rotation = Quaternion.Euler(90f, yawDegrees, 0f);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);

        go.AddComponent<MeshFilter>().sharedMesh = QuadMesh(tiling);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    private static readonly Dictionary<Vector2, Mesh> QuadCache = new();

    private static Mesh QuadMesh(Vector2 tiling)
    {
        if (QuadCache.TryGetValue(tiling, out Mesh cached) && cached != null)
            return cached;

        var m = new MeshBuilder();
        m.AddQuad(
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            Vector3.back, tiling);
        Mesh mesh = RegisterGeometry(m.Build($"Decal_{tiling.x}x{tiling.y}"));
        QuadCache[tiling] = mesh;
        return mesh;
    }

    private static void AddBoxObject(GameObject parent, string name, Material material,
        Vector3 min, Vector3 max) => AddChamferedBoxObject(parent, name, material, min, max, 0f);

    private static void AddChamferedBoxObject(GameObject parent, string name, Material material,
        Vector3 min, Vector3 max, float chamfer)
    {
        Vector3 centre = (min + max) * 0.5f;
        var builder = new MeshBuilder();
        builder.AddChamferedBox(
            Vector3.zero, max.x - min.x, max.z - min.z,
            min.y - centre.y, max.y - centre.y, chamfer);

        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.position = centre;

        go.AddComponent<MeshFilter>().sharedMesh = RegisterGeometry(builder.Build(name));
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    // ================================================================= generated geometry library

    // Every mesh this builder generates for the scene is stored as a sub-asset of one file.
    // Without that they would be runtime-only objects and the whole arena would come back empty
    // the next time the scene is opened.
    private static Mesh geometryRoot;

    private static void BeginGeometryLibrary()
    {
        EnsureFolder(MeshFolder);
        QuadCache.Clear();
        geometryRoot = AssetDatabase.LoadAssetAtPath<Mesh>(GeometryAssetPath);
        if (geometryRoot == null)
        {
            geometryRoot = new Mesh { name = "ArenaGeometry" };
            AssetDatabase.CreateAsset(geometryRoot, GeometryAssetPath);
            return;
        }

        foreach (Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(GeometryAssetPath))
            Object.DestroyImmediate(sub, true);
    }

    private static Mesh RegisterGeometry(Mesh mesh)
    {
        if (geometryRoot == null)
        {
            Debug.LogError("[Arena] Geometry library was not opened; mesh would not survive a scene reload.");
            return mesh;
        }
        AssetDatabase.AddObjectToAsset(mesh, geometryRoot);
        return mesh;
    }

    private static void EndGeometryLibrary()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(GeometryAssetPath, ImportAssetOptions.ForceUpdate);
        geometryRoot = null;
    }

    /// <summary>Accumulates flat-shaded triangles. Winding is derived from a normal hint so a
    /// mis-ordered corner can never ship an inside-out face.</summary>
    private class MeshBuilder
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<int> triangles = new();

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normalHint)
            => AddQuad(a, b, c, d, normalHint, Vector2.one);

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normalHint, Vector2 tiling)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(n, normalHint) < 0f)
            {
                (b, d) = (d, b);
                n = -n;
            }

            int baseIndex = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) normals.Add(n);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(tiling.x, 0f));
            uvs.Add(new Vector2(tiling.x, tiling.y));
            uvs.Add(new Vector2(0f, tiling.y));
            triangles.Add(baseIndex); triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 3);
        }

        /// <summary>A box whose top edges are chamfered inward by <paramref name="chamfer"/>.
        /// This is the one primitive the whole kit is made of.</summary>
        public void AddChamferedBox(Vector3 centre, float sizeX, float sizeZ,
            float yMin, float yMax, float chamfer)
        {
            float hx = sizeX * 0.5f;
            float hz = sizeZ * 0.5f;
            chamfer = Mathf.Clamp(chamfer, 0f, Mathf.Min(Mathf.Min(hx, hz) * 0.5f, (yMax - yMin) * 0.5f));
            float shoulderY = yMax - chamfer;

            Vector3 O(float x, float y, float z) => centre + new Vector3(x, y, z);

            var bottom = new[] { O(-hx, yMin, -hz), O(hx, yMin, -hz), O(hx, yMin, hz), O(-hx, yMin, hz) };
            var shoulder = new[] { O(-hx, shoulderY, -hz), O(hx, shoulderY, -hz), O(hx, shoulderY, hz), O(-hx, shoulderY, hz) };

            var sideNormals = new[] { Vector3.back, Vector3.right, Vector3.forward, Vector3.left };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                AddQuad(bottom[i], bottom[j], shoulder[j], shoulder[i], sideNormals[i]);
            }

            if (chamfer <= 0f)
            {
                AddQuad(shoulder[0], shoulder[1], shoulder[2], shoulder[3], Vector3.up);
            }
            else
            {
                float tx = hx - chamfer;
                float tz = hz - chamfer;
                var top = new[] { O(-tx, yMax, -tz), O(tx, yMax, -tz), O(tx, yMax, tz), O(-tx, yMax, tz) };
                // Adjacent chamfer quads share the corner diagonal exactly, so the surface closes
                // without corner triangles. Adding them would double-cover the corner.
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    AddQuad(shoulder[i], shoulder[j], top[j], top[i], (sideNormals[i] + Vector3.up).normalized);
                }
                AddQuad(top[0], top[1], top[2], top[3], Vector3.up);
            }

            AddQuad(bottom[0], bottom[1], bottom[2], bottom[3], Vector3.down);
        }

        /// <summary>A box with selected faces omitted, for detail that sits against a parent surface.</summary>
        public void AddOpenBox(Vector3 min, Vector3 max, bool skipMinY = false, bool skipMaxY = false)
        {
            var a = new Vector3(min.x, min.y, min.z);
            var b = new Vector3(max.x, min.y, min.z);
            var c = new Vector3(max.x, min.y, max.z);
            var d = new Vector3(min.x, min.y, max.z);
            var e = new Vector3(min.x, max.y, min.z);
            var f = new Vector3(max.x, max.y, min.z);
            var g = new Vector3(max.x, max.y, max.z);
            var h = new Vector3(min.x, max.y, max.z);

            AddQuad(a, b, f, e, Vector3.back);
            AddQuad(d, c, g, h, Vector3.forward);
            AddQuad(b, c, g, f, Vector3.right);
            AddQuad(a, d, h, e, Vector3.left);
            if (!skipMaxY) AddQuad(e, f, g, h, Vector3.up);
            if (!skipMinY) AddQuad(a, b, c, d, Vector3.down);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
