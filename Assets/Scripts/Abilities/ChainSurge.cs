using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Voltaic's active ability: a self-cast burst of lightning that arcs out from the caster's own
/// cell -- there is no square to aim, matching <c>UnitData.selectAbilitySquare = false</c> -- and
/// reaches as many as <see cref="MaxTargets"/> nearby enemies at once. Each bolt lands moderate
/// damage and a short stun, deliberately lighter than the knockback-grade lockout
/// <see cref="DashRush.KnockbackStunSeconds"/> already owns, per the designer's brief: "good
/// damage, some stun."
/// <para>
/// Nothing here travels. The caster never moves, and its own shooting is left alone through the
/// cast, so unlike DashRush, Shield or AreaLock this ability holds none of the
/// PauseMovement/PauseShooting choreography those relocating (or channelled) abilities need --
/// there is no travel to pause movement for, and no aimed beam to stand shooting down for. It also
/// does not override <see cref="Ability.BuildPlannedPath"/>, the same choice AreaLock makes: the
/// zap reaches every target without travelling, so the base class's "report nothing to draw" is
/// already the right preview.
/// </para>
/// </summary>
public class ChainSurge : Ability
{
    /// <summary>
    /// The most enemies a single cast can reach -- the fifth arc of the zap, not a sixth. Applied
    /// only after the line-of-sight filter in <see cref="ResolveTargets"/>, so a crowd of six
    /// enemies with one standing behind a wall still lands on the five actually reachable, rather
    /// than the wall-blocked one displacing a reachable target from the count.
    /// </summary>
    private const int MaxTargets = 5;

    /// <summary>
    /// Deliberately shorter than <see cref="DashRush.KnockbackStunSeconds"/> (0.6s): that number was
    /// the designer's explicit correction for how a hard knockback interrupt should feel, and a zap
    /// is not a knockback. "Some stun" reads as a quick jolt here, not a lockout.
    /// </summary>
    public const float StunSeconds = 0.3f;

    /// <summary>
    /// Moderate per-target damage -- comfortably under Grenade's 80-point splash, because this can
    /// land on up to <see cref="MaxTargets"/> targets in the same cast rather than one.
    /// </summary>
    [SerializeField]
    private float damage = 45f;

    /// <summary>
    /// Charge time before the zap discharges. The ability used to resolve on the frame it started,
    /// which gave the defender nothing to see and made it the fastest thing in the batch.
    /// </summary>
    private const float ChargeSeconds = 0.4f;

