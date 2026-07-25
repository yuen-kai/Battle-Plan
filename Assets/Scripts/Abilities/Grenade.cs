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

        ThrowFxClientRpc(startPosition, targetPosition, abilityTime);

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

        // Explode and damage enemies. The shake now rides inside ExplosionFxClientRpc, which
        // already reaches every peer, rather than costing a second broadcast of its own.
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

    /// <summary>
    /// The toss and the fuse. The toss sits on the thrower and is fog-gated like any other unit
    /// action; the fuse sits on the landing square and is deliberately audible through fog, because
    /// the grenade's blink telegraph is already shown to both players as counterplay — the sound
    /// leaks nothing the screen is not already showing, and it is the only warning a player outside
    /// the blast has. Both are client-local presentation: no simulation state is touched here.
    /// </summary>
    [ClientRpc]
    private void ThrowFxClientRpc(Vector3 fromPosition, Vector3 landingPosition, float flightTime)
    {
        BattlePlanAudio.PlayAt(AudioCueId.GrenadeThrow, fromPosition, gameObject);

        // Anticipation (§9.3): the landing square wears the exact blast footprint for the whole
        // arc, pulsing faster as the fuse runs down. Deliberately shown to both players — it is
        // the dodge counterplay, which is also why it is never a frame wider than the rules.
        AbilityFX.GrenadeTelegraph(landingPosition, flightTime);

        // The fuse clip is authored at one second. Scaling its rate by the actual flight time keeps
        // the accelerating tick landing on the detonation rather than drifting off it if the throw
        // duration is ever retuned.
        BattlePlanAudio.PlayAt(
            AudioCueId.GrenadeFuse,
            landingPosition,
            null,
            1f,
            1f / Mathf.Max(0.1f, flightTime)
        );
    }

    [ClientRpc]
    private void ExplosionFxClientRpc(Vector3 explosionPosition, float areaRadius)
    {
        // Impact frame and aftermath (§9.3); runs on host too, because the host is a client. The
        // ring is sized off the same AreaRadius the damage query used, so it cannot drift from it.
        AbilityFX.GrenadeDetonation(explosionPosition, areaRadius);
        BattlePlanAudio.PlayAt(AudioCueId.GrenadeExplode, explosionPosition);
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
