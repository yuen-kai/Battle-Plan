using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Health : NetworkBehaviour
{
    public UnitData unitData;

    // NetworkVariables (not ClientRpcs) so health/alive state survives fog NetworkHide/NetworkShow:
    // NGO drops object-scoped RPCs for clients the object is hidden from, but resyncs
    // NetworkVariables on NetworkShow.
    private NetworkVariable<float> currentHealth = new();
    private NetworkVariable<bool> isAlive = new(true);

    private Transform unitCanvas;
    private Transform healthBar;
    private Transform healthFill;

    private const float TypicalMaxHealth = 100f;

    /// <summary>Read-only HP accessor for dev tooling/tests (server-authoritative value on host).</summary>
    public float CurrentHealth => currentHealth.Value;
    public bool IsAlive => isAlive.Value;

    public override void OnNetworkSpawn()
    {
        unitCanvas = transform.Find("UnitCanvas");
        healthBar = unitCanvas != null ? unitCanvas.Find("HealthBar") : null;
        healthFill = healthBar != null ? healthBar.Find("HealthFill") : null;

        currentHealth.OnValueChanged += OnHealthChanged;
        isAlive.OnValueChanged += OnAliveChanged;

        if (IsServer)
        {
            isAlive.Value = true;
            currentHealth.Value = unitData.maxHealth;
        }

        UpdateMaxHealthScale();
        UpdateHealthFill(currentHealth.Value);

        // A hidden unit that died before NetworkShow must immediately stay absent on this client.
        if (!IsServer && !isAlive.Value)
            gameObject.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
        isAlive.OnValueChanged -= OnAliveChanged;
        base.OnNetworkDespawn();
    }

    void Update()
    {
        if (!IsClient)
            return;

        if (GameLoop.Instance?.TeamCamera != null && unitCanvas != null)
            unitCanvas.forward = GameLoop.Instance.TeamCamera.transform.forward;
    }

    public void TakeDamage(float damage)
    {
        if (!IsServer || !isAlive.Value)
            return;

        currentHealth.Value = Mathf.Max(0f, currentHealth.Value - damage);
        if (currentHealth.Value > 0f)
            return;

        isAlive.Value = false;
        GameLoop.Instance?.DisableUnitCard(gameObject);

        // Leave the NetworkObject active through this frame's network update so the final
        // NetworkVariable values can be sent, then preserve the existing tag-based teamSize rule.
        StartCoroutine(DeactivateOnServerNextFrame());
    }

    private IEnumerator DeactivateOnServerNextFrame()
    {
        yield return null;
        if (IsServer && !isAlive.Value)
            gameObject.SetActive(false);
    }

    private void OnHealthChanged(float previousValue, float newValue)
    {
        UpdateHealthFill(newValue);

        // Impact frame on every peer; NetworkVariable callbacks also fire after fog NetworkShow
        // resync, but only flash on an actual decrease.
        if (newValue < previousValue && gameObject.activeInHierarchy)
        {
            float severity = (previousValue - newValue) >= unitData.maxHealth * 0.4f ? 1.5f : 1f;
            HitFlash.FlashTarget(gameObject, 0.12f, severity);
        }
    }

    private void OnAliveChanged(bool previousValue, bool newValue)
    {
        // Death pop: shockwave ring in team color at the body's last position. Spawned as an
        // independent object so it outlives the unit's deactivation below.
        if (previousValue && !newValue)
        {
            Color teamColor = gameObject.CompareTag("BlueTeam")
                ? new Color(0.22f, 0.78f, 1f)
                : new Color(1f, 0.23f, 0.33f);
            ImpactShockwave.Spawn(transform.position, teamColor, 2.2f, 0.5f);
        }

        // The host/server deactivates after one network-update opportunity above.
        if (!IsServer && !newValue)
            gameObject.SetActive(false);
    }

    private void UpdateHealthFill(float health)
    {
        if (healthFill == null)
            return;

        healthFill.localScale = new Vector3(
            Mathf.Clamp(health / unitData.maxHealth, 0f, 1f),
            1f,
            1f
        );
    }

    private void UpdateMaxHealthScale()
    {
        if (healthBar == null)
            return;

        healthBar.localScale = new Vector3(
            Mathf.Clamp(unitData.maxHealth / TypicalMaxHealth, 0f, 1f),
            1f,
            1f
        );
    }
}
