using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One-call ground shockwave: a dark, opaque dust front that occludes the deck rather than
/// brightening it, a slower collar ring behind it, a white-hot core in the crater they leave
/// between them, and the clods all of it drops as it settles.
/// <para>
/// The deck sits near luminance 182 of 255, so an effect built out of added light has 73 levels to
/// work with and one built out of occlusion has 182. Every silhouette here is therefore matter —
/// alpha-blended geometry several stops below the deck, ending in a hard alpha step.
/// </para>
/// <para>
/// The one thing that is light is the core, and it is the exception that proves the rule: a ring of
/// dark matter is a container, and a container with nothing in it measures darker in the middle
/// than the floor it is standing on. The cyan band cannot fill it — at hue 189 and saturation 0.55
/// this tonemapper caps at luminance 208, so that band is incapable of carrying a flash however
/// hard it is driven. The flash is therefore its own layer, roughly a cell across, written far
/// above white so it clips flat in all three channels, held across the same frames as the mass
/// peak, and skirted in the ability's hue so the white sits inside colour rather than on the deck.
/// </para>
/// <para>
/// Nothing about the shape is concentric or closed: the front is ragged, vented in three or four
/// places, and carries half again as much mass on one side, and the crest is a handful of separate
/// arcs rather than a hoop. A perfect ring of even width reads as a manufactured object.
/// </para>
/// <para>
/// Inside the silhouette the dust is a lit surface rather than a fill. A wall of it rises off the
/// trailing edge and falls over the leading one, lumped by a relief field and shaded by the blast
/// itself as a low lamp in the crater, so every lump has a face toward the blast some seventy
/// levels above the face away from it and the lit faces carry the ability's colour. The crest is
/// the burning near face of that same volume — a slab tens of pixels deep, torn along the same
/// lumps — and it lights the deck around the front. A hard outline around a flat interior is a
/// cut-out however good the outline is, and a hot band one pixel wide emits nothing.
/// </para>
/// <para>
/// Purely local/visual — call on each peer at the moment of impact (grenade detonation, Area Lock
/// hit, Pogo landing, shield activation):
/// </para>
///
///   ImpactShockwave.Spawn(position, teamColor);                    // standard hit
///   ImpactShockwave.Spawn(position, teamColor, maxRadius: 5f);     // big boom
///   ImpactShockwave.Spawn(position, teamColor, groundDust: 0f);    // rim and light only
/// </summary>
public class ImpactShockwave : MonoBehaviour
{
    // Above the floor and the fog overlay tiles (y 0.05); each layer gets its own slice so the
    // transparent sort order between them is decided by depth rather than by spawn order.
    const float ClodHeight = 0.075f;
    const float CollarHeight = 0.085f;
    const float FrontHeight = 0.095f;
    const float RimHeight = 0.105f;
    const float GlowHeight = 0.112f;
    const float CoreHeight = 0.118f;

    // Headroom on each quad for the ragged, one-sided silhouette to bulge into. The front's nominal
    // boundary is still driven to exactly maxRadius, which is the figure a caller sizes against.
    const float FrontQuadPad = 1.42f;
    const float CollarQuadPad = 1.42f;
    const float ClodQuadPad = 1.34f;
    const float CoreQuadPad = 1.32f;

    // Nothing may survive into the next beat of planning.
    const float MaxLifetime = 0.85f;

    // The shading model runs the dust from one endpoint to the other, so the palette's width IS the
    // internal contrast: composited over a deck at luminance 182 these land near 25 on the face
    // turned away from the blast and near 155 on the face turned toward it. A 50-level palette
    // makes a hard silhouette around a flat fill however carefully the surface is lit, which is a
    // paper cut-out; this one is wide enough for the lighting to be worth 70 levels a lump.
    static readonly Color DustShadow = new(0.112f, 0.101f, 0.090f);
    static readonly Color DustLit = new(0.660f, 0.632f, 0.590f);
    static readonly Color ClodShadow = new(0.126f, 0.114f, 0.102f);
    static readonly Color ClodLit = new(0.672f, 0.645f, 0.604f);

    // How much of the ability's colour the blast puts into the faces it lights. The shadows keep
    // the dust's own warm albedo, so the mass runs warm-dark to cool-lit rather than being one
    // grey-brown at two values — colour has to own area inside the silhouette, not a rim.
    const float KeyTintStrength = 0.55f;

    // Two arcs of the wheel where a saturated hue still reaches luminance 205-225 through this
    // tonemapper, both of them secondary axes: green and blue may clip while red is held down, or
    // red and green may clip while blue is held down, and in each case the held channel keeps the
    // saturation. On a primary axis the same colour caps near luminance 150, which is what a hot
    // band authored in the 200-238 arc does however hard it is driven. Linear rgb, unit scaled.
    static readonly Vector4 CoolCrest = new(0.075f, 0.475f, 1f, 1f);
    static readonly Vector4 CoolCrestCore = new(0.105f, 0.600f, 1f, 1f);
    static readonly Vector4 WarmCrest = new(1f, 0.293f, 0.062f, 1f);
    static readonly Vector4 WarmCrestCore = new(1f, 0.420f, 0.115f, 1f);

    // The core's ramp. The centre is written past the point where every channel resolves to 255, so
    // it lands dead flat rather than approaching white; the shoulder and the rim hold two channels
    // at the ceiling and keep the third down, which is the only way a pixel here stays above
    // luminance 240 and still belongs to the ability's hue. Linear rgb, unit scaled.
    static readonly Vector4 CoreWhite = new(3.6f, 3.6f, 3.6f, 1f);
    static readonly Vector4 CoolCoreHot = new(1.05f, 2.20f, 3.00f, 1f);
    static readonly Vector4 CoolCoreRim = new(1.00f, 2.10f, 2.90f, 1f);
    static readonly Vector4 WarmCoreHot = new(3.00f, 2.15f, 1.00f, 1f);
    static readonly Vector4 WarmCoreRim = new(2.90f, 2.05f, 0.95f, 1f);

    // The same two colours as light rather than as emission, for the lit faces and the pop.
    static readonly Color CoolLight = new(0.303f, 0.719f, 1f);
    static readonly Color WarmLight = new(1f, 0.578f, 0.276f);

    // Silhouette character, shared by the dust and the crest so the two agree on where the front is.
    const float OutlineRag = 0.17f;
    const float MassLean = 0.29f;
    const float VentCount = 4f;
    const float VentHalfWidth = 0.038f;
    const float BoundaryCells = 7f;

