using System.Collections;
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

    private readonly NetworkVariable<StunState> stunState = new();

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

    public bool IsStunned => IsStunnedAt(stunState.Value, CurrentServerTime);

    [HideInInspector]
    public bool selectMovement = true;

    private AbilityStatusRing abilityStatusRing;
    private StunPulse stunPulse;
    private Coroutine stunCoroutine;

    private double CurrentServerTime =>
        NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        teamIndex.OnValueChanged += OnTeamIndexChanged;
        abilityCooldownRoundsRemaining.OnValueChanged += OnAbilityCooldownRoundsChanged;
        RefreshTeamPresentation();
        RefreshAbilityStatusRing(animate: false);

        stunState.OnValueChanged += OnStunStateChanged;
        RefreshStunPresentation(stunState.Value);
    }

    public override void OnNetworkDespawn()
    {
        teamIndex.OnValueChanged -= OnTeamIndexChanged;
        abilityCooldownRoundsRemaining.OnValueChanged -= OnAbilityCooldownRoundsChanged;
        stunState.OnValueChanged -= OnStunStateChanged;
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

    public void ApplyStun(float duration)
    {
        if (!IsServer || duration <= 0f)
            return;

        Movement movement = GetComponent<Movement>();
        Shooting shooting = GetComponent<Shooting>();
        if (movement == null || shooting == null)
            return;

        if (stunCoroutine != null)
            StopCoroutine(stunCoroutine);

        stunState.Value = new StunState(NetworkManager.ServerTime.Time, duration);
        GetComponent<Ability>()?.InterruptForStun();

        movement.PauseMovement();
        movement.moving = false;
        transform.position =
            GridSystem.GetNearestGridCell(transform.position) + Helper.heightOffset(transform);
        Physics.SyncTransforms();
        shooting.StandDown();
        stunCoroutine = StartCoroutine(ResumeAfterStun(duration));
    }

    private IEnumerator ResumeAfterStun(float duration)
    {
        yield return new WaitForSeconds(duration);
        stunCoroutine = null;
        ClearStun();
    }

    /// <summary>Clears the round's statuses and sets the ability charge a recorded board had.</summary>
    public void ServerRestoreState(int cooldownRounds)
    {
        if (!IsServer)
            return;
        if (stunCoroutine != null)
        {
            StopCoroutine(stunCoroutine);
            stunCoroutine = null;
        }
        if (stunState.Value.Active)
            stunState.Value = default;
        abilityCooldownRoundsRemaining.Value = Mathf.Clamp(cooldownRounds, 0, Mathf.Max(0, ConfiguredAbilityCooldownRounds));
        GetComponent<Movement>()?.ClearDiveRecovery();
        GetComponent<Movement>()?.ClearTemporaryMoveSpeedBoost();
        GetComponent<Health>()?.ClearDamageReduction();
        GetComponent<Shooting>()?.ServerClearTargetLock();
    }

    private void ClearStun()
    {
        if (IsServer && stunState.Value.Active)
            stunState.Value = default;

        TryResumeShooting(true);
    }

    public static bool IsStunnedAt(StunState state, double serverTime)
    {
        return state.Active && state.ProgressAt(serverTime) < 1f;
    }

    public void PauseShootingForAbility()
    {
        Movement movement = GetComponent<Movement>();
        if (movement != null)
        {
            movement.PauseMovement();
            movement.moving = false;
        }
        GetComponent<Shooting>()?.PauseShooting();
    }

    public void ResumeShootingAfterAbility()
    {
        TryResumeShooting(onlyIfWeaponsStillFree: true);
    }

    private void TryResumeShooting(bool onlyIfWeaponsStillFree = false)
    {
        if (!IsStunned)
            GetComponent<Movement>()?.transitionToShooting(onlyIfWeaponsStillFree);
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

    public bool RefundAbilityCooldown()
    {
        if (!IsServer)
            return false;

        int remaining = abilityCooldownRoundsRemaining.Value;
        if (!RefundAbilityCooldown(ref remaining))
            return false;

        abilityCooldownRoundsRemaining.Value = remaining;
        return true;
    }

    /// <summary>
    /// Hands the ability straight back, for the sandbox. Reports whether anything was recharging.
    /// </summary>
    public bool ClearAbilityCooldown()
    {
        if (!IsServer || abilityCooldownRoundsRemaining.Value == 0)
            return false;

        abilityCooldownRoundsRemaining.Value = 0;
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

    public static bool RefundAbilityCooldown(ref int remainingRounds)
    {
        remainingRounds = Mathf.Max(0, remainingRounds);
        if (remainingRounds == 0)
            return false;

        remainingRounds = 0;
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

    private void OnStunStateChanged(StunState previousValue, StunState newValue)
    {
        RefreshStunPresentation(newValue);
    }

    private void RefreshStunPresentation(StunState state)
    {
        if (state.Active && stunPulse == null)
            stunPulse = StunPulse.Attach(gameObject);

        if (stunPulse != null)
            stunPulse.SetStun(state);
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
        ConfigureVisionCone(cone, IsFriendlyToLocalPlayer, GetUnitData());
    }

    public static void ConfigureVisionCone(VisionConeVisual cone, bool friendly, UnitData unitData)
    {
        cone.SetColor(
            friendly
                ? new Color(0.92f, 0.95f, 1f, 0.18f)
                : TeamPalette.EnemyBright.WithAlpha(0.10f)
        );

        // The flashlight is the weapon envelope, not fog vision:
        // bulletSpread is one side of center, while VisionConeVisual expects the full angle.
        // targetRange is stored in cells, so convert it to world distance.
        if (unitData != null)
        {
            cone.SetWeaponEnvelope(unitData.bulletSpread, unitData.targetRange * GameLoop.cellSize);
        }
    }

    void SetTeamIndicators() =>
        ApplyTeamIndicators(gameObject, teamMaterials, IsFriendlyToLocalPlayer);

    /// <summary>
    /// Paints a body's team props, live or not. A display copy of a unit has lost this component
    /// along with every other behaviour, so it is handed the prefab's materials instead.
    /// </summary>
    public static void ApplyTeamIndicators(GameObject body, List<Material> materials, bool friendly)
    {
        if (body == null || materials == null || materials.Count < 2)
            return;

        foreach (Transform part in body.GetComponentsInChildren<Transform>())
        {
            if (!part.CompareTag("TeamIndicatorProp"))
                continue;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
                renderer.materials = new[] { friendly ? materials[0] : materials[1] };
        }
    }

    private UnitData GetUnitData()
    {
        return GetComponent<Shooting>()?.unitData ?? GetComponent<Movement>()?.unitData;
    }
}

/// <summary>
/// A stun as replicated: when it landed on the server clock and how long it holds. Mirrors
/// Movement's DiveRecoveryState — the indicator's fill is a pure function of elapsed time, so this
/// is written once per stun instead of every frame, and a peer that fog reveals midway through
/// still draws the right point in it.
/// </summary>
public struct StunState : INetworkSerializable, System.IEquatable<StunState>
{
    public bool Active;
    public double StartServerTime;
    public float Duration;

    public StunState(double startServerTime, float duration)
    {
        Active = true;
        StartServerTime = startServerTime;
        Duration = Mathf.Max(0.0001f, duration);
    }

    /// <summary>Stun position, 0 the instant it lands through 1 once it has worn off.</summary>
    public float ProgressAt(double serverTime)
    {
        return Mathf.Clamp01((float)((serverTime - StartServerTime) / Duration));
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Active);
        serializer.SerializeValue(ref StartServerTime);
        serializer.SerializeValue(ref Duration);
    }

    public bool Equals(StunState other)
    {
        return Active == other.Active
            && StartServerTime.Equals(other.StartServerTime)
            && Duration.Equals(other.Duration);
    }

    public override bool Equals(object obj)
    {
        return obj is StunState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return System.HashCode.Combine(Active, StartServerTime, Duration);
    }
}
