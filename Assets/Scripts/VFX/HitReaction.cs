using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The victim's half of a hit: what the thing being hit does about it.
/// <para>
/// An explosion that leaves its targets standing perfectly still did not happen. A hit has to move
/// the body — shoved away from the source, compressed by the force, and recovering with a wobble —
/// and it has to be legible as damage rather than as movement the player ordered.
/// </para>
/// <para>
/// Purely local and visual: this displaces the visual body, never the unit's authoritative
/// position. Call on every peer when damage is applied.
/// </para>
/// </summary>
public static class HitReaction
{
    /// <summary>
    /// Plays the reaction for <paramref name="victim"/> being hit from
    /// <paramref name="sourcePosition"/>.
    /// </summary>
    /// <param name="victim">Unit root that took the hit.</param>
    /// <param name="sourcePosition">Where the hit came from, for knockback direction.</param>
    /// <param name="severity">
    /// Damage as a fraction of the victim's maximum health, clamped to a sane range by the
    /// implementation. 1 is a hit that nearly kills.
    /// </param>
    public static void Play(GameObject victim, Vector3 sourcePosition, float severity = 0.4f)
    {
        if (victim == null)
            return;

        float force = HitBody.Force(severity);
        HitFlash.FlashDamage(victim, force);

        HitReactionDriver driver = HitBody.DriverFor(victim);
        if (driver != null)
            driver.Strike(sourcePosition, force);
    }

    /// <summary>
    /// Plays the death reaction: the body goes down rather than out.
    /// <para>
    /// A rider landing throws the deck outward from a point of contact; a death has to be the
    /// opposite read, so nothing here expands and nothing here travels. The body buckles straight
    /// down over five captured frames, is crushed to under half its height, and is held with most
    /// of it already inside the deck while the hole the caller opens burns underneath it. That
    /// held frame is the whole point: an impact panel has to contain a body going down, not an
    /// empty tile where one used to be.
    /// </para>
    /// <para>
    /// The networked object is deactivated within the frame it dies, so the fall is carried by a
    /// standalone copy of the model that owns nothing on the network and outlives it.
    /// </para>
    /// </summary>
    public static void PlayDeath(GameObject victim, Vector3 sourcePosition, Color teamColor)
    {
        if (victim == null)
            return;

        HitReactionDriver driver = HitBody.DriverFor(victim);
        if (driver != null)
        {
            driver.Fall(sourcePosition, teamColor);
            return;
        }

        // Already deactivated by the death it is reacting to. The body still has to leave, and the
        // puppet owns nothing on the unit, so it can be dropped from here on its own.
        HitDeathPuppet.SpawnFor(victim.transform, sourcePosition, teamColor);
    }
}

/// <summary>
/// Where a unit's model actually lives, and how far off the ground it stands.
/// <para>
/// The networked root's position and scale are replicated, so nothing here may write to them. Every
/// deformation runs on the model children instead — and never on the base puck, the vision cone or
/// any of the board overlays that share the same root.
/// </para>
/// </summary>
static class HitBody
{
    /// <summary>Root children that carry board UI or overlays rather than the model.</summary>
    static readonly string[] NonBodyNames =
    {
        "canvas", "bar", "alert", "vision", "cone", "puck", "overlay", "indicator",
        "laser", "path", "glow", "ring", "shadow", "marker", "range", "fog",
    };

    /// <summary>Damage fraction, clamped to the range the reaction envelopes were tuned against.</summary>
    internal static float Force(float severity) => Mathf.Clamp(severity, 0.05f, 1.15f);

    internal static bool IsBodyPart(Transform child)
    {
        if (child == null)
            return false;
        // Deep for UI, shallow for effects: a canvas anywhere under a child means the child is the
        // health bar, but a model that happens to carry a muzzle flash is still the model, and
        // dropping it would leave the unit with no body to react with at all.
        if (child.GetComponentInChildren<Canvas>(true) != null)
            return false;
        if (child.GetComponent<ParticleSystem>() != null || child.GetComponent<LineRenderer>() != null)
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
    /// Fills <paramref name="parts"/> with the model children and <paramref name="renderers"/> with
    /// the character surfaces inside them. Effect renderers the unit is carrying are left out: they
    /// drive their own property blocks on <c>BattlePlan/*</c> shaders and a tint would take their
    /// state with it.
    /// </summary>
    internal static void Collect(Transform root, List<Transform> parts, List<Renderer> renderers)
    {
        parts.Clear();
        renderers.Clear();
        if (root == null)
            return;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (!IsBodyPart(child))
                continue;

            parts.Add(child);
            foreach (Renderer candidate in child.GetComponentsInChildren<Renderer>(true))
            {
                if (
                    (candidate is MeshRenderer || candidate is SkinnedMeshRenderer)
                    && DiveRecoveryPulse.IsCharacterRenderer(candidate)
                )
                {
                    renderers.Add(candidate);
                }
            }
        }
    }

    /// <summary>The world box the character's surfaces occupy right now.</summary>
    internal static bool TryBounds(IReadOnlyList<Renderer> renderers, out Bounds bounds)
    {
        bounds = default;
        if (renderers == null)
            return false;

        bool any = false;
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer bodyRenderer = renderers[i];
            if (bodyRenderer == null)
                continue;
            if (any)
            {
                bounds.Encapsulate(bodyRenderer.bounds);
            }
            else
            {
                bounds = bodyRenderer.bounds;
                any = true;
            }
        }

