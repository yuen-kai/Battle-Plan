using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Brings the three menu scenes' 3D layer into FIELD DAY, the way
/// <c>ArenaBuilder</c> does for <c>Game.unity</c>: every value is a named constant in the
/// LOOK VALUES block, the pass is deterministic and idempotent, and anything genuinely
/// hand-tuned is asserted rather than clobbered.
///
/// Scope is strictly what renders *behind* the interface — camera clear, 3D staging,
/// materials, the contour, the table and the lighting. Nothing under <c>Assets/UI/</c> or
/// <c>Assets/Scripts/Menus/</c> is written by this file; the UI Toolkit layer is complete and
/// owns itself. Two stylesheets are *read*, and only for the regions they declare for 3D:
/// <c>.title-model-stage</c> and <c>.roster-model-stage</c> both say in as many words where
/// the model is meant to sit, and the shipped staging ignored both.
///
/// The thesis, from ArtDirection §5.5: the menus are the tabletop and the match is the board
/// sitting on it. So the menus clear to <c>--bp-table</c> rather than to the board's
/// <c>--bp-sky</c>, they stand their pieces on an actual table, and they are lit by the
/// *same* rig as the board — the sun euler, intensity, colour, shadow settings and gradient
/// ambient are all read from <see cref="ArenaBuilder"/> rather than re-typed, because a
/// duplicated colour literal is precisely how the board's clear colour sat two palette
/// revisions out of date.
/// </summary>
public static class MenuSceneBuilder
{
    // ================================================================================
    // LOOK VALUES — every colour, framing number and lighting value this builder writes
    // lives in this block and nowhere else, so re-composing a menu is one edit here
    // instead of a hunt through the file or, worse, a drag in the Scene view that no
    // review can see.
    //
    // Most values are OWNED (written every run). The camera transform is ASSERTED
    // (compared, warned on, never written). See the per-section notes for which and why.
    // ================================================================================

    // ---- the tabletop -----------------------------------------------------------
    // ArtDirection §5.5: "All four screens sit on --bp-table, so the menus read as the
    // tabletop and the match reads as the board on it." Palette §1.1 gives --bp-table as
    // "the tabletop the whole game sits on — full-screen menu backgrounds".
    //
    // OWNED. Same reasoning as ArenaBuilder's ClearColour: this is a palette token, the
    // palette is the single source of truth for colour, and there is no hand-tuned value
    // underneath it to protect. All three menu cameras shipped with a hue the palette does
    // not contain — mauve, periwinkle and a saturated cyan — none of which is derivable
    // from any token, so there is nothing here that an owner chose and a build would lose.
    private const string TableHex = "#5E5346";

    // Only two of the three menu screens actually show the clear colour. The UI root is
    // transparent on the title (.title-screen) and on crew selection (.roster-screen), so
    // the clear colour IS the visible bed there. Match setup paints its own --bp-table bed
    // in USS (.join-screen), so its clear colour never reaches a pixel — it is still set,
    // because an invisible wrong value is a trap for whoever next makes that root
    // transparent.
    private const string ClearColourNote =
        "--bp-table #5E5346. Visible on Title and Crew selection (transparent UI roots); "
        + "covered by the USS .join-screen bed on Match setup.";

    // ---- lighting ---------------------------------------------------------------
    // OWNED. The menus shipped with Unity's default new-scene rig: a 1.0-intensity light at
    // euler (50, -30, 0) over *skybox* ambient at the default dark grey. That is not a
    // decision anyone made; it is what File > New Scene produces. The board's rig is the
    // decision, and §5.5's "the same object as the board" only means anything if the pieces
    // are lit by the same sun.
    //
    // Every value below is read from ArenaBuilder so the two rigs cannot diverge. The one
    // deliberate difference is recorded in MenuSunShadows: see below.
    private static Vector3 SunEuler => ArenaBuilder.SunEuler;
    private static Color SunColour => ArenaBuilder.RecordedSunColour;
    private const float SunIntensity = ArenaBuilder.RecordedSunIntensity;
    private const float SunShadowStrength = ArenaBuilder.SunShadowStrength;
    private const float SunShadowBias = ArenaBuilder.SunShadowBias;
    private const float SunShadowNormalBias = ArenaBuilder.SunShadowNormalBias;

    // The board's azimuth-90 ruling (§7.7) is a *fairness* constraint: the match camera
    // flips 180 degrees between seats, so an off-axis sun advantages one seat. No menu
    // camera flips, so fairness does not apply here — but matching the board costs nothing
    // and it is what makes a menu piece and a board piece read as the same object under
    // the same light. Kept identical on purpose; do not "improve" the menu azimuth.
    private const bool MenuSunShadows = true;
    private const float SunAimToleranceDegrees = 0.5f;

    private static Color AmbientSky => ArenaBuilder.RecordedAmbientSky;
    private static Color AmbientEquator => ArenaBuilder.RecordedAmbientEquator;
    private static Color AmbientGround => ArenaBuilder.RecordedAmbientGround;
    private const float AmbientIntensity = ArenaBuilder.RecordedAmbientIntensity;
    private const float ReflectionIntensity = ArenaBuilder.RecordedReflectionIntensity;

    // ---- the table plane ----------------------------------------------------------
    // OWNED, and created if absent. Matching the board's sun buys nothing without a
    // surface to catch it: a contact shadow is the strongest "this is sitting on a table"
    // cue available, and it is the one cue the shipped menus had no way to produce.
    //
    // Geometry note that decides the whole design: all three menu cameras have IDENTITY
    // rotation, so they look horizontally down +Z with no downward pitch. A horizontal
    // plane below the eye is therefore visible as the lower half of the frame, converging
    // on a horizon at exactly screen y 0.5 — which is what a floor looks like from a
    // standing eye height. It is not edge-on and it is not invisible. What it does mean is
    // that the plane can never be a floating rectangle with visible corners: it has to run
    // off every visible edge, so the only boundary is the horizon. Hence the extent below,
    // which is large on purpose.
    private const string TableObjectName = "Menu Table";
    private const string TableMaterialPath = "Assets/Materials/Menus/Menu_Table.mat";
    // Half-extent in world units. A plane of half-extent W stops covering the frustum
    // beyond z = W / (tan(fov/2) * aspect); past that its side edges intrude. At 200 units
    // that limit is ~195 units out, where the residual gap between the plane's far corner
    // and the horizon is well under a pixel. Reported every run so it stays checked rather
    // than assumed.
    private const float TableHalfExtent = 200f;
    private const float TableSmoothness = 0.12f;

    // How far the table sits below the camera eye, PER SCENE. One tabletop height across
    // all three menus was the more elegant idea and it is not the one that survives
    // contact: the drop is what fixes how far a piece has to stand from the camera to put
    // its feet at a declared screen height, and therefore how large it is in WORLD units at
    // a given size on screen.
    //
    // That matters for exactly one reason. The sun's shadow bias and normal bias
    // (0.05 / 0.4, read from ArenaBuilder) are authored in world units against a 2.3-unit
    // board capsule. A piece much smaller than that gets a proportionally looser bias and
    // its contact shadow detaches from its feet, which destroys the one cue this plane
    // exists to provide. So each screen's drop is chosen to land its pieces near board
    // scale, and nothing on screen can reveal the difference: the cameras sit at different
    // depths with different fields of view, and an unpitched camera puts the horizon at
    // 0.5 regardless of how far down the surface is.
    //
    // Title (fov 48.35, eye y 1): -1.4 puts both figures ~3.0 units tall, 4.6 and 5.6 units
    // out. A close-up of two pieces slightly larger than board scale.
    // Crew  (fov 60,    eye y 1): -4.7 puts the soldier 2.30 units tall at 9.25 units out —
    // the board capsule exactly. Board scale, across the table.
    private const float TitleTableDrop = -1.4f;
    private const float CrewTableDrop = -4.7f;

    // ---- the paper contour ---------------------------------------------------------
    // OWNED. The contour law generalised: a drawn edge carries the contrast, and which
    // colour carries it depends on the bed. Ink on the light board, paper on the dark
    // table. That is not an exception to §4.3, it is §4.3 stated properly, and §7.3
    // already names --bp-deck-0 as the remedy for exactly the ink-on-table pairing it
    // bans.
    //
    // The arithmetic is what forces it rather than a preference. --bp-table has relative
    // luminance 0.0901, so a *darker* contour is capped at 2.80:1 (pure black); ink
    // manages 2.33:1. Solving the WCAG ratio for a 3.0:1 pass on the dark side needs
    // luminance -0.0033, which is negative — proof that the dark family is the wrong tool
    // on this bed, not a near miss to tune. Paper measures 7.13:1, clearing the 3.0:1
    // non-text threshold by 2.4x and the 4.5:1 text threshold as well.
    //
    // The pass itself was already running. The FreeOutline renderer feature (Linework
    // Lite) lives on URP_Renderer.asset, which is renderer index 0 in ALL FOUR pipeline
    // assets (Low/Medium/High/URP), and every menu camera uses index 0 or -1. Linework
    // Lite does not cap its outline list — [DisallowMultipleRendererFeature] limits the
    // project to one FEATURE driving N entries, which is a different thing — so a fourth
    // entry beside Silhouette, Selection and Threat costs nothing.
    //
    // Its own entry rather than the shared Silhouette one buys two things worth having.
    // The colour can be paper without touching a value the board depends on; and the
    // GameObject layerMask can be Everything, so filtering is purely by rendering layer.
    // That removes the BlueTeam borrow an earlier draft needed only to satisfy
    // Silhouette's layerMask of 192 — menu pieces now stay on Default, which is what a
    // decorative object in a scene with no gameplay should be on.
    private const int ContourRenderingLayerIndex = 4;
    private const string ContourRenderingLayerName = "PaperOutline";
    private const int ContourGameObjectLayer = 0;       // Default
    private const string ContourColourHex = "#FCF9F3"; // --bp-deck-0, paper