    /// <summary>Dead time after the zap before this unit may shoot again.</summary>
    private const float RecoverySeconds = 0.8f;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 3f)
    {
        if (!IsServer)
            yield break;

        Movement movement = GetComponent<Movement>();
        Shooting shooting = GetComponent<Shooting>();

        // The zap owns the weapon for its charge and recovery. Previously this ability never touched
        // Shooting at all, so the caster kept firing its pistol throughout and a recovery would have
        // been invisible.
        if (shooting != null)
            shooting.PauseShooting();

        yield return new WaitForSeconds(ChargeSeconds);

        // Self-cast: the zap is centered on the caster's own position rather than whatever square
        // was passed in. For this ability UnitData.selectAbilitySquare is false, so abilitySquare
        // already resolves to the caster's own cell (see GameLoop's plan[0]-vs-plan[^1] square
        // pick) -- reading transform.position directly is simply the more direct way to say that.
        // Resolved AFTER the charge, so an enemy that walked clear during it is genuinely missed.
        List<GameObject> targets = ResolveTargets(transform.position, AreaRadius);
        ApplyZap(targets);

        // Both ends of every bolt are resolved here, on the server, because this is the only place
        // that still holds the targets themselves -- the RPC can only carry positions.
        ShowZapClientRpc(HandOrigin(), StrikePoints(targets));

        // Recovery before the weapon is handed back -- see RecoverySeconds.
        yield return new WaitForSeconds(RecoverySeconds);
        if (movement != null)
            movement.transitionToShooting();
    }

    /// <summary>
    /// Every enemy within <paramref name="radiusCells"/> cells of <paramref name="center"/> the zap
    /// can actually reach, nearest first, capped at <see cref="MaxTargets"/>. Mirrors Grenade's own
    /// overlap-then-line-of-sight check -- a lightning bolt does not arc through a wall, unlike a
    /// wider-radius ability that deliberately ignores them -- with one addition on top: candidates
    /// are sorted by distance and truncated to the cap only after the line-of-sight pass, so being
    /// wall-blocked can never be the reason a reachable sixth target loses its place to a nearer one
    /// that was never actually in danger of being cut for range.
    /// <para>
    /// Exposed as its own method (rather than folded into <see cref="ExecuteAbility"/>) so target
    /// selection can be exercised directly without a running NetworkManager or IsServer context,
    /// the same way DashRush exposes <see cref="DashRush.FindEnemiesOnCells"/> for direct testing.
    /// </para>
    /// </summary>
    public List<GameObject> ResolveTargets(Vector3 center, float radiusCells)
    {
        string enemyTeam = GameLoop.GetEnemyTeam(gameObject.tag);

        // Units move by Transform during execution. Sync before the overlap query so a completed
        // dodge is evaluated at its current position even when no physics tick ran this frame.
        Physics.SyncTransforms();

        Collider[] candidates = Physics.OverlapSphere(
            center,
            radiusCells * GameLoop.cellSize,
            LayerMask.GetMask(enemyTeam)
        );

        List<GameObject> reachable = new();
        foreach (Collider candidate in candidates)
        {
            Vector3 directionToCandidate = (candidate.transform.position - center).normalized;
            float distanceToCandidate = Vector3.Distance(center, candidate.transform.position);
            if (
                Physics.Raycast(
                    center,
                    directionToCandidate,
                    out RaycastHit hit,
                    distanceToCandidate,
                    LayerMask.GetMask("Walls", enemyTeam)
                )
            )
            {
                // If something (a wall, or another enemy standing in front) is hit before the
                // candidate itself, the bolt never reaches it -- skip.
                if (hit.collider != candidate)
                    continue;
            }

            reachable.Add(candidate.gameObject);
        }

        reachable.Sort(
            (a, b) =>
                Vector3.Distance(center, a.transform.position)
                    .CompareTo(Vector3.Distance(center, b.transform.position))
        );

        if (reachable.Count > MaxTargets)
            reachable.RemoveRange(MaxTargets, reachable.Count - MaxTargets);

        return reachable;
    }

    /// <summary>
    /// Lands the zap on every entry in <paramref name="targets"/>: damage, then a short stun. Takes
    /// the already-resolved list rather than re-querying, so <see cref="ExecuteAbility"/> and any
    /// test calling this directly always zap exactly who <see cref="ResolveTargets"/> selected.
    /// <para>
    /// Left ungated on IsServer here on purpose: <see cref="Health.TakeDamage"/> and
    /// <see cref="Unit.ApplyStun"/> already refuse to run off the server on their own, and
    /// <see cref="ExecuteAbility"/> already refuses to call this at all off the server. Gating a
    /// third time here would just be one more place that check could drift out of sync.
    /// </para>
    /// </summary>
    public void ApplyZap(List<GameObject> targets)
    {
        if (targets == null)
            return;

        foreach (GameObject target in targets)
        {
            if (target == null)
                continue;

            target.GetComponent<Health>()?.TakeDamage(damage);
            target.GetComponent<Unit>()?.ApplyStun(StunSeconds);
        }
    }

    // ---- Presentation only, from here down. Nothing below decides anything: the damage and the
    // stun are already spent by the time ShowZapClientRpc is sent, and every value here exists to
    // make what happened legible rather than to change it.

    /// <summary>
    /// Where the bolts leave from, in preference order. <see cref="PlaceholderModelBuilder"/> builds
    /// its anchors as <c>&lt;model root&gt;/Anchors/&lt;anchor name&gt;</c>, and on the Voltaic
    /// prefab the model root under the unit is itself named "Body" -- hence the "Body/" prefix,
    /// which is that object's name and not a group inside the model. Do not "fix" it to
    /// "Anchors/..." without checking the prefab.
    /// <para>
    /// The off hand comes first because that is where the charge coil is: Voltaic's right hand holds
    /// the charge pistol, which is the basic attack's weapon, so the ability firing from the coil
    /// hand keeps the two reads apart. Either anchor is a correct answer to "from his hand", so a
    /// model that only has one still works.
    /// </para>
    /// </summary>
    private static readonly string[] HandAnchorPaths =
    {
        "Body/Anchors/LeftHand",
        "Body/Anchors/RightHand",
    };

    /// <summary>
    /// Hand height above the caster's own origin when no anchor can be found -- a unit's transform
    /// sits at its feet. Matches the figure <see cref="AbilityWindup"/> falls back to for the same
    /// question (1.02), so a bolt and a wind-up orb leave the same caster from the same height.
    /// </summary>
    private const float HandHeightFallback = 1.02f;

    /// <summary>Chest height on this roster, for a target whose body cannot be measured.</summary>
    private const float StrikeHeightFallback = 0.95f;

    /// <summary>
    /// HDR gain on <see cref="LightningBolt.ElectricBlue"/>. Over the scene's bloom threshold of
    /// 1.8 on its strongest channel, so the arc blooms instead of merely being a pale blue line.
    /// </summary>
    private const float BoltIntensity = 2.4f;

    /// <summary>
    /// Seconds a bolt stays up, jittered per target. Long enough to be caught by a 30 Hz capture
    /// and read as five separate arcs, short enough to be gone before the stun pulse takes over as
    /// the thing describing what happened.
    /// </summary>
    private const float BoltLifetime = 0.3f;
    private const float BoltLifetimeJitter = 0.04f;

    // The discharge at the hand is the loudest single flash of the cast -- it is the one place the
    // whole ability comes from -- and each strike is deliberately smaller, because there are up to
    // five of them and the frame still has to be readable with all five in it.
    private const float DischargeScale = 1.2f;
    private const float DischargeIntensity = 1.1f;
    private const float StrikeScale = 0.9f;
    private const float StrikeIntensity = 0.7f;

    /// <summary>
    /// The zap, on every peer: a lightning bolt from the caster's hand to each enemy the cast
    /// reached, the discharge flash at the hand they all left from, and a strike flash where each
    /// one landed. This is the ability -- "he zaps up to 5 enemies in range using lightning bolts
    /// from his hand" -- and without it a cast takes health off five units with nothing on screen
    /// to say why.
    /// <para>
    /// Runs on the host as well as on remote clients, unlike <c>AreaLock.ShowLaserClientRpc</c>,
    /// which returns early on the server because the host already renders that beam from server
    /// code. Nothing above draws anything, so an <c>IsServer</c> guard here would leave the host --
    /// one of the two players in a match -- watching an invisible ability.
    /// </para>
    /// <para>
    /// All five bolts land on one frame rather than being chased outward one by one. The ability is
    /// a burst from a single caster, not a chain hopping between victims, and the board's own
    /// impact language spends its budget on one held money frame (see <see cref="ImpactCore"/>)
    /// rather than smearing the event across several. Their shapes still differ: every
    /// <see cref="LightningBolt"/> draws its own jag.
    /// </para>
    /// <para>
    /// Nothing here touches the victims' own bodies. Damage arriving on a peer already plays
    /// <c>HitReaction</c> and a damage number off <c>Health</c>'s health-changed callback, and the
    /// stun already pulses the body off the replicated <c>StunState</c> -- so a <c>HitFlash</c> or
    /// a shockwave added here would stamp a second, differently-timed copy of a reaction the victim
    /// is already playing.
    /// </para>
    /// </summary>
    /// <param name="origin">The caster's hand, in world space.</param>
    /// <param name="strikePoints">Where each bolt lands. Empty when the cast reached nobody; the
    /// discharge still fires, because the caster visibly spent the ability either way.</param>
    [ClientRpc]
    private void ShowZapClientRpc(Vector3 origin, Vector3[] strikePoints)
    {
        Color bolt = AbilityJuice.Hot(LightningBolt.ElectricBlue, BoltIntensity);
        ImpactCore.Spawn(origin, bolt, DischargeScale, DischargeIntensity);

        if (strikePoints == null)
            return;

        foreach (Vector3 strikePoint in strikePoints)
        {
            // Lifetimes are jittered so the five arcs do not all wink out on the same frame, which
            // is the one thing that would make a burst of separate bolts look like one object.
            LightningBolt.Spawn(
                origin,
                strikePoint,
                bolt,
                BoltLifetime + Random.Range(-BoltLifetimeJitter, BoltLifetimeJitter)
            );
            ImpactCore.Spawn(strikePoint, bolt, StrikeScale, StrikeIntensity);
        }
    }

    /// <summary>
    /// The caster's hand, resolved against <see cref="HandAnchorPaths"/>. The designer's brief is
    /// specific -- the bolts come from his hand -- and the caster's own transform is at its feet,
    /// which would fire the whole cast out of the floor.
    /// <para>
    /// Looked up per cast rather than cached in a field. This runs once per activation, on the
    /// server only, against a handful of children -- so caching would buy nothing measurable and
    /// cost a reference that has to be kept honest across the model being re-authored. A missing
    /// anchor falls back to a height instead of failing, so a model swapped for one with no anchor
    /// group loses the exact hand and nothing else.
    /// </para>
    /// </summary>
    private Vector3 HandOrigin()
    {
        foreach (string anchorPath in HandAnchorPaths)
        {
            Transform hand = transform.Find(anchorPath);
            if (hand != null)
                return hand.position;
        }
        return transform.position + Vector3.up * HandHeightFallback;
    }

    /// <summary>
    /// Where each bolt lands, for <see cref="ShowZapClientRpc"/>. Taken from each target's own
    /// collider bounds, which is the body's middle at whatever scale that model is actually built
    /// at -- this roster mixes hand-authored models with procedural placeholders, and a fixed chest
    /// height that is right for one is knee height on the other. A target's transform alone would
    /// put every bolt on the floor at its feet.
    /// </summary>
    private static Vector3[] StrikePoints(List<GameObject> targets)
    {
        if (targets == null)
            return System.Array.Empty<Vector3>();

        List<Vector3> points = new(targets.Count);
        foreach (GameObject target in targets)
        {
            if (target == null)
                continue;

            points.Add(
                target.TryGetComponent(out Collider body)
                    ? body.bounds.center
                    : target.transform.position + Vector3.up * StrikeHeightFallback
            );
        }
        return points.ToArray();
    }
}