        return any && bounds.extents.sqrMagnitude > 0.0001f;
    }

    /// <summary>
    /// Where the body's mass sits and where it meets the ground, so a contact mass lands on the
    /// body and a topple turns about the feet. Falls back to a typical unit when nothing measurable
    /// is present. Both heights are offsets from the networked root, which sits at the middle of a
    /// capsule taller than the model and so is usually above the unit's head.
    /// </summary>
    internal static void Measure(
        Transform root,
        IReadOnlyList<Renderer> renderers,
        out float centreHeight,
        out float footHeight
    )
    {
        centreHeight = 0.85f;
        footHeight = -0.2f;
        if (root == null || !TryBounds(renderers, out Bounds bounds))
            return;

        centreHeight = Mathf.Clamp(bounds.center.y - root.position.y, -2f, 3f);
        footHeight = Mathf.Clamp(bounds.min.y - root.position.y, -3f, 2f);
    }

    /// <summary>The horizontal direction a hit from <paramref name="sourcePosition"/> pushes toward.</summary>
    internal static Vector3 AwayFrom(Transform victim, Vector3 sourcePosition)
    {
        if (victim == null)
            return Vector3.right;

        Vector3 away = victim.position - sourcePosition;
        away.y = 0f;
        if (away.sqrMagnitude > 0.0004f)
            return away.normalized;

        Vector3 behind = -victim.forward;
        behind.y = 0f;
        return behind.sqrMagnitude > 0.0004f ? behind.normalized : Vector3.right;
    }

    internal static HitReactionDriver DriverFor(GameObject victim)
    {
        if (victim == null || !victim.activeInHierarchy)
            return null;
        if (!victim.TryGetComponent(out HitReactionDriver driver))
            driver = victim.AddComponent<HitReactionDriver>();
        return driver;
    }

    /// <summary>The camera a card has to face; null when there is no board to film.</summary>
    internal static Camera BoardCamera()
    {
        Camera board = GameLoop.Instance != null ? GameLoop.Instance.TeamCamera : null;
        return board != null ? board : Camera.main;
    }
}

/// <summary>
/// Drives one unit's model children through a hit and puts them back exactly where it found them.
/// <para>
/// A hit resolves as five overlapping layers on five clocks: the body is shoved back, it is crushed
/// along the axis the force came down, it leans away, it is turned, and it comes back off the
/// ground. The turn outlasting the shove is what leaves a frame taken well after the hit still
/// reading as recovering rather than as already fine.
/// </para>
/// <para>
/// All five are deliberately tiny. Being shot is the most common thing that happens on this board,
/// and an event that frequent has to be readable at a glance without being watchable — the layers
/// exist to make the hit land on the right frame and in the right direction, not to be a
/// performance the player waits out before the next one arrives.
/// </para>
/// <para>
/// Everything is shaped for the board camera, which looks down at 73 degrees. At that pitch the
/// ground plane is very nearly the screen plane: a horizontal displacement is worth its full length
/// on screen and a vertical one is worth less than a third of it. So the crush happens in the
/// ground plane — narrower along the impact axis, wider across it — and the lean stays small,
/// because a lean is a tall body converting its height into sideways screen travel, which widens
/// the silhouette exactly when it is supposed to be narrowing.
/// </para>
/// </summary>
[DisallowMultipleComponent]
class HitReactionDriver : MonoBehaviour
{
    // Out fast, held for a beat, then a single cosine spring home. The layers differ in how long
    // each stage takes, which keeps them one event without letting them travel as one object.
    //
    // The whole reaction is finished inside a third of a second. Damage lands on the same body
    // several times in a round, and a recovery still running when the next bullet arrives merges
    // with it into one continuous wobble — at which point the unit is not reacting to hits, it is
    // just permanently unsteady.
    const float ShoveSnap = 0.04f;
    const float ShoveHold = 0.018f;
    const float ShoveSettle = 0.12f;
    const float ShoveShape = 0.85f;

    const float CrushSnap = 0.028f;
    const float CrushHold = 0.016f;
    const float CrushSettle = 0.095f;
    const float CrushShape = 1.7f;

    const float LeanSnap = 0.05f;
    const float LeanHold = 0.014f;
    const float LeanSettle = 0.14f;
    const float LeanShape = 1f;

    const float TurnSnap = 0.06f;
    const float TurnSettle = 0.28f;
    const float TurnShape = 1.15f;

    const float LiftSnap = 0.04f;
    const float LiftHold = 0.01f;
    const float LiftSettle = 0.1f;
    const float LiftShape = 2.4f;

    // Peaks at force 1, in world units against a 2.7-unit cell and a body a little under a unit
    // wide. Routine rifle fire is ten damage into a hundred and twenty, so the force nearly every
    // hit on this board arrives with is 0.08 — which means the constant term, not the gain, was
    // setting the size of almost every reaction, and it was setting it at a tenth of a cell of
    // travel and ten degrees of turn per bullet. The base is now close to nothing and the ramp
    // does the work: a rifle round nudges the body a fiftieth of a cell and turns it under two
    // degrees, a sniper round moves it a sixteenth, and only something near lethal displaces it
    // far enough to read on a still frame.
    const float ShoveBase = 0.022f;
    const float ShoveGain = 0.34f;
    const float LiftBase = 0.006f;
    const float LiftGain = 0.07f;
    const float LeanBase = 0.7f;
    const float LeanGain = 3.4f;
    const float TurnBase = 1.1f;
    const float TurnGain = 6.5f;

