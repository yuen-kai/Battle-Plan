using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What is still there a third of a second after the flash: the evidence that something happened.
/// <para>
/// Without this, an impact is a light that switched on and off and the board resets to exactly its
/// previous state. Aftermath is the slow half of the effect — a burn taken out of the floor, a
/// plume that climbs and leans off it, fire in the crater, sparks thrown clear and coals still
/// alight in the char once the rest of it has gone — and it is what carries the hit's weight long
/// after the bright frames are gone.
/// </para>
/// <para>
/// The board floor sits at luminance 181 of 255, so everything structural here is darker than the
/// floor and opaque. Light is a garnish laid inside that dark material, never the silhouette.
/// </para>
/// <para>
/// Nothing here adds to the board. The burn multiplies it, the smoke and the fire and the sparks
/// all cover it. On a floor this bright an additive layer inherits the floor's blue and cannot hold
/// a hue: the only way to put saturated colour on this board is to occlude it first.
/// </para>
/// <para>
/// It must also know when to leave. Nothing here may survive long enough to obscure the next
/// round's planning.
/// </para>
/// <para>Purely local and visual. Call on every peer at the moment of impact.</para>
/// </summary>
public static class Aftermath
{
    /// <summary>
    /// Leaves the consequence of an impact at <paramref name="position"/>.
    /// </summary>
    /// <param name="position">World position of the hit.</param>
    /// <param name="color">Ability colour, for tinting embers and smoke.</param>
    /// <param name="radius">World-space radius the impact covered.</param>
    /// <param name="kind">Which consequence to leave.</param>
    public static void Spawn(
        Vector3 position,
        Color color,
        float radius = 2.5f,
        AftermathKind kind = AftermathKind.Scorch
    )
    {
        GameObject root = new("Aftermath");
        root.transform.position = new Vector3(position.x, 0f, position.z);
        root.AddComponent<Runner>().Build(kind, color, Mathf.Clamp(radius, 0.6f, 8f));
    }

    /// <summary>
    /// One mass of the plume. The table these come from is authored rather than generated: a plume
    /// whose parts are drawn from one distribution reads as a bunch of grapes no matter how many
    /// parts it has, so the sizes here span better than five to one, the offsets are deliberately
    /// lopsided, and every mass runs on its own clock.
    /// </summary>
    readonly struct Mass
    {
        /// <summary>Drawn width, as a multiple of the impact radius.</summary>
        public readonly float Size;
        public readonly float OffsetX;
        public readonly float OffsetZ;

        /// <summary>Share of the plume's climb and of its lean, so the column shears as it goes.</summary>
        public readonly float RiseScale;
        public readonly float LeanScale;

        /// <summary>Outward crawl along its own offset — what makes the low dust spread and settle.</summary>
        public readonly float Outward;

        public readonly float Delay;
        public readonly float Lifetime;

        /// <summary>Scale multiple reached at the end of life, after the initial burst to full size.</summary>
        public readonly float Grow;

        /// <summary>Fraction of life held fully opaque before it starts to dissipate.</summary>
        public readonly float HoldEnd;
        public readonly float ErodeStart;
        public readonly float ErodeMax;

        /// <summary>Value multiplier: the dominant mass is the darkest thing on the board.</summary>
        public readonly float Tone;
        public readonly int Lobes;

        public Mass(
            float size,
            float offsetX,
            float offsetZ,
            float riseScale,
            float leanScale,
            float outward,
            float delay,
            float lifetime,
            float grow,
            float holdEnd,
            float erodeStart,
            float erodeMax,
            float tone,
            int lobes
        )
        {
            Size = size;
            OffsetX = offsetX;
            OffsetZ = offsetZ;
            RiseScale = riseScale;
            LeanScale = leanScale;
            Outward = outward;
            Delay = delay;
            Lifetime = lifetime;
            Grow = grow;
            HoldEnd = holdEnd;
            ErodeStart = erodeStart;
            ErodeMax = erodeMax;
            Tone = tone;
            Lobes = lobes;
        }
    }

    // One dominant body, three shoulders, three fragments and three low skirts. Ordered largest
    // first so a profile that wants a thinner plume can simply take fewer of them and still keep
    // the hierarchy. Everything is dead by 1.42s; the mark outlives it alone.
    //
    // Drawn widths run 1.69, 0.93, 0.89, 0.71, 0.60, 0.56, 0.51, 0.47 cells with the three skirts
    // at 0.87, 0.69 and 0.58, so the ladder from the dominant mass to the smallest fragment is
    // better than three to one with every rung occupied. A plume that is one mountain surrounded
    // by grit reads as a mountain surrounded by grit; the rungs in between are what make it one
    // body coming apart.
    static readonly Mass[] Masses =
    {
        new(1.52f, -0.06f, 0.04f, 1.00f, 1.00f, 0.00f, 0.00f, 1.42f, 1.14f, 0.34f, 0.28f, 0.62f, 0.86f, 5),
        new(0.84f, 0.42f, -0.23f, 0.86f, 0.78f, 0.07f, 0.05f, 1.20f, 1.28f, 0.30f, 0.24f, 0.70f, 0.94f, 4),
        // Shed sideways rather than lifted: the one mass that clears the body's rim outright, so
        // the plume has a satellite the same order of size as its shoulders and not just as its
        // grit. It leans twice as far as it climbs, which is what carries it clear.
        new(0.80f, 1.18f, -0.52f, 0.62f, 1.24f, 0.22f, 0.09f, 1.10f, 1.30f, 0.28f, 0.22f, 0.74f, 0.98f, 4),
        new(0.64f, -0.38f, 0.32f, 1.12f, 0.62f, 0.09f, 0.11f, 1.06f, 1.34f, 0.30f, 0.22f, 0.72f, 1.00f, 4),
        new(0.54f, -0.90f, -0.24f, 0.74f, 0.40f, 0.16f, 0.13f, 0.82f, 1.44f, 0.26f, 0.18f, 0.84f, 0.90f, 3),
        new(0.50f, 0.20f, 0.58f, 1.22f, 0.55f, 0.13f, 0.18f, 0.90f, 1.40f, 0.26f, 0.20f, 0.80f, 1.04f, 3),
        new(0.46f, 0.98f, 0.44f, 0.95f, 0.30f, 0.18f, 0.24f, 0.70f, 1.44f, 0.24f, 0.18f, 0.86f, 1.08f, 3),
        new(0.42f, -0.24f, -0.96f, 1.30f, 0.66f, 0.17f, 0.30f, 0.64f, 1.44f, 0.24f, 0.16f, 0.88f, 0.96f, 3),
        // The three skirts are the darkest thing on the board. They crawl along the floor past
        // whatever the board has built on it, so dust lighter than a shadowed block reads as a
        // spill rather than as displaced material — and being ground-level they are what actually
        // buries the tile seams the plume is judged on.
        new(0.78f, -0.68f, 0.20f, 0.10f, 0.06f, 0.32f, 0.00f, 0.68f, 1.62f, 0.22f, 0.16f, 0.90f, 0.64f, 3),
        new(0.62f, 0.72f, -0.34f, 0.08f, 0.05f, 0.34f, 0.03f, 0.60f, 1.62f, 0.22f, 0.14f, 0.92f, 0.68f, 3),
        new(0.52f, 0.10f, 0.78f, 0.12f, 0.05f, 0.32f, 0.06f, 0.56f, 1.70f, 0.20f, 0.14f, 0.92f, 0.60f, 3),
    };