    // Surface character, shared by the dust and the crest so the crest burns on the lumps the dust
    // is shaded with rather than across them. The lamp is the blast itself, sitting low in the
    // crater, and it is wrapped: dust is lit by scatter arriving from every direction at once, so a
    // lump has a bright face, a dim face and no line between them. The wall's own form is spread
    // across the band's whole width rather than concentrated into a step inside it. Between them
    // those two decisions are the difference between a cluster of lit lumps and a glazed ribbon
    // with a terminator running down it, at no cost to how far apart the lit and unlit faces sit.
    const float LumpCycles = 26f;
    const float LumpSlope = 0.55f;
    const float LumpShare = 0.40f;
    const float WallCrest = 0f;
    const float WallFoot = 0.92f;
    const float DustAmbient = 0.012f;
    const float DustKey = 0.95f;
    const float DustKeyElevation = 0.314f;
    const float DustKeyGamma = 1f;
    const float DustSky = 0.02f;
    const float DustMicro = 0.05f;

    static readonly int DustDarkId = Shader.PropertyToID("_DustDark");
    static readonly int DustLitId = Shader.PropertyToID("_DustLit");
    static readonly int DustKeyLitId = Shader.PropertyToID("_DustKeyLit");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    static readonly int RadiusId = Shader.PropertyToID("_Radius");
    static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    static readonly int InnerRagId = Shader.PropertyToID("_InnerRag");
    static readonly int OuterRagId = Shader.PropertyToID("_OuterRag");
    static readonly int ErodeId = Shader.PropertyToID("_Erode");
    static readonly int ErodeBiasId = Shader.PropertyToID("_ErodeBias");
    static readonly int ErodeDirId = Shader.PropertyToID("_ErodeDir");
    static readonly int BlastDirId = Shader.PropertyToID("_BlastDir");
    static readonly int RagId = Shader.PropertyToID("_Rag");
    static readonly int RagCountId = Shader.PropertyToID("_RagCount");
    static readonly int LeanId = Shader.PropertyToID("_Lean");
    static readonly int BiasDirId = Shader.PropertyToID("_BiasDir");
    static readonly int VentsId = Shader.PropertyToID("_Vents");
    static readonly int VentWidthId = Shader.PropertyToID("_VentWidth");
    static readonly int ErodeScaleId = Shader.PropertyToID("_ErodeScale");
    static readonly int LumpScaleId = Shader.PropertyToID("_LumpScale");
    static readonly int LumpReliefId = Shader.PropertyToID("_LumpRelief");
    static readonly int LumpShareId = Shader.PropertyToID("_LumpShare");
    static readonly int FormInId = Shader.PropertyToID("_FormIn");
    static readonly int FormOutId = Shader.PropertyToID("_FormOut");
    static readonly int DomeReliefId = Shader.PropertyToID("_DomeRelief");
    static readonly int AmbientId = Shader.PropertyToID("_Ambient");
    static readonly int KeyId = Shader.PropertyToID("_Key");
    static readonly int KeyElevId = Shader.PropertyToID("_KeyElev");
    static readonly int KeyGammaId = Shader.PropertyToID("_KeyGamma");
    static readonly int SkyId = Shader.PropertyToID("_Sky");
    static readonly int MicroId = Shader.PropertyToID("_Micro");
    static readonly int TintReachId = Shader.PropertyToID("_TintReach");
    static readonly int SeedId = Shader.PropertyToID("_Seed");
    static readonly int ShapeSeedId = Shader.PropertyToID("_ShapeSeed");
    static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    static readonly int EmberColorId = Shader.PropertyToID("_EmberColor");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int WidthId = Shader.PropertyToID("_Width");
    static readonly int RimPushId = Shader.PropertyToID("_RimPush");
    static readonly int SlabDepthId = Shader.PropertyToID("_SlabDepth");
    static readonly int SlabHoldId = Shader.PropertyToID("_SlabHold");
    static readonly int SlabCutId = Shader.PropertyToID("_SlabCut");
    static readonly int SlabSoftId = Shader.PropertyToID("_SlabSoft");
    static readonly int SlabOutId = Shader.PropertyToID("_SlabOut");
    static readonly int SlabSwingId = Shader.PropertyToID("_SlabSwing");
    static readonly int StreakId = Shader.PropertyToID("_Streak");
    static readonly int StreakCellsId = Shader.PropertyToID("_StreakCells");
    static readonly int EmberShareId = Shader.PropertyToID("_EmberShare");
    static readonly int TailInId = Shader.PropertyToID("_TailIn");
    static readonly int TailAmpId = Shader.PropertyToID("_TailAmp");
    static readonly int TailPowId = Shader.PropertyToID("_TailPow");
    static readonly int OutReachId = Shader.PropertyToID("_OutReach");
    static readonly int OutAmpId = Shader.PropertyToID("_OutAmp");
    static readonly int OutPowId = Shader.PropertyToID("_OutPow");
    static readonly int GroundAmpId = Shader.PropertyToID("_GroundAmp");
    static readonly int GroundReachId = Shader.PropertyToID("_GroundReach");
    static readonly int GroundPowId = Shader.PropertyToID("_GroundPow");
    static readonly int ReliefBiteId = Shader.PropertyToID("_ReliefBite");
    static readonly int ReliefGammaId = Shader.PropertyToID("_ReliefGamma");
    static readonly int ArcCountId = Shader.PropertyToID("_ArcCount");
    static readonly int ArcSpanId = Shader.PropertyToID("_ArcSpan");
    static readonly int ArcSeedId = Shader.PropertyToID("_ArcSeed");
    static readonly int WhiteColorId = Shader.PropertyToID("_WhiteColor");
    static readonly int HotColorId = Shader.PropertyToID("_HotColor");
    static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    static readonly int PlateauId = Shader.PropertyToID("_Plateau");
    static readonly int MidId = Shader.PropertyToID("_Mid");
    static readonly int WobbleId = Shader.PropertyToID("_Wobble");
    static readonly int BiteId = Shader.PropertyToID("_Bite");
    static readonly int BiteScaleId = Shader.PropertyToID("_BiteScale");
    static readonly int InnerId = Shader.PropertyToID("_Inner");
    static readonly int ReachId = Shader.PropertyToID("_Reach");
    static readonly int FalloffId = Shader.PropertyToID("_Falloff");
    static readonly int UnevenId = Shader.PropertyToID("_Uneven");

    static Mesh sharedQuadMesh;

