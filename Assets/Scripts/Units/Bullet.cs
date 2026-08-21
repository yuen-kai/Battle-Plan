using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A shot in flight. Bullets are no longer network objects: the server instantiates an authoritative
/// one that resolves damage, and every peer instantiates its own visual-only copy from a single
/// <c>FireBulletClientRpc</c>. Travel time is gameplay here - at three cells per second a shot is
/// airborne long enough for its target to walk out of it - so the server still simulates a real
/// projectile rather than resolving the hit at the muzzle.
/// </summary>
public class Bullet : MonoBehaviour
{
    private static readonly HashSet<Bullet> activeServerBullets = new();

    public float damage = 10f;
    public float backstabMultiplier = 1f;
    public float backstabAngle = 90f; // Angle in degrees from forward direction to consider a backstab
    public float range = 50f;
    public string enemyTeam = "RedTeam";

    // Opt-in, off by default so the five existing units stay single-target hitscan-on-contact.
    public bool explodesOnImpact = false;
    public float aoeRadius = 0f;
    public bool pierces = false;

    /// <summary>
    /// The velocity this shot was launched with, kept so a piercing round can be put back on its
    /// original course after a collision. A bullet carries a real, non-trigger collider and a
    /// Rigidbody, so Unity has already resolved the impact by the time
    /// <see cref="OnCollisionEnter"/> runs: simply declining to destroy the bullet leaves it
    /// deflected or stopped dead against the body it was supposed to pass through. Restoring this
    /// is what actually makes piercing work.
    /// </summary>
    private Vector3 launchVelocity;

    // How the shot is painted for whoever is watching it: own team first, enemy second, the same
    // order Unit uses for its team indicators.
    public List<Material> teamMaterials;

    private Vector3 startPosition;

    private float maxLifetime = 8f;
    private float timeElapsed = 0f;

    /// <summary>
    /// Only the server's copy applies damage. Every other copy is a tracer that flies the same path
    /// and stops on the same geometry, purely so the shot is visible.
    /// </summary>
    private bool isAuthoritative;

    /// <summary>Reported to on every confirmed hit (after backstab is folded in), authoritative
    /// copy only. Settable at spawn time for a future consecutive-hit-streak component to subscribe
    /// to; unset by default so existing bullets report to nobody.</summary>
    private System.Action<GameObject> onHit;

    // Guards a piercing bullet from re-damaging the same collider on a re-entrant collision, and
    // (for an exploding bullet) keeps the direct hit and the splash from double-counting the same
    // target.
    private readonly HashSet<GameObject> hitTargets = new();

    public static int ActiveServerBulletCount => activeServerBullets.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveBullets()
    {
        activeServerBullets.Clear();
    }

    /// <summary>
    /// The team that fired this shot, read off the team it is able to damage. Which prefab a shot
    /// spawns from is fixed by the shooter's real team — the prefab also carries which shields the
    /// round passes through — so this pairing holds on every peer with nothing replicated, and a
    /// bullet can be coloured on the frame it appears rather than a tick later.
    /// </summary>
    public static int GetShooterTeamIndex(string damageableTeam)
    {
        return GameLoop.GetEnemyTeamIndex(GameLoop.GetTeamIndex(damageableTeam));
    }

    /// <summary>
    /// Arms the shot. Called immediately after instantiation rather than from Awake, because the
    /// damage figures and the team it may hit are only known to the caller.
    /// </summary>
    public void Initialize(
        Vector3 velocity,
        float shotDamage,
        float shotBackstabMultiplier,
        float shotRange,
        float shotBackstabAngle,
        string damageableTeam,
        bool authoritative,
        bool shotExplodesOnImpact = false,
        float shotAoeRadius = 0f,
        bool shotPierces = false,
        System.Action<GameObject> shotOnHit = null
    )
    {
        damage = shotDamage;
        backstabMultiplier = shotBackstabMultiplier;
        range = shotRange;
        backstabAngle = shotBackstabAngle;
        enemyTeam = damageableTeam;
        isAuthoritative = authoritative;
        explodesOnImpact = shotExplodesOnImpact;
        aoeRadius = shotAoeRadius;
        pierces = shotPierces;
        onHit = shotOnHit;
        startPosition = transform.position;
        launchVelocity = velocity;

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
            body.linearVelocity = velocity;

        ApplyTeamPresentation();

        if (authoritative)
            activeServerBullets.Add(this);
    }

