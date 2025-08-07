using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class Health : NetworkBehaviour
{
    public UnitData unitData;

    private float currentHealth;
    private Transform healthBar;
    private Transform healthFill;

    private float typicalMaxHealth = 100f;


    public override void OnNetworkSpawn()
    {
        healthBar = transform.Find("UnitCanvas").Find("HealthBar");
        healthFill = healthBar.Find("HealthFill");

        if (IsServer)
        {
            SetHealthBarClientRpc(unitData.maxHealth);
            SetHealthClientRpc(unitData.maxHealth);
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!IsClient) return;
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Transform UnitCanvas = transform.Find("UnitCanvas");
            UnitCanvas.forward = Camera.main.transform.forward;
        }
    }

    public void TakeDamage(float damage)
    {
        SetHealthClientRpc(currentHealth - damage);

        if (currentHealth <= 0)
        {
            GameLoop.disableUnitCard(gameObject);
            NetworkHelper.Instance.SetActive(gameObject, false);
        }
    }

    [ClientRpc]
    private void SetHealthClientRpc(float health)
    {
        currentHealth = health;
        healthFill.localScale = new Vector3(Mathf.Clamp(currentHealth / unitData.maxHealth, 0f, 1f), 1f, 1f);
    }

    [ClientRpc]
    private void SetHealthBarClientRpc(float maxHealth)
    {
        healthBar.localScale = new Vector3(Mathf.Clamp(maxHealth / typicalMaxHealth, 0f, 1f), 1f, 1f);
    }
}
