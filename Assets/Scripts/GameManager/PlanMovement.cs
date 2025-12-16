using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A simple reference to a dictionary that maps GameObjects (units) to their movement paths (list of Vector3 positions).
/// </summary>
public class PathsDict : Dictionary<GameObject, List<Vector3>>, INetworkSerializable
{
    public PathsDict()
        : base() { }

    public PathsDict(PathsDict dict)
        : base(dict) { }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        int count = Count;

        if (serializer.IsWriter)
        {
            serializer.SerializeValue(ref count);

            foreach (var kvp in this)
            {
                NetworkObjectReference netRef = kvp.Key.GetComponent<NetworkObject>();
                serializer.SerializeValue(ref netRef);

                var path = kvp.Value;
                int pathCount = path.Count;
                serializer.SerializeValue(ref pathCount);

                for (int i = 0; i < pathCount; i++)
                {
                    Vector3 pos = path[i];
                    serializer.SerializeValue(ref pos);
                }
            }
        }
        else
        {
            Clear();
            serializer.SerializeValue(ref count);

            for (int i = 0; i < count; i++)
            {
                NetworkObjectReference netRef = default;
                serializer.SerializeValue(ref netRef);

                int pathCount = 0;
                serializer.SerializeValue(ref pathCount);

                List<Vector3> path = new();
                for (int j = 0; j < pathCount; j++)
                {
                    Vector3 pos = default;
                    serializer.SerializeValue(ref pos);
                    path.Add(pos);
                }

                if (netRef.TryGet(out NetworkObject netObj))
                {
                    this[netObj.gameObject] = path;
                }
                else
                {
                    Debug.LogWarning(
                        "[PlanMovement] Failed to resolve NetworkObjectReference during deserialization, skipping unit path"
                    );
                }
            }
        }
    }
}

/// <summary>
/// Manages interactive path planning on a grid for team units.
/// Handles mouse-driven path creation, visual feedback, range displays, and movement validation.
/// Supports both normal movement and dash mechanics with configurable distances.
/// </summary>
public class PlanMovement : MonoBehaviour
{
    private List<GameObject> teamCharacters = new List<GameObject>();
    public GameObject selectedUnit;

    public PathsDict movementPaths = new PathsDict();
    private List<Vector3> currentMovementPath = new List<Vector3>();

    // Visual representation of paths
    private GameObject visualPathsParent;
    private Dictionary<GameObject, GameObject> visualPaths =
        new Dictionary<GameObject, GameObject>();

    private GameObject currentVisualPath;
    private GameObject currentPathNodes;
    private GameObject currentPathEdges;

    [SerializeField]
    private GameObject pathNodePrefab;

    [SerializeField]
    private GameObject pathEdgePrefab;

    // Range overlays
    private GameObject moveOverlay;
    private GameObject attackOverlay;

    [SerializeField]
    private GameObject moveOverlayCellPrefab;

    [SerializeField]
    private GameObject attackOverlayPrefab;

    public TMP_Text timerTextUI;

    private static float cellSize => GameLoop.cellSize; //shortened reference to GameLoop.cellSize

    public static PlanMovement Instance { get; private set; }

    const Vector3 visualPathHeightOffset = new Vector3(0, 0.1f, 0);

    void Awake()
    {
        Instance = this;
    }

    public void SwitchToUnit(GameObject newSelectedUnit, int range)
    {
        selectedUnit = newSelectedUnit;
        DisplayMoveRange(range);
        if (!movementPaths.ContainsKey(selectedUnit) || !visualPaths.ContainsKey(selectedUnit))
        {
            ResetPath();
        }
        else
        {
            SetCurrentPath();
        }
        // DisplayAttackRange();
    }

    void SetCurrentPath()
    {
        currentMovementPath = movementPaths[selectedUnit];
        currentVisualPath = visualPaths[selectedUnit];
        currentPathNodes = currentVisualPath.transform.Find("PathNodes").gameObject;
        currentPathEdges = currentVisualPath.transform.Find("PathEdges").gameObject;
    }

    void InitializeVisuals()
    {
        movementPaths = new PathsDict();
        visualPaths = new Dictionary<GameObject, GameObject>();
        AddCharacterOutlines();
        visualPathsParent = new GameObject("VisualPaths");
    }

