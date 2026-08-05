using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Anticipation: the beat before an ability lands, where the board tells you it is coming and the
/// caster visibly commits to it.
/// <para>
/// This is the frame a player should be able to read and brace against. It carries two jobs at
/// once — mark the ground that is about to be dangerous, and show the caster loading the action —
/// and it has to do both without ever being mistaken for the impact itself.
/// </para>
/// <para>Purely local and visual. Call on every peer; never gate gameplay on it.</para>
/// </summary>
public static class AbilityWindup
{
    static readonly Dictionary<Transform, AbilityWindupRunner> RunningByCaster = new();

    /// <summary>
    /// Plays the wind-up for an ability that will resolve at <paramref name="target"/> in
    /// <paramref name="seconds"/>.
    /// </summary>
    /// <param name="caster">The unit committing to the ability; may be null for casterless FX.</param>
    /// <param name="target">World position the ability will resolve at.</param>
    /// <param name="color">Owning team's colour, already viewer-relative.</param>
    /// <param name="seconds">How long until impact. The whole wind-up must complete inside this.</param>
    /// <param name="shape">How the danger area should be marked.</param>
    /// <param name="radius">World-space radius of the danger area, for Disc and Point.</param>
    public static void Play(
        Transform caster,
        Vector3 target,
        Color color,
        float seconds,
        WindupShape shape,
        float radius = 2.5f
    )
    {
        // Below this the countdown cannot be read, and a one-frame telegraph reads as a glitch.
        if (seconds < 0.12f)
            return;

        Cancel(caster);

        GameObject host = new("AbilityWindup");
        AbilityWindupRunner runner = host.AddComponent<AbilityWindupRunner>();
        if (caster != null)
            RunningByCaster[caster] = runner;
        runner.Begin(caster, target, color, seconds, shape, Mathf.Max(0.6f, radius));
    }

    /// <summary>Cancels any wind-up still running for <paramref name="caster"/>.</summary>
    public static void Cancel(Transform caster)
    {
        if (caster == null)
            return;
        if (!RunningByCaster.TryGetValue(caster, out AbilityWindupRunner runner))
            return;
        RunningByCaster.Remove(caster);
        if (runner != null)
            runner.Abort();
    }

    internal static void Forget(Transform caster, AbilityWindupRunner runner)
    {
        if (caster == null || runner == null)
            return;
        if (RunningByCaster.TryGetValue(caster, out AbilityWindupRunner current) && current == runner)
            RunningByCaster.Remove(caster);
    }
}

/// <summary>
/// Drives one wind-up: the hazard plate whose cells arm one at a time, the countdown ring that
/// closes on the target, and the caster's load and release. Spawned and owned by
/// <see cref="AbilityWindup"/>.
/// </summary>
class AbilityWindupRunner : MonoBehaviour
{
    static readonly int TintMulId = Shader.PropertyToID("_TintMul");
    static readonly int TintStrengthId = Shader.PropertyToID("_TintStrength");
    static readonly int BullseyeMulId = Shader.PropertyToID("_BullseyeMul");
    static readonly int MarkMulId = Shader.PropertyToID("_MarkMul");
    static readonly int MarkOnTintId = Shader.PropertyToID("_MarkOnTint");
    static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    static readonly int RingColorId = Shader.PropertyToID("_RingColor");
    static readonly int KeyColorId = Shader.PropertyToID("_KeyColor");
    static readonly int SeamColorId = Shader.PropertyToID("_SeamColor");
    static readonly int SeamAlphaId = Shader.PropertyToID("_SeamAlpha");
    static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
    static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
    static readonly int CellOffsetId = Shader.PropertyToID("_CellOffset");
    static readonly int SegmentId = Shader.PropertyToID("_Segment");
    static readonly int HalfWidthId = Shader.PropertyToID("_HalfWidth");
    static readonly int BorderWidthId = Shader.PropertyToID("_BorderWidth");
    static readonly int KeyWidthId = Shader.PropertyToID("_KeyWidth");
    static readonly int SeamWidthId = Shader.PropertyToID("_SeamWidth");
    static readonly int BullseyeWidthId = Shader.PropertyToID("_BullseyeWidth");
    static readonly int CasterId = Shader.PropertyToID("_Caster");
    static readonly int OrderLateralId = Shader.PropertyToID("_OrderLateral");
    static readonly int OrderBiasId = Shader.PropertyToID("_OrderBias");
    static readonly int RevealFrontId = Shader.PropertyToID("_RevealFront");
    static readonly int ArmFrontId = Shader.PropertyToID("_ArmFront");
    static readonly int ArmFlashId = Shader.PropertyToID("_ArmFlash");
    static readonly int ChevronReachId = Shader.PropertyToID("_ChevronReach");
    static readonly int ChevronHalfId = Shader.PropertyToID("_ChevronHalf");
    static readonly int ChevronWidthId = Shader.PropertyToID("_ChevronWidth");
    static readonly int RingRadiusId = Shader.PropertyToID("_RingRadius");
    static readonly int RingThicknessId = Shader.PropertyToID("_RingThickness");
    static readonly int RingAlphaId = Shader.PropertyToID("_RingAlpha");
    static readonly int RingHotId = Shader.PropertyToID("_RingHot");
    static readonly int RingHotFracId = Shader.PropertyToID("_RingHotFrac");
    static readonly int RingWobbleId = Shader.PropertyToID("_RingWobble");
    static readonly int RingOnTintId = Shader.PropertyToID("_RingOnTint");
    static readonly int BladeModeId = Shader.PropertyToID("_BladeMode");
    static readonly int FillId = Shader.PropertyToID("_Fill");
    static readonly int GlintId = Shader.PropertyToID("_Glint");
    static readonly int SeedId = Shader.PropertyToID("_Seed");
    static readonly int LightDirId = Shader.PropertyToID("_LightDir");
    static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    static readonly int GlintColorId = Shader.PropertyToID("_GlintColor");
    static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // Above the fog overlay tiles at y = 0.05 and below the shockwaves at y = 0.08.
    const float PlateHeight = 0.056f;

    // The board floor sits near luminance 180 with almost no hue, so the danger area is carved out
    // of it by multiplying down into a saturated amber rather than by adding light to it. These are
    // linear multipliers against the floor; they land the plate around luminance 113 falling to 98,
    // at saturation 0.49 rising to 0.65, with the floor's own tile seams still reading through.
    static readonly Vector4 OpeningTint = new(0.66f, 0.26f, 0.075f, 0f);
    static readonly Vector4 LoadedTint = new(0.58f, 0.22f, 0.055f, 0f);
    const float BullseyeMultiply = 0.66f;

    // The ring and the chevrons are cut with the same multiply, one step deeper than the tint, so
    // both stay amber material rather than turning into grey haze. On clean floor this lands near
    // luminance 85 at saturation 0.62; eased back over the plate it lands near 79 and keeps its hue
    // instead of bottoming out into black.
    static readonly Vector4 MarkMultiply = new(0.25f, 0.108f, 0.027f, 0f);
    const float MarkOverTint = 0.72f;

    // A count, not a fade. Cells switch on one at a time across the whole wind-up, so the plate is
    // a third of its final size a third of the way in and the last square lands just before the
    // ability does. The ladder tightens as it runs: a count whose last step falls two hundred
    // milliseconds short of the event stops counting to anything.
    const float ArmFirst = 0.04f;
    const float ArmLast = 0.927f;
    const float ArmCurve = 1.15f;
    const float ArmFlashSeconds = 0.055f;

