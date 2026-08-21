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

    // One replicated write per stun rather than a per-frame countdown, the same trade Movement's
    // DiveRecoveryState makes: the indicator's fill is a pure function of elapsed server time, so
    // every peer draws the right point in it from the moment it learns about it, fog reveals
    // midway through included.
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

    /// <summary>True for the short, wall-clock window a knockback or similar hit has interrupted
    /// this unit for. Not round-based: it is meant to resolve inside the same round's execution
    /// window it was applied in.</summary>
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
        // Not animated: the cooldown a unit spawns with, or comes back out of fog with, is state
        // that was already true before this client could see it, not a recharge to play out.
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

    /// <summary>
    /// Hard-interrupts this unit the way it already interrupts itself for a shield rush, a pogo
    /// jump or an area lock — movement paused, weapons stood down — then hands it back at the end
    /// of a short, wall-clock window instead of a round boundary. A second call mid-window
    /// restarts the timer rather than stacking a second recovery behind the first.
    /// </summary>
    public void ApplyStun(float duration)
    {
        if (!IsServer || duration <= 0f)
            return;

        Movement movement = GetComponent<Movement>();
        Shooting shooting = GetComponent<Shooting>();
        if (movement == null || shooting == null)
            return;

        movement.PauseMovement();
        movement.moving = false;
        shooting.StandDown();

        if (stunCoroutine != null)
            StopCoroutine(stunCoroutine);

        stunState.Value = new StunState(NetworkManager.ServerTime.Time, duration);

        // Guarded so this stays callable from an edit-mode test that forces IsServer without a
        // running player loop; coroutines are otherwise only ever started in a live match.
        if (Application.isPlaying)
            stunCoroutine = StartCoroutine(ResumeAfterStun(duration));
    }

    private IEnumerator ResumeAfterStun(float duration)
    {
        yield return new WaitForSeconds(duration);
        stunCoroutine = null;
        ClearStun();
    }

    private void ClearStun()
    {
        if (IsServer && stunState.Value.Active)
            stunState.Value = default;

        // Mirrors how a dodger picks back up after its dive recovery: whatever the unit would
        // normally be doing resumes rather than staying stood down.
        GetComponent<Movement>()?.transitionToShooting();
    }

    /// <summary>Whether <paramref name="state"/> is still an active stun at <paramref name="serverTime"/>.</summary>
    public static bool IsStunnedAt(StunState state, double serverTime)
    {
        return state.Active && state.ProgressAt(serverTime) < 1f;
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
