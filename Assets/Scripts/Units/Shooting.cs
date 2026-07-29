using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages combat mechanics for units including enemy detection, targeting systems, ammunition management, and projectile firing.
/// Features target acquisition with line-of-sight validation, visual targeting laser with animated lock-on sequence,
/// automatic reloading cycles, and configurable bullet properties such as spread, damage, and backstab mechanics.
/// Supports pause/resume functionality for tactical control and maintains bullet lifecycle management.
/// </summary>
public class Shooting : NetworkBehaviour
{
    public UnitData unitData;

    private string enemyTeam;

    private TargetLaserVisual targetLaser;

    // One replicated value written once per lock, rather than five written every frame. The beam's
    // geometry follows both units' live transforms and its ramp is a pure function of elapsed
    // server time, so each peer can animate the whole thing from the instant the lock began.
    // A NetworkVariable rather than an RPC because object-scoped RPCs are dropped while a unit is
    // NetworkHidden for fog, whereas a variable resyncs on NetworkShow.
    private readonly NetworkVariable<TargetLockState> targetLock = new();

    private List<GameObject> bullets = new();
    private int currentAmmo;

    [HideInInspector]
    public bool allowShooting = true; // Controls whether the unit can start a new shooting cycle

    [HideInInspector]
    public bool stillShooting = true;

    private Coroutine shootingCoroutine;

    // CONTROLLER
    // All setup is in OnNetworkSpawn (not Start) so a fog NetworkShow re-runs it and the current
    // lock state is applied to a freshly (re)created beam.
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        EnsureTargetLaser();

        targetLock.OnValueChanged += OnTargetLockChanged;
        targetLaser.SetLock(targetLock.Value);

        if (!IsServer)
        {
            enabled = false;
            return;
        }

