using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shooting : MonoBehaviour
{
    public GameObject blueBulletPrefab;
    public GameObject redBulletPrefab;

    public float timeBetweenShots;

    public int magazineSize;
    public float reloadTime;

    public float bulletSpread; // Angle of spread in degrees on one side of the center line

    //Parameters in cells (/sec)
    public float bulletSpeed;
    public float targetRange; // Range to find targets within
    public float bulletRange;
    public int damage;

    public float cellSize;

    public string enemyTeam;

    private int currentAmmo;
    public bool allowShooting = true; // Controls whether the unit is currently shooting
    public bool stillShooting = true;

    void Start()
    {
    }

    public IEnumerator StartShooting()
    {
        enemyTeam = transform.tag == "BlueTeam" ? "RedTeam" : "BlueTeam";
        GameObject bulletPrefab = transform.tag == "BlueTeam" ? blueBulletPrefab : redBulletPrefab;


        currentAmmo = magazineSize;
        string bulletObjectName = transform.name + "'s Bullets";
        GameObject bullets = GameObject.Find(bulletObjectName) ?? new GameObject(bulletObjectName);
        allowShooting = true;
        stillShooting = true;

        while (allowShooting)
        {
            GameObject target = FindNearestEnemy();

            while (currentAmmo > 0)
            {
                //Find/Refind target
                if (target == null || !lineOfSight(target))
                {
                    target = FindNearestEnemy();
                    if(!allowShooting) break;
                    yield return null;
                    continue;
                }

                // Face the target
                Vector3 directionToTarget = (target.transform.position - transform.position).normalized;
                transform.rotation = Quaternion.LookRotation(directionToTarget);

                // Fire bullet with spread
                Vector3 baseDirection = transform.forward;
                float spreadAngle = Random.Range(-bulletSpread, bulletSpread);
                Vector3 shootDirection = Quaternion.AngleAxis(spreadAngle, transform.up) * baseDirection;

                GameObject bullet = Instantiate(bulletPrefab, transform.position, Quaternion.LookRotation(shootDirection));
                bullet.transform.parent = bullets.transform;

                Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
                Bullet bulletScript = bullet.GetComponent<Bullet>();
                if (bulletRb == null || bulletScript == null)
                {
                    Debug.LogWarning("Bullet set up wrongly!");
                    yield break; // Exit the coroutine if bullet setup is incorrect
                } //Error handling
                bulletRb.velocity = shootDirection * bulletSpeed * cellSize;
                bulletScript.damage = damage;
                bulletScript.range = bulletRange * cellSize;
                currentAmmo--;

                yield return new WaitForSeconds(timeBetweenShots);
            }

            if (allowShooting)
            {
                yield return StartCoroutine(Reload());
            }
        }
        
        while(bullets.transform.childCount > 0)
        {
            yield return null; // Wait for all bullets to be destoryed
        }
        yield return new WaitForSeconds(0.1f); // Small delay to ensure player deaths are processed
        stillShooting = false;
    }

    IEnumerator Reload()
    {
        yield return new WaitForSeconds(reloadTime);
        currentAmmo = magazineSize;
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
        RaycastHit hit;
        if (Physics.Raycast(transform.position, directionToEnemy, out hit, targetRange * cellSize, LayerMask.GetMask("Walls", enemyTeam)))
        {
            if (hit.collider.gameObject == enemy)
            {
                return true;
            }
        }
        return false;
    }
}