    struct Clod
    {
        public Transform Transform;
        public Material Material;
        public Vector3 Direction;
        public Vector2 TowardBlast;
        public float Distance;
        public float QuadSize;
        public float Delay;
        public float Lifetime;
        public float Height;
        public float Spin;
        public float SpinRate;
        public float DriftSpeed;
        public float Stretch;
        public float ErodeStart;
        public float ErodeShape;
        public float Seed;
        public float Churn;
        public float ShadeSwing;
        public float ShadePulse;
        public float ShadePhase;
        public float ShadeEnd;
    }

    readonly List<Material> ownedMaterials = new();

    Material frontMaterial;
    Material rimMaterial;
    Material collarMaterial;
    Material coreMaterial;
    Material glowMaterial;
    Clod[] clods;
    Light popLight;

    bool crestIsCool;
    Color dustKeyLit;
    Color clodKeyLit;

    float maxRadius;
    float totalLifetime;
    float dustSeed;
    float shapeSeed;
    float collarSeed;
    float arcSeed;

    // The direction the blast leans, on the board's xz plane. The front is heaviest here, the crest
    // is brightest here, the collar is pushed this way, and the settling clods walk this way.
    Vector2 leanDirection = Vector2.right;

    float frontTravel;
    float frontLife;
    float frontQuadHalf;
    float frontBandWorld;
    float frontOpacity;

    float rimLife;
    float rimHold;
    float rimWidthWorld;

    float collarDelay;
    float collarTravel;
    float collarLife;
    float collarQuadHalf;
    float collarOuterWorld;
    float collarBandWorld;
    float collarOpacity;

    float coreRadiusWorld;
    float coreQuadHalf;
    float glowQuadHalf;
    float glowReachWorld;
    float glowPeak;
    float coreRise;
    float coreHoldStart;
    float coreHoldEnd;
    float coreLife;

    float clodOpacity;
    float lightLifetime;
    float lightPeak;

    public static void Spawn(
        Vector3 position,
        Color color,
        float maxRadius = 2.5f,
        float duration = 0.45f,
        bool withLightPop = true,
        float groundDust = 1f
    )
    {
        GameObject root = new("ImpactShockwave");
        root.transform.position = new Vector3(position.x, 0f, position.z);

        ImpactShockwave shockwave = root.AddComponent<ImpactShockwave>();
        shockwave.Build(
            color,
            Mathf.Max(0.25f, maxRadius),
            Mathf.Max(0.1f, duration),
            withLightPop,
            Mathf.Clamp01(groundDust)
        );
    }

    void Build(Color color, float radius, float life, bool withLightPop, float groundDust)
    {
        maxRadius = radius;
        dustSeed = Random.Range(0f, 20f);
        shapeSeed = Random.Range(2f, 40f);
        collarSeed = Random.Range(48f, 88f);
        arcSeed = Random.Range(96f, 136f);

        float leanAngle = Random.Range(0f, Mathf.PI * 2f);
        leanDirection = new Vector2(Mathf.Cos(leanAngle), Mathf.Sin(leanAngle));

        // A pressure front is over in a beat. The caller's duration only nudges the crossing inside
        // that window: a front that takes half a second to cross three cells stops being a front
        // and becomes a circle that grows, and it is at its most massive when it arrives rather
        // than when it leaves.
        frontTravel = Mathf.Clamp(life * 0.26f, 0.105f, 0.155f);
        frontLife = frontTravel + 0.19f;
        rimHold = frontTravel + 0.05f;
        rimLife = frontTravel + 0.15f;

        collarDelay = 0.025f;
        collarTravel = frontTravel * 1.15f;
        collarLife = collarTravel + 0.2f;

        // Tied to the crossing rather than stated in seconds, so the flash and the mass peak on the
        // same frames however long the caller's beat is. At the standard crossing that is a hundred
        // millisecond plateau either side of the arrival, which no 30 Hz sample can straddle.
        coreRise = frontTravel * 0.39f;
        coreHoldStart = frontTravel * 0.60f;
        coreHoldEnd = frontTravel * 1.30f;
        coreLife = frontTravel * 1.86f;

        // A one-and-a-third-cell ring fires on every ability activation and every death. Dark
        // matter at that size has to stay punctuation rather than a hole in the deck, so the small
        // case gets less of everything except the hard edge.
        float massWeight = Mathf.InverseLerp(1.6f, 4f, maxRadius);

        frontQuadHalf = maxRadius * FrontQuadPad;
        frontBandWorld = maxRadius * Mathf.Lerp(0.22f, 0.34f, massWeight);
        frontOpacity = Mathf.Lerp(0.82f, 1f, massWeight) * groundDust;
        rimWidthWorld = Mathf.Min(0.72f, maxRadius * 0.16f);

        collarOuterWorld = maxRadius * 0.48f;
        collarQuadHalf = collarOuterWorld * CollarQuadPad;
        collarBandWorld = collarOuterWorld * 0.42f;
        collarOpacity = frontOpacity * 0.96f;

        clodOpacity = Mathf.Lerp(0.86f, 0.97f, massWeight) * groundDust;

        // About a cell and a quarter across on a full-sized blast, which is what puts three to four
        // per cent of the panel over luminance 240 with well under one per cent of it clipping in
        // all three channels — and capped against the caller's radius, so a one-and-a-third-cell
        // ring gets a flash a half cell wide rather than being filled in solid.
        float cell = GameLoop.cellSize > 0.01f ? GameLoop.cellSize : 2.7f;
        coreRadiusWorld = Mathf.Min(cell * 0.66f, maxRadius * 0.4f);
        coreQuadHalf = coreRadiusWorld * CoreQuadPad;
        // Dies inside the crater: past the front's inner boundary it would only wash the deck,
        // where this hue tonemaps to a pale nothing whatever it is authored as.
        glowReachWorld = Mathf.Min(coreRadiusWorld * 0.6f, maxRadius * 0.34f);
        glowQuadHalf = (coreRadiusWorld + glowReachWorld) * 1.12f;
        glowPeak = Mathf.Lerp(0.85f, 1.35f, massWeight);

        crestIsCool = IsCoolHue(color);
        Color blastLight = crestIsCool ? CoolLight : WarmLight;
        dustKeyLit = LitBy(DustLit, blastLight);
        clodKeyLit = LitBy(ClodLit, blastLight);

        BuildRim();
        if (groundDust > 0.01f)
        {
            BuildFront();
            BuildCollar();
            BuildClods(massWeight, groundDust);
        }
        BuildCore();
        if (withLightPop)
            BuildLight(blastLight, massWeight);

        totalLifetime = LongestLayerLifetime();
        StartCoroutine(Animate());
    }