        GameLoop.OrderAllowShooting += SetAllowShooting;
        GameLoop.OrderStillShooting += SetStillShooting;
        GameLoop.OrderContinueShooting += ContinueShooting;
        // enemyTeam is resolved lazily (ResolveEnemyTeam): at spawn time GameLoop has not yet
        // assigned this unit's team tag (tags are set right after NetworkHelper.Spawn returns).
    }

    public override void OnNetworkDespawn()
    {
        targetLock.OnValueChanged -= OnTargetLockChanged;

        if (IsServer)
        {
            GameLoop.OrderAllowShooting -= SetAllowShooting;
            GameLoop.OrderStillShooting -= SetStillShooting;
            GameLoop.OrderContinueShooting -= ContinueShooting;
        }

        base.OnNetworkDespawn();
    }

    private void EnsureTargetLaser()
    {
        if (targetLaser != null)
            return;

        targetLaser = GetComponent<TargetLaserVisual>();
        if (targetLaser == null)
            targetLaser = gameObject.AddComponent<TargetLaserVisual>();
    }

    private void SetAllowShooting(bool toggle)
    {
        allowShooting = toggle;
    }

    private void SetStillShooting(bool toggle)
    {
        stillShooting = toggle;
    }

    private void OnTargetLockChanged(TargetLockState previousValue, TargetLockState newValue)
    {
        EnsureTargetLaser();
        targetLaser.SetLock(newValue);
    }

    /// <summary>
    /// Publishes a lock once, when it is acquired. Re-locking the same target is a no-op so the
    /// value stays clean of per-frame writes; a new target replaces it and restarts the ramp.
    /// </summary>
    private void BeginTargetLock(GameObject target, float duration)
    {
        if (!IsServer)
            return;

        NetworkObject targetObject = target != null ? target.GetComponent<NetworkObject>() : null;
        if (targetObject == null || duration <= 0f)
        {
            ClearTargetLock();
            return;
        }

        if (
            targetLock.Value.Active
            && targetLock.Value.TargetObjectId == targetObject.NetworkObjectId
        )
        {
            return;
        }

        targetLock.Value = new TargetLockState(
            targetObject.NetworkObjectId,
            NetworkManager.ServerTime.Time,
            duration
        );
    }

    private void ClearTargetLock()
    {
        if (IsServer && targetLock.Value.Active)
            targetLock.Value = default;
    }

    /// <summary>
    /// A target-lock laser must be dodgeable/readable: reveal this shooter to the victim's
    /// client for the lock duration (plus a short grace) so the beam replicates and renders.
    /// </summary>
    private void ForceRevealForTargetLock(GameObject target)
    {
        if (!IsServer || target == null || unitData.targetLockDuration <= 0f)
            return;

        Unit targetIdentity = target.GetComponent<Unit>();
        if (targetIdentity == null || GameLoop.Instance == null)
            return;

        GameLoop.Instance.ForceRevealToTeam(
            gameObject,
            targetIdentity.TeamIndex,
            unitData.targetLockDuration + 0.5f
        );
    }

    public void PauseShooting()
    {
        if (shootingCoroutine != null)
            StopCoroutine(shootingCoroutine);
        ClearTargetLock();

        allowShooting = true;
        stillShooting = true;
    }

    public void StartShooting()
    {
        PauseShooting();
        shootingCoroutine = StartCoroutine(InitiateShooting());
    }

    public void ContinueShooting()
    {
        allowShooting = true; //reallow shooting
        if (stillShooting == false) //restart shooting if not already shooting
        {
            stillShooting = true;
            shootingCoroutine = StartCoroutine(InitiateShooting());
        }
    }

    private IEnumerator RotateToFaceTarget(GameObject target)
    {
        yield return StartCoroutine(
            transform
                .GetComponent<Movement>()
                .RotateToFaceTarget(target.transform.position, unitData.rotationSpeed)
        );
    }

    // SHOOTING
    public IEnumerator InitiateShooting()
    {
        if (GetComponent<AnimationHandler>() != null)
        {
            GetComponent<AnimationHandler>().PlayAnimation("Aiming");
        }

        currentAmmo = unitData.magazineSize;

        float remainingTargetLockTime = unitData.targetLockDuration;

        while (allowShooting)
        {
            GameObject target = FindNearestEnemy();
            if (target)
            {
                yield return StartCoroutine(RotateToFaceTarget(target));
                ForceRevealForTargetLock(target);
            }

            remainingTargetLockTime = unitData.targetLockDuration;

            while (currentAmmo > 0)
            {
                //Refind target
                if (target == null || !lineOfSight(target))
                {
                    ClearTargetLock();
                    target = FindNearestEnemy();
                    if (target)
                    {
                        remainingTargetLockTime = unitData.targetLockDuration;
                        yield return StartCoroutine(RotateToFaceTarget(target));
                        ForceRevealForTargetLock(target);
                    }
                    if (!allowShooting)
                        break;

                    yield return null;
                    continue;
                }

                //Target lock
                if (remainingTargetLockTime > 0f)
                {
                    transform.rotation = Quaternion.LookRotation(
                        (target.transform.position - transform.position).normalized
                    ); // Track target

                    BeginTargetLock(target, unitData.targetLockDuration);

                    remainingTargetLockTime -= Time.deltaTime;

                    yield return null;
                    continue;
                }
                ClearTargetLock();

                transform.rotation = Quaternion.LookRotation(
                    (target.transform.position - transform.position).normalized
                ); // Track target
                FireBullet();

                yield return new WaitForSeconds(unitData.timeBetweenShots);
            }

            if (allowShooting)
            {
                yield return StartCoroutine(Reload());
            }
        }

        if (GetComponent<AnimationHandler>() != null)
        {
            GetComponent<AnimationHandler>().PlayAnimation("Idle");
        }

        // Wait for all bullets to be destroyed
        while (GetActiveBulletCount() > 0)
        {
            yield return null;
        }

        yield return new WaitForSeconds(0.1f); // Small delay to ensure player deaths are processed
        stillShooting = false;
    }

    public void FireBullet(
        float spread = -1,
        float bulletSpeed = -1,
        float damage = -1,
        float backstabMultiplier = -1,
        float range = -1,
        float backstabAngle = -1,
        GameObject bulletPrefab = null
    )
    {
        spread = spread == -1 ? unitData.bulletSpread : spread;
        bulletSpeed = bulletSpeed == -1 ? unitData.bulletSpeed : bulletSpeed;
        damage = damage == -1 ? unitData.damage : damage;
        backstabMultiplier =
            backstabMultiplier == -1 ? unitData.backstabMultiplier : backstabMultiplier;
        range = range == -1 ? unitData.bulletRange : range;
        backstabAngle = backstabAngle == -1 ? unitData.backstabAngle : backstabAngle;
        bulletPrefab = bulletPrefab ?? ResolveBulletPrefab();

        // Fire bullet with spread
        Vector3 baseDirection = transform.forward;
        float spreadAngle = Random.Range(-spread, spread);
        Vector3 shootDirection = Quaternion.AngleAxis(spreadAngle, transform.up) * baseDirection;

        Vector3 origin = transform.position;
        bullets.Add(
            CreateBullet(
                bulletPrefab,
                origin,
                shootDirection,
                bulletSpeed,
                damage,
                backstabMultiplier,
                range,
                backstabAngle,
                authoritative: true
            )
        );

        // Clients need the tracer, not the arithmetic: damage is resolved on the authoritative copy
        // above, and spread is already folded into the direction so every peer draws the same shot.
        FireBulletClientRpc(origin, shootDirection, bulletSpeed);

        currentAmmo--;

        if (GetComponent<AnimationHandler>() != null)
        {
            GetComponent<AnimationHandler>().TriggerAnimation("Shoot");
        }
    }

    /// <summary>
    /// Draws the tracer on every other peer. Object-scoped, so a shot from a unit this client
    /// cannot see stays invisible to it, which is what fog already implies but network-spawned
    /// bullets never honoured.
    /// </summary>
    [ClientRpc]
    private void FireBulletClientRpc(Vector3 origin, Vector3 direction, float bulletSpeed)
    {
        if (IsServer)
            return; // the authoritative copy is already in flight

        CreateBullet(
            ResolveBulletPrefab(),
            origin,
            direction,
            bulletSpeed,
            unitData.damage,
            unitData.backstabMultiplier,
            unitData.bulletRange,
            unitData.backstabAngle,
            authoritative: false
        );
    }

    private GameObject CreateBullet(
        GameObject bulletPrefab,
        Vector3 origin,
        Vector3 direction,
        float bulletSpeed,
        float damage,
        float backstabMultiplier,
        float range,
        float backstabAngle,
        bool authoritative
    )
    {
        GameObject bullet = Instantiate(bulletPrefab, origin, Quaternion.LookRotation(direction));
        bullet
            .GetComponent<Bullet>()
            .Initialize(
                direction * bulletSpeed * GameLoop.cellSize,
                damage,
                backstabMultiplier,
                range * GameLoop.cellSize,
                backstabAngle,
                ResolveEnemyTeam(),
                authoritative
            );
        return bullet;
    }

    private GameObject ResolveBulletPrefab()
    {
        return GetComponent<Unit>()?.TeamIndex == GameLoop.HostTeamIndex
            ? unitData.blueBulletPrefab
            : unitData.redBulletPrefab;
    }

    public static float GetProjectileCollisionRadius(GameObject bulletPrefab)
    {
        SphereCollider collider = bulletPrefab?.GetComponentInChildren<SphereCollider>(
            includeInactive: true
        );
        if (collider == null)
            return 0f;

        Vector3 scale = collider.transform.lossyScale;
        return collider.radius
            * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

    private bool TryProjectileCast(
        Vector3 origin,
        Vector3 direction,
        float distance,
        int layerMask,
        out RaycastHit hit
    )
    {
        if (direction.sqrMagnitude <= Mathf.Epsilon || distance <= 0f)
        {
            hit = default;
            return false;
        }

        direction.Normalize();
        float projectileRadius = GetProjectileCollisionRadius(ResolveBulletPrefab());
        return projectileRadius > 0f
            ? Physics.SphereCast(
                origin,
                projectileRadius,
                direction,
                out hit,
                distance,
                layerMask,
                QueryTriggerInteraction.Ignore
            )
            : Physics.Raycast(
                origin,
                direction,
                out hit,
                distance,
                layerMask,
                QueryTriggerInteraction.Ignore
            );
    }

    private int GetActiveBulletCount()
    {
        bullets.RemoveAll(bullet => bullet == null);
        return bullets.Count;
    }

    IEnumerator Reload()
    {
        yield return new WaitForSeconds(unitData.reloadTime);
        currentAmmo = unitData.magazineSize;
    }

    private string ResolveEnemyTeam()
    {
        // The team tag is assigned by GameLoop right after spawn, which is later than
        // OnNetworkSpawn — so resolve on first use instead of at spawn.
        if (string.IsNullOrEmpty(enemyTeam))
        {
            int ownTeamIndex = GetComponent<Unit>()?.TeamIndex ?? -1;
            enemyTeam = GameLoop.GetTeamName(GameLoop.GetEnemyTeamIndex(ownTeamIndex));
        }
        return enemyTeam;
    }

    GameObject FindNearestEnemy()
    {
        GameObject nearestEnemy = null;
        float nearestDistance = Mathf.Infinity;

        if (IsServer)
        {
            if (string.IsNullOrEmpty(ResolveEnemyTeam()))
                return null;

            // Server: use tags or any authoritative lookup
            GameObject[] enemies = GameObject.FindGameObjectsWithTag(enemyTeam);
            foreach (GameObject enemy in enemies)
            {
                float distance = Vector3.Distance(transform.position, enemy.transform.position);
                if (distance < nearestDistance && lineOfSight(enemy))
                {
                    nearestEnemy = enemy;
                    nearestDistance = distance;
                }
            }
            return nearestEnemy;
        }

        // Client: use ownership-based discovery to avoid tag usage
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
        {
            return null;
        }
        int ownTeamIndex = GetComponent<Unit>()?.TeamIndex ?? -1;
        foreach (var netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null)
                continue;
            Unit candidateIdentity = netObj.GetComponent<Unit>();
            if (
                candidateIdentity == null
                || candidateIdentity.TeamIndex < 0
                || candidateIdentity.TeamIndex == ownTeamIndex
                || netObj.GetComponent<Health>() == null
            )
            {
                continue;
            }

            GameObject enemy = netObj.gameObject;
            float distance = Vector3.Distance(transform.position, enemy.transform.position);
            if (distance < nearestDistance && lineOfSight(enemy))
            {
                nearestEnemy = enemy;
                nearestDistance = distance;
            }
        }
        return nearestEnemy;
    }

    bool lineOfSight(GameObject enemy)
    {
        Unit identity = GetComponent<Unit>();
        if (
            GameLoop.Instance != null
            && identity != null
            && !GameLoop.Instance.CanTeamObserveUnit(identity.TeamIndex, enemy)
        )
        {
            return false;
        }

        if (
            GameLoop.Instance != null
            && GameLoop.Instance.DoesWorldSegmentCrossActiveSmoke(
                transform.position,
                enemy.transform.position
            )
        )
        {
            return false;
        }

        // Check for clear line of sight within range
        Vector3 directionToEnemy = (enemy.transform.position - transform.position).normalized;
        bool hitSomething = TryProjectileCast(
            transform.position,
            directionToEnemy,
            unitData.targetRange * GameLoop.cellSize,
            LayerMask.GetMask("Walls", enemyTeam),
            out RaycastHit hit
        );
        if (
            hitSomething
            && (
                hit.collider.gameObject == enemy
                || hit.collider.transform.IsChildOf(enemy.transform)
            )
        )
        {
            return true;
        }
        return false;
    }
}

