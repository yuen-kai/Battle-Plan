using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BunkerBuster : Ability
{
    private const float RocketSpeedCellsPerSecond = 2.5f;

    private const float RocketRangeCells = 8f;

    private const float RecoverySeconds = 1.4f;

    private const float FlightHeightCells = 0.45f;

    [SerializeField]
    private float damage = 75f;

    private GameObject activeRocket;

    public GameObject rocketPrefab;
    public GameObject rocketExplosionPrefab;

    public override AbilityPathKind BuildPlannedPath(
        Vector3 targetSquare,
        UnitData data,
        List<Vector3> points
    )
    {
        return AbilityTrajectory.BuildGroundRun(
            transform.position,
            ResolvePlannedImpactPoint(targetSquare),
            points
        )
            ? AbilityPathKind.Ground
            : AbilityPathKind.None;
    }

    public Vector3 ResolvePlannedImpactPoint(Vector3 abilitySquare)
    {
        Vector3 launchPoint = GetLaunchPoint();
        Vector3 aimPoint = new(abilitySquare.x, launchPoint.y, abilitySquare.z);

        Vector3 toTarget = aimPoint - launchPoint;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= Mathf.Epsilon)
            return aimPoint;

        Vector3 direction = toTarget.normalized;

        float travel = RocketRangeCells * GameLoop.cellSize;

        if (
            Physics.Raycast(
                launchPoint,
                direction,
                out RaycastHit hit,
                travel,
                BlockingLayerMask(),
                QueryTriggerInteraction.Ignore
            )
        )
        {
            return hit.point;
        }

        return launchPoint + direction * travel;
    }

    private int BlockingLayerMask()
    {
        int walls = LayerMask.GetMask("Walls");

        Unit identity = GetComponent<Unit>();
        if (identity == null || identity.TeamIndex < 0)
            return walls;

        string enemyTeam = GameLoop.GetTeamName(GameLoop.GetEnemyTeamIndex(identity.TeamIndex));
        if (string.IsNullOrEmpty(enemyTeam))
            return walls;

        return walls | LayerMask.GetMask(enemyTeam);
    }

    private Vector3 GetLaunchPoint()
    {
        Vector3 position = transform.position;
        return new Vector3(position.x, FlightHeightCells * GameLoop.cellSize, position.z);
    }

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 1.5f)
    {
        if (!IsServer)
            yield break;

        Vector3 launchPoint = GetLaunchPoint();
        Vector3 aimDirection = abilitySquare - launchPoint;
        aimDirection.y = 0f;
        if (aimDirection.sqrMagnitude <= Mathf.Epsilon)
            yield break;

        Vector3 direction = aimDirection.normalized;

        Movement movement = GetComponent<Movement>();
        UnitData data = movement != null ? movement.unitData : null;

        BeginInterruptibleAbilityAction();

        if (movement != null && data != null)
        {
            yield return movement.RotateToFaceTarget(
                transform.position + direction * GameLoop.cellSize,
                data.rotationSpeed
            );
        }

        activeRocket = NetworkHelper.Spawn(
            rocketPrefab,
            launchPoint,
            Quaternion.LookRotation(direction, Vector3.up)
        );

        Vector3 impactPoint = launchPoint;
        float remainingDistance = RocketRangeCells * GameLoop.cellSize;
        float speed = RocketSpeedCellsPerSecond * GameLoop.cellSize;
        int blockingLayers = BlockingLayerMask();

        while (remainingDistance > Mathf.Epsilon)
        {
            if (activeRocket == null)
                break;

            float stepDistance = Mathf.Min(speed * Time.deltaTime, remainingDistance);
            if (stepDistance <= Mathf.Epsilon)
            {
                yield return null;
                continue;
            }

            Physics.SyncTransforms();
            if (
                Physics.Raycast(
                    impactPoint,
                    direction,
                    out RaycastHit hit,
                    stepDistance,
                    blockingLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                impactPoint = hit.point;
                activeRocket.transform.position = impactPoint;
                break;
            }

            impactPoint += direction * stepDistance;
            remainingDistance -= stepDistance;
            activeRocket.transform.position = impactPoint;
            yield return null;
        }

        CameraEffects.Instance?.CameraShakeClientRpc(0.7f, 0.18f);
        ExplosionFxClientRpc(impactPoint, AreaRadius);
        DetonateRocket(impactPoint, AreaRadius);
        DestroyWallsInBlast(impactPoint, AreaRadius);

        if (rocketExplosionPrefab != null)
        {
            GameObject explosionEffect = NetworkHelper.Spawn(
                rocketExplosionPrefab,
                impactPoint,
                Quaternion.identity
            );
            ParticleSystem particles = explosionEffect.GetComponent<ParticleSystem>();
            NetworkHelper.Instance.Despawn(
                explosionEffect,
                particles != null ? particles.main.duration : 1f
            );
        }
        DespawnActiveRocket();

        yield return new WaitForSeconds(RecoverySeconds);
        CompleteInterruptibleAbilityAction();
    }

    protected override void OnAbilityInterrupted()
    {
        DespawnActiveRocket();
    }

    private void DespawnActiveRocket()
    {
        if (activeRocket == null)
            return;

        if (NetworkHelper.Instance != null)
            NetworkHelper.Instance.Despawn(activeRocket);
        else
            Destroy(activeRocket);
        activeRocket = null;
    }

    [ClientRpc]
    private void ExplosionFxClientRpc(Vector3 explosionPosition, float areaRadius)
    {
        float blast = areaRadius * GameLoop.cellSize;
        Color scorch = new(1f, 0.35f, 0.05f);

        ImpactCore.Spawn(explosionPosition, AbilityJuice.Hot(scorch, 2f), blast * 0.5f, 1.6f);
        ImpactShockwave.Spawn(explosionPosition, scorch, blast, 0.7f);

        DebrisBurst.Spawn(explosionPosition, scorch, blast * 0.65f, 32);
        Aftermath.Spawn(explosionPosition, scorch, blast * 0.6f, AftermathKind.Scorch);
    }

    private void DetonateRocket(Vector3 explosionPosition, float AreaRadius)
    {
        string enemyTeam = GameLoop.GetEnemyTeam(gameObject.tag);

        Physics.SyncTransforms();

        Collider[] enemiesInRange = Physics.OverlapSphere(
            explosionPosition,
            AreaRadius * GameLoop.cellSize,
            LayerMask.GetMask(enemyTeam)
        );

        foreach (Collider enemy in enemiesInRange)
        {
            Vector3 directionToEnemy = (enemy.transform.position - explosionPosition).normalized;
            float distanceToEnemy = Vector3.Distance(explosionPosition, enemy.transform.position);
            if (
                Physics.Raycast(
                    explosionPosition,
                    directionToEnemy,
                    out RaycastHit hit,
                    distanceToEnemy,
                    LayerMask.GetMask("Walls", enemyTeam)
                )
            )
            {
                if (hit.collider != enemy)
                    continue;
            }

            enemy.transform.GetComponent<Health>()?.TakeDamage(damage);
        }
    }

    private void DestroyWallsInBlast(Vector3 explosionPosition, float areaRadius)
    {
        Vector2Int impactCell = GridSystem.ConvertToGridCoords(explosionPosition);
        foreach (
            Vector2Int cell in GetWallCellsWithinRadius(
                impactCell,
                areaRadius,
                GameLoop.wallLayout
            )
        )
        {
            GameLoop.Instance.TryDestroyWallCell(cell);
        }
    }

    public static List<Vector2Int> GetWallCellsWithinRadius(
        Vector2Int center,
        float radiusCells,
        IEnumerable<Vector2Int> wallCells
    )
    {
        List<Vector2Int> hits = new();
        if (wallCells == null || radiusCells < 0f)
            return hits;

        float radiusSquared = radiusCells * radiusCells;
        int scanExtent = Mathf.CeilToInt(radiusCells);

        foreach (Vector2Int cell in wallCells)
        {
            int dx = cell.x - center.x;
            int dy = cell.y - center.y;
            if (Mathf.Abs(dx) > scanExtent || Mathf.Abs(dy) > scanExtent)
                continue;
            if (dx * dx + dy * dy <= radiusSquared)
                hits.Add(cell);
        }
        return hits;
    }
}