    /// <summary>
    /// Colours the shot from the side of the board it is watched from. Both players read their own
    /// crew as blue, so a shot that kept the colour its prefab was authored in would come out as
    /// the wrong side's fire on the client's screen — every unit there is already drawn from the
    /// client's perspective.
    /// </summary>
    private void ApplyTeamPresentation()
    {
        Renderer bulletRenderer = GetComponentInChildren<Renderer>();
        if (bulletRenderer == null || teamMaterials == null || teamMaterials.Count < 2)
            return;

        bulletRenderer.sharedMaterial = GameLoop.IsTeamFriendlyToLocalPlayer(
            GetShooterTeamIndex(enemyTeam)
        )
            ? teamMaterials[0]
            : teamMaterials[1];
    }

    private void OnDestroy()
    {
        activeServerBullets.Remove(this);
    }

    void Update()
    {
        if (IsPathBlockedBySmoke(transform.position))
        {
            Destroy(gameObject);
            return;
        }

        if (Vector3.Distance(startPosition, transform.position) > range || timeElapsed > maxLifetime)
        {
            Destroy(gameObject);
            return;
        }
        timeElapsed += Time.deltaTime;
    }

    private void OnCollisionEnter(Collision other) // built-in function
    {
        GameObject hitObject = other.gameObject;

        if (explodesOnImpact)
        {
            Vector3 impactPosition = other.contactCount > 0
                ? other.GetContact(0).point
                : transform.position;

            if (isAuthoritative)
            {
                if (
                    hitObject.CompareTag(enemyTeam)
                    && !IsPathBlockedBySmoke(hitObject.transform.position)
                    && hitTargets.Add(hitObject)
                )
                {
                    ApplyDirectHitDamage(hitObject);
                }
                ResolveAreaImpact(impactPosition);
            }

            // Outside the authoritative guard on purpose: damage is the server's business, but the
            // blast has to be SEEN by everyone. Each peer already has its own copy of this bullet
            // (the server's authoritative one, or the tracer from FireBulletClientRpc), so each can
            // play its own explosion locally with no extra RPC. Without this, an exploding round
            // dealt splash damage completely invisibly -- the designer's note that the rocket
            // launcher's basic attack "should explode" was about exactly this missing read.
            PlayImpactExplosion(impactPosition);

            Destroy(gameObject);
            return;
        }

        bool isEnemy = hitObject.CompareTag(enemyTeam);
        if (isEnemy)
        {
            if (
                isAuthoritative
                && !IsPathBlockedBySmoke(hitObject.transform.position)
                && hitTargets.Add(hitObject)
            )
            {
                ApplyDirectHitDamage(hitObject);
            }

            // A piercing bullet keeps flying through anything it can already damage; it only stops
            // on a wall (the tag check above fails) or when range/lifetime expires in Update().
            if (pierces)
            {
                KeepFlyingThrough(other);
                return;
            }
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Puts a piercing round back on the course it was launched on after passing through a body.
    /// <para>
    /// Returning early from <see cref="OnCollisionEnter"/> is not on its own enough to pierce
    /// anything. The collider is a real, non-trigger collider on a Rigidbody, so the physics step
    /// has already resolved the impact before this method is reached — the shot arrives here already
    /// deflected off its line, and often stopped against the body entirely, which is exactly what a
    /// pierce is supposed to not do. Two things are needed: the impulse has to be undone by
    /// restoring the launch velocity, and this collider pair has to be excluded from further
    /// collisions so the bullet does not immediately collide with the same body again while it is
    /// still inside it (which would re-apply the impulse every frame of the overlap and stall the
    /// round mid-target).
    /// </para>
    /// </summary>
    private void KeepFlyingThrough(Collision other)
    {
        Collider ownCollider = GetComponent<Collider>();
        if (ownCollider != null && other.collider != null)
            Physics.IgnoreCollision(ownCollider, other.collider);

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
            body.linearVelocity = launchVelocity;
    }

    /// <summary>
    /// The visible half of an exploding round, played locally on whichever peer owns this copy.
    /// Sized from <see cref="aoeRadius"/> (already in world units by the time it reaches this class)
    /// so the flash a player sees matches the area that was actually damaged, rather than being a
    /// decorative puff at an arbitrary scale. Deliberately smaller and shorter than a Grenade's
    /// detonation: this is a basic attack that fires repeatedly, so it must not read as loud as a
    /// once-per-few-rounds ability.
    /// </summary>
    private void PlayImpactExplosion(Vector3 impactPosition)
    {
        if (aoeRadius <= 0f)
            return;

        Color blast = new(1f, 0.42f, 0.1f);
        ImpactCore.Spawn(impactPosition, AbilityJuice.Hot(blast, 1.6f), aoeRadius * 0.5f, 1f);
        ImpactShockwave.Spawn(
            impactPosition,
            blast,
            aoeRadius,
            0.32f,
            withLightPop: false,
            groundDust: 0.45f
        );
        DebrisBurst.Spawn(impactPosition, blast, aoeRadius * 0.5f, 8);
    }

    private void ApplyDirectHitDamage(GameObject hitObject)
    {
        float finalDamage = !CheckBackstab(hitObject) ? damage : damage * backstabMultiplier;
        hitObject.GetComponent<Health>()?.TakeDamage(finalDamage);
        onHit?.Invoke(hitObject);
    }

    /// <summary>
    /// Splash on top of the direct hit, mirroring Grenade.ExplodeGrenade: every other enemy within
    /// <see cref="aoeRadius"/> of the impact with a clear line back to it takes flat damage (no
    /// backstab — there is no single "behind" for an explosion). The directly hit target is
    /// skipped here since <see cref="ApplyDirectHitDamage"/> already paid it out above.
    /// </summary>
    private void ResolveAreaImpact(Vector3 impactPosition)
    {
        if (aoeRadius <= 0f || IsPathBlockedBySmoke(impactPosition))
            return;

        // Units move by Transform during execution; sync before the overlap query so a completed
        // move is evaluated at its current position even when no physics tick ran this frame.
        Physics.SyncTransforms();

        Collider[] enemiesInRange = Physics.OverlapSphere(
            impactPosition,
            aoeRadius,
            LayerMask.GetMask(enemyTeam)
        );

        foreach (Collider enemyCollider in enemiesInRange)
        {
            GameObject enemyObject = enemyCollider.gameObject;
            if (!hitTargets.Add(enemyObject))
                continue;

            Vector3 directionToEnemy = (enemyObject.transform.position - impactPosition).normalized;
            float distanceToEnemy = Vector3.Distance(impactPosition, enemyObject.transform.position);
            if (
                Physics.Raycast(
                    impactPosition,
                    directionToEnemy,
                    out RaycastHit hit,
                    distanceToEnemy,
                    LayerMask.GetMask("Walls", enemyTeam)
                )
                && hit.collider.gameObject != enemyObject
            )
            {
                // Something else was in the way before the ray reached this candidate.
                continue;
            }

            enemyObject.GetComponent<Health>()?.TakeDamage(damage);
            onHit?.Invoke(enemyObject);
        }
    }

    private bool IsPathBlockedBySmoke(Vector3 destination)
    {
        return GameLoop.Instance != null
            && GameLoop.Instance.DoesWorldSegmentCrossActiveSmoke(startPosition, destination);
    }

    private bool CheckBackstab(GameObject hitObject)
    {
        Vector3 targetForward = hitObject.transform.forward;
        Vector3 bulletDirection = (hitObject.transform.position - startPosition).normalized;
        float angle = Vector3.Angle(targetForward, -bulletDirection);
        return angle >= backstabAngle;
    }
}
