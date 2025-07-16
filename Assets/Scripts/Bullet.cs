using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float damage = 10f; // Damage dealt by the bullet
    public float range = 50f; // Maximum range of the bullet
    public string team = "BlueTeam"; // Team of the bullet
    public string enemyTeam = "RedTeam"; // Enemy team of the bullet

    private Vector3 startPosition; // Starting position of the bullet
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
        //if (hitObject.CompareTag(enemyTeam))
        //{
        //    hitObject.GetComponent<Health>().TakeDamage(damage);
        //}
        // Destroy self (bullet)
        Destroy(gameObject);
    }
}
