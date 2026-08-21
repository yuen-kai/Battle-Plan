using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Blitz's active: an aggressive leap that deals damage where it lands, rather than Pogo's purely
/// cosmetic hop. Designer's brief, verbatim: "Someone whos ability is jumps aggressively and deals
/// damage with that jump (unlike pogo which does it as movement); add the debris animation for the
/// ability." -- i.e. this is <see cref="Pogo"/>'s own lob-arc travel, unchanged, with a damage-
/// dealing landing grafted onto the one place Pogo's own landing has none.
/// <para>
/// The travel choreography -- pausing <see cref="Movement"/>/<see cref="Shooting"/>, disabling the
/// caster's own <see cref="Collider"/> for the flight, sampling
/// <see cref="AbilityTrajectory.SampleLob"/> frame by frame, re-enabling the <see cref="Collider"/>
/// on arrival, then <see cref="GameLoop.HoldReturnFireWindow"/> before
/// <see cref="Movement.transitionToShooting"/> -- is copied from <see cref="Pogo"/> verbatim,
/// including its exact arc height, so a rider does not suddenly hang higher or lower in the air
/// depending on which kit it belongs to.
/// </para>
/// <para>
/// The one mechanical addition on top of that shared travel is the landing hit: an AoE damage query
/// built the same way as <see cref="Grenade"/>'s own blast (its private <c>ExplodeGrenade</c>) --
/// <see cref="Physics.OverlapSphere"/> against only the enemy team's physics layer, followed by a
/// per-target <see cref="Physics.Raycast"/> line-of-sight check against "Walls" plus that same
/// layer, so a target standing behind cover is skipped exactly the way a grenade's own splash would
/// skip it. That query is duplicated here rather than shared with Grenade -- this project tolerates
/// that between sibling abilities (Grenade and Pogo do not share a base beyond <see cref="Ability"/>
/// either) rather than reaching for premature sharing. On top of the damage, the landing also fires
/// the impact VFX the designer explicitly asked for: an <see cref="ImpactShockwave"/> sized to the
/// damage radius, and, carrying the brief's own words, a <see cref="DebrisBurst"/>.
/// </para>
/// <para>
/// The landing was, for a while, the only thing this ability showed -- which left the two beats
/// before it unreadable. An unannounced leap gives the crew it lands among no frame to react in, and
/// a rider whose model is teleported along <see cref="AbilityTrajectory.SampleLob"/> once per frame
/// has nothing tying one frame's position to the next, so the flight itself reads as a unit
/// blinking along an invisible arc. <see cref="LaunchFxClientRpc"/> answers both, and answers them
/// without touching the travel: the <see cref="AbilityWindup"/> hazard mark and the
/// <see cref="ShatterLeapTrail"/> are both started on the frame the leap begins and both timed to
/// end when it lands, so the ability costs exactly as long as it always did.
/// </para>
/// </summary>
public class ShatterLeap : Ability
{
    // Slower than Pogo's 1-second hop, on the designer's note that ability speed was too high across
    // the new characters. A damaging leap that can be seen coming is also one the defender can
    // answer, and the extra time is what the launch telegraph arms across.
    float abilityTime = 1.5f;

    /// <summary>Dead time after landing before this unit may shoot again.</summary>
    private const float RecoverySeconds = 0.85f;

    // Matches Pogo's own arc exactly -- see the class summary for why.
    private const float ArcApexHeight = 5f;

    // A landing hit, not a dedicated blast: sized well under Grenade's own 80-point detonation
    // (this is a caster arriving on top of someone, with a modest splash to the sides of that,
    // rather than a thrown charge built to blanket a room) but still enough that getting caught
    // under an aggressive jumper stings.
    [SerializeField]
    private float damage = 50f;

    public override AbilityPathKind BuildPlannedPath(
        Vector3 targetSquare,
        UnitData data,
        List<Vector3> points
    )
    {
        return AbilityTrajectory.BuildLob(
            transform.position,
            GetLandingPosition(targetSquare),
            ArcApexHeight,
            points
        )
            ? AbilityPathKind.Lob
            : AbilityPathKind.None;
    }

    /// <summary>
    /// Where the caster comes down. Reads the collider, so it has to be resolved before the jump
    /// disables it -- see Pogo's own identical method for why.
    /// </summary>
    private Vector3 GetLandingPosition(Vector3 abilitySquare)
    {
        return abilitySquare + Helper.heightOffset(transform);
    }

