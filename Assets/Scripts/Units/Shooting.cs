using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shooting : MonoBehaviour
{
    public string enemyTeam;
    public UnitData unitData;

    LineRenderer targetLaser;
    float startAnimWidth = 0.05f;
    float endAnimWidth = 0.2f;
    Color startAnimColor = Color.white;
    Color endAnimColor = Color.red;

    private int currentAmmo;
    [HideInInspector] public bool allowShooting = true; // Controls whether the unit can start a new shooting cycle
    [HideInInspector] public bool stillShooting = true;

    GameObject bulletsParent;

    Coroutine shootingCoroutine;

    void Start()
    {
        targetLaser = gameObject.AddComponent<LineRenderer>();
        targetLaser.enabled = false;
        string bulletObjectName = transform.name + "'s Bullets";
        bulletsParent = GameObject.Find(bulletObjectName) ?? new GameObject(bulletObjectName);
    }

    public void StartShooting()
    {
        StopShooting();
        shootingCoroutine = StartCoroutine(InitiateShooting());
    }

    public void ContinueShooting()
    {
        allowShooting = true;
        if (stillShooting == false)
        {
            stillShooting = true;
            shootingCoroutine = StartCoroutine(InitiateShooting());
        }
    }

    public void StopShooting()
    {
        if (shootingCoroutine != null) StopCoroutine(shootingCoroutine);
        allowShooting = true;
        stillShooting = true;
        targetLaser.enabled = false;
    }

    private IEnumerator RotateToFaceTarget(GameObject target)
    {
        yield return StartCoroutine(transform.GetComponent<Movement>().RotateToFaceTarget(target.transform.position, unitData.rotationSpeed));
    }

    public IEnumerator InitiateShooting()
    {
        enemyTeam = GameLoop.GetEnemyTeam(transform.tag);

        currentAmmo = unitData.magazineSize;

        float remainingTargetLockTime = unitData.targetLockDuration;

        while (allowShooting)
        {
            GameObject target = FindNearestEnemy();
            if (target) yield return StartCoroutine(RotateToFaceTarget(target));

            remainingTargetLockTime = unitData.targetLockDuration;

            while (currentAmmo > 0)
            {
                //Find/Refind target
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
                    // Face the target
                    transform.rotation = Quaternion.LookRotation((target.transform.position - transform.position).normalized);

                    targetLaser.enabled = true;
                    targetLaser.SetPositions(new Vector3[] { transform.position, target.transform.position });
                    targetLaser.startWidth = targetLaser.endWidth = Mathf.Lerp(startAnimWidth, endAnimWidth, 1 - remainingTargetLockTime / unitData.targetLockDuration);
                    targetLaser.startColor = targetLaser.endColor = Color.Lerp(startAnimColor, endAnimColor, 1 - remainingTargetLockTime / unitData.targetLockDuration);


                    remainingTargetLockTime -= Time.deltaTime;
                    yield return null;
                    continue;
                }
                targetLaser.enabled = false;

                FireBullet();

                yield return new WaitForSeconds(unitData.timeBetweenShots); //respects timer pauses
            }

            if (allowShooting)
            {
                yield return StartCoroutine(Reload());
            }
        }

        while (bulletsParent.transform.childCount > 0)
        {
            yield return null; // Wait for all bullets to be destoryed
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

        GameObject bullet = Instantiate(bulletPrefab, transform.position, Quaternion.LookRotation(shootDirection));
        bullet.transform.parent = bulletsParent.transform;

        Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
        Bullet bulletScript = bullet.GetComponent<Bullet>();

        bulletRb.velocity = shootDirection * bulletSpeed * GameLoop.cellSize;
        bulletScript.damage = damage;
        bulletScript.backstabMultiplier = backstabMultiplier;
        bulletScript.range = range * GameLoop.cellSize;
        bulletScript.backstabAngle = backstabAngle;
        currentAmmo--;
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
