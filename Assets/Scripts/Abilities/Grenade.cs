using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public partial class Grenade : Ability
{
    float abilityTime = 1;

    // How high the throw rises over the midpoint of its flight.
    private const float ArcApexHeight = 2.5f;

    [SerializeField]
    private float damage = 80f;
    public GameObject grenadePrefab;
    public GameObject grenadeExplosionPrefab;

    public override AbilityPathKind BuildPlannedPath(
        Vector3 targetSquare,
        UnitData data,
        List<Vector3> points
    )
    {
        return AbilityTrajectory.BuildLob(
            transform.position,
            GetThrowTarget(targetSquare),
            ArcApexHeight,
            points
        )
            ? AbilityPathKind.Lob
            : AbilityPathKind.None;
    }

    /// <summary>Where the grenade comes to rest: the target square at the thrower's own height.</summary>
    private Vector3 GetThrowTarget(Vector3 abilitySquare)
    {
        return abilitySquare + Helper.heightOffset(transform);
    }

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 3)
    {
        // Instantiate the grenade at the current position
        GameObject grenade = NetworkHelper.Spawn(
            grenadePrefab,
            transform.position,
            Quaternion.identity
        );
        Vector3 startPosition = grenade.transform.position;
        Vector3 targetPosition = GetThrowTarget(abilitySquare);

        float elapsed = 0f;

        while (elapsed < abilityTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / abilityTime;

            grenade.transform.position = AbilityTrajectory.SampleLob(
                startPosition,
                targetPosition,
                ArcApexHeight,
                progress
            );

            yield return null; // Wait for next frame
        }
        grenade.transform.position = targetPosition;

        // Explode and damage enemies
        CameraEffects.Instance.CameraShakeClientRpc();
        ExplosionFxClientRpc(targetPosition, AreaRadius);
        ExplodeGrenade(targetPosition, AreaRadius);
        GameObject explosionEffect = NetworkHelper.Spawn(
            grenadeExplosionPrefab,
            targetPosition,
            Quaternion.identity
        );
        NetworkHelper.Instance.Despawn(
            explosionEffect,
            explosionEffect.GetComponent<ParticleSystem>().main.duration
        );
        NetworkHelper.Instance.Despawn(grenade);
    }

    [ClientRpc]
    private void ExplosionFxClientRpc(Vector3 explosionPosition, float areaRadius)
    {
        // Alarm-yellow shockwave matching the damage radius; runs on host too (host is a client).
        ImpactShockwave.Spawn(
            explosionPosition,
            new Color(1f, 0.77f, 0f),
            areaRadius * GameLoop.cellSize,
            0.55f
        );
    }

    private void ExplodeGrenade(Vector3 explosionPosition, float AreaRadius)
    {
        string enemyTeam = GameLoop.GetEnemyTeam(gameObject.tag);

        // Units move by Transform during execution. Sync before the overlap query so a completed
        // dodge is evaluated at its current position even when no physics tick ran this frame.
        Physics.SyncTransforms();

        // Find all enemies within explosion range
        Collider[] enemiesInRange = Physics.OverlapSphere(
            explosionPosition,
            AreaRadius * GameLoop.cellSize,
            LayerMask.GetMask(enemyTeam)
        );

        foreach (Collider enemy in enemiesInRange)
        {
            // Check line of sight from explosion to enemy
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
                // If raycast hits an obstacle before reaching the enemy, skip damage
                if (hit.collider != enemy)
                    continue;
            }

            enemy.transform.GetComponent<Health>()?.TakeDamage(damage);
        }
    }
}
