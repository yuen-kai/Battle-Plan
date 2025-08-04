using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;
using cakeslice;
using Outline = cakeslice.Outline;

public class PathsDict : Dictionary<GameObject, List<Vector3>>
{
    public PathsDict() : base()
    {
    }

    public PathsDict(PathsDict dict) : base(dict) { }
} // A simple reference to a dictionary that maps GameObjects (units) to their movement paths (list of Vector3 positions).

public class PlanMovement : MonoBehaviour
{
    private string team;
    private List<GameObject> teamCharacters = new List<GameObject>();
    private GameObject selectedUnit;

    public PathsDict movementPaths = new PathsDict();
    private List<Vector3> currentMovementPath = new List<Vector3>();

    // Visual representation of paths
    private GameObject visualPathsParent;
    private Dictionary<GameObject, GameObject> visualPaths = new Dictionary<GameObject, GameObject>();

    private GameObject currentVisualPath;
    private GameObject currentPathNodes;
    private GameObject currentPathEdges;

    [SerializeField] private GameObject pathNodePrefab;
    [SerializeField] private GameObject pathEdgePrefab;

    // Range overlays
    private GameObject moveOverlay;
    private GameObject attackOverlay;
    [SerializeField] private GameObject moveOverlayCellPrefab;
    [SerializeField] private GameObject attackOverlayPrefab;


    public TMP_Text timerTextUI;

    private static float cellSize => GameLoop.cellSize; //shortened reference to GameLoop.cellSize

    private bool isDragging = false;


    public static PlanMovement Instance
    {
        get; private set;
    }

    void Awake()
    {
        Instance = this;
    }


