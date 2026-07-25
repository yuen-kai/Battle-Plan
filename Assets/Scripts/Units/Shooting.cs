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

    // THE SERVER'S ENCODING OF LOCK PROGRESS, not a pair of palette colours. The shooting loop
    // writes Color.Lerp(startAnimColor, endAnimColor, lockProgress) into the replicated laserColor
    // every frame of the lock, so what actually crosses the wire is a 0..1 scalar wearing a Color.
    // The client decodes it back out in DecodeLockProgress and never puts it on screen — see
    // ApplyLockRamp for why the beam's hue cannot come from here.
    private Color startAnimColor = Color.white;
    private Color endAnimColor = Color.red;

    // === Target-lock beam presentation, ArtDirection §8.2 ===

    /// <summary>
    /// The beam's opacity at full lock. §8.2 gives the target lock as Alpha at 0.88 in
    /// --bp-red-deep, and the ramp lands on that figure rather than starting from it.
    /// </summary>
    private const float LockAlphaFull = 0.88f;

    /// <summary>
    /// Opacity the moment the lock begins. Deliberately not near zero: this beam is the only
    /// warning the victim of a one-shot gets, so the first frame has to be a definite dark
    /// hairline rather than a hint. The growth toward LockAlphaFull compounds with the server's
    /// existing startAnimWidth -> endAnimWidth ramp, so the beam both widens and firms up as the
    /// shot charges — §9.1's anticipation beat, produced by darkening rather than by brightening.
    /// </summary>
    private const float LockAlphaStart = 0.55f;

    /// <summary>
    /// §8.2 specifies the beam as a 0.03 core inside a 0.20 glow. <c>_CoreWidth</c> on
    /// BattlePlan/EnergyBeam is a fraction of the line's own width rather than a world size, and
    /// endAnimWidth is already 0.20, so the spec's two widths reduce to 0.03 / 0.20 here. The
    /// previous 0.35 drew a 0.07 core, more than twice §8.2's.
    /// </summary>
    private const float LockCoreWidthFraction = 0.15f;

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

        // §8.2: a dark line, not a glowing one. Alpha at --bp-red-deep, no hot core — the beam
        // reads by scoring the pale board rather than by out-shining it, and the HDR core palette
        // §6 licenses belongs to Area Lock, which this must never out-read.
        //
        // Alpha has to be set through FXPalette.Composite.Apply, not by hand: _CompositeMode picks
        // the branch inside the shader and _SrcBlend/_DstBlend are the render state that has to
        // agree with it. Setting the mode alone leaves the blend state at the shader's One/One
        // default, which is how this beam spent the redesign compositing additive while claiming to
        // be alpha.
        Shader beamShader = Shader.Find("BattlePlan/EnergyBeam");
        if (beamShader == null)
        {
            Debug.LogWarning(
                "[Shooting] BattlePlan/EnergyBeam shader not found, falling back to Sprites/Default"
            );
            beamShader = Shader.Find("Sprites/Default");
        }
        if (beamShader == null)
            return;

        Material beamMaterial = new(beamShader) { hideFlags = HideFlags.HideAndDontSave };
        if (beamMaterial.HasProperty("_GlowColor"))
        {
            // Both terms take the same value, as in BeamVFX.CreateLayer: one line, one colour. A
            // lighter core would put a pale filament down the middle of a threat line.
            beamMaterial.SetColor("_GlowColor", FXPalette.RedDeep);
            beamMaterial.SetColor("_CoreColor", FXPalette.RedDeep);
            beamMaterial.SetFloat("_CoreWidth", LockCoreWidthFraction);
            beamMaterial.SetFloat("_NoiseStrength", 0.2f);
            FXPalette.Composite.Apply(beamMaterial, FXPalette.AbovePlaneComposite);
        }
        else if (beamMaterial.HasProperty("_Color"))
        {
            // The fallback shader has one colour slot and no composite modes. It still has to be
            // told the hue: ApplyLockRamp holds the vertex colour at white precisely so the material
            // owns it, so a material that took no colour draws a white beam rather than a red one.
            beamMaterial.SetColor("_Color", FXPalette.RedDeep);
        }
        targetLaser.sharedMaterial = beamMaterial;
    }

    /// <summary>
    /// Puts the replicated lock ramp on screen as opacity, never as hue.
    ///
    /// WHICH CHANNEL OWNS WHAT. BattlePlan/EnergyBeam multiplies its material tint by the
    /// LineRenderer's vertex colour and scales coverage by the vertex alpha, so the two are not
    /// interchangeable: <c>.rgb</c> is a hue modulator and <c>.a</c> is an opacity modulator. §8.2
    /// fixes the hue at --bp-red-deep, which means the material owns it and the vertex colour must
    /// stay white — writing the replicated ramp straight onto startColor/endColor multiplies a
    /// near-pure red by a white-to-red ramp and annihilates the crimson's green and blue tail as
    /// the lock completes. The hue would drift while the ramp itself became invisible, because
    /// --bp-red-deep has almost no green or blue left for the ramp to remove. Same structural trap
    /// as authoring a tracer's team colour in both its material and its trail gradient.
    ///
    /// So the ramp moves to the only channel that carries no hue. A threat telegraph that firms up
    /// is exactly what alpha is for, and on a bright board growing opacity in a near-ink colour is
    /// growing darkness — which is the emphasis FIELD DAY actually spends.
    /// </summary>
    private void ApplyLockRamp(Color replicatedRamp)
    {
        float alpha = Mathf.Lerp(LockAlphaStart, LockAlphaFull, DecodeLockProgress(replicatedRamp));
        targetLaser.startColor = targetLaser.endColor = new Color(1f, 1f, 1f, alpha);
    }

    /// <summary>
    /// Recovers the 0..1 lock progress the server encoded into <see cref="laserColor"/>.
    ///
    /// This is the inverse of the server's <c>Color.Lerp</c>, computed by projecting the received
    /// value onto the startAnimColor -> endAnimColor axis. Written that way rather than as "read the
    /// green channel" because the encoding *is* those two fields: a hardcoded channel decodes
    /// silently wrong the day someone retunes the pair, and the beam would still draw — just with
    /// its ramp flat or running backwards, which is the kind of failure nobody files a bug for. A
    /// projection is exact for any pair of endpoints and needs no knowledge of which one they are.
    ///
    /// A zero-length axis means the two endpoints have been made equal, i.e. there is no ramp to
    /// decode, so the beam holds at <see cref="LockAlphaStart"/> instead of dividing by zero.
    /// </summary>
    private float DecodeLockProgress(Color replicatedRamp)
    {
        float axisR = endAnimColor.r - startAnimColor.r;
        float axisG = endAnimColor.g - startAnimColor.g;
        float axisB = endAnimColor.b - startAnimColor.b;

        float axisLengthSquared = axisR * axisR + axisG * axisG + axisB * axisB;
        if (axisLengthSquared < Mathf.Epsilon)
            return 0f;

        float projection =
            (replicatedRamp.r - startAnimColor.r) * axisR
            + (replicatedRamp.g - startAnimColor.g) * axisG
            + (replicatedRamp.b - startAnimColor.b) * axisB;

        return Mathf.Clamp01(projection / axisLengthSquared);
    }

    private void ApplyCurrentLaserState()
    {
        EnsureTargetLaser();
        targetLaser.SetPositions(new[] { laserStartPos.Value, laserEndPos.Value });
        targetLaser.startWidth = targetLaser.endWidth = laserWidth.Value;
        ApplyLockRamp(laserColor.Value);
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
        ApplyLockRamp(newValue);
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
                    // Carries lockProgress, not a colour to draw. Clients decode it in
                    // DecodeLockProgress and spend it on opacity; §8.2 fixes the beam's hue on the
                    // material. Retuning the two endpoints stays safe, repurposing this to mean a
                    // literal beam colour does not.
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
