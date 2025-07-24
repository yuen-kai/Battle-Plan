using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Health : MonoBehaviour
{
    public float maxHealth;
    private float currentHealth;
    private Transform healthFill;

    // Start is called before the first frame update
    void Start()
    {
        Transform healthBar = transform.Find("HealthBar");
        healthBar.localScale = new Vector3(maxHealth/100f, 1f, 1f);

        healthFill = healthBar.Find("HealthFill");
        SetHealth(maxHealth);
    }

    // Update is called once per frame
    void Update()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Transform healthBar = healthFill?.parent;
            healthBar?.LookAt(mainCamera.transform);
            healthBar?.Rotate(0, 180, 0);
        }
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