    // The crush roughly holds its ground-plane footprint while changing its shape: a body that only
    // shrinks reads as retreating into the distance, not as being squeezed. A near-lethal hit ends
    // about 0.93 as wide along the impact axis and 1.12 as deep across it; a rifle round stays
    // inside two percent of its own silhouette, which is a body registering a hit rather than a
    // body being deformed by one.
    const float CompressBase = 0.008f;
    const float CompressGain = 0.055f;
    const float SpreadBase = 0.013f;
    const float SpreadGain = 0.092f;
    const float SinkBase = 0.004f;
    const float SinkGain = 0.028f;

    /// <summary>
    /// How much of the body's travel the health canvas inherits. Rigidly parented it would sell the
    /// bar as part of the model; left behind entirely it reads as a desynced piece of UI floating
    /// half a cell off the unit it belongs to.
    /// </summary>
    const float CanvasFollow = 0.68f;

    const float CollapseHoldSeconds = 0.62f;

    struct BodyPart
    {
        public Transform Part;
        public Vector3 BasePosition;
        public Quaternion BaseRotation;
        public Vector3 BaseScale;
    }

    BodyPart[] parts;
    Renderer[] bodyRenderers;
    Transform canvas;
    Vector3 canvasBase;
    bool posed;
    float centreHeight = 0.85f;
    float footHeight = -0.2f;
    float turnSign = 1f;

    // Root-local, on the unit's own vertical axis. The networked root sits at the middle of a
    // capsule taller than the model, which puts its origin above the unit's head: a lean hung from
    // there sweeps the feet sideways instead of tipping the shoulders, and a squash centred there
    // pulls the body off the floor.
    Vector3 bodyPivotLocal = new(0f, -1f, 0f);
    Vector3 bodyCentreLocal;

    Vector3 shoveDirection = Vector3.forward;
    Vector3 visualOffset;
    HitStampFlash contact;
    float contactInset;
    float force;
    float elapsed;
    float lifetime;
    bool collapsing;
    bool running;

    internal void Strike(Vector3 sourcePosition, float hitForce)
    {
        if (!EnsureBody())
            return;

        shoveDirection = HitBody.AwayFrom(transform, sourcePosition);

        // A second hit landing mid-recovery takes over the timeline but inherits whatever weight the
        // first one had left, so a chip hit cannot cancel the reaction to a grenade.
        if (running && !collapsing)
        {
            float left = 1f - Mathf.Clamp01(elapsed / Mathf.Max(0.01f, lifetime));
            hitForce = Mathf.Max(hitForce, force * left);
        }

        force = HitBody.Force(hitForce);
        lifetime = TurnSnap + TurnSettle;

        collapsing = false;
        visualOffset = Vector3.zero;
        elapsed = 0f;
        running = true;
        enabled = true;

        contactInset = HitStampFlash.ContactInset(force);
        // The ember rides the blow rather than burning at full radius on every plink: the hot
        // scale is the ember's own size inside the lump, so a rifle round strikes off a spark and
        // only a heavy hit leaves a coal in it.
        contact = HitStampFlash.SpawnContact(
            ContactPoint(),
            -shoveDirection,
            force,
            0.12f + 0.6f * force
        );
    }