    /// <summary>
    /// Secondary layers are capped so nothing survives into the next beat of planning; the front
    /// itself is never stretched, however long the caller's <c>duration</c> is.
    /// </summary>
    float LongestLayerLifetime()
    {
        float longest = Mathf.Max(frontLife, collarDelay + collarLife);
        longest = Mathf.Max(longest, Mathf.Max(rimLife, coreLife));
        if (clods != null)
        {
            for (int i = 0; i < clods.Length; i++)
                longest = Mathf.Max(longest, clods[i].Delay + clods[i].Lifetime);
        }
        return Mathf.Min(longest + 0.03f, MaxLifetime);
    }

    void BuildFront()
    {
        MeshRenderer renderer = CreateGroundQuad(
            "DustFront",
            Shader.Find("BattlePlan/ShockwaveDust"),
            FrontHeight,
            out frontMaterial
        );
        if (renderer == null)
            return;

        renderer.transform.localScale = Vector3.one * (frontQuadHalf * 2f);

        ApplyDustSurface(frontMaterial, DustShadow, DustLit, dustKeyLit);
        ApplySilhouette(frontMaterial, shapeSeed);
        frontMaterial.SetFloat(InnerRagId, 0.42f);
        frontMaterial.SetFloat(OpacityId, frontOpacity);
    }

    void BuildRim()
    {
        MeshRenderer renderer = CreateGroundQuad(
            "BlastCrest",
            Shader.Find("BattlePlan/ShockwaveEdge"),
            RimHeight,
            out rimMaterial
        );
        if (renderer == null)
            return;

        renderer.transform.localScale = Vector3.one * (frontQuadHalf * 2f);

        rimMaterial.SetVector(GlowColorId, crestIsCool ? CoolCrest : WarmCrest);
        rimMaterial.SetVector(EmberColorId, crestIsCool ? CoolCrestCore : WarmCrestCore);
        // The hot core is the same hue further up, not a step toward white: this tonemapper trades
        // saturation for every channel driven past the clip point. Nothing on the crest reaches
        // white at all — a near-white hairline on a dust edge is a specular highlight, and dust has
        // no specular lobe. BlastCore owns the whole white budget and spends it as area.
        rimMaterial.SetFloat(EmberShareId, 0.22f);
        // Just inside the boundary: the burning face then sits on the outermost strip of dust,
        // which is the only backing on this board that lets a hot colour stay a colour. Over the
        // deck the same light tonemaps to a pale wash whatever it is authored as.
        rimMaterial.SetFloat(RimPushId, -0.30f);
        // Depth, not amplitude. Dimming a slab drops it under luminance 200 and hands the colour
        // straight back, so the crest buys its area by running further into the mass instead.
        rimMaterial.SetFloat(SlabDepthId, 1.15f);
        rimMaterial.SetFloat(SlabHoldId, 0.5f);
        rimMaterial.SetFloat(SlabCutId, 0.2f);
        rimMaterial.SetFloat(SlabSoftId, 0.13f);
        rimMaterial.SetFloat(SlabOutId, 0.09f);
        rimMaterial.SetFloat(SlabSwingId, 0.16f);
        rimMaterial.SetFloat(StreakId, 0.45f);
        rimMaterial.SetFloat(StreakCellsId, 34f);
        rimMaterial.SetFloat(TailInId, 1.15f);
        rimMaterial.SetFloat(TailAmpId, 0.42f);
        rimMaterial.SetFloat(TailPowId, 1.5f);
        rimMaterial.SetFloat(OutReachId, 1.45f);
        rimMaterial.SetFloat(OutAmpId, 0.36f);
        rimMaterial.SetFloat(OutPowId, 0.85f);
        rimMaterial.SetFloat(GroundAmpId, 0.52f);
        rimMaterial.SetFloat(GroundReachId, 2f);
        rimMaterial.SetFloat(GroundPowId, 0.65f);
        rimMaterial.SetFloat(ReliefBiteId, 0.42f);
        rimMaterial.SetFloat(ReliefGammaId, 0.75f);
        rimMaterial.SetFloat(LumpScaleId, LumpCycles);
        rimMaterial.SetFloat(LumpReliefId, LumpSlope);
        rimMaterial.SetFloat(ArcCountId, Mathf.Floor(Random.Range(5f, 8f)));
        rimMaterial.SetFloat(ArcSpanId, 0.076f);
        rimMaterial.SetFloat(ArcSeedId, arcSeed);
        // Same fields the dust evaluates, so the arcs ride its boundary instead of cutting across
        // it and burn on the lumps it is shaded with instead of sitting on top of them.
        ApplySilhouette(rimMaterial, shapeSeed);
        rimMaterial.SetFloat(IntensityId, 0f);
    }

    /// <summary>
    /// The flash, and the only thing in the effect allowed to be white. Opaque rather than
    /// additive, so the silhouette terminates on an alpha step and the written radiance survives
    /// verbatim, and torn around its rim in two dimensions so what fills the crater is a blast
    /// rather than a lamp. The skirt under it is the one soft thing here: light, in the ability's
    /// hue, reaching from the core's rim to just short of the front.
    /// </summary>
    void BuildCore()
    {
        MeshRenderer glowRenderer = CreateGroundQuad(
            "CoreGlow",
            Shader.Find("BattlePlan/ShockwaveGlow"),
            GlowHeight,
            out glowMaterial
        );
        if (glowRenderer != null)
        {
            glowRenderer.transform.localScale = Vector3.one * (glowQuadHalf * 2f);
            glowRenderer.transform.localPosition = CoreOffset(GlowHeight);
            glowMaterial.SetVector(GlowColorId, crestIsCool ? CoolCrest : WarmCrest);
            glowMaterial.SetFloat(ReachId, Mathf.Clamp(glowReachWorld / glowQuadHalf, 0.02f, 1f));
            glowMaterial.SetFloat(FalloffId, 1.8f);
            glowMaterial.SetFloat(UnevenId, 0.45f);
            glowMaterial.SetFloat(RagCountId, 7f);
            glowMaterial.SetFloat(ShapeSeedId, arcSeed);
            glowMaterial.SetFloat(IntensityId, 0f);
        }

        MeshRenderer coreRenderer = CreateGroundQuad(
            "BlastCore",
            Shader.Find("BattlePlan/ShockwaveCore"),
            CoreHeight,
            out coreMaterial
        );
        if (coreRenderer == null)
            return;

        coreRenderer.transform.localScale = Vector3.one * (coreQuadHalf * 2f);
        coreRenderer.transform.localPosition = CoreOffset(CoreHeight);

        coreMaterial.SetVector(WhiteColorId, CoreWhite);
        coreMaterial.SetVector(HotColorId, crestIsCool ? CoolCoreHot : WarmCoreHot);
        coreMaterial.SetVector(RimColorId, crestIsCool ? CoolCoreRim : WarmCoreRim);
        // Where the flat white stops, as a share of the radius. Squared, that is the share of the
        // core that clips in all three channels, and the reference runs a fifth to a quarter.
        coreMaterial.SetFloat(PlateauId, 0.30f);
        coreMaterial.SetFloat(MidId, 0.58f);
        coreMaterial.SetFloat(WobbleId, 0.16f);
        coreMaterial.SetFloat(RagCountId, Mathf.Floor(Random.Range(4f, 7f)));
        coreMaterial.SetFloat(BiteId, 0.20f);
        // Two to eight tears round the rim: enough to break the circle, not so many that the
        // boundary turns into a fringe and stops being a silhouette.
        coreMaterial.SetFloat(BiteScaleId, LumpCycles * 0.66f);
        coreMaterial.SetFloat(SeedId, dustSeed);
        coreMaterial.SetFloat(ShapeSeedId, shapeSeed + 11.9f);
        coreMaterial.SetFloat(RadiusId, 0f);
        coreMaterial.SetFloat(OpacityId, 0f);
    }