    // Width is INHERITED from the board's contour, not owned here, and this reverses what
    // this builder was about to do. It is worth writing down why, because the wrong answer
    // is the intuitive one.
    //
    // Width is literally in PIXELS: the Free Outline shader's clip-space pass offsets by
    // normalize(normalHCS.xy) / _ScreenParams.xy * w * width * 2, so after the perspective
    // divide it is a constant `width` pixels at any distance, with scaleWithResolution off.
    // The board uses 2.5 px, tuned against units occupying well under a tenth of frame
    // height. A menu figure at 0.72 of frame height is roughly 10x that on screen, where
    // 2.5 px is 0.32% of the silhouette — which looks like a case for widening it to keep
    // the line-to-subject ratio somewhere near the board's.
    //
    // §4.3.1 has already considered and rejected exactly that reasoning: "A contour's
    // required width is set by the display, not by the size of its host. A 0.78px ink line
    // is equally visible on a 4px bullet and on a 99px cell, and the only reason to make it
    // wider on the larger object would be style, not legibility." It then splits the floor
    // by motion — 0.78 px for a moving contour, 1.56 px for a static one that is stuck at
    // whatever sub-pixel phase it landed on — and caps every contour at a quarter of its
    // host's NARROWEST silhouette dimension.
    //
    // That cap is the part that settles it. Linework's stencil masking draws the outer
    // silhouette, so the contour also wraps thin protrusions: a rifle barrel, a pogo shaft.
    // Those are the narrowest hosts in frame, not the torso, and a heavy line closes them
    // into a solid. The board's 2.5 px is already 1.6x the static floor and already proven
    // against those same protrusions on those same meshes at a SMALLER pixel size, where
    // the cap is tightest. At menu scale every host is larger, so the same width is
    // strictly safer.
    //
    // So it is copied from the board's entry at creation and asserted afterwards rather
    // than rewritten — a later deliberate widening for style is permitted by §4.3.1 and
    // should not be clobbered, but dropping below the static floor is a defect.
    private const float ContourStaticFloorPixels = 1.56f;

    private const string OutlineSettingsPath = "Assets/Free Outline Settings.asset";
    private const string ContourEntryName = "Menu Contour";
    // Cloned rather than authored from scratch, and this is the important part: extrusion
    // method, occlusion, masking strategy, blend mode and render queue are all copied from
    // the board's own contour, which is already proven to produce a clean edge on THESE
    // character meshes. Clip-space normal extrusion gaps on split normals, so "it works on
    // the board's characters" is evidence and a fresh guess would not be.
    private const string ContourTemplateEntryName = "Silhouette";
    private const string TagManagerPath = "ProjectSettings/TagManager.asset";

    // ---- materials -----------------------------------------------------------------
    // OWNED. This is the defect behind the pure-saturated-blue pogo stick on the title
    // screen, and it is a binding problem rather than a colour problem.
    //
    // Every character .fbx in the project imports with materialLocation 1 — Use Embedded
    // Materials — and an EMPTY externalObjects remap. Its material names are Blender
    // defaults ("Material", "Material.001", ...), materialName is BasedOnTextureName and
    // materialSearch is RecursiveUp, so the importer's search finds nothing to bind to and
    // falls back to generating read-only sub-assets inside the .fbx from the DCC's own
    // material data. Those sub-assets cannot be edited and no pass over Assets/Materials/
    // can reach them. The unit prefabs escape this by overriding m_Materials on every
    // renderer with the shared Char_* assets; the menu scenes instance the raw .fbx, so
    // they inherit the Blender colours wholesale. The blue was never off-palette — it was
    // never on the palette to begin with.
    //
    // The fix binds the same shared Char_* assets the board binds, so the menus sit at the
    // level where colour cannot drift. Three routes are tried in order, each with a guard,
    // and anything unresolved is reported rather than guessed:
    //   1. mesh-name match against the paired unit prefab, submesh counts having to agree,
    //      and any mesh name that appears twice with different materials dropped rather
    //      than resolved arbitrarily.
    //   2. no match: log the slot with its embedded material, colour and measured
    //      saturation, and leave it alone. A wrong Char_* is worse than a known gap.
    // Then, regardless of route, the saturation guard below runs — that one is a breach
    // rather than a preference, so it fires even on an unresolved slot.
    // ---- the sniper, and why a subtree ---------------------------------------------
    // Mesh-name matching resolved the pogo rider 13 of 13 and the soldier 10 of 11, and
    // resolved the title sniper barely at all — every name it needed was ambiguous, so the
    // pass correctly refused to guess and he shipped as a near-solid black cut-out on the
    // most-seen screen in the game. The ambiguity was real and the default was right; the
    // cause is one level further down.
    //
    // Sniper.prefab contains TWO characters. It references Sniper.fbx *and* SniperOld.fbx:
    // 20 plain renderers under 'Sniper/' and 'SniperRifle/' whose meshes all come from
    // SniperOld.fbx, plus a Sniper.fbx prefab instance renamed 'Sniper (1)' carrying the
    // authored Char_* overrides on all 15 of its slots. Blender numbers meshes per file, so
    // both copies contain a 'Circle', a 'Cube', a 'Cylinder' and a 'Sphere.003' — the same
    // NAMES on different GEOMETRY, with different materials. That is not a case a matcher
    // can resolve; it is a case where the question was asked of the wrong subtree.
    //
    // The title's Sniper test.fbx is a re-export of Sniper.fbx WITH a rig: same 15 models,
    // same names, same embedded material assignments, plus Gun/ and Person/ armatures. So
    // scoped to 'Sniper (1)' the match is exact and total, and it stays live — a palette
    // revision that moves Char_Black reaches the title screen without anyone editing this
    // file. Freezing 17 slots into hand-written pins would have looked like a fix and would
    // have been a second copy of the board's colour story, which is the class of bug this
    // whole pass exists to delete. One pin remains, for the one mesh that was genuinely
    // re-exported; see the sniper's spec.
    //
    // The legacy SniperOld/PogoRiderOld copies inside those two prefabs are a separate
    // finding and not this pass's to fix. Reported rather than touched.
    private const string SniperSubtree = "Sniper (1)";

    private const string CharacterMaterialFolder = "Assets/Materials/Characters";
    // Palette §8: characters and unit local colour cap at 70% HSL saturation, and every
    // authored Char_* sits under it (the highest is Char_AmberPads at 52%). An embedded
    // FBX material above the cap is not a style choice, it is un-regraded DCC output — the
    // title pogo stick measured 100%. Those get Char_TeamBase, which is what the board's
    // own PogoRider.prefab puts on the same parts, and a loud log line.
    private const float CharacterSaturationCap = 0.70f;
    private const string SaturationHoldingMaterial = "Char_TeamBase";

    // The gap the saturation guard could not see, and it is worth naming precisely: §8's cap
    // is a CHROMA budget, so it is measured on saturation, and a neutral has no chroma to
    // measure. Pure black and pure white are 0% saturated and sailed straight through — the
    // title sniper's dominant material is (0,0,0) and three of his slots are (1,1,1). Both
    // are as off-palette as the pogo stick's 100%-saturation blue was; they breach VALUE
    // rather than chroma, and no chroma test can ever catch them.
    //
    // So the guard gets a second axis, bounded by the palette's own extremes in HSL L, the
    // same instrument §8 insists on: --bp-ink #151A20 at 10.4% is the darkest value in the
    // game and --bp-deck-0 #FCF9F3 at 97.1% is the lightest. Anything outside that window is
    // outside the palette by construction.
    //
    // The bounds are deliberately the extremes rather than the character ramp's own, so the
    // guard only ever fires on something genuinely unauthorable. It is checked against every
    // Char_* asset: the darkest, Char_Black at 15.9%, clears the floor by 5.5 points; the
    // lightest, Char_Skin at 56.7%, is nowhere near the ceiling. Material.019's rgb(204,204,204)
    // at 80% is off-palette but inside the window and is NOT caught — correct, because a
    // guard that fires on plausible values stops being evidence of anything.
    private const float PaletteLightnessFloor = 0.104f;    // --bp-ink
    private const float PaletteLightnessCeiling = 0.971f;   // --bp-deck-0

    // ---- framing ------------------------------------------------------------------
    // OWNED, and derived rather than typed. A piece's scale, depth and world position are
    // SOLVED from a declared screen rectangle against the scene's own camera, instead of
    // being hand-numbers that only hold at whatever aspect the author happened to have
    // open.
    //
    // This is the fix for the crew-selection defect and it is deliberately structural. The
    // menu scenes hold raw .fbx instances at localScale 1, while every unit prefab scales
    // the same meshes to 0.25-0.30 (Prefabs/Units/Soldier.prefab: 0.3; PogoRider: 0.3;
    // Sniper: 0.25). So a menu figure rendered at roughly 3.3x its board size — around 7.7
    // world units against a 2.3-unit capsule — and at that size it cannot be composed by
    // moving it, only by pushing it out of frame in a different direction. Declaring the
    // screen height and solving for scale makes the class of bug unreachable.
    //
    // DEPTH IS SOLVED, NOT DECLARED. Once the pieces share a table, a piece's height in
    // frame is no longer free: its feet are on the plane, so where its base sits on screen
    // is a statement about how far away it is. So the spec declares the screen base and
    // the builder solves the distance that puts the feet there. Perspective then supplies
    // the depth cue for free — a further piece is automatically higher in frame and
    // smaller — which is what the shipped staging was faking by nudging Y.
    //
    // Framing is authored at 16:9 and verified again at the narrowest aspect the UI
    // supports, because horizontal headroom is what a narrow window takes away first.
    private const float ReferenceAspect = 16f / 9f;
    private const float NarrowestVerifiedAspect = 4f / 3f;
    // No part of a staged silhouette may come within this fraction of a frame edge. The
    // brief for this pass is "composed rather than clipping", and a piece that merely
    // touches the edge still reads as cropped, so the floor is a visible gap rather than
    // zero.
    private const float EdgeMarginFraction = 0.02f;

    // ---- the region the interface has reserved -------------------------------------
    // Stronger than "does not clip the frame", and the check this pass actually needed:
    // the UI Toolkit layer already declares, in USS, where the 3D is supposed to read. Two
    // elements say so in as many words:
    //
    //   TitleScreen.uss   .title-model-stage { flex-grow: 1; min-width: 320px; }
    //                     beside a 420px .title-column, with the file header stating
    //                     ".title-model-stage reserves the right side, so the live 3D scene
    //                     is the table."
    //   CharacterSelectionToybox.uss
    //                     .roster-model-stage { flex-grow: 1; min-height: 120px; }
    //                     inside the 300px .roster-aside, under a comment reading "Left
    //                     empty on purpose so the 3D model reads behind the lower third."
    //
    // The shipped staging ignored both — the crew pieces were parked bottom-RIGHT, over the
    // 320px .roster-crew column, when the UI had reserved the lower LEFT for them. That is
    // the other half of why that screen read as a mistake, and no frame-edge check would
    // ever have found it, because nothing was leaving the frame.
    //
    // These rects are normalised, origin bottom-left, computed from the USS pixel widths at
    // the 1512-wide reference layout. They are PIXEL-derived, so they shift as the window
    // narrows and a fixed 420px column eats a larger fraction — which is exactly why the
    // report prints the piece against them at two aspects instead of trusting one.
    private const float ReferenceWidthPx = 1512f;