    public override void ResetForRespawn()
    {
        base.ResetForRespawn();
        Collider unitCollider = GetComponent<Collider>();
        if (unitCollider != null)
            unitCollider.enabled = true;
    }

    /// <summary>
    /// The default splash: a single-target-ish landing hit with a modest radius around it, not
    /// Grenade's dedicated blast. Named so a caller that just wants "the standard leap" does not
    /// have to restate the number.
    /// </summary>
    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 1.6f)
    {
        Vector3 startPosition = transform.position;
        Vector3 targetPosition = GetLandingPosition(abilitySquare); //Has to be before collider is disabled

        transform.GetComponent<Movement>().PauseMovement();
        transform.GetComponent<Shooting>().PauseShooting();
        transform.GetComponent<Movement>().moving = true;
        transform.GetComponent<Collider>().enabled = false;

        // Fired here rather than before the pause above so the tell and the flight start on the same
        // frame: this is what the ability is spending its existing second on, not an extra beat added
        // in front of it. Sent unguarded, matching ImpactFxClientRpc's own call below.
        LaunchFxClientRpc(targetPosition, AreaRadius, abilityTime);

        float elapsed = 0f;

        while (elapsed < abilityTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / abilityTime;

            transform.position = AbilityTrajectory.SampleLob(
                startPosition,
                targetPosition,
                ArcApexHeight,
                progress
            );

            yield return null; // Wait for next frame
        }
        transform.position = targetPosition;

        // Unlike Pogo, something does go off here. A bit more force than Pogo's own pure-movement
        // landing (0.25s / 0.035) -- this rider hits the ground meaning to hurt somebody -- but
        // well short of Grenade's default detonation shake (0.5s / 0.1), because the splash under
        // it is a landing hit, not a dedicated charge.
        CameraEffects.Instance?.CameraShakeClientRpc(0.4f, 0.07f);
        ImpactFxClientRpc(targetPosition, AreaRadius);
        ApplyLandingDamage(FindEnemiesInLandingRadius(targetPosition, AreaRadius));

        transform.GetComponent<Collider>().enabled = true;

        // Recovery: the rider is on the floor where it landed for a beat before it can bring a
        // weapon up. Unlike Pogo -- which resumes shooting on the landing frame -- this leap deals
        // damage, so landing on top of somebody and immediately opening fire was too much for one
        // action. Held before HoldReturnFireWindow so the window is opened when the rider is
        // actually ready to trade, not while it is still picking itself up.
        yield return new WaitForSeconds(RecoverySeconds);

        // Hold the round's weapons free so the units it came down among can answer it, exactly as
        // Pogo's own landing does.
        GameLoop.Instance?.HoldReturnFireWindow();
        transform.GetComponent<Movement>().transitionToShooting();
    }

    /// <summary>
    /// Every enemy collider caught in the landing's blast, exactly the way <see cref="Grenade"/>'s
    /// own <c>ExplodeGrenade</c> finds its targets: an overlap query against only the enemy team's
    /// physics layer, then a per-target line-of-sight raycast against "Walls" plus that same layer,
    /// so a target standing behind cover is skipped. Pulled out as its own public method -- rather
    /// than inlined into <see cref="ExecuteAbility"/> -- for the same reason
    /// <see cref="SuppressingFire.FindEnemiesInBarrage"/> and <see cref="ChainSurge.ResolveTargets"/>
    /// are: it lets an edit-mode test exercise the query directly without a running NetworkManager
    /// or IsServer context.
    /// </summary>
    public List<GameObject> FindEnemiesInLandingRadius(Vector3 landingPosition, float areaRadius)
    {
        List<GameObject> found = new();
        string enemyTeam = GameLoop.GetEnemyTeam(gameObject.tag);

        // Units move by Transform during execution; sync before the overlap query so a completed
        // dodge is evaluated at its current position even when no physics tick ran this frame (see
        // Grenade.ExplodeGrenade's own comment for the same reasoning).
        Physics.SyncTransforms();

        Collider[] enemiesInRange = Physics.OverlapSphere(
            landingPosition,
            areaRadius * GameLoop.cellSize,
            LayerMask.GetMask(enemyTeam)
        );

        foreach (Collider enemy in enemiesInRange)
        {
            Vector3 directionToEnemy = (enemy.transform.position - landingPosition).normalized;
            float distanceToEnemy = Vector3.Distance(landingPosition, enemy.transform.position);
            if (
                Physics.Raycast(
                    landingPosition,
                    directionToEnemy,
                    out RaycastHit hit,
                    distanceToEnemy,
                    LayerMask.GetMask("Walls", enemyTeam)
                )
            )
            {
                // If raycast hits an obstacle before reaching the enemy, skip it.
                if (hit.collider != enemy)
                    continue;
            }

            found.Add(enemy.gameObject);
        }
        return found;
    }

    /// <summary>Flat damage to every target already resolved by <see cref="FindEnemiesInLandingRadius"/>.</summary>
    public void ApplyLandingDamage(List<GameObject> targets)
    {
        foreach (GameObject target in targets)
        {
            if (target == null)
                continue;
            target.GetComponent<Health>()?.TakeDamage(damage);
        }
    }

    /// <summary>
    /// The two beats before the landing, on every peer: where this is coming down, and the fact that
    /// something is crossing the board to get there.
    /// <para>
    /// The tell is <see cref="AbilityWindup"/>, the project's existing "an ability is about to resolve
    /// here" telegraph, rather than anything new -- a hazard plate whose cells arm one at a time under
    /// a countdown ring that closes on the target. Given <paramref name="flightSeconds"/> as its
    /// countdown, it arms across the flight and releases on the frame the rider touches down, so the
    /// crew standing under it gets the whole second of warning that the leap was already taking. This
    /// is the honest reading of a "pre-leap" tell for an ability that must not grow any longer: the
    /// warning has to come out of the time the leap already spends, not out of new time in front of it.
    /// </para>
    /// <para>
    /// Deliberately played with a <c>null</c> caster, which <see cref="AbilityWindup.Play"/> documents
    /// as supported. Passing the real caster would also run its caster performance, and that pose is
    /// authored for a unit standing still while it loads: it pulls the model children back along the
    /// aim by 0.58 cells -- over one and a half world units at this board's cell size, most of a unit's
    /// own height -- which on a rider already travelling that aim would drag its model bodily off the
    /// base plate it is standing on and out from under its own health bar. The ground half of the
    /// wind-up is the half that carries the warning anyway; the caster half is anticipation for a beat
    /// this ability does not have.
    /// </para>
    /// <para>
    /// <see cref="WindupShape.Disc"/> and not <see cref="WindupShape.Point"/>: the landing is an area
    /// hit, and Point clamps its mark to little over half a cell, which would promise a single square
    /// and then damage the ring of squares around it.
    /// </para>
    /// </summary>
    [ClientRpc]
    private void LaunchFxClientRpc(
        Vector3 landingPosition,
        float areaRadius,
        float flightSeconds
    )
    {
        Color teamColor = GameLoop.GetTeamColorForViewer(GetComponent<Unit>()?.TeamIndex ?? -1);

        AbilityWindup.Play(
            // No caster, for the reason above: the ground mark is the half that carries the warning.
            null,
            landingPosition,
            teamColor,
            flightSeconds,
            WindupShape.Disc,
            // The same world-space radius the landing damage is resolved against (see
            // FindEnemiesInLandingRadius), so the mark cannot promise a different area than the hit.
            areaRadius * GameLoop.cellSize
        );

        ShatterLeapTrail.Attach(transform, teamColor, flightSeconds);
    }

    [ClientRpc]
    private void ImpactFxClientRpc(Vector3 landingPosition, float areaRadius)
    {
        Color teamColor = GameLoop.GetTeamColorForViewer(GetComponent<Unit>()?.TeamIndex ?? -1);
        float blast = areaRadius * GameLoop.cellSize;

        // Sized to the damage radius, and given the light pop Pogo's own landing deliberately
        // withholds ("a flash is a detonation, and nothing detonated") -- here something did.
        // groundDust sits between Pogo's light landing dust (0.35) and Grenade's full blast dust
        // (1f): enough to read as a forceful impact, not the loudest event on the board.
        ImpactShockwave.Spawn(
            landingPosition,
            teamColor,
            blast,
            0.5f,
            withLightPop: true,
            groundDust: 0.7f
        );

        // The debris throw the designer's brief explicitly asked for. Thrown a fraction of the
        // blast rather than the whole of it, same convention as Grenade's own call, with a smaller
        // piece count to match this ability's smaller default radius so a landing hit does not
        // litter the board with as much debris as a dedicated grenade.
        DebrisBurst.Spawn(landingPosition, teamColor, blast * 0.55f, 14);
    }
}

