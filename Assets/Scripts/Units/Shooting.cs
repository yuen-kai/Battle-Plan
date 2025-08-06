using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

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

    private List<GameObject> bullets = new List<GameObject>();
    private int currentAmmo;
    [HideInInspector] public bool allowShooting = true; // Controls whether the unit can start a new shooting cycle
    [HideInInspector] public bool stillShooting = true;

    private Coroutine shootingCoroutine;

    // CONTROLLER
    void Start()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }
        GameLoop.OrderAllowShooting += (toggle) => allowShooting = toggle;
        GameLoop.OrderStillShooting += (toggle) => stillShooting = toggle;
        GameLoop.OrderContinueShooting += ContinueShooting;

        targetLaser = gameObject.AddComponent<LineRenderer>();
        targetLaser.enabled = false;

        Debug.Log(transform.tag);
        enemyTeam = GameLoop.GetEnemyTeam(transform.tag);
    }

    public void PauseShooting()
    {
        if (shootingCoroutine != null) StopCoroutine(shootingCoroutine);
        targetLaser.enabled = false;

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
        yield return StartCoroutine(transform.GetComponent<Movement>().RotateToFaceTarget(target.transform.position, unitData.rotationSpeed));
    }


    // SHOOTING
    public IEnumerator InitiateShooting()
    {
        currentAmmo = unitData.magazineSize;

        float remainingTargetLockTime = unitData.targetLockDuration;

        while (allowShooting)
        {
            GameObject target = FindNearestEnemy();
            if (target) yield return StartCoroutine(RotateToFaceTarget(target));

            remainingTargetLockTime = unitData.targetLockDuration;

            while (currentAmmo > 0)
            {
                //Refind target
                if (target == null || !lineOfSight(target))
                {
                    targetLaser.enabled = false;
                    target = FindNearestEnemy();
                    if (target)
                    {
                        remainingTargetLockTime = unitData.targetLockDuration;
                        yield return StartCoroutine(RotateToFaceTarget(target));
                    }
                    if (!allowShooting) break;

                    yield return null;
                    continue;
                }

                //Target lock
                if (remainingTargetLockTime > 0f)
                {
                    transform.rotation = Quaternion.LookRotation((target.transform.position - transform.position).normalized); // Track target

                    targetLaser.enabled = true;
                    targetLaser.SetPositions(new Vector3[] { transform.position, target.transform.position });
                    targetLaser.startWidth = targetLaser.endWidth = Mathf.Lerp(startAnimWidth, endAnimWidth, 1 - remainingTargetLockTime / unitData.targetLockDuration);
                    targetLaser.startColor = targetLaser.endColor = Color.Lerp(startAnimColor, endAnimColor, 1 - remainingTargetLockTime / unitData.targetLockDuration);

                    remainingTargetLockTime -= Time.deltaTime;

                    yield return null;
                    continue;
                }
                targetLaser.enabled = false;

                transform.rotation = Quaternion.LookRotation((target.transform.position - transform.position).normalized); // Track target
                FireBullet();

                yield return new WaitForSeconds(unitData.timeBetweenShots);
            }

            if (allowShooting)
            {
                yield return StartCoroutine(Reload());
            }
        }

        // Wait for all bullets to be destroyed
        while (GetActiveBulletCount() > 0)
        {
            yield return null;
        }

        yield return new WaitForSeconds(0.1f); // Small delay to ensure player deaths are processed
        stillShooting = false;
    }

    public void FireBullet(float spread = -1, float bulletSpeed = -1, float damage = -1, float backstabMultiplier = -1, float range = -1, float backstabAngle = -1, GameObject bulletPrefab = null)
    {
        spread = spread == -1 ? unitData.bulletSpread : spread;
        bulletSpeed = bulletSpeed == -1 ? unitData.bulletSpeed : bulletSpeed;
        damage = damage == -1 ? unitData.damage : damage;
        backstabMultiplier = backstabMultiplier == -1 ? unitData.backstabMultiplier : backstabMultiplier;
        range = range == -1 ? unitData.bulletRange : range;
        backstabAngle = backstabAngle == -1 ? unitData.backstabAngle : backstabAngle;
        bulletPrefab = bulletPrefab ?? (transform.tag == "BlueTeam" ? unitData.blueBulletPrefab : unitData.redBulletPrefab);

        // Fire bullet with spread
        Vector3 baseDirection = transform.forward;
        float spreadAngle = Random.Range(-spread, spread);
        Vector3 shootDirection = Quaternion.AngleAxis(spreadAngle, transform.up) * baseDirection;

        GameObject bullet = NetworkHelper.Spawn(bulletPrefab, transform.position, Quaternion.LookRotation(shootDirection));
        bullets.Add(bullet);

        Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
        Bullet bulletScript = bullet.GetComponent<Bullet>();

        bulletRb.linearVelocity = shootDirection * bulletSpeed * GameLoop.cellSize;
        bulletScript.damage = damage;
        bulletScript.backstabMultiplier = backstabMultiplier;
        bulletScript.range = range * GameLoop.cellSize;
        bulletScript.backstabAngle = backstabAngle;

        currentAmmo--;
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

    GameObject FindNearestEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag(enemyTeam);
        GameObject nearestEnemy = null;
        float nearestDistance = Mathf.Infinity;

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

    bool lineOfSight(GameObject enemy)
    {
        // Check for clear line of sight within range
        Vector3 directionToEnemy = (enemy.transform.position - transform.position).normalized;
        if (Physics.Raycast(transform.position, directionToEnemy, out RaycastHit hit, unitData.targetRange * GameLoop.cellSize, LayerMask.GetMask("Walls", enemyTeam)))
        {
            if (hit.collider.gameObject == enemy)
            {
                return true;
            }
        }
        return false;
    }
}
