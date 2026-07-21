using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

                int pathCount = path?.Count ?? 0;
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

public enum AbilityTargetValidationReason : byte
{
    Valid,
    NoTargetCell,
    TargetNotRequired,
    AbilityUnavailable,
    OutOfBounds,
    DirectionNotAdjacent,
    OutOfRange,
    WallBlocked,
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
    private bool useUnitCards;
    private int planningRangeOverride = -1;

    public PathsDict plans = new();
    public List<Vector3> currentPlan = new();

    // Visual representation of paths
    private GameObject planVisualsFolder;
    private Dictionary<GameObject, GameObject> planVisuals = new();

    public GameObject currentVisuals;

    // Range overlays
    private GameObject moveOverlay;
    private GameObject attackOverlay;

    [SerializeField]
    private GameObject moveOverlayCellPrefab;

    [SerializeField]
    private GameObject attackOverlayPrefab;

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
        useUnitCards = units == null;
        planningRangeOverride = range;
        teamCharacters = units ?? PopulateTeamCharacters();
        while (
            units == null
            && teamCharacters.Count < RosterRules.UnitsPerPlayer
            && NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsListening
            && NetworkManager.Singleton.ServerTime.Time < endTime
        )
        {
            // Unit spawns and TeamIndex NetworkVariables can arrive just after the phase RPC.
            yield return null;
            teamCharacters = PopulateTeamCharacters();
        }

        InitializeVisuals();
        // Select the first unit up front so planning (and especially the dodge window, where
        // cards don't map to the alerted subset) is immediately usable without a card click.
        selectedUnit = null;
        SwitchToUnit(teamCharacters.FirstOrDefault(IsPlanningUnitAvailable), range);

        NetworkManager networkManager = NetworkManager.Singleton;
        float timer;
        while (
            networkManager != null
            && (timer = (float)(endTime - networkManager.ServerTime.Time)) > 0
        )
        {
            GameHUDController.Instance?.SetTimer(timer);
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
        GameHUDController.Instance?.SetTimer(0f);
        if (networkManager == null || !networkManager.IsListening)
            yield break;
        callback(plans);
    }

    /// <summary>
    /// Planning-phase ability targeting: while a unit is in ability mode (card toggled yellow),
    /// clicks pick a target square inside the displayed range. Directional abilities use one of
    /// the eight adjacent cells as a direction anchor; self-targeted abilities need no square.
    /// The square is stored as the plan's second element: (true, [startCell, targetSquare]).
    /// </summary>
    void AbilitySelection()
    {
        Movement movement = selectedUnit != null ? selectedUnit.GetComponent<Movement>() : null;
        UnitData unitData = movement != null ? movement.unitData : null;
        if (!Input.GetMouseButtonDown(0))
            return;
        if (GameHUDController.IsPointerOverUI(Input.mousePosition))
            return;
        if (selectedUnit == null || unitData == null)
        {
            ShowInvalidTargetFeedback(AbilityTargetValidationReason.AbilityUnavailable);
            return;
        }

        GameObject hoveredNode = Mouse.GetObjectUnderMouse("PathNode");
        Vector3? clicked = Mouse.GetGridCellUnderMouse();
        if (clicked == null && hoveredNode != null)
            clicked = GridSystem.GetNearestGridCell(hoveredNode.transform.position);
        if (clicked == null)
        {
            ShowInvalidTargetFeedback(AbilityTargetValidationReason.NoTargetCell);
            return;
        }

        Vector3 square = clicked.Value;
        Vector3 start = GridSystem.GetNearestGridCell(selectedUnit);
        Vector2Int startCell = GridSystem.ConvertToGridCoords(start);
        Vector2Int selectedCell = GridSystem.ConvertToGridCoords(square);
        AbilityTargetValidationReason validation = ValidateAbilityTarget(
            unitData,
            startCell,
            selectedCell,
            GameLoop.gridBounds.Contains(new Vector2(square.x, square.z)),
            GameLoop.wallLayout.Contains(selectedCell)
        );
        if (
            validation == AbilityTargetValidationReason.Valid
            && selectedUnit.GetComponent<Smoke>() != null
            && !GridSystem.IsSquareFootprintInBounds(selectedCell, Smoke.FootprintRadius)
        )
        {
            validation = AbilityTargetValidationReason.OutOfBounds;
        }

        if (validation == AbilityTargetValidationReason.TargetNotRequired)
        {
            GameHUDController.Instance?.SetTargetFeedback(
                GetAbilityTargetFeedback(validation),
                false
            );
            return;
        }

        if (validation != AbilityTargetValidationReason.Valid)
        {
            bool canStartMovement =
                validation == AbilityTargetValidationReason.DirectionNotAdjacent
                || validation == AbilityTargetValidationReason.OutOfRange;
            if (
                canStartMovement
                && selectedCell != startCell
                && PathSelection.Instance?.TryStartPath() == true
            )
            {
                GameHUDController.Instance?.ClearTargetFeedback();
                return;
            }
            ShowInvalidTargetFeedback(validation);
            return;
        }

        plans[selectedUnit] = (true, new List<Vector3> { start, square });
        currentPlan = plans[selectedUnit].Item2;
        UpdateAbilityTargetIndicator(start, square, unitData);
        GameHUDController.Instance?.SetTargetFeedback("Target locked.", false);
    }

