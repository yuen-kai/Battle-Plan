using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GridSystem : MonoBehaviour
{
    [SerializeField] private GameObject cellPrefab; // Public variable for the GameObject prefab
    public static float cellSize {  get; private set; } // Size of each grid cell
    [SerializeField] private int gridWidth = 10; // Width of the grid
    [SerializeField] private int gridHeight = 10; // Height of the grid
    GameObject[,] gridArray; // 2D array to hold grid cells

    // Start is called before the first frame update
    void Start()
    {
        cellSize = cellPrefab.GetComponent<Renderer>().bounds.size.x; // Set cellSize to the width of the cellPrefab
        CreateGrid();
    }

    void CreateGrid()
    {
        gridArray = new GameObject[gridWidth, gridHeight];
        GameObject gridParent = new("GridCells"); // Create a parent GameObject for the grid cells
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                Vector3 position = new(x * cellSize, 0, z * cellSize);
                GameObject cell = Instantiate(cellPrefab, position, Quaternion.identity); // Instantiate the prefab
                cell.transform.parent = gridParent.transform; // Set the parent of the cell to the gridParent
                gridArray[x, z] = cell; // Store the cell in the grid array
            }
        }
    }

    public static GameObject DisplayGridRange(
        Vector3 currentPos,
        int dist,
        GameObject overlayPrefab
    )
    {
        GameObject overlay = new("MoveOverlay");

        for (int i = -dist; i <= dist; i++)
        {
            int horizontalDist = dist - Mathf.Abs(i);
            for (int j = -horizontalDist; j <= horizontalDist; j++)
            {
                float cellSize = GameLoop.cellSize;
                Vector2 overlayCellPos = new(
                    currentPos.x + j * cellSize,
                    currentPos.z + i * cellSize
                );
                if (GameLoop.gridBounds.Contains(overlayCellPos))
                {
                    GameObject overlayCell = Instantiate(
                        overlayPrefab,
                        new Vector3(overlayCellPos.x, 0.2f, overlayCellPos.y),
                        Quaternion.identity
                    );
                    overlayCell.transform.parent = overlay.transform;
                }
            }
        }

        return overlay;
    }

    public static Vector3 GetNearestGridCell(GameObject character)
    {
        Vector3 position = character.transform.position; // Get the position of the character
        return GetNearestGridCell(position); // Return the nearest grid cell position
    }

    public static Vector3 GetNearestGridCell(Vector3 position)
    {
        float x = Mathf.Round(position.x / cellSize) * cellSize;
        float z = Mathf.Round(position.z / cellSize) * cellSize;
        return new Vector3(x, 0, z);
    }

    public static Vector2Int ConvertToGridCoords(Vector3 position)
    {
        //Assuming grid's bottom left corner is at (0,0) and the grid is aligned with the world axes
        int x = Mathf.RoundToInt(position.x / cellSize);
        int z = Mathf.RoundToInt(position.z / cellSize);
        return new Vector2Int(x, z);
    }
}