    // ---- per-scene staging ----------------------------------------------------------
    // Anchors are normalised screen coordinates with the origin at BOTTOM-LEFT.
    // AnchorX is the horizontal centre of the piece's silhouette. BaseY is where the
    // bottom of it meets the table, and it is the value that decides the piece's depth.
    // Height is the silhouette's share of frame height.
    //
    // Yaw is applied and pitch and roll are forced to zero. The shipped staging carried a
    // tumbling (25.5, 82.8, 24.3) on the crew soldier and a -20.06 roll on the title
    // sniper, which is most of why both screens read as accidents rather than
    // compositions: pieces on a table stand up.

    private static readonly SceneSpec[] Scenes =
    {
        // ---------------------------------------------------------------- title screen
        // The card-stock column is 420px of the 1512 reference, and .title-model-stage
        // takes everything right of it, full height. The title is the one screen with room
        // for a hero read, so the pair is staged large — but in frame, inside the reserved
        // region, and standing up. The shipped composition cropped both figures at the
        // bottom edge and rolled the sniper 20 degrees, which was not a poster crop
        // anybody chose: it is what a 3.3x-oversized piece looks like when someone drags it
        // until the interesting part is visible.
        //
        // These bases (0.16, 0.22) solve to 4.6 and 5.6 units out at ~3.0 world units tall
        // each, a shade over the board's 2.3-unit capsule. The horizon crossing their
        // mid-chest is what sells the camera as sitting at the table.
        new(
            "Assets/Scenes/Title Screen.unity",
            "Title",
            // .title-column is 420px of the 1512 reference and .title-layout has no padding,
            // so the stage is everything right of 420/1512 = 0.278, full height.
            stage: Rect.MinMaxRect(420f / ReferenceWidthPx, 0f, 1f, 1f),
            tableDrop: TitleTableDrop,
            new PieceSpec[]
            {
                new("Battle Plan character Pogostick Rider", anchorX: 0.500f, baseY: 0.160f,
                    height: 0.720f, yaw: 18f,
                    pairedPrefab: "Assets/Prefabs/Units/PogoRider.prefab"),
                // The sniper resolves against a SUBTREE of its paired prefab, and that is the
                // whole fix for it reading as a black cut-out. See SniperSubtree below.
                new("Sniper test", anchorX: 0.790f, baseY: 0.220f,
                    height: 0.600f, yaw: -28f,
                    pairedPrefab: "Assets/Prefabs/Units/Sniper.prefab",
                    pairedSubtree: SniperSubtree,
                    pins: new[]
                    {
                        new PinSpec("Cube",
                            new[] { "Char_Black", "Char_Skin", "Char_Gunmetal" },
                            "The one renderer whose mesh was re-exported between the two files: the "
                            + "title's 'Cube' carries geometry 'Cube.001' and the board's carries "
                            + "'Cube.006'. Every other mesh name is identical across the pair, so this "
                            + "is the single slot a name match can miss. Pinned from the board's own "
                            + "override on the same node, in the same order, and the order is "
                            + "independently confirmed by the embedded triple it replaces: "
                            + "Material.014 pure black -> Char_Black, Material.016 rgb(255,220,163) "
                            + "-> Char_Skin, Material.017 pure black on the gun body -> "
                            + "Char_Gunmetal."),
                    }),
            }),

        // ------------------------------------------------------------- crew selection
        // The screen the player spends real time on, and the one this pass exists to fix.
        //
        // ONE piece, not two. UI-ContentInventory asks this screen for *a* model, and the
        // reserved region is a fifth of the frame width: .roster-model-stage sits at the
        // bottom of the 300px .roster-aside, so 32px of .roster-frame padding then 300px of
        // column, x 0.021 to 0.220 of the reference width. Two figures in that only works
        // staggered and overlapping, which spends the whole region on a crowd read and
        // leaves neither piece legible. One piece centred in the hole the interface left is
        // both what was asked for and what fits.
        //
        // The piece goes LOWER LEFT, because that is where the interface put the hole. The
        // shipped staging had both figures parked bottom-RIGHT instead, across the 320px
        // .roster-crew column, when a USS comment three lines long said "Left empty on
        // purpose so the 3D model reads behind the lower third" about the opposite side.
        //
        // The Soldier rather than the pogo rider, for three reasons in ascending order of
        // weight: the title already stages the pogo rider, so this screen gets variety;
        // the soldier is the baseline unit on the screen where a roster is chosen; and the
        // menu Soldier instance wraps the SAME .fbx as Soldier.prefab, so its material
        // binding is exact rather than inferred across two separate exports.
        //
        // baseY 0.060 with height 0.215 solves to 9.25 units out at 2.30 world units tall —
        // the board's capsule to two decimal places, i.e. an effective FBX scale on the
        // prefab's own 0.30. It is not a figurine *like* a board piece; it is the board
        // piece, at board size, on the table beside the cards. Top of the silhouette lands
        // at 0.275, inside the reserved region's 0.33 ceiling.
        new(
            "Assets/Scenes/HomeScreen.unity",
            "Crew selection",
            stage: Rect.MinMaxRect(32f / ReferenceWidthPx, 0f, 332f / ReferenceWidthPx, 0.33f),
            tableDrop: CrewTableDrop,
            new PieceSpec[]
            {
                new("Soldier", anchorX: 0.120f, baseY: 0.060f,
                    height: 0.215f, yaw: -16f,
                    pairedPrefab: "Assets/Prefabs/Units/Soldier.prefab"),
            },
            retired: new[] { "Battle Plan character Pogostick Rider" }),

        // ---------------------------------------------------------------- match setup
        // Deliberately empty, and that is a finding rather than an omission.
        //
        // UI-ContentInventory §2 asks for "a picture or 3D element" on this screen and the
        // UI already satisfies it with a picture: .join-stage__picture in JoinGame.uss
        // draws Assets/Images/JoinToyboxStage.png. There is no 3D staging in the scene at
        // all and none is needed — the .join-stage container is opaque, so a model added
        // here would render behind a solid bed and be seen by nobody.
        //
        // No table plane either, for the same reason. The row exists so the clear colour
        // and the lighting are still brought into line.
        new(
            "Assets/Scenes/JoinGame.unity",
            "Match setup",
            stage: Rect.zero,
            tableDrop: 0f,
            new PieceSpec[] { }),
    };

    // ================================================================================

    /// <summary>
    /// One declared material binding, keyed by the renderer's transform path relative to the
    /// staged root — which is unique inside a piece where a mesh name is not. Every pin
    /// carries its reasoning, because a pin is an assertion about which part of a character
    /// a renderer is, and an unexplained one is indistinguishable from a guess.
    /// </summary>
    private readonly struct PinSpec
    {
        public readonly string Path;
        public readonly string[] Materials;
        public readonly string Why;

        public PinSpec(string path, string[] materials, string why)
        {
            Path = path;
            Materials = materials;
            Why = why;
        }
    }

    private readonly struct PieceSpec
    {
        public readonly string Name;
        public readonly float AnchorX;
        public readonly float BaseY;
        public readonly float Height;
        public readonly float Yaw;
        public readonly string PairedPrefab;

        /// <summary>
        /// Optional child path inside the paired prefab to resolve materials against, instead
        /// of the whole prefab. This exists because two unit prefabs carry a legacy copy of
        /// their character beside the current one, and the two copies share Blender's default
        /// mesh names while being different geometry.
        /// </summary>
        public readonly string PairedSubtree;

        public readonly PinSpec[] Pins;

        public PieceSpec(string name, float anchorX, float baseY, float height, float yaw,
            string pairedPrefab, string pairedSubtree = null, PinSpec[] pins = null)
        {
            Name = name;
            AnchorX = anchorX;
            BaseY = baseY;
            Height = height;
            Yaw = yaw;
            PairedPrefab = pairedPrefab;
            PairedSubtree = pairedSubtree;
            Pins = pins ?? System.Array.Empty<PinSpec>();
        }
    }

    private readonly struct SceneSpec
    {
        public readonly string Path;
        public readonly string Label;

        /// <summary>The normalised region the UI has reserved for 3D, origin bottom-left.</summary>
        public readonly Rect Stage;

        /// <summary>World units below the camera eye that this screen's table sits at.</summary>
        public readonly float TableDrop;

        public readonly PieceSpec[] Pieces;

        /// <summary>Root objects that were staged once and are deliberately not any more.</summary>
        public readonly string[] Retired;

        public SceneSpec(string path, string label, Rect stage, float tableDrop, PieceSpec[] pieces,
            string[] retired = null)
        {
            Path = path;
            Label = label;
            Stage = stage;
            TableDrop = tableDrop;
            Pieces = pieces;
            Retired = retired ?? System.Array.Empty<string>();
        }
    }

    // ============================================================== entry points

    [MenuItem("Battle Plan/Art/Build Menu Scenes", false, 15)]
    public static void BuildMenuScenes() => Run(write: true, inventoryOnly: false);

    /// <summary>
    /// Reports every difference the build would make and writes nothing. This is the one
    /// to run first in a barrier: it needs no scene to be open, dirties nothing, and its
    /// log is the diff.
    /// </summary>
    [MenuItem("Battle Plan/Art/Verify Menu Scenes", false, 16)]
    public static void VerifyMenuScenes() => Run(write: false, inventoryOnly: false);

    /// <summary>
    /// Prints, for every renderer slot on every staged piece, what material is bound, where
    /// that material lives, and its measured HSL saturation against the §8 character cap.
    /// This is the evidence a declared slot map should be authored from, and it is separate
    /// from Verify because it is the one report worth reading in full.
    /// </summary>
    [MenuItem("Battle Plan/Art/Report Menu Materials", false, 17)]
    public static void ReportMenuMaterials() => Run(write: false, inventoryOnly: true);

    private static void Run(bool write, bool inventoryOnly)
    {
        // Both modes, not just write: verifying opens each scene Single, which discards the
        // open one without asking. Losing a broker's unsaved scene to a read-only check
        // would be a nasty way to find that out.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[Menus] Cancelled — the open scene has unsaved changes.");
            return;
        }

        string reopen = SceneManager.GetActiveScene().path;
        var report = new List<string>();

