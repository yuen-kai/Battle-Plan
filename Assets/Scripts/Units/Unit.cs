using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class Unit : NetworkBehaviour
{
    public List<Material> teamMaterials;
    private readonly NetworkVariable<int> teamIndex = new(-1);
    private readonly NetworkVariable<int> rosterSlot = new(-1);
    private readonly NetworkVariable<int> abilityCooldownRoundsRemaining = new(0);

    public int TeamIndex => teamIndex.Value;
    public int RosterSlot => rosterSlot.Value;
    public int ConfiguredAbilityCooldownRounds =>
        GetConfiguredAbilityCooldownRounds(GetUnitData(), GetComponent<Ability>() != null);
    public int AbilityCooldownRoundsRemaining => abilityCooldownRoundsRemaining.Value;
    public bool CanUseAbility =>
        GetComponent<Ability>() != null && AbilityCooldownRoundsRemaining == 0;
    public bool IsFriendlyToLocalPlayer =>
        GameLoop.Instance != null
        && GameLoop.Instance.LocalTeamIndex >= 0
        && TeamIndex == GameLoop.Instance.LocalTeamIndex;

    [HideInInspector]
    public bool selectMovement = true;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        teamIndex.OnValueChanged += OnTeamIndexChanged;
        RefreshTeamPresentation();
    }

    public override void OnNetworkDespawn()
    {
        teamIndex.OnValueChanged -= OnTeamIndexChanged;
        base.OnNetworkDespawn();
    }

    public void SetTeamIndex(int value)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[Unit] Only the server may assign a logical team.");
            return;
        }
        if (value < 0 || value >= GameLoop.TeamCount)
        {
            Debug.LogError($"[Unit] Invalid logical team index {value}.");
            return;
        }

        teamIndex.Value = value;
        RefreshTeamPresentation();
    }

    public void SetRosterSlot(int value)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[Unit] Only the server may assign a roster slot.");
            return;
        }
        if (value < 0 || value >= RosterRules.UnitsPerPlayer)
        {
            Debug.LogError($"[Unit] Invalid roster slot {value}.");
            return;
        }

        rosterSlot.Value = value;
    }

    public bool TryStartAbilityCooldown()
    {
        if (!IsServer)
            return false;

        int remaining = abilityCooldownRoundsRemaining.Value;
        if (
            !TryStartAbilityCooldown(
                ref remaining,
                ConfiguredAbilityCooldownRounds,
                GetComponent<Ability>() != null
            )
        )
        {
            return false;
        }

        abilityCooldownRoundsRemaining.Value = remaining;
        return true;
    }

    public bool TickAbilityCooldownRound()
    {
        if (!IsServer)
            return false;

        int remaining = abilityCooldownRoundsRemaining.Value;
        if (!TickAbilityCooldownRound(ref remaining))
            return false;

        abilityCooldownRoundsRemaining.Value = remaining;
        return true;
    }

    public static int GetConfiguredAbilityCooldownRounds(UnitData data, bool hasAbility)
    {
        return hasAbility ? Mathf.Max(1, data != null ? data.abilityCooldownRounds : 1) : 0;
    }

    public static bool TryStartAbilityCooldown(
        ref int remainingRounds,
        int configuredRounds,
        bool hasAbility
    )
    {
        remainingRounds = Mathf.Max(0, remainingRounds);
        if (!hasAbility || remainingRounds > 0)
            return false;

        remainingRounds = Mathf.Max(1, configuredRounds);
        return true;
    }

    public static bool TickAbilityCooldownRound(ref int remainingRounds)
    {
        remainingRounds = Mathf.Max(0, remainingRounds);
        if (remainingRounds == 0)
            return false;

        remainingRounds--;
        return true;
    }

    private void OnTeamIndexChanged(int previousValue, int newValue)
    {
        RefreshTeamPresentation();
    }

    public void RefreshTeamPresentation()
    {
        SetTeamIndicators();
        SetupVisionCone();
    }

    public static void RefreshAllTeamPresentation()
    {
        foreach (
            Unit unit in Object.FindObjectsByType<Unit>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
        {
            unit.RefreshTeamPresentation();
        }
    }

    void SetupVisionCone()
    {
        VisionConeVisual cone = GetComponentInChildren<VisionConeVisual>(true);
        if (cone == null)
            return;

        // Raid-style tactical flashlights: friendly cones are neutral white with only a hint of
        // cool tint; enemy cones remain a dim red warning. Fog hides enemy cones with the unit.
        // Alphas stay low: cones are additive and overlap, so they sum up fast.
        // The friendly cone stays near-white on purpose: it is a light volume, not a team badge,
        // and paint-coloured light reads as an effect. Only the enemy cone carries team identity,
        // and it now carries the shared enemy red rather than a sixth one of its own.
        cone.SetColor(
            IsFriendlyToLocalPlayer
                ? new Color(0.92f, 0.95f, 1f, 0.18f)
                : TeamPalette.EnemyBright.WithAlpha(0.10f)
        );

        // The flashlight is the weapon envelope, not fog vision:
        // bulletSpread is one side of center, while VisionConeVisual expects the full angle.
        // targetRange is stored in cells, so convert it to world distance.
        UnitData unitData = GetUnitData();
        if (unitData != null)
        {
            cone.SetWeaponEnvelope(unitData.bulletSpread, unitData.targetRange * GameLoop.cellSize);
        }
    }

    void SetTeamIndicators()
    {
        GameObject[] teamIndicators = GetComponentsInChildren<Transform>()
            .Where(t => t.CompareTag("TeamIndicatorProp"))
            .Select(t => t.gameObject)
            .ToArray();

        foreach (GameObject indicator in teamIndicators)
        {
            Renderer renderer = indicator.GetComponent<Renderer>();
            if (renderer != null && teamMaterials != null && teamMaterials.Count >= 2)
            {
                renderer.materials = new[]
                {
                    IsFriendlyToLocalPlayer ? teamMaterials[0] : teamMaterials[1],
                };
            }
        }
    }

    private UnitData GetUnitData()
    {
        return GetComponent<Shooting>()?.unitData ?? GetComponent<Movement>()?.unitData;
    }
}
