using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GridSystem : MonoBehaviour
{
    public GameObject cellPrefab; // Public variable for the GameObject prefab
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
        GameObject gridParent = new GameObject("GridCells"); // Create a parent GameObject for the grid cells
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridHeight; z++)
            {
                Vector3 position = new Vector3(x * cellSize, 0, z * cellSize);
                GameObject cell = Instantiate(cellPrefab, position, Quaternion.identity); // Instantiate the prefab
                cell.transform.parent = gridParent.transform; // Set the parent of the cell to the gridParent
                gridArray[x, z] = cell; // Store the cell in the grid array
            }
        }

        gridArray[3, 5].transform.Find("Inner").GetComponent<Renderer>().material.color = Color.red; // Change the color of the child named 'inner' to red

    }

    // Update is called once per frame
    void Update()
    {

    }
}
