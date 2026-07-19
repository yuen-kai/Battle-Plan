using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Shield : Ability
{
    private float abilityTime = 3f;
    private Transform shieldTransform;

    // NetworkVariable (not ClientRpc) so shield state survives fog NetworkHide/NetworkShow:
    // a unit revealed mid-shield renders the correct state from the resynced value.
    private NetworkVariable<bool> shieldActive = new(false);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        shieldTransform = transform.Find("Shield");
        shieldActive.OnValueChanged += OnShieldActiveChanged;
        ApplyShieldState(shieldActive.Value);
    }

    public override void OnNetworkDespawn()
    {
        shieldActive.OnValueChanged -= OnShieldActiveChanged;
        base.OnNetworkDespawn();
    }

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        shieldActive.Value = true;
        yield return new WaitForSeconds(abilityTime);
        shieldActive.Value = false;
    }

    private void OnShieldActiveChanged(bool previousValue, bool newValue)
    {
        ApplyShieldState(newValue);
    }

    private void ApplyShieldState(bool active)
    {
        if (shieldTransform == null)
            shieldTransform = transform.Find("Shield");
        if (shieldTransform != null)
            shieldTransform.gameObject.SetActive(active);

        // Materialization ring on every peer (NetworkVariable callback runs everywhere).
        if (active && gameObject.activeInHierarchy)
        {
            Color teamColor =
                GetComponent<Unit>()?.TeamIndex == GameLoop.HostTeamIndex
                    ? new Color(0.22f, 0.78f, 1f)
                    : new Color(1f, 0.23f, 0.33f);
            ImpactShockwave.Spawn(transform.position, teamColor, 1.6f, 0.35f);
        }
    }
}