/// <summary>
/// A lock-on as replicated: who is being locked, when the lock started on the server clock, and how
/// long it runs. The beam's endpoints come from the two units' live transforms and its ramp is a
/// pure function of elapsed time, so none of that needs replicating and this is written once per
/// lock rather than every frame.
/// </summary>
public struct TargetLockState : INetworkSerializable, System.IEquatable<TargetLockState>
{
    public bool Active;
    public ulong TargetObjectId;
    public double StartServerTime;
    public float Duration;

    public TargetLockState(ulong targetObjectId, double startServerTime, float duration)
    {
        Active = true;
        TargetObjectId = targetObjectId;
        StartServerTime = startServerTime;
        Duration = Mathf.Max(0.0001f, duration);
    }

    /// <summary>Ramp position, 0 at acquisition through 1 when the shot is released.</summary>
    public float ProgressAt(double serverTime)
    {
        return Mathf.Clamp01((float)((serverTime - StartServerTime) / Duration));
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Active);
        serializer.SerializeValue(ref TargetObjectId);
        serializer.SerializeValue(ref StartServerTime);
        serializer.SerializeValue(ref Duration);
    }

    public bool Equals(TargetLockState other)
    {
        return Active == other.Active
            && TargetObjectId == other.TargetObjectId
            && StartServerTime.Equals(other.StartServerTime)
            && Duration.Equals(other.Duration);
    }

    public override bool Equals(object obj)
    {
        return obj is TargetLockState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return System.HashCode.Combine(Active, TargetObjectId, StartServerTime, Duration);
    }
}

