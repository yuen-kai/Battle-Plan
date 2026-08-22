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
        bool ignoresWallCell = false,
        Vector2Int ignoredWallCell = default
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

        if (ignoresWallCell)
            IgnoreWall(ignoredWallCell);

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
        bool isFirstEnemyHit = isEnemy && hitTargets.Add(hitObject);

        if (explodesOnImpact)
        {
            if (isAuthoritative)
            {
                if (
                    isFirstEnemyHit
                    && !IsPathBlockedBySmoke(hitObject.transform.position)
                )
                {
                    ApplyDirectHitDamage(hitObject);
                }
                ResolveAreaImpact(impactPosition);
            }

            PlayImpactExplosion(impactPosition);

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
        float finalDamage = !CheckBackstab(hitObject) ? damage : damage * backstabMultiplier;
        hitObject.GetComponent<Health>()?.TakeDamage(finalDamage);
    }

    private void ResolveAreaImpact(Vector3 impactPosition)
    {
        if (aoeRadius <= 0f || IsPathBlockedBySmoke(impactPosition))
            return;

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
                continue;
            }

            enemyObject.GetComponent<Health>()?.TakeDamage(damage);
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