    /// <summary>
    /// The recipe for one kind of consequence, written out so the four read against each other:
    /// what is burned into the floor, what is still alight in it, how much matter is thrown up.
    /// </summary>
    struct Profile
    {
        /// <summary>Peak density. The mark's colours are per-channel multipliers, not a film.</summary>
        public float MarkOpacity;
        public Color MarkColor;
        public Color MarkEdgeColor;
        public Color MarkAshColor;
        public float MarkScale;
        public float MarkRise;
        public float MarkHold;
        public float MarkFade;
        public float MarkSpatter;

        public float CoalIntensity;
        public float CoalScale;
        public float CoalPeak;

        /// <summary>How long the fire stays at full heat before it starts cooling.</summary>
        public float CoalHold;
        public float CoalOut;
        public Color CoalDeep;
        public Color CoalEmber;
        public Color CoalCore;
        public Color CoalGlint;
        public float CoalTint;

        /// <summary>How much of the bed survives it as separate coals. 0 leaves a cold crater.</summary>
        public float CoalRemnant;
        public float CoalRemnantHold;
        public float CoalRemnantOut;

        public int MassCount;
        public float MassScale;
        public float LifeScale;
        public Color SmokeLit;
        public Color SmokeShadow;
        public float SmokeTint;
        public float Rise;
        public float Lean;

        public int SparkCount;
        public int MoteCount;
        public Color SparkCore;
        public Color SparkTail;
        public float SparkTint;
    }

    static Profile ProfileFor(AftermathKind kind)
    {
        switch (kind)
        {
            case AftermathKind.Dust:
                // Displaced material, no burn: low skirts that spread and settle, and a pale scuff
                // where the floor was scraped clean rather than charred.
                return new Profile
                {
                    MarkOpacity = 0.86f,
                    MarkColor = new Color(0.60f, 0.575f, 0.545f),
                    MarkEdgeColor = new Color(0.85f, 0.835f, 0.815f),
                    MarkAshColor = new Color(0.90f, 0.89f, 0.875f),
                    MarkScale = 3.0f,
                    MarkRise = 0.26f,
                    MarkHold = 0.94f,
                    MarkFade = 0.6f,
                    MarkSpatter = 0.62f,
                    MassCount = 11,
                    MassScale = 1.05f,
                    LifeScale = 0.92f,
                    SmokeLit = new Color(0.440f, 0.418f, 0.375f),
                    SmokeShadow = new Color(0.030f, 0.0285f, 0.0255f),
                    SmokeTint = 0.05f,
                    Rise = 0.52f,
                    Lean = 0.78f,
                };

            case AftermathKind.Embers:
                // An energy hit leaves almost no matter: a shallow burn, a cooling core, and motes
                // in the ability's own colour that carry on drifting out of it.
                return new Profile
                {
                    MarkOpacity = 1f,
                    MarkColor = new Color(0.40f, 0.27f, 0.38f),
                    MarkEdgeColor = new Color(0.72f, 0.62f, 0.70f),
                    MarkAshColor = new Color(0.80f, 0.72f, 0.79f),
                    MarkScale = 2.5f,
                    MarkRise = 0.2f,
                    MarkHold = 0.9f,
                    MarkFade = 0.55f,
                    MarkSpatter = 0.4f,
                    CoalIntensity = 0.85f,
                    CoalScale = 1.5f,
                    CoalPeak = 0.26f,
                    CoalHold = 0.24f,
                    CoalOut = 1f,
                    CoalDeep = new Color(0.26f, 0.05f, 0.03f),
                    CoalEmber = new Color(1.4f, 0.66f, 0.2f),
                    CoalCore = new Color(2.3f, 1.2f, 0.36f),
                    CoalGlint = new Color(3.2f, 2.6f, 1.9f),
                    CoalTint = 0.7f,
                    // An energy hit leaves less burning matter behind it than a shell does, and
                    // what it leaves goes out sooner.
                    CoalRemnant = 0.6f,
                    CoalRemnantHold = 1f,
                    CoalRemnantOut = 1.24f,
                    MassCount = 5,
                    MassScale = 0.85f,
                    LifeScale = 0.95f,
                    SmokeLit = new Color(0.330f, 0.300f, 0.300f),
                    SmokeShadow = new Color(0.020f, 0.0168f, 0.0192f),
                    SmokeTint = 0.42f,
                    Rise = 0.98f,
                    Lean = 0.86f,
                    SparkCount = 8,
                    MoteCount = 12,
                    SparkCore = new Color(0.95f, 0.72f, 0.24f),
                    SparkTail = new Color(2.1f, 0.6f, 0.12f),
                    SparkTint = 0.6f,
                };

            case AftermathKind.Smoke:
                // A column and nothing else: no burn under it and no sparks in it, so it reads as
                // cover rather than as damage.
                return new Profile
                {
                    MassCount = 11,
                    MassScale = 1.15f,
                    LifeScale = 1.3f,
                    SmokeLit = new Color(0.420f, 0.395f, 0.360f),
                    SmokeShadow = new Color(0.026f, 0.0234f, 0.0211f),
                    SmokeTint = 0.06f,
                    Rise = 1.1f,
                    Lean = 1.05f,
                };

            default:
                // The full consequence, and the one the strip's third frame is judged on: a char
                // that outlasts everything, a plume that climbs and leans clear of it, fire left in
                // the crater and sparks thrown out of the fire.
                return new Profile
                {
                    // Multipliers on whatever the burn lies over, so the mark is scorch-coloured
                    // rather than a neutral dimming: blue is crushed roughly twice as hard as red.
                    // Against the board's floor that lands at 0.78 / 0.65 / 0.47 of its rgb, mean
                    // luminance 66 under it and 93 under the deepest char.
                    MarkOpacity = 1f,
                    MarkColor = new Color(0.44f, 0.255f, 0.115f),
                    MarkEdgeColor = new Color(0.74f, 0.60f, 0.445f),
                    MarkAshColor = new Color(0.78f, 0.66f, 0.54f),
                    MarkScale = 3.3f,
                    MarkRise = 0.22f,
                    // The fire and the smoke are both out by 1.20s and the burn starts fading on
                    // the same beat, so nothing on the board is ever held still: there is no
                    // stretch where only an unchanging decal is left sitting on the tile.
                    MarkHold = 0.98f,
                    MarkFade = 0.60f,
                    MarkSpatter = 0.55f,
                    CoalIntensity = 1f,
                    // Wide enough that the plume cannot sit on all of it. At this camera the smoke
                    // covers everything within about 0.7 cells of the crater for the whole window
                    // the strip is cut from, so a fire narrower than that is a fire nobody sees.
                    CoalScale = 1.9f,
                    CoalPeak = 0.3f,
                    CoalHold = 0.32f,
                    CoalOut = 1.2f,
                    // The heat ramp, and the whole reason round two bleached. Authored so that red
                    // clips and green does not: the ember body lands at rgb (238, 199, 73),
                    // saturation 0.69, and the hot core at (253, 227, 121), saturation 0.52 and
                    // luminance 225. About half the bed clears luminance 200 while still carrying
                    // saturation over 0.45. The glint is the only white anywhere in the fire and it
                    // covers well under a hundredth of a cell.
                    CoalDeep = new Color(0.29f, 0.04f, 0.011f),
                    CoalEmber = new Color(1.55f, 0.72f, 0.145f),
                    CoalCore = new Color(2.55f, 1.26f, 0.305f),
                    CoalGlint = new Color(3.6f, 2.9f, 2.05f),
                    // Barely tinted, and that is deliberate. The ability colour on this board is a
                    // deep blue, so pulling the fire even a tenth of the way toward it lifts the
                    // ember's blue channel from 0.15 to 0.33 and takes its saturation from 0.69 to
                    // 0.39 — under the floor where colour reads at all. Ability identity is the
                    // smoke's and the shockwave's job; fire is fire-coloured.
                    CoalTint = 0.03f,
                    // The bed is one field of fire and it dies as one, which leaves the frame a
                    // second after the hit with nothing burning in it. What a fire leaves is
                    // separate lumps in the char, and they have to outlast the sheet of flame or
                    // the consequence is a spill rather than a burn that is still going.
                    CoalRemnant = 1f,
                    CoalRemnantHold = 1.3f,
                    CoalRemnantOut = 1.52f,
                    MassCount = 11,
                    MassScale = 1f,
                    LifeScale = 1f,
                    // Warm charcoal: lit tops just under the floor's value, undersides near black.
                    // Round one's plume was blue-dominant mauve at low chroma, which is the hue
                    // opposite of a cyan-grey board and is what made it read as a bruise.
                    // Shaded from 27 to 157 of 255 against a floor at 181, so the whole plume is
                    // darker than the board and a lobe lit across that range carries an internal
                    // spread near 39. The shadow end stops short of black on purpose: graded with
                    // this profile's contrast, a darker one clips to zero and reads as a hole
                    // punched in the board rather than as material.
                    SmokeLit = new Color(0.400f, 0.346f, 0.280f),
                    SmokeShadow = new Color(0.020f, 0.0164f, 0.0132f),
                    SmokeTint = 0.1f,
                    // Climb plus lean carries the plume about 1.5 cells up-screen over its life.
                    // Leaning is three times as efficient as climbing at this camera pitch and it
                    // does not drag the mass toward the lens, which is what keeps the head of the
                    // plume inside the frame.
                    Rise = 0.86f,
                    Lean = 0.96f,
                    SparkCount = 12,
                    MoteCount = 6,
                    // Premultiplied, so these are the pixel and not an addition to the floor. The
                    // tail runs rgb (234, 145, 0) to (255, 195, 0) across the intensity spread and
                    // the core lifts the head to (255, 228, 94) — saturation 0.63 at luminance 224,
                    // which is a hot spark rather than the cream one a whiter core produced.
                    SparkCore = new Color(1.05f, 0.78f, 0.2f),
                    SparkTail = new Color(2.3f, 0.52f, 0.06f),
                    SparkTint = 0.05f,
                };
        }
    }

