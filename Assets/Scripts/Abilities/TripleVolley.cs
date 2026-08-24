using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class TripleVolley : Ability
{
    private const float FanSpreadDegrees = 12f;
    private const float RecoverySeconds = 1.1f;
    private const float VolleyVisualLocalWidth = 0.6f;
    private const float VolleyVisualLocalLength = 3.5f;

    public const float VolleyDamage = 40f;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        if (!IsServer)
            yield break;

        Movement movement = GetComponent<Movement>();
        Shooting shooting = GetComponent<Shooting>();
        UnitData data = movement != null ? movement.unitData : null;
        if (movement == null || shooting == null || data == null)
            yield break;

        Vector2Int casterCell = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(gameObject)
        );
        if (
            !GridSystem.TryGetAdjacentDirection(
                casterCell,
                GridSystem.ConvertToGridCoords(abilitySquare),
                out Vector2Int direction
            )
        )
        {
            Debug.LogWarning(
                $"[TripleVolley] {name} could not resolve a volley direction from {abilitySquare} "
                    + $"(caster at {casterCell}); aborting the ability. Check this unit's UnitData "
                    + "has selectAbilityDirection and abilityFixedDistance set."
            );
            yield break;
        }

        BeginInterruptibleAbilityAction();

        Vector3 direction3D = new(direction.x, 0f, direction.y);
        Vector3 facingTarget = transform.position + direction3D * GameLoop.cellSize;
        yield return movement.RotateToFaceTarget(facingTarget, data.rotationSpeed);

        Vector3[] fanDirections = ComputeFanDirections(direction3D);
        Vector3 origin = transform.position;
        foreach (Vector3 fanDirection in fanDirections)
            CreateVolleyProjectile(origin, fanDirection, data, authoritative: true);

        FireVolleyClientRpc(origin, fanDirections[0], fanDirections[1], fanDirections[2]);
        GetComponent<AnimationHandler>()?.TriggerAnimation("Shoot");

        yield return new WaitForSeconds(RecoverySeconds);
        CompleteInterruptibleAbilityAction();
    }

    [ClientRpc]
    private void FireVolleyClientRpc(
        Vector3 origin,
        Vector3 leftDirection,
        Vector3 centerDirection,
        Vector3 rightDirection
    )
    {
        if (IsServer)
            return;

        Movement movement = GetComponent<Movement>();
        UnitData data = movement != null ? movement.unitData : null;
        if (data == null)
            return;

        CreateVolleyProjectile(origin, leftDirection, data, authoritative: false);
        CreateVolleyProjectile(origin, centerDirection, data, authoritative: false);
        CreateVolleyProjectile(origin, rightDirection, data, authoritative: false);
        GetComponent<AnimationHandler>()?.TriggerAnimation("Shoot");
    }

    private void CreateVolleyProjectile(
        Vector3 origin,
        Vector3 direction,
        UnitData data,
        bool authoritative
    )
    {
        GameObject projectilePrefab = ResolveProjectilePrefab(data);
        if (projectilePrefab == null)
        {
            Debug.LogWarning($"[TripleVolley] {name} has no projectile prefab for its team.");
            return;
        }

        GameObject projectile = Instantiate(
            projectilePrefab,
            origin,
            Quaternion.LookRotation(direction)
        );
        ApplyVolleyVisualDimensions(projectile);

        Bullet bullet = projectile.GetComponent<Bullet>();
        if (bullet == null)
        {
            Debug.LogWarning(
                $"[TripleVolley] Projectile prefab {projectilePrefab.name} has no Bullet component."
            );
            Destroy(projectile);
            return;
        }

        int teamIndex = GetComponent<Unit>()?.TeamIndex ?? -1;
        string enemyTeam = GameLoop.GetTeamName(GameLoop.GetEnemyTeamIndex(teamIndex));
        bullet.Initialize(
            direction * data.bulletSpeed * GameLoop.cellSize,
            VolleyDamage,
            data.backstabMultiplier,
            data.bulletRange * GameLoop.cellSize,
            data.backstabAngle,
            enemyTeam,
            authoritative,
            shotPierces: true
        );
    }

    private GameObject ResolveProjectilePrefab(UnitData data)
    {
        return GetComponent<Unit>()?.TeamIndex == GameLoop.HostTeamIndex
            ? data.blueBulletPrefab
            : data.redBulletPrefab;
    }

    private static void ApplyVolleyVisualDimensions(GameObject projectile)
    {
        Renderer visual = projectile.GetComponentInChildren<Renderer>();
        if (visual == null)
            return;

        // Enlarge only the renderer child so the ability reads as heavy without silently widening
        // the ordinary arrow collider or changing Farsight's basic shots.
        visual.transform.localScale = new Vector3(
            VolleyVisualLocalWidth,
            VolleyVisualLocalLength,
            VolleyVisualLocalWidth
        );
    }

    public static Vector3[] ComputeFanDirections(Vector3 baseDirection)
    {
        Vector3 flatDirection = new(baseDirection.x, 0f, baseDirection.z);
        if (flatDirection.sqrMagnitude <= Mathf.Epsilon)
            return new[] { Vector3.zero, Vector3.zero, Vector3.zero };

        flatDirection.Normalize();
        return new[]
        {
            Quaternion.AngleAxis(-FanSpreadDegrees, Vector3.up) * flatDirection,
            flatDirection,
            Quaternion.AngleAxis(FanSpreadDegrees, Vector3.up) * flatDirection,
        };
    }
}