    public static AbilityTargetValidationReason ValidateAbilityTarget(
        UnitData unitData,
        Vector2Int startCell,
        Vector2Int targetCell,
        bool isInBounds,
        bool isWall
    )
    {
        if (unitData == null)
            return AbilityTargetValidationReason.AbilityUnavailable;
        if (!unitData.selectAbilitySquare)
            return AbilityTargetValidationReason.TargetNotRequired;
        if (!isInBounds)
            return AbilityTargetValidationReason.OutOfBounds;

        if (unitData.selectAbilityDirection)
        {
            return unitData.abilityFixedDistance > 0
                && GridSystem.TryGetAdjacentDirection(startCell, targetCell, out _)
                ? AbilityTargetValidationReason.Valid
                : AbilityTargetValidationReason.DirectionNotAdjacent;
        }

        int manhattanCells =
            Mathf.Abs(targetCell.x - startCell.x) + Mathf.Abs(targetCell.y - startCell.y);
        if (manhattanCells > unitData.abilitySquareRange)
            return AbilityTargetValidationReason.OutOfRange;
        if (!unitData.responseDistLine && isWall)
            return AbilityTargetValidationReason.WallBlocked;
        return AbilityTargetValidationReason.Valid;
    }

    public static string GetAbilityTargetFeedback(AbilityTargetValidationReason reason)
    {
        return reason switch
        {
            AbilityTargetValidationReason.NoTargetCell =>
                "Choose a highlighted target cell.",
            AbilityTargetValidationReason.TargetNotRequired =>
                "This ability activates on its caster.",
            AbilityTargetValidationReason.AbilityUnavailable =>
                "This unit has no targetable ability.",
            AbilityTargetValidationReason.OutOfBounds =>
                "Choose a target inside the battlefield.",
            AbilityTargetValidationReason.DirectionNotAdjacent =>
                "Choose one of the eight adjacent direction cells.",
            AbilityTargetValidationReason.OutOfRange =>
                "Target is outside this ability's range.",
            AbilityTargetValidationReason.WallBlocked =>
                "That ability cannot target a wall cell.",
            _ => string.Empty,
        };
    }

    private static void ShowInvalidTargetFeedback(AbilityTargetValidationReason reason)
    {
        GameHUDController.Instance?.SetTargetFeedback(GetAbilityTargetFeedback(reason), true);
    }

    // Runtime-generated ability target visuals (marker + AOE disc, or preview line), mirroring
    // how AreaLock builds its laser at runtime — no prefab/scene references needed.
    private GameObject abilityTargetIndicator;