    sealed class Runner : MonoBehaviour
    {
        // Nothing may survive into the next beat of planning.
        const float MaxLifetime = 2.7f;

        // The burn sits under the fog overlay tiles at y 0.05 so it reads as part of the floor, and
        // the fire it leaves sits just above the burn and still under the fog.
        const float MarkHeight = 0.028f;
        const float CoalHeight = 0.042f;
        const float ScatterHeight = 0.046f;

        // The lattice the surviving coals are placed on, as a multiple of the impact radius.
        // Narrow enough that the outermost coal still lands on char rather than on clean floor.
        const float ScatterSpan = 0.92f;
        const int ScatterCells = 4;

        // Nominal coal width as a multiple of the impact radius. Twelve of these, spread better
        // than two to one and cut at full coverage, are worth about 0.06 cells squared of ember
        // still inside the band a burning pixel has to occupy to read as fire at all.
        const float ScatterSpot = 0.084f;

        // Set back against the plume's lean. By the time the fire has broken up the column has
        // travelled off the crater in that direction, and a coal under it is a coal nobody sees.
        const float ScatterOffset = 0.11f;
        const float ScatterWinkOut = 0.14f;

        // A billboard under a 73 degree pitch lies almost flat, so a mass centred on the floor would
        // sink half of itself through it. Both terms scale with the mass so a small puff still sits
        // on the ground while the plume's body clears it.
        const float FloorBias = 0.22f;
        const float FloorPerSize = 0.2f;

        // The lobe cluster a smoke mass draws never fills its quad; this is the measured fraction
        // of the quad the silhouette actually spans, so sizes can be authored as drawn widths.
        const float SilhouetteSpan = 0.83f;

        // Sparks are thrown up-screen and out of the crater rather than around it. A full circle of
        // them reads as a rosette; an arc reads as material leaving in a direction.
        const float SparkArcStart = -0.9f;
        const float SparkArcSpan = 2.6f;

        // A mass is never perfectly smooth-edged, even at full density.
        const float BaseTear = 0.1f;

        static readonly int MarkColorId = Shader.PropertyToID("_MarkColor");
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        static readonly int AshColorId = Shader.PropertyToID("_AshColor");
        static readonly int AshId = Shader.PropertyToID("_Ash");
        static readonly int AshScaleId = Shader.PropertyToID("_AshScale");
        static readonly int DriftId = Shader.PropertyToID("_Drift");
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        static readonly int RimAlphaId = Shader.PropertyToID("_RimAlpha");
        static readonly int EdgeCutId = Shader.PropertyToID("_EdgeCut");
        static readonly int EdgePixelsId = Shader.PropertyToID("_EdgePixels");
        static readonly int CoreDepthId = Shader.PropertyToID("_CoreDepth");
        static readonly int LobeCountId = Shader.PropertyToID("_LobeCount");
        static readonly int SpreadId = Shader.PropertyToID("_Spread");
        static readonly int MinLobeId = Shader.PropertyToID("_MinLobe");
        static readonly int MaxLobeId = Shader.PropertyToID("_MaxLobe");
        static readonly int KnitId = Shader.PropertyToID("_Knit");
        static readonly int WarpId = Shader.PropertyToID("_Warp");
        static readonly int WarpScaleId = Shader.PropertyToID("_WarpScale");
        static readonly int BaysId = Shader.PropertyToID("_Bays");
        static readonly int BayScaleId = Shader.PropertyToID("_BayScale");
        static readonly int GritId = Shader.PropertyToID("_Grit");
        static readonly int GritScaleId = Shader.PropertyToID("_GritScale");
        static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        static readonly int MottleId = Shader.PropertyToID("_Mottle");
        static readonly int SootId = Shader.PropertyToID("_Soot");
        static readonly int SootBandId = Shader.PropertyToID("_SootBand");
        static readonly int SootCutId = Shader.PropertyToID("_SootCut");
        static readonly int SootScaleId = Shader.PropertyToID("_SootScale");
        static readonly int SpatterAmountId = Shader.PropertyToID("_SpatterAmount");
        static readonly int RayFrequencyId = Shader.PropertyToID("_RayFrequency");
        static readonly int FringeCutId = Shader.PropertyToID("_FringeCut");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int LitColorId = Shader.PropertyToID("_LitColor");
        static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");
        static readonly int ErodeId = Shader.PropertyToID("_Erode");
        static readonly int TearId = Shader.PropertyToID("_Tear");
        static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        static readonly int ShadeLowId = Shader.PropertyToID("_ShadeLow");
        static readonly int ShadeSpanId = Shader.PropertyToID("_ShadeSpan");
        static readonly int SweepId = Shader.PropertyToID("_Sweep");
        static readonly int LobeWeightId = Shader.PropertyToID("_LobeWeight");
        static readonly int CreaseId = Shader.PropertyToID("_Crease");
        static readonly int RimId = Shader.PropertyToID("_Rim");
        static readonly int LightAngleId = Shader.PropertyToID("_LightAngle");
        static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        static readonly int EmberColorId = Shader.PropertyToID("_EmberColor");
        static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        static readonly int GlintColorId = Shader.PropertyToID("_GlintColor");
        static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        static readonly int TailColorId = Shader.PropertyToID("_TailColor");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int CrackScaleId = Shader.PropertyToID("_CrackScale");
        static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        static readonly int ThresholdSlopeId = Shader.PropertyToID("_ThresholdSlope");
        static readonly int FeatherId = Shader.PropertyToID("_Feather");
        static readonly int HeatSpanId = Shader.PropertyToID("_HeatSpan");
        static readonly int CoolingId = Shader.PropertyToID("_Cooling");
        static readonly int GlintScaleId = Shader.PropertyToID("_GlintScale");
        static readonly int GlintCutId = Shader.PropertyToID("_GlintCut");
        static readonly int GlintHeatId = Shader.PropertyToID("_GlintHeat");
        static readonly int HeadWidthId = Shader.PropertyToID("_HeadWidth");
        static readonly int TailWidthId = Shader.PropertyToID("_TailWidth");
        static readonly int CoreCutId = Shader.PropertyToID("_CoreCut");
        static readonly int SolidityId = Shader.PropertyToID("_Solidity");
        static readonly int TailAlphaId = Shader.PropertyToID("_TailAlpha");
        static readonly int AgeId = Shader.PropertyToID("_Age");
        static readonly int CellsId = Shader.PropertyToID("_Cells");
        static readonly int CoalSizeId = Shader.PropertyToID("_CoalSize");
        static readonly int SizeMinId = Shader.PropertyToID("_SizeMin");
        static readonly int SizeMaxId = Shader.PropertyToID("_SizeMax");
        static readonly int JitterId = Shader.PropertyToID("_Jitter");
        static readonly int WobbleId = Shader.PropertyToID("_Wobble");
        static readonly int RimSpanId = Shader.PropertyToID("_RimSpan");
        static readonly int FlickerId = Shader.PropertyToID("_Flicker");
        static readonly int DeathStartId = Shader.PropertyToID("_DeathStart");
        static readonly int DeathEndId = Shader.PropertyToID("_DeathEnd");
        static readonly int DeathSpanId = Shader.PropertyToID("_DeathSpan");