/// <summary>
/// Local-only lock-on beam. Every peer animates it from the replicated <see cref="TargetLockState"/>
/// and the live transforms of the two units, which is why the lock itself costs a single replicated
/// write instead of a per-frame stream of endpoints, widths and colours.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public sealed class TargetLaserVisual : MonoBehaviour
{
    private const float StartWidth = 0.05f;
    private const float EndWidth = 0.2f;
    private static readonly Color StartColor = Color.white;
    private static readonly Color EndColor = Color.red;

    private LineRenderer beam;
    private TargetLockState lockState;
    private Transform target;

    private void Awake()
    {
        beam = GetComponent<LineRenderer>();
        beam.positionCount = 2;
        beam.enabled = false;

        // Energy-beam look: white HDR core, glow tinted by the white->red lock-on ramp riding the
        // LineRenderer vertex colors.
        Shader beamShader = Shader.Find("BattlePlan/EnergyBeam");
        if (beamShader != null)
        {
            Material beamMaterial = new(beamShader);
            beamMaterial.SetColor("_GlowColor", Color.white);
            beamMaterial.SetColor("_CoreColor", Color.white * 1.5f);
            beamMaterial.SetFloat("_CoreWidth", 0.35f);
            beamMaterial.SetFloat("_NoiseStrength", 0.2f);
            beam.material = beamMaterial;
        }
    }

    public void SetLock(TargetLockState state)
    {
        lockState = state;
        target = null;
        if (beam != null)
            beam.enabled = state.Active;
    }

    private void LateUpdate()
    {
        if (beam == null || !lockState.Active)
            return;

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || (target == null && !TryResolveTarget(manager)))
        {
            // A target this peer cannot see yet leaves the beam dark rather than drawing to a
            // position fog has not disclosed.
            beam.enabled = false;
            return;
        }

        float progress = lockState.ProgressAt(manager.ServerTime.Time);
        beam.enabled = true;
        beam.SetPosition(0, transform.position);
        beam.SetPosition(1, target.position);
        beam.startWidth = beam.endWidth = Mathf.Lerp(StartWidth, EndWidth, progress);
        beam.startColor = beam.endColor = Color.Lerp(StartColor, EndColor, progress);
    }

    private bool TryResolveTarget(NetworkManager manager)
    {
        if (
            manager.SpawnManager == null
            || !manager.SpawnManager.SpawnedObjects.TryGetValue(
                lockState.TargetObjectId,
                out NetworkObject targetObject
            )
            || targetObject == null
        )
        {
            return false;
        }

        target = targetObject.transform;
        return true;
    }
}
