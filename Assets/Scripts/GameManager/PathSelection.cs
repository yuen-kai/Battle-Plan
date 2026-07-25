using System.Collections.Generic;
using UnityEngine;

public class PathSelection : MonoBehaviour
{
    public static PathSelection Instance { get; private set; }

    private bool pathDragActive;

    // Set once a drag has been told its route is resting somewhere it cannot stop, so the notice is
    // stated instead of rewritten on every frame the pointer sits there.
    private bool reportedHeldCell;

    // Whose route this drag is editing. Held separately from the selection because a drag can be
    // ended by switching units, which has already moved the selection on by the time we settle.
    private GameObject draggedUnit;

    // The unit a press landed on while it was already selected with nothing drawn. Letting go on it
    // without having drawn anything turns it to its ability, so a unit that is staying where it is
    // can be pointed at a target on the board rather than through its card.
    private GameObject abilityTapUnit;

    private List<Vector3> CurrentPlan =>
        PlanMovement.Instance != null ? PlanMovement.Instance.currentPlan : null;
    private PathRibbon CurrentRibbon =>
        PlanMovement.Instance != null ? PlanMovement.Instance.currentRibbon : null;
    private GameObject SelectedUnit =>
        PlanMovement.Instance != null ? PlanMovement.Instance.selectedUnit : null;
    private float CellSize => GameLoop.cellSize;

    void Awake()
    {
        Instance = this;
    }

    public void MovementSelection(int moveDist)
    {
        if (PlanMovement.Instance?.CanEditPlan != true)
        {
            pathDragActive = false;
            return;
        }

        moveDist =
            moveDist == -1 ? SelectedUnit.GetComponent<Movement>().unitData.moveDist : moveDist;
        if (Input.GetMouseButtonDown(0))
        {
            TryStartPath();
        }
        else if (Input.GetMouseButton(0) && pathDragActive)
        {
            ExtendPath(moveDist);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            SettleRouteEnd();
            pathDragActive = false;
            ResolveAbilityTap();
        }
    }

    /// <summary>
    /// Drops a route back to the last cell it may actually stop on. A drag is free to run through a
    /// square a team-mate finishes on — routing around one would be a worse restriction than the
    /// rule is worth — but letting go there would commit orders the server only has to cut short,
    /// so the trailing cells are given up the moment the player stops asking for them.
    /// </summary>
    void SettleRouteEnd()
    {
        ClearHeldCellNotice();
        GameObject unit = draggedUnit;
        draggedUnit = null;
        if (!pathDragActive || unit == null)
            return;

        PlanMovement.Instance?.TrimRouteToLastFreeCell(unit);
    }

    public bool TryStartPath()
    {
        if (PlanMovement.Instance?.CanEditPlan != true)
        {
            pathDragActive = false;
            return false;
        }

        pathDragActive = StartPath();
        draggedUnit = pathDragActive ? SelectedUnit : null;
        return pathDragActive;
    }

    public void CancelCurrentDrag()
    {
        abilityTapUnit = null;
        SettleRouteEnd();
        pathDragActive = false;
    }

    /// <summary>
    /// Turns the selected unit to its ability when a press and release both land on it without a
    /// route being drawn in between. A unit already going somewhere is left alone: pressing it
    /// gives up its route, and only the click after that — with nothing left to abandon — reads as
    /// asking for the ability instead.
    /// </summary>
    void ResolveAbilityTap()
    {
        GameObject unit = abilityTapUnit;
        abilityTapUnit = null;
        if (unit == null)
            return;

        Vector3? released = Mouse.GetGridCellUnderMouse();
        if (released == null || released.Value != GridSystem.GetNearestGridCell(unit))
            return;

        PlanMovement.Instance?.TrySwitchToAbilityPlan(unit);
    }

