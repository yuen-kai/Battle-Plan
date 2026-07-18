using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PathSelection : MonoBehaviour
{
    public static PathSelection Instance { get; private set; }

    [SerializeField]
    private GameObject pathNodePrefab;

    [SerializeField]
    private GameObject pathEdgePrefab;

    private GameObject currentPathNodes;
    private GameObject currentPathEdges;
    private bool pathDragActive;
    private List<Vector3> CurrentPlan => PlanMovement.Instance != null ? PlanMovement.Instance.currentPlan : null;
    private GameObject CurrentVisuals => PlanMovement.Instance != null ? PlanMovement.Instance.currentVisuals : null;
    private GameObject SelectedUnit => PlanMovement.Instance != null ? PlanMovement.Instance.selectedUnit : null;
    private bool SelectedUnitUsesAbility =>
        SelectedUnit != null
        && PlanMovement.Instance.plans.TryGetValue(
            SelectedUnit,
            out (bool, List<Vector3>) plan
        )
        && plan.Item1;
    private float CellSize => GameLoop.cellSize;
    private Vector3 VisualPlanHeightOffset => PlanMovement.visualPlanHeightOffset;

    void Awake()
    {
        Instance = this;
    }

    public void MovementSelection(int moveDist)
    {
        moveDist = moveDist == -1 ? SelectedUnit.GetComponent<Movement>().unitData.moveDist : moveDist;
        if (Input.GetMouseButtonDown(0))
        {
            pathDragActive = StartPath();
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

    bool StartPath()
    {
        Vector3 characterCell = GridSystem.GetNearestGridCell(SelectedUnit);
        Vector3? clickedCell = Mouse.GetGridCellUnderMouse();
        if (clickedCell == characterCell)
        {
            InitializePath();
            return HasCurrentPathVisuals();
        }

        GameObject node = Mouse.GetObjectUnderMouse("PathNode");
        if (node?.transform.parent?.parent?.gameObject != CurrentVisuals)
        {
            // Clicking another of your units on the board selects it — the only selection
            // method during dodge windows, where the unit cards don't map to the alerted set.
            if (
                clickedCell != null
                && PlanMovement.Instance.TrySelectUnitAtCell(clickedCell.Value)
                && !SelectedUnitUsesAbility
            )
            {
                // The mouse-down that switched units is also the start of this drag. Set up
                // that unit's visual containers before the held-mouse frame extends its path.
                InitializePath();
                return HasCurrentPathVisuals();
            }
            return false;
        }

        BindCurrentPathVisuals();
        if (!HasCurrentPathVisuals())
            return false;

        ResetPath(node);
        return true;
    }

    void ExtendPath(int moveDist)
    {
        // A drag is only valid after StartPath has created containers for the selected unit.
        // Ignore malformed/off-unit drags instead of mutating the plan and throwing every frame.
        if (
            !pathDragActive
            || CurrentPlan == null
            || CurrentPlan.Count == 0
            || !HasCurrentPathVisuals()
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
            ResetPath(currentPathNodes.transform.childCount - 2);
            return;
        }

        Vector3 last = CurrentPlan[^1];
        if (ValidMove(last, currentTile, moveDist))
        {
            CurrentPlan.Add(currentTile);
            AddPathSectionVisual(currentTile, last, CurrentPlan.Count - 1, moveDist);
        }
    }


    void InitializePath()
    {
        if (!CurrentVisuals)
        {
            currentPathNodes = null;
            currentPathEdges = null;
            return;
        }

        BindCurrentPathVisuals();
        if (!currentPathNodes || !currentPathEdges)
        {
            currentPathNodes = Helper.CreateGameObject("PathNodes", CurrentVisuals);
            currentPathEdges = Helper.CreateGameObject("PathEdges", CurrentVisuals);
        }
        else
        {
            ResetPath(-1);
        }
    }

    void BindCurrentPathVisuals()
    {
        if (!CurrentVisuals)
        {
            currentPathNodes = null;
            currentPathEdges = null;
            return;
        }

        currentPathNodes = CurrentVisuals.transform.Find("PathNodes")?.gameObject;
        currentPathEdges = CurrentVisuals.transform.Find("PathEdges")?.gameObject;
    }

    bool HasCurrentPathVisuals()
    {
        return CurrentVisuals
            && currentPathNodes
            && currentPathEdges
            && currentPathNodes.transform.parent == CurrentVisuals.transform
            && currentPathEdges.transform.parent == CurrentVisuals.transform;
    }

    void ResetPath(int index)
    {
        for (int i = currentPathNodes.transform.childCount - 1; i > index; i--)
        {
            Destroy(currentPathNodes.transform.GetChild(i)?.gameObject);
            Destroy(currentPathEdges.transform.GetChild(i)?.gameObject);
        }

        CurrentPlan
            .RemoveRange(index + 2, CurrentPlan.Count - (index + 2)); //current plan includes start but visual does not
    }

    void ResetPath(GameObject node)
    {
        ResetPath(node.transform.GetSiblingIndex());
    }

    bool ValidMove(Vector3 last, Vector3 currentTile, int moveDist)
    {
        bool withinMoveDistance = CurrentPlan.Count - 1 < moveDist;
        bool exactlyOneTileAway = Mathf.Abs(Vector3.Distance(last, currentTile) - CellSize) <= 0.1f;
        bool notInWall = !GameLoop.wallLayout.Contains(GridSystem.ConvertToGridCoords(currentTile));
        bool notAlreadyInPath = !CurrentPlan.Contains(currentTile);

        return withinMoveDistance && exactlyOneTileAway && notInWall && notAlreadyInPath;
    }

    void AddPathSectionVisual(Vector3 cell, Vector3 last, int length, int moveDist)
    {
        GameObject node = Instantiate(pathNodePrefab, cell, Quaternion.identity);
        node.transform.parent = currentPathNodes.transform;
        node.transform.position += VisualPlanHeightOffset;

        GameObject edge = Instantiate(pathEdgePrefab, (cell + last) / 2, Quaternion.identity);
        edge.transform.parent = currentPathEdges.transform;
        edge.transform.position += VisualPlanHeightOffset;

        if (length == moveDist)
        {
            node.transform.GetComponent<Renderer>().material.color = Color.green;
        }
    }
}