/// <summary>
/// The in-flight read: a short, fading streak hanging off the rider for as long as it is airborne.
///
/// <para>Without it the flight is the one part of this ability a player cannot follow. The rider's
/// position is assigned outright once a frame from <see cref="AbilityTrajectory.SampleLob"/>, so
/// nothing on screen connects one frame's silhouette to the next and a body crossing four cells in a
/// second reads as it blinking rather than travelling. A streak is the cheapest thing that states
/// "these positions are the same object": it is the frames the rider has just left, still drawn.</para>
///
/// <para>Sampled off the rider's transform rather than recomputed from the lob. A peer that is not
/// the server does not move this unit itself -- its body arrives interpolated -- so a trail computed
/// from the arc would be a second, subtly different flight path drawn beside the one the player is
/// actually watching. Sampling the transform means the streak is by construction attached to
/// whatever that peer sees.</para>
///
/// <para>Its own <see cref="MonoBehaviour"/> for the same reason <c>AbilityWindupRunner</c> and
/// <see cref="Aftermath"/>'s own runner are: an <see cref="Ability"/> disables itself on every peer
/// but the server, so a per-frame effect cannot live on the ability that started it. Lives in this
/// file rather than <c>Assets/Scripts/VFX</c> because nothing else leaps -- <see cref="Pogo"/>
/// deliberately reads as a movement hop, not an attack -- and the project's convention (see
/// <c>Movement.cs</c>'s own <see cref="SpeedBoostIndicatorVisual"/>) is to keep a single-caller
/// helper next to its caller until a second caller shows up.</para>
///
/// <para>Purely local and visual. It is spawned from a <c>[ClientRpc]</c> scoped to the caster, so a
/// viewer that fog is hiding the rider from never receives it in the first place; a rider that goes
/// out of view mid-flight is caught by the renderer mirror in <see cref="LateUpdate"/>.</para>
/// </summary>
sealed class ShatterLeapTrail : MonoBehaviour
{
    private const string BeamShaderName = "BattlePlan/EnergyBeam";
    private const string FallbackShaderName = "Sprites/Default";

    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");

