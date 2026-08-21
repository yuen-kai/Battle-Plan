using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Which state is driving a <see cref="BuffAura"/>.
/// <para>
/// One aura is attached per unit, but a unit can be under more than one buff at a time -- Zealot
/// carries both <see cref="Overdrive"/>'s fire-rate window and <see cref="ConsecutiveHitTracker"/>'s
/// damage streak, and those two windows open and close independently. Each state therefore drives
/// its own slot rather than a single shared one: with one slot, whichever buff was applied second
/// would silently overwrite the first, and then switch the aura off entirely when its own (shorter)
/// window closed, taking a still-live buff's read down with it. Which slot ends up on screen is
/// decided by <c>BuffAura.ResolveStrongestSlot</c>.
/// </para>
/// </summary>
public enum BuffAuraSource
{
    /// <summary>A temporary fire-rate boost: <see cref="Overdrive"/>, <see cref="FullAuto"/>.</summary>
    FireRate,

    /// <summary>A consecutive-hit damage streak: <see cref="ConsecutiveHitTracker"/>.</summary>
    DamageStreak,
}

/// <summary>
/// Local-only "this unit is buffed right now" presentation: a ring of short energy risers that climb
/// the unit from its base plate and fade out around head height, more of them and faster the
/// stronger the buff is.
///
/// <para>Attached and driven the same way <see cref="StunPulse"/> is -- <see cref="Attach"/> on
/// first use, then <see cref="SetActive"/>/<see cref="HoldFor"/> whenever the state behind it
/// changes -- because a fire-rate buff is a state a unit is in for a window, not an event that
/// happens once. A one-shot burst at the moment of casting can only say "something was applied";
/// it cannot answer "is it still up?", which is the only question a player actually asks about a
/// buff.</para>
///
/// <para>Deliberately drawn <em>around</em> the silhouette rather than on it. Pogo's landing had to
/// have its dust plumes moved off the rider's chest for exactly this reason: the one thing that
/// makes an effect belong to a unit is that the unit is still legible inside it. So the risers stand
/// on a ring wider than the body (which measures about 0.64 across the arms) but inside the base
/// puck (1.755 across), they are hairline-thin, and they are additive with depth testing left on --
/// so the half of the ring behind the unit is occluded by its own body and the aura reads as
/// something the unit is standing in rather than a decal stamped over it.</para>
///
/// <para>Built from <see cref="LineRenderer"/>s on the existing <c>BattlePlan/EnergyBeam</c> shader,
/// the same route <see cref="BeamVFX"/> takes, rather than a particle system: six lines and one
/// material is cheap enough to leave running on every buffed unit for the whole buff window, which a
/// particle system with its own asset and emission budget is not.</para>
///
/// <para>Nothing here is networked. Call it from a <c>[ClientRpc]</c> (see
/// <see cref="Overdrive"/>/<see cref="FullAuto"/>) so every peer runs its own copy, and never gate
/// gameplay on it.</para>
/// </summary>
public sealed class BuffAura : MonoBehaviour
{
    public const string GameObjectName = "BuffAura";
    public const string ShaderName = "BattlePlan/EnergyBeam";

    /// <summary>
    /// How many risers the ring holds. Six divides evenly into opposing pairs (see
    /// <see cref="RingIndexForOrder"/>) and gives the streak's five steps enough distinct rungs to
    /// count, while staying few enough that they read as separate risers rather than a solid collar
    /// at the board camera's distance.
    /// </summary>
    private const int RiserCount = 6;

    /// <summary>
    /// Radius of the ring, in world units. Outside the body (the placeholder humanoid measures about
    /// 0.32 from centre to the outside of an arm) and inside the base puck's 0.88, so the risers
    /// flank the silhouette instead of crossing it, and never reach far enough out to be mistaken
    /// for Shield Rush's 2.15-across ground ring.
    /// </summary>
    private const float RingRadius = 0.58f;

    /// <summary>Length of the lit stretch of each riser, in world units.</summary>
    private const float SegmentLength = 0.42f;

    /// <summary>
    /// Share of the unit's own height the risers climb before fading out. Measured off the collider
    /// rather than assumed, because unit heights vary per character
    /// (<c>PlaceholderModelSpec.HeightScale</c>), and a riser that overshoots stops reading as
    /// energy coming off the body and starts reading as a beam leaving it.
    /// </summary>
    private const float ClimbBodyFraction = 0.86f;

    /// <summary>Fallback climb height for a unit with no collider to measure.</summary>
    private const float ClimbFallback = 1.55f;

    /// <summary>Clearance above the base plate the ring stands on, so it does not sink into it.</summary>
    private const float GroundOffset = 0.02f;