    bool StartPath()
    {
        abilityTapUnit = null;
        Vector3? pointer = Mouse.GetGridPointUnderMouse();
        if (pointer == null || SelectedUnit == null)
            return false;

        // A press on the selected unit while it has drawn nothing may turn out to be the click that
        // turns it to its ability. Only the release can say, since this is also the press a route
        // is drawn out from.
        bool pressedIdleSelection =
            CurrentPlan?.Count == 1
            && GridSystem.GetNearestGridCell(pointer.Value)
                == GridSystem.GetNearestGridCell(SelectedUnit);
        if (pressedIdleSelection)
            abilityTapUnit = SelectedUnit;

        // Pressing on a drawn route grabs that route and trims it back to the pressed cell. The
        // pick uses the exact pointer position, so where routes share a cell you grab the one you
        // are actually pointing at rather than whichever happens to be checked first.
        if (
            PlanMovement.Instance.TryFindPlannedRouteAtPoint(
                pointer.Value,
                out GameObject owner,
                out int planIndex
            )
        )
        {
            if (!PlanMovement.Instance.TrySelectUnitForRoute(owner))
                return false;

            TruncatePlan(planIndex);
            return CurrentRibbon != null;
        }

        // Pressing another friendly unit starts a fresh route for it. Preparing movement also makes
        // this gesture work when the previously selected unit was targeting an ability.
        if (
            PlanMovement.Instance.TrySelectUnitForMovementAtCell(
                GridSystem.GetNearestGridCell(pointer.Value)
            )
        )
        {
            TruncatePlan(0);
            return CurrentRibbon != null;
        }
        return false;
    }

    void ExtendPath(int moveDist)
    {
        // A drag is only valid once a route exists for the selected unit. Ignore malformed or
        // off-unit drags instead of mutating the plan and throwing every frame.
        if (
            !pathDragActive
            || CurrentPlan == null
            || CurrentPlan.Count == 0
            || CurrentRibbon == null
        )
        {
            pathDragActive = false;
            return;
        }

        Vector3? selectedTile = Mouse.GetGridCellUnderMouse();
        if (selectedTile == null)
            return;
        Vector3 currentTile = selectedTile.Value;

        //Undo movementPath
        if (CurrentPlan.Count >= 2 && CurrentPlan[^2] == currentTile)
        {
            TruncatePlan(CurrentPlan.Count - 2);
            ReportRouteEnd();
            return;
        }

        Vector3 last = CurrentPlan[^1];
        if (!ValidMove(last, currentTile, moveDist))
            return;

        CurrentPlan.Add(currentTile);
        // The press has drawn somewhere, so it is a route being laid out rather than a click.
        abilityTapUnit = null;
        PlanMovement.Instance.NotifyCurrentRouteChanged();
        ReportRouteEnd();
    }

    /// <summary>
    /// Says once, while the route is resting on a square a team-mate finishes on, that letting go
    /// here will not hold. The route itself is already drawn as unusable at that end; this only
    /// puts the reason into words.
    /// </summary>
    void ReportRouteEnd()
    {
        bool blocked = PlanMovement.Instance?.IsRouteEndBlocked(SelectedUnit) == true;
        if (!blocked)
        {
            ClearHeldCellNotice();
            return;
        }

        if (reportedHeldCell)
            return;

        reportedHeldCell = true;
        GameHUDController.Instance?.SetTargetFeedback(
            "Another unit ends its move there — your route will stop short.",
            true
        );
    }

    /// <summary>Drops the notice once the route ends somewhere it can hold.</summary>
    void ClearHeldCellNotice()
    {
        if (!reportedHeldCell)
            return;

        reportedHeldCell = false;
        GameHUDController.Instance?.ClearTargetFeedback();
    }

    /// <summary>Drops every step after the given plan index, keeping the start cell at index 0.</summary>
    void TruncatePlan(int keepThroughIndex)
    {
        List<Vector3> plan = CurrentPlan;
        if (plan == null || plan.Count == 0)
            return;

        int keep = Mathf.Clamp(keepThroughIndex, 0, plan.Count - 1);
        if (plan.Count > keep + 1)
            plan.RemoveRange(keep + 1, plan.Count - (keep + 1));

        PlanMovement.Instance.NotifyCurrentRouteChanged();
    }

    /// <summary>
    /// Whether the route may be extended onto a cell. A square a team-mate finishes on is not
    /// excluded here: marching through one another is already how units behave, and refusing the
    /// step would make the player route around a body to reach open ground behind it. Stopping
    /// there is what is disallowed, and that is settled when the drag ends.
    /// </summary>
    bool ValidMove(Vector3 last, Vector3 currentTile, int moveDist)
    {
        bool withinMoveDistance = CurrentPlan.Count - 1 < moveDist;
        bool exactlyOneTileAway = Mathf.Abs(Vector3.Distance(last, currentTile) - CellSize) <= 0.1f;
        bool notInWall = !GameLoop.wallLayout.Contains(GridSystem.ConvertToGridCoords(currentTile));
        bool notAlreadyInPath = !CurrentPlan.Contains(currentTile);

        return withinMoveDistance && exactlyOneTileAway && notInWall && notAlreadyInPath;
    }
}
