using UnityEngine;

/// <summary>
/// The sound of giving orders. Everything here describes the local player's own planning, so none
/// of it can leak information: the routes, the selections and the commit state are all things this
/// client authored.
///
/// <para>Its one piece of state is the waypoint counter behind the path tick. Each cell added to a
/// route raises the tick by 40 cents and the walk resets when the route does, so drawing a
/// five-cell path winds audibly upward — the closest thing the game has to a signature sound.</para>
/// </summary>
public static class PlanningAudio
{
    private const float CentsPerWaypoint = 40f;
    private const int MaxWalkedWaypoints = 12;

    private static int lastRouteLength;
    private static Vector3? lastRejectedCell;

    /// <summary>Called whenever the selected unit's drawn route changes length.</summary>
    public static void RouteChanged(int waypointCount)
    {
        if (waypointCount == lastRouteLength)
            return;

        bool extended = waypointCount > lastRouteLength;
        int step = Mathf.Clamp(waypointCount - 1, 0, MaxWalkedWaypoints);
        lastRouteLength = waypointCount;
        lastRejectedCell = null;

        // A route collapsing back to its start cell is a reset, not an erase; it has no tick.
        if (waypointCount <= 1 && !extended)
            return;

        BattlePlanAudio.Play(
            extended ? AudioCueId.PathNodeAdd : AudioCueId.PathNodeRemove,
            pitchScale: BattlePlanAudio.SemitonesToPitch(step * CentsPerWaypoint / 100f)
        );
    }

    public static void RouteReset()
    {
        lastRouteLength = 0;
        lastRejectedCell = null;
    }

    /// <summary>
    /// The drag is resting on a square the route cannot reach. A held pointer re-tests the same
    /// square every frame, so this speaks once per square rather than once per frame; the cue's own
    /// repeat limit is the backstop if a drag jitters between two bad cells.
    /// </summary>
    public static void RouteRejected(Vector3 cell)
    {
        if (lastRejectedCell.HasValue && lastRejectedCell.Value == cell)
            return;

        lastRejectedCell = cell;
        BattlePlanAudio.Play(AudioCueId.PathInvalid);
    }

    public static void UnitSelected(GameObject unit)
    {
        RouteReset();
        BattlePlanAudio.Play(unit != null ? AudioCueId.UnitSelect : AudioCueId.UnitDeselect);
    }

    public static void AbilityModeChanged(bool abilityMode)
    {
        RouteReset();
        BattlePlanAudio.Play(abilityMode ? AudioCueId.AbilityModeEnter : AudioCueId.AbilityModeExit);
    }

    /// <summary>
    /// Maps the HUD's commit state machine onto the latch. The states arrive as the same strings
    /// the HUD puts on screen, which keeps this in step with what the player is reading.
    /// </summary>
    public static void CommitStateChanged(string status)
    {
        switch (status)
        {
            case "SENDING":
                BattlePlanAudio.Play(AudioCueId.LockIn);
                break;
            case "WAITING":
                BattlePlanAudio.Play(AudioCueId.LockInWaiting);
                break;
            case "UNLOCKING":
                BattlePlanAudio.Play(AudioCueId.Unlock);
                break;
        }
    }
}
