using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;

public class PlanMovement : MonoBehaviour
{
    float cellSize;

    bool isDragging = false;
    GameObject selectedUnit; // Reference to the Cube GameObject that will move

    [SerializeField] private GameObject pathNodePrefab; // Prefab for the path visual nodes
    [SerializeField] private GameObject pathEdgePrefab; // Prefab for the path visual edges
    string team;

    public GameObject[] teamCharacters = new GameObject[3];
    public Dictionary<GameObject, List<Vector3>> movementPaths = new Dictionary<GameObject, List<Vector3>>();
    List<Vector3> movementPath = new List<Vector3>(); // List to store the path of the selected unit

    GameObject visualPathsParent;
    Dictionary<GameObject, GameObject> visualPaths = new Dictionary<GameObject, GameObject>();

    //Current path visual objects
    GameObject visualPath;
    GameObject pathNodes;
    GameObject pathEdges;

    [SerializeField] private TMP_Text timerTextUI;


    // Start is called before the first frame update
    void Start()
    {
        cellSize = GameLoop.cellSize;
    }

    //TODO: Redo part of the path by dragging from node

    public IEnumerator ChoosePaths(string team, System.Action<Dictionary<GameObject, List<Vector3>>> callback, float timer)
    {
        this.team = team;
        this.teamCharacters = GameObject.FindGameObjectsWithTag(team);
        movementPaths = new Dictionary<GameObject, List<Vector3>>();

        // Initialize movement paths for each character
        foreach (GameObject character in teamCharacters)
        {
            movementPaths[character] = new List<Vector3>();
        }

        visualPathsParent = new GameObject("VisualPaths");

        while (timer > 0)
        {
            timerTextUI.text = (Mathf.CeilToInt(timer)).ToString();
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

            timer -= Time.deltaTime; // Decrease timer each frame
            yield return null; // Wait for the next frame
        }

        // Time is up, finalize any in-progress path
        if (isDragging)
        {
            EndPath();
        }
        Destroy(visualPathsParent); // Clean up the visual path
        timerTextUI.text = "";
        callback(movementPaths); // Return the paths after the selection time is over
    }

    // Modify StartPath to create the child objects
    void StartPath()
    {
        selectedUnit = GetCharacterUnderMouse();
        if (selectedUnit != null)
        {

            //Reset movement path
            movementPaths[selectedUnit].Clear();
            movementPath = movementPaths[selectedUnit];
            movementPath.Add(GetGameCellUnderCharacter(selectedUnit));

            //Reset visual path
            if (visualPaths.ContainsKey(selectedUnit)) Destroy(visualPaths[selectedUnit]);
            visualPath = new GameObject("VisualPath");
            visualPath.transform.parent = visualPathsParent.transform;
            visualPaths[selectedUnit] = visualPath;

            pathNodes = new GameObject("PathNodes");
            pathNodes.transform.parent = visualPath.transform;

            pathEdges = new GameObject("PathEdges");
            pathEdges.transform.parent = visualPath.transform;

            isDragging = true;
        }
        else
        {
            GameObject node = GetNodeUnderMouse();
            if (node == null) return;

            //Set up path info from node
            visualPath = node.transform.parent.parent.gameObject;
            pathNodes = visualPath.transform.Find("PathNodes").gameObject;
            pathEdges = visualPath.transform.Find("PathEdges").gameObject;

            selectedUnit = visualPaths.FirstOrDefault(x => x.Value == visualPath).Key;

            //Remove all nodes after it
            int index = node.transform.GetSiblingIndex();
            movementPaths[selectedUnit].RemoveRange(index + 2, movementPaths[selectedUnit].Count - (index + 2)); //movement path includes start but visual does not

            for (int i = pathNodes.transform.childCount - 1; i > index; i--)
            {
                Destroy(pathNodes.transform.GetChild(i).gameObject);
                Destroy(pathEdges.transform.GetChild(i).gameObject);
            }

            movementPath = movementPaths[selectedUnit];
        }

        isDragging = true;
    }

    // Update AddPathSectionVisual to parent to the correct child
    void AddPathSectionVisual(Vector3 cell, Vector3 last, int length)
    {
        Vector3 heightOffset = new Vector3(0, pathNodePrefab.GetComponent<Renderer>().bounds.size.y / 2, 0);
        GameObject node = Instantiate(pathNodePrefab, cell + heightOffset, Quaternion.identity);
        node.transform.parent = pathNodes.transform;

        GameObject edge = Instantiate(pathEdgePrefab, (cell + last) / 2 + heightOffset, Quaternion.identity);
        edge.transform.parent = pathEdges.transform;

        if (length == selectedUnit.GetComponent<Movement>().moveDist)
        {
            node.transform.GetComponent<Renderer>().material.color = Color.green;
        }
    }

    // Update backOfVisualPath to use pathEdgesParent and pathNodesParent
    GameObject backOfVisualPath(GameObject parent, int index = 1)
    {
        return parent.transform.GetChild(parent.transform.childCount - index).gameObject;
    }

    void ExtendPath()
    {
        Vector3 currentTile = GetGridCellUnderMouse();
        Vector3 last = movementPath[^1]; //^1 => -1

        if (movementPath.Count >= 2 && movementPath[^2] == currentTile) //Undoing movementPath
        {
            movementPath.RemoveAt(movementPath.Count - 1); // Remove the last tile if the current tile is the same as the previous one
            Destroy(backOfVisualPath(pathNodes)); // Remove the last path edge visual
            Destroy(backOfVisualPath(pathEdges)); // Remove the last path node visual

            return;
        }

        if (movementPath.Count - 1 < selectedUnit.GetComponent<Movement>().moveDist //Cause move dist excludes start tile
            && Mathf.Abs(Vector3.Distance(last, currentTile) - cellSize) <= 0.1f //Exactly one tile away, no diagonal
            && !movementPath.Contains(currentTile))
        {
            movementPath.Add(currentTile);
            AddPathSectionVisual(currentTile, last, movementPath.Count - 1);
        }
    }

    void EndPath()
    {
        isDragging = false;
        if (!selectedUnit) return;
        //PrintPaths(movementPaths);
        //ConfirmPathButton.Show(); // optional
    }

    void PrintPaths(Dictionary<GameObject, List<Vector3>> paths)
    {
        foreach (var pair in paths)
        {
            Debug.Log($"Unit: {pair.Key.name}, Path: {string.Join(", ", pair.Value)}");
        }
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
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, LayerMask.GetMask(team)))
        {
            return hit.collider.gameObject; // Return the GameObject that was hit
        }
        return null; // Return null if no object was hit
    }

    GameObject GetNodeUnderMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition); // Create a ray from the camera to the mouse position
        RaycastHit hit; // Variable to store raycast hit information
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, LayerMask.GetMask("PathNode")))
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
