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
    private List<Vector3> CurrentPlan => PlanMovement.Instance != null ? PlanMovement.Instance.currentPlan : null;
    private GameObject CurrentVisuals => PlanMovement.Instance != null ? PlanMovement.Instance.currentVisuals : null;
    private GameObject SelectedUnit => PlanMovement.Instance != null ? PlanMovement.Instance.selectedUnit : null;
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
            StartPath();
        }
        else if (Input.GetMouseButton(0))
        {
            ExtendPath(moveDist);
        }
    }

    void StartPath()
    {
        Vector3 characterCell = GridSystem.GetNearestGridCell(SelectedUnit);
        if (Mouse.GetGridCellUnderMouse() == characterCell)
        {
            InitializePath();
        }
        else
        {
            GameObject node = Mouse.GetObjectUnderMouse("PathNode");
            if (node?.transform.parent?.parent?.gameObject != CurrentVisuals)
                return;
            ResetPath(node);
        }

    }

    void ExtendPath(int moveDist)
    {
        if (CurrentPlan.Count == 0) return;

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
        currentPathNodes = CurrentVisuals.transform.Find("PathNodes")?.gameObject;
        currentPathEdges = CurrentVisuals.transform.Find("PathEdges")?.gameObject;
        if(!currentPathNodes || !currentPathEdges)
        {
            currentPathNodes = Helper.CreateGameObject("PathNodes", CurrentVisuals);
            currentPathEdges = Helper.CreateGameObject("PathEdges", CurrentVisuals);
        }
        else
        {
            ResetPath(-1);
        }
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