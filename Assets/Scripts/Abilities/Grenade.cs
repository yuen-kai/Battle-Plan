using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Grenade : MonoBehaviour, IAbility
{
    float abilityTime = 1;
    float throwHeight = 5f;
    public float damage = 50f;
    public GameObject grenadePrefab;
    public GameObject grenadeExplosionPrefab;

    public IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 3)
    {
        // Instantiate the grenade at the current position
        GameObject grenade = Instantiate(grenadePrefab, transform.position, Quaternion.identity);
        Vector3 startPosition = grenade.transform.position;
        Vector3 targetPosition = abilitySquare + new Vector3(0, GetComponent<Collider>().bounds.size.y / 2, 0);

        float elapsed = 0f;

        while (elapsed < abilityTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / abilityTime;
            
            // Interpolate between start and target positions
            Vector3 currentPos = Vector3.Lerp(startPosition, targetPosition, progress);
            currentPos.y += throwHeight * 2 * progress * (1 - progress);

            grenade.transform.position = currentPos;
            
            yield return null; // Wait for next frame
        }
        grenade.transform.position = targetPosition;
        
        // Explode and damage enemies
        ExplodeGrenade(targetPosition, AreaRadius);
        GameObject explosionEffect = Instantiate(grenadeExplosionPrefab, targetPosition, Quaternion.identity);
        Destroy(explosionEffect, explosionEffect.GetComponent<ParticleSystem>().main.duration);
        Destroy(grenade);
    }

    private void ExplodeGrenade(Vector3 explosionPosition, float AreaRadius)
    {
        string enemyTeam = GameLoop.GetEnemyTeam(gameObject.tag);

        // Find all enemies within explosion range
        Collider[] enemiesInRange = Physics.OverlapSphere(explosionPosition, AreaRadius * GameLoop.cellSize, LayerMask.GetMask(enemyTeam));
        
        foreach (Collider enemy in enemiesInRange)
        {
            // Check line of sight from explosion to enemy
            Vector3 directionToEnemy = (enemy.transform.position - explosionPosition).normalized;
            float distanceToEnemy = Vector3.Distance(explosionPosition, enemy.transform.position);
            if (Physics.Raycast(explosionPosition, directionToEnemy, out RaycastHit hit, distanceToEnemy, LayerMask.GetMask("Walls", enemyTeam)))
            {
                // If raycast hits an obstacle before reaching the enemy, skip damage
                if (hit.collider != enemy)
                    continue;
            }

            enemy.transform.GetComponent<Health>()?.TakeDamage(damage);
        }
    }
}
