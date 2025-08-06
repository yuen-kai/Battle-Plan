using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class Bullet : NetworkBehaviour
{
    public float damage = 10f;
    public float backstabMultiplier = 1f;
    public float backstabAngle = 90f; // Angle in degrees from forward direction to consider a backstab
    public float range = 50f;
    public string enemyTeam = "RedTeam";

    private Vector3 startPosition;

    private float maxLifetime = 8f;
    private float timeElapsed = 0f;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }
        startPosition = transform.position;
    }

    void Update()
    {
        if (Vector3.Distance(startPosition, transform.position) > range || timeElapsed > maxLifetime)
        {
            NetworkHelper.Despawn(gameObject);
        }
        timeElapsed += Time.deltaTime;
    }

    private void OnCollisionEnter(Collision other) // built-in function
    {
        GameObject hitObject = other.gameObject;
        if (hitObject.CompareTag(enemyTeam))
        {
            float finalDamage = !CheckBackstab(hitObject) ? damage : damage * backstabMultiplier;
            hitObject.GetComponent<Health>()?.TakeDamage(finalDamage);
        }

        NetworkHelper.Despawn(gameObject);
    }

    private bool CheckBackstab(GameObject hitObject)
    {
        Vector3 targetForward = hitObject.transform.forward;
        Vector3 bulletDirection = (hitObject.transform.position - startPosition).normalized;
        float angle = Vector3.Angle(targetForward, -bulletDirection);
        return angle >= backstabAngle;
    }
}