    void ClearVisuals()
    {
        RemoveCharacterOutlines();
        Destroy(visualPathsParent);
        Destroy(moveOverlay);
        Destroy(attackOverlay);
    }


    public IEnumerator StartPlanning(
        System.Action<PathsDict> callback,
        double endTime,
        List<GameObject> units = null,
        int range = -1
    )
    {
        teamCharacters = units ?? PopulateTeamCharacters();
        range = range == -1 ? selectedUnit.GetComponent<Movement>().unitData.moveDist : range;

        InitializeVisuals();
        SwitchToUnit(teamCharacters[0], range);

        float timer;
        while ((timer = (float)(endTime - NetworkManager.Singleton.ServerTime.Time)) > 0)
        {
            timerTextUI.text = Mathf.CeilToInt(timer).ToString();
            if (selectedUnit == null)
            {
                Debug.LogWarning("No unit selected");
            }
            else
            {
                if (selectedUnit.GetComponent<Movement>().selectMovement)
                {
                    MovementSelection(range);
                }
                else
                {
                    AbilitySelection();
                }
            }
            timer -= Time.unscaledDeltaTime;
            yield return null;
        }

        ClearVisuals();
        timerTextUI.text = "";
        callback(movementPaths);
    }

    void MovementSelection(int moveDist)
    {
        if (Input.GetMouseButtonDown(0))
        {
            StartPath();
        }
        else if (Input.GetMouseButton(0))
        {
            ExtendPath(moveDist);
        }
    }

    void AbilitySelection()
    {
        //TODO: implement ability selection
    }

    /// <summary>
    /// Populates the teamCharacters list with units belonging to the local client's team
    /// </summary>
    private List<GameObject> PopulateTeamCharacters()
    {
        List<GameObject> localTeamCharacters = new List<GameObject>();

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
        {
            Debug.LogWarning(
                "[PlanMovement] NetworkManager not available, cannot populate team characters"
            );
            return localTeamCharacters;
        }

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        foreach (var netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null)
                continue;

            // Check if this is a unit (has Movement component)
            if (netObj.GetComponent<Movement>() != null && netObj.OwnerClientId == localClientId)
            {
                localTeamCharacters.Add(netObj.gameObject);
            }
        }

