using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlanMovement : MonoBehaviour
{
    private float cellSize;
    List<Vector3> movementPath = new List<Vector3>();
    bool isDragging = false;
    [SerializeField] private GameObject Cube; // Reference to the Cube GameObject that will move

    // Start is called before the first frame update
    void Start()
    {
        cellSize = GridSystem.cellSize;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Debug.Log("start");

            StartPath();
            Debug.Log("Path started at: " + movementPath[0]);
        }
        else if (Input.GetMouseButton(0) && isDragging)
        {
            ExtendPath();
            Debug.Log("Path extended to: " + movementPath[movementPath.Count - 1]);
        }
        else if (Input.GetMouseButtonUp(0))
        {
            EndPath();
            Debug.Log("Path ended with " + movementPath.Count + " tiles.");
        }
    }

    void StartPath()
    {
        Vector3 tile = GetCellUnderMouse();
        //if (TileHasFriendlyUnit(tile))
        //{
        //    selectedUnit = GetUnit(tile);
        movementPath.Clear();
        movementPath.Add(tile);
        isDragging = true;
        //}
    }

    void ExtendPath()
    {
        Vector3 currentTile = GetCellUnderMouse();
        Vector3 last = movementPath[-1];
        if (IsValidNextTile(last, currentTile) && !movementPath.Contains(currentTile))
        {
            movementPath.Add(currentTile);
            //DrawPathVisual(movementPath);
        }
    }

    bool IsValidNextTile(Vector3 current, Vector3 next)
    {
        float distance = Vector3.Distance(current, next);
        return distance == cellSize;
    }

    void EndPath()
    {
        isDragging = false;
        StartCoroutine(Cube.GetComponent<Movement>().MoveToCells(movementPath));
        //ConfirmPathButton.Show(); // optional
    }

    public Vector3 GetCellUnderMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition); // Create a ray from the camera to the mouse position
        RaycastHit hit; // Variable to store raycast hit information
        Vector3 worldPosition = Physics.Raycast(ray, out hit, Mathf.Infinity, LayerMask.GetMask("Grid")) ? hit.point : Input.mousePosition; //if no collision use mouse position
        return GetNearestGridCell(worldPosition);
    }

    public Vector3 GetNearestGridCell(Vector3 position)
    {
        float x = Mathf.Round(position.x / cellSize) * cellSize;
        float z = Mathf.Round(position.z / cellSize) * cellSize;
        return new Vector3(x, position.y, z);
    }

}
