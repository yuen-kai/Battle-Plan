using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class Bullet : NetworkBehaviour
{
    private static readonly HashSet<Bullet> activeServerBullets = new();

    public float damage = 10f;
    public float backstabMultiplier = 1f;
    public float backstabAngle = 90f; // Angle in degrees from forward direction to consider a backstab
    public float range = 50f;
    public string enemyTeam = "RedTeam";

    private Vector3 startPosition;

    private float maxLifetime = 8f;
    private float timeElapsed = 0f;

    public static int ActiveServerBulletCount => activeServerBullets.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveBullets()
    {
        activeServerBullets.Clear();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }
        activeServerBullets.Add(this);
        startPosition = transform.position;
    }

    public override void OnNetworkDespawn()
    {
        activeServerBullets.Remove(this);
        base.OnNetworkDespawn();
    }

    private void OnDestroy()
    {
        activeServerBullets.Remove(this);
    }

    void Update()
    {
        if (Vector3.Distance(startPosition, transform.position) > range || timeElapsed > maxLifetime)
        {
            NetworkHelper.Instance.Despawn(gameObject);
        }
        timeElapsed += Time.deltaTime;
    }

    private void OnCollisionEnter(Collision other) // built-in function
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }
        GameObject hitObject = other.gameObject;
        if (hitObject.CompareTag(enemyTeam))
        {
            float finalDamage = !CheckBackstab(hitObject) ? damage : damage * backstabMultiplier;
            hitObject.GetComponent<Health>()?.TakeDamage(finalDamage);
        }

        NetworkHelper.Instance.Despawn(gameObject);
    }

    private bool CheckBackstab(GameObject hitObject)
    {
        Vector3 targetForward = hitObject.transform.forward;
        Vector3 bulletDirection = (hitObject.transform.position - startPosition).normalized;
        float angle = Vector3.Angle(targetForward, -bulletDirection);
        return angle >= backstabAngle;
    }
}
