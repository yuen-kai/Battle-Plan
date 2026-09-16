using System.Collections.Generic;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    private static readonly HashSet<Bullet> activeServerBullets = new();

    public float damage = 10f;
    public float backstabMultiplier = 1f;
    public float backstabAngle = 90f;
    public float range = 50f;
    public string enemyTeam = "RedTeam";

    public bool explodesOnImpact = false;
    public float aoeRadius = 0f;
    public bool pierces = false;

    private Vector3 launchVelocity;
    private Vector3 collisionCenterOffset;
    private float collisionRadius;
    private int collisionMask;

    public List<Material> teamMaterials;

    private Vector3 startPosition;

    private float maxLifetime = 8f;
    private float timeElapsed = 0f;

    private bool isAuthoritative;

    private readonly HashSet<GameObject> hitTargets = new();
    private readonly HashSet<Collider> ignoredColliders = new();

    public static int ActiveServerBulletCount => activeServerBullets.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveBullets()
    {
        activeServerBullets.Clear();
    }

    public static int GetShooterTeamIndex(string damageableTeam)
    {
        return GameLoop.GetEnemyTeamIndex(GameLoop.GetTeamIndex(damageableTeam));
    }

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
        bool ignoreAdjacentWalls = false
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
        startPosition = transform.position;
        launchVelocity = velocity;

        ConfigureCollisionQuery();

        if (ignoreAdjacentWalls)
            IgnoreAdjacentWalls(GridSystem.ConvertToGridCoords(startPosition));

        ApplyTeamPresentation();

        if (authoritative)
            activeServerBullets.Add(this);
    }

    private void ConfigureCollisionQuery()
    {
        SphereCollider sphere = GetComponentInChildren<SphereCollider>(includeInactive: true);
        if (sphere != null)
        {
            Vector3 scale = sphere.transform.lossyScale;
            collisionRadius = sphere.radius
                * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            collisionCenterOffset = sphere.transform.TransformPoint(sphere.center)
                - transform.position;
            collisionMask = ~sphere.excludeLayers.value;

            for (int layer = 0; layer < 32; layer++)
            {
                if (Physics.GetIgnoreLayerCollision(sphere.gameObject.layer, layer))
                    collisionMask &= ~(1 << layer);
            }
        }
        else
        {
            collisionMask = Physics.DefaultRaycastLayers;
        }

        foreach (Collider projectileCollider in GetComponentsInChildren<Collider>(true))
            projectileCollider.enabled = false;
    }

    private void IgnoreAdjacentWalls(Vector2Int originCell)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                IgnoreWall(originCell + new Vector2Int(dx, dy));
            }
        }
    }

    private void IgnoreWall(Vector2Int wallCell)
    {
        if (
            GameLoop.Instance == null
            || !GameLoop.Instance.TryGetWallInstance(wallCell, out GameObject wall)
        )
        {
            return;
        }

        Collider[] wallColliders = wall.GetComponentsInChildren<Collider>();
        foreach (Collider wallCollider in wallColliders)
            ignoredColliders.Add(wallCollider);
    }

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
        Move(Time.deltaTime);
    }

    private void Move(float deltaTime)
    {
        Vector3 displacement = launchVelocity * deltaTime;
        float distance = displacement.magnitude;
        if (distance <= Mathf.Epsilon)
            return;

        Physics.SyncTransforms();

        Vector3 direction = displacement / distance;
        RaycastHit[] hits = collisionRadius > 0f
            ? Physics.SphereCastAll(
                transform.position + collisionCenterOffset,
                collisionRadius,
                direction,
                distance,
                collisionMask,
                QueryTriggerInteraction.Ignore
            )
            : Physics.RaycastAll(
                transform.position,
                direction,
                distance,
                collisionMask,
                QueryTriggerInteraction.Ignore
            );

        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || ignoredColliders.Contains(hit.collider))
                continue;

            GameObject hitObject = hit.collider.gameObject;
            if (pierces && hitObject.CompareTag(enemyTeam) && hitTargets.Contains(hitObject))
                continue;

            transform.position += direction * hit.distance;
            ResolveImpact(hitObject, hit.point);
            return;
        }

        transform.position += displacement;
    }

    private void ResolveImpact(GameObject hitObject, Vector3 impactPosition)
    {
        bool isEnemy = hitObject.CompareTag(enemyTeam);
        if (isAuthoritative && ShieldRush.IsShieldLayer(hitObject.layer))
            GameLoop.Instance?.ReportShieldBlockedDamage(
                hitObject.GetComponentInParent<Unit>()?.gameObject,
                impactPosition
            );
        bool isFirstEnemyHit = isEnemy && hitTargets.Add(hitObject);

        if (explodesOnImpact)
        {
            if (
                isAuthoritative
                && isFirstEnemyHit
                && !IsPathBlockedBySmoke(hitObject.transform.position)
            )
            {
                ApplyDirectHitDamage(hitObject);
            }

            Blast.Explode(
                impactPosition,
                aoeRadius,
                enemyTeam,
                ShieldRush.GetEnemyShieldMask(GetShooterTeamIndex(enemyTeam)),
                damage,
                () => PlayImpactExplosion(impactPosition),
                authoritative: isAuthoritative && !IsPathBlockedBySmoke(impactPosition),
                alreadyHit: hitTargets
            );

            Destroy(gameObject);
            return;
        }

        if (isEnemy)
        {
            if (
                isAuthoritative
                && !IsPathBlockedBySmoke(hitObject.transform.position)
                && isFirstEnemyHit
            )
            {
                ApplyDirectHitDamage(hitObject);
            }

            if (pierces)
                return;
        }

        Destroy(gameObject);
    }

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
        bool crit = IsCrit(hitObject);
        float finalDamage = crit ? damage * backstabMultiplier : damage;
        hitObject.GetComponent<Health>()?.TakeDamage(finalDamage, crit);
    }

    /// <summary>
    /// Whether this hit earned the shooter's multiplier. A rear hit from a weapon whose multiplier
    /// is one deals exactly the damage a frontal one would, so it is an ordinary hit and must not
    /// announce itself as anything else.
    /// </summary>
    private bool IsCrit(GameObject hitObject)
    {
        return backstabMultiplier > 1f && CheckBackstab(hitObject);
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
