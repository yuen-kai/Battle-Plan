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

    // The rocket detonates on the first wall in its path and the blast takes that wall down with
    // it, which is what this ability is named for — so a blocked shot is never a wasted one, and
    // the aim only wants a clear line where it can have one for free.
    public override AbilityLineOfFire LineOfFire => AbilityLineOfFire.Preferred;

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

        walls |= ShieldRush.GetEnemyShieldMask(identity.TeamIndex);

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
                activeRocket.transform.position = impactPoint;
                break;
            }

            impactPoint += direction * stepDistance;
            remainingDistance -= stepDistance;
            activeRocket.transform.position = impactPoint;
            yield return null;
        }

        Blast.Explode(
            impactPoint,
            AreaRadius * GameLoop.cellSize,
            GameLoop.GetEnemyTeam(gameObject.tag),
            ShieldRush.GetEnemyShieldMask(gameObject),
            damage,
            () =>
            {
                CameraEffects.Instance?.CameraShakeClientRpc(0.7f, 0.18f);
                ExplosionFxClientRpc(impactPoint, AreaRadius);
                if (rocketExplosionPrefab == null)
                    return;

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
            },
            destroysWalls: true
        );
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
}