        try
        {
            if (!inventoryOnly)
            {
                int contour = EnsureContourEntry(write);
                report.Add($"contour entry: {contour} value(s) {(write ? "written" : "would change")}");
            }

            for (int i = 0; i < Scenes.Length; i++)
            {
                SceneSpec spec = Scenes[i];
                EditorUtility.DisplayProgressBar(
                    "Menu scenes", spec.Label, (i + 0.5f) / Scenes.Length);

                Scene scene = EditorSceneManager.OpenScene(spec.Path, OpenSceneMode.Single);
                if (!scene.IsValid())
                {
                    Debug.LogError($"[Menus] Could not open {spec.Path}.");
                    continue;
                }

                if (inventoryOnly)
                {
                    InventoryMaterials(spec);
                    continue;
                }

                int changes = 0;
                changes += ApplyCamera(spec, write);
                changes += ApplyLighting(spec, write);
                changes += ApplyAmbient(spec, write);
                changes += ApplyTablePlane(spec, write);
                changes += ApplyRetired(spec, write);
                changes += ApplyStaging(spec, write);

                report.Add($"{spec.Label}: {changes} value(s) {(write ? "written" : "would change")}");

                if (write && changes > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (!string.IsNullOrEmpty(reopen))
                EditorSceneManager.OpenScene(reopen, OpenSceneMode.Single);
        }

        if (inventoryOnly)
        {
            Debug.Log("[Menus] Material inventory complete.");
            return;
        }

        Debug.Log($"[Menus] {(write ? "Build" : "Verify")} complete. "
                  + string.Join(" | ", report)
                  + $"\nClear colour: {ClearColourNote}");
    }

    // ============================================================== camera

    private static int ApplyCamera(SceneSpec spec, bool write)
    {
        Camera cam = FindMenuCamera();
        if (cam == null)
        {
            Debug.LogError($"[Menus] {spec.Label}: no enabled camera. Nothing behind the UI can render.");
            return 0;
        }

        int changed = 0;
        Color table = Hex(TableHex);

        // Clear flags first: JoinGame shipped with CameraClearFlags.Nothing (m_ClearFlags 4),
        // which does not clear the colour target at all. Whatever it showed was undefined
        // framebuffer content, so the "saturated cyan" recorded against that scene was the
        // background colour field, not the rendered pixel. Solid Color is the only clear
        // that makes a menu bed a colour anyone can review.
        if (cam.clearFlags != CameraClearFlags.SolidColor)
        {
            Debug.Log($"[Menus] {spec.Label}: '{cam.name}' clear flags {cam.clearFlags} -> Solid Color. "
                      + "A menu camera that does not clear colour renders undefined framebuffer content.");
            if (write) cam.clearFlags = CameraClearFlags.SolidColor;
            changed++;
        }

        if (!Approximately(cam.backgroundColor, table))
        {
            Debug.Log($"[Menus] {spec.Label}: '{cam.name}' clear colour {cam.backgroundColor} "
                      + $"-> --bp-table {TableHex} = {table}.");
            if (write) cam.backgroundColor = table;
            changed++;
        }

        // ASSERTED, not owned. The camera transform and FOV are what the whole UI layout
        // was composed against, and the staging solver reads them as the fixed frame — so
        // moving one would invalidate both at once. Reported so a drift is visible.
        if (cam.transform.rotation != Quaternion.identity)
        {
            Debug.LogWarning($"[Menus] {spec.Label}: '{cam.name}' is rotated "
                             + $"{cam.transform.rotation.eulerAngles}. The framing solver and the table "
                             + "plane both assume an axis-aligned camera looking down +Z; neither is "
                             + "valid for a rotated one. Staging skipped for this scene.");
        }

        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null && !data.renderPostProcessing)
        {
            Debug.LogWarning($"[Menus] {spec.Label}: post-processing is off on '{cam.name}', so the "
                             + "scene's Color Grade volume renders nothing. Left as found — this is a "
                             + "post decision, not a staging one.");
        }

        if (write && changed > 0) EditorUtility.SetDirty(cam);
        return changed;
    }

    private static Camera FindMenuCamera() =>
        Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .OrderByDescending(c => c.CompareTag("MainCamera"))
            .ThenBy(c => c.depth)
            .FirstOrDefault();

    // ============================================================== lighting

    private static int ApplyLighting(SceneSpec spec, bool write)
    {
        Light sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(l => l.type == LightType.Directional);

        if (sun == null)
        {
            Debug.LogWarning($"[Menus] {spec.Label}: no directional light. The pieces will be lit by "
                             + "ambient alone and will read flat against the table.");
            return 0;
        }

        int changed = 0;

        // Compared as a rotation rather than as three numbers. Azimuth 90 sits on the ZXY
        // gimbal, so eulerAngles can round-trip to a different triple describing the same
        // aim, and a component-wise compare would report drift on every run for ever.
        Quaternion wantedAim = Quaternion.Euler(SunEuler);
        if (Quaternion.Angle(sun.transform.rotation, wantedAim) > SunAimToleranceDegrees)
        {
            Debug.Log($"[Menus] {spec.Label}: sun aim {sun.transform.rotation.eulerAngles} -> {SunEuler}.");
            if (write) sun.transform.rotation = wantedAim;
            changed++;
        }

        if (!Approximately(sun.color, SunColour))
        {
            Debug.Log($"[Menus] {spec.Label}: sun colour {sun.color} -> {SunColour}.");
            if (write) sun.color = SunColour;
            changed++;
        }

        if (!Approximately(sun.intensity, SunIntensity))
        {
            Debug.Log($"[Menus] {spec.Label}: sun intensity {sun.intensity} -> {SunIntensity}.");
            if (write) sun.intensity = SunIntensity;
            changed++;
        }

        LightShadows wanted = MenuSunShadows ? LightShadows.Soft : LightShadows.None;
        if (sun.shadows != wanted || !Approximately(sun.shadowStrength, SunShadowStrength))
        {
            Debug.Log($"[Menus] {spec.Label}: sun shadows {sun.shadows}/{sun.shadowStrength} "
                      + $"-> {wanted}/{SunShadowStrength}.");
            if (write)
            {
                sun.shadows = wanted;
                sun.shadowStrength = SunShadowStrength;
                sun.shadowBias = SunShadowBias;
                sun.shadowNormalBias = SunShadowNormalBias;
            }
            changed++;
        }

        if (write && changed > 0) EditorUtility.SetDirty(sun);
        return changed;
    }

    private static int ApplyAmbient(SceneSpec spec, bool write)
    {
        int changed = 0;

        if (RenderSettings.ambientMode != AmbientMode.Trilight)
        {
            Debug.Log($"[Menus] {spec.Label}: ambient mode {RenderSettings.ambientMode} -> Gradient.");
            if (write) RenderSettings.ambientMode = AmbientMode.Trilight;
            changed++;
        }

        changed += SetAmbient(spec, "sky", RenderSettings.ambientSkyColor, AmbientSky,
            c => RenderSettings.ambientSkyColor = c, write);
        changed += SetAmbient(spec, "equator", RenderSettings.ambientEquatorColor, AmbientEquator,
            c => RenderSettings.ambientEquatorColor = c, write);
        changed += SetAmbient(spec, "ground", RenderSettings.ambientGroundColor, AmbientGround,
            c => RenderSettings.ambientGroundColor = c, write);

        if (!Approximately(RenderSettings.ambientIntensity, AmbientIntensity))
        {
            Debug.Log($"[Menus] {spec.Label}: ambient intensity {RenderSettings.ambientIntensity} "
                      + $"-> {AmbientIntensity}.");
            if (write) RenderSettings.ambientIntensity = AmbientIntensity;
            changed++;
        }

        if (!Approximately(RenderSettings.reflectionIntensity, ReflectionIntensity))
        {
            Debug.Log($"[Menus] {spec.Label}: reflection intensity {RenderSettings.reflectionIntensity} "
                      + $"-> {ReflectionIntensity}.");
            if (write) RenderSettings.reflectionIntensity = ReflectionIntensity;
            changed++;
        }

        return changed;
    }

    private static int SetAmbient(SceneSpec spec, string label, Color actual, Color wanted,
        System.Action<Color> apply, bool write)
    {
        if (Approximately(actual, wanted)) return 0;
        Debug.Log($"[Menus] {spec.Label}: ambient {label} {actual} -> {wanted}.");
        if (write) apply(wanted);
        return 1;
    }

    // ============================================================== the table

    private static int ApplyTablePlane(SceneSpec spec, bool write)
    {
        if (spec.Pieces.Length == 0) return 0;

        Camera cam = FindMenuCamera();
        if (cam == null || cam.transform.rotation != Quaternion.identity) return 0;

        Material mat = LoadOrCreateTableMaterial(write);
        GameObject table = FindStagedRoot(TableObjectName);
        int changed = 0;

        if (table == null)
        {
            float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float converge = TableHalfExtent / (tanHalf * ReferenceAspect);
            Debug.Log($"[Menus] {spec.Label}: creating '{TableObjectName}' — a {2f * TableHalfExtent:0} unit "
                      + $"plane in --bp-table {spec.TableDrop} below the eye. Its far corners reach the "
                      + $"frame edge at {converge:0} units, where the gap to the horizon is sub-pixel. "
                      + "The horizon itself lands at screen y 0.500 by construction: a camera with no "
                      + "pitch puts its own eye height there.");
            if (!write) return 1;

            table = GameObject.CreatePrimitive(PrimitiveType.Plane);
            table.name = TableObjectName;
            Object.DestroyImmediate(table.GetComponent<Collider>());
            changed++;
        }

        Vector3 eye = cam.transform.position;
        // Pushed forward so the plane spans from just behind the camera to well past the
        // furthest piece. Nothing is gained by centring it on the eye and half of it would
        // be behind the viewer.
        var wantedPos = new Vector3(eye.x, eye.y + spec.TableDrop, eye.z + TableHalfExtent * 0.9f);
        var wantedScale = Vector3.one * (TableHalfExtent / 5f);   // the Plane primitive is 10 units across

        if (!Approximately(table.transform.position, wantedPos)
            || !Approximately(table.transform.localScale, wantedScale)
            || table.transform.rotation != Quaternion.identity)
        {
            Debug.Log($"[Menus] {spec.Label}: table plane -> pos {wantedPos}, scale {wantedScale.x:0.0}.");
            if (write)
            {
                table.transform.SetPositionAndRotation(wantedPos, Quaternion.identity);
                table.transform.localScale = wantedScale;
            }
            changed++;
        }

        var rend = table.GetComponent<MeshRenderer>();
        if (rend != null)
        {
            // Receives, never casts. A floor casting into itself only buys shadow acne, and
            // the point of this object is to be the thing shadows land ON.
            if (rend.shadowCastingMode != ShadowCastingMode.Off || !rend.receiveShadows)
            {
                if (write)
                {
                    rend.shadowCastingMode = ShadowCastingMode.Off;
                    rend.receiveShadows = true;
                }
                changed++;
            }

            // Explicitly Default only. The table is the surface the contour reads against;
            // putting it on the contour layer would draw a paper line along the horizon,
            // which is the one edge in frame that must not be drawn.
            uint defaultLayer = 1u;
            if (rend.renderingLayerMask != defaultLayer)
            {
                if (write) rend.renderingLayerMask = defaultLayer;
                changed++;
            }

            if (mat != null && rend.sharedMaterial != mat)
            {
                Debug.Log($"[Menus] {spec.Label}: table plane material -> {TableMaterialPath}.");
                if (write) rend.sharedMaterial = mat;
                changed++;
            }

            if (write && changed > 0) EditorUtility.SetDirty(rend);
        }

        if (write && changed > 0) EditorUtility.SetDirty(table);
        return changed;
    }