        static readonly Vector3[] QuadVertices =
        {
            new(-0.5f, -0.5f, 0f),
            new(0.5f, -0.5f, 0f),
            new(-0.5f, 0.5f, 0f),
            new(0.5f, 0.5f, 0f),
        };
        static readonly Vector2[] QuadUvs = { new(0f, 0f), new(1f, 0f), new(0f, 1f), new(1f, 1f) };
        static readonly int[] QuadTriangles = { 0, 2, 1, 2, 3, 1 };

        static Mesh sharedQuad;

        struct Puff
        {
            public Transform Transform;
            public Material Material;
            public Mass Spec;
            public Vector3 Anchor;
            public Vector3 Outward;
            public Vector3 Sway;
            public float Size;
            public float Lifetime;
            public float RollRate;
            public float Roll;
            public float Phase;
        }

        struct Spark
        {
            public Transform Transform;
            public Material Material;
            public Vector3 Origin;
            public Vector3 Velocity;
            public float Gravity;
            public float Drag;
            public float Size;
            public float Stretch;
            public float Delay;
            public float Lifetime;
            public float Intensity;
            public float FlickerRate;
            public float Phase;
        }

        readonly List<Material> ownedMaterials = new();

        Transform cameraTransform;
        Profile profile;
        float radius;

        Transform markTransform;
        Material markMaterial;
        Vector3 markScale;

        Transform coalTransform;
        Material coalMaterial;
        float coalDiameter;

        Material scatterMaterial;

        Vector3 leanDirection;
        float spinAngle;

        Puff[] puffs;
        Spark[] sparks;
        float totalLifetime;

        public void Build(AftermathKind kind, Color color, float impactRadius)
        {
            radius = impactRadius;
            profile = ProfileFor(kind);

            Camera boardCamera = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
            if (boardCamera == null)
                boardCamera = Camera.main;
            if (boardCamera != null)
                cameraTransform = boardCamera.transform;

            // The board is viewed from one fixed pitch, so the plume always leans away from the
            // camera. Which way it leans sideways varies; that it climbs up-screen does not.
            float leanAngle = Random.Range(-0.55f, 0.55f);
            leanDirection = new Vector3(Mathf.Sin(leanAngle), 0f, Mathf.Cos(leanAngle));
            spinAngle = Random.Range(0f, Mathf.PI * 2f);

            // Callers pass anything from a team tint to an HDR beam colour; normalising keeps the
            // smoke from blowing out while preserving the ability's hue.
            Color tint = ToLdr(color);

            BuildMark();
            BuildCoals(tint);
            BuildCoalScatter(tint);
            BuildPuffs(tint);
            BuildSparks(tint);

            totalLifetime = LongestLayerLifetime();
            if (totalLifetime <= 0.01f)
            {
                Destroy(gameObject);
                return;
            }

            StartCoroutine(Animate());
        }

        void BuildMark()
        {
            if (profile.MarkOpacity <= 0.005f)
                return;

            Shader shader = Shader.Find("BattlePlan/AftermathScorch");
            MeshRenderer renderer = CreateGroundQuad("Mark", shader, MarkHeight, out markMaterial);
            if (renderer == null)
                return;

            // Off-centre from the plume it sits under, stretched along an arbitrary axis and spun:
            // three cheap ways to guarantee the burn shares no axis with the board or the smoke.
            float drift = radius * Random.Range(0.08f, 0.17f);
            float driftAngle = Random.Range(0f, Mathf.PI * 2f);
            float aspect = Random.Range(0.86f, 1.18f);
            float diameter = radius * profile.MarkScale;

            markTransform = renderer.transform;
            markTransform.localPosition = new Vector3(
                Mathf.Cos(driftAngle) * drift,
                MarkHeight,
                Mathf.Sin(driftAngle) * drift
            );
            markTransform.localRotation =
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Quaternion.Euler(90f, 0f, 0f);
            markScale = new Vector3(diameter * aspect, diameter / aspect, 1f);
            markTransform.localScale = markScale;

            markMaterial.SetColor(MarkColorId, profile.MarkColor);
            markMaterial.SetColor(EdgeColorId, profile.MarkEdgeColor);
            markMaterial.SetColor(AshColorId, profile.MarkAshColor);
            markMaterial.SetFloat(AshId, 0f);
            markMaterial.SetFloat(AshScaleId, Random.Range(4f, 5f));
            markMaterial.SetFloat(OpacityId, 0f);
            markMaterial.SetFloat(RimAlphaId, 0.34f);
            // Cut where the old ramp crossed half, so the burn keeps the footprint it had and only
            // the transition changes: the floor now goes to char in about three pixels instead of
            // over twenty. Everything about how deep the char is stays where it was measured.
            markMaterial.SetFloat(EdgeCutId, Random.Range(0.042f, 0.056f));
            markMaterial.SetFloat(EdgePixelsId, 2.2f);
            markMaterial.SetFloat(CoreDepthId, Random.Range(0.38f, 0.46f));
            markMaterial.SetFloat(LobeCountId, 4f);
            markMaterial.SetFloat(SpreadId, Random.Range(0.25f, 0.30f));
            markMaterial.SetFloat(MinLobeId, 0.15f);
            markMaterial.SetFloat(MaxLobeId, 0.27f);
            markMaterial.SetFloat(KnitId, 0.14f);
            markMaterial.SetFloat(WarpId, Random.Range(0.13f, 0.16f));
            markMaterial.SetFloat(WarpScaleId, Random.Range(3.2f, 4f));
            // Two or three bays worth a twelfth of the mark's width, with a finer raggedness on
            // top of them. A burn that reaches further in some directions than others cannot be
            // read as a stamp however hard its edge is.
            markMaterial.SetFloat(BaysId, Random.Range(0.42f, 0.54f));
            markMaterial.SetFloat(BayScaleId, Random.Range(0.30f, 0.42f));
            markMaterial.SetFloat(GritId, Random.Range(0.11f, 0.16f));
            markMaterial.SetFloat(GritScaleId, Random.Range(1.1f, 1.5f));
            markMaterial.SetFloat(NoiseScaleId, Random.Range(6.5f, 8.5f));
            markMaterial.SetFloat(MottleId, 0.3f);
            markMaterial.SetFloat(SootId, 0.78f);
            markMaterial.SetFloat(SootBandId, 0.06f);
            markMaterial.SetFloat(SootCutId, 0.68f);
            markMaterial.SetFloat(SootScaleId, Random.Range(15f, 20f));
            markMaterial.SetFloat(SpatterAmountId, profile.MarkSpatter);
            markMaterial.SetFloat(RayFrequencyId, Random.Range(2.8f, 4.2f));
            markMaterial.SetFloat(FringeCutId, 0.605f);
            markMaterial.SetFloat(SeedId, Random.Range(0f, 30f));
        }

