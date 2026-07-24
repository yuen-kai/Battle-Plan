using System.Collections.Generic;
using UnityEngine;

public class PathSelection : MonoBehaviour
{
    public static PathSelection Instance { get; private set; }

    private bool pathDragActive;

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
            pathDragActive = false;
        }
    }

    public bool TryStartPath()
    {
        if (PlanMovement.Instance?.CanEditPlan != true)
        {
            pathDragActive = false;
            return false;
        }

        pathDragActive = StartPath();
        return pathDragActive;
    }

    public void CancelCurrentDrag()
    {
        pathDragActive = false;
    }

    bool StartPath()
    {
        Vector3? pointer = Mouse.GetGridPointUnderMouse();
        if (pointer == null || SelectedUnit == null)
            return false;

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
            return;
        }

        Vector3 last = CurrentPlan[^1];
        if (ValidMove(last, currentTile, moveDist))
        {
            CurrentPlan.Add(currentTile);
            PlanMovement.Instance.NotifyCurrentRouteChanged();
        }
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

    bool ValidMove(Vector3 last, Vector3 currentTile, int moveDist)
    {
        bool withinMoveDistance = CurrentPlan.Count - 1 < moveDist;
        bool exactlyOneTileAway = Mathf.Abs(Vector3.Distance(last, currentTile) - CellSize) <= 0.1f;
        bool notInWall = !GameLoop.wallLayout.Contains(GridSystem.ConvertToGridCoords(currentTile));
        bool notAlreadyInPath = !CurrentPlan.Contains(currentTile);

        return withinMoveDistance && exactlyOneTileAway && notInWall && notAlreadyInPath;
    }
}
