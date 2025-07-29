using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;
using cakeslice;
using Outline = cakeslice.Outline;

public class PlanMovement : MonoBehaviour
{
    static float cellSize => GameLoop.cellSize;

    bool isDragging = false;
    GameObject selectedUnit; // Reference to the Cube GameObject that will move

    [SerializeField] private GameObject pathNodePrefab; // Prefab for the path visual nodes
    [SerializeField] private GameObject pathEdgePrefab; // Prefab for the path visual edges
    string team;

    public List<GameObject> teamCharacters = new List<GameObject>();
    public Dictionary<GameObject, List<Vector3>> movementPaths = new Dictionary<GameObject, List<Vector3>>();
    List<Vector3> movementPath = new List<Vector3>(); // List to store the path of the selected unit

    GameObject visualPathsParent;
    Dictionary<GameObject, GameObject> visualPaths = new Dictionary<GameObject, GameObject>();

    //Current path visual objects
    GameObject visualPath;
    GameObject pathNodes;
    GameObject pathEdges;

    public TMP_Text timerTextUI;

    [SerializeField] private GameObject moveOverlayCellPrefab;
    [SerializeField] private GameObject attackOverlayPrefab;

    GameObject moveOverlay;
    GameObject attackOverlay;

    public static PlanMovement Instance
    {
        get; private set;
    } // Singleton instance for easy access

    void Awake()
    {
        Instance = this; // Set the singleton instance
    }

    public IEnumerator ChoosePaths(string team, System.Action<Dictionary<GameObject, List<Vector3>>> callback, float timer, List<GameObject> dashUnits = null, int dashDist = -1)
    {
        this.team = team;
        this.teamCharacters = dashUnits ?? GameObject.FindGameObjectsWithTag(team).ToList();
        movementPaths = new Dictionary<GameObject, List<Vector3>>();

        // Initialize movement paths for each character
        foreach (GameObject character in teamCharacters)
        {
            movementPaths[character] = new List<Vector3>();
        }

        //Add outline effect
        foreach (GameObject character in teamCharacters)
        {
            Renderer[] renderers = character.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in renderers)
            {
                GameObject go = rend.gameObject;
                if (go.GetComponent<Outline>() == null)
                {
                    go.AddComponent<Outline>();
                    go.GetComponent<Outline>().color = GameLoop.GetTeamIndex(team);
                }

                go.GetComponent<Outline>().enabled = true;
            }
        }

        visualPathsParent = new GameObject("VisualPaths");

        while (timer > 0)
        {
            timerTextUI.text = (Mathf.CeilToInt(timer)).ToString();
            if (Input.GetMouseButtonDown(0))
            {
                selectedUnit = GetCharacterUnderMouse();
                DisplayMoveRange();
                //DisplayAttackRange();
                StartPath(dashUnits);
            }
            else if (Input.GetMouseButton(0) && isDragging)
            {
                ExtendPath(dashDist);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                EndPath();
            }

            timer -= Time.unscaledDeltaTime; // Decrease timer each frame (unaffected by time scale)
            yield return null; // Wait for the next frame
        }

        // Time is up, finalize any in-progress path
        if (isDragging)
        {
            EndPath();
        }

