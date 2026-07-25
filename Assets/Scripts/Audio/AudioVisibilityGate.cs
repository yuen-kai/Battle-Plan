using UnityEngine;

/// <summary>
/// The fog-of-war contract for sound.
///
/// <para>Battle Plan hides enemy units from a client outright: a fogged enemy is absent from that
/// client's spawn table, and on the host — which cannot hide from itself — its renderers are
/// force-disabled instead. Audio has no equivalent of <c>forceRenderingOff</c>, so a positional
/// cue played without a check would announce an enemy's exact cell to a player who is not allowed
/// to know it. That is a competitive advantage, not a cosmetic bug.</para>
///
/// <para>This gate answers one question — <i>can the local team see the cell this happened
/// in?</i> — using the same visible-cell set the client fog overlay darkens the board with, so a
/// sound is audible exactly when the tile it happens on is lit. Cues that must pierce fog declare
/// <see cref="AudioVisibilityRule.AlwaysAudible"/> and are justified individually in
/// <see cref="AudioCueTable"/>.</para>
/// </summary>
public static class AudioVisibilityGate
{
    /// <summary>
    /// Whether a cue at <paramref name="worldPosition"/> may be heard by the local player.
    /// <paramref name="sourceUnit"/> is optional and only consulted for
    /// <see cref="AudioVisibilityRule.VisibleCellOrOwnUnit"/>: you always hear your own crew.
    /// </summary>
    public static bool IsAudible(
        Vector3 worldPosition,
        AudioVisibilityRule rule,
        GameObject sourceUnit = null
    )
    {
        if (rule == AudioVisibilityRule.AlwaysAudible)
            return true;

        GameLoop gameLoop = GameLoop.Instance;

        // Outside a match (menus, crew select) there is no hidden information to protect.
        if (gameLoop == null || !gameLoop.FogOfWarEnabled)
            return true;

        if (rule == AudioVisibilityRule.VisibleCellOrOwnUnit && IsLocalTeamUnit(sourceUnit))
            return true;

        return gameLoop.IsCellVisibleToLocalTeam(
            GridSystem.ConvertToGridCoords(worldPosition)
        );
    }

    public static bool IsLocalTeamUnit(GameObject unit)
    {
        if (unit == null)
            return false;

        Unit identity = unit.GetComponentInParent<Unit>();
        return identity != null && GameLoop.IsTeamFriendlyToLocalPlayer(identity.TeamIndex);
    }
}
