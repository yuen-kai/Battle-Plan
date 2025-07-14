using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlanMovement : MonoBehaviour
{
    private float cellSize = 2.7f;
    List<Vector3> movementPath = new List<Vector3>();
    bool isDragging = false;
    [SerializeField] private GameObject Cube; // Reference to the Cube GameObject that will move

    // Start is called before the first frame update
    void Start()
    {
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            StartPath();

        }
        else if (Input.GetMouseButton(0) && isDragging)
        {
            ExtendPath();
        }
        else if (Input.GetMouseButtonUp(0))
        {
            EndPath();
        }
    }

    void StartPath()
    {
        Vector3 tile = GetPositionUnderMouse();
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
        Vector3 currentTile = GetPositionUnderMouse();
        Vector3 last = movementPath[^1]; //^1 => -1
        if (IsValidNextTile(last, currentTile) && !movementPath.Contains(currentTile))
        {
            movementPath.Add(currentTile);
            //DrawPathVisual(movementPath);
        }
    }

    void EndPath()
    {
        isDragging = false;
        StartCoroutine(Cube.GetComponent<Movement>().MoveToCells(movementPath));

        //ConfirmPathButton.Show(); // optional
    }

    public Vector2Int ConvertToGridCoords(Vector3 position)
    {
        int x = Mathf.RoundToInt(position.x / cellSize);
        int z = Mathf.RoundToInt(position.z / cellSize);
        return new Vector2Int(x, z);
    }

    bool IsValidNextTile(Vector3 current, Vector3 next)
    {
        float distance = Vector3.Distance(current, next);
        float buffer = 0.1f; // Small buffer amount
        return Mathf.Abs(distance - cellSize) <= buffer;
    }

    public Vector3 GetPositionUnderMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition); // Create a ray from the camera to the mouse position
        RaycastHit hit; // Variable to store raycast hit information
        Vector3 worldPosition = Physics.Raycast(ray, out hit, Mathf.Infinity, LayerMask.GetMask("Grid")) ? hit.point : Input.mousePosition; //if no collision use mouse position
        return GetNearestGridCell(worldPosition);
    }

    Vector3 GetNearestGridCell(Vector3 position)
    {
        float x = Mathf.Round(position.x / cellSize) * cellSize;
        float z = Mathf.Round(position.z / cellSize) * cellSize;
        return new Vector3(x, 0, z);
    }

}