        foreach (GameObject character in teamCharacters)
        {
            Renderer[] renderers = character.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in renderers)
            {
                GameObject go = rend.gameObject;
                go.GetComponent<Outline>().enabled = false;
            }
        }
        Destroy(visualPathsParent); // Clean up the visual path
        Destroy(moveOverlay);
        Destroy(attackOverlay);
        timerTextUI.text = "";
        callback(movementPaths); // Return the paths after the selection time is over
    }


    //TODO: display on node select as well

    void DisplayMoveRange()
    {
        Destroy(moveOverlay);
        if (selectedUnit == null) return;

        moveOverlay = Helper.DisplayGridRange(GetGridCellUnderCharacter(selectedUnit), selectedUnit.GetComponent<Movement>().unitData.moveDist, moveOverlayCellPrefab);
    }


    //TODO: update attack range on path change
    //TODO: check line of sight
    void DisplayAttackRange()
    {
        if (attackOverlay) Destroy(attackOverlay);

        if (selectedUnit == null) return;

        Vector3 currentPos = GetGridCellUnderCharacter(selectedUnit);
        //TODO: put at end of path

        attackOverlay = Instantiate(attackOverlayPrefab, currentPos + new Vector3(0, 0.1f, 0), Quaternion.identity);
        float diameter = 2 * selectedUnit.GetComponent<Shooting>().unitData.targetRange * cellSize;

        attackOverlay.transform.localScale = new Vector3(diameter, attackOverlay.transform.localScale.y, diameter);
    }

    // Modify StartPath to create the child objects
    void StartPath(List<GameObject> dashUnits = null)
    {
        if (selectedUnit?.tag == team && (dashUnits == null || dashUnits.Contains(selectedUnit)))
        {
            //Reset movement path
            movementPaths[selectedUnit].Clear();
            movementPath = movementPaths[selectedUnit];
            movementPath.Add(GetGridCellUnderCharacter(selectedUnit));

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
    void AddPathSectionVisual(Vector3 cell, Vector3 last, int length, int moveDist)
    {
        Vector3 heightOffset = new Vector3(0, pathNodePrefab.GetComponent<Renderer>().bounds.size.y / 2, 0);
        GameObject node = Instantiate(pathNodePrefab, cell + heightOffset, Quaternion.identity);
        node.transform.parent = pathNodes.transform;

        GameObject edge = Instantiate(pathEdgePrefab, (cell + last) / 2 + heightOffset, Quaternion.identity);
        edge.transform.parent = pathEdges.transform;

        if (length == moveDist)
        {
            node.transform.GetComponent<Renderer>().material.color = Color.green;
        }
    }

    // Update backOfVisualPath to use pathEdgesParent and pathNodesParent
    GameObject backOfVisualPath(GameObject parent, int index = 1)
    {
        return parent.transform.GetChild(parent.transform.childCount - index).gameObject;
    }

    void ExtendPath(int dashDist = -1)
    {
        Vector3? selectedTile = GetGridCellUnderMouse();
        if (selectedTile == null) return;
        Vector3 currentTile = selectedTile.Value;
        Vector3 last = movementPath[^1]; //^1 => -1

        if (movementPath.Count >= 2 && movementPath[^2] == currentTile) //Undoing movementPath
        {
            movementPath.RemoveAt(movementPath.Count - 1); // Remove the last tile if the current tile is the same as the previous one
            Destroy(backOfVisualPath(pathNodes)); // Remove the last path edge visual
            Destroy(backOfVisualPath(pathEdges)); // Remove the last path node visual

            return;
        }

        int moveDist = dashDist == -1 ? selectedUnit.GetComponent<Movement>().unitData.moveDist : dashDist;
        if (movementPath.Count - 1 < moveDist //Cause move dist excludes start tile
            && Mathf.Abs(Vector3.Distance(last, currentTile) - cellSize) <= 0.1f //Exactly one tile away, no diagonal
            && !movementPath.Contains(currentTile))
        {
            movementPath.Add(currentTile);
            AddPathSectionVisual(currentTile, last, movementPath.Count - 1, moveDist);
        }
    }

    void EndPath()
    {
        isDragging = false;
        selectedUnit = null;
        //if (!selectedUnit) return;
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

    public static Vector2Int ConvertToGridCoords(Vector3 position)
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
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, LayerMask.GetMask(GameLoop.teams.ToArray())))
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

    public static Vector3 GetGridCellUnderCharacter(GameObject character)
    {
        Vector3 position = character.transform.position; // Get the position of the character
        return GetNearestGridCell(position); // Return the nearest grid cell position
    }


    public static Vector3? GetGridCellUnderMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition); // Create a ray from the camera to the mouse position
        RaycastHit hit; // Variable to store raycast hit information
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, LayerMask.GetMask("Grid")))
        {
            return GetNearestGridCell(hit.point);
        }
        return null;
    }

    static Vector3 GetNearestGridCell(Vector3 position)
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