    // Only breaks ties between cells the caster stands equidistant from. Skewed off the lateral
    // axis so cells mirrored through the caster cannot arm on the same frame either.
    const float OrderBias = 0.13f;
    const float OrderSkew = 0.35f;

    // A hard hairline, five or six pixels at the board camera. The countdown earns its contrast
    // from being dark and crisp, not from being wide: a wide soft trough on this floor is worth
    // twenty luminance levels, a hard thin one is worth ninety.
    const float RingBandWidth = 0.1f;
    const float RingStartCells = 2.2f;
    const float RingWobble = 0.014f;

    // The collapsed ring has to clear the silhouette of whatever is standing on the target cell,
    // or the one frame the whole countdown exists for lands behind a unit.
    const float CollapseThickness = 2.6f;
    const float CollapseSnapThickness = 3.4f;
    const float RingCoreGain = 2.6f;
    const float RingCoreFraction = 0.8f;

    // Chevrons sit on the threatened tiles, one per armed cell, pointing the way the danger is
    // spreading. Roughly a tenth of a cell of ink each, so a full nine-cell plate carries about one
    // whole cell of them.
    const float ChevronReach = 0.45f;
    const float ChevronHalf = 0.9f;
    const float ChevronWidth = 0.21f;

    // Resolved on first use, not in a field initializer: the first Play call reaches these from
    // inside AddComponent, and Unity refuses to report the active colour space during a
    // MonoBehaviour constructor.
    static Color? alarmInk;
    static Color? keyInk;
    static Color? seamInk;
    static Color? ringCoreInk;

    static Color AlarmInk => alarmInk ??= Srgb(1f, 0.541f, 0.031f);
    static Color KeyInk => keyInk ??= Srgb(0.094f, 0.047f, 0.024f);
    static Color SeamInk => seamInk ??= Srgb(0.588f, 0.235f, 0.071f);

    // Redder than the alarm so that driving it into bloom clips one channel and leaves the others
    // room. An amber core scaled far enough to bloom bleaches to cream; this one stays orange.
    static Color RingCoreInk => ringCoreInk ??= Srgb(1f, 0.36f, 0.02f);

    // The charge's palette: one turquoise hue at five values, linear rgb, handed to the shader
    // unconverted the way the shockwave's crest is.
    //
    // Warm was not available to it. A pixel whose red channel has clipped reaches luminance
    // 255 * (0.2126 + 0.7874 * (1 - s) + 0.0119 * h * s) at hue h and saturation s, so at
    // saturation 0.45 it does not clear luminance 200 until hue 26 and at saturation 0.6 not until
    // hue 37: every warm colour that is both bright and saturated sits inside twenty degrees of the
    // amber this same wind-up is burning into the floor, which is exactly the collision to be
    // avoided. Near hue 165 the same saturation reaches luminance 220, and 165 is far enough off
    // the 185-195 arc the death blast answers a cool caller in for the two never to be confused.
    static readonly Vector4 ChargeLit = new(0.9f, 3f, 1.78f, 1f);
    static readonly Vector4 ChargeHot = new(1.152f, 3.84f, 2.28f, 1f);
    static readonly Vector4 ChargeDeep = new(0.522f, 1.74f, 1.032f, 1f);

    // The gather is the same colour as matter rather than as light: its lit faces sit just under
    // the orb's and its unlit faces sit far under the board, so every blade has a silhouette on a
    // floor at luminance 182 without spending the hue that makes it read as a charge.
    static readonly Vector4 BladeLit = new(0.738f, 2.46f, 1.46f, 1f);
    static readonly Vector4 BladeHot = new(1.098f, 3.66f, 2.17f, 1f);
    static readonly Vector4 BladeDeep = new(0.144f, 0.48f, 0.285f, 1f);

    // Written past the point where all three channels resolve to 255 so it lands dead flat, and
    // held to about a tenth of the orb for it: this tonemapper trades hue for every channel driven
    // past the clip point, so white is a glint inside the colour and never a mass of its own.
    static readonly Vector4 ChargeGlint = new(3.2f, 3.5f, 3.4f, 1f);
    static readonly Vector4 ChargeKeyline = new(0.008f, 0.017f, 0.014f, 1f);

    // Hard geometry, in world units. At the board camera these are roughly 9, 4 and 2 pixels.
    const float BorderWidth = 0.15f;
    const float KeyWidth = 0.06f;
    const float SeamHalfWidth = 0.03f;
    const float BullseyeWidth = 0.1f;

    // Nothing may keep warning about an event that has already happened.
    const float CutSeconds = 0.055f;

    // The pull-back runs the whole wind-up on a square ease-in, so the caster is already travelling
    // in the first frame and is at its fastest on the frame the ability fires. A window at the end
    // is an ease-*out* into the trigger: it parks the caster for the last two hundred milliseconds,
    // which is the one moment it must not be still.
    const float PullBackCells = 0.58f;
    const float LoadLean = 18f;
    const float LoadSpread = 1.15f;
    const float LoadFlatten = 0.87f;
    const float LoadSink = 0.06f;

    // Travel and shape are on separate clocks. Half a degree of lean per frame is invisible, so the
    // gather lands most of its crouch early and then keeps deepening; the travel stays accelerating
    // underneath it.
    const float GatherSnap = 0.45f;
    const float GatherFirst = 0.06f;
    const float GatherSet = 0.26f;

    // Held tension. Sampled at thirty hertz an eleven-hertz shiver aliases into a different phase
    // every frame, which is what keeps two neighbouring frames from ever being identical.
    const float TrembleReach = 0.05f;
    const float TrembleRate = 70f;

    // The body warms through the whole wind-up even while the pose is still. Colour is the one
    // anticipation cue that costs no travel, and the caster stands too near the frame edge to
    // spend travel on anything but the load itself.
    const float BodyWarmth = 0.65f;

    // A quarter of a cell, standing just past the barrel. The caster's own silhouette measures
    // about eighty-five pixels square at the board camera, and anything carried on the unit much
    // larger than a third of that stops being something the unit is holding and becomes something
    // covering the unit — its outline, its rifle, its facing and its health bar with it.
    const float OrbCells = 0.25f;
    const float OrbClearance = 0.3f;
    const float OrbDepthLift = 0.22f;
    // Two or three pixels at the board camera. Thinner than that and bloom eats the orb's own
    // outline on the way out and it stops being an object the caster is holding.
    const float OrbKeyWidth = 0.13f;
    const float MuzzleForwardFallback = 1.15f;
    const float MuzzleHeightFallback = 1.02f;

    // The orb lights early because it is what identifies the charge, but it is a fifth of the
    // charge's area and never more: it reaches full size on the same beat as everything else.
    const float OrbSeed = 0.45f;
    const float OrbIgnite = 0.06f;
    const float OrbAlight = 0.2f;
    const float OrbFull = 0.97f;
    const float OrbGrowCurve = 0.55f;

    // The gather is a count, not a ramp. Five fine blades are in by a third of the way through and
    // the dominant one lands in the last breath, so the charge multiplies between panels rather
    // than holding one silhouette on a size curve — a shape that only grows is a state icon.
    const float GatherForward = 0.45f;
    const float GatherFarCells = 0.87f;
    const float GatherNearCells = 0.53f;
    const float GatherConvergeFirst = 0.1f;
    const float GatherConvergeLast = 0.98f;
    const float BladeFlightSpan = 0.14f;
    const float BladeEntryScale = 0.35f;
    const float BladeEntryReachCells = 0.8f;
    const float BladeAspect = 0.62f;
    const float BladeKeyWidth = 0.13f;
    const float BladeDriftRate = 2.3f;