        void BuildCoals(Color tint)
        {
            if (profile.CoalIntensity <= 0.01f)
                return;

            Shader shader = Shader.Find("BattlePlan/AftermathCoals");
            MeshRenderer renderer = CreateGroundQuad("Coals", shader, CoalHeight, out coalMaterial);
            if (renderer == null)
                return;

            coalTransform = renderer.transform;
            coalDiameter = radius * profile.CoalScale;
            coalTransform.localScale = Vector3.one * coalDiameter;

            coalMaterial.SetColor(DeepColorId, TintHot(profile.CoalDeep, tint, profile.CoalTint));
            coalMaterial.SetColor(EmberColorId, TintHot(profile.CoalEmber, tint, profile.CoalTint));
            coalMaterial.SetColor(CoreColorId, TintHot(profile.CoalCore, tint, profile.CoalTint * 0.5f));
            coalMaterial.SetColor(GlintColorId, TintHot(profile.CoalGlint, tint, profile.CoalTint * 0.3f));
            coalMaterial.SetFloat(IntensityId, 0f);
            // Enough coals across the bed that one seed cannot land a sparse draw: at this count
            // the covered fraction varies by a seventh between hits instead of by a factor of four.
            coalMaterial.SetFloat(CrackScaleId, Random.Range(11.5f, 13.5f));
            coalMaterial.SetFloat(ThresholdId, 0.428f);
            coalMaterial.SetFloat(ThresholdSlopeId, 0.2f);
            // Coverage is cut in a fiftieth of the noise range and colour is graded over fourteen
            // times that. Sharing one ramp is what put the whole bed on its hottest colour last
            // round; separating them is what gives a coal a rim, a body and a core.
            coalMaterial.SetFloat(FeatherId, 0.022f);
            coalMaterial.SetFloat(HeatSpanId, 0.3f);
            coalMaterial.SetFloat(CoreCutId, 0.36f);
            coalMaterial.SetFloat(CoolingId, 0.24f);
            coalMaterial.SetFloat(GlintScaleId, Random.Range(34f, 42f));
            coalMaterial.SetFloat(GlintCutId, 0.78f);
            coalMaterial.SetFloat(GlintHeatId, 0.72f);
            coalMaterial.SetFloat(SeedId, Random.Range(0f, 30f));
        }

        /// <summary>
        /// What the bed leaves once it has broken up. A sheet of fire that goes out as one sheet
        /// takes every burning pixel with it, and a burn with nothing alight in it a second later
        /// is a spill; the same area spent as a dozen separate coals is the remains of a fire.
        /// They are placed on a jittered lattice with its corners dropped, so the count is fixed
        /// at twelve, none of them clump, and none of them lands off the char.
        /// </summary>
        void BuildCoalScatter(Color tint)
        {
            if (profile.CoalIntensity <= 0.01f || profile.CoalRemnant <= 0.01f)
                return;

            Shader shader = Shader.Find("BattlePlan/AftermathCoalScatter");
            MeshRenderer renderer = CreateGroundQuad(
                "Coals Left",
                shader,
                ScatterHeight,
                out scatterMaterial
            );
            if (renderer == null)
                return;

            Vector3 back = leanDirection * (-radius * ScatterOffset);
            float side = radius * ScatterSpan;

            Transform scatter = renderer.transform;
            scatter.localPosition = new Vector3(back.x, ScatterHeight, back.z);
            scatter.localRotation =
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Quaternion.Euler(90f, 0f, 0f);
            scatter.localScale = new Vector3(side, side, 1f);

            // The same ember the bed burns at, because that colour is the one measured to land
            // inside the band. Coverage is one wherever a coal exists, so its pixels are that
            // colour outright rather than a blend toward the char under them.
            scatterMaterial.SetColor(EmberColorId, TintHot(profile.CoalEmber, tint, profile.CoalTint));
            scatterMaterial.SetColor(RimColorId, TintHot(profile.CoalDeep, tint, profile.CoalTint));
            scatterMaterial.SetFloat(IntensityId, 0f);
            scatterMaterial.SetFloat(AgeId, 0f);
            scatterMaterial.SetFloat(CellsId, ScatterCells);
            scatterMaterial.SetFloat(
                CoalSizeId,
                ScatterSpot * ScatterCells / ScatterSpan * Mathf.Sqrt(profile.CoalRemnant)
            );
            scatterMaterial.SetFloat(SizeMinId, 0.78f);
            scatterMaterial.SetFloat(SizeMaxId, 1.42f);
            scatterMaterial.SetFloat(JitterId, 0.36f);
            scatterMaterial.SetFloat(WobbleId, 0.34f);
            scatterMaterial.SetFloat(RimSpanId, 0.18f);
            // A coal is only ten pixels across on the strip, so its edge is a single pixel of
            // coverage: anything wider spends the coal's whole body on a blend toward the char
            // and takes the pixels out of the band they were sized to fill.
            scatterMaterial.SetFloat(EdgePixelsId, 1f);
            scatterMaterial.SetFloat(FlickerId, 0.14f);
            scatterMaterial.SetFloat(DeathStartId, profile.CoalRemnantHold);
            scatterMaterial.SetFloat(DeathEndId, profile.CoalRemnantOut);
            scatterMaterial.SetFloat(DeathSpanId, ScatterWinkOut);
            scatterMaterial.SetFloat(SeedId, Random.Range(0f, 30f));
        }