    public IEnumerator ChoosePaths(string team, System.Action<PathsDict> callback, float timer, List<GameObject> dashUnits = null, int dashDist = -1)
    {
        //Initialize
        this.team = team;
        this.teamCharacters = dashUnits ?? GameObject.FindGameObjectsWithTag(team).ToList();

        movementPaths = new PathsDict();
        foreach (GameObject character in teamCharacters)
        {
            movementPaths[character] = new List<Vector3>();
        }

        AddCharacterOutlines();

        visualPathsParent = new GameObject("VisualPaths");


        //Path selection loop
        while (timer > 0)
        {
            timerTextUI.text = (Mathf.CeilToInt(timer)).ToString();
            if (Input.GetMouseButtonDown(0))
            {
                selectedUnit = GetCharacterUnderMouse();
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

            timer -= Time.unscaledDeltaTime;
            yield return null;
        }

        //Cleanup
        if (isDragging) // Finalize any in-progress path
        {
            EndPath();
        }

        RemoveCharacterOutlines();
        Destroy(visualPathsParent);
        Destroy(moveOverlay);
        Destroy(attackOverlay);
        timerTextUI.text = "";
        callback(movementPaths);
    }

    void StartPath(List<GameObject> dashUnits = null)
    {
        bool isValidUnitSelected = selectedUnit?.tag == team && (dashUnits == null || dashUnits.Contains(selectedUnit));
        if (isValidUnitSelected)
        {
            //Reset movement path
            movementPaths[selectedUnit].Clear();
            currentMovementPath = movementPaths[selectedUnit];
            currentMovementPath.Add(GetGridCellUnderCharacter(selectedUnit));

            //Reset visual path
            if (visualPaths.ContainsKey(selectedUnit)) Destroy(visualPaths[selectedUnit]);
            currentVisualPath = new GameObject("VisualPath");
            currentVisualPath.transform.parent = visualPathsParent.transform;
            visualPaths[selectedUnit] = currentVisualPath;

            currentPathNodes = new GameObject("PathNodes");
            currentPathNodes.transform.parent = currentVisualPath.transform;

            currentPathEdges = new GameObject("PathEdges");
            currentPathEdges.transform.parent = currentVisualPath.transform;


            isDragging = true;
        }
        else
        {
            GameObject node = GetNodeUnderMouse();
            if (node == null) return;

            //Set up path info from node
            currentVisualPath = node.transform.parent.parent.gameObject;
            currentPathNodes = currentVisualPath.transform.Find("PathNodes").gameObject;
            currentPathEdges = currentVisualPath.transform.Find("PathEdges").gameObject;

            selectedUnit = visualPaths.FirstOrDefault(x => x.Value == currentVisualPath).Key;

            //Remove all nodes after it
            int index = node.transform.GetSiblingIndex();

            for (int i = currentPathNodes.transform.childCount - 1; i > index; i--)
            {
                Destroy(currentPathNodes.transform.GetChild(i).gameObject);
                Destroy(currentPathEdges.transform.GetChild(i).gameObject);
            }

            movementPaths[selectedUnit].RemoveRange(index + 2, movementPaths[selectedUnit].Count - (index + 2)); //movement path includes start but visual does not

            currentMovementPath = movementPaths[selectedUnit];
        }

        DisplayMoveRange();
        isDragging = true;
    }

    void ExtendPath(int dashDist = -1)
    {
        Vector3? selectedTile = GetGridCellUnderMouse();
        if (selectedTile == null) return;

        Vector3 currentTile = selectedTile.Value;

        //Undoing movementPath
        if (currentMovementPath.Count >= 2 && currentMovementPath[^2] == currentTile)
        {
            currentMovementPath.RemoveAt(currentMovementPath.Count - 1);
            Destroy(LastChild(currentPathNodes));
            Destroy(LastChild(currentPathEdges));
            return;
        }

        //Add to movementPath
        Vector3 last = currentMovementPath[^1];
        int moveDist = dashDist == -1 ? selectedUnit.GetComponent<Movement>().unitData.moveDist : dashDist;
        if (currentMovementPath.Count - 1 < moveDist //Cause move dist excludes start tile
            && Mathf.Abs(Vector3.Distance(last, currentTile) - cellSize) <= 0.1f //Exactly one tile away, no diagonal
            && !currentMovementPath.Contains(currentTile))
        {
            currentMovementPath.Add(currentTile);
            AddPathSectionVisual(currentTile, last, currentMovementPath.Count - 1, moveDist);
        }
    }

    void EndPath()
    {
        isDragging = false;
        selectedUnit = null;
    }


    void DisplayMoveRange()
    {
        Destroy(moveOverlay);
        if (selectedUnit == null) return;

        moveOverlay = Helper.DisplayGridRange(GetGridCellUnderCharacter(selectedUnit), selectedUnit.GetComponent<Movement>().unitData.moveDist, moveOverlayCellPrefab);
    }

    void DisplayAttackRange()
    {
        //TODO: update attack range on path change
        //TODO: check line of sight
        if (attackOverlay) Destroy(attackOverlay);

        if (selectedUnit == null) return;

        Vector3 currentPos = GetGridCellUnderCharacter(selectedUnit);
        //TODO: put at end of path

        attackOverlay = Instantiate(attackOverlayPrefab, currentPos + new Vector3(0, 0.1f, 0), Quaternion.identity);
        float diameter = 2 * selectedUnit.GetComponent<Shooting>().unitData.targetRange * cellSize;

        attackOverlay.transform.localScale = new Vector3(diameter, attackOverlay.transform.localScale.y, diameter);
    }


    void AddPathSectionVisual(Vector3 cell, Vector3 last, int length, int moveDist)
    {
        Vector3 heightOffset = Helper.heightOffset(pathNodePrefab.transform);

        GameObject node = Instantiate(pathNodePrefab, cell, Quaternion.identity);
        node.transform.parent = currentPathNodes.transform;
        node.transform.position += heightOffset;

        GameObject edge = Instantiate(pathEdgePrefab, (cell + last) / 2, Quaternion.identity);
        edge.transform.parent = currentPathEdges.transform;
        edge.transform.position += heightOffset;

        if (length == moveDist)
        {
            node.transform.GetComponent<Renderer>().material.color = Color.green;
        }
    }

    void AddCharacterOutlines()
    {
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
    }

    void RemoveCharacterOutlines()
    {
        foreach (GameObject character in teamCharacters)
        {
            Renderer[] renderers = character.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in renderers)
            {
                GameObject go = rend.gameObject;
                go.GetComponent<Outline>().enabled = false;
            }
        }
    }


    public static GameObject GetCharacterUnderMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition); // Create a ray from the camera to the mouse position
        RaycastHit hit; // Variable to store raycast hit information
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, LayerMask.GetMask(GameLoop.teams.ToArray())))
        {
            return hit.collider.gameObject; // Return the GameObject that was hit
        }
        return null; // Return null if no object was hit
    }

    public static GameObject GetNodeUnderMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition); // Create a ray from the camera to the mouse position
        RaycastHit hit; // Variable to store raycast hit information
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, LayerMask.GetMask("PathNode")))
        {
            return hit.collider.gameObject; // Return the GameObject that was hit
        }
        return null; // Return null if no object was hit
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

    public static Vector3 GetGridCellUnderCharacter(GameObject character)
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


    public static GameObject LastChild(GameObject parent, int index = 1)
    {
        return parent.transform.GetChild(parent.transform.childCount - index).gameObject;
    }


    public static void PrintPaths(PathsDict paths)
    {
        foreach (var pair in paths)
        {
            Debug.Log($"Unit: {pair.Key.name}, Path: {string.Join(", ", pair.Value)}");
        }
    }

}
