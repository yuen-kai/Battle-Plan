using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A simple reference to a dictionary that maps GameObjects (units) to a tuple containing a boolean and their movement paths (list of Vector3 positions).
/// </summary>
public class PathsDict : Dictionary<GameObject, (bool, List<Vector3>)>, INetworkSerializable
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

                var (boolValue, path) = kvp.Value;
                serializer.SerializeValue(ref boolValue);

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

                bool boolValue = false;
                serializer.SerializeValue(ref boolValue);

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
                    this[netObj.gameObject] = (boolValue, path);
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
    private List<GameObject> teamCharacters = new();
    public GameObject selectedUnit;

    public PathsDict plans = new();
    public List<Vector3> currentPlan = new();

    // Visual representation of paths
    private GameObject planVisualsFolder;
    private Dictionary<GameObject, GameObject> planVisuals =
        new();

    public GameObject currentVisuals;

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

    public static Vector3 visualPlanHeightOffset = new(0, 0.1f, 0);

    void Awake()
    {
        Instance = this;
    }

    public IEnumerator StartPlanning(
        System.Action<PathsDict> callback,
        double endTime,
        List<GameObject> units = null,
        int range = -1
    )
    {
        teamCharacters = units ?? PopulateTeamCharacters();

        InitializeVisuals();
        SwitchToUnit(null, range);

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
                if (!plans[selectedUnit].Item1)
                {
                    PathSelection.Instance.MovementSelection(range);
                }
                else
                {
                    AbilitySelection();
                }
            }
            yield return null;
        }

        ClearVisuals();
        timerTextUI.text = "";
        callback(plans);
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
        List<GameObject> localTeamCharacters = new();

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

            if (netObj.GetComponent<Unit>() != null && netObj.OwnerClientId == localClientId)
            {
                localTeamCharacters.Add(netObj.gameObject);
            }
        }

        return localTeamCharacters;
    }

    public void SwitchToUnit(int unitIndex)
    {
        SwitchToUnit(teamCharacters[unitIndex]);
    }

    public void SwitchToUnit(GameObject newSelectedUnit, int range = -1)
    {
        GameObject oldSelectedUnit = selectedUnit;
        selectedUnit = newSelectedUnit;
        if (selectedUnit == null) return;

        DisplayMoveRange(range == -1 ? selectedUnit.GetComponent<Movement>().unitData.moveDist : range);
        if (!plans.ContainsKey(selectedUnit))
        {
            plans[selectedUnit] = (false, new List<Vector3> { GridSystem.GetNearestGridCell(selectedUnit)});
            ResetVisualPlan();
        }
        else if (oldSelectedUnit != null && oldSelectedUnit == selectedUnit)
        {
            plans[selectedUnit] = (!plans[selectedUnit].Item1, new List<Vector3> { GridSystem.GetNearestGridCell(selectedUnit)});

            int unitIndex = teamCharacters.IndexOf(selectedUnit);
            if (unitIndex != -1)
            {
                CardHandler cardHandler = GameLoop.Instance.unitCards.transform.GetChild(unitIndex).GetComponent<CardHandler>();
                cardHandler.SetCardColor(plans[selectedUnit].Item1 ? Color.yellow : Color.white);
            }
            
            ResetVisualPlan();
        }
        currentPlan = plans[selectedUnit].Item2;
        currentVisuals = planVisuals[selectedUnit];
        // DisplayAttackRange();
    }

    void ResetVisualPlan()
    {
        if (planVisuals.ContainsKey(selectedUnit))
            Destroy(planVisuals[selectedUnit]);

        planVisuals[selectedUnit] = Helper.CreateGameObject("PlanVisual", planVisualsFolder);
    }

    //================
    void InitializeVisuals()
    {
        plans = new PathsDict();
        planVisuals = new Dictionary<GameObject, GameObject>();
        AddCharacterOutlines();
        planVisualsFolder = new GameObject("PlanVisuals");
    }

    void ClearVisuals()
    {
        RemoveCharacterOutlines();
        Destroy(planVisualsFolder);
        Destroy(moveOverlay);
        Destroy(attackOverlay);
    }

    void DisplayMoveRange(int range)
    {
        Destroy(moveOverlay);
        if (selectedUnit == null)
            return;

        moveOverlay = GridSystem.DisplayGridRange(
            GridSystem.GetNearestGridCell(selectedUnit),
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

        Vector3 currentPos = GridSystem.GetNearestGridCell(selectedUnit);
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

    //-------------------
    public static void PrintPlans(PathsDict actions)
    {
        foreach (var pair in actions)
        {
            var (boolValue, path) = pair.Value;
            Debug.Log($"Unit: {pair.Key.name}, Boolean: {boolValue}, Path: {string.Join(", ", path)}");
        }
    }
}
