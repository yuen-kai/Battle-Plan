using System.Collections.Generic;
using UnityEngine;

public partial class PlanMovement
{
    void ResetVisualPlan()
    {
        PathRibbon ribbon = EnsureRibbon(selectedUnit);
        currentRibbon = ribbon;
        ribbon?.Clear();
    }

    /// <summary>
    /// Roster slot drives both a unit's route colour and its lane, so a route stays tied to the
    /// same unit card all match. Falls back to team ordering if identity has not replicated yet.
    /// </summary>
    private int GetRosterSlot(GameObject unit)
    {
        Unit identity = unit != null ? unit.GetComponent<Unit>() : null;
        int slot = identity != null ? identity.RosterSlot : -1;
        return slot >= 0 ? slot : Mathf.Max(0, teamCharacters.IndexOf(unit));
    }

    private PathRibbon EnsureRibbon(GameObject unit)
    {
        if (unit == null)
            return null;
        if (planVisuals.TryGetValue(unit, out PathRibbon existing) && existing != null)
            return existing;

        int slot = GetRosterSlot(unit);
        PathRibbon ribbon = PathRibbon.Create(
            planVisualsFolder != null ? planVisualsFolder.transform : null,
            $"PlanRoute_{slot}",
            slot
        );
        planVisuals[unit] = ribbon;
        return ribbon;
    }

    /// <summary>
    /// Redraws after <see cref="PathSelection"/> extends or trims the selected route. Editing one
    /// route changes which cells are shared, so every route is rebuilt rather than just this one.
    /// </summary>
    public void NotifyCurrentRouteChanged()
    {
        RefreshAllRibbons();
    }

    /// <summary>
    /// Repaints every route so the unit being edited reads bright and full width while the rest
    /// stay dimmed but legible, and each route holds the centre of its cells except where it has
    /// to share them.
    /// </summary>
    private void RefreshAllRibbons()
    {
        currentRibbon = selectedUnit != null ? EnsureRibbon(selectedUnit) : null;
        RebuildLaneMap();
        foreach (KeyValuePair<GameObject, PathRibbon> pair in planVisuals)
        {
            if (pair.Value == null)
                continue;

            pair.Value.SetSelected(pair.Key == HighlightedUnit);
            pair.Value.SetEndBlocked(IsRouteEndBlocked(pair.Key));
            DrawRoute(pair.Key, pair.Value);
        }
    }

    private void RebuildLaneMap()
    {
        laneMap.Clear();
        foreach (KeyValuePair<GameObject, (bool, List<Vector3>)> entry in plans)
        {
            // Ability plans hold a target square rather than a route, so they claim no lanes.
            if (entry.Value.Item1 || entry.Value.Item2 == null)
                continue;

            laneMap.AddRoute(GetRosterSlot(entry.Key), entry.Value.Item2);
        }
    }

    private void DrawRoute(GameObject unit, PathRibbon ribbon)
    {
        if (ribbon == null)
            return;

        bool hasPlan = plans.TryGetValue(unit, out (bool, List<Vector3>) plan);
        if (!hasPlan || plan.Item1 || plan.Item2 == null)
            ribbon.Clear();
        else
            ribbon.SetRoute(plan.Item2, laneMap);
    }

    void InitializeVisuals()
    {
        plans = new PathsDict();
        planVisuals = new Dictionary<GameObject, PathRibbon>();
        abilityVisuals = new Dictionary<GameObject, GameObject>();
        currentRibbon = null;
        AddCharacterOutlines();
        planVisualsFolder = new GameObject("PlanVisuals");
    }

    void ClearVisuals()
    {
        RemoveCharacterOutlines();
        if (planVisualsFolder != null)
        {
            planVisualsFolder.SetActive(false);
            Destroy(planVisualsFolder);
        }
        if (moveOverlay != null)
        {
            moveOverlay.SetActive(false);
            Destroy(moveOverlay);
        }
        ClearAbilityIndicators();
        planVisuals.Clear();
        laneMap.Clear();
        currentRibbon = null;
        planVisualsFolder = null;
        moveOverlay = null;
        planningRangeOverride = -1;

        if (useUnitCards)
        {
            GameHUDController.Instance?.ClearCardPlanningStates();
        }
    }

    void DisplayMoveRange(int range, Color tint, bool includeOwnCell = true)
    {
        Destroy(moveOverlay);
        if (selectedUnit == null)
            return;

        moveOverlay = GridSystem.DisplayGridRange(
            GridSystem.GetNearestGridCell(selectedUnit),
            range,
            moveOverlayCellPrefab,
            tint,
            includeOwnCell
        );
    }

    void DisplayAbilityDirections()
    {
        Destroy(moveOverlay);
        if (selectedUnit == null)
            return;

        moveOverlay = GridSystem.DisplayGridDirections(
            GridSystem.GetNearestGridCell(selectedUnit),
            moveOverlayCellPrefab,
            TeamPalette.AbilityRange
        );
    }

    void AddCharacterOutlines()
    {
        foreach (GameObject character in teamCharacters)
        {
            if (character == null)
                continue;

            Renderer[] renderers = character.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in renderers)
            {
                if (rend != null)
                    rend.renderingLayerMask = 1 << 1;
            }
        }
    }

    void RemoveCharacterOutlines()
    {
        foreach (GameObject character in teamCharacters)
        {
            if (character == null)
                continue;

            Renderer[] renderers = character.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in renderers)
            {
                if (rend != null)
                    rend.renderingLayerMask = 1 << 0;
            }
        }
    }

    public static void PrintPlans(PathsDict actions)
    {
        foreach (var pair in actions)
        {
            var (boolValue, path) = pair.Value;
            Debug.Log(
                $"Unit: {pair.Key.name}, Boolean: {boolValue}, Path: {string.Join(", ", path)}"
            );
        }
    }
}
