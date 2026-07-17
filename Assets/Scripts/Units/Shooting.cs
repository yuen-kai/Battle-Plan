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

    private LineRenderer targetLaser;
    private float startAnimWidth = 0.05f;
    private float endAnimWidth = 0.2f;
    private Color startAnimColor = Color.white;
    private Color endAnimColor = Color.red;

    // Network variables for laser synchronization
    private NetworkVariable<bool> isLaserEnabled = new(false);
    private NetworkVariable<Vector3> laserStartPos = new();
    private NetworkVariable<Vector3> laserEndPos = new();
    private NetworkVariable<float> laserWidth = new();
    private NetworkVariable<Color> laserColor = new();

    private List<GameObject> bullets = new();
    private int currentAmmo;

    [HideInInspector]
    public bool allowShooting = true; // Controls whether the unit can start a new shooting cycle

    [HideInInspector]
    public bool stillShooting = true;

    private Coroutine shootingCoroutine;

    // CONTROLLER
    // All setup is in OnNetworkSpawn (not Start) so a fog NetworkShow re-runs it and the current
    // laser NetworkVariable values are applied to a freshly (re)created LineRenderer.
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        EnsureTargetLaser();

        // Subscribe to network variable changes on all clients
        isLaserEnabled.OnValueChanged += OnLaserEnabledChanged;
        laserStartPos.OnValueChanged += OnLaserPositionChanged;
        laserEndPos.OnValueChanged += OnLaserPositionChanged;
        laserWidth.OnValueChanged += OnLaserWidthChanged;
        laserColor.OnValueChanged += OnLaserColorChanged;
        ApplyCurrentLaserState();

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
        // Unsubscribe from network variable changes
        isLaserEnabled.OnValueChanged -= OnLaserEnabledChanged;
        laserStartPos.OnValueChanged -= OnLaserPositionChanged;
        laserEndPos.OnValueChanged -= OnLaserPositionChanged;
        laserWidth.OnValueChanged -= OnLaserWidthChanged;
        laserColor.OnValueChanged -= OnLaserColorChanged;

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

        targetLaser = GetComponent<LineRenderer>();
        if (targetLaser == null)
            targetLaser = gameObject.AddComponent<LineRenderer>();
        targetLaser.enabled = false;

        // Energy-beam look: white HDR core, glow tinted by the replicated start/end colors
        // (the white->red lock-on ramp rides the LineRenderer vertex colors).
        Shader beamShader = Shader.Find("BattlePlan/EnergyBeam");
        if (beamShader != null)
        {
            Material beamMaterial = new(beamShader);
            beamMaterial.SetColor("_GlowColor", Color.white);
            beamMaterial.SetColor("_CoreColor", Color.white * 1.5f);
            beamMaterial.SetFloat("_CoreWidth", 0.35f);
            beamMaterial.SetFloat("_NoiseStrength", 0.2f);
            targetLaser.material = beamMaterial;
        }
    }

    private void ApplyCurrentLaserState()
    {
        EnsureTargetLaser();
        targetLaser.SetPositions(new[] { laserStartPos.Value, laserEndPos.Value });
        targetLaser.startWidth = targetLaser.endWidth = laserWidth.Value;
        targetLaser.startColor = targetLaser.endColor = laserColor.Value;
        targetLaser.enabled = isLaserEnabled.Value;
    }

    private void SetAllowShooting(bool toggle)
    {
        allowShooting = toggle;
    }

    private void SetStillShooting(bool toggle)
    {
        stillShooting = toggle;
    }

    private void OnLaserEnabledChanged(bool previousValue, bool newValue)
    {
        EnsureTargetLaser();
        targetLaser.enabled = newValue;
    }

    private void OnLaserPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        EnsureTargetLaser();
        targetLaser.SetPositions(new[] { laserStartPos.Value, laserEndPos.Value });
    }

    private void OnLaserWidthChanged(float previousValue, float newValue)
    {
        EnsureTargetLaser();
        targetLaser.startWidth = targetLaser.endWidth = newValue;
    }

    private void OnLaserColorChanged(Color previousValue, Color newValue)
    {
        EnsureTargetLaser();
        targetLaser.startColor = targetLaser.endColor = newValue;
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
        isLaserEnabled.Value = false;

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
                    isLaserEnabled.Value = false;
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

                    float lockProgress = 1 - remainingTargetLockTime / unitData.targetLockDuration;

                    isLaserEnabled.Value = true;
                    laserWidth.Value = Mathf.Lerp(startAnimWidth, endAnimWidth, lockProgress);
                    laserColor.Value = Color.Lerp(startAnimColor, endAnimColor, lockProgress);
                    laserStartPos.Value = transform.position;
                    laserEndPos.Value = target.transform.position;

                    remainingTargetLockTime -= Time.deltaTime;

                    yield return null;
                    continue;
                }
                isLaserEnabled.Value = false;

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

        GameObject bullet = NetworkHelper.Spawn(
            bulletPrefab,
            transform.position,
            Quaternion.LookRotation(shootDirection)
        );
        bullets.Add(bullet);

        Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
        Bullet bulletScript = bullet.GetComponent<Bullet>();

        bulletRb.linearVelocity = shootDirection * bulletSpeed * GameLoop.cellSize;
        bulletScript.damage = damage;
        bulletScript.backstabMultiplier = backstabMultiplier;
        bulletScript.range = range * GameLoop.cellSize;
        bulletScript.backstabAngle = backstabAngle;
        bulletScript.enemyTeam = enemyTeam;

        currentAmmo--;

        if (GetComponent<AnimationHandler>() != null)
        {
            GetComponent<AnimationHandler>().TriggerAnimation("Shoot");
        }
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
        float projectileRadius = GetProjectileCollisionRadius(ResolveBulletPrefab());
        RaycastHit hit;
        bool hitSomething;
        if (projectileRadius > 0f)
        {
            hitSomething = Physics.SphereCast(
                transform.position,
                projectileRadius,
                directionToEnemy,
                out hit,
                unitData.targetRange * GameLoop.cellSize,
                LayerMask.GetMask("Walls", enemyTeam),
                QueryTriggerInteraction.Ignore
            );
        }
        else
        {
            hitSomething = Physics.Raycast(
                transform.position,
                directionToEnemy,
                out hit,
                unitData.targetRange * GameLoop.cellSize,
                LayerMask.GetMask("Walls", enemyTeam),
                QueryTriggerInteraction.Ignore
            );
        }
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
