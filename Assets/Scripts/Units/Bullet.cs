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
            if (isAuthoritative)
            {
                Vector3 impactPosition = other.GetContact(0).point;
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
                return;
        }

        Destroy(gameObject);
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
