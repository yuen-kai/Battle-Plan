using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlanMovement : MonoBehaviour
{
    private float cellSize = 2.7f;
    List<Vector3> movementPath = new List<Vector3>();
    bool isDragging = false;
    GameObject selectedUnit; // Reference to the Cube GameObject that will move

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
        isDragging = true;
    }

    void ExtendPath()
    {
        Vector3 currentTile = GetGridCellUnderMouse();
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
        if (!selectedUnit) return;
        StartCoroutine(selectedUnit.GetComponent<Movement>().MoveToCells(movementPath));

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

}