    /// <summary>
    /// Pushed off the impact point against the collar's own lean, so the flash, the collar and the
    /// front never share a centre and the crater's mound is left showing on one side of the core.
    /// </summary>
    Vector3 CoreOffset(float height) =>
        new(-leanDirection.x * maxRadius * 0.05f, height, -leanDirection.y * maxRadius * 0.05f);

    void BuildCollar()
    {
        MeshRenderer renderer = CreateGroundQuad(
            "DustCollar",
            Shader.Find("BattlePlan/ShockwaveDust"),
            CollarHeight,
            out collarMaterial
        );
        if (renderer == null)
            return;

        renderer.transform.localScale = Vector3.one * (collarQuadHalf * 2f);
        // Pushed off the impact point down the lean, so the two rings are never concentric.
        renderer.transform.localPosition = new Vector3(
            leanDirection.x * maxRadius * 0.12f,
            CollarHeight,
            leanDirection.y * maxRadius * 0.12f
        );

        ApplyDustSurface(collarMaterial, DustShadow, DustLit, dustKeyLit);
        ApplySilhouette(collarMaterial, collarSeed);
        collarMaterial.SetFloat(InnerRagId, 0.55f);
        collarMaterial.SetFloat(FormOutId, WallFoot * 0.88f);
        collarMaterial.SetFloat(LumpScaleId, LumpCycles * 1.45f);
        collarMaterial.SetFloat(OpacityId, 0f);
    }

    void BuildClods(float massWeight, float groundDust)
    {
        Shader clodShader = Shader.Find("BattlePlan/ShockwavePuff");
        if (clodShader == null || maxRadius < 2.1f)
            return;

        int count = Mathf.RoundToInt(Mathf.Lerp(2f, 11f, massWeight) * Mathf.Clamp01(groundDust + 0.2f));
        if (count < 1)
            return;

        // One dominant mass, a few mid, a scattering of small, at better than 4:1 — an even spread
        // of same-sized blobs reads as confetti however dark they are.
        float sizeReference = Mathf.Min(maxRadius, 5f);
        float startAngle = Random.Range(0f, Mathf.PI * 2f);
        float leanAngle = Mathf.Atan2(leanDirection.y, leanDirection.x);
        clods = new Clod[count];

        for (int i = 0; i < count; i++)
        {
            MeshRenderer renderer = CreateQuad("DustClod", clodShader, out Material material);
            if (renderer == null)
                break;

            // Golden-angle stepping with jitter, warped toward the lean: no axis of symmetry for
            // the eye to lock onto, and more of the burst downwind than upwind.
            float angle = startAngle + i * 2.39996f + Random.Range(-0.4f, 0.4f);
            angle += (Mathf.Repeat(leanAngle - angle + Mathf.PI, Mathf.PI * 2f) - Mathf.PI) * 0.28f;
            float downwind =
                0.5f + 0.5f * (Mathf.Cos(angle) * leanDirection.x + Mathf.Sin(angle) * leanDirection.y);

            float spin = Random.Range(0f, 360f);
            float clodRadius;

            Clod clod = new()
            {
                Transform = renderer.transform,
                Material = material,
                Direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)),
                Height = ClodHeight + i * 0.0025f,
                Spin = spin,
                SpinRate = Random.Range(-22f, 22f),
                Stretch = Random.Range(0.16f, 0.42f),
                ErodeStart = Random.Range(0.16f, 0.4f),
                ErodeShape = Random.Range(0.9f, 1.9f),
                Seed = Random.Range(0f, 20f),
                Churn = Random.Range(0.35f, 1.1f),
                ShadeSwing = Random.Range(0.03f, 0.075f),
                ShadePulse = Random.Range(1.6f, 3.4f),
                ShadePhase = Random.Range(0f, Mathf.PI * 2f),
                ShadeEnd = Random.Range(0.9f, 1.1f),
            };

            if (i == 0)
            {
                // The mound left in the crater, which is what keeps the middle of the aftermath
                // from being an empty ring. It waits for the front to pass over it first.
                clodRadius = sizeReference * 0.5f;
                clod.Distance = maxRadius * Random.Range(0.05f, 0.18f);
                clod.Delay = 0.15f;
                clod.Lifetime = 0.62f;
                clod.DriftSpeed = maxRadius * Random.Range(0.06f, 0.13f);
                clod.Stretch *= 0.45f;
            }
            else if (i < 4)
            {
                clodRadius = sizeReference * Random.Range(0.235f, 0.285f);
                clod.Distance = maxRadius * Random.Range(0.34f, 0.62f);
                clod.Delay = Random.Range(0.17f, 0.22f);
                clod.Lifetime = Random.Range(0.48f, 0.55f);
                clod.DriftSpeed = maxRadius * Random.Range(0.16f, 0.34f);
            }
            else
            {
                clodRadius = sizeReference * Random.Range(0.115f, 0.165f);
                clod.Distance = maxRadius * Random.Range(0.52f, 0.88f);
                clod.Delay = Random.Range(0.2f, 0.28f);
                clod.Lifetime = Random.Range(0.38f, 0.46f);
                clod.DriftSpeed = maxRadius * Random.Range(0.22f, 0.45f);
            }