    internal void Fall(Vector3 sourcePosition, Color teamColor)
    {
        if (!EnsureBody())
            return;

        shoveDirection = HitBody.AwayFrom(transform, sourcePosition);
        force = 1.15f;
        visualOffset = Vector3.zero;

        HitDeathPuppet.Spawn(
            transform,
            bodyRenderers,
            shoveDirection,
            teamColor,
            centreHeight,
            footHeight,
            turnSign
        );

        // The puppet is standing in the same place wearing the same pose, so the original has to be
        // gone this frame rather than shrinking out over several: the server keeps the networked
        // object alive one frame longer than the clients do, and two bodies is worse than none.
        collapsing = true;
        elapsed = 0f;
        lifetime = CollapseHoldSeconds;
        running = true;
        enabled = true;
        ApplyPose(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

        // No contact mass. A card planted at the body's chest height covered the whole collapse in
        // the frame it fired, which is what turned a death into a puff of smoke appearing on an
        // empty tile. Spall belongs to a hit that was survived.
        contact = null;
    }

    void LateUpdate()
    {
        // After the animation pass, so a rig that carries root motion cannot argue with the pose.
        if (!running)
        {
            enabled = false;
            return;
        }

        elapsed += Time.deltaTime;
        if (elapsed >= lifetime)
        {
            Restore();
            return;
        }

        if (collapsing)
        {
            ApplyPose(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            return;
        }

        float shove = Envelope(elapsed, ShoveSnap, ShoveHold, ShoveSettle, ShoveShape);
        float crush = Envelope(elapsed, CrushSnap, CrushHold, CrushSettle, CrushShape);
        float lean = Envelope(elapsed, LeanSnap, LeanHold, LeanSettle, LeanShape);
        float turn = Envelope(elapsed, TurnSnap, 0f, TurnSettle, TurnShape);
        float lift = Envelope(elapsed, LiftSnap, LiftHold, LiftSettle, LiftShape);

        ApplyPose(
            (ShoveBase + ShoveGain * force) * shove,
            (LiftBase + LiftGain * force) * lift,
            (LeanBase + LeanGain * force) * lean,
            (TurnBase + TurnGain * force) * turn * turnSign,
            (CompressBase + CompressGain * force) * crush,
            (SpreadBase + SpreadGain * force) * crush,
            (SinkBase + SinkGain * force) * crush,
            1f
        );

        // Driven from here rather than from the card's own Update so the mark cannot be a frame
        // behind the body it is stuck to: the whole point of it is that the two are one object.
        if (contact != null)
            contact.Track(ContactPoint());
    }

    void OnDisable() => Restore();

    /// <summary>
    /// Snap out, hold, then one cosine spring that crosses home at the middle of its settle,
    /// overshoots, and is exactly zero at the end. The overshoot depth is set by
    /// <paramref name="shape"/>: a higher power flattens the rebound without changing when the
    /// layer is finished.
    /// </summary>
    static float Envelope(float time, float snap, float hold, float settle, float shape)
    {
        if (time <= 0f)
            return 0f;
        if (time < snap)
            return 1f - Mathf.Pow(1f - Mathf.Clamp01(time / Mathf.Max(0.0001f, snap)), 4f);

        float held = time - snap;
        if (held < hold)
            return 1f;

        float progress = Mathf.Clamp01((held - hold) / Mathf.Max(0.0001f, settle));
        if (progress >= 1f)
            return 0f;
        return Mathf.Pow(1f - progress, shape) * Mathf.Cos(Mathf.PI * progress);
    }

    void ApplyPose(
        float shove,
        float lift,
        float leanDegrees,
        float turnDegrees,
        float compress,
        float spread,
        float sink,
        float uniform
    )
    {
        if (parts == null || parts.Length == 0)
            return;

        // Built in world space, where the shove is guaranteed horizontal and unit length, and only
        // then brought into the root's frame.
        Vector3 localUp = transform.InverseTransformDirection(Vector3.up);
        Vector3 localAim = transform.InverseTransformDirection(shoveDirection);
        Vector3 leanAxis = transform.InverseTransformDirection(
            Vector3.Cross(Vector3.up, shoveDirection)
        );

        Quaternion turn =
            Quaternion.AngleAxis(leanDegrees, leanAxis)
            * Quaternion.AngleAxis(turnDegrees, localUp);

        visualOffset = shoveDirection * shove + Vector3.up * lift;

        Vector3 rootSquash = Squash(localAim, localUp, compress, spread, sink, uniform);
        Vector3 localOffset = transform.InverseTransformVector(visualOffset);

        for (int i = 0; i < parts.Length; i++)
        {
            Transform part = parts[i].Part;
            if (part == null)
                continue;

            // The offset from the feet lives in the root's frame while the scale lives in the
            // part's, and an imported model is rarely aligned to its parent — this one is yawed a
            // quarter turn, which would compress it across the impact instead of along it.
            Quaternion intoPart = Quaternion.Inverse(parts[i].BaseRotation);
            Vector3 crushed = Vector3.Scale(parts[i].BasePosition - bodyPivotLocal, rootSquash);

            part.localPosition = bodyPivotLocal + turn * crushed + localOffset;
            part.localRotation = turn * parts[i].BaseRotation;
            part.localScale = Vector3.Scale(
                parts[i].BaseScale,
                Squash(intoPart * localAim, intoPart * localUp, compress, spread, sink, uniform)
            );
        }

        if (canvas != null)
        {
            Vector3 lead = bodyCentreLocal - bodyPivotLocal;
            Vector3 carried = turn * Vector3.Scale(lead, rootSquash) - lead + localOffset;
            canvas.localPosition = canvasBase + carried * CanvasFollow;
        }

        posed = true;
    }

    /// <summary>
    /// Compression along <paramref name="aim"/>, a spread across it in the same plane, and a sink
    /// along <paramref name="up"/>, projected onto the axes a Transform can actually scale. The
    /// three directions are orthonormal, so the weights on any one axis sum to 1 and a diagonal hit
    /// blends between axes rather than snapping to one.
    /// </summary>
    static Vector3 Squash(
        Vector3 aim,
        Vector3 up,
        float compress,
        float spread,
        float sink,
        float uniform
    )
    {
        Vector3 scale = Vector3.zero;
        for (int axis = 0; axis < 3; axis++)
        {
            float along = aim[axis] * aim[axis];
            float vertical = up[axis] * up[axis];
            float across = Mathf.Max(0f, 1f - along - vertical);
            scale[axis] =
                (along * (1f - compress) + vertical * (1f - sink) + across * (1f + spread))
                * uniform;
        }
        return scale;
    }

    void Restore()
    {
        running = false;
        collapsing = false;
        visualOffset = Vector3.zero;
        contact = null;

        if (posed)
        {
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    Transform part = parts[i].Part;
                    if (part == null)
                        continue;
                    part.localPosition = parts[i].BasePosition;
                    part.localRotation = parts[i].BaseRotation;
                    part.localScale = parts[i].BaseScale;
                }
            }
            if (canvas != null)
                canvas.localPosition = canvasBase;
        }

        posed = false;
        enabled = false;
    }

