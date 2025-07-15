using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shooting : MonoBehaviour
{
    public GameObject bulletPrefab;

    public float timeToFindTarget = 0.15f; // Time to find a target before shooting
    public float timeBetweenShots = 0.3f;

    public int magazineSize = 10;
    public float reloadTime = 2f;

    public float bulletSpread = 3f; // Angle of spread in degrees on one side of the center line

    public float bulletSpeed = 20f;
    public float bulletRange = 50f;
    public int damage = 10;

    public string team = "BlueTeam";
    public string enemyTeam = "RedTeam";

    private int currentAmmo;
    private bool isReloading = false;

    void Start()
    {
        currentAmmo = magazineSize;
    }

    public IEnumerator StartShooting()
    {
        if (bullet.GetComponent<Rigidbody>() == null || bullet.GetComponent<Bullet>() == null)
        {
            Debug.Warn("Bullet set up wrongly!");
            yield break; // Exit the coroutine if bullet setup is incorrect
        }

        while (true)
        {
            GameObject target;

            while (currentAmmo > 0)
            {
                //Find/Refind target
                while (target == null || !lineOfSight(target) || !IsTargetInRange(target))
                {
                    target = FindNearestEnemy();
                    yield return null;
                }

                yield return new WaitForSeconds(timeToFindTarget);

                // Face the target
                Vector3 directionToTarget = (target.transform.position - transform.position).normalized;
                transform.rotation = Quaternion.LookRotation(directionToTarget);

                // Fire bullet with spread
                Vector3 baseDirection = transform.forward;
                float spreadAngle = Random.Range(-bulletSpread, bulletSpread);
                Vector3 shootDirection = Quaternion.AngleAxis(spreadAngle, transform.up) * baseDirection;

                GameObject bullet = Instantiate(bulletPrefab, transform.position, Quaternion.LookRotation(shootDirection));
                bulletRb.velocity = shootDirection * bulletSpeed;
                bulletScript.damage = damage;
                bulletScript.range = bulletRange;
                bulletScript.team = team;

                currentAmmo--;

                yield return new WaitForSeconds(timeBetweenShots);
            }

            yield return StartCoroutine(Reload());
        }
    }

    IEnumerator Reload()
    {
        isReloading = true;
        yield return new WaitForSeconds(reloadTime);
        currentAmmo = magazineSize;
        isReloading = false;
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
        // Check for clear line of sight
        Vector3 directionToEnemy = (enemy.transform.position - transform.position).normalized;
        RaycastHit hit;

        if (Physics.Raycast(transform.position, directionToEnemy, out hit, distance))
        {
            // If we hit the enemy first, we have clear line of sight
            if (hit.collider.gameObject == enemy)
            {
                return true;
            }
        }
        return false;
    }

    bool IsTargetInRange(GameObject target)
    {
        float distanceToTarget = Vector3.Distance(transform.position, target.transform.position);
        return distanceToTarget <= bulletRange;
    }
}
