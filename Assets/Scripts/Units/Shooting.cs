using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Shooting : NetworkBehaviour
{
    public UnitData unitData;

    private string enemyTeam;

    private TargetLaserVisual targetLaser;

    private readonly NetworkVariable<TargetLockState> targetLock = new();

    private List<GameObject> bullets = new();
    private int currentAmmo;
    private int continuousShotsFired;
    private float lastShotTime = float.NegativeInfinity;

    [HideInInspector]
    public bool allowShooting = true;

    [HideInInspector]
    public bool stillShooting = true;

    private Coroutine shootingCoroutine;

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

    public bool holdFire;

    public void StartShooting()
    {
        if (holdFire)
        {
            StandDown();
            return;
        }
        PauseShooting();
        shootingCoroutine = StartCoroutine(InitiateShooting());
    }

    public void StandDown()
    {
        PauseShooting();
        allowShooting = false;
        stillShooting = false;
    }

    public void ContinueShooting()
    {
        if (holdFire)
        {
            StandDown();
            return;
        }

        allowShooting = true;
        if (!stillShooting)
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
                .RotateToFaceTarget(GetHorizontalTargetPosition(target), unitData.rotationSpeed)
        );
    }

    private Vector3 GetHorizontalTargetPosition(GameObject target)
    {
        Vector3 targetPosition = target.transform.position;
        targetPosition.y = transform.position.y;
        return targetPosition;
    }

    private void FaceTargetHorizontally(GameObject target)
    {
        Vector3 direction = GetHorizontalTargetPosition(target) - transform.position;
        if (direction.sqrMagnitude > Mathf.Epsilon)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

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

                if (remainingTargetLockTime > 0f)
                {
                    FaceTargetHorizontally(target);

                    BeginTargetLock(target, unitData.targetLockDuration);

                    remainingTargetLockTime -= Time.deltaTime;

                    yield return null;
                    continue;
                }
                ClearTargetLock();

                FaceTargetHorizontally(target);

                float shotTime = Time.time;
                if (shotTime - lastShotTime >= unitData.fireRateRampResetDelay)
                    continuousShotsFired = 0;

                float shotDelay = ComputeRampedShotDelay(
                    continuousShotsFired,
                    unitData.fireRateRampShots,
                    unitData.fireRateRampStartDelay,
                    unitData.timeBetweenShots
                );

                FireBullet();
                continuousShotsFired++;
                lastShotTime = shotTime;
                yield return new WaitForSeconds(shotDelay);
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

        while (GetActiveBulletCount() > 0)
        {
            yield return null;
        }

        yield return new WaitForSeconds(0.1f);
        stillShooting = false;
    }

    public static float ComputeRampedShotDelay(
        int continuousShotsFired,
        int rampShots,
        float rampStartDelay,
        float floorDelay
    )
    {
        if (rampShots <= 0)
            return floorDelay;

        float progress = Mathf.Clamp01((float)continuousShotsFired / rampShots);
        return Mathf.Lerp(rampStartDelay, floorDelay, progress);
    }

    public void FireBullet()
    {
        float spreadAngle = Random.Range(-unitData.bulletSpread, unitData.bulletSpread);
        Vector3 direction =
            Quaternion.AngleAxis(spreadAngle, transform.up) * transform.forward;

        FireResolvedBullet(
            direction,
            unitData.bulletRange,
            unitData.bulletExplodesOnImpact,
            unitData.bulletAoeRadius,
            unitData.bulletPierces,
            consumeAmmo: true,
            ignoredWallCell: null
        );
    }

    public void FireBulletInDirection(
        Vector3 direction,
        float range = -1,
        bool? pierces = null,
        Vector2Int? ignoredWallCell = null
    )
    {
        if (direction.sqrMagnitude <= Mathf.Epsilon)
            return;
        direction.Normalize();

        range = range == -1 ? unitData.bulletRange : range;
        FireResolvedBullet(
            direction,
            range,
            unitData.bulletExplodesOnImpact,
            unitData.bulletAoeRadius,
            pierces ?? unitData.bulletPierces,
            consumeAmmo: false,
            ignoredWallCell: ignoredWallCell
        );
    }

    private void FireResolvedBullet(
        Vector3 direction,
        float range,
        bool explodesOnImpact,
        float aoeRadius,
        bool pierces,
        bool consumeAmmo,
        Vector2Int? ignoredWallCell
    )
    {
        Vector3 origin = transform.position;
        bool ignoresWallCell = ignoredWallCell.HasValue;
        Vector2Int wallCell = ignoredWallCell.GetValueOrDefault();
        bullets.Add(
            CreateBullet(
                origin,
                direction,
                range,
                explodesOnImpact,
                aoeRadius,
                pierces,
                authoritative: true,
                ignoresWallCell: ignoresWallCell,
                ignoredWallCell: wallCell
            )
        );

        FireBulletClientRpc(
            origin,
            direction,
            range,
            explodesOnImpact,
            aoeRadius,
            pierces,
            ignoresWallCell,
            wallCell
        );

        if (consumeAmmo)
            currentAmmo--;

        GetComponent<AnimationHandler>()?.TriggerAnimation("Shoot");
    }

    [ClientRpc]
    private void FireBulletClientRpc(
        Vector3 origin,
        Vector3 direction,
        float range,
        bool explodesOnImpact,
        float aoeRadius,
        bool pierces,
        bool ignoresWallCell,
        Vector2Int ignoredWallCell
    )
    {
        if (IsServer)
            return;

        CreateBullet(
            origin,
            direction,
            range,
            explodesOnImpact,
            aoeRadius,
            pierces,
            authoritative: false,
            ignoresWallCell: ignoresWallCell,
            ignoredWallCell: ignoredWallCell
        );
    }

    private GameObject CreateBullet(
        Vector3 origin,
        Vector3 direction,
        float range,
        bool explodesOnImpact,
        float aoeRadius,
        bool pierces,
        bool authoritative,
        bool ignoresWallCell,
        Vector2Int ignoredWallCell
    )
    {
        GameObject bullet = Instantiate(
            ResolveBulletPrefab(),
            origin,
            Quaternion.LookRotation(direction)
        );
        bullet
            .GetComponent<Bullet>()
            .Initialize(
                direction * unitData.bulletSpeed * GameLoop.cellSize,
                unitData.damage,
                unitData.backstabMultiplier,
                range * GameLoop.cellSize,
                unitData.backstabAngle,
                ResolveEnemyTeam(),
                authoritative,
                explodesOnImpact,
                aoeRadius * GameLoop.cellSize,
                pierces,
                ignoresWallCell,
                ignoredWallCell
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
