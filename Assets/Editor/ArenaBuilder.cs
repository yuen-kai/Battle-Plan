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
/// Builds the board in <c>Assets/Scenes/Game.unity</c>: the checker deck, the cover kit on the
/// wall prefab, the silhouette blocks outside the board, and the lighting and post pass. Every
/// dimension is a named constant so the board is reviewable as a diff rather than as a pile of
/// hand-placed transforms, and the build is idempotent — it clears what it made last time first.
///
/// Deliberately not here: deck markings (spawn bands, chevrons, ticks), the board rail and its
/// corner posts, and any static hill pad. The hill is drawn at runtime by <c>GameLoop</c> so it
/// can be King of the Hill only and tinted by whoever holds it.
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

    private const float YCellSeparator = 0.000f;

    // cover kit envelope — all four variants share it exactly.
    private const float CoverHeight = 2.0f;
    private const float CoverBodyTop = 1.94f;   // the 0.12 cap occupies 1.94 → 2.06, centred on 2.0
    private const float CoverChamfer = 0.06f;
    private const int CoverTriBudget = 240;

    private const float BackdropBaseY = -0.8f;
    private const int BackdropBlockBudget = 40;
    private const int BackdropSeed = 20260725;


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
    // RULED: this overrides the art direction, which specifies (50, -30, 0) and calls the
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
    // Clear Flags = Solid Color, background --bp-sky, no skybox.
    //
    // Was #9CC6DC, which the hue guard below fails: 47.8% HSL saturation at 12.1 degrees
    // from team blue, against a 30% cap inside the guard band and a 12% cap for the
    // backdrop class. It is the largest area in the frame and it is a camera clear colour
    // rather than a material, so a palette-only sweep never looked at it. the palette
    // retuned it to 10.4%.
    private const string SkyHex = "#CAD1D4";
    // The camera clear colour is --bp-sky and there is deliberately no second literal for
    // it here. An earlier revision carried a recorded (0.55, 0.74, 0.82) and asserted that
    // it *was* --bp-sky; it never equalled any value the token has held, and a number that
    // resolves cleanly to the wrong thing is worse than one that resolves to nothing
    // (the art direction).
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

        public MatSpec(string name, string token, string hex, float metallic, float smoothness,
            float alpha = 1f, string emissionHex = null, float emissionGain = 0f)
        {
            Name = name; Token = token; Hex = hex; Alpha = alpha; Metallic = metallic;
            Smoothness = smoothness; EmissionHex = emissionHex; EmissionGain = emissionGain;
        }
    }

    // Smoothness stays low across the board. A bright key over a glossy deck produces
    // specular that the bloom then amplifies, and hazing the image costs every effect
    // its contrast — the deck is the one surface that must never sparkle.
    // FIELD DAY. Every value below is a token from the palette, and the token
    // name travels with it so a re-grade is a lookup rather than an inference. Nothing in
    // the arena is metallic (the art direction): on a bright board a specular highlight is
    // indistinguishable from an effect, and that one rule removes more accidental ugliness
    // than any other in the section.
    private static readonly MatSpec[] MapPalette =
    {
        // Deck. A retarget of the shipped Map_FloorDay pair rather than new assets, which
        // is the whole reason the board still reads as the one the user liked.
        //
        // A is the LIGHTER square and B the darker partner, matching the palette The two
        // were the wrong way round here, which put B at 0.706 display sRGB, outside the
        // mid-tone window below. --bp-ground-a also moved (#ABB6BB -> #A8B3B8) in palette
        // for the same reason, and lands at 0.695: at the top of the window rather
        // than over it.
        new("Map_DeckA",        "--bp-ground-a",     "#A8B3B8", 0.00f, 0.12f),
        new("Map_DeckB",        "--bp-ground-b",     "#9FAAB0", 0.00f, 0.12f),
        // Cell separator, drawn 0.12 wide. Darker than the fill: on a light board the line
        // is a groove, which is both cheaper in headroom and more legible than a highlight.
        new("Map_GridLine",     "--bp-ground-line",  "#6E7A80", 0.00f, 0.05f),

        // Cover body. A horizontal face collects materially more irradiance than a vertical
        // one under this sun, so a dark body under the light cap the wall prefab already
        // carries gives four separated steps from deck to shaded face — which is what makes
        // a 2.0 m blocker read as solid and full height from a 73-degree camera.
        new("Map_Cover",        "--bp-cover",        "#46525A", 0.00f, 0.18f),
        // The pillar variant's ID strip.
        new("Map_CoverPlate",   "--bp-ground-paint", "#E8E2D4", 0.00f, 0.18f),

        // The silhouette blocks outside the board, and the table the board sits on.
        // Backdrop is the sky darkened two rungs in sRGB: #CAD1D4 x 0.877^2. The space
        // matters — the same factor in linear gives a backdrop lighter than the deck.
        new("Map_Backdrop",     "--bp-sky (-2)",     "#9BA1A3", 0.00f, 0.05f),
        new("Map_Void",         "--bp-table",        "#5E5346", 0.00f, 0.08f),
    };

    // ---- post stack -------------------------------------------------------------
    // Written into the volume profile asset by its own menu item.
    //
    // Neutral, not ACES. ACES systematically desaturates exactly the chroma a colourful
    // board depends on, and it does the damage brightest-first, which on a bright board
    // is most of the image. One field, and it outweighs the shader work in the project.
    private const TonemappingMode Tonemap = TonemappingMode.Neutral;
    private const float PostExposure = 0f;
    // Contrast is where the ink gets its bite; saturation pushes the small
    // saturated pieces without touching the ground much, because there is little chroma
    // on the ground to push.
    private const float PostContrast = 14f;
    private const float PostSaturation = 8f;

    // Bloom threshold sits well above the deck's own specular. At 1.0 with the deck at
    // linear 0.40 the board hazes itself and every effect loses contrast. Only the seven
    // HDR values in the palette are meant to cross it.
    private const float BloomThreshold = 1.8f;
    private const float BloomIntensity = 0.5f;
    private const float BloomScatter = 0.55f;
    private const float BloomClamp = 8f;

    // sunlight, and the cool-shadow / warm-highlight split that is the single
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

    // URP shadow settings, applied by their own menu item. Distance covers the
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
    private const string MapMaterials = "Assets/Materials/Map/";
    private const string MeshFolder = "Assets/Meshes/Arena/";
    // The shipped profile is edited in place rather than a second one added beside it. The Game
    // scene already references this asset, so there is no rewiring to get wrong, and the original
    // values are recoverable from git.
    private const string VolumeProfilePath = "Assets/Settings/GameDaylight Volume Profile.asset";

    private const string ArenaRootName = "BattlePlanArena";
    private const string DeckRootName = "GridCells";
    private const string GeometryAssetPath = MeshFolder + "ArenaGeometry.asset";

    // Roots the old board left behind. Two deliberate absences: "Walls", which holds
    // disabled in-scene NetworkObjects and is a multiplayer decision rather than an art one, and
    // "Directional Light", which is the shipped sun the whole direction rests on.
    private static readonly string[] LegacyRoots = { "ArenaDressing" };

    // mirrored about column 7 and row 4.5. Read from GameLoop at build time; this copy
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

            EditorUtility.DisplayProgressBar("Arena", "Building cover kit meshes", 0.20f);
            Dictionary<string, Mesh> kit = BuildCoverKitMeshes();

            EditorUtility.DisplayProgressBar("Arena", "Rebuilding the wall prefab", 0.35f);
            RebuildWallPrefab(kit);

            EditorUtility.DisplayProgressBar("Arena", "Rebuilding the deck", 0.50f);
            GameObject arena = ResetArenaRoot(scene);
            BeginGeometryLibrary();
            RebuildDeck(scene);

            EditorUtility.DisplayProgressBar("Arena", "Placing the backdrop", 0.70f);
            BuildBackdrop(arena);

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

    // ================================================================= cover kit

    /// <summary>
    /// Four silhouettes, one envelope. Height never varies: every wall cell blocks line of sight
    /// by rule, so a prop that reads as shootable-over is a lie.
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
    /// describes. The recess itself is not modelled: 0.04 of depth is invisible from the game
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
    /// One face only: 0.9 x 0.06 is 0.054 m², and caps emissive plate area at 0.06 m² per
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
            Material plate = LoadMaterial("Map_CoverPlate");

            // The prefab's existing TopCap child covers 1.94 -> 2.06, which is exactly where the
            // kit bodies stop. It keeps the material it already has: the cap is not part of this
            // port, only the body silhouettes underneath it are.
            if (FindDescendant(root.transform, "TopCap") == null)
                Debug.LogWarning("[Arena] Wall.prefab has no TopCap child; the cover tops will be open.");

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

    // ================================================================= deck

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
                var tile = (GameObject)PrefabUtility.InstantiatePrefab(prefab, deckRoot.transform);
                tile.name = $"Deck_{column:00}_{row:00}";
                tile.transform.localPosition = new Vector3(column * Cell, YCellSeparator, row * Cell);

                // Every cell is a plain checker square. Spawn ranks and the hill pad are not
                // painted into the deck: the hill is drawn at runtime by GameLoop, and only in
                // King of the Hill.
                Material face = (column + row) % 2 == 1 ? deckB : deckA;

                Transform inner = FindDescendant(tile.transform, "Inner");
                if (inner != null && inner.TryGetComponent(out MeshRenderer innerRenderer))
                    innerRenderer.sharedMaterial = face;

                GameObjectUtility.SetStaticEditorFlags(tile, StaticEditorFlags.BatchingStatic);
            }
        }

        Debug.Log($"[Arena] Deck rebuilt: {Columns * Rows} checker tiles.");
    }

    /// <summary>
    /// the separator narrows from 0.20 to 0.12 world units. That is the single change to the
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
                Debug.LogWarning($"[Arena] '{cam.name}' clears with {cam.clearFlags}. The board wants "
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
    }

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