    /// <summary>
    /// The rest pose is read once, the first time this unit is ever hit, and every restore goes
    /// back to that. A second hit arriving mid-reaction therefore cannot record the deformed pose
    /// as the new rest, and neither can another system that happens to be posing the same children.
    /// </summary>
    bool EnsureBody()
    {
        if (parts != null)
            return parts.Length > 0;

        List<Transform> found = new();
        List<Renderer> renderers = new();
        HitBody.Collect(transform, found, renderers);

        parts = new BodyPart[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            parts[i] = new BodyPart
            {
                Part = found[i],
                BasePosition = found[i].localPosition,
                BaseRotation = found[i].localRotation,
                BaseScale = found[i].localScale,
            };
        }

        bodyRenderers = renderers.ToArray();
        turnSign = (GetInstanceID() & 1) == 0 ? 1f : -1f;
        HitBody.Measure(transform, bodyRenderers, out centreHeight, out footHeight);

        float centreLocal = 0f;
        float footLocal = -1f;
        if (HitBody.TryBounds(bodyRenderers, out Bounds bounds))
        {
            Vector3 column = transform.position;
            centreLocal = transform
                .InverseTransformPoint(new Vector3(column.x, bounds.center.y, column.z))
                .y;
            footLocal = transform
                .InverseTransformPoint(new Vector3(column.x, bounds.min.y, column.z))
                .y;
        }
        bodyCentreLocal = new Vector3(0f, centreLocal, 0f);
        bodyPivotLocal = new Vector3(0f, Mathf.Min(footLocal, centreLocal - 0.05f), 0f);

        canvas = FindCanvas();
        canvasBase = canvas != null ? canvas.localPosition : Vector3.zero;

        return parts.Length > 0;
    }

    /// <summary>The health bar's holder, found by what it is rather than by what it is called.</summary>
    Transform FindCanvas()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.GetComponent<Canvas>() != null)
                return child;
        }
        return null;
    }

    /// <summary>
    /// The middle of the body as it stands right now, pushed back along the blow far enough that
    /// the contact mass straddles the near edge of the silhouette instead of trailing a cell
    /// behind it. The offset has to ride the shove: the body clears most of its knockback inside
    /// the first captured frame, and a mark left at the original cell reads as a prop on the floor.
    /// </summary>
    Vector3 ContactPoint()
    {
        return transform.position
            + visualOffset
            + Vector3.up * centreHeight
            - shoveDirection * contactInset;
    }
}

/// <summary>
/// The matter knocked off a body at the moment of contact: one hard-edged, uneven lump of spall
/// planted on the side the blow arrived from, biting into the silhouette and spilling just past
/// it, with an ember in its outer half sized by the blow that struck it off.
/// <para>
/// It is opaque and darker than the board rather than additive. The floor here sits at luminance
/// 182 of 255, so glow has seventy levels to climb and an occluder has a hundred and eighty to
/// fall; only material that occludes reads as debris struck off a body. The ember inside it is
/// authored so that red clips and the other two channels do not, which keeps its bloom in its own
/// hue instead of throwing a white halo over the unit's outline.
/// </para>
/// </summary>
class HitStampFlash : MonoBehaviour
{
    static readonly int MassLitId = Shader.PropertyToID("_MassLit");
    static readonly int MassShadeId = Shader.PropertyToID("_MassShade");
    static readonly int HotColorId = Shader.PropertyToID("_HotColor");
    static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    static readonly int GlintColorId = Shader.PropertyToID("_GlintColor");
    static readonly int FacingId = Shader.PropertyToID("_Facing");
    static readonly int ReachId = Shader.PropertyToID("_Reach");
    static readonly int SolidId = Shader.PropertyToID("_Solid");
    static readonly int HotId = Shader.PropertyToID("_Hot");
    static readonly int BreakId = Shader.PropertyToID("_Break");
    static readonly int SeedId = Shader.PropertyToID("_Seed");

    // Scene-linear, straight out of an unlit pass, so these are the values that reach the frame.
    // The two spall tones land at luminance 65 and 21 against a floor at 182. The ember stops one
    // channel short of white — 1.55 sits under the scene's 1.8 bloom threshold — and only the core
    // above it is allowed to spill, in red, where a halo cannot lift a dark outline past the level
    // that still reads as an outline.
    static readonly Vector4 MassLit = new(0.07f, 0.058f, 0.055f, 1f);
    static readonly Vector4 MassShade = new(0.016f, 0.012f, 0.013f, 1f);
    static readonly Vector4 HotColor = new(1.55f, 0.32f, 0.045f, 1f);
    static readonly Vector4 CoreColor = new(3.2f, 0.34f, 0.05f, 1f);
    static readonly Vector4 GlintColor = new(4.4f, 2.2f, 0.7f, 1f);

    // Held flat for two frames at 30Hz before anything moves, because an impact has one money
    // frame and a peak that lasts a single sample is a peak nobody sees. Two is the floor: the
    // mark is on screen for a sixth of a second in total, which is as long as a mark left by
    // something that happens this often can be without becoming scenery.
    const float HoldSeconds = 0.06f;
    const float FadeSeconds = 0.12f;
    const float DriftDistance = 0.1f;

    Camera board;
    Material material;
    Vector3 anchor;
    Vector3 facing = Vector3.right;
    float size;
    float hotScale;
    float elapsed;

    /// <summary>
    /// How far back along the blow the mass sits from the middle of the body: far enough that it
    /// still straddles the near edge of the silhouette, close enough that a bullet does not leave
    /// debris out on clean floor half a cell from the unit it came off.
    /// <para>
    /// The card used to be a cell and a third across whatever the hit was worth, which put more
    /// wreckage on the deck for a graze than the graze took off the health bar. It is sized off
    /// the blow now: a rifle round strikes off a lump about a fifth of a cell across and a
    /// near-lethal one about twice that.
    /// </para>
    /// </summary>
    internal static float ContactInset(float force) => ContactSize(force) * 0.3f;