    // Full size arrives inside the last hundred and fifty milliseconds, which is the only stretch
    // of a wind-up a player is braced for.
    const float SurgeSeconds = 0.15f;
    const float SurgeGain = 0.18f;

    // One dominant blade, three mid, five small, four fine, on angles and radii that do not repeat
    // and arriving smallest first. The arc is open across the front, so nothing ever crosses the
    // rifle, the muzzle or the orb, and its near radius clears the caster's silhouette by a third
    // of a cell however far the pull-back has carried it.
    static readonly float[] BladeCells =
        { 0.5f, 0.42f, 0.39f, 0.37f, 0.33f, 0.31f, 0.3f, 0.28f, 0.26f, 0.24f, 0.22f, 0.2f, 0.16f };
    static readonly float[] BladeArrive =
    {
        0.845f, 0.805f, 0.755f, 0.7f, 0.63f, 0.55f, 0.46f, 0.36f, 0.28f, 0.245f, 0.21f, 0.175f,
        0.14f,
    };
    static readonly float[] BladeAngleDegrees =
        { 137f, 228f, 178f, 104f, 255f, 196f, 155f, 268f, 118f, 211f, 88f, 243f, 168f };
    static readonly float[] BladeRadiusCells =
        { 0.02f, 0.04f, 0.11f, 0.08f, 0.1f, 0.01f, 0.16f, 0.05f, 0.18f, 0.16f, 0.13f, 0.19f, 0.2f };
    static readonly float[] BladeDriftDegrees =
        { 2.4f, -3.1f, 1.8f, -2.2f, 3.4f, -1.5f, 2.7f, -3.6f, 1.9f, -2.8f, 3.2f, -1.7f, 2.1f };

    // Alternating sides of the gather plane, so two transparent quads at the same distance never
    // swap order between frames.
    static readonly float[] BladeDepth =
    {
        0.03f, -0.03f, 0.05f, -0.05f, 0.02f, -0.02f, 0.04f, -0.04f, 0.06f, -0.06f, 0.01f, -0.01f,
        0.07f,
    };

    // The shot takes the charge with it: the orb stretches down the line of fire and the gather is
    // eaten head-first, both gone on the plate's own beat.
    const float DischargeStretch = 2.8f;
    const float DischargeFlatten = 1.25f;
    const float DischargeTravelCells = 0.34f;
    const float DischargeEatenTo = 0.42f;

    const float StrikeReachCells = 0.2f;
    const float StrikeLean = 26f;
    const float StrikeSpread = 0.92f;
    const float StrikeStretch = 1.1f;
    const float StrikeLift = 0.09f;

    const float PeakSeconds = 0.08f;
    const float HoldSeconds = 0.2f;
    const float SettleSeconds = 0.42f;
    const float ReleaseFlashSeconds = 0.16f;

    // Multipliers on the caster's own albedo: loading sinks the body toward a warm silhouette, the
    // release is the only beat where it brightens.
    static readonly Color LoadInk = new(0.6f, 0.47f, 0.4f, 1f);
    static readonly Color DriveInk = new(2f, 1.15f, 0.42f, 1f);

    static readonly string[] NonBodyNames =
    {
        "canvas", "bar", "alert", "vision", "cone", "puck", "overlay", "indicator",
        "laser", "path", "glow", "ring", "shadow", "marker", "range",
    };

    struct BodyPart
    {
        public Transform Part;
        public Vector3 BasePosition;
        public Quaternion BaseRotation;
        public Vector3 BaseScale;
    }

    Transform caster;
    Vector3 target;
    Color teamColor;
    float duration;
    WindupShape shape;
    float radius;

    Transform plate;
    Material plateMaterial;
    float ringStartRadius;
    float ringFillRadius;

    // Live cells sorted by where they fall in the countdown, and the moment each of them arms.
    float[] cellOrders;
    float[] cellArmProgress;

    BodyPart[] bodyParts;
    Renderer[] bodyRenderers;
    Color[] bodyAlbedo;
    MaterialPropertyBlock bodyProperties;
    bool inkApplied;

    Transform chargeRoot;
    Transform orbQuad;
    Transform[] bladeQuads;
    Renderer[] bladeRenderers;
    Material orbMaterial;
    Material bladeMaterial;
    MaterialPropertyBlock bladeProperties;
    Transform chargeFacing;
    float muzzleForward;
    float muzzleHeight;
    float poseReach;
    float poseLift;

    Vector3 aim = Vector3.forward;
    float beatPhase;
    Coroutine routine;
    bool aborted;

    internal void Begin(
        Transform casterTransform,
        Vector3 targetPosition,
        Color color,
        float seconds,
        WindupShape windupShape,
        float dangerRadius
    )
    {
        caster = casterTransform;
        target = targetPosition;
        teamColor = color;
        duration = seconds;
        shape = windupShape;
        radius = shape == WindupShape.Point
            ? Mathf.Min(dangerRadius, GameLoop.cellSize * 0.55f)
            : dangerRadius;

        Vector3 casterPosition = caster != null ? caster.position : target - Vector3.forward;
        Vector3 toTarget = target - casterPosition;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.0004f)
            aim = toTarget.normalized;
        else if (caster != null)
            aim = caster.forward;

        Camera board = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
        if (board == null)
            board = Camera.main;
        chargeFacing = board != null ? board.transform : null;

        if (shape != WindupShape.Self)
            BuildMark();
        CollectCasterBody();
        BuildCharge();

