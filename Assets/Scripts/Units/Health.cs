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
    private UnityEngine.UI.Image healthFillImage;

    private const float TypicalMaxHealth = 100f;

    /// <summary>Read-only HP accessor for dev tooling/tests (server-authoritative value on host).</summary>
    public float CurrentHealth => currentHealth.Value;
    public float MaxHealth => unitData != null ? Mathf.Max(0f, unitData.maxHealth) : 0f;
    public bool IsAlive => isAlive.Value;

    public override void OnNetworkSpawn()
    {
        unitCanvas = transform.Find("UnitCanvas");
        healthBar = unitCanvas != null ? unitCanvas.Find("HealthBar") : null;
        healthFill = healthBar != null ? healthBar.Find("HealthFill") : null;
        healthFillImage =
            healthFill != null ? healthFill.GetComponent<UnityEngine.UI.Image>() : null;

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
        {
            GameLoop.Instance?.NotifyEnemyUnitStatusChanged(gameObject);
            return;
        }

        isAlive.Value = false;
        GetComponent<Movement>()?.ClearTemporaryMoveSpeedBoost();
        GameLoop.Instance?.DisableUnitCard(gameObject);
        GameLoop.Instance?.NotifyEnemyUnitStatusChanged(gameObject);

        // Leave the NetworkObject active through this frame's network update so the final
        // NetworkVariable values can be sent before round-end arbitration.
        StartCoroutine(DeactivateOnServerNextFrame());
    }

    /// <summary>
    /// Server-only revival for respawn-enabled modes. Restores health and transient
    /// movement/shooting state without resetting the unit's ability cooldown.
    /// </summary>
    public bool RespawnAt(Vector3 position, Quaternion rotation)
    {
        if (!IsServer || isAlive.Value)
            return false;

        gameObject.SetActive(true);
        transform.SetPositionAndRotation(position, rotation);
        GetComponent<Ability>()?.ResetForRespawn();

        Movement movement = GetComponent<Movement>();
        if (movement != null)
        {
            movement.PauseMovement();
            movement.ClearTemporaryMoveSpeedBoost();
            movement.moving = false;
        }

        Shooting shooting = GetComponent<Shooting>();
        shooting?.PauseShooting();

        Transform alert = transform.Find("UnitCanvas/Alert");
        if (alert != null)
            alert.gameObject.SetActive(false);

        currentHealth.Value = unitData.maxHealth;
        isAlive.Value = true;
        GetComponent<AnimationHandler>()?.PlayAnimation("Idle");
        GameLoop.Instance?.NotifyEnemyUnitStatusChanged(gameObject);
        return true;
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
            // Viewer-relative, matching the unit body and its tracers: the ring has to read as
            // "one of mine died" on one screen and "one of theirs" on the other.
            Color teamColor = TeamPalette.BrightForViewer(
                GameLoop.IsTeamFriendlyToLocalPlayer(
                    gameObject.CompareTag("BlueTeam")
                        ? GameLoop.HostTeamIndex
                        : GameLoop.OpponentTeamIndex
                )
            );
            ImpactShockwave.Spawn(transform.position, teamColor, 2.2f, 0.5f);
        }

        // The host/server controls its active state directly. Death hides the remote object;
        // fog-authorized respawn observers are reactivated after visibility is resolved.
        if (!IsServer && !newValue)
            gameObject.SetActive(false);
    }

    private void UpdateHealthFill(float health)
    {
        if (healthFill == null)
            return;

        float fraction = Mathf.Clamp(health / unitData.maxHealth, 0f, 1f);
        healthFill.localScale = new Vector3(fraction, 1f, 1f);

        // Same ramp and same thresholds as the HUD card. These two bars show one number, and
        // before this one of them was permanently green while the other was permanently red.
        if (healthFillImage != null)
            healthFillImage.color = TeamPalette.ForHealthFraction(fraction);
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
