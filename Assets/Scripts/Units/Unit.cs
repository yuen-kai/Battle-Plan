using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class Unit : NetworkBehaviour
{
    public List<Material> teamMaterials;
    private readonly NetworkVariable<int> teamIndex = new(-1);
    private readonly NetworkVariable<int> rosterSlot = new(-1);
    private readonly NetworkVariable<int> remainingAbilityUses = new(0);

    public int TeamIndex => teamIndex.Value;
    public int RosterSlot => rosterSlot.Value;
    public int RemainingAbilityUses => remainingAbilityUses.Value;
    public bool CanUseAbility => GetComponent<Ability>() != null && RemainingAbilityUses > 0;
    public bool IsFriendlyToLocalPlayer =>
        GameLoop.Instance != null
        && GameLoop.Instance.LocalTeamIndex >= 0
        && TeamIndex == GameLoop.Instance.LocalTeamIndex;

    [HideInInspector]
    public bool selectMovement = true;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            UnitData data = GetUnitData();
            remainingAbilityUses.Value = GetInitialAbilityUses(
                data,
                GetComponent<Ability>() != null
            );
        }
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

    public bool TryConsumeAbilityUse()
    {
        if (!IsServer || GetComponent<Ability>() == null)
            return false;

        int remaining = remainingAbilityUses.Value;
        if (!TryConsumeAbilityCharge(ref remaining))
            return false;

        remainingAbilityUses.Value = remaining;
        return true;
    }

    public static int GetInitialAbilityUses(UnitData data, bool abilityPresent)
    {
        return abilityPresent && data != null ? Mathf.Max(0, data.uses) : 0;
    }

    public static bool TryConsumeAbilityCharge(ref int remainingUses)
    {
        if (remainingUses <= 0)
            return false;

        remainingUses--;
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
        cone.SetColor(
            IsFriendlyToLocalPlayer
                ? new Color(0.92f, 0.95f, 1f, 0.15f)
                : new Color(1f, 0.3f, 0.32f, 0.08f)
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
