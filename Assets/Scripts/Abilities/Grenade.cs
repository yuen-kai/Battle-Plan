using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public partial class Grenade : Ability
{
    float abilityTime = 1;

    // How high the throw rises over the midpoint of its flight.
    private const float ArcApexHeight = 2.5f;

    // Just under a turn and a half across the one-second flight. Enough that the lever comes round
    // more than once so the tumble is unmistakable, slow enough that the silhouette is still
    // readable on any frame the player happens to look at.
    private const float TumbleDegrees = 520f;

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

        // Thrown, not carried. The model has a lever and a top now, and something holding one
        // attitude the whole way across the board reads as a prop sliding along a curve rather
        // than an object that left a hand. It turns end over end about the axis across its own
        // flight, which is the one tumble a thrown thing gets for free.
        Vector3 flight = targetPosition - startPosition;
        flight.y = 0f;
        Vector3 tumbleAxis =
            flight.sqrMagnitude > 0.0001f
                ? Vector3.Cross(Vector3.up, flight.normalized)
                : Vector3.right;
        Quaternion thrownFrom = grenade.transform.rotation;

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
            grenade.transform.rotation =
                Quaternion.AngleAxis(progress * TumbleDegrees, tumbleAxis) * thrownFrom;

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
        float blast = areaRadius * GameLoop.cellSize;
        Color alarm = new(1f, 0.77f, 0f);

        // Alarm-yellow shockwave matching the damage radius; runs on host too (host is a client).
        ImpactShockwave.Spawn(explosionPosition, alarm, blast, 0.55f);

        // The displaced ground, which the loudest event in the game did not have. The burst brings
        // charred chunks, a scorch multiplied into the deck and coals cooling in it — all of which
        // a Pogo landing used to own, and none of which a jump that takes nobody's health had any
        // business leaving on the floor.
        //
        // Thrown a fraction of the blast rather than the whole of it. The shockwave already states
        // the damage radius, and pieces travelling that far stop reading as ground coming out of a
        // hole and start reading as a second, wider event.
        DebrisBurst.Spawn(explosionPosition, alarm, blast * 0.55f, 24);
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