        void BuildPuffs(Color tint)
        {
            int count = Mathf.Min(profile.MassCount, Masses.Length);
            if (count <= 0)
                return;

            Shader shader = Shader.Find("BattlePlan/AftermathSmoke");
            if (shader == null)
            {
                Debug.LogWarning("[Aftermath] BattlePlan/AftermathSmoke missing, skipping smoke");
                return;
            }

            // Smoke is matter, not light: it only faintly carries the ability's hue so the fire
            // inside it stays the unambiguous colour read.
            Color lit = Color.Lerp(profile.SmokeLit, ScaleTo(tint, profile.SmokeLit), profile.SmokeTint);
            Color shadow = Color.Lerp(
                profile.SmokeShadow,
                ScaleTo(tint, profile.SmokeShadow),
                profile.SmokeTint * 0.5f
            );

            float spin = spinAngle;
            float cos = Mathf.Cos(spin);
            float sin = Mathf.Sin(spin);

            List<Puff> built = new(count);
            for (int i = 0; i < count; i++)
            {
                Mass spec = Masses[i];
                MeshRenderer renderer = CreateQuad("Mass", shader, out Material material);
                if (renderer == null)
                    break;

                float size = spec.Size * radius * profile.MassScale;
                Vector3 anchor = new(
                    (spec.OffsetX * cos - spec.OffsetZ * sin) * radius,
                    0f,
                    (spec.OffsetX * sin + spec.OffsetZ * cos) * radius
                );
                Vector3 outward = anchor.sqrMagnitude > 0.0001f ? anchor.normalized : leanDirection;

                Puff puff = new()
                {
                    Transform = renderer.transform,
                    Material = material,
                    Spec = spec,
                    Anchor = anchor,
                    Outward = outward,
                    Sway = new Vector3(-outward.z, 0f, outward.x) * (radius * Random.Range(-0.14f, 0.14f)),
                    Size = size,
                    Lifetime = Mathf.Max(
                        0.1f,
                        Mathf.Min(spec.Lifetime * profile.LifeScale, MaxLifetime - spec.Delay)
                    ),
                    RollRate = Random.Range(-26f, 26f),
                    Roll = Random.Range(0f, 360f),
                    Phase = Random.Range(0f, Mathf.PI * 2f),
                };

                // Fewer lobes have to sit further apart to cover the same silhouette; these keep
                // every mass spanning roughly 0.83 of its quad whatever it is made of.
                bool sparse = spec.Lobes <= 3;

                material.SetColor(LitColorId, lit * spec.Tone);
                material.SetColor(ShadowColorId, shadow * spec.Tone);
                material.SetFloat(OpacityId, 0f);
                material.SetFloat(ErodeId, 0f);
                material.SetFloat(TearId, BaseTear);
                // Roughly a three-pixel silhouette whatever the mass is worth on screen: a hard
                // outline is most of what separates material from haze.
                material.SetFloat(EdgeWidthId, Mathf.Clamp(0.11f / Mathf.Max(0.35f, size), 0.02f, 0.16f));
                material.SetFloat(LobeCountId, spec.Lobes);
                material.SetFloat(SpreadId, sparse ? 0.5f : 0.46f);
                material.SetFloat(MinLobeId, sparse ? 0.18f : 0.16f);
                material.SetFloat(MaxLobeId, sparse ? 0.38f : 0.36f);
                material.SetFloat(KnitId, Random.Range(0.13f, 0.17f));
                material.SetFloat(WarpId, Random.Range(0.09f, 0.11f));
                material.SetFloat(WarpScaleId, Random.Range(2.7f, 3.3f));
                // Erosion works through this noise, so the outer masses tear at a coarser scale
                // than the body: they come apart into pieces you can still call masses instead of
                // into grit. The two largest keep the fine noise that gives the plume its edge.
                material.SetFloat(
                    NoiseScaleId,
                    i <= 1 ? Random.Range(8f, 10.5f) : Random.Range(4.6f, 6.4f)
                );
                material.SetFloat(MottleId, Random.Range(0.72f, 0.88f));
                material.SetFloat(ShadeLowId, Random.Range(0.48f, 0.56f));
                material.SetFloat(ShadeSpanId, Random.Range(0.23f, 0.29f));
                material.SetFloat(SweepId, Random.Range(1.25f, 1.6f));
                material.SetFloat(LobeWeightId, Random.Range(0.72f, 0.8f));
                material.SetFloat(CreaseId, Random.Range(0.18f, 0.28f));
                material.SetFloat(RimId, Random.Range(0.62f, 0.7f));
                material.SetFloat(SeedId, Random.Range(0f, 30f));

                puff.Transform.localPosition = anchor + Vector3.up * FloorHeight(size * 0.28f);
                puff.Transform.localScale = Vector3.zero;
                built.Add(puff);
            }

            if (built.Count > 0)
                puffs = built.ToArray();
        }

        void BuildSparks(Color tint)
        {
            int total = profile.SparkCount + profile.MoteCount;
            if (total <= 0)
                return;

            Shader shader = Shader.Find("BattlePlan/AftermathEmber");
            if (shader == null)
                return;

            Color core = TintHot(profile.SparkCore, tint, profile.SparkTint * 0.4f);
            Color tail = TintHot(profile.SparkTail, tint, profile.SparkTint);
            float leanAngle = Mathf.Atan2(leanDirection.x, leanDirection.z);

            List<Spark> built = new(total);
            for (int i = 0; i < total; i++)
            {
                bool isMote = i >= profile.SparkCount;
                MeshRenderer renderer = CreateQuad(isMote ? "Mote" : "Spark", shader, out Material material);
                if (renderer == null)
                    break;

                // Two outliers escape the arc so the scatter never closes into a rosette.
                float angle =
                    i % 7 == 3
                        ? Random.Range(0f, Mathf.PI * 2f)
                        : leanAngle + SparkArcStart + Random.value * SparkArcSpan;
                Vector3 outward = new(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                Spark spark = new()
                {
                    Transform = renderer.transform,
                    Material = material,
                    FlickerRate = Random.Range(13f, 27f),
                    Phase = Random.Range(0f, Mathf.PI * 2f),
                };

                if (isMote)
                {
                    // Buoyant coals lifted out of the crater: slow, long-lived, and still warm in
                    // the frame the strip cuts last.
                    spark.Origin =
                        outward * (radius * Random.Range(0.05f, 0.45f))
                        + Vector3.up * Random.Range(0.3f, 1.1f);
                    spark.Velocity =
                        outward * Random.Range(0.2f, 0.9f)
                        + leanDirection * Random.Range(0.3f, 1.2f)
                        + Vector3.up * Random.Range(0.9f, 2.1f);
                    spark.Gravity = -0.5f;
                    spark.Drag = 1.1f;
                    spark.Size = Random.Range(0.16f, 0.34f) * SizeScale();
                    spark.Delay = Random.Range(0.1f, 0.55f);
                    spark.Lifetime = Random.Range(0.65f, 1.25f);
                    spark.Intensity = Random.Range(0.45f, 0.95f);
                }
                else
                {
                    // Burning fragments thrown out of the fire: fast, short, and beaten straight
                    // back down. Speed drives the streak, so no two are the same length.
                    float speed = Random.Range(2.2f, 7.5f);
                    spark.Origin =
                        outward * (radius * Random.Range(0.02f, 0.3f))
                        + Vector3.up * Random.Range(0.25f, 1.4f);
                    spark.Velocity = outward * speed + Vector3.up * Random.Range(0.6f, 3.4f);
                    spark.Gravity = -9.5f;
                    spark.Drag = 1.7f;
                    spark.Size = Random.Range(0.22f, 0.8f) * SizeScale();
                    spark.Delay = Random.Range(0.01f, 0.34f) + (i % 3 == 0 ? Random.Range(0f, 0.18f) : 0f);
                    spark.Lifetime = Random.Range(0.26f, 0.62f);
                    spark.Intensity = Random.Range(0.8f, 1.35f);
                }

                spark.Stretch = isMote
                    ? Random.Range(1f, 1.7f)
                    : Mathf.Clamp(1.3f + spark.Velocity.magnitude * 0.42f, 1.3f, 4.6f);
                spark.Lifetime = Mathf.Max(0.1f, Mathf.Min(spark.Lifetime, MaxLifetime - spark.Delay));

                material.SetColor(CoreColorId, core);
                material.SetColor(TailColorId, tail);
                material.SetFloat(IntensityId, 0f);
                material.SetFloat(HeadWidthId, isMote ? 0.4f : 0.34f);
                material.SetFloat(TailWidthId, isMote ? 0.14f : 0.055f);
                // A mote is a rounder, near-solid coal; a spark is a thin spine dragging a
                // thinner smear, so its trail can cross the board without becoming a bar.
                material.SetFloat(SolidityId, isMote ? 3.1f : 2.6f);
                material.SetFloat(TailAlphaId, isMote ? 0.62f : 0.3f);
                material.SetFloat(CoreCutId, isMote ? 0.68f : 0.58f);

                spark.Transform.localPosition = spark.Origin;
                spark.Transform.localScale = Vector3.zero;
                built.Add(spark);
            }

            if (built.Count > 0)
                sparks = built.ToArray();
        }

        float SizeScale() => Mathf.Clamp(radius / 3f, 0.55f, 1.5f);

        float LongestLayerLifetime()
        {
            float longest = 0f;

            if (markMaterial != null)
                longest = Mathf.Max(longest, profile.MarkRise + profile.MarkHold + profile.MarkFade);
            if (coalMaterial != null)
                longest = Mathf.Max(longest, profile.CoalOut);
            if (scatterMaterial != null)
                longest = Mathf.Max(longest, profile.CoalRemnantOut + ScatterWinkOut);

            if (puffs != null)
            {
                for (int i = 0; i < puffs.Length; i++)
                    longest = Mathf.Max(longest, puffs[i].Spec.Delay + puffs[i].Lifetime);
            }
            if (sparks != null)
            {
                for (int i = 0; i < sparks.Length; i++)
                    longest = Mathf.Max(longest, sparks[i].Delay + sparks[i].Lifetime);
            }

            return Mathf.Min(longest, MaxLifetime);
        }

        IEnumerator Animate()
        {
            float elapsed = 0f;
            while (elapsed < totalLifetime)
            {
                UpdateMark(elapsed);
                UpdateCoals(elapsed);
                UpdateCoalScatter(elapsed);
                UpdatePuffs(elapsed);
                UpdateSparks(elapsed);

                elapsed += Time.deltaTime;
                yield return null;
            }

            Destroy(gameObject);
        }

        void UpdateMark(float elapsed)
        {
            if (markMaterial == null)
                return;

            float density;
            if (elapsed < profile.MarkRise)
            {
                // The burn is taken, not laid down: it reaches full depth faster than it appears.
                float rise = elapsed / Mathf.Max(0.01f, profile.MarkRise);
                density = profile.MarkOpacity * (1f - Mathf.Pow(1f - rise, 2.2f));
            }
            else if (elapsed < profile.MarkRise + profile.MarkHold)
            {
                density = profile.MarkOpacity;
            }
            else
            {
                float fade =
                    (elapsed - profile.MarkRise - profile.MarkHold)
                    / Mathf.Max(0.05f, profile.MarkFade);
                density = profile.MarkOpacity * (1f - Mathf.SmoothStep(0f, 1f, fade));
            }

            markMaterial.SetFloat(OpacityId, Mathf.Max(0f, density));

            // Three slow drifts under the fade, none of them worth more than a couple of levels on
            // its own. A depth ramp alone can hold the same bytes for four frames running at 30Hz;
            // a burn that eats outward while it smoulders and ashes over cannot.
            markMaterial.SetFloat(AshId, Mathf.Clamp01((elapsed - 0.3f) / 1.9f));
            markMaterial.SetFloat(DriftId, elapsed * 0.25f);
            if (markTransform != null)
            {
                float creep = 1f + 0.13f * (1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / 1.8f), 1.7f));
                markTransform.localScale = markScale * creep;
            }
        }