    static float ContactSize(float force) => 0.9f + 0.85f * Mathf.Clamp01(force);

    /// <summary>The strike itself, aimed back at whatever threw it.</summary>
    internal static HitStampFlash SpawnContact(
        Vector3 position,
        Vector3 towardSource,
        float force,
        float hotScale
    )
    {
        Shader stampShader = Shader.Find("BattlePlan/HitStamp");
        Camera camera = HitBody.BoardCamera();
        if (stampShader == null || camera == null)
            return null;

        GameObject card = GameObject.CreatePrimitive(PrimitiveType.Quad);
        card.name = "HitStamp";
        Collider cardCollider = card.GetComponent<Collider>();
        if (cardCollider != null)
        {
            // Disabled before it is destroyed: destruction waits for the end of the frame, and a
            // collider standing in a unit's chest for even one frame could answer a line-of-sight
            // raycast that decides gameplay.
            cardCollider.enabled = false;
            Destroy(cardCollider);
        }

        Material material = new(stampShader);
        material.SetVector(MassLitId, MassLit);
        material.SetVector(MassShadeId, MassShade);
        material.SetVector(HotColorId, HotColor);
        material.SetVector(CoreColorId, CoreColor);
        material.SetVector(GlintColorId, GlintColor);
        material.SetFloat(SeedId, Random.Range(0f, 40f));
        material.SetFloat(ReachId, 1f);
        material.SetFloat(SolidId, 1f);
        material.SetFloat(BreakId, 0f);
        material.SetFloat(HotId, Mathf.Clamp01(hotScale));

        Renderer cardRenderer = card.GetComponent<Renderer>();
        cardRenderer.material = material;
        cardRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cardRenderer.receiveShadows = false;
        cardRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        HitStampFlash flash = card.AddComponent<HitStampFlash>();
        flash.board = camera;
        flash.material = material;
        flash.anchor = position;
        flash.facing =
            towardSource.sqrMagnitude > 0.0001f ? towardSource.normalized : camera.transform.right;
        flash.size = ContactSize(force);
        flash.hotScale = Mathf.Clamp01(hotScale);
        flash.Place();
        return flash;
    }

    /// <summary>Moves the mark to where the body it marks has got to.</summary>
    internal void Track(Vector3 position)
    {
        anchor = position;
        Place();
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float total = HoldSeconds + FadeSeconds;
        if (elapsed >= total)
        {
            Destroy(gameObject);
            return;
        }

        if (material == null)
            return;

        if (elapsed < HoldSeconds)
        {
            material.SetFloat(ReachId, 1f + 0.06f * (elapsed / HoldSeconds));
            material.SetFloat(HotId, hotScale);
            material.SetFloat(BreakId, 0f);
            material.SetFloat(SolidId, 1f);
        }
        else
        {
            // The light leaves first and the matter after it, and the matter leaves in pieces: a
            // shape that dissolves on one opacity ramp is a decal, whatever it is a picture of.
            float fading = (elapsed - HoldSeconds) / FadeSeconds;
            material.SetFloat(ReachId, 1.06f + 0.16f * fading);
            material.SetFloat(HotId, hotScale * Mathf.Clamp01(1f - fading * 1.9f));
            material.SetFloat(BreakId, Mathf.Clamp01((fading - 0.12f) * 1.15f));
            material.SetFloat(SolidId, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, fading)));
        }

        Place();
    }

    /// <summary>
    /// Faces the card at the board and creeps it back along the blow. Spall sprays toward whatever
    /// struck it, and the creep is what keeps the mass from holding one bounding box for its whole
    /// life while its own lighting changes underneath it.
    /// </summary>
    void Place()
    {
        if (board == null || material == null)
            return;

        Transform view = board.transform;
        float drift = DriftDistance * (1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / (HoldSeconds + FadeSeconds)), 2f));

        // Pulled toward the camera so the mass wins against the body it is wrapping, but still
        // depth-tested so it cannot show through a wall between the board and the lens.
        transform.SetPositionAndRotation(anchor + facing * drift - view.forward * 0.3f, view.rotation);
        transform.localScale = Vector3.one * size;
        material.SetVector(
            FacingId,
            new Vector4(Vector3.Dot(facing, view.right), Vector3.Dot(facing, view.up), 0f, 0f)
        );
    }

    void OnDestroy()
    {
        if (material == null)
            return;
        Destroy(material);
        material = null;
    }
}