    private static Material LoadOrCreateTableMaterial(bool write)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(TableMaterialPath);
        Color table = Hex(TableHex);

        if (mat == null)
        {
            if (!write) return null;

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[Menus] URP Lit shader not found; cannot create the table material.");
                return null;
            }

            EnsureFolder(System.IO.Path.GetDirectoryName(TableMaterialPath).Replace('\\', '/'));

            mat = new Material(lit);
            AssetDatabase.CreateAsset(mat, TableMaterialPath);
            Debug.Log($"[Menus] created {TableMaterialPath} in --bp-table {TableHex}.");
        }

        // The albedo IS the token. Matching the clear colour is the whole trick: the lit
        // plane renders a little above the unlit clear, so the horizon reads as the far
        // edge of the table rather than as a seam between two different browns.
        bool dirty = false;
        if (mat.HasProperty("_BaseColor") && !Approximately(mat.GetColor("_BaseColor"), table))
        {
            if (write) mat.SetColor("_BaseColor", table);
            dirty = true;
        }
        if (mat.HasProperty("_Smoothness") && !Approximately(mat.GetFloat("_Smoothness"), TableSmoothness))
        {
            if (write) mat.SetFloat("_Smoothness", TableSmoothness);
            dirty = true;
        }
        if (mat.HasProperty("_Metallic") && !Approximately(mat.GetFloat("_Metallic"), 0f))
        {
            if (write) mat.SetFloat("_Metallic", 0f);
            dirty = true;
        }

        if (write && dirty)
        {
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
        }

        return mat;
    }

    // ============================================================== staging

    /// <summary>
    /// Deactivates scene objects that used to be staged and no longer are. Dropping a piece
    /// from the spec is not enough on its own: the builder only ever touches what it is
    /// told to compose, so an unlisted piece would simply keep the oversized, frame-clipping
    /// transform it shipped with — a worse outcome than before the pass. Deactivated rather
    /// than deleted, because reversing a hidden object is a checkbox and reversing a deleted
    /// one is a git archaeology exercise.
    /// </summary>
    private static int ApplyRetired(SceneSpec spec, bool write)
    {
        int changed = 0;

        foreach (string name in spec.Retired)
        {
            GameObject go = FindStagedRoot(name);
            if (go == null) continue;
            if (!go.activeSelf) continue;

            Debug.Log($"[Menus] {spec.Label}: '{name}' retired from staging -> deactivated. It is no "
                      + "longer composed, so leaving it enabled would leave it exactly where it was "
                      + "wrong. Still in the scene, one checkbox from coming back.");
            if (write)
            {
                go.SetActive(false);
                EditorUtility.SetDirty(go);
            }
            changed++;
        }

        return changed;
    }

    private static int ApplyStaging(SceneSpec spec, bool write)
    {
        if (spec.Pieces.Length == 0)
        {
            Debug.Log($"[Menus] {spec.Label}: no 3D staging by design. Its content requirement is met "
                      + "by the UI picture, which this builder does not touch.");
            return 0;
        }

        Camera cam = FindMenuCamera();
        if (cam == null || cam.transform.rotation != Quaternion.identity) return 0;

        int changed = 0;
        foreach (PieceSpec piece in spec.Pieces)
        {
            GameObject go = FindStagedRoot(piece.Name);
            if (go == null)
            {
                Debug.LogError($"[Menus] {spec.Label}: staged piece '{piece.Name}' is not in the scene. "
                               + "The content inventory requires this screen to show a 3D model; the "
                               + "builder composes what is there and never adds or removes a piece.");
                continue;
            }

            changed += ApplyPieceMaterials(spec, go, piece, write);
            changed += ApplyOutline(spec, go, piece, write);
            changed += Compose(spec, cam, go, piece, write);
        }

        return changed;
    }

    /// <summary>
    /// Only root objects are considered. A staged piece is always a scene root here, and
    /// matching by name anywhere in the hierarchy would happily grab a bone called
    /// "Soldier" inside the rig.
    /// </summary>
    private static GameObject FindStagedRoot(string name) =>
        SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == name);

    private static int Compose(SceneSpec spec, Camera cam, GameObject go, PieceSpec piece, bool write)
    {
        Transform t = go.transform;
        Vector3 wasPos = t.position;
        Quaternion wasRot = t.rotation;
        Vector3 wasScale = t.localScale;

        // Rotate first: bounds are axis-aligned in world space, so the silhouette this
        // solver sizes has to be the silhouette the player will actually see.
        t.rotation = Quaternion.Euler(0f, piece.Yaw, 0f);
        t.localScale = Vector3.one;

        if (!TryWorldBounds(go, out Bounds unit) || unit.size.y <= Mathf.Epsilon)
        {
            Restore(t, wasPos, wasRot, wasScale);
            Debug.LogWarning($"[Menus] {spec.Label}: '{piece.Name}' has no renderer bounds; left as found.");
            return 0;
        }

        Vector3 eye = cam.transform.position;
        float tableY = eye.y + spec.TableDrop;
        float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

        // Solve the distance that puts the piece's feet at the declared screen height,
        // given that its feet are on the table. Both terms are negative — the table is
        // below the eye and the base is below centre — so the distance comes out positive.
        float fromCentre = 2f * piece.BaseY - 1f;
        if (fromCentre >= -Mathf.Epsilon)
        {
            Restore(t, wasPos, wasRot, wasScale);
            Debug.LogError($"[Menus] {spec.Label}: '{piece.Name}' baseY {piece.BaseY} is at or above the "
                           + "frame centre. The horizon is at 0.5 for an unpitched camera, so a piece "
                           + "standing on the table cannot have its feet there — it would have to be "
                           + "infinitely far away.");
            return 0;
        }

        float distance = spec.TableDrop / (fromCentre * tanHalf);
        if (distance <= cam.nearClipPlane)
        {
            Restore(t, wasPos, wasRot, wasScale);
            Debug.LogError($"[Menus] {spec.Label}: '{piece.Name}' solves to {distance:0.00} units, inside "
                           + "the near plane. Raise its baseY.");
            return 0;
        }

        float halfH = distance * tanHalf;
        float halfW = halfH * ReferenceAspect;

        float scale = piece.Height * 2f * halfH / unit.size.y;
        t.localScale = Vector3.one * scale;

        if (!TryWorldBounds(go, out Bounds scaled))
        {
            Restore(t, wasPos, wasRot, wasScale);
            return 0;
        }

        // Feet on the table, silhouette centred on the anchor, depth centre on the solved
        // plane.
        t.position += new Vector3(
            (2f * piece.AnchorX - 1f) * halfW - scaled.center.x + eye.x,
            tableY - scaled.min.y,
            eye.z + distance - scaled.center.z);

        bool moved = !Approximately(t.position, wasPos)
                     || Quaternion.Angle(t.rotation, wasRot) > 0.05f
                     || !Approximately(t.localScale, wasScale);

        TryWorldBounds(go, out Bounds placed);
        Debug.Log($"[Menus] {spec.Label}: '{piece.Name}' solved to {distance:0.00} units out, "
                  + $"{placed.size.y:0.00} world units tall (a board unit is ~2.3), scale "
                  + $"{wasScale.x:0.000} -> {scale:0.000}.");
        ReportFraming(spec, piece, cam, placed);

        // Bounds in edit mode are the rest pose. Root motion is off on both title pieces
        // (m_ApplyRootMotion 0), so the solved transform holds — but a generic clip can
        // still carry a curve on the root, and a swinging limb can reach outside the rest
        // silhouette. This is the one part of the framing that a static check cannot
        // settle, so it is named rather than assumed.
        var animator = go.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            Debug.LogWarning($"[Menus] {spec.Label}: '{piece.Name}' is animated by "
                             + $"'{animator.runtimeAnimatorController?.name ?? "no controller"}'. The "
                             + "rectangle above is its REST POSE, and the table raises the stakes on that: "
                             + "the feet are now pinned to a surface, so any clip that dips below the rest "
                             + "pose will pass through it. Root motion is off, so the solved transform "
                             + "holds — confirm the vertical extremes of the clip in Play mode before "
                             + "calling this framing verified.");
        }

        if (!write)
        {
            Restore(t, wasPos, wasRot, wasScale);
        }
        else if (moved)
        {
            EditorUtility.SetDirty(go);
        }

        return moved ? 1 : 0;
    }

    private static void Restore(Transform t, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        t.position = pos;
        t.rotation = rot;
        t.localScale = scale;
    }

    /// <summary>
    /// Re-projects the placed silhouette's eight corners and asserts the composition twice
    /// over: against the frame, and against the region the UI reserved. This is the check
    /// that would have caught the shipped crew-selection framing — which never left the
    /// frame at all, and was simply in the wrong quarter of it.
    /// </summary>
    private static void ReportFraming(SceneSpec spec, PieceSpec piece, Camera cam, Bounds placed)
    {
        foreach (float aspect in new[] { ReferenceAspect, NarrowestVerifiedAspect })
        {
            if (!TryScreenRect(cam, placed, aspect, out Rect r))
            {
                Debug.LogError($"[Menus] {spec.Label}: '{piece.Name}' crosses the near plane.");
                return;
            }

            float m = EdgeMarginFraction;
            bool offFrame = r.xMin < m || r.xMax > 1f - m || r.yMin < m || r.yMax > 1f - m;

            // The stage rect is only meaningful at the reference aspect. It is derived from
            // USS PIXEL widths, and the stylesheets reflow under their own breakpoints —
            // .narrow .roster-columns switches to a column and .narrow .title-model-stage
            // stops reserving anything at all. So at a narrower aspect the reserved region
            // is a different rect that nobody has declared, and checking a piece against
            // the wide-layout rect there would report a failure against the wrong target.
            bool reference = Mathf.Approximately(aspect, ReferenceAspect);
            Rect stage = spec.Stage;
            bool offStage = reference && stage.width > 0f
                            && (r.xMin < stage.xMin || r.xMax > stage.xMax
                                || r.yMin < stage.yMin || r.yMax > stage.yMax);

            string line = $"[Menus] {spec.Label}: '{piece.Name}' at {aspect:0.00} occupies "
                          + $"x {r.xMin:0.000}-{r.xMax:0.000}, y {r.yMin:0.000}-{r.yMax:0.000}"
                          + (reference
                              ? $" (reserved stage x {stage.xMin:0.000}-{stage.xMax:0.000}, "
                                + $"y {stage.yMin:0.000}-{stage.yMax:0.000})."
                              : " (stage rect not checked — the stylesheets reflow below the "
                                + "reference aspect).");

            if (offFrame && reference)
            {
                Debug.LogWarning(line + $" CLIPS the {m:P0} frame margin. Lower its height fraction or "
                                      + "move its anchor inward — do not accept a cropped piece on a "
                                      + "tabletop bed.");
            }
            else if (offStage)
            {
                Debug.LogWarning(line + " LEAVES the region the UI reserved for it, so part of it will "
                                      + "sit behind an opaque card. Adjust the anchor, not the "
                                      + "stylesheet.");
            }
            else if (offFrame)
            {
                // EXPECTED BELOW THE REFERENCE ASPECT — not a defect, and logged at Log
                // level so a barrier does not read it as one.
                //
                // The interface has already ruled on this breakpoint. Both stylesheets stop
                // reserving a stage entirely at narrow: TitleScreen.uss has
                // ".narrow .title-model-stage { display: none }" and
                // CharacterSelectionToybox.uss has ".narrow .roster-model-stage,
                // .narrow .map-column { display: none }". So below the reference aspect
                // there is no reserved region for a piece to be composed inside, and the 3D
                // agreeing with the UI means going away rather than negotiating a smaller
                // corner. Vertical framing is unaffected either way, since a narrower window
                // takes horizontal headroom and the vertical field of view is fixed.
                Debug.Log(line + " Crops at this aspect, which is EXPECTED below the reference aspect and "
                                 + "not a defect: both stylesheets set the model stage to display: none "
                                 + "under .narrow, so no reserved region exists here to be composed "
                                 + "inside. Fully honouring that would hide the piece too, which needs a "
                                 + "runtime responder rather than a serialized transform.");
            }
            else
            {
                Debug.Log(line);
            }
        }
    }

    private static bool TryScreenRect(Camera cam, Bounds b, float aspect, out Rect rect)
    {
        rect = default;
        float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector3 eye = cam.transform.position;

        float xMin = float.MaxValue, xMax = float.MinValue;
        float yMin = float.MaxValue, yMax = float.MinValue;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? b.min.x : b.max.x,
                (i & 2) == 0 ? b.min.y : b.max.y,
                (i & 4) == 0 ? b.min.z : b.max.z);

            float dz = corner.z - eye.z;
            if (dz <= cam.nearClipPlane) return false;

            float halfH = dz * tanHalf;
            float halfW = halfH * aspect;
            float vx = ((corner.x - eye.x) / halfW + 1f) * 0.5f;
            float vy = ((corner.y - eye.y) / halfH + 1f) * 0.5f;

            xMin = Mathf.Min(xMin, vx);
            xMax = Mathf.Max(xMax, vx);
            yMin = Mathf.Min(yMin, vy);
            yMax = Mathf.Max(yMax, vy);
        }

        rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }

    private static bool TryWorldBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(includeInactive: false);
        bool any = false;

        foreach (Renderer r in renderers)
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            if (!any)
            {
                bounds = r.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return any;
    }

    // ============================================================== the contour

    /// <summary>
    /// Creates or reconciles the paper contour entry in the shared Linework Lite settings,
    /// modelled on the board's own Silhouette entry so the extrusion behaviour is proven
    /// rather than guessed. Read and written through SerializedObject so this file carries
    /// no compile-time dependency on the package's types — if Linework Lite is ever
    /// removed, this degrades to a warning instead of breaking the editor assembly.
    /// Runs once per build, not per scene: the settings asset is global.
    /// </summary>
    private static int EnsureContourEntry(bool write)
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(OutlineSettingsPath);
        Object settings = all?.FirstOrDefault(o => o is ScriptableObject && AssetDatabase.IsMainAsset(o));
        if (settings == null)
        {
            Debug.LogError($"[Menus] {OutlineSettingsPath} has no main asset. Nothing will draw a contour "
                           + "for the menu pieces.");
            return 0;
        }

        Object template = all.FirstOrDefault(o => o != null && o.name == ContourTemplateEntryName);
        if (template == null)
        {
            Debug.LogError($"[Menus] no '{ContourTemplateEntryName}' entry in {OutlineSettingsPath} to "
                           + "clone. That entry is the board's own contour and the only proof that "
                           + "clip-space extrusion produces a clean edge on these meshes; authoring a "
                           + "fresh entry would be a guess, so nothing is written.");
            return 0;
        }

        Object entry = all.FirstOrDefault(o => o != null && o.name == ContourEntryName);
        int changed = 0;
        bool created = false;

        if (entry == null && !write)
        {
            Debug.Log($"[Menus] contour: '{ContourEntryName}' would be created in {OutlineSettingsPath}, "
                      + $"modelled on '{ContourTemplateEntryName}'.");
            ReportContourWiring();
            return 1;
        }

        if (entry == null)
        {
            // CreateInstance and copy named properties, NOT Instantiate(template). The
            // difference matters: Outline holds a runtime `material` it builds in OnEnable
            // with HideAndDontSave, so it serializes as null but is non-null in memory. A
            // clone would carry a reference to the TEMPLATE'S material instance, and
            // FreeOutline writes _OutlineColor and _OutlineWidth into outline.material
            // every frame — two entries sharing one material means the last one processed
            // wins for both, and the board's contour would come out paper-coloured and
            // 6 px wide. A fresh instance builds its own.
            entry = ScriptableObject.CreateInstance(template.GetType());
            entry.name = ContourEntryName;
            AssetDatabase.AddObjectToAsset(entry, settings);
            created = true;
            changed++;
        }

        var so = new SerializedObject(entry);

        // The proven-behaviour set: how the edge is extruded, depth-tested, masked and
        // blended. Copied from the board's contour rather than typed, because clip-space
        // normal extrusion gaps on split normals and "it already produces a clean edge on
        // these exact character meshes" is evidence no fresh value could claim.
        if (created && write)
        {
            var from = new SerializedObject(template);
            foreach (string path in new[]
                     {
                         "isActive", "renderQueue", "occlusion", "maskingStrategy", "blendMode",
                         "extrusionMethod", "scaling", "width", "minWidth", "referenceResolution",
                         "customResolution", "materialType", "customMaterial", "enableOcclusion",
                         "occludedColor",
                     })
            {
                SerializedProperty p = from.FindProperty(path);
                if (p != null) so.CopyFromSerializedProperty(p);
            }
        }

        // The contour bit ALONE, not Default alongside it. Unity's FilteringSettings culls
        // with a bitwise AND, so an entry that also accepted Default would accept the table
        // plane — and a paper line traced along the horizon is the one edge in frame that
        // must not be drawn. The pieces carry Default | contour; the entry tests only for
        // the contour bit; the table carries only Default and is excluded.
        changed += SetUInt(so, "RenderingLayer.m_Bits", 1u << ContourRenderingLayerIndex, write);
        // Everything. The rendering-layer filter above is already exclusive to menu pieces,
        // so a second filter on GameObject layers would only be a way to break the wiring
        // later — and it is what forced the BlueTeam borrow when this rode on Silhouette.
        changed += SetInt(so, "layerMask.m_Bits", ~0, write);
        changed += SetColour(so, "color", Hex(ContourColourHex), write);
        // Pixels at any resolution, matching the board. Turning this on would make the
        // contour a fraction of frame height instead, which is a different decision from a
        // different pass.
        changed += SetBool(so, "scaleWithResolution", false, write);
        if (write && so.hasModifiedProperties) so.ApplyModifiedPropertiesWithoutUndo();

        // ASSERTED, not owned. §4.3.1 permits exceeding the floor for style and forbids
        // exceeding the cap, so a later widening is a decision to respect rather than
        // overwrite — but falling below the static floor is a defect, because a contour
        // stuck at a bad sub-pixel phase for the whole screen's life loses half its coverage
        // to the neighbouring pixel.
        SerializedProperty widthProp = so.FindProperty("width");
        float boardWidth = new SerializedObject(template).FindProperty("width")?.floatValue ?? 0f;
        if (widthProp != null && widthProp.floatValue + Mathf.Epsilon < ContourStaticFloorPixels)
        {
            Debug.LogWarning($"[Menus] contour width is {widthProp.floatValue:0.00} px, below §4.3.1's "
                             + $"{ContourStaticFloorPixels:0.00} px static-contour floor. A menu piece "
                             + "holds its screen position, so it is stuck at whatever sub-pixel phase it "
                             + "landed on and the worst phase must still clear 3.0:1.");
        }
        else if (widthProp != null && !Approximately(widthProp.floatValue, boardWidth))
        {
            Debug.Log($"[Menus] contour width is {widthProp.floatValue:0.00} px against the board's "
                      + $"{boardWidth:0.00} px. Above the floor, so permitted as style by §4.3.1 — noted "
                      + "rather than reverted. The cap it must not cross is a quarter of the NARROWEST "
                      + "silhouette dimension in frame, which is a rifle barrel or a pogo shaft, not a "
                      + "torso.");
        }

        if (created && write)
        {
            var list = new SerializedObject(settings);
            SerializedProperty outlines = list.FindProperty("outlines");
            if (outlines == null || !outlines.isArray)
            {
                Debug.LogError($"[Menus] could not find the 'outlines' list on {OutlineSettingsPath}; the "
                               + $"'{ContourEntryName}' entry exists as a sub-asset but is not referenced, "
                               + "so it will not render. Add it in the inspector.");
            }
            else
            {
                int i = outlines.arraySize;
                outlines.InsertArrayElementAtIndex(i);
                outlines.GetArrayElementAtIndex(i).objectReferenceValue = entry;
                list.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Menus] contour: created '{ContourEntryName}' in {OutlineSettingsPath} as outline "
                      + $"{outlines?.arraySize ?? 0}, extrusion and occlusion modelled on "
                      + $"'{ContourTemplateEntryName}'.");
        }
        else if (changed > 0)
        {
            Debug.Log($"[Menus] contour: {changed} value(s) on '{ContourEntryName}' "
                      + $"{(write ? "written" : "would change")}.");
        }

        ReportContourWiring();
        return changed;
    }

    /// <summary>
    /// Reports the two things this contour depends on that live outside this file: the
    /// rendering layer name in TagManager (broker scope — ProjectSettings is not ours to
    /// write) and the measured contrast the paper colour actually achieves.
    /// </summary>
    private static void ReportContourWiring()
    {
        Object[] tags = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
        Object tagManager = tags?.FirstOrDefault(o => o != null);
        string found = null;
        int count = 0;

        if (tagManager != null)
        {
            SerializedProperty layers = new SerializedObject(tagManager).FindProperty("m_RenderingLayers");
            if (layers != null && layers.isArray)
            {
                count = layers.arraySize;
                if (ContourRenderingLayerIndex < count)
                    found = layers.GetArrayElementAtIndex(ContourRenderingLayerIndex).stringValue;
            }
        }

        if (found == ContourRenderingLayerName)
        {
            Debug.Log($"[Menus] contour layer: rendering layer {ContourRenderingLayerIndex} is "
                      + $"'{found}'. Wiring complete.");
        }
        else if (found == null)
        {
            Debug.LogWarning($"[Menus] contour layer: {TagManagerPath} declares {count} rendering "
                             + $"layer(s), so index {ContourRenderingLayerIndex} does not exist yet. The "
                             + "contour entry and the pieces are both already set to bit "
                             + $"{1u << ContourRenderingLayerIndex} and will render the moment the layer "
                             + $"is added — the mask is a uint and does not need a name to work; the name "
                             + "is what stops the next person reusing the bit. ProjectSettings is broker "
                             + $"scope: add '{ContourRenderingLayerName}' at index "
                             + $"{ContourRenderingLayerIndex}.");
        }
        else
        {
            Debug.LogError($"[Menus] contour layer: rendering layer {ContourRenderingLayerIndex} is "
                           + $"'{found}', not '{ContourRenderingLayerName}'. Something else has claimed "
                           + "the bit this contour filters on, so the menu pieces and that feature will "
                           + "now draw over each other. Resolve before shipping.");
        }

        // Stated in the log rather than only in a document, because the ceiling is the
        // thing somebody will otherwise try to fix by lightening the bed — which would be
        // fixing the wrong end of a load-bearing idea.
        Debug.Log("[Menus] contour contrast, --bp-deck-0 paper on --bp-table: 7.13:1 against a 3.0:1 "
                  + "non-text threshold, and 7.32:1 if the pipeline reads the stored colour as linear "
                  + "rather than sRGB. Both readings pass, which is a second reason to be on the light "
                  + "side of the bed: the same ambiguity put the old dark contour somewhere between "
                  + "1.82:1 and 2.69:1, and neither end of that was a pass. The dark family cannot get "
                  + "there at all here — ink is 2.33:1 and banned by §7.3, pure black is 2.80:1, and "
                  + "solving the ratio for 3.0:1 on the dark side needs NEGATIVE luminance. Against the "
                  + "pieces themselves paper runs 14.26:1 on Char_Black down to 2.86:1 on Char_Skin; "
                  + "that lowest pairing is contour-to-figure, not the graded contour-to-ground one, and "
                  + "no single colour clears 3.0:1 against both this bed and that skin — so the worst "
                  + "contrast anywhere along the edge still improves from ink's 2.33:1 to 2.86:1.");
    }

    private static int ApplyOutline(SceneSpec spec, GameObject go, PieceSpec piece, bool write)
    {
        int changed = 0;
        // Default is carried alongside the contour bit. URP_Renderer ships
        // m_SupportsLightLayers 0, so the sun's own rendering-layer mask (1, Default only,
        // in all four scenes) is ignored today — but the day somebody turns light layers on,
        // a contour-only piece would stop being lit while the board stayed fine. The bit
        // costs nothing: no feature filters on Default.
        uint wanted = 1u | (1u << ContourRenderingLayerIndex);

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (r.renderingLayerMask == wanted) continue;

            if (write)
            {
                r.renderingLayerMask = wanted;
                EditorUtility.SetDirty(r);
            }
            changed++;
        }

        // The GameObject layer goes back to Default on every node in the hierarchy. An
        // earlier draft rode on the board's Silhouette entry, whose layerMask is 192, and
        // so had to park these decorations on BlueTeam to be seen at all. The contour's own
        // entry filters on Everything, so the borrow is no longer needed — and a menu prop
        // sitting on a team layer was always a thing to explain rather than a thing to want.
        foreach (Transform t in go.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (t.gameObject.layer == ContourGameObjectLayer) continue;
            if (write)
            {
                t.gameObject.layer = ContourGameObjectLayer;
                EditorUtility.SetDirty(t.gameObject);
            }
            changed++;
        }

        if (changed > 0)
        {
            Debug.Log($"[Menus] {spec.Label}: '{piece.Name}' -> contour on. {changed} renderer/layer "
                      + $"value(s) moved onto rendering bit {wanted} (Default | "
                      + $"{ContourRenderingLayerName}) and GameObject layer "
                      + $"{LayerMask.LayerToName(ContourGameObjectLayer)}.");
        }

        return changed;
    }

    // -------------------------------------------------- serialized setters
    // Each returns 1 if the value differs, so verify mode counts exactly what a build
    // would write without writing any of it.

    private static int SetUInt(SerializedObject so, string path, uint value, bool write)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p == null || p.uintValue == value) return 0;
        if (write) p.uintValue = value;
        return 1;
    }

    private static int SetInt(SerializedObject so, string path, int value, bool write)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p == null || p.intValue == value) return 0;
        if (write) p.intValue = value;
        return 1;
    }

    private static int SetFloat(SerializedObject so, string path, float value, bool write)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p == null || Approximately(p.floatValue, value)) return 0;
        if (write) p.floatValue = value;
        return 1;
    }

    private static int SetBool(SerializedObject so, string path, bool value, bool write)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p == null || p.boolValue == value) return 0;
        if (write) p.boolValue = value;
        return 1;
    }

    private static int SetColour(SerializedObject so, string path, Color value, bool write)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p == null || Approximately(p.colorValue, value)) return 0;
        if (write) p.colorValue = value;
        return 1;
    }

    // ============================================================== materials

    private static int ApplyPieceMaterials(SceneSpec spec, GameObject go, PieceSpec piece, bool write)
    {
        Renderer[] mine = SortedRenderers(go);
        if (mine.Length == 0) return 0;

        var wanted = new Dictionary<Renderer, Material[]>();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(piece.PairedPrefab);
        if (prefab == null)
        {
            Debug.LogWarning($"[Menus] {spec.Label}: '{piece.Name}' has no paired prefab at "
                             + $"{piece.PairedPrefab}; only the saturation guard can run.");
        }
        else
        {
            string prefabName = System.IO.Path.GetFileName(piece.PairedPrefab);
            GameObject source = prefab;

            // Scope to a subtree where one is declared. Without this the sniper's match is
            // asked to choose between two different characters that share Blender's default
            // mesh names, which it cannot do and should not try.
            if (piece.PairedSubtree != null)
            {
                Transform sub = FindSubtree(prefab.transform, piece.PairedSubtree);
                if (sub == null)
                {
                    Debug.LogError($"[Menus] {spec.Label}: '{piece.Name}' declares paired subtree "
                                   + $"'{piece.PairedSubtree}' but {prefabName} has no such child. Either "
                                   + "the prefab was restructured or the path is stale; matching against "
                                   + "the whole prefab instead would reintroduce the ambiguity this "
                                   + "subtree exists to remove, so nothing is matched.");
                    source = null;
                }
                else
                {
                    source = sub.gameObject;
                    prefabName = $"{prefabName} / {piece.PairedSubtree}";
                }
            }

            Renderer[] theirs = source == null
                ? System.Array.Empty<Renderer>()
                : SortedRenderers(source);

            // Route 1 — MESH NAME. Not path, and the reason is worth stating because path
            // was the obvious first choice and it does not work: a unit prefab wraps its
            // model under a Unit root carrying Health, Shooting and the vision cone, so the
            // same renderer is at "Soldier/Torso" there and at "Torso" in the menu scene.
            // Every path would miss by exactly one level, and a suffix match would then have
            // to guess when two subtrees end the same way.
            //
            // Mesh names come from the FBX and survive both the wrapper and a re-export, so
            // they are the thing that actually identifies "the same part of the same
            // character". Names appearing more than once with DIFFERENT materials are
            // dropped from the map rather than resolved arbitrarily.
            var byMesh = new Dictionary<string, Material[]>();
            var ambiguous = new HashSet<string>();
            foreach (Renderer r in theirs)
            {
                string mesh = MeshName(r);
                if (mesh == null) continue;

                if (byMesh.TryGetValue(mesh, out Material[] existing))
                {
                    if (!SameMaterials(existing, r.sharedMaterials)) ambiguous.Add(mesh);
                }
                else
                {
                    byMesh[mesh] = r.sharedMaterials;
                }
            }
            foreach (string a in ambiguous) byMesh.Remove(a);

            int hits = 0;
            var missed = new List<string>();
            foreach (Renderer r in mine)
            {
                string mesh = MeshName(r);
                if (mesh != null
                    && byMesh.TryGetValue(mesh, out Material[] mats)
                    && mats.Length == r.sharedMaterials.Length)
                {
                    wanted[r] = mats;
                    hits++;
                }
                else
                {
                    missed.Add($"{RelativePath(go.transform, r.transform)} (mesh '{mesh ?? "?"}')");
                }
            }

            if (missed.Count == 0)
            {
                Debug.Log($"[Menus] {spec.Label}: '{piece.Name}' — all {hits} renderers resolved against "
                          + $"{prefabName} by mesh name.");
            }
            else
            {
                Debug.LogWarning($"[Menus] {spec.Label}: '{piece.Name}' — {hits}/{mine.Length} renderers "
                                 + $"resolved against {prefabName} by mesh name. Unresolved, and left "
                                 + $"alone rather than guessed:\n    {string.Join("\n    ", missed)}\n"
                                 + "Run Report Menu Materials and pin these explicitly. A wrong Char_* is "
                                 + "worse than a known gap — except where the guards below fire, which "
                                 + "are breaches rather than preferences.");
            }
        }

        // Declared pins, applied last so they win, and audited so they can be reviewed rather
        // than trusted. Each one logs whether the match already agreed with it, disagreed with
        // it, or found nothing — which is the difference between a pin that is redundant
        // insurance and a pin that is load-bearing, and that is exactly what a reader needs to
        // know before deleting one.
        foreach (PinSpec pin in piece.Pins)
        {
            Renderer target = mine.FirstOrDefault(
                r => RelativePath(go.transform, r.transform) == pin.Path);
            if (target == null)
            {
                Debug.LogError($"[Menus] {spec.Label}: '{piece.Name}' pins '{pin.Path}' but no renderer "
                               + "sits there. A stale pin is worse than none, because it reads as "
                               + "coverage. Re-run Report Menu Materials and correct the path.");
                continue;
            }

            int slots = target.sharedMaterials.Length;
            if (pin.Materials.Length != slots)
            {
                Debug.LogError($"[Menus] {spec.Label}: '{piece.Name}' pins {pin.Materials.Length} "
                               + $"material(s) at '{pin.Path}' but it has {slots} submesh slot(s). "
                               + "Not applied — a partial slot list would silently leave the tail on "
                               + "embedded colour.");
                continue;
            }

            var pinned = new Material[slots];
            bool complete = true;
            for (int i = 0; i < slots; i++)
            {
                pinned[i] = LoadCharacterMaterial(pin.Materials[i]);
                if (pinned[i] != null) continue;
                Debug.LogError($"[Menus] {spec.Label}: '{piece.Name}' pin '{pin.Path}' slot {i} names "
                               + $"'{pin.Materials[i]}', which is not in {CharacterMaterialFolder}.");
                complete = false;
            }
            if (!complete) continue;

            string verdict = !wanted.TryGetValue(target, out Material[] matched)
                ? "the mesh-name match found nothing here, so the pin is the only source"
                : SameMaterials(matched, pinned)
                    ? "the mesh-name match independently agreed, so the pin is redundant insurance "
                      + "against a re-export and can be deleted if that stops mattering"
                    : $"the mesh-name match wanted {string.Join(", ", matched.Select(m => m?.name ?? "<none>"))}"
                      + " and the pin OVERRIDES it — reconcile these, one of them is wrong";

            Debug.Log($"[Menus] {spec.Label}: '{piece.Name}' pin '{pin.Path}' -> "
                      + $"{string.Join(", ", pin.Materials)}. Audit: {verdict}.\n    Reasoning: {pin.Why}");

            wanted[target] = pinned;
        }

        // The guards. Anything still bound to a material that lives INSIDE an .fbx is an
        // embedded sub-asset carrying raw DCC colour. Two axes are checked, because §8's cap
        // is a chroma budget and a neutral has no chroma: saturation catches the pogo stick's
        // 100% blue, and lightness catches the sniper's pure black and pure white, which the
        // saturation test passed at 0% and which are equally off-palette. Both become
        // Char_TeamBase, which is what PogoRider.prefab puts on the pogo frame anyway.
        Material holding = LoadCharacterMaterial(SaturationHoldingMaterial);
        foreach (Renderer r in mine)
        {
            Material[] slots = wanted.TryGetValue(r, out Material[] w)
                ? (Material[])w.Clone()
                : (Material[])r.sharedMaterials.Clone();

            for (int i = 0; i < slots.Length; i++)
            {
                if (!IsEmbedded(slots[i])) continue;
                if (!TryHsl(slots[i], out float sat, out float lightness)) continue;

                string breach = null;
                if (sat > CharacterSaturationCap)
                {
                    breach = $"{sat:P0} HSL saturation, over §8's {CharacterSaturationCap:P0} character "
                             + "cap";
                }
                else if (lightness < PaletteLightnessFloor)
                {
                    breach = $"HSL lightness {lightness:P1}, below --bp-ink's {PaletteLightnessFloor:P1} — "
                             + "darker than the darkest value in the game";
                }
                else if (lightness > PaletteLightnessCeiling)
                {
                    breach = $"HSL lightness {lightness:P1}, above --bp-deck-0's "
                             + $"{PaletteLightnessCeiling:P1} — lighter than paper";
                }

                if (breach == null) continue;

                Debug.LogWarning($"[Menus] {spec.Label}: '{piece.Name}' / "
                                 + $"{RelativePath(go.transform, r.transform)} slot {i} is embedded "
                                 + $"'{slots[i].name}' at {breach} -> {SaturationHoldingMaterial}. This is "
                                 + "un-regraded DCC output, not an authored colour. A neutral holding "
                                 + "value is not the right answer, only a palette-legal one — resolve it "
                                 + "with a pin or a subtree.");
                if (holding != null) slots[i] = holding;
            }

            wanted[r] = slots;
        }

        int changed = 0;
        foreach (KeyValuePair<Renderer, Material[]> pair in wanted)
        {
            if (SameMaterials(pair.Key.sharedMaterials, pair.Value)) continue;
            if (write)
            {
                pair.Key.sharedMaterials = pair.Value;
                EditorUtility.SetDirty(pair.Key);
            }
            changed++;
        }

        if (changed > 0)
        {
            Debug.Log($"[Menus] {spec.Label}: '{piece.Name}' — {changed} renderer(s) rebound to shared "
                      + "Char_* assets. Colour now lives at the level the board uses, where a palette "
                      + "revision reaches it.");
        }

        return changed;
    }

    private static void InventoryMaterials(SceneSpec spec)
    {
        foreach (PieceSpec piece in spec.Pieces)
        {
            GameObject go = FindStagedRoot(piece.Name);
            if (go == null) continue;

            var lines = new List<string>
            {
                $"[Menus] {spec.Label} / '{piece.Name}' material inventory "
                + $"(paired prefab {piece.PairedPrefab}):"
            };

            foreach (Renderer r in SortedRenderers(go))
            {
                Material[] slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material m = slots[i];
                    string where = m == null ? "<none>" : AssetDatabase.GetAssetPath(m);
                    bool hsl = TryHsl(m, out float s, out float l);
                    string sat = hsl ? $"{s:P0}" : "n/a";
                    string lit = hsl ? $"{l:P1}" : "n/a";
                    string flag = m != null && IsEmbedded(m) ? "EMBEDDED" : "shared";
                    lines.Add($"    mesh '{MeshName(r) ?? "?"}' @ {RelativePath(go.transform, r.transform)} "
                              + $"[{i}] {m?.name ?? "<none>"}  {flag}  S {sat}  L {lit}  {where}");
                }
            }

            // The other half of the pin: what the board binds, keyed the same way, so a
            // declared override can be written from one log instead of two.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(piece.PairedPrefab);
            if (prefab != null)
            {
                // The whole prefab, not the declared subtree, and on purpose: the point of this
                // report is to make a subtree or pin decision reviewable, which needs the
                // alternatives visible. Scoping it here would hide the legacy copy that made
                // the sniper ambiguous in the first place.
                Transform sub = piece.PairedSubtree == null
                    ? null
                    : prefab.transform.Find(piece.PairedSubtree);
                lines.Add($"  -- {System.IO.Path.GetFileName(piece.PairedPrefab)} binds (whole prefab; "
                          + (piece.PairedSubtree == null
                              ? "no subtree declared):"
                              : $"'{piece.PairedSubtree}' is the declared subtree and is marked *):"));

                foreach (Renderer r in SortedRenderers(prefab))
                {
                    string mats = string.Join(", ", r.sharedMaterials.Select(m => m?.name ?? "<none>"));
                    bool inSubtree = sub != null && r.transform.IsChildOf(sub);
                    lines.Add($"   {(inSubtree ? "*" : " ")} mesh '{MeshName(r) ?? "?"}' @ "
                              + $"{RelativePath(prefab.transform, r.transform)} -> {mats}");
                }
            }

            Debug.Log(string.Join("\n", lines));
        }
    }

    /// <summary>
    /// Hierarchy order, which is stable for a given asset and therefore safe to index
    /// against. Skinned and mesh renderers only — a particle or trail renderer is not a
    /// material slot anybody is trying to match.
    /// </summary>
    private static Renderer[] SortedRenderers(GameObject root) =>
        root.GetComponentsInChildren<Renderer>(includeInactive: true)
            .Where(r => r is MeshRenderer or SkinnedMeshRenderer)
            .ToArray();

    /// <summary>
    /// Resolves a declared subtree by relative path first and by descendant name second. The
    /// path is what the spec means; the name search exists because a prefab author can reparent
    /// a group without renaming it, and silently matching nothing there would drop a piece back
    /// onto embedded colour without saying so. A duplicate name is refused rather than picked
    /// from, since the entire purpose of the subtree is to remove an ambiguity.
    /// </summary>
    private static Transform FindSubtree(Transform root, string path)
    {
        Transform direct = root.Find(path);
        if (direct != null) return direct;

        Transform[] matches = root.GetComponentsInChildren<Transform>(includeInactive: true)
            .Where(t => t != root && t.name == path)
            .ToArray();

        if (matches.Length == 1)
        {
            Debug.Log($"[Menus] paired subtree '{path}' is not at that path under '{root.name}' but a "
                      + $"single descendant carries the name, at '{RelativePath(root, matches[0])}'. Using "
                      + "it — update the spec's path to match.");
            return matches[0];
        }

        if (matches.Length > 1)
        {
            Debug.LogError($"[Menus] paired subtree '{path}' matches {matches.Length} descendants of "
                           + $"'{root.name}'. Refusing to choose: an ambiguous subtree cannot resolve an "
                           + "ambiguity.");
        }

        return null;
    }

    private static string MeshName(Renderer r)
    {
        if (r is SkinnedMeshRenderer skinned) return skinned.sharedMesh?.name;
        MeshFilter filter = r.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh?.name : null;
    }

    private static string RelativePath(Transform root, Transform t)
    {
        if (t == root) return ".";
        var parts = new List<string>();
        for (Transform c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>A material that lives inside a model file is an importer-generated sub-asset:
    /// read-only, sourced from the DCC, and unreachable by any pass over Assets/Materials.</summary>
    private static bool IsEmbedded(Material m)
    {
        if (m == null) return false;
        string path = AssetDatabase.GetAssetPath(m);
        return path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".blend", System.StringComparison.OrdinalIgnoreCase);
    }

    private static Material LoadCharacterMaterial(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>($"{CharacterMaterialFolder}/{name}.mat");

    /// <summary>
    /// HSL saturation and lightness, which is the instrument §8 insists on — HSV reports a
    /// different number for the same colour, in both directions, and would pass things the
    /// table means to stop while inventing breaches that are not there.
    /// </summary>
    private static bool TryHsl(Material m, out float saturation, out float lightness)
    {
        saturation = 0f;
        lightness = 0f;
        if (m == null) return false;

        Color c;
        if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
        else if (m.HasProperty("_Color")) c = m.GetColor("_Color");
        else return false;

        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        lightness = (max + min) * 0.5f;
        if (Mathf.Approximately(max, min)) return true;

        saturation = lightness > 0.5f
            ? (max - min) / (2f - max - min)
            : (max - min) / (max + min);
        return true;
    }

    private static bool SameMaterials(Material[] a, Material[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    // ============================================================== helpers

    // Same tolerance ArenaBuilder uses for environment drift, for the same reason: a
    // colour picker round-trips through 8-bit, so an exact float compare reports drift
    // that nobody introduced.
    private const float Tolerance = 0.02f;

    private static bool Approximately(float a, float b) => Mathf.Abs(a - b) <= Tolerance;

    private static bool Approximately(Color a, Color b) =>
        Approximately(a.r, b.r) && Approximately(a.g, b.g) && Approximately(a.b, b.b);

    private static bool Approximately(Vector3 a, Vector3 b) =>
        Approximately(a.x, b.x) && Approximately(a.y, b.y) && Approximately(a.z, b.z);

    /// <summary>Walks the path so the folder constant above can be moved without this
    /// breaking, which a hard-coded parent/child pair could not survive.</summary>
    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string built = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{built}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
            built = next;
        }
    }

    private static Color Hex(string hex)
    {
        if (!ColorUtility.TryParseHtmlString(hex, out Color c))
            Debug.LogError($"[Menus] Bad hex {hex}");
        return c;
    }
}
