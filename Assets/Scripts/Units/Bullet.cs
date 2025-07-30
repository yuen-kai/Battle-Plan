using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float damage = 10f;
    public float backstabMultiplier = 1f; //by default, no backstab multiplier
    public float backstabAngle = 90f; // Angle in degrees to consider a backstab
    public float range = 50f;
    public string enemyTeam = "RedTeam";

    private Vector3 startPosition;
    // Start is called before the first frame update
    void Start()
    {
        startPosition = transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        if (Vector3.Distance(startPosition, transform.position) > range)
        {
            Destroy(gameObject);
        }
    }

    private void OnCollisionEnter(Collision other) // built-in function
    {
        GameObject hitObject = other.gameObject;
        if (hitObject.CompareTag(enemyTeam))
        {
            float finalDamage = damage;

            Vector3 bulletDirection = (hitObject.transform.position - startPosition).normalized;
            Vector3 targetForward = hitObject.transform.forward;

            float angle = Vector3.Angle(targetForward, -bulletDirection);
            if (angle >= backstabAngle)
            {
                finalDamage = damage * backstabMultiplier;
            }

            hitObject.GetComponent<Health>()?.TakeDamage(finalDamage);
        }
        // Destroy self (bullet)
        Destroy(gameObject);
    }
}