            // A small burst has less to settle, so its debris is off the deck sooner. Only the
            // largest bursts are worth holding an aftermath open for. The upwind side goes first,
            // which walks the whole aftermath's centroid downwind as it thins.
            clod.Lifetime *= Mathf.Lerp(0.65f, 1f, massWeight) * Mathf.Lerp(0.72f, 1.2f, downwind);
            clodRadius *= Mathf.Lerp(0.74f, 1.22f, downwind);
            clod.Distance *= Mathf.Lerp(0.84f, 1.16f, downwind);
            clod.QuadSize = clodRadius * 2f * ClodQuadPad;

            // Every clod is lit from the crater it was thrown out of, so a dozen of them read as a
            // dozen lumps of one material under one lamp rather than a dozen unrelated sprites.
            clod.TowardBlast = clod.Distance > 0.01f
                ? new Vector2(-clod.Direction.x, -clod.Direction.z)
                : -leanDirection;

            ApplyDustSurface(material, ClodShadow, ClodLit, clodKeyLit);
            material.SetVector(BlastDirId, InQuadFrame(clod.TowardBlast, spin));
            material.SetVector(ErodeDirId, InQuadFrame(leanDirection, spin));
            material.SetFloat(ErodeBiasId, Random.Range(0.22f, 0.38f));
            material.SetFloat(RadiusId, 1f / ClodQuadPad);
            material.SetFloat(RagId, Random.Range(0.2f, 0.3f));
            material.SetFloat(RagCountId, Random.Range(3f, 6f));
            material.SetFloat(ErodeScaleId, Random.Range(12f, 22f));
            material.SetFloat(LumpScaleId, Random.Range(18f, 28f));
            material.SetFloat(DomeReliefId, Random.Range(0.62f, 0.78f));
            material.SetFloat(KeyElevId, Random.Range(0.30f, 0.40f));
            material.SetFloat(SeedId, clod.Seed);
            material.SetFloat(OpacityId, clodOpacity);

