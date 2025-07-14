using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlanMovement : MonoBehaviour
{
    private float cellSize = 2.7f;
    List<Vector3> movementPath = new List<Vector3>();
    bool isDragging = false;
    GameObject selectedUnit; // Reference to the Cube GameObject that will move
    GameObject visualPath;
    [SerializeField] private GameObject pathNodePrefab; // Prefab for the path visual nodes
    [SerializeField] private GameObject pathEdgePrefab; // Prefab for the path visual edges

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
        selectedUnit = GetCharacterUnderMouse();
        if (selectedUnit == null) //TODO: Check if is enemy character
        {
            Debug.Log("No character selected");
            return;
        }
        movementPath.Clear();
        movementPath.Add(GetGameCellUnderCharacter(selectedUnit));
        visualPath = new GameObject("PathNodes");
        isDragging = true;
    }

    void ExtendPath()
    {
        Vector3 currentTile = GetGridCellUnderMouse();
        Vector3 last = movementPath[^1]; //^1 => -1

        if (movementPath.Count >= 2 && movementPath[^2] == currentTile) //Undoing movementPath
        {
            movementPath.RemoveAt(movementPath.Count - 1); // Remove the last tile if the current tile is the same as the previous one
            Destroy(backOfVisualPath(1)); // Remove the last path edge visual
            Destroy(backOfVisualPath(2)); // Remove the last path node visual

            return;
        }

        if (movementPath.Count - 1 < selectedUnit.GetComponent<Movement>().MOVE_DIST //Cause move dist excludes start tile
            && Mathf.Abs(Vector3.Distance(last, currentTile) - cellSize) <= 0.1f //Exactly one tile away, no diagonal
            && !movementPath.Contains(currentTile))
        {
            movementPath.Add(currentTile);
            AddPathSectionVisual(currentTile, last);
        }
    }

    void EndPath()
    {
        isDragging = false;
        if (!selectedUnit) return;
        StartCoroutine(selectedUnit.GetComponent<Movement>().MoveToCells(movementPath));
        Destroy(visualPath);
        //ConfirmPathButton.Show(); // optional
    }

    void AddPathSectionVisual(Vector3 cell, Vector3 last)
    {
        Vector3 heightOffset = new Vector3(0, pathNodePrefab.GetComponent<Renderer>().bounds.size.y / 2, 0);
        GameObject node = Instantiate(pathNodePrefab, cell + heightOffset, Quaternion.identity);
        node.transform.parent = visualPath.transform;

        GameObject edge = Instantiate(pathEdgePrefab, (cell+last) / 2 + heightOffset, Quaternion.identity);
        edge.transform.parent = visualPath.transform;
        edge.transform.Rotate(90, 0, 0); // Rotate the edge on its side

    }

    public Vector2Int ConvertToGridCoords(Vector3 position)
    {
        //Assuming grid's bottom left corner is at (0,0) and the grid is aligned with the world axes
        int x = Mathf.RoundToInt(position.x / cellSize);
        int z = Mathf.RoundToInt(position.z / cellSize);
        return new Vector2Int(x, z);
    }

    GameObject GetCharacterUnderMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition); // Create a ray from the camera to the mouse position
        RaycastHit hit; // Variable to store raycast hit information
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, LayerMask.GetMask("Player")))
        {
            return hit.collider.gameObject; // Return the GameObject that was hit
        }
        return null; // Return null if no object was hit
    }

    Vector3 GetGameCellUnderCharacter(GameObject character)
    {
        Vector3 position = character.transform.position; // Get the position of the character
        return GetNearestGridCell(position); // Return the nearest grid cell position
    }


    Vector3 GetGridCellUnderMouse()
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

    GameObject backOfVisualPath(int index)
    {
        return visualPath.transform.GetChild(visualPath.transform.childCount - index).gameObject;
    }
}
