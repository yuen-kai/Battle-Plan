using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;
    private Transform healthFill;

    // Start is called before the first frame update
    void Start()
    {
        healthFill = transform.Find("HealthBar").Find("HealthFill");
        SetHealth(maxHealth);
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void TakeDamage(float damage)
    {
        SetHealth(currentHealth - damage);

        if (currentHealth <= 0)
        {
            Destroy(gameObject);
        }
    }

    public void SetHealth(float health)
    {
        currentHealth = health;
        healthFill.localScale = new Vector3(Mathf.Clamp(currentHealth / maxHealth, 0f, 1f), 1f, 1f);
    }
}
