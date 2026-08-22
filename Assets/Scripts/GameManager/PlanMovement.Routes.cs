using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public partial class PlanMovement
{
    /// <summary>
    /// Selects the team unit occupying the given cell and prepares its movement path for editing.
    /// This is essential during dodge windows, where the unit cards don't map to the alerted set.
    /// </summary>
    public bool TrySelectUnitForMovementAtCell(Vector3 cell)
    {
        if (!CanEditPlan)
            return false;

        foreach (GameObject character in teamCharacters)
        {
            if (!IsPlanningUnitAvailable(character) || character == selectedUnit)
                continue;
            if (GridSystem.GetNearestGridCell(character) == cell)
                return TrySetSelectionMode(character, false);
        }
        return false;
    }

    // Two routes drawn closer together than this within one cell count as pointed at equally.
    private const float LaneTieDistance = 0.02f;

    /// <summary>
    /// Finds the drawn movement route nearest the pointer inside the cell it is over, reporting the
    /// owner and the index of that cell within the owner's plan. Candidates are compared against
    /// the position each ribbon actually drew, so where several routes share a cell you get the one
    /// you are pointing at; the selected unit only breaks a genuine tie.
    /// </summary>
    public bool TryFindPlannedRouteAtPoint(
        Vector3 worldPoint,
        out GameObject unit,
        out int planIndex
    )
    {
        unit = null;
        planIndex = -1;
        if (!CanEditPlan)
            return false;

        Vector3 cell = GridSystem.GetNearestGridCell(worldPoint);
        float bestDistance = float.MaxValue;

        foreach (GameObject character in teamCharacters)
        {
            if (!TryFindRouteCellForUnit(character, cell, out int index))
                continue;

            float distance = GetDrawnRouteDistance(character, index, worldPoint, cell);
            bool clearlyCloser = distance < bestDistance - LaneTieDistance;
            bool tiedButSelected =
                character == selectedUnit && distance <= bestDistance + LaneTieDistance;
            if (unit != null && !clearlyCloser && !tiedButSelected)
                continue;

            unit = character;
            planIndex = index;
            bestDistance = Mathf.Min(bestDistance, distance);
        }
        return unit != null;
    }

    /// <summary>
    /// Flat distance from the pointer to where a unit's ribbon drew the given plan step, falling
    /// back to the cell centre for a plan too short to have been drawn.
    /// </summary>
    private float GetDrawnRouteDistance(
        GameObject unit,
        int planIndex,
        Vector3 worldPoint,
        Vector3 cell
    )
    {
        Vector3 reference = cell;
        if (
            planVisuals.TryGetValue(unit, out PathRibbon ribbon)
            && ribbon != null
            && ribbon.TryGetDrawnPoint(planIndex, out Vector3 drawn)
        )
        {
            reference = drawn;
        }

        Vector2 delta = new(worldPoint.x - reference.x, worldPoint.z - reference.z);
        return delta.magnitude;
    }

    private bool TryFindRouteCellForUnit(GameObject unit, Vector3 cell, out int planIndex)
    {
        planIndex = -1;
        if (
            unit == null
            || !IsPlanningUnitAvailable(unit)
            || !plans.TryGetValue(unit, out (bool, List<Vector3>) plan)
            || plan.Item1
            || plan.Item2 == null
        )
        {
            return false;
        }

        planIndex = plan.Item2.IndexOf(cell);
        return planIndex >= 0;
    }

    /// <summary>
    /// Selects the unit that owns a drawn route and prepares that unit for movement editing.
    /// </summary>
    public bool TrySelectUnitForRoute(GameObject unit)
    {
        if (!CanEditPlan || unit == null)
            return false;

        return unit == selectedUnit || TrySetSelectionMode(unit, false);
    }

    /// <summary>
    /// Turns the unit being edited to its ability, for the board-side click on a unit that is not
    /// going anywhere. A unit holding a route keeps it, so the click that gives up a route is never
    /// also the one that changes what the unit is doing with its round.
    /// </summary>
    public bool TrySwitchToAbilityPlan(GameObject unit)
    {
        // A dodge response is movement only — the server throws away an ability-flagged plan — so
        // the gesture is offered in the same window the cards are, and nowhere else.
        if (!CanEditPlan || !useUnitCards || unit == null || unit != selectedUnit)
            return false;
        if (!plans.TryGetValue(unit, out (bool, List<Vector3>) plan))
            return false;
        if (plan.Item1 || (plan.Item2?.Count ?? 0) > 1)
            return false;

        return TrySetSelectionMode(unit, true);
    }

    /// <summary>
    /// Resolves a click that landed on one of your own units rather than on the board. Pointing at
    /// a unit asks for that unit: another unit takes over the selection, and the unit already
    /// holding it turns over to its other order. The dock reaches the same two states from the
    /// ability's side, so a player who never discovers this gesture is not locked out of anything.
    /// Returns true when the click was spent on the selection and must not also be read as naming
    /// a square.
    /// </summary>
    public bool TrySelectPlanningUnit(GameObject unit)
    {
        // teamCharacters is the set that may be given orders, which during a dodge window is only
        // the alerted units — the rest of the team is on the board but is not taking any.
        if (!CanEditPlan || unit == null || !teamCharacters.Contains(unit))
            return false;
        if (!IsPlanningUnitAvailable(unit))
            return false;

        if (unit != selectedUnit)
        {
            SwitchToUnit(unit);
            return selectedUnit == unit;
        }

        bool abilityMode = plans.TryGetValue(unit, out (bool, List<Vector3>) plan) && plan.Item1;
        return abilityMode
            ? TrySetSelectionMode(unit, false)
            // Turning the other way has rules of its own — no abilities in a dodge, and a drawn
            // route is given up before it is replaced — and they are kept in one place.
            : TrySwitchToAbilityPlan(unit);
    }

    private static bool IsPlanningUnitAvailable(GameObject unit)
    {
        if (unit == null || !unit.activeInHierarchy)
            return false;

        Health health = unit.GetComponent<Health>();
        return health == null || health.IsAlive;
    }

    /// <summary>
    /// Populates the teamCharacters list with units belonging to the local client's team
    /// </summary>
    private List<GameObject> PopulateTeamCharacters()
    {
        List<GameObject> localTeamCharacters = new();

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
        {
            Debug.LogWarning(
                "[PlanMovement] NetworkManager not available, cannot populate team characters"
            );
            return localTeamCharacters;
        }

        int localTeamIndex = GameLoop.Instance != null ? GameLoop.Instance.LocalTeamIndex : -1;
        if (localTeamIndex < 0)
            return localTeamCharacters;

        localTeamCharacters.AddRange(
            NetworkManager
                .Singleton.SpawnManager.SpawnedObjectsList.Where(netObj => netObj != null)
                .Select(netObj => netObj.GetComponent<Unit>())
                .Where(unit =>
                    unit != null
                    && unit.TeamIndex == localTeamIndex
                    && unit.RosterSlot >= 0
                )
                .OrderBy(unit => unit.RosterSlot)
                .Select(unit => unit.gameObject)
        );

        return localTeamCharacters;
    }

    private bool TryRefreshTeamCharactersForSession(int sessionVersion)
    {
        if (sessionVersion != planningSessionVersion || !planningActive)
            return false;

        teamCharacters = PopulateTeamCharacters();
        reservationUnits = teamCharacters;
        return true;
    }

    /// <summary>
    /// Which entry of a plan holds the cell its unit is standing on when the round resolves: the
    /// last step of a route, or the first cell for a unit spending the round on an ability, since
    /// a caster does not march anywhere during execution. -1 for a plan with nothing in it.
    /// </summary>
    public static int GetPlannedEndIndex(bool abilityMode, int planLength)
    {
        if (planLength <= 0)
            return -1;

        return abilityMode ? 0 : planLength - 1;
    }

    /// <summary>The cell a plan leaves its unit standing on; the unit's own cell without one.</summary>
    private Vector3 GetPlannedEndCell(GameObject unit)
    {
        if (plans.TryGetValue(unit, out (bool, List<Vector3>) plan))
        {
            int endIndex = GetPlannedEndIndex(plan.Item1, plan.Item2?.Count ?? 0);
            if (endIndex >= 0)
                return plan.Item2[endIndex];
        }
        return GridSystem.GetNearestGridCell(unit);
    }

    /// <summary>
    /// Whether another unit of this team already finishes the round standing on the given cell.
    /// Two units cannot share a square, so this is the cell a route may pass over but not stop on.
    /// </summary>
    public bool IsEndCellHeldByAnotherUnit(Vector3 cell, GameObject excludedUnit)
    {
        Vector2Int target = GridSystem.ConvertToGridCoords(cell);
        foreach (GameObject unit in reservationUnits)
        {
            if (unit == excludedUnit || !IsPlanningUnitAvailable(unit))
                continue;
            if (GridSystem.ConvertToGridCoords(GetPlannedEndCell(unit)) == target)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a unit's route currently stops on a square a team-mate finishes on. Routes are free
    /// to run through those squares, so this end state is reachable mid-drag and is drawn as
    /// unusable until the route is settled.
    /// </summary>
    public bool IsRouteEndBlocked(GameObject unit)
    {
        if (unit == null || !plans.TryGetValue(unit, out (bool, List<Vector3>) plan))
            return false;
        if (plan.Item1 || plan.Item2 == null || plan.Item2.Count < 2)
            return false;

        return IsEndCellHeldByAnotherUnit(plan.Item2[^1], unit);
    }

    /// <summary>
    /// Gives up the trailing cells of a route until it stops somewhere its team leaves free. A
    /// route may be drawn across a team-mate's destination, so this is what holds the player to a
    /// destination they can actually keep, rather than letting the server silently cut orders the
    /// player already believed were given.
    /// </summary>
    public void TrimRouteToLastFreeCell(GameObject unit)
    {
        if (unit == null || !plans.TryGetValue(unit, out (bool, List<Vector3>) plan))
            return;

        List<Vector3> route = plan.Item2;
        if (plan.Item1 || route == null || route.Count < 2)
            return;

        int endIndex = route.Count - 1;
        while (endIndex > 0 && IsEndCellHeldByAnotherUnit(route[endIndex], unit))
            endIndex--;
        if (endIndex == route.Count - 1)
            return;

        route.RemoveRange(endIndex + 1, route.Count - (endIndex + 1));
        if (unit == selectedUnit)
            currentPlan = route;
        RefreshAllRibbons();
    }

    /// <summary>
    /// Settles every unit's route so no two of them finish on the same square, matching the order
    /// the server resolves them in so the board a player commits is the board they get. Shorter
    /// orders are honoured first, which leaves a unit holding its ground on the cell it occupies
    /// and cuts short the one walking into it.
    /// </summary>
    private void TrimAllRoutesToFreeCells()
    {
        HashSet<Vector2Int> claimed = new();
        List<(GameObject unit, int rosterSlot, List<Vector3> route)> movers = new();
        foreach (GameObject unit in reservationUnits)
        {
            if (!IsPlanningUnitAvailable(unit))
                continue;

            bool hasPlan = plans.TryGetValue(unit, out (bool, List<Vector3>) plan);
            if (!hasPlan || plan.Item1 || plan.Item2 == null || plan.Item2.Count < 2)
            {
                claimed.Add(GridSystem.ConvertToGridCoords(GetPlannedEndCell(unit)));
                continue;
            }
            movers.Add((unit, GetRosterSlot(unit), plan.Item2));
        }

        // Must match the order the server resolves these in, or the board the player is shown at
        // commit is not the board the round is run from.
        movers.Sort(
            (left, right) =>
            {
                int lengthComparison = left.route.Count.CompareTo(right.route.Count);
                return lengthComparison != 0
                    ? lengthComparison
                    : left.rosterSlot.CompareTo(right.rosterSlot);
            }
        );
        List<int> lengths = GameLoop.ResolveUniqueEndCellLengths(
            movers
                .Select(mover =>
                    (IReadOnlyList<Vector2Int>)
                        mover.route.Select(GridSystem.ConvertToGridCoords).ToList()
                )
                .ToList(),
            claimed
        );

        bool trimmedAny = false;
        for (int i = 0; i < movers.Count; i++)
        {
            List<Vector3> route = movers[i].route;
            if (lengths[i] >= route.Count)
                continue;

            route.RemoveRange(lengths[i], route.Count - lengths[i]);
            trimmedAny = true;
            if (movers[i].unit == selectedUnit)
                currentPlan = route;
        }

        if (trimmedAny)
            RefreshAllRibbons();
    }
}