    /// <summary>
    /// How long a sample stays drawn behind the rider. Short on purpose: the streak has to say
    /// "travelling" without becoming a rope laid across the board that outlives the jump. At this
    /// board's speeds it holds roughly a third of a cell of history.
    /// </summary>
    private const float TailSeconds = 0.24f;

    /// <summary>
    /// Ceiling on retained samples, so a high refresh rate cannot grow the line unbounded. Well
    /// above <see cref="TailSeconds"/> worth of frames at any playable rate; hitting it drops the
    /// oldest, which is the sample already closest to fading out anyway.
    /// </summary>
    private const int MaxSamples = 64;

    /// <summary>Ignores sub-millimetre jitter, so a stalled frame does not stack samples on a point.</summary>
    private const float MinSampleSpacing = 0.05f;

    // Width at the rider and at the far end of the tail. The taper is what gives the streak a
    // direction: an even-width line reads as a fixed object the rider is dragging.
    private const float HeadWidth = 0.42f;
    private const float TailWidth = 0.05f;

    private Transform rider;
    private Renderer riderRenderer;
    private LineRenderer line;
    private Material trailMaterial;
    private readonly List<Vector3> samples = new(MaxSamples);
    private readonly List<float> sampleTimes = new(MaxSamples);
    private float flightEndsAt;

    /// <summary>
    /// Starts a streak on <paramref name="rider"/> for the next <paramref name="flightSeconds"/>.
    /// Unparented on purpose: the samples are world positions the rider has already left, and a
    /// child of the rider would carry them along with it and draw a line that never moves.
    /// </summary>
    public static void Attach(Transform rider, Color color, float flightSeconds)
    {
        if (rider == null || flightSeconds <= 0f)
            return;

        GameObject host = new("ShatterLeapTrail");
        ShatterLeapTrail trail = host.AddComponent<ShatterLeapTrail>();
        trail.rider = rider;
        trail.flightEndsAt = Time.time + flightSeconds;
        trail.Build(color);
    }