        return localTeamCharacters;
    }

    void ResetPath()
    {
        movementPaths[selectedUnit] = new List<Vector3> { characterCell };
        currentMovementPath = movementPaths[selectedUnit];

        if (visualPaths.ContainsKey(selectedUnit))
            Destroy(visualPaths[selectedUnit]);

        currentVisualPath = CreateGameObject("VisualPath", visualPathsParent);
        currentPathNodes = CreateGameObject("PathNodes", currentVisualPath);
        currentPathEdges = CreateGameObject("PathEdges", currentVisualPath);

        visualPaths[selectedUnit] = currentVisualPath;
    }

    void ResetPath(int index)
    {
        for (int i = currentPathNodes.transform.childCount - 1; i > index; i--)
        {
            Destroy(currentPathNodes.transform.GetChild(i).gameObject);
            Destroy(currentPathEdges.transform.GetChild(i).gameObject);
        }

        currentMovementPath
            .RemoveRange(index + 2, currentMovementPath.Count - (index + 2)); //movement path includes start but visual does not
    }

    void ResetPath(GameObject node)
    {
        currentPathNodes = currentVisualPath.transform.Find("PathNodes").gameObject;
        currentPathEdges = currentVisualPath.transform.Find("PathEdges").gameObject;
        ResetPath(node.transform.GetSiblingIndex());
    }

    /// <summary>
    /// Helper to create a GameObject with a given name and parent.
    /// </summary>
    GameObject CreateGameObject(string name, GameObject parent)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent?.transform);
        return obj;
    }

    void StartPath()
    {
        Vector3 characterCell = GetGridCellUnderCharacter(selectedUnit);
        if (GetGridCellUnderMouse() == characterCell)
        {
            ResetPath();
        }
        else
        {
            GameObject node = GetObjectUnderMouse("PathNode");
            if (!visualPaths.ContainsKey(selectedUnit) || node?.transform.parent?.parent?.gameObject != currentVisualPath)
                return;
            ResetPath(node);
        }

    }

    void ExtendPath(int moveDist)
    {
        if (currentMovementPath.Count == 0) return;

        Vector3? selectedTile = GetGridCellUnderMouse();
        if (selectedTile == null)
            return;
        Vector3 currentTile = selectedTile.Value;

        //Undo movementPath
        if (currentMovementPath.Count >= 2 && currentMovementPath[^2] == currentTile)
        {
            ResetPath(currentPathNodes.transform.childCount - 2);
            return;
        }

        Vector3 last = currentMovementPath[^1];
        if (ValidMove(last, currentTile, moveDist))
        {
            currentMovementPath.Add(currentTile);
            AddPathSectionVisual(currentTile, last, currentMovementPath.Count - 1, moveDist);
        }
    }

    bool ValidMove(Vector3 last, Vector3 currentTile, int moveDist)
    {
        bool withinMoveDistance = currentMovementPath.Count - 1 < moveDist;
        bool exactlyOneTileAway = Mathf.Abs(Vector3.Distance(last, currentTile) - cellSize) <= 0.1f;
        bool notInWall = !GameLoop.wallLayout.Contains(ConvertToGridCoords(currentTile));
        bool notAlreadyInPath = !currentMovementPath.Contains(currentTile);

        return withinMoveDistance && exactlyOneTileAway && notInWall && notAlreadyInPath;
    }

    void DisplayMoveRange(int range)
    {
        Destroy(moveOverlay);
        if (selectedUnit == null)
            return;

        moveOverlay = Helper.DisplayGridRange(
            GetGridCellUnderCharacter(selectedUnit),
            range,
            moveOverlayCellPrefab
        );
    }

    void DisplayAttackRange()
    {
        //TODO: update attack range on path change
        //TODO: check line of sight
        if (attackOverlay)
            Destroy(attackOverlay);

        if (selectedUnit == null)
            return;

        Vector3 currentPos = GetGridCellUnderCharacter(selectedUnit);
        //TODO: put at end of path

        attackOverlay = Instantiate(
            attackOverlayPrefab,
            currentPos + new Vector3(0, 0.1f, 0),
            Quaternion.identity
        );
        float diameter = 2 * selectedUnit.GetComponent<Shooting>().unitData.targetRange * cellSize;

        attackOverlay.transform.localScale = new Vector3(
            diameter,
            attackOverlay.transform.localScale.y,
            diameter
        );
    }

    void AddPathSectionVisual(Vector3 cell, Vector3 last, int length, int moveDist)
    {
        GameObject node = Instantiate(pathNodePrefab, cell, Quaternion.identity);
        node.transform.parent = currentPathNodes.transform;
        node.transform.position += visualPathHeightOffset;

        GameObject edge = Instantiate(pathEdgePrefab, (cell + last) / 2, Quaternion.identity);
        edge.transform.parent = currentPathEdges.transform;
        edge.transform.position += visualPathHeightOffset;

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
                rend.gameObject.GetComponent<Renderer>().renderingLayerMask = 1 << 1;
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
                rend.gameObject.GetComponent<Renderer>().renderingLayerMask = 1 << 0;
            }
        }
    }

    public static RaycastHit? GetHitObjectUnderMouse(int layerMask)
    {
        Camera teamCamera = GameLoop.Instance.teamCamera;
        if (teamCamera == null)
            return null;

        Ray ray = teamCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, layerMask))
        {
            return hit;
        }
        return null;
    }

    public static GameObject GetObjectUnderMouse(int layerMask)
    {
        return GetHitObjectUnderMouse(layerMask)?.collider.gameObject;
    }
    public static GameObject GetObjectUnderMouse(string layerMaskName)
    {
        return GetObjectUnderMouse(LayerMask.GetMask(layerMaskName));
    }

    public static Vector3? GetGridCellUnderMouse()
    {
        RaycastHit? hit = GetHitObjectUnderMouse(LayerMask.GetMask("Grid"));
        if (hit == null)
        {
            return null;
        }
        return GetNearestGridCell(hit.Value.point);
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