    void UpdateAbilityTargetIndicator(Vector3 start, Vector3 square, UnitData unitData)
    {
        ClearAbilityTargetIndicator();
        abilityTargetIndicator = new GameObject("AbilityTargetIndicator");

        if (selectedUnit != null && selectedUnit.GetComponent<Smoke>() != null)
        {
            CreateSmokeTargetIndicator(square);
            return;
        }

        if (
            unitData.selectAbilityDirection
            && GridSystem.TryGetAdjacentDirection(
                GridSystem.ConvertToGridCoords(start),
                GridSystem.ConvertToGridCoords(square),
                out Vector2Int abilityDirection
            )
        )
        {
            Vector2Int destination = GridSystem.GetDirectionalDestination(
                GridSystem.ConvertToGridCoords(start),
                abilityDirection,
                unitData.abilityFixedDistance,
                GameLoop.wallLayout
            );
            square = GameLoop.gridCoordToWorld(destination);
        }

        if (unitData.responseDistLine)
        {
            Vector3 casterPos = selectedUnit.transform.position;
            Vector3 direction = (
                square + Helper.heightOffset(selectedUnit.transform) - casterPos
            ).normalized;
            if (direction == Vector3.zero)
                return;
            Vector3 end = casterPos + direction * 50f;
            if (
                Physics.Raycast(
                    casterPos,
                    direction,
                    out RaycastHit hit,
                    Mathf.Infinity,
                    LayerMask.GetMask("Walls")
                )
            )
            {
                end = hit.point;
            }

            LineRenderer lr = abilityTargetIndicator.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = new Color(1f, 0.8f, 0f, 0.9f);
            lr.startWidth = lr.endWidth = 0.12f;
            lr.positionCount = 2;
            lr.SetPosition(0, casterPos);
            lr.SetPosition(1, end);
        }
        else
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(marker.GetComponent<Collider>());
            marker.transform.parent = abilityTargetIndicator.transform;
            float diameter = Mathf.Max(1f, 2f * unitData.abilityRadius * cellSize);
            marker.transform.position = square + new Vector3(0, 0.15f, 0);
            marker.transform.localScale = new Vector3(diameter, 0.05f, diameter);
            Renderer rend = marker.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Sprites/Default"));
            rend.material.color = new Color(1f, 0.8f, 0f, 0.5f);
        }
    }

    void CreateSmokeTargetIndicator(Vector3 square)
    {
        Vector2Int center = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(square)
        );
        Material previewMaterial = new(Shader.Find("Sprites/Default"));
        Color previewColor = new(1f, 0.8f, 0f, 0.9f);
        float halfCell = cellSize * 0.46f;
        float lineWidth = Mathf.Max(0.04f, cellSize * 0.04f);

        foreach (
            Vector2Int cell in GridSystem.GetSquareFootprint(center, Smoke.FootprintRadius)
        )
        {
            GameObject cellOutline = new($"SmokePreviewCell_{cell.x}_{cell.y}");
            cellOutline.transform.SetParent(abilityTargetIndicator.transform, true);

            Vector3 cellCenter = GameLoop.gridCoordToWorld(cell) + new Vector3(0, 0.15f, 0);
            LineRenderer outline = cellOutline.AddComponent<LineRenderer>();
            outline.material = previewMaterial;
            outline.startColor = outline.endColor = previewColor;
            outline.startWidth = outline.endWidth = lineWidth;
            outline.positionCount = 5;
            outline.SetPositions(
                new[]
                {
                    cellCenter + new Vector3(-halfCell, 0, -halfCell),
                    cellCenter + new Vector3(-halfCell, 0, halfCell),
                    cellCenter + new Vector3(halfCell, 0, halfCell),
                    cellCenter + new Vector3(halfCell, 0, -halfCell),
                    cellCenter + new Vector3(-halfCell, 0, -halfCell),
                }
            );
        }
    }

    void ClearAbilityTargetIndicator()
    {
        if (abilityTargetIndicator != null)
        {
            // Destroy is deferred; hide the private planning preview before the phase can advance.
            abilityTargetIndicator.SetActive(false);
            Destroy(abilityTargetIndicator);
        }
        abilityTargetIndicator = null;
    }

    /// <summary>
    /// Selects the team unit occupying the given cell and prepares its movement path for editing.
    /// This is essential during dodge windows, where the unit cards don't map to the alerted set.
    /// </summary>
    public bool TrySelectUnitForMovementAtCell(Vector3 cell)
    {
        foreach (GameObject character in teamCharacters)
        {
            if (!IsPlanningUnitAvailable(character) || character == selectedUnit)
                continue;
            if (GridSystem.GetNearestGridCell(character) == cell)
                return TrySetSelectionMode(character, false);
        }
        return false;
    }

    /// <summary>
    /// Selects the unit that owns a path node and prepares that unit for movement editing.
    /// </summary>
    public bool TrySelectUnitForPathNode(GameObject node)
    {
        if (node == null)
            return false;

        GameObject owner = null;
        for (Transform ancestor = node.transform; ancestor != null; ancestor = ancestor.parent)
        {
            foreach (KeyValuePair<GameObject, GameObject> pair in planVisuals)
            {
                if (pair.Value == null || pair.Value != ancestor.gameObject)
                    continue;

                owner = pair.Key;
                break;
            }
            if (
                owner != null
                || (planVisualsFolder != null && ancestor.gameObject == planVisualsFolder)
            )
                break;
        }

        if (owner == null)
            return false;
        return owner == selectedUnit || TrySetSelectionMode(owner, false);
    }

    private static bool IsPlanningUnitAvailable(GameObject unit)
    {
        if (unit == null || !unit.activeInHierarchy)
            return false;

        Health health = unit.GetComponent<Health>();
        return health == null || health.IsAlive;
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

        int localTeamIndex = GameLoop.Instance != null ? GameLoop.Instance.LocalTeamIndex : -1;
        if (localTeamIndex < 0)
            return localTeamCharacters;

        localTeamCharacters.AddRange(
            NetworkManager
                .Singleton.SpawnManager.SpawnedObjectsList.Where(netObj => netObj != null)
                .Select(netObj => netObj.GetComponent<Unit>())
                .Where(unit =>
                    unit != null
                    && unit.TeamIndex == localTeamIndex
                    && unit.RosterSlot >= 0
                )
                .OrderBy(unit => unit.RosterSlot)
                .Select(unit => unit.gameObject)
        );

        return localTeamCharacters;
    }

    public void SwitchToUnit(int unitIndex)
    {
        SelectUnit(unitIndex);
    }

    public void SelectUnit(int unitIndex)
    {
        if (!useUnitCards || unitIndex < 0 || unitIndex >= teamCharacters.Count)
            return;

        GameObject unit = teamCharacters[unitIndex];
        if (IsPlanningUnitAvailable(unit))
            SwitchToUnit(unit);
    }

    public void SetSelectionMode(int unitIndex, bool abilityMode)
    {
        if (!useUnitCards || unitIndex < 0 || unitIndex >= teamCharacters.Count)
            return;

        TrySetSelectionMode(teamCharacters[unitIndex], abilityMode);
    }

    private bool TrySetSelectionMode(GameObject unit, bool abilityMode)
    {
        if (!IsPlanningUnitAvailable(unit) || (abilityMode && unit.GetComponent<Ability>() == null))
            return false;

        if (selectedUnit != unit)
            SwitchToUnit(unit);

        if (!plans.ContainsKey(unit))
        {
            plans[unit] = (false, new List<Vector3> { GridSystem.GetNearestGridCell(unit) });
        }

        if (plans[unit].Item1 != abilityMode)
        {
            plans[unit] = (abilityMode, new List<Vector3> { GridSystem.GetNearestGridCell(unit) });
            ClearAbilityTargetIndicator();
            ResetVisualPlan();
        }

        GameHUDController.Instance?.ClearTargetFeedback();
        ApplySelectedUnitModeVisuals();
        RefreshUnitCards();
        return true;
    }

    public void SwitchToUnit(GameObject newSelectedUnit, int range = -1)
    {
        if (newSelectedUnit != null && !IsPlanningUnitAvailable(newSelectedUnit))
            return;

        GameObject previousUnit = selectedUnit;
        selectedUnit = newSelectedUnit;
        GameHUDController.Instance?.ClearTargetFeedback();
        if (selectedUnit == null)
        {
            ClearAbilityTargetIndicator();
            Destroy(moveOverlay);
            RefreshUnitCards();
            return;
        }
        if (previousUnit != selectedUnit)
            ClearAbilityTargetIndicator();

        if (!plans.ContainsKey(selectedUnit))
        {
            plans[selectedUnit] = (
                false,
                new List<Vector3> { GridSystem.GetNearestGridCell(selectedUnit) }
            );
            ResetVisualPlan();
        }

        ApplySelectedUnitModeVisuals(range);
        RefreshUnitCards();
    }

    private void ApplySelectedUnitModeVisuals(int range = -1)
    {
        if (selectedUnit == null || !plans.ContainsKey(selectedUnit))
            return;

        // Show the range for the explicit MOVE / ABILITY mode selected on the card.
        UnitData unitData = selectedUnit.GetComponent<Movement>().unitData;
        bool abilityMode = plans[selectedUnit].Item1;
        int movementRange =
            range != -1
                ? range
                : (planningRangeOverride != -1 ? planningRangeOverride : unitData.moveDist);
        if (abilityMode && unitData.selectAbilityDirection)
            DisplayAbilityDirections();
        else
            DisplayMoveRange(
                abilityMode
                    ? (unitData.selectAbilitySquare ? unitData.abilitySquareRange : 0)
                    : movementRange
            );

        currentPlan = plans[selectedUnit].Item2;
        if (planVisuals.TryGetValue(selectedUnit, out GameObject visuals))
            currentVisuals = visuals;

        if (
            abilityMode
            && unitData.selectAbilitySquare
            && currentPlan != null
            && currentPlan.Count >= 2
        )
        {
            UpdateAbilityTargetIndicator(currentPlan[0], currentPlan[1], unitData);
        }
    }

    private void RefreshUnitCards()
    {
        if (!useUnitCards || GameLoop.Instance == null)
            return;

        int count = teamCharacters.Count;
        for (int i = 0; i < count; i++)
        {
            bool selected = teamCharacters[i] == selectedUnit;
            bool abilityMode =
                plans.TryGetValue(teamCharacters[i], out (bool, List<Vector3>) plan) && plan.Item1;

            GameHUDController.Instance?.SetCardPlanningState(i, selected, abilityMode);
        }
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
        ClearAbilityTargetIndicator();
        planningRangeOverride = -1;

        if (useUnitCards)
        {
            GameHUDController.Instance?.ClearCardPlanningStates();
        }
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

    void DisplayAbilityDirections()
    {
        Destroy(moveOverlay);
        if (selectedUnit == null)
            return;

        moveOverlay = GridSystem.DisplayGridDirections(
            GridSystem.GetNearestGridCell(selectedUnit),
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
            Debug.Log(
                $"Unit: {pair.Key.name}, Boolean: {boolValue}, Path: {string.Join(", ", path)}"
            );
        }
    }
}