    // Climb rate at the weakest and strongest a buff can read. The speed carries as much of the
    // "faster" message as the brightness does, which is what makes this legible for a fire-rate buff
    // specifically rather than being a generic glow.
    private const float SlowClimbsPerSecond = 0.85f;
    private const float FastClimbsPerSecond = 1.95f;

    /// <summary>Drawn width of a riser, in world units. Two or three pixels at the board camera.</summary>
    private const float RiserWidth = 0.115f;

    /// <summary>
    /// How much a <see cref="Kick"/> adds to the drawn strength, and how fast that decays. A streak
    /// climbing one hit at a time moves the steady-state read by a fifth at most, which is not
    /// something a player watching the board notices; the kick is what turns each individual
    /// confirmed hit into a visible tick upward instead.
    /// </summary>
    private const float KickStrength = 0.3f;
    private const float KickDecayPerSecond = 5.5f;

    // Shares of a riser's climb spent fading in at the bottom and out at the top. Unequal on
    // purpose: energy leaving a body should arrive quickly and dissipate slowly, so a symmetric
    // fade reads as a bar sliding up and down rather than as something rising off the unit.
    private const float FadeInFraction = 0.18f;
    private const float FadeOutFraction = 0.4f;

    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    private static readonly int CoreWidthId = Shader.PropertyToID("_CoreWidth");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    /// <summary>
    /// One slot's worth of state. Read from <see cref="BuffAuraSource"/> rather than hard-coded so a
    /// third buff added to the enum later gets a slot without this having to be remembered.
    /// </summary>
    private static readonly int SlotCount = System.Enum.GetValues(typeof(BuffAuraSource)).Length;

    /// <summary>
    /// One buff's contribution to the aura. <see cref="ExpiresAt"/> is a local wall-clock deadline
    /// rather than a countdown so a slot needs exactly one write per buff, the same trade
    /// <see cref="StunState"/> makes with its own start-plus-duration pair.
    /// </summary>
    private struct Slot
    {
        public bool Active;
        public Color Tint;
        public float Strength;
        public float ExpiresAt;
        public float Kick;
    }

    private Slot[] slots;
    private Transform rig;
    private LineRenderer[] risers;
    private Vector3[] ringOffsets;
    private Material auraMaterial;
    private float climbTop = ClimbFallback;
    private float ringSeed;
    private bool rigUnavailable;

    /// <summary>Whether any buff is currently holding the aura up.</summary>
    public bool IsActive => ResolveStrongestSlot() >= 0;

    /// <summary>
    /// Attaches the aura to <paramref name="unit"/>, or hands back the one already on it. Mirrors
    /// <see cref="StunPulse.Attach"/> exactly, including being safe to call every time a buff lands
    /// rather than needing a cached reference: a caller that holds one across the unit's death would
    /// otherwise have to distinguish "no aura yet" from "aura destroyed with its unit", and Unity's
    /// destroyed-object null already answers that for free.
    /// </summary>
    public static BuffAura Attach(GameObject unit)
    {
        if (unit == null)
            return null;
        if (!unit.TryGetComponent(out BuffAura aura))
            aura = unit.AddComponent<BuffAura>();
        return aura;
    }

    /// <summary>
    /// Turns <paramref name="source"/>'s contribution on or off for as long as the caller says so,
    /// with no deadline of its own. For a buff whose end is an event the caller will observe --
    /// Zealot's streak ends on a reload, not on a timer.
    /// </summary>
    /// <param name="strength">
    /// How loudly this buff should read, 0 to 1. Drives the number of lit risers, the climb rate and
    /// the brightness together, so an escalating buff escalates on every axis at once instead of
    /// only getting brighter.
    /// </param>
    public void SetActive(BuffAuraSource source, bool on, Color color, float strength = 1f)
    {
        Set(source, on, color, strength, Mathf.Infinity);
    }

    /// <summary>
    /// Holds <paramref name="source"/>'s contribution up for <paramref name="seconds"/> and then
    /// drops it on its own.
    /// <para>
    /// The timing lives here rather than in the calling ability on purpose. An <see cref="Ability"/>
    /// disables itself on every peer but the server (see <see cref="Ability.OnNetworkSpawn"/>), so an
    /// ability that started a coroutine from inside its own <c>[ClientRpc]</c> to clear the aura
    /// later would be running it on a component the peer has deliberately switched off. The aura is
    /// an always-enabled component on the unit itself, so it can simply expire itself.
    /// </para>
    /// <para>
    /// Timed off local <see cref="Time.time"/> from the frame the RPC arrived rather than off
    /// <c>NetworkManager.ServerTime</c>: the boost it is reporting is itself held against the
    /// server's own <c>Time.time</c> (see <see cref="Shooting.TryApplyTemporaryFireRateBoost"/>), so
    /// there is no server timestamp to align to that would be more faithful than this.
    /// </para>
    /// </summary>
    public void HoldFor(BuffAuraSource source, Color color, float seconds, float strength = 1f)
    {
        if (seconds <= 0f)
        {
            Set(source, false, color, strength, Mathf.Infinity);
            return;
        }

        Set(source, true, color, strength, Time.time + seconds);
    }

