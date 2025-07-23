using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float damage = 10f;
    public float range = 50f;
    public string team = "BlueTeam";
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
        if (hitObject.CompareTag(team))
        {
            return;
        }
        if (hitObject.CompareTag(enemyTeam))
        {
            hitObject.GetComponent<Health>()?.TakeDamage(damage);
        }
        // Destroy self (bullet)
        Destroy(gameObject);
    }
}
