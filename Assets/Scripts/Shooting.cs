using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shooting : MonoBehaviour
{
    public GameObject bulletPrefab;

    public float timeBetweenShots = 0.3f;

    public int magazineSize = 10;
    public float reloadTime = 2f;

    public float bulletSpread = 1f; // Angle of spread in degrees on one side of the center line

    public float bulletSpeed = 30f;
    public float targetRange = 50f; // Range to find targets within
    public float bulletRange = 100f;
    public int damage = 10;

    public string team = "BlueTeam";
    public string enemyTeam = "RedTeam";

    private int currentAmmo;

    void Start()
    {
        
    }

    public IEnumerator StartShooting()
    {
        currentAmmo = magazineSize;
        GameObject bullets = new GameObject(transform.name + "'s Bullets");


        while (true)
        {
            GameObject target = null;

            while (currentAmmo > 0)
            {
                //Find/Refind target
                while (target == null || !lineOfSight(target))
                {
                    target = FindNearestEnemy();
                    yield return null;
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
                bulletRb.velocity = shootDirection * bulletSpeed;
                bulletScript.damage = damage;
                bulletScript.range = bulletRange;
                bulletScript.team = team;
                bulletScript.enemyTeam = enemyTeam;

                int bulletLayer = LayerMask.NameToLayer("Projectile");

                // Prevent friendly fire
                GameObject[] friendlyPlayers = GameObject.FindGameObjectsWithTag(team);
                foreach (GameObject player in friendlyPlayers)
                {
                    Physics.IgnoreCollision(GetComponent<Collider>(), player.GetComponent<Collider>());
                }

                currentAmmo--;

                yield return new WaitForSeconds(timeBetweenShots);
            }

            yield return StartCoroutine(Reload());
        }
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

        if (Physics.Raycast(transform.position, directionToEnemy, out hit, targetRange, LayerMask.GetMask("Walls", enemyTeam)))
        {
            if (hit.collider.gameObject == enemy)
            {
                return true;
            }
        }
        return false;
    }
}