    /// <summary>
    /// A short brightness spike on top of <paramref name="source"/>'s steady read, for a buff that
    /// advances in discrete steps. Ignored for a slot that is not currently up, so a stale kick from
    /// a buff that has since expired cannot flash an aura back on for a frame.
    /// </summary>
    public void Kick(BuffAuraSource source)
    {
        EnsureSlots();
        int index = (int)source;
        if (index < 0 || index >= slots.Length || !slots[index].Active)
            return;

        slots[index].Kick = 1f;
    }

    private void Set(
        BuffAuraSource source,
        bool active,
        Color color,
        float strength,
        float expiresAt
    )
    {
        EnsureSlots();
        int index = (int)source;
        if (index < 0 || index >= slots.Length)
            return;

        if (!active)
        {
            slots[index] = default;
            Draw();
            return;
        }

        bool wasActive = slots[index].Active;
        slots[index] = new Slot
        {
            Active = true,
            Tint = color,
            Strength = Mathf.Clamp01(strength),
            ExpiresAt = expiresAt,
            // A slot switching on opens at the top of a kick rather than easing in: the frame a buff
            // lands is the frame it has to be visible, the same reason StunPulse opens at the top of
            // its pulse instead of the bottom. A slot that was already up keeps whatever kick it had.
            Kick = wasActive ? slots[index].Kick : 1f,
        };

        EnsureRig();
        Draw();
    }

    /// <summary>
    /// Drawn in <see cref="LateUpdate"/> rather than <see cref="Update"/>, matching
    /// <see cref="AbilityStatusRing"/>: the risers are placed relative to a unit that moves during
    /// execution, and a ring positioned before that move spends the whole move a frame behind it.
    /// </summary>
    private void LateUpdate()
    {
        if (slots == null)
            return;

        float step = Time.deltaTime;
        for (int index = 0; index < slots.Length; index++)
        {
            if (!slots[index].Active)
                continue;

            if (Time.time >= slots[index].ExpiresAt)
            {
                slots[index] = default;
                continue;
            }

            slots[index].Kick = Mathf.MoveTowards(
                slots[index].Kick,
                0f,
                KickDecayPerSecond * step
            );
        }

        Draw();
    }