        void UpdateCoals(float elapsed)
        {
            if (coalTransform == null || coalMaterial == null)
                return;

            // Take, hold, cool. The hold is what stops the fire's brightest frame and the plume's
            // heaviest frame landing a fifth of a second apart: it covers the whole window the
            // strip's middle panel can be cut from.
            float envelope;
            if (elapsed < profile.CoalPeak)
            {
                float take = elapsed / Mathf.Max(0.01f, profile.CoalPeak);
                envelope = Mathf.Lerp(0.06f, 1f, Mathf.Pow(take, 0.75f));
            }
            else if (elapsed < profile.CoalPeak + profile.CoalHold)
            {
                envelope = 1f;
            }
            else
            {
                float cool =
                    (elapsed - profile.CoalPeak - profile.CoalHold)
                    / Mathf.Max(0.05f, profile.CoalOut - profile.CoalPeak - profile.CoalHold);
                envelope = Mathf.Pow(Mathf.Clamp01(1f - cool), 1.5f);
            }

            // Shallow enough that the hold is still a hold, deep enough that no two frames of the
            // bed carry the same coals: it rides the coverage threshold, so blotches breathe at
            // their edges rather than the whole bed pulsing together.
            float flicker =
                0.955f
                + 0.03f * Mathf.Sin(elapsed * 13.7f)
                + 0.015f * Mathf.Sin(elapsed * 31.3f + 1.7f);
            coalMaterial.SetFloat(
                IntensityId,
                Mathf.Clamp01(profile.CoalIntensity * envelope * flicker)
            );

            // The bed spreads as the fire takes hold, then draws back in as it burns out.
            float life = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, profile.CoalOut));
            float spread = Mathf.Lerp(0.72f, 1.08f, Mathf.Min(1f, elapsed * 2.6f));
            coalTransform.localScale =
                Vector3.one * (coalDiameter * spread * Mathf.Lerp(1f, 0.9f, life));
        }

        void UpdateCoalScatter(float elapsed)
        {
            if (scatterMaterial == null)
                return;

            // Nothing at all until the bed's hold is over, so the frame the fire is judged on is
            // the frame the fire already had and the coals cannot take credit for it. What comes
            // in afterwards is the sheet of flame breaking into pieces.
            float takeover = profile.CoalPeak + profile.CoalHold + 0.04f;
            float emerge = Mathf.Clamp01((elapsed - takeover) / 0.24f);
            // Drives coal width, never coal heat: a coal that dims walks out of the luminance band
            // it exists to occupy long before it leaves the frame.
            scatterMaterial.SetFloat(IntensityId, emerge * emerge * (3f - 2f * emerge));
            scatterMaterial.SetFloat(AgeId, elapsed);
        }

        void UpdatePuffs(float elapsed)
        {
            if (puffs == null)
                return;

            bool hasCamera = cameraTransform != null;
            Quaternion facing = hasCamera ? cameraTransform.rotation : Quaternion.Euler(73f, 0f, 0f);
            Vector3 forward = facing * Vector3.forward;
            float riseBase = radius * profile.Rise;
            float leanBase = radius * profile.Lean;

            for (int i = 0; i < puffs.Length; i++)
            {
                Puff puff = puffs[i];
                if (puff.Transform == null || puff.Material == null)
                    continue;
                if (elapsed < puff.Spec.Delay)
                {
                    puff.Transform.localScale = Vector3.zero;
                    continue;
                }

                float progress = Mathf.Clamp01(
                    (elapsed - puff.Spec.Delay) / Mathf.Max(0.01f, puff.Lifetime)
                );
                float climb = 1f - Mathf.Pow(1f - progress, 2f);
                float spread = 1f - Mathf.Pow(1f - progress, 2.6f);
                float scale = GrowCurve(progress, puff.Spec.Grow);
                float diameter = puff.Size * scale;

                Vector3 offset = puff.Anchor;
                offset += puff.Outward * (puff.Spec.Outward * radius * spread);
                offset += leanDirection * (leanBase * puff.Spec.LeanScale * climb);
                offset += puff.Sway * Mathf.Sin(progress * 3.1f + puff.Phase);
                offset.y = FloorHeight(diameter) + riseBase * puff.Spec.RiseScale * climb;

                float erode = ErodeAmount(progress, puff.Spec.ErodeStart, puff.Spec.ErodeMax);
                float roll = puff.Roll + puff.RollRate * (elapsed - puff.Spec.Delay);

                puff.Transform.localPosition = offset;
                puff.Transform.localScale = Vector3.one * (diameter / SilhouetteSpan);
                puff.Transform.rotation = Quaternion.AngleAxis(roll, forward) * facing;
                puff.Material.SetFloat(OpacityId, Fade(progress, puff.Spec.HoldEnd));
                puff.Material.SetFloat(ErodeId, erode);
                puff.Material.SetFloat(TearId, BaseTear + erode * 1.5f);
                // The mass churns but the light does not follow it round.
                puff.Material.SetFloat(LightAngleId, -roll * Mathf.Deg2Rad);
            }
        }

        void UpdateSparks(float elapsed)
        {
            if (sparks == null)
                return;

            bool hasCamera = cameraTransform != null;
            Quaternion fallback = Quaternion.Euler(73f, 0f, 0f);
            Vector3 forward = hasCamera ? cameraTransform.forward : fallback * Vector3.forward;
            Vector3 right = hasCamera ? cameraTransform.right : Vector3.right;
            Vector3 up = hasCamera ? cameraTransform.up : fallback * Vector3.up;

            for (int i = 0; i < sparks.Length; i++)
            {
                Spark spark = sparks[i];
                if (spark.Transform == null || spark.Material == null)
                    continue;
                if (elapsed < spark.Delay)
                {
                    spark.Transform.localScale = Vector3.zero;
                    continue;
                }

                float age = elapsed - spark.Delay;
                float progress = Mathf.Clamp01(age / Mathf.Max(0.01f, spark.Lifetime));

                // Closed form rather than integrated per frame, so an arc is identical on every peer
                // and at every frame rate the capture rig runs at.
                float damped =
                    spark.Drag > 0.001f ? (1f - Mathf.Exp(-spark.Drag * age)) / spark.Drag : age;
                Vector3 position =
                    spark.Origin
                    + spark.Velocity * damped
                    + Vector3.up * (0.5f * spark.Gravity * age * age);
                Vector3 velocity =
                    spark.Velocity * Mathf.Exp(-spark.Drag * age) + Vector3.up * (spark.Gravity * age);

                // Sparks die where they land instead of sinking through the tiles.
                const float floorHeight = 0.1f;
                float clearance = Mathf.Clamp01((position.y - floorHeight) / 0.28f);
                if (position.y < floorHeight)
                    position.y = floorHeight;

                Vector3 streak = right * Vector3.Dot(velocity, right) + up * Vector3.Dot(velocity, up);
                if (streak.sqrMagnitude < 1e-4f)
                    streak = up;

                float fade = progress < 0.09f ? progress / 0.09f : Mathf.Pow(1f - progress, 1.5f);
                float flicker = 0.76f + 0.24f * Mathf.Sin(age * spark.FlickerRate + spark.Phase);

                spark.Transform.localPosition = position;
                spark.Transform.rotation = Quaternion.LookRotation(forward, streak);
                spark.Transform.localScale = new Vector3(spark.Size, spark.Size * spark.Stretch, 1f);
                spark.Material.SetFloat(
                    IntensityId,
                    spark.Intensity * fade * flicker * clearance
                );
            }
        }

        /// <summary>How high a mass of this width has to sit before its billboard cuts the floor.</summary>
        static float FloorHeight(float diameter) => FloorBias + diameter * FloorPerSize;

        /// <summary>
        /// Bursts out to full width, then keeps inflating for the rest of its life. The second half
        /// is what stops the plume holding one silhouette while its opacity ramps away.
        /// </summary>
        static float GrowCurve(float progress, float grow)
        {
            float burst = 1f - Mathf.Pow(1f - Mathf.Clamp01(progress / 0.3f), 2.2f);
            float swell = Mathf.Clamp01((progress - 0.3f) / 0.7f);
            return Mathf.Lerp(0.28f, 1f, burst) * Mathf.Lerp(1f, grow, 1f - Mathf.Pow(1f - swell, 1.6f));
        }

        /// <summary>
        /// Snaps in, then stays near-opaque for most of its life and collapses at the end. Thinning
        /// is erosion's job: what is left of a mass has to stay solid enough to hide the tile grid,
        /// or the plume reads as a stain rather than as material.
        /// </summary>
        static float Fade(float progress, float holdEnd)
        {
            const float rampIn = 0.05f;
            if (progress < rampIn)
                return progress / rampIn;
            if (progress < holdEnd)
                return 1f;

            float tail = (progress - holdEnd) / Mathf.Max(0.01f, 1f - holdEnd);
            return 1f - Mathf.SmoothStep(0f, 1f, tail) * tail;
        }

        /// <summary>
        /// Walks the mass's visible boundary inwards through its own noise so it comes apart into
        /// pieces with hard outlines, rather than greying out into haze.
        /// </summary>
        static float ErodeAmount(float progress, float start, float max)
        {
            if (progress <= start)
                return 0f;
            return max * Mathf.Pow((progress - start) / Mathf.Max(0.01f, 1f - start), 1.25f);
        }

        MeshRenderer CreateGroundQuad(
            string quadName,
            Shader shader,
            float height,
            out Material material
        )
        {
            MeshRenderer renderer = CreateQuad(quadName, shader, out material);
            if (renderer != null)
            {
                renderer.transform.localPosition = Vector3.up * height;
                renderer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            return renderer;
        }

        MeshRenderer CreateQuad(string quadName, Shader shader, out Material material)
        {
            material = null;
            Mesh mesh = SharedQuad();
            if (shader == null || mesh == null)
            {
                Debug.LogWarning($"[Aftermath] missing shader or quad mesh for '{quadName}'");
                return null;
            }

            GameObject quad = new(quadName);
            quad.transform.SetParent(transform, false);

            MeshFilter filter = quad.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            material = new Material(shader);
            ownedMaterials.Add(material);

            MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return renderer;
        }

        /// <summary>
        /// Built by hand rather than borrowed from a primitive, so no collider is ever created and
        /// the effect never touches the physics scene to draw a billboard. Unity may unload it
        /// between scenes; the null check rebuilds it.
        /// </summary>
        static Mesh SharedQuad()
        {
            if (sharedQuad != null)
                return sharedQuad;

            sharedQuad = new Mesh { name = "AftermathQuad" };
            sharedQuad.vertices = QuadVertices;
            sharedQuad.uv = QuadUvs;
            sharedQuad.triangles = QuadTriangles;
            // Flat bounds make billboards pop in and out of the frustum as they rotate.
            sharedQuad.bounds = new Bounds(Vector3.zero, Vector3.one);
            return sharedQuad;
        }

        static Color ToLdr(Color color)
        {
            float peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            if (peak <= 1f)
                return new Color(color.r, color.g, color.b, 1f);
            return new Color(color.r / peak, color.g / peak, color.b / peak, 1f);
        }

        /// <summary>Pulls a hue toward the ability's colour without changing how hot it burns.</summary>
        static Color TintHot(Color hot, Color tint, float amount)
        {
            if (amount <= 0.001f)
                return hot;

            Color tinted = ScaleTo(tint, hot);
            return Color.Lerp(hot, tinted, Mathf.Clamp01(amount));
        }

        /// <summary>Rescales <paramref name="hue"/> to carry the same total energy as a reference.</summary>
        static Color ScaleTo(Color hue, Color reference)
        {
            float hueEnergy = hue.r + hue.g + hue.b;
            float referenceEnergy = reference.r + reference.g + reference.b;
            if (hueEnergy < 0.001f)
                return reference;
            float gain = referenceEnergy / hueEnergy;
            return new Color(hue.r * gain, hue.g * gain, hue.b * gain, 1f);
        }

        void OnDestroy()
        {
            for (int i = 0; i < ownedMaterials.Count; i++)
            {
                if (ownedMaterials[i] != null)
                    Destroy(ownedMaterials[i]);
            }
            ownedMaterials.Clear();
        }
    }
}