        routine = StartCoroutine(Run());
    }

    internal void Abort()
    {
        aborted = true;
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        RestoreCasterBody();
        if (this != null)
            Destroy(gameObject);
    }

    IEnumerator Run()
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float progress = Mathf.Clamp01(elapsed / duration);
            beatPhase += Time.deltaTime * Mathf.Lerp(2.2f, 9f, progress * progress);
            UpdateMark(progress, elapsed);
            UpdateCasterLoad(elapsed);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // The fire frame. The countdown lands dead centre while the plate is still down, so the
        // mark resolves onto a point instead of dissolving.
        UpdateMark(1f, duration);
        UpdateCasterLoad(duration);
        yield return null;

        float sinceFire = 0f;
        while (sinceFire < CutSeconds)
        {
            float cut = Mathf.Clamp01(sinceFire / CutSeconds);
            UpdateCut(cut);
            UpdateCasterRelease(sinceFire);
            UpdateChargeExit(cut);
            sinceFire += Time.deltaTime;
            yield return null;
        }

        DestroyMark();
        DestroyCharge();

        float recover = PeakSeconds + HoldSeconds + SettleSeconds;
        while (bodyParts != null && sinceFire < recover)
        {
            UpdateCasterRelease(sinceFire);
            sinceFire += Time.deltaTime;
            yield return null;
        }

        routine = null;
        RestoreCasterBody();
        Destroy(gameObject);
    }

    // ---------------------------------------------------------------- hazard plate

    void BuildMark()
    {
        Shader telegraph = Shader.Find("BattlePlan/WindupTelegraph");
        if (telegraph == null)
        {
            Debug.LogWarning(
                "[AbilityWindup] BattlePlan/WindupTelegraph shader not found, skipping telegraph"
            );
            return;
        }

        float cell = Mathf.Max(0.1f, GameLoop.cellSize);
        Vector3 gridOrigin = GameLoop.gridCoordToWorld(Vector2Int.zero);
        Vector2 cellOffset = new(
            Mathf.Round((target.x - gridOrigin.x) / cell) * cell + gridOrigin.x - target.x,
            Mathf.Round((target.z - gridOrigin.z) / cell) * cell + gridOrigin.z - target.z
        );

        // A disc is the degenerate case of a lane, so both shapes share one distance test.
        Vector2 segmentEnd = Vector2.zero;
        if (shape == WindupShape.Line && caster != null)
        {
            segmentEnd = new Vector2(
                caster.position.x - target.x,
                caster.position.z - target.z
            );
        }

        // A live cell's centre is at most `radius` from the segment, and the cell itself reaches
        // half a cell further than its centre.
        Vector2 low = Vector2.Min(segmentEnd, Vector2.zero) - Vector2.one * radius;
        Vector2 high = Vector2.Max(segmentEnd, Vector2.zero) + Vector2.one * radius;
        float plateHalfExtent =
            Mathf.Max(Mathf.Max(-low.x, high.x), Mathf.Max(-low.y, high.y)) + cell * 0.5f;

        Vector2 aimFlat = new(aim.x, aim.z);
        aimFlat = aimFlat.sqrMagnitude > 0.0001f ? aimFlat.normalized : Vector2.up;
        Vector2 casterFlat = caster != null
            ? new Vector2(caster.position.x - target.x, caster.position.z - target.z)
            : -aimFlat * (cell * 2f);
        // A caster standing on its own target gives no direction to count outward along.
        if (casterFlat.magnitude < cell * 0.75f)
            casterFlat = -aimFlat * (cell * 2f);
        Vector2 orderLateral = (new Vector2(-aimFlat.y, aimFlat.x) + aimFlat * OrderSkew).normalized;

        float plateOuterRadius = BuildCount(
            cell,
            cellOffset,
            segmentEnd,
            plateHalfExtent,
            casterFlat,
            orderLateral
        );

        ringFillRadius = cell * 0.5f;
        // Wide enough to start outside whatever it is counting down on, but a long lane must not
        // drag the hoop out to the horizon.
        ringStartRadius = Mathf.Clamp(plateOuterRadius, RingStartCells * cell, 4f * cell);
        float quadHalf =
            Mathf.Max(plateHalfExtent + BorderWidth + KeyWidth, ringStartRadius + 0.9f) + 0.2f;

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "WindupHazardPlate";
        Collider quadCollider = quad.GetComponent<Collider>();
        if (quadCollider != null)
            Destroy(quadCollider);

        quad.transform.SetParent(transform, false);
        quad.transform.position = new Vector3(target.x, PlateHeight, target.z);
        // No yaw: the shader reads board coordinates straight off the quad's UVs.
        quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = Vector3.one * (quadHalf * 2f);
        plate = quad.transform;

        plateMaterial = new Material(telegraph);
        Renderer quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.material = plateMaterial;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        quadRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        plateMaterial.SetFloat(QuadSizeId, quadHalf * 2f);
        plateMaterial.SetFloat(CellSizeId, cell);
        plateMaterial.SetVector(CellOffsetId, cellOffset);
        plateMaterial.SetVector(SegmentId, new Vector4(segmentEnd.x, segmentEnd.y, 0f, 0f));
        plateMaterial.SetFloat(HalfWidthId, radius);
        plateMaterial.SetVector(TintMulId, OpeningTint);
        plateMaterial.SetFloat(BullseyeMulId, BullseyeMultiply);
        plateMaterial.SetVector(MarkMulId, MarkMultiply);
        plateMaterial.SetFloat(MarkOnTintId, MarkOverTint);
        plateMaterial.SetColor(EdgeColorId, AlarmInk);
        plateMaterial.SetColor(SeamColorId, SeamInk);
        plateMaterial.SetColor(RingColorId, AbilityJuice.Hot(RingCoreInk, RingCoreGain));
        // Danger reads as danger whoever cast it, so only the near-black keyline carries ownership.
        plateMaterial.SetColor(KeyColorId, Color.Lerp(KeyInk, teamColor * 0.16f, 0.35f));
        plateMaterial.SetFloat(BorderWidthId, BorderWidth);
        plateMaterial.SetFloat(KeyWidthId, KeyWidth);
        plateMaterial.SetFloat(SeamWidthId, SeamHalfWidth);
        plateMaterial.SetFloat(BullseyeWidthId, BullseyeWidth);

        plateMaterial.SetVector(
            CasterId,
            new Vector4(casterFlat.x, casterFlat.y, aimFlat.x, aimFlat.y)
        );
        plateMaterial.SetVector(OrderLateralId, orderLateral);
        plateMaterial.SetFloat(OrderBiasId, OrderBias);
        plateMaterial.SetFloat(ChevronReachId, ChevronReach);
        plateMaterial.SetFloat(ChevronHalfId, ChevronHalf);
        plateMaterial.SetFloat(ChevronWidthId, ChevronWidth);

        plateMaterial.SetFloat(RingWobbleId, RingWobble);
        plateMaterial.SetFloat(RingHotFracId, RingCoreFraction);
        plateMaterial.SetFloat(RingAlphaId, 1f);

        UpdateMark(0f, 0f);
    }

    /// <summary>
    /// Enumerates the cells this wind-up will cover and puts them in countdown order, nearest the
    /// caster first, then hands each one the moment it arms. The shader recomputes the same order
    /// per fragment, so the two have to agree — which is why the reveal front is parked halfway
    /// between neighbouring cells rather than exactly on one.
    /// </summary>
    /// <returns>Distance from the target to the outermost corner of the covered area.</returns>
    float BuildCount(
        float cell,
        Vector2 cellOffset,
        Vector2 segmentEnd,
        float plateHalfExtent,
        Vector2 casterFlat,
        Vector2 orderLateral
    )
    {
        int span = Mathf.Clamp(Mathf.CeilToInt(plateHalfExtent / cell) + 1, 1, 16);
        List<float> orders = new();
        float outerRadius = 0f;

        for (int ix = -span; ix <= span; ix++)
        {
            for (int iy = -span; iy <= span; iy++)
            {
                Vector2 centre = new(ix * cell + cellOffset.x, iy * cell + cellOffset.y);
                if (SegmentDistance(centre, segmentEnd) > radius)
                    continue;

                Vector2 toCell = centre - casterFlat;
                orders.Add(toCell.magnitude + OrderBias * Vector2.Dot(toCell, orderLateral));
                outerRadius = Mathf.Max(outerRadius, centre.magnitude + cell * 0.7071f);
            }
        }

        orders.Sort();
        cellOrders = orders.ToArray();
        cellArmProgress = new float[cellOrders.Length];
        for (int i = 0; i < cellOrders.Length; i++)
        {
            if (cellOrders.Length <= 1)
            {
                cellArmProgress[i] = ArmFirst;
                continue;
            }

            // Evenly spaced steps leave the last one landing well before the event. Tightening the
            // ladder keeps the opening cadence readable and still lands the final square inside the
            // last tenth of a second.
            float step = i / (float)(cellOrders.Length - 1);
            cellArmProgress[i] =
                Mathf.Lerp(ArmFirst, ArmLast, 1f - Mathf.Pow(1f - step, ArmCurve));
        }

        return outerRadius;
    }

    /// <summary>Mirrors the shader's danger test: distance from a point to the danger segment.</summary>
    static float SegmentDistance(Vector2 point, Vector2 segmentStart)
    {
        Vector2 run = -segmentStart;
        float along = Mathf.Clamp01(
            Vector2.Dot(point - segmentStart, run) / Mathf.Max(Vector2.Dot(run, run), 1e-5f)
        );
        return (point - segmentStart - run * along).magnitude;
    }

    void UpdateMark(float progress, float elapsed)
    {
        if (plateMaterial == null)
            return;

        float pressure = Mathf.Pow(progress, 1.7f);
        float beat = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(beatPhase * Mathf.PI * 2f), 3f);

        plateMaterial.SetVector(TintMulId, Vector4.Lerp(OpeningTint, LoadedTint, pressure));
        plateMaterial.SetFloat(TintStrengthId, Mathf.Lerp(0.94f, 1f, Mathf.Clamp01(progress * 3f)));
        plateMaterial.SetFloat(BorderWidthId, BorderWidth * (1f + 0.5f * beat * pressure));
        plateMaterial.SetFloat(SeamAlphaId, Mathf.Lerp(0.35f, 0.62f, pressure));

        ApplyCount(progress, elapsed);
        ApplyRing(progress);
    }

    /// <summary>
    /// Walks the reveal front through the cell order. Only the cell that armed most recently
    /// carries the flash, and it is always the highest ordered live one.
    /// </summary>
    void ApplyCount(float progress, float elapsed)
    {
        if (cellOrders == null || cellOrders.Length == 0)
            return;

        int armed = 0;
        while (armed < cellOrders.Length && progress >= cellArmProgress[armed])
            armed++;

        float front;
        if (armed == 0)
            front = cellOrders[0] - 1f;
        else if (armed >= cellOrders.Length)
            front = cellOrders[cellOrders.Length - 1] + 1f;
        else
            front = 0.5f * (cellOrders[armed - 1] + cellOrders[armed]);

        float armFront =
            armed >= 2 ? 0.5f * (cellOrders[armed - 2] + cellOrders[armed - 1]) : cellOrders[0] - 1f;

        float flash = 0f;
        if (armed > 0)
        {
            float since = elapsed - cellArmProgress[armed - 1] * duration;
            flash = 1f - Mathf.Clamp01(since / ArmFlashSeconds);
        }

        plateMaterial.SetFloat(RevealFrontId, front);
        plateMaterial.SetFloat(ArmFrontId, armFront);
        plateMaterial.SetFloat(ArmFlashId, Mathf.Clamp01(flash));
    }

    /// <summary>
    /// The hoop reaches the impact point on the exact frame the ability fires, and spends the last
    /// fifth of the wind-up covering a third of its travel. It is dark amber material the whole way
    /// in and only becomes light once its radius has run out.
    /// </summary>
    void ApplyRing(float progress)
    {
        float cell = Mathf.Max(0.1f, GameLoop.cellSize);
        float ringRadius = ringStartRadius * (1f - progress * progress);

        // The band holds its hairline width until the radius is all but gone. A hoop that fattens
        // as it closes spends its last hundred milliseconds as a soft filled disc, which is the one
        // stretch where the countdown has to be at its hardest.
        float collapse = Mathf.InverseLerp(ringFillRadius * 0.3f, 0f, ringRadius);
        plateMaterial.SetFloat(RingRadiusId, ringRadius);
        plateMaterial.SetFloat(
            RingThicknessId,
            Mathf.Max(RingBandWidth, CollapseThickness * collapse)
        );
        plateMaterial.SetFloat(RingAlphaId, 1f);

        // Over bare floor the cut is already deep. Over the plate it is eased back so the mark
        // keeps the plate's hue, and that easing is what turns the closing hoop into a smudge — so
        // it is walked off as the radius drops rather than held to the end.
        float close = Mathf.InverseLerp(cell * 1.5f, cell * 0.5f, ringRadius);
        plateMaterial.SetFloat(RingOnTintId, Mathf.Lerp(MarkOverTint, 1f, close));

        // Held off to the last few frames: the hoop has to still be dark material while it is
        // still travelling, or it reads as having vanished before the event it was counting to.
        plateMaterial.SetFloat(RingHotId, collapse * collapse * collapse);
    }

    /// <summary>
    /// The whole mark leaves on one beat, as the collapsed ring snapping out. Anything that
    /// lingers, and above all anything that shows the alarm colour again, warns about a danger
    /// that has already resolved.
    /// </summary>
    void UpdateCut(float cut)
    {
        if (plateMaterial == null)
            return;

        // Negative widths, not zero: at zero a hairline of border and keyline survives on the cell
        // edges themselves, which is exactly the residue this cut exists to remove.
        plateMaterial.SetFloat(TintStrengthId, 0f);
        plateMaterial.SetFloat(BorderWidthId, -1f);
        plateMaterial.SetFloat(KeyWidthId, -1f);
        plateMaterial.SetFloat(SeamAlphaId, 0f);
        plateMaterial.SetFloat(BullseyeWidthId, -1f);
        plateMaterial.SetFloat(RingRadiusId, 0f);
        plateMaterial.SetFloat(
            RingThicknessId,
            Mathf.Lerp(CollapseThickness, CollapseSnapThickness, cut)
        );
        plateMaterial.SetFloat(RingAlphaId, 1f - cut * cut);
        // Fully hot: nothing dark may outlive the event, so the exit beat is light, not material.
        plateMaterial.SetFloat(RingHotId, 1f);
        plateMaterial.SetFloat(RingHotFracId, 1f);
        plateMaterial.SetColor(
            RingColorId,
            AbilityJuice.Hot(RingCoreInk, Mathf.Lerp(RingCoreGain, 0.5f, cut))
        );
    }

    void DestroyMark()
    {
        if (plate != null)
        {
            Destroy(plate.gameObject);
            plate = null;
        }
        DestroyMaterial(ref plateMaterial);
    }

    // ---------------------------------------------------------------- caster

    void CollectCasterBody()
    {
        if (caster == null)
            return;

        List<BodyPart> parts = new();
        List<Renderer> renderers = new();
        for (int i = 0; i < caster.childCount; i++)
        {
            Transform child = caster.GetChild(i);
            if (child == null || !IsBodyPart(child))
                continue;

            parts.Add(
                new BodyPart
                {
                    Part = child,
                    BasePosition = child.localPosition,
                    BaseRotation = child.localRotation,
                    BaseScale = child.localScale,
                }
            );
            renderers.AddRange(child.GetComponentsInChildren<MeshRenderer>(true));
            renderers.AddRange(child.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }

        if (parts.Count == 0)
            return;

        bodyParts = parts.ToArray();
        bodyRenderers = renderers.ToArray();
        bodyProperties = new MaterialPropertyBlock();

        // The load multiplies the unit's own albedo, so the shared team material's tint has to be
        // read up front rather than replaced.
        bodyAlbedo = new Color[bodyRenderers.Length];
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            Material shared = bodyRenderers[i] != null ? bodyRenderers[i].sharedMaterial : null;
            bodyAlbedo[i] =
                shared != null && shared.HasProperty(BaseColorId)
                    ? shared.GetColor(BaseColorId)
                    : Color.white;
        }
    }

    /// <summary>
    /// The networked root's transform is off limits, so anticipation is driven on the model
    /// children only — and never on the health bar, vision cone or team pucks that share the root.
    /// </summary>
    static bool IsBodyPart(Transform child)
    {
        if (child.GetComponentInChildren<Canvas>(true) != null)
            return false;
        if (child.GetComponentInChildren<ParticleSystem>(true) != null)
            return false;
        if (child.GetComponentInChildren<LineRenderer>(true) != null)
            return false;
        if (child.gameObject.tag == "TeamIndicatorProp")
            return false;

        string childName = child.name.ToLowerInvariant();
        for (int i = 0; i < NonBodyNames.Length; i++)
        {
            if (childName.Contains(NonBodyNames[i]))
                return false;
        }

        return child.GetComponentInChildren<MeshRenderer>(true) != null
            || child.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
    }

    /// <summary>
    /// The caster loads across the whole wind-up on two clocks: a squared ease-in for the travel,
    /// so it is already moving in the opening frame and is at its fastest on the frame the ability
    /// fires, and a faster curve for the crouch, because a pose has to be legibly loaded long
    /// before a pull-back this slow has gone anywhere.
    /// </summary>
    void UpdateCasterLoad(float elapsed)
    {
        float progress = duration > 0.0001f ? Mathf.Clamp01(elapsed / duration) : 1f;
        float travel = progress * progress;
        float gather =
            GatherSnap * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GatherFirst, GatherSet, progress))
            + (1f - GatherSnap) * travel;
        float tremble =
            TrembleReach
            * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.3f, progress))
            * (0.35f + 0.65f * gather)
            * Mathf.Sin(elapsed * TrembleRate);

        ApplyPose(
            -PullBackCells * Mathf.Max(0.1f, GameLoop.cellSize) * travel + tremble,
            Mathf.Lerp(1f, LoadSpread, gather),
            Mathf.Lerp(1f, LoadFlatten, gather),
            -LoadLean * gather,
            -LoadSink * gather
        );

        float warm = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 1.6f));
        ApplyCasterInk(Mathf.Max(gather, warm * BodyWarmth), 0f);
        UpdateCharge(progress, elapsed);
    }

    void UpdateCasterRelease(float sinceFire)
    {
        float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(sinceFire / PeakSeconds), 3f);
        float settle = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.Clamp01((sinceFire - PeakSeconds - HoldSeconds) / SettleSeconds)
        );
        float cell = Mathf.Max(0.1f, GameLoop.cellSize);
        // One counter-bounce so the recovery lands on its feet rather than fading out.
        float bounce = Mathf.Sin(settle * Mathf.PI) * 0.07f;

        ApplyPose(
            Mathf.Lerp(-PullBackCells * cell, StrikeReachCells * cell, rise) * (1f - settle),
            Mathf.Lerp(Mathf.Lerp(LoadSpread, StrikeSpread, rise), 1f, settle),
            Mathf.Lerp(Mathf.Lerp(LoadFlatten, StrikeStretch, rise), 1f, settle) - bounce,
            Mathf.Lerp(Mathf.Lerp(-LoadLean, StrikeLean, rise), 0f, settle),
            Mathf.Lerp(Mathf.Lerp(-LoadSink, StrikeLift, rise), 0f, settle)
        );

        // The hot beat belongs to the release itself. It leaves with the mark; only the pose is
        // allowed to carry on into the frames after the ability has fired.
        float flash = Mathf.Clamp01(1f - sinceFire / ReleaseFlashSeconds);
        ApplyCasterInk(0f, Mathf.Min(rise * (1f - settle), flash));
    }

    void ApplyPose(float reach, float spread, float flatten, float leanDegrees, float lift)
    {
        // Kept even when there is no body to pose: the charge rides the same offset, so it has to
        // travel with the caster whether or not the model was resolvable.
        poseReach = reach;
        poseLift = lift;

        if (caster == null || bodyParts == null)
            return;

        Vector3 localOffset = caster.InverseTransformVector(aim * reach + Vector3.up * lift);
        Vector3 localAim = caster.InverseTransformDirection(aim);
        localAim.y = 0f;
        Vector3 leanAxis = Vector3.Cross(
            Vector3.up,
            localAim.sqrMagnitude > 0.0001f ? localAim.normalized : Vector3.forward
        );
        Quaternion lean = Quaternion.AngleAxis(leanDegrees, leanAxis);

        float verticalScale = Mathf.Max(0.2f, flatten);
        float spreadScale = Mathf.Max(0.2f, spread);

        for (int i = 0; i < bodyParts.Length; i++)
        {
            Transform part = bodyParts[i].Part;
            if (part == null)
                continue;
            part.localPosition = lean * bodyParts[i].BasePosition + localOffset;
            part.localRotation = lean * bodyParts[i].BaseRotation;
            Vector3 baseScale = bodyParts[i].BaseScale;
            part.localScale = new Vector3(
                baseScale.x * spreadScale,
                baseScale.y * verticalScale,
                baseScale.z * spreadScale
            );
        }
    }

    void ApplyCasterInk(float load, float lunge)
    {
        if (bodyRenderers == null || bodyProperties == null || bodyAlbedo == null)
            return;

        if (load <= 0.002f && lunge <= 0.002f)
        {
            if (!inkApplied)
                return;
            inkApplied = false;
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                if (bodyRenderers[i] != null)
                    bodyRenderers[i].SetPropertyBlock(null);
            }
            return;
        }

        inkApplied = true;
        Color factor = Color.Lerp(Color.white, LoadInk, Mathf.Clamp01(load));
        factor = Color.Lerp(factor, DriveInk, Mathf.Clamp01(lunge));

        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            Renderer bodyRenderer = bodyRenderers[i];
            if (bodyRenderer == null)
                continue;
            Color albedo = bodyAlbedo[i];
            Color tint = new(
                albedo.r * factor.r,
                albedo.g * factor.g,
                albedo.b * factor.b,
                albedo.a
            );
            bodyRenderer.GetPropertyBlock(bodyProperties);
            bodyProperties.SetColor(BaseColorId, tint);
            bodyRenderer.SetPropertyBlock(bodyProperties);
        }
    }

    void RestoreCasterBody()
    {
        if (bodyParts != null)
        {
            for (int i = 0; i < bodyParts.Length; i++)
            {
                Transform part = bodyParts[i].Part;
                if (part == null)
                    continue;
                part.localPosition = bodyParts[i].BasePosition;
                part.localRotation = bodyParts[i].BaseRotation;
                part.localScale = bodyParts[i].BaseScale;
            }
        }

        if (bodyRenderers != null)
        {
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                if (bodyRenderers[i] != null)
                    bodyRenderers[i].SetPropertyBlock(null);
            }
        }
        inkApplied = false;
    }

    // ---------------------------------------------------------------- charge

    /// <summary>
    /// Builds the charge the caster is loading: a small shaded orb standing off the muzzle and the
    /// gather of blades converging on it from behind the unit. The orb is a quarter of a cell so
    /// the caster keeps its outline, its rifle and its health bar; the charge's area lives in the
    /// gather, which arrives a blade at a time rather than switching on at full size.
    /// </summary>
    void BuildCharge()
    {
        if (caster == null)
            return;

        Shader charge = Shader.Find("BattlePlan/WindupCharge");
        if (charge == null)
        {
            Debug.LogWarning(
                "[AbilityWindup] BattlePlan/WindupCharge shader not found, skipping charge"
            );
            return;
        }

        ResolveMuzzle();

        chargeRoot = new GameObject("WindupCharge").transform;
        chargeRoot.SetParent(transform, false);

        orbMaterial = new Material(charge);
        orbMaterial.SetFloat(BladeModeId, 0f);
        orbMaterial.SetVector(CoreColorId, ChargeLit);
        orbMaterial.SetVector(EdgeColorId, ChargeHot);
        orbMaterial.SetVector(DeepColorId, ChargeDeep);
        orbMaterial.SetVector(GlintColorId, ChargeGlint);
        orbMaterial.SetVector(KeyColorId, ChargeKeyline);
        orbMaterial.SetFloat(KeyWidthId, OrbKeyWidth);
        // The quad is turned to face the target, so the lit side is its own local +x.
        orbMaterial.SetVector(LightDirId, new Vector4(1f, 0.3f, 0f, 0f));
        // Ordering the orb against the gather by distance would let a blade swap in front of it
        // whenever the caster leans; it is the one thing here allowed to cover another.
        orbMaterial.renderQueue = 3062;

        bladeMaterial = new Material(charge);
        bladeMaterial.SetFloat(BladeModeId, 1f);
        bladeMaterial.SetVector(CoreColorId, BladeLit);
        bladeMaterial.SetVector(EdgeColorId, BladeHot);
        bladeMaterial.SetVector(DeepColorId, BladeDeep);
        bladeMaterial.SetVector(GlintColorId, ChargeGlint);
        bladeMaterial.SetVector(KeyColorId, ChargeKeyline);
        bladeMaterial.SetFloat(KeyWidthId, BladeKeyWidth);
        bladeMaterial.renderQueue = 3052;

        orbQuad = BuildChargeQuad("ChargeOrb", orbMaterial);
        bladeQuads = new Transform[BladeCells.Length];
        bladeRenderers = new Renderer[BladeCells.Length];
        for (int i = 0; i < BladeCells.Length; i++)
        {
            bladeQuads[i] = BuildChargeQuad("ChargeBlade", bladeMaterial);
            bladeRenderers[i] = bladeQuads[i] != null
                ? bladeQuads[i].GetComponent<Renderer>()
                : null;
        }
        bladeProperties = new MaterialPropertyBlock();

        UpdateCharge(0f, 0f);
    }

    /// <summary>
    /// Finds where the charge belongs on this caster: just past the tip of whatever it holds out
    /// front, at that thing's own height. Measured rather than assumed, because the roster's models
    /// differ and an orb parked inside a rifle is an orb standing where the rifle used to be.
    /// </summary>
    void ResolveMuzzle()
    {
        muzzleForward = MuzzleForwardFallback;
        muzzleHeight = MuzzleHeightFallback;
        if (caster == null || bodyRenderers == null)
            return;

        Bounds body = default;
        bool measured = false;
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            // Hidden parts still report bounds, and a disabled prop parked at the model's origin
            // would drag the measurement off the unit entirely.
            if (!IsMeasurable(bodyRenderers[i]))
                continue;
            if (!measured)
            {
                body = bodyRenderers[i].bounds;
                measured = true;
                continue;
            }
            body.Encapsulate(bodyRenderers[i].bounds);
        }
        if (!measured)
            return;

        float footHeight = body.min.y - caster.position.y;
        float bodyHeight = Mathf.Max(0.4f, body.size.y);

        // The held weapon is whatever reaches furthest toward the target without being a foot.
        Bounds held = body;
        float heldReach = 0f;
        bool found = false;
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            if (!IsMeasurable(bodyRenderers[i]))
                continue;
            Bounds part = bodyRenderers[i].bounds;
            if (part.center.y - caster.position.y < footHeight + bodyHeight * 0.32f)
                continue;
            float reach = Vector3.Dot(part.center - caster.position, aim);
            if (found && reach <= heldReach)
                continue;
            heldReach = reach;
            held = part;
            found = true;
        }
        if (!found)
            return;

        float tip =
            heldReach
            + Mathf.Abs(held.extents.x * aim.x)
            + Mathf.Abs(held.extents.y * aim.y)
            + Mathf.Abs(held.extents.z * aim.z);
        muzzleForward = Mathf.Clamp(tip, 0.5f, 2.2f) + OrbClearance;
        muzzleHeight = Mathf.Clamp(
            held.center.y - caster.position.y,
            footHeight + bodyHeight * 0.45f,
            footHeight + bodyHeight * 0.8f
        );
    }

    static bool IsMeasurable(Renderer part)
    {
        return part != null && part.gameObject.activeInHierarchy && part.enabled;
    }

    Transform BuildChargeQuad(string quadName, Material material)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = quadName;
        Collider quadCollider = quad.GetComponent<Collider>();
        if (quadCollider != null)
            Destroy(quadCollider);

        quad.transform.SetParent(chargeRoot, false);
        Renderer quadRenderer = quad.GetComponent<Renderer>();
        // Shared, not instanced: thirteen blades read from one material and vary through a property
        // block, so assigning through `material` here would leak thirteen copies of it.
        quadRenderer.sharedMaterial = material;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        quadRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        return quad.transform;
    }

    /// <summary>
    /// Lights the orb and flies the gather in. The orb reaches only two thirds of its size by the
    /// time a strip's first panel is cut and the blades arrive on thirteen separate clocks, so the
    /// charge multiplies between panels instead of holding one silhouette at two scales — and no
    /// two frames of the wind-up carry the same arrangement.
    /// </summary>
    void UpdateCharge(float progress, float elapsed)
    {
        if (chargeRoot == null)
            return;

        float cell = Mathf.Max(0.1f, GameLoop.cellSize);
        chargeRoot.SetPositionAndRotation(GatherAnchor(), ChargeRotation());

        Vector2 aimScreen = AimOnScreen();
        float surge =
            1f + SurgeGain * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SurgeStart, 1f, progress));

        // The surge belongs to the gather. A quarter of a cell is the whole budget the orb has if
        // the caster is to stay the thing being looked at, and it does not get to overrun it.
        float ignite = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(OrbIgnite, OrbAlight, progress));
        float grow = Mathf.Pow(Mathf.InverseLerp(OrbAlight, OrbFull, progress), OrbGrowCurve);
        float orbSize = OrbCells * cell * ignite * (OrbSeed + (1f - OrbSeed) * grow);
        PlaceOrb(MuzzleAnchor(), aimScreen, orbSize, orbSize, elapsed);
        if (orbMaterial != null)
        {
            orbMaterial.SetFloat(FillId, Mathf.Clamp01(ignite * 2.4f));
            orbMaterial.SetFloat(GlintId, 0.35f + 0.65f * grow);
        }

        if (bladeQuads == null)
            return;

        float converge = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(GatherConvergeFirst, GatherConvergeLast, progress)
        );
        float ring = Mathf.Lerp(GatherFarCells, GatherNearCells, converge) * cell;
        float aimDegrees = Mathf.Atan2(aimScreen.y, aimScreen.x) * Mathf.Rad2Deg;
        Vector2 keyScreen = (aimScreen * 0.45f + Vector2.up * 0.89f).normalized;

        for (int i = 0; i < bladeQuads.Length; i++)
        {
            float flight = Mathf.InverseLerp(
                BladeArrive[i] - BladeFlightSpan,
                BladeArrive[i],
                progress
            );
            float dive = Mathf.SmoothStep(0f, 1f, flight);
            PlaceBlade(
                i,
                aimDegrees,
                keyScreen,
                ring + BladeRadiusCells[i] * cell + BladeEntryReachCells * cell * (1f - dive),
                BladeCells[i] * cell * surge * Mathf.Lerp(BladeEntryScale, 1f, dive),
                Mathf.Clamp01(flight * 2.2f),
                elapsed
            );
        }
    }

    /// <summary>
    /// The shot takes the charge with it: the orb stretches down the line of fire and the gather is
    /// eaten head-first, both gone on the plate's own beat. A wind-up cue that outlives the event it
    /// warned about is worse than no cue at all.
    /// </summary>
    void UpdateChargeExit(float cut)
    {
        if (chargeRoot == null)
            return;

        float cell = Mathf.Max(0.1f, GameLoop.cellSize);
        chargeRoot.SetPositionAndRotation(GatherAnchor(), ChargeRotation());

        Vector2 aimScreen = AimOnScreen();
        // The clock carries on from the wind-up: restarting it would snap every blade back to its
        // opening bend on the frame the ability fires.
        float clock = duration + CutSeconds * cut;
        float leaving = Mathf.InverseLerp(0.45f, 1f, cut);
        float fill = 1f - leaving * leaving;
        float full = OrbCells * cell;

        PlaceOrb(
            MuzzleAnchor() + aim * (DischargeTravelCells * cell * cut),
            aimScreen,
            full * Mathf.Lerp(1f, DischargeStretch, cut),
            full * Mathf.Lerp(1f, DischargeFlatten, cut),
            clock
        );
        if (orbMaterial != null)
        {
            orbMaterial.SetFloat(FillId, fill);
            orbMaterial.SetFloat(GlintId, 1f + cut);
        }

        if (bladeQuads == null)
            return;

        float ring = GatherNearCells * cell;
        float aimDegrees = Mathf.Atan2(aimScreen.y, aimScreen.x) * Mathf.Rad2Deg;
        Vector2 keyScreen = (aimScreen * 0.45f + Vector2.up * 0.89f).normalized;

        for (int i = 0; i < bladeQuads.Length; i++)
        {
            // Shortened from the tail with the head held on the ring. Collapsing the ring instead
            // would drag the whole gather across the caster it spent the wind-up standing behind.
            PlaceBlade(
                i,
                aimDegrees,
                keyScreen,
                ring + BladeRadiusCells[i] * cell,
                BladeCells[i] * cell * (1f + SurgeGain) * Mathf.Lerp(1f, DischargeEatenTo, cut),
                fill,
                clock
            );
        }
    }

    void PlaceOrb(Vector3 world, Vector2 aimScreen, float along, float across, float clock)
    {
        if (orbQuad == null || chargeRoot == null)
            return;

        Vector3 local = chargeRoot.InverseTransformPoint(world);
        // Pulled toward the camera so the orb stands off the barrel rather than inside it.
        orbQuad.localPosition = new Vector3(local.x, local.y, local.z - OrbDepthLift);
        orbQuad.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(aimScreen.y, aimScreen.x) * Mathf.Rad2Deg
        );
        orbQuad.localScale = new Vector3(along, across, 1f);

        // Drifts the silhouette wobble and the internal chords rather than spinning the quad, so
        // the interior circulates without the glint orbiting with it.
        if (orbMaterial != null)
            orbMaterial.SetFloat(SeedId, clock * 1.5f);
    }

    void PlaceBlade(
        int index,
        float aimDegrees,
        Vector2 keyScreen,
        float radius,
        float length,
        float fill,
        float clock
    )
    {
        Transform blade = bladeQuads[index];
        if (blade == null)
            return;

        if (fill <= 0.001f)
        {
            blade.localScale = Vector3.zero;
            return;
        }

        float degrees =
            aimDegrees
            + BladeAngleDegrees[index]
            + BladeDriftDegrees[index] * Mathf.Sin(clock * BladeDriftRate + index * 1.7f);
        float radians = degrees * Mathf.Deg2Rad;
        float centre = radius + length * 0.5f;
        blade.localPosition = new Vector3(
            Mathf.Cos(radians) * centre,
            Mathf.Sin(radians) * centre,
            BladeDepth[index]
        );
        // The head is the blade's local +x, so facing it back down its own radius is what makes the
        // gather read as falling into the orb rather than orbiting the caster.
        float facing = degrees + 180f;
        blade.localRotation = Quaternion.Euler(0f, 0f, facing);
        blade.localScale = new Vector3(length, length * BladeAspect, 1f);

        Renderer bladeRenderer = bladeRenderers != null ? bladeRenderers[index] : null;
        if (bladeRenderer == null || bladeProperties == null)
            return;

        // Lit mostly by the charge it is falling into, with a share of one board-wide key so the
        // gather keeps a consistent bright edge instead of every blade being lit down its own axis.
        float facingRadians = facing * Mathf.Deg2Rad;
        float cos = Mathf.Cos(facingRadians);
        float sin = Mathf.Sin(facingRadians);
        Vector4 key = new(
            0.58f + 0.42f * (cos * keyScreen.x + sin * keyScreen.y),
            0.42f * (cos * keyScreen.y - sin * keyScreen.x),
            0f,
            0f
        );

        bladeProperties.SetVector(LightDirId, key);
        bladeProperties.SetFloat(FillId, fill);
        bladeProperties.SetFloat(SeedId, index * 1.7f + clock * 0.6f);
        bladeRenderer.SetPropertyBlock(bladeProperties);
    }

    /// <summary>Where the target lies on screen, which is the side every lit face turns toward.</summary>
    Vector2 AimOnScreen()
    {
        Vector3 local = Quaternion.Inverse(ChargeRotation()) * aim;
        Vector2 flat = new(local.x, local.y);
        return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector2.right;
    }

    float SurgeStart =>
        1f - Mathf.Clamp(SurgeSeconds / Mathf.Max(duration, 0.0001f), 0.08f, 0.35f);

    Vector3 MuzzleAnchor()
    {
        if (caster == null)
            return transform.position;
        return caster.position
            + aim * (muzzleForward + poseReach)
            + Vector3.up * (muzzleHeight + poseLift);
    }

    Vector3 GatherAnchor()
    {
        if (caster == null)
            return transform.position;
        return caster.position
            + aim * (GatherForward + poseReach)
            + Vector3.up * (muzzleHeight + poseLift);
    }

    Quaternion ChargeRotation()
    {
        return chargeFacing != null ? chargeFacing.rotation : Quaternion.Euler(73f, 0f, 0f);
    }

    void DestroyCharge()
    {
        if (chargeRoot != null)
        {
            Destroy(chargeRoot.gameObject);
            chargeRoot = null;
        }
        orbQuad = null;
        bladeQuads = null;
        bladeRenderers = null;
        DestroyMaterial(ref orbMaterial);
        DestroyMaterial(ref bladeMaterial);
    }

    // ---------------------------------------------------------------- teardown

    /// <summary>
    /// Shader colours are handed over as raw values, so sRGB ink has to be converted here rather
    /// than relying on the inspector's conversion.
    /// </summary>
    static Color Srgb(float r, float g, float b)
    {
        Color authored = new(r, g, b, 1f);
        return QualitySettings.activeColorSpace == ColorSpace.Linear ? authored.linear : authored;
    }

    void OnDestroy()
    {
        if (!aborted)
            RestoreCasterBody();
        AbilityWindup.Forget(caster, this);

        DestroyMaterial(ref plateMaterial);
        DestroyMaterial(ref orbMaterial);
        DestroyMaterial(ref bladeMaterial);
    }

    static void DestroyMaterial(ref Material material)
    {
        if (material == null)
            return;
        Destroy(material);
        material = null;
    }
}