/// <summary>
/// A standalone copy of the model, pressed into the deck so a death can be seen.
/// <para>
/// The networked object is deactivated within the frame it dies, which would cut any reaction
/// driven on the unit itself off after one frame. This snapshots the body's meshes into an object
/// that owns nothing on the network and takes it down.
/// </para>
/// <para>
/// It goes straight down and nowhere else. A topple is horizontal travel, and horizontal travel is
/// what a hit looks like; the one thing a death has that no other event on this board has is a
/// downward vector. So the knees give, the body loses more than half its height while spreading in
/// the ground plane, and it is driven into the deck until the board itself cuts it away — the deck
/// is opaque and the camera is 73 degrees off the horizontal, so nothing here needs to fade.
/// </para>
/// <para>
/// It is held for three captured frames with most of it already under the surface. That held pose
/// is what puts a body going down in the impact panel, and it is timed against the hole opening
/// beneath it: both clocks come from <see cref="DeathCollapse"/> so they cannot drift apart.
/// </para>
/// <para>
/// It also has to be legible for the whole of the sink, not for the first two frames of it. The
/// hole under it holds its light while the body is still coming down and the body holds its paint
/// for the same five frames, so the crush happens against something rather than inside it.
/// </para>
/// </summary>
class HitDeathPuppet : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    const float SinkSeconds = DeathCollapse.SinkSeconds;
    const float SubmergedSeconds = DeathCollapse.SubmergedSeconds;
    const float SwallowSeconds = DeathCollapse.SwallowSeconds;

    /// <summary>
    /// Far enough under the floor that nothing is left of it. Reached only after the held pose,
    /// so the body is legible for six frames before it is gone rather than for none.
    /// </summary>
    const float SwallowDepth = 1.05f;

    /// <summary>What is left of the body once it is swallowed, on top of the crush.</summary>
    const float SwallowHeight = 0.42f;

    // Small, and only enough to keep the crush off a machined vertical axis. A body that turns or
    // leans as it goes converts height into sideways screen travel, which widens a silhouette at
    // exactly the moment it is supposed to be collapsing.
    const float TiltDegrees = 7f;
    const float YawDegrees = 13f;

    /// <summary>
    /// A corpse is a hole in a near-white board, not a light on it. It ends as a cold near-black
    /// silhouette against the lit shaft it is going into — but not before the panel has had time
    /// to show it, so the char waits.
    /// </summary>
    static readonly Color CharColor = new(0.03f, 0.038f, 0.046f, 1f);

    const float TeamInChar = 0.22f;

    /// <summary>
    /// How long the body keeps its own paint, and how long the char then takes, in seconds. The
    /// team colour is the only thing in the panel that says whose unit this was, and the old ramp
    /// had taken it by the third captured frame — so the sink was legible for two frames and
    /// everything it did after that happened to a shape nobody could pick out. Five frames of
    /// paint costs the held frame nothing: by then the char is running and the shaft behind the
    /// body is bright enough to silhouette it either way.
    /// </summary>
    const float CharDelay = 0.12f;

    /// <inheritdoc cref="CharDelay"/>
    const float CharSeconds = 0.26f;

    /// <summary>
    /// What the hole throws back up onto the body, and only once the body has stopped being its
    /// own colour. At full strength from the first frame this cold cast simply overwrote the
    /// albedo: the corpse turned cyan, and a cyan corpse inside a cyan hole is not a corpse.
    /// </summary>
    const float RimStrength = 0.016f;

    readonly List<Mesh> bakedMeshes = new();
    Renderer[] pieces;
    Color[] albedo;
    MaterialPropertyBlock properties;

    Vector3 origin;
    Vector3 tiltAxis = Vector3.forward;
    Vector3 baseScale = Vector3.one;
    Quaternion baseRotation = Quaternion.identity;
    Color charTone = CharColor;
    float footHeight = -0.2f;
    float spinSign = 1f;
    float elapsed;

    /// <summary>Drops a corpse for a unit this has no other knowledge of, live or already hidden.</summary>
    internal static void SpawnFor(Transform root, Vector3 sourcePosition, Color teamColor)
    {
        if (root == null)
            return;

        List<Transform> parts = new();
        List<Renderer> renderers = new();
        HitBody.Collect(root, parts, renderers);
        if (renderers.Count == 0)
            return;

        HitBody.Measure(root, renderers, out float centreHeight, out float footHeight);
        float spinSign = (root.GetInstanceID() & 1) == 0 ? 1f : -1f;
        Vector3 direction = HitBody.AwayFrom(root, sourcePosition);
        Spawn(root, renderers.ToArray(), direction, teamColor, centreHeight, footHeight, spinSign);
    }

    internal static void Spawn(
        Transform root,
        Renderer[] sources,
        Vector3 direction,
        Color teamColor,
        float centreHeight,
        float footHeight,
        float spinSign
    )
    {
        if (root == null || sources == null || sources.Length == 0)
            return;

        GameObject host = new("HitDeathPuppet");
        host.transform.SetPositionAndRotation(root.position, root.rotation);
        host.transform.localScale = root.lossyScale;

        HitDeathPuppet puppet = host.AddComponent<HitDeathPuppet>();
        puppet.Build(root, sources);
        if (puppet.pieces == null || puppet.pieces.Length == 0)
        {
            Destroy(host);
            return;
        }

        Vector3 axis = Vector3.Cross(Vector3.up, direction);
        puppet.origin = root.position;
        puppet.baseScale = host.transform.localScale;
        puppet.baseRotation = root.rotation;
        // Only the axis survives the direction the blow came from: a corpse that also travels
        // along it is a body being knocked over, and this one is being taken straight down.
        puppet.tiltAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.right;
        puppet.charTone = Color.Lerp(CharColor, teamColor * 0.22f, TeamInChar);
        // Clamped short of the root, which sits above the model's head: a crush hung from there
        // would pull the body off the floor instead of pressing it into it.
        puppet.footHeight = Mathf.Min(footHeight, centreHeight - 0.1f);
        puppet.spinSign = spinSign;
        puppet.Pose();
    }

    void Build(Transform root, Renderer[] sources)
    {
        List<Renderer> built = new();
        List<Color> tints = new();

        for (int i = 0; i < sources.Length; i++)
        {
            // activeSelf rather than activeInHierarchy: by the time a death reaches here the whole
            // unit is usually switched off, and a corpse made of nothing is worse than no corpse.
            Renderer source = sources[i];
            if (source == null || !source.enabled || !source.gameObject.activeSelf)
                continue;

            Mesh mesh = null;
            if (source is SkinnedMeshRenderer skinned)
            {
                mesh = new Mesh { name = "HitDeathBake" };
                skinned.BakeMesh(mesh);
                bakedMeshes.Add(mesh);
            }
            else if (source.TryGetComponent(out MeshFilter filter))
            {
                mesh = filter.sharedMesh;
            }

            if (mesh == null)
                continue;

            Matrix4x4 relative = root.worldToLocalMatrix * source.transform.localToWorldMatrix;
            GameObject piece = new(source.name) { layer = source.gameObject.layer };
            piece.transform.SetParent(transform, false);
            piece.transform.localPosition = (Vector3)relative.GetColumn(3);
            piece.transform.localRotation = relative.rotation;
            piece.transform.localScale = relative.lossyScale;

            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer pieceRenderer = piece.AddComponent<MeshRenderer>();
            // Shared, never instanced: the corpse borrows the crew's materials and tints itself
            // through a property block, so nothing it does can follow the living units home.
            pieceRenderer.sharedMaterials = source.sharedMaterials;
            pieceRenderer.shadowCastingMode = source.shadowCastingMode;
            pieceRenderer.receiveShadows = false;

            Material shared = source.sharedMaterial;
            built.Add(pieceRenderer);
            tints.Add(
                shared != null && shared.HasProperty(BaseColorId)
                    ? shared.GetColor(BaseColorId)
                    : Color.white
            );
        }

        pieces = built.ToArray();
        albedo = tints.ToArray();
        properties = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (pieces == null || pieces.Length == 0)
            return;

        elapsed += Time.deltaTime;
        if (elapsed >= SinkSeconds + SubmergedSeconds + SwallowSeconds)
        {
            Destroy(gameObject);
            return;
        }

        Pose();
    }

    void Pose()
    {
        // Five captured frames of going down, three of being held there, then gone. The crush
        // accelerates slightly so the body is still moving when it stops, which reads as hitting
        // something rather than as easing into a rest pose.
        float sink = Mathf.Pow(Mathf.Clamp01(elapsed / SinkSeconds), 1.35f);
        float swallow = Mathf.Clamp01(
            (elapsed - SinkSeconds - SubmergedSeconds) / SwallowSeconds
        );
        float gone = swallow * swallow;

        // The ground plane is very nearly the screen plane at this camera, so height is the cheap
        // axis and footprint is the expensive one. Losing more than half the height while gaining
        // a fifth of the width is what turns a standing silhouette into a squat one — a body that
        // only shrinks reads as retreating into the distance.
        float squat = Mathf.Lerp(1f, DeathCollapse.CrushHeight, sink) * Mathf.Lerp(1f, SwallowHeight, gone);
        float spread = Mathf.Lerp(1f, DeathCollapse.CrushSpread, sink) * Mathf.Lerp(1f, 0.86f, gone);
        float depth = DeathCollapse.SinkDepth * sink + SwallowDepth * gone;

        Quaternion turn =
            Quaternion.AngleAxis(TiltDegrees * sink * spinSign, tiltAxis)
            * Quaternion.AngleAxis(YawDegrees * sink * spinSign, Vector3.up);

        Vector3 pivot = origin + Vector3.up * (footHeight - depth);

        transform.rotation = turn * baseRotation;
        transform.position = pivot + turn * (Vector3.up * (-footHeight * squat));
        transform.localScale = Vector3.Scale(baseScale, new Vector3(spread, squat, spread));

        // Held, then charred. The crush is the whole content of the impact panel and it is only
        // readable while the body still looks like the unit it was.
        Tint(
            Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(CharDelay, CharDelay + CharSeconds, elapsed)
            )
        );
    }

    void Tint(float progress)
    {
        if (pieces == null || properties == null)
            return;

        // Darkening, never brightening. The board's floor is close to white and its bloom threshold
        // is close to the paint's rest value, so the only direction a corpse can go and still be
        // seen is down — and it has to end darker than the hole's inner wall, or the silhouette
        // that carries the whole read is a dark shape on a dark shape.
        float gain = Mathf.Lerp(0.95f, 0.28f, progress);
        float blend = Mathf.Lerp(0.12f, 0.94f, progress);

        // Only the cast of the light it is falling into, never a lamp of its own, and only once
        // there is no paint left for it to overwrite. Emission may or may not be enabled on a crew
        // material; if it is not, the silhouette still carries this on its own and nothing here
        // depends on it.
        Color rim =
            DeathCollapse.VoidLight
            * (RimStrength * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, progress)));

        for (int i = 0; i < pieces.Length; i++)
        {
            Renderer piece = pieces[i];
            if (piece == null)
                continue;
            Color tint = Color.Lerp(albedo[i], charTone, blend) * gain;
            tint.a = albedo[i].a;
            piece.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, tint);
            properties.SetColor(EmissionColorId, rim);
            piece.SetPropertyBlock(properties);
        }
    }

    void OnDestroy()
    {
        for (int i = 0; i < bakedMeshes.Count; i++)
        {
            if (bakedMeshes[i] != null)
                Destroy(bakedMeshes[i]);
        }
        bakedMeshes.Clear();
    }
}
