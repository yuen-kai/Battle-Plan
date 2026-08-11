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

    private AbilityStatusRing abilityStatusRing;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        teamIndex.OnValueChanged += OnTeamIndexChanged;
        abilityCooldownRoundsRemaining.OnValueChanged += OnAbilityCooldownRoundsChanged;
        RefreshTeamPresentation();
        // Not animated: the cooldown a unit spawns with, or comes back out of fog with, is state
        // that was already true before this client could see it, not a recharge to play out.
        RefreshAbilityStatusRing(animate: false);
    }

    public override void OnNetworkDespawn()
    {
        teamIndex.OnValueChanged -= OnTeamIndexChanged;
        abilityCooldownRoundsRemaining.OnValueChanged -= OnAbilityCooldownRoundsChanged;
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

    private void OnAbilityCooldownRoundsChanged(int previousValue, int newValue)
    {
        RefreshAbilityStatusRing(animate: true);
    }

    /// <summary>
    /// Points the charge dial on this unit's base plate at its cooldown, building it on first use.
    /// The HUD card carries the same number, but only for units that fit on the card strip and
    /// only while the player is looking away from the board — this is the copy that is on screen
    /// whenever the unit it belongs to is.
    /// </summary>
    private void RefreshAbilityStatusRing(bool animate)
    {
        int configuredRounds = ConfiguredAbilityCooldownRounds;
        if (configuredRounds <= 0)
            return;

        if (abilityStatusRing == null)
            abilityStatusRing = AbilityStatusRing.Create(transform);
        if (abilityStatusRing == null)
            return;

        abilityStatusRing.SetCharge(AbilityCooldownRoundsRemaining, configuredRounds, animate);
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