    private void OnDisable()
    {
        // A unit going inactive (respawn pooling, fog teardown) must not come back still wearing a
        // buff it no longer has. Every slot is re-driven by its own state on the next change.
        if (slots != null)
            System.Array.Clear(slots, 0, slots.Length);
        if (rig != null)
            rig.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (auraMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(auraMaterial);
        else
            DestroyImmediate(auraMaterial);
        auraMaterial = null;
    }

    private void EnsureSlots()
    {
        slots ??= new Slot[Mathf.Max(1, SlotCount)];
    }

    /// <summary>
    /// The one slot that gets drawn: the loudest one currently up.
    /// <para>
    /// Blending two live buffs into one aura was the other option and is worse. Averaging their
    /// colours produces a third colour that belongs to neither state -- a team-blue fire-rate buff
    /// mixed with an amber streak lands on a muddy grey that names nothing -- and stacking two rings
    /// at different radii puts a second collar around a silhouette this effect already has to work
    /// hard not to bury. A status read shows the most urgent thing that is true, the way the HUD
    /// does, and hands the ring straight back to the next-loudest buff the moment the top one
    /// expires (rather than losing it, which a single-slot aura would).
    /// </para>
    /// </summary>
    private int ResolveStrongestSlot()
    {
        if (slots == null)
            return -1;

        int strongest = -1;
        float loudest = -1f;
        for (int index = 0; index < slots.Length; index++)
        {
            if (!slots[index].Active)
                continue;

            float loudness = DrawnStrength(slots[index]);
            if (loudness <= loudest)
                continue;

            loudest = loudness;
            strongest = index;
        }

        return strongest;
    }

    private static float DrawnStrength(Slot slot)
    {
        return Mathf.Clamp01(slot.Strength + slot.Kick * KickStrength);
    }

    private void Draw()
    {
        int strongest = ResolveStrongestSlot();
        if (strongest < 0)
        {
            if (rig != null && rig.gameObject.activeSelf)
                rig.gameObject.SetActive(false);
            return;
        }

        EnsureRig();
        if (risers == null)
            return;

        if (!rig.gameObject.activeSelf)
            rig.gameObject.SetActive(true);

        Slot slot = slots[strongest];
        float strength = DrawnStrength(slot);
        PushMaterial(slot.Tint, strength);

        // At least one riser even at the bottom of the range: a buff that is up has to be visible at
        // all before it is legible as weak or strong.
        int lit = Mathf.Clamp(Mathf.CeilToInt(strength * RiserCount), 1, RiserCount);
        float climbsPerSecond = Mathf.Lerp(SlowClimbsPerSecond, FastClimbsPerSecond, strength);
        float basePhase = Mathf.Repeat(Time.time * climbsPerSecond + ringSeed, 1f);
        float travel = Mathf.Max(0.05f, climbTop - SegmentLength);
        float peakAlpha = Mathf.Lerp(0.55f, 1f, strength);

        for (int order = 0; order < risers.Length; order++)
        {
            LineRenderer riser = risers[order];
            if (riser == null)
                continue;

            if (order >= lit)
            {
                riser.enabled = false;
                continue;
            }

            riser.enabled = true;

            // Staggered across the lit risers rather than across all six, so the ring keeps an even
            // beat as it fills instead of leaving a silent gap where the unlit ones would have been.
            float phase = Mathf.Repeat(basePhase + (float)order / lit, 1f);
            float bottom = phase * travel;
            Vector3 offset = ringOffsets[order];

            riser.SetPosition(0, offset + Vector3.up * bottom);
            riser.SetPosition(1, offset + Vector3.up * (bottom + SegmentLength));

            float fade =
                Mathf.Clamp01(phase / FadeInFraction)
                * Mathf.Clamp01((1f - phase) / FadeOutFraction);
            float alpha = fade * peakAlpha;

            // The head of the riser is the bright end and the tail trails it: position 0 is the
            // bottom of the segment, so the gradient runs dim-to-hot upward and the thing reads as
            // rising rather than as a lit bar of fixed length sliding up the unit.
            riser.startColor = new Color(1f, 1f, 1f, alpha * 0.35f);
            riser.endColor = new Color(1f, 1f, 1f, alpha);
        }
    }

    private void PushMaterial(Color color, float strength)
    {
        if (auraMaterial == null)
            return;

        // Pushed into HDR through AbilityJuice.Hot so the risers bloom rather than merely being
        // bright, and pushed further as the buff strengthens -- which is what makes a capped streak
        // read as white-hot instead of just as one more riser than a four-hit streak.
        auraMaterial.SetColor(GlowColorId, AbilityJuice.Hot(color, Mathf.Lerp(1.1f, 2.4f, strength)));
        auraMaterial.SetColor(
            CoreColorId,
            AbilityJuice.Hot(AbilityJuice.HotCore, Mathf.Lerp(0.3f, 0.75f, strength))
        );
        auraMaterial.SetFloat(IntensityId, Mathf.Lerp(0.6f, 1.15f, strength));
    }

    private void EnsureRig()
    {
        if (risers != null || rigUnavailable)
            return;

        Shader auraShader = Shader.Find(ShaderName);
        if (auraShader == null)
        {
            // Costs the unit its aura and nothing else, the same failure AbilityStatusRing takes
            // when its own shader is missing.
            Debug.LogWarning($"[BuffAura] {ShaderName} shader not found; buff aura disabled.");
            rigUnavailable = true;
            return;
        }

        auraMaterial = new Material(auraShader)
        {
            name = "Buff Aura (Runtime)",
            hideFlags = HideFlags.DontSave,
        };
        // A short riser wants a tighter core and a coarser shimmer than a beam spanning the board:
        // the shader's own defaults are scaled for an Area Lock laser, whose UV runs many cells.
        auraMaterial.SetFloat(CoreWidthId, 0.3f);
        auraMaterial.SetFloat(EdgeSoftnessId, 0.55f);
        auraMaterial.SetFloat(ScrollSpeedId, 5f);
        auraMaterial.SetFloat(NoiseScaleId, 3.5f);
        auraMaterial.SetFloat(NoiseStrengthId, 0.4f);

        GameObject rigObject = new(GameObjectName) { layer = gameObject.layer };
        rigObject.transform.SetParent(transform, worldPositionStays: false);
        rig = rigObject.transform;
        PlaceRig();

        // A per-unit phase offset so two buffed units standing side by side do not strobe in
        // lockstep, which reads as one effect spanning both of them rather than one each.
        ringSeed = Random.value;

        risers = new LineRenderer[RiserCount];
        ringOffsets = new Vector3[RiserCount];
        bool forceRenderingOff = ResolveForceRenderingOff();

        for (int order = 0; order < RiserCount; order++)
        {
            int ringIndex = RingIndexForOrder(order);
            float angle = ringIndex / (float)RiserCount * Mathf.PI * 2f;
            ringOffsets[order] = new Vector3(
                Mathf.Cos(angle) * RingRadius,
                0f,
                Mathf.Sin(angle) * RingRadius
            );
            risers[order] = CreateRiser(order, forceRenderingOff);
        }
    }

    /// <summary>
    /// Which position on the ring the <paramref name="order"/>th lit riser takes. A partly-lit ring
    /// that simply lit positions 0..n would bunch every riser onto one side of the unit and read as
    /// a lopsided smear rather than a field the unit is standing in, so the order alternates sides:
    /// for six positions it walks 0, 3, 1, 4, 2, 5.
    /// </summary>
    private static int RingIndexForOrder(int order)
    {
        int half = RiserCount / 2;
        return order % 2 == 0 ? order / 2 : half + order / 2;
    }

    private LineRenderer CreateRiser(int order, bool forceRenderingOff)
    {
        GameObject riserObject = new($"Riser{order + 1}") { layer = rig.gameObject.layer };
        riserObject.transform.SetParent(rig, worldPositionStays: false);

        LineRenderer riser = riserObject.AddComponent<LineRenderer>();
        riser.sharedMaterial = auraMaterial;
        riser.useWorldSpace = false;
        riser.positionCount = 2;
        riser.widthMultiplier = RiserWidth;
        riser.numCapVertices = 0;
        riser.numCornerVertices = 0;
        riser.textureMode = LineTextureMode.Stretch;
        // Billboarded, like every other line in this project's VFX layer: a riser this thin edge-on
        // to the board camera would vanish for whichever units happen to be standing the wrong way.
        riser.alignment = LineAlignment.View;
        riser.shadowCastingMode = ShadowCastingMode.Off;
        riser.receiveShadows = false;
        riser.lightProbeUsage = LightProbeUsage.Off;
        riser.reflectionProbeUsage = ReflectionProbeUsage.Off;
        // Host fog cannot NetworkHide server-owned objects, so GameLoop suppresses their renderers
        // locally instead. Inherit that state at construction or an aura built for a unit currently
        // hidden would light up an enemy nobody is allowed to see. Later visibility changes are
        // covered for free: GameLoop.SetUnitVisualsLocal sweeps GetComponentsInChildren<Renderer>
        // on the unit, and these are children of it.
        riser.forceRenderingOff = forceRenderingOff;
        return riser;
    }

    private bool ResolveForceRenderingOff()
    {
        foreach (Renderer unitRenderer in GetComponentsInChildren<Renderer>(true))
        {
            if (unitRenderer.forceRenderingOff)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Stands the ring on the top of the unit's base plate and cancels the unit's own scale, so the
    /// aura is the same world size on every prefab variant. A unit does not stand on the board, it
    /// stands on its own puck, so a ring placed on the board plane starts inside the very plate the
    /// unit is standing on -- <see cref="UnitBasePlate"/> exists for exactly this.
    /// </summary>
    private void PlaceRig()
    {
        Vector3 unitScale = transform.lossyScale;
        float scaleX = Mathf.Max(Mathf.Abs(unitScale.x), 0.001f);
        float scaleY = Mathf.Max(Mathf.Abs(unitScale.y), 0.001f);
        float scaleZ = Mathf.Max(Mathf.Abs(unitScale.z), 0.001f);

        rig.localPosition = new Vector3(
            0f,
            UnitBasePlate.ClearanceAboveOrigin(transform, GroundOffset) / scaleY,
            0f
        );
        rig.localRotation = Quaternion.identity;
        rig.localScale = new Vector3(1f / scaleX, 1f / scaleY, 1f / scaleZ);

        // Measured off the collider so a shorter character's aura stays on its body instead of
        // standing over its head. The rig sits at the plate, whose underside is the collider's own
        // bottom, so the collider's full height is the climb available above it.
        Collider unitCollider = GetComponent<Collider>();
        float bodyHeight = unitCollider != null ? unitCollider.bounds.size.y : 0f;
        climbTop =
            bodyHeight > 0.01f ? bodyHeight * ClimbBodyFraction : ClimbFallback;
    }
}