    private void Build(Color color)
    {
        // The same shader BeamVFX runs on, with the same fallback: a hot core inside a coloured glow
        // is already what a streak of displaced energy looks like, and it honours the LineRenderer's
        // own vertex colour, which is what lets the tail fade without a material per frame.
        Shader trailShader = Shader.Find(BeamShaderName);
        if (trailShader == null)
            trailShader = Shader.Find(FallbackShaderName);
        if (trailShader == null)
        {
            Debug.LogWarning($"[ShatterLeapTrail] {BeamShaderName} not found; no trail will be drawn");
            Destroy(gameObject);
            return;
        }

        trailMaterial = new Material(trailShader)
        {
            name = "Shatter Leap Trail (Runtime)",
            hideFlags = HideFlags.DontSave,
        };
        if (trailMaterial.HasProperty(GlowColorId))
        {
            trailMaterial.SetColor(GlowColorId, AbilityJuice.Hot(color, 1.7f));
            trailMaterial.SetColor(CoreColorId, AbilityJuice.HotCore);
        }

        line = gameObject.AddComponent<LineRenderer>();
        line.sharedMaterial = trailMaterial;
        line.useWorldSpace = true;
        line.positionCount = 0;
        line.widthMultiplier = HeadWidth;
        // Index 0 is the oldest sample, so the curve runs thin-to-thick toward the rider.
        line.widthCurve = AnimationCurve.EaseInOut(0f, TailWidth / HeadWidth, 1f, 1f);
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // Host fog suppresses a hidden unit's renderers locally rather than despawning it, so the
        // trail follows whichever renderer of the rider it can find instead of drawing a streak for
        // a body that is currently not allowed to be on screen.
        riderRenderer = rider.GetComponentInChildren<Renderer>(true);
    }

    /// <summary>
    /// Sampled in <see cref="LateUpdate"/> because the rider's position is assigned from inside
    /// <see cref="ShatterLeap.ExecuteAbility"/>'s coroutine, which resumes in the Update phase -- so
    /// this is the first point in the frame where the arc position being drawn is final.
    /// </summary>
    private void LateUpdate()
    {
        if (line == null)
            return;

        float now = Time.time;
        bool flying = rider != null && now < flightEndsAt;

        if (flying)
        {
            Vector3 head = rider.position;
            if (
                samples.Count == 0
                || (samples[^1] - head).sqrMagnitude > MinSampleSpacing * MinSampleSpacing
            )
            {
                if (samples.Count >= MaxSamples)
                {
                    samples.RemoveAt(0);
                    sampleTimes.RemoveAt(0);
                }
                samples.Add(head);
                sampleTimes.Add(now);
            }
        }

        DropStaleSamples(now);

        // Once sampling has stopped the tail drains itself within TailSeconds, so this is also the
        // exit for a rider that was destroyed mid-flight rather than a case needing its own branch.
        if (!flying && samples.Count < 2)
        {
            Destroy(gameObject);
            return;
        }

        if (riderRenderer != null)
            line.forceRenderingOff = riderRenderer.forceRenderingOff;

        if (samples.Count < 2)
        {
            line.positionCount = 0;
            return;
        }

        // Pushed one at a time rather than through SetPositions, whose array overload wants an array
        // exactly as long as positionCount -- and this line's length changes almost every frame as it
        // fills and then drains, so that array would have to be reallocated nearly every frame to
        // save at most sixty-four assignments on a one-second effect.
        line.positionCount = samples.Count;
        for (int index = 0; index < samples.Count; index++)
            line.SetPosition(index, samples[index]);

        // The streak dims as it drains so the last frames of it leave rather than being cut. The
        // tail end is dimmer than the head throughout: it is older, and the rider is the thing the
        // eye is meant to be following.
        float alpha = flying
            ? 1f
            : Mathf.Clamp01(1f - (now - flightEndsAt) / Mathf.Max(0.0001f, TailSeconds));
        line.startColor = new Color(1f, 1f, 1f, alpha * 0.12f);
        line.endColor = new Color(1f, 1f, 1f, alpha);
    }

    private void DropStaleSamples(float now)
    {
        int stale = 0;
        while (stale < sampleTimes.Count && now - sampleTimes[stale] > TailSeconds)
            stale++;

        if (stale <= 0)
            return;

        samples.RemoveRange(0, stale);
        sampleTimes.RemoveRange(0, stale);
    }

    private void OnDestroy()
    {
        if (trailMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(trailMaterial);
        else
            DestroyImmediate(trailMaterial);
        trailMaterial = null;
    }
}