            clod.Transform.localRotation = Quaternion.Euler(90f, 0f, spin);
            clod.Transform.localPosition = clod.Direction * (clod.Distance * 0.82f) + Vector3.up * clod.Height;
            clod.Transform.localScale = Vector3.zero;
            clods[i] = clod;
        }
    }

    void BuildLight(Color blastLight, float massWeight)
    {
        GameObject lightObject = new("ImpactLight");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = Vector3.up * Mathf.Min(1.5f, maxRadius * 0.5f);

        popLight = lightObject.AddComponent<Light>();
        // The lamp the dust is shaded against, so the pop and the shading agree about what colour
        // the blast is. A white pop washes hue off the deck at the one moment the frame has any.
        popLight.color = Color.Lerp(Color.white, blastLight, 0.62f);
        popLight.type = LightType.Point;
        popLight.range = maxRadius * 2.2f;
        popLight.shadows = LightShadows.None;

        // Short enough to be spent before the front arrives: the frame where the dust has to be at
        // its darkest is not a frame to be lifting the deck around it.
        lightPeak = Mathf.Lerp(3f, 6f, massWeight);
        lightLifetime = 0.13f;
        popLight.intensity = lightPeak;
    }

    IEnumerator Animate()
    {
        float elapsed = 0f;
        while (elapsed < totalLifetime)
        {
            FrontGeometry(elapsed, out float outerWorld, out float bandWorld);
            UpdateFront(elapsed, outerWorld, bandWorld);
            UpdateRim(elapsed, outerWorld);
            UpdateCollar(elapsed);
            UpdateCore(elapsed);
            UpdateClods(elapsed);
            UpdateLight(elapsed);

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Where the front is and how much of it there is, in world units. The crest rides the same two
    /// numbers so the light never drifts off the dust it is supposed to be lighting.
    /// </summary>
    void FrontGeometry(float elapsed, out float outerWorld, out float bandWorld)
    {
        float travel = Mathf.Clamp01(elapsed / frontTravel);
        float eased = 1f - Mathf.Pow(1f - travel, 3.2f);
        float decay = Mathf.Clamp01((elapsed - frontTravel) / Mathf.Max(0.01f, frontLife - frontTravel));

        // Past the crossing it only creeps, so the silhouette has stopped moving well before it
        // starts breaking up and the two reads never compete.
        outerWorld = Mathf.Lerp(maxRadius * 0.07f, maxRadius, eased) * Mathf.Lerp(1f, 1.03f, decay);
        bandWorld =
            Mathf.Lerp(maxRadius * 0.06f, frontBandWorld, eased) * Mathf.Lerp(1f, 0.45f, decay * decay);
    }

    void UpdateFront(float elapsed, float outerWorld, float bandWorld)
    {
        if (frontMaterial == null)
            return;

        float decay = Mathf.Clamp01((elapsed - frontTravel) / Mathf.Max(0.01f, frontLife - frontTravel));

        frontMaterial.SetFloat(RadiusId, Mathf.Clamp01(outerWorld / frontQuadHalf));
        frontMaterial.SetFloat(ThicknessId, Mathf.Clamp(bandWorld / Mathf.Max(outerWorld, 0.001f), 0.02f, 0.98f));
        // Opacity never ramps: the front is fully occluding from the first frame to the last, and
        // it leaves by losing pieces instead of by dissolving. The threshold starts well under the
        // erosion field's floor so "intact" means every pixel of it, not most of them.
        frontMaterial.SetFloat(ErodeId, Mathf.Lerp(-0.35f, 1.02f, Mathf.Pow(decay, 1.7f)));
        frontMaterial.SetFloat(SeedId, dustSeed + elapsed * 0.9f);
    }

    void UpdateRim(float elapsed, float outerWorld)
    {
        if (rimMaterial == null)
            return;

        float travel = Mathf.Clamp01(elapsed / frontTravel);
        float eased = 1f - Mathf.Pow(1f - travel, 3.2f);
        // Held flat across the arrival so the hot frame and the heavy frame are the same frame and
        // both survive a 30 Hz sample, then dropped.
        float fade = 1f - Mathf.Clamp01((elapsed - rimHold) / Mathf.Max(0.01f, rimLife - rimHold));

        float width = Mathf.Lerp(maxRadius * 0.06f, rimWidthWorld, eased);

        rimMaterial.SetFloat(RadiusId, Mathf.Clamp01(outerWorld / frontQuadHalf));
        rimMaterial.SetFloat(WidthId, Mathf.Clamp(width / frontQuadHalf, 0.002f, 0.5f));
        // Narrow, because the colour only survives in a narrow window: under the peak the band
        // drops below luminance 200 and over it the held channel goes too and the hue bleaches.
        rimMaterial.SetFloat(IntensityId, Mathf.Lerp(2.7f, 3.05f, eased) * Mathf.Pow(fade, 0.9f));
        // The crest reads the dust's relief, so it has to be handed the dust's drifting seed.
        rimMaterial.SetFloat(SeedId, dustSeed + elapsed * 0.9f);
    }

    void UpdateCollar(float elapsed)
    {
        if (collarMaterial == null)
            return;

        if (elapsed < collarDelay)
        {
            collarMaterial.SetFloat(OpacityId, 0f);
            return;
        }

        float since = elapsed - collarDelay;
        float travel = Mathf.Clamp01(since / collarTravel);
        float eased = 1f - Mathf.Pow(1f - travel, 2.6f);
        float decay = Mathf.Clamp01((since - collarTravel) / Mathf.Max(0.01f, collarLife - collarTravel));

        float outerWorld = Mathf.Lerp(maxRadius * 0.05f, collarOuterWorld, eased);
        float bandWorld =
            Mathf.Lerp(maxRadius * 0.04f, collarBandWorld, eased) * Mathf.Lerp(1f, 0.5f, decay * decay);

        collarMaterial.SetFloat(OpacityId, collarOpacity);
        collarMaterial.SetFloat(RadiusId, Mathf.Clamp01(outerWorld / collarQuadHalf));
        collarMaterial.SetFloat(ThicknessId, Mathf.Clamp(bandWorld / Mathf.Max(outerWorld, 0.001f), 0.02f, 0.98f));
        collarMaterial.SetFloat(ErodeId, Mathf.Lerp(-0.35f, 1.02f, Mathf.Pow(decay, 1.5f)));
        collarMaterial.SetFloat(SeedId, dustSeed * 1.7f - elapsed * 1.1f);
    }

    /// <summary>
    /// The flash's one money frame, held. Every layer under it peaks as the front arrives, so this
    /// arrives with them: up over two frames, flat across the crossing, then gone.
    /// <para>
    /// It collapses rather than fades. Alpha under one blends the written radiance back toward
    /// whatever is behind it, and a white disc that does that stops clipping while it is still the
    /// largest thing on screen; one that shrinks stays blown out for its whole life and changes
    /// silhouette on every frame instead. The white's share of the radius barely moves for the same
    /// reason the impact core's does not — retracting it while the mesh shrinks leaves the hue rim
    /// as the widest thing on screen and the panel reads as an eye rather than as a flash.
    /// </para>
    /// </summary>
    void UpdateCore(float elapsed)
    {
        if (coreMaterial == null && glowMaterial == null)
            return;

        float radius;
        float envelope;
        float plateau = 0.30f;

        if (elapsed < coreRise || elapsed >= coreLife)
        {
            radius = 0f;
            envelope = 0f;
        }
        else if (elapsed < coreHoldStart)
        {
            float rise = (elapsed - coreRise) / Mathf.Max(0.001f, coreHoldStart - coreRise);
            radius = coreRadiusWorld * Mathf.Lerp(0.42f, 1f, rise);
            envelope = rise;
        }
        else if (elapsed <= coreHoldEnd)
        {
            float drift = (elapsed - coreHoldStart) / Mathf.Max(0.001f, coreHoldEnd - coreHoldStart);
            radius = coreRadiusWorld * Mathf.Lerp(1f, 1.06f, drift);
            envelope = 1f;
            plateau = Mathf.Lerp(0.30f, 0.26f, drift);
        }
        else
        {
            float gone = (elapsed - coreHoldEnd) / Mathf.Max(0.001f, coreLife - coreHoldEnd);
            radius = coreRadiusWorld * 1.06f * (1f - 0.86f * Mathf.Pow(gone, 1.3f));
            envelope = (1f - gone) * (1f - gone);
            plateau = 0.26f;
        }

        if (coreMaterial != null)
        {
            coreMaterial.SetFloat(RadiusId, Mathf.Clamp01(radius / coreQuadHalf));
            coreMaterial.SetFloat(PlateauId, plateau);
            coreMaterial.SetFloat(OpacityId, radius > 0.001f ? 1f : 0f);
        }

        if (glowMaterial != null)
        {
            glowMaterial.SetFloat(InnerId, Mathf.Clamp01(radius / glowQuadHalf));
            glowMaterial.SetFloat(IntensityId, glowPeak * envelope);
        }
    }

    void UpdateClods(float elapsed)
    {
        if (clods == null)
            return;

        for (int i = 0; i < clods.Length; i++)
        {
            Clod clod = clods[i];
            if (clod.Transform == null || clod.Material == null)
                continue;
            if (elapsed < clod.Delay)
            {
                clod.Transform.localScale = Vector3.zero;
                continue;
            }

            float since = elapsed - clod.Delay;
            float progress = Mathf.Clamp01(since / Mathf.Max(0.01f, clod.Lifetime));
            float arrive = Mathf.Clamp01(progress / 0.16f);

            // Nothing here holds still for a frame: the mass walks downwind, turns, draws out along
            // one axis, loses its upwind flank first and keeps churning inside. A footprint and a
            // centroid that repeat across five frames read as a decal however dark they are.
            float creep = Mathf.Lerp(0.82f, 1.08f, 1f - Mathf.Pow(1f - progress, 2f));
            float spin = clod.Spin + clod.SpinRate * since;
            clod.Transform.localPosition =
                clod.Direction * (clod.Distance * creep)
                + new Vector3(leanDirection.x, 0f, leanDirection.y) * (clod.DriftSpeed * since)
                + Vector3.up * clod.Height;
            clod.Transform.localRotation = Quaternion.Euler(90f, 0f, spin);

            float stretch = clod.Stretch * Mathf.Pow(progress, 0.8f);
            float size =
                clod.QuadSize
                * Mathf.Lerp(0.42f, 1f, arrive)
                * Mathf.Lerp(1f, 0.66f, Mathf.Pow(progress, 1.4f));
            clod.Transform.localScale = new Vector3(
                size * (1f + stretch),
                size * (1f - stretch * 0.7f),
                size
            );

            float settle = Mathf.InverseLerp(clod.ErodeStart, 1f, progress);
            clod.Material.SetFloat(ErodeId, Mathf.Lerp(-0.15f, 1.2f, Mathf.Pow(settle, clod.ErodeShape)));
            clod.Material.SetFloat(SeedId, clod.Seed + since * clod.Churn);
            clod.Material.SetVector(BlastDirId, InQuadFrame(clod.TowardBlast, spin));
            clod.Material.SetVector(ErodeDirId, InQuadFrame(leanDirection, spin));

            float value =
                Mathf.Lerp(1f, clod.ShadeEnd, progress)
                * (1f + clod.ShadeSwing * Mathf.Sin(since * clod.ShadePulse + clod.ShadePhase));
            clod.Material.SetVector(DustDarkId, LinearRgb(ClodShadow * value));
            clod.Material.SetVector(DustLitId, LinearRgb(ClodLit * value));
            clod.Material.SetVector(DustKeyLitId, LinearRgb(clodKeyLit * value));
        }
    }

    void UpdateLight(float elapsed)
    {
        if (popLight == null)
            return;

        float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, lightLifetime));
        popLight.intensity = lightPeak * Mathf.Pow(1f - progress, 3f);
    }

    /// <summary>
    /// The boundary field the dust and the crest both evaluate. Feeding them the same numbers is
    /// what puts the hot arcs on the front's leading edge rather than somewhere near it.
    /// </summary>
    void ApplySilhouette(Material material, float seed)
    {
        material.SetFloat(OuterRagId, OutlineRag);
        material.SetFloat(RagCountId, BoundaryCells);
        material.SetFloat(LeanId, MassLean);
        material.SetFloat(VentsId, VentCount);
        material.SetFloat(VentWidthId, VentHalfWidth);
        material.SetFloat(ShapeSeedId, seed);
        material.SetVector(BiasDirId, new Vector4(leanDirection.x, leanDirection.y, 0f, 0f));
    }

    MeshRenderer CreateGroundQuad(string quadName, Shader shader, float height, out Material material)
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
        Mesh mesh = SharedQuadMesh();
        if (shader == null || mesh == null)
        {
            Debug.LogWarning($"[ImpactShockwave] missing shader or quad mesh for '{quadName}', skipping layer");
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

    static Mesh SharedQuadMesh()
    {
        if (sharedQuadMesh != null)
            return sharedQuadMesh;

        // Borrowed from a throwaway primitive: the built-in quad outlives the GameObject that
        // carried it, and this keeps a dozen clods from each paying for CreatePrimitive.
        // Deactivated before Destroy's end-of-frame sweep so its collider never touches the board.
        GameObject temporary = GameObject.CreatePrimitive(PrimitiveType.Quad);
        temporary.SetActive(false);
        MeshFilter filter = temporary.GetComponent<MeshFilter>();
        if (filter != null)
            sharedQuadMesh = filter.sharedMesh;
        Destroy(temporary);
        return sharedQuadMesh;
    }

    /// <summary>
    /// Which of the two bright arcs the caller's colour belongs to. Callers pass anything from a
    /// team hue to an HDR beam colour, and the exact hue cannot be honoured: at 234 degrees, where
    /// the friendly team sits, a saturated colour caps around luminance 150 and a bright one has
    /// bleached to cream long before it gets there. So a cool caller is answered in the 185-195
    /// band and a warm one in the 25-50 band, which are the two arcs that carry both. The team read
    /// survives the move; a pinstripe of the exact hue does not survive being looked at.
    /// </summary>
    static bool IsCoolHue(Color color)
    {
        float peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        Color ldr = peak > 1f ? new Color(color.r / peak, color.g / peak, color.b / peak) : color;

        Color.RGBToHSV(ldr, out float hue, out float saturation, out float value);
        if (saturation < 0.08f || value < 0.02f)
            return false;
        return hue >= 0.42f && hue < 0.83f;
    }

    /// <summary>
    /// The dust's own albedo seen under the blast's light rather than under the sky. Luminance is
    /// matched back to the untinted colour, so this puts hue into the mass without moving its
    /// value: the lighting decides the shape, and this decides only what colour the shape is.
    /// </summary>
    static Color LitBy(Color albedo, Color light)
    {
        Color mixed = new(
            albedo.r * Mathf.Lerp(1f, light.r, KeyTintStrength),
            albedo.g * Mathf.Lerp(1f, light.g, KeyTintStrength),
            albedo.b * Mathf.Lerp(1f, light.b, KeyTintStrength)
        );
        float want = Luminance(albedo);
        float got = Mathf.Max(Luminance(mixed), 1e-4f);
        float scale = Mathf.Min(want / got, 1f / Mathf.Max(mixed.maxColorComponent, 1e-4f));
        return mixed * scale;
    }

    static float Luminance(Color color) => 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;

    /// <summary>
    /// The lighting model every dust layer shares. Handing the dust and the crest the same lump
    /// field is what lets the crest burn along the lumps instead of across them.
    /// </summary>
    void ApplyDustSurface(Material material, Color shadow, Color skyLit, Color keyLit)
    {
        material.SetVector(DustDarkId, LinearRgb(shadow));
        material.SetVector(DustLitId, LinearRgb(skyLit));
        material.SetVector(DustKeyLitId, LinearRgb(keyLit));
        material.SetFloat(LumpScaleId, LumpCycles);
        material.SetFloat(LumpReliefId, LumpSlope);
        material.SetFloat(LumpShareId, LumpShare);
        material.SetFloat(FormInId, WallCrest);
        material.SetFloat(FormOutId, WallFoot);
        material.SetFloat(AmbientId, DustAmbient);
        material.SetFloat(KeyId, DustKey);
        material.SetFloat(KeyElevId, DustKeyElevation);
        material.SetFloat(KeyGammaId, DustKeyGamma);
        material.SetFloat(SkyId, DustSky);
        material.SetFloat(MicroId, DustMicro);
        material.SetFloat(TintReachId, 1.05f);
    }

    /// <summary>
    /// Linear rgb for a <c>Vector</c> shader property. Colour properties are converted on the way
    /// in by the setter and these values are chosen for what they composite to in frame, so they
    /// are handed over already converted and never guessed at twice.
    /// </summary>
    static Vector4 LinearRgb(Color srgb)
    {
        Color linear = srgb.linear;
        return new Vector4(linear.r, linear.g, linear.b, 1f);
    }

    /// <summary>
    /// A board-space xz direction expressed in a quad's own uv frame, so spinning a clod to break
    /// the stamped-sprite read does not also spin where the light comes from or which flank of it
    /// gives way first.
    /// </summary>
    static Vector4 InQuadFrame(Vector2 boardDirection, float spinDegrees)
    {
        float radians = -spinDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector4(
            boardDirection.x * cos - boardDirection.y * sin,
            boardDirection.x * sin + boardDirection.y * cos,
            0f,
            0f
        );
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
