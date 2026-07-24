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
    private Dictionary<GameObject, PathRibbon> planVisuals = new();
    private readonly RouteLaneMap laneMap = new();

    public PathRibbon currentRibbon;

    // Range overlays
    private GameObject moveOverlay;

    private System.Action<PathsDict, int> planningCallback;
    private System.Action<int> planningUnlockCallback;
    private bool planningActive;
    private bool planningInitialized;
    private bool planningSubmitted;
    private bool planningLockPending;
    private bool planningUnlockPending;
    private bool lockInAvailable;
    private int planningSessionVersion;
    private int planningRoundToken = -1;
    private int planningCommitVersion;
    private double planningEndTime;

    [SerializeField]
    private GameObject moveOverlayCellPrefab;

    private static float cellSize => GameLoop.cellSize; //shortened reference to GameLoop.cellSize

    public static PlanMovement Instance { get; private set; }
    public bool CanEditPlan => planningActive && planningInitialized && !planningSubmitted;
    public bool CanUnlockPlan =>
        planningActive
        && planningInitialized
        && planningSubmitted
        && !planningLockPending
        && !planningUnlockPending
        && lockInAvailable;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        Instance = this;
    }

    void OnDisable()
    {
        EndPlanningSession();
        if (Instance == this)
            Instance = null;
    }

    public IEnumerator StartPlanning(
        System.Action<PathsDict, int> callback,
        double endTime,
        List<GameObject> units = null,
        int range = -1,
        bool allowLockIn = false,
        System.Action<int> unlockCallback = null,
        int planningRound = -1
    )
    {
        if (planningActive || planningInitialized)
            EndPlanningSession();
        int sessionVersion = ++planningSessionVersion;

        planningCallback = callback;
        planningActive = true;
        planningInitialized = false;
        planningSubmitted = false;
        planningLockPending = false;
        planningUnlockPending = false;
        lockInAvailable = allowLockIn && units == null;
        planningUnlockCallback = unlockCallback;
        planningRoundToken = planningRound;
        planningCommitVersion = 0;
        planningEndTime = endTime;
        useUnitCards = units == null;
        planningRangeOverride = range;
        GameHUDController.Instance?.HidePlanningCommit();
        teamCharacters = units ?? PopulateTeamCharacters();
        while (
            sessionVersion == planningSessionVersion
            &&
            planningActive
            && units == null
            && teamCharacters.Count < RosterRules.UnitsPerPlayer
            && NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsListening
            && NetworkManager.Singleton.ServerTime.Time < endTime
        )
        {
            // Unit spawns and TeamIndex NetworkVariables can arrive just after the phase RPC.
            yield return null;
            if (!TryRefreshTeamCharactersForSession(sessionVersion))
                yield break;
        }

        if (!planningActive || sessionVersion != planningSessionVersion)
            yield break;

        InitializeVisuals();
        planningInitialized = true;
        // Select the first unit up front so planning (and especially the dodge window, where
        // cards don't map to the alerted subset) is immediately usable without a card click.
        selectedUnit = null;
        SwitchToUnit(teamCharacters.FirstOrDefault(IsPlanningUnitAvailable), range);
        if (lockInAvailable)
            GameHUDController.Instance?.ShowPlanningCommitReady();
        else
            GameHUDController.Instance?.HidePlanningCommit();

        NetworkManager networkManager = NetworkManager.Singleton;
        float timer;
        while (
            sessionVersion == planningSessionVersion
            &&
            planningActive
            && networkManager != null
            && (timer = (float)(endTime - networkManager.ServerTime.Time)) > 0
        )
        {
            GameHUDController.Instance?.SetTimer(timer);
            if (!CanEditPlan)
            {
                PathSelection.Instance?.CancelCurrentDrag();
            }
            else if (selectedUnit == null)
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

        if (!planningActive || sessionVersion != planningSessionVersion)
            yield break;
        if (networkManager == null || !networkManager.IsListening)
        {
            EndPlanningSession();
            yield break;
        }

        if (!planningSubmitted)
            SubmitCurrentPlan(sessionVersion);
    }

    public bool TryLockIn()
    {
        if (!CanEditPlan || !lockInAvailable)
            return false;

        GameObject incompleteAbilityUnit = plans
            .Where(entry =>
            {
                if (!entry.Value.Item1)
                    return false;
                UnitData data = entry.Key?.GetComponent<Movement>()?.unitData;
                return data != null
                    && data.selectAbilitySquare
                    && (entry.Value.Item2 == null || entry.Value.Item2.Count < 2);
            })
            .Select(entry => entry.Key)
            .FirstOrDefault();
        if (incompleteAbilityUnit != null)
        {
            SwitchToUnit(incompleteAbilityUnit);
            GameHUDController.Instance?.SetTargetFeedback(
                "Choose an ability target before locking in.",
                true
            );
            return false;
        }

        SubmitCurrentPlan(planningSessionVersion);
        return true;
    }

    public bool TryUnlock()
    {
        if (!CanUnlockPlan)
            return false;

        if (planningUnlockCallback == null)
            return false;

        planningUnlockPending = true;
        PathSelection.Instance?.CancelCurrentDrag();
        GameHUDController.Instance?.ShowPlanningCommitUnlocking();
        planningUnlockCallback.Invoke(planningCommitVersion);
        return true;
    }

    public void NotifyPlanningCommitAccepted(int planningRound, int commitVersion)
    {
        if (
            !IsCurrentPlanningCommit(planningRound, commitVersion)
            || !planningSubmitted
            || !planningLockPending
        )
        {
            return;
        }

        planningLockPending = false;
        GameHUDController.Instance?.ShowPlanningCommitWaiting();
    }

    public void NotifyPlanningCommitRetracted(int planningRound, int commitVersion)
    {
        if (
            !IsCurrentPlanningCommit(planningRound, commitVersion)
            || !planningSubmitted
            || !planningUnlockPending
        )
        {
            return;
        }

        planningSubmitted = false;
        planningLockPending = false;
        planningUnlockPending = false;
        if (HasPlanningDeadlineElapsed())
        {
            SubmitCurrentPlan(planningSessionVersion);
            return;
        }

        GameHUDController.Instance?.SetCardsInteractable(true);
        GameHUDController.Instance?.ShowPlanningCommitReady();
        RestoreEditablePlanningVisuals();
    }

    public void NotifyPlanningCommitFinalized(int planningRound)
    {
        if (
            !planningActive
            || !lockInAvailable
            || planningRound != planningRoundToken
        )
        {
            return;
        }

        planningSubmitted = true;
        planningLockPending = false;
        planningUnlockPending = false;
        lockInAvailable = false;
        PathSelection.Instance?.CancelCurrentDrag();
        HideEditablePlanningOverlays();
        GameHUDController.Instance?.SetCardsInteractable(false);
        GameHUDController.Instance?.ShowPlanningCommitLocked();
    }

    private bool IsCurrentPlanningCommit(int planningRound, int commitVersion)
    {
        return planningActive
            && lockInAvailable
            && planningRound == planningRoundToken
            && commitVersion == planningCommitVersion;
    }

    private bool HasPlanningDeadlineElapsed()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null
            && networkManager.IsListening
            && HasPlanningDeadlineElapsed(networkManager.ServerTime.Time, planningEndTime);
    }

    public static bool HasPlanningDeadlineElapsed(double serverTime, double endTime)
    {
        return serverTime >= endTime;
    }

    public void EndPlanningSession(bool hideCommit = true)
    {
        planningSessionVersion++;
        planningActive = false;
        planningInitialized = false;
        planningSubmitted = true;
        planningLockPending = false;
        planningUnlockPending = false;
        planningCallback = null;
        planningUnlockCallback = null;
        lockInAvailable = false;
        planningRoundToken = -1;
        planningEndTime = 0d;
        PathSelection.Instance?.CancelCurrentDrag();
        ClearVisuals();
        GameHUDController.Instance?.SetTimer(0f);
        if (hideCommit)
            GameHUDController.Instance?.HidePlanningCommit();
    }

    private void SubmitCurrentPlan(int sessionVersion)
    {
        if (
            sessionVersion != planningSessionVersion
            || !planningActive
            || planningSubmitted
        )
            return;

        bool showCommitState = lockInAvailable;
        PathsDict submittedPlans = plans;
        System.Action<PathsDict, int> callback = planningCallback;
        planningSubmitted = true;
        planningLockPending = showCommitState;
        planningUnlockPending = false;
        planningCommitVersion++;
        PathSelection.Instance?.CancelCurrentDrag();
        if (showCommitState)
        {
            HideEditablePlanningOverlays();
            GameHUDController.Instance?.SetCardsInteractable(false);
            GameHUDController.Instance?.ShowPlanningCommitSending();
        }
        else
        {
            planningActive = false;
            planningInitialized = false;
            planningCallback = null;
            ClearVisuals();
            GameHUDController.Instance?.SetTimer(0f);
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            if (showCommitState)
            {
                planningSubmitted = false;
                planningLockPending = false;
                GameHUDController.Instance?.SetCardsInteractable(true);
                GameHUDController.Instance?.ShowPlanningCommitReady();
                RestoreEditablePlanningVisuals();
            }
            else
            {
                GameHUDController.Instance?.HidePlanningCommit();
            }
            return;
        }
        callback?.Invoke(submittedPlans, planningCommitVersion);
    }

    private void HideEditablePlanningOverlays()
    {
        if (moveOverlay != null)
        {
            moveOverlay.SetActive(false);
            Destroy(moveOverlay);
            moveOverlay = null;
        }
        ClearAbilityTargetIndicator();
    }

    private void RestoreEditablePlanningVisuals()
    {
        if (!CanEditPlan || selectedUnit == null)
            return;

        ApplySelectedUnitModeVisuals();
        RefreshUnitCards();
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

        Vector3? clicked = Mouse.GetGridCellUnderMouse();
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

    // Runtime-generated ability target visuals (AOE disc, single/multi-cell square outline, or
    // preview line), mirroring how AreaLock builds its laser at runtime — no prefab/scene
    // references needed.
    private GameObject abilityTargetIndicator;

    // MoveOverlayCell's root plane is opaque (URP Lit, ZWrite on) at world y=0.2, and its "Inner"
    // highlight sits at y=0.227; both are shown under the target cell whenever an ability's range
    // is displayed. A transparent marker placed at or below that height fails the depth test
    // against the opaque tile and is fully hidden, even at full alpha. Keep ability target
    // markers clearly above both so they render on top of the range overlay.
    private const float AbilityIndicatorHeight = 0.26f;

    void UpdateAbilityTargetIndicator(Vector3 start, Vector3 square, UnitData unitData)
    {
        ClearAbilityTargetIndicator();
        abilityTargetIndicator = new GameObject("AbilityTargetIndicator");

        if (selectedUnit != null && selectedUnit.GetComponent<Smoke>() != null)
        {
            CreateSquareFootprintIndicator(square, Smoke.FootprintRadius);
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
        else if (unitData.abilityRadius > 0f)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(marker.GetComponent<Collider>());
            marker.transform.parent = abilityTargetIndicator.transform;
            float diameter = 2f * unitData.abilityRadius * cellSize;
            marker.transform.position = square + new Vector3(0, AbilityIndicatorHeight, 0);
            marker.transform.localScale = new Vector3(diameter, 0.05f, diameter);
            Renderer rend = marker.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Sprites/Default"));
            rend.material.color = new Color(1f, 0.8f, 0f, 0.5f);
        }
        else
        {
            // Point-target abilities have no blast radius to draw as an AOE disc (e.g. the Pogo
            // Rider's Jump — it lands on exactly one cell). Outline that single target square
            // instead so the selection reads clearly, matching the other units' visible markers.
            CreateSquareFootprintIndicator(square, 0);
        }
    }

    void CreateSquareFootprintIndicator(Vector3 square, int radius)
    {
        Vector2Int center = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(square)
        );
        Material previewMaterial = new(Shader.Find("Sprites/Default"));
        Color previewColor = new(1f, 0.8f, 0f, 0.9f);
        float halfCell = cellSize * 0.46f;
        float lineWidth = Mathf.Max(0.04f, cellSize * 0.04f);

        foreach (Vector2Int cell in GridSystem.GetSquareFootprint(center, radius))
        {
            GameObject cellOutline = new($"AbilityTargetCell_{cell.x}_{cell.y}");
            cellOutline.transform.SetParent(abilityTargetIndicator.transform, true);

            Vector3 cellCenter =
                GameLoop.gridCoordToWorld(cell) + new Vector3(0, AbilityIndicatorHeight, 0);
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
        if (!CanEditPlan)
            return false;

        foreach (GameObject character in teamCharacters)
        {
            if (!IsPlanningUnitAvailable(character) || character == selectedUnit)
                continue;
            if (GridSystem.GetNearestGridCell(character) == cell)
                return TrySetSelectionMode(character, false);
        }
        return false;
    }

    // Two routes drawn closer together than this within one cell count as pointed at equally.
    private const float LaneTieDistance = 0.02f;

    /// <summary>
    /// Finds the drawn movement route nearest the pointer inside the cell it is over, reporting the
    /// owner and the index of that cell within the owner's plan. Candidates are compared against
    /// the position each ribbon actually drew, so where several routes share a cell you get the one
    /// you are pointing at; the selected unit only breaks a genuine tie.
    /// </summary>
    public bool TryFindPlannedRouteAtPoint(
        Vector3 worldPoint,
        out GameObject unit,
        out int planIndex
    )
    {
        unit = null;
        planIndex = -1;
        if (!CanEditPlan)
            return false;

        Vector3 cell = GridSystem.GetNearestGridCell(worldPoint);
        float bestDistance = float.MaxValue;

        foreach (GameObject character in teamCharacters)
        {
            if (!TryFindRouteCellForUnit(character, cell, out int index))
                continue;

            float distance = GetDrawnRouteDistance(character, index, worldPoint, cell);
            bool clearlyCloser = distance < bestDistance - LaneTieDistance;
            bool tiedButSelected =
                character == selectedUnit && distance <= bestDistance + LaneTieDistance;
            if (unit != null && !clearlyCloser && !tiedButSelected)
                continue;

            unit = character;
            planIndex = index;
            bestDistance = Mathf.Min(bestDistance, distance);
        }
        return unit != null;
    }

    /// <summary>
    /// Flat distance from the pointer to where a unit's ribbon drew the given plan step, falling
    /// back to the cell centre for a plan too short to have been drawn.
    /// </summary>
    private float GetDrawnRouteDistance(
        GameObject unit,
        int planIndex,
        Vector3 worldPoint,
        Vector3 cell
    )
    {
        Vector3 reference = cell;
        if (
            planVisuals.TryGetValue(unit, out PathRibbon ribbon)
            && ribbon != null
            && ribbon.TryGetDrawnPoint(planIndex, out Vector3 drawn)
        )
        {
            reference = drawn;
        }

        Vector2 delta = new(worldPoint.x - reference.x, worldPoint.z - reference.z);
        return delta.magnitude;
    }

    private bool TryFindRouteCellForUnit(GameObject unit, Vector3 cell, out int planIndex)
    {
        planIndex = -1;
        if (
            unit == null
            || !IsPlanningUnitAvailable(unit)
            || !plans.TryGetValue(unit, out (bool, List<Vector3>) plan)
            || plan.Item1
            || plan.Item2 == null
        )
        {
            return false;
        }

        planIndex = plan.Item2.IndexOf(cell);
        return planIndex >= 0;
    }

    /// <summary>
    /// Selects the unit that owns a drawn route and prepares that unit for movement editing.
    /// </summary>
    public bool TrySelectUnitForRoute(GameObject unit)
    {
        if (!CanEditPlan || unit == null)
            return false;

        return unit == selectedUnit || TrySetSelectionMode(unit, false);
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

    private bool TryRefreshTeamCharactersForSession(int sessionVersion)
    {
        if (sessionVersion != planningSessionVersion || !planningActive)
            return false;

        teamCharacters = PopulateTeamCharacters();
        return true;
    }

    public void SwitchToUnit(int unitIndex)
    {
        SelectUnit(unitIndex);
    }

    public bool TryActivateUnitCard(int unitIndex)
    {
        if (
            !CanEditPlan
            || !useUnitCards
            || unitIndex < 0
            || unitIndex >= teamCharacters.Count
        )
            return false;

        GameObject unit = teamCharacters[unitIndex];
        if (!IsPlanningUnitAvailable(unit))
            return false;

        if (selectedUnit != unit)
        {
            SwitchToUnit(unit);
            return selectedUnit == unit;
        }

        bool abilityMode =
            plans.TryGetValue(unit, out (bool, List<Vector3>) plan) && plan.Item1;
        return TrySetSelectionMode(unit, !abilityMode);
    }

    public void SelectUnit(int unitIndex)
    {
        if (
            !CanEditPlan
            || !useUnitCards
            || unitIndex < 0
            || unitIndex >= teamCharacters.Count
        )
            return;

        GameObject unit = teamCharacters[unitIndex];
        if (IsPlanningUnitAvailable(unit))
            SwitchToUnit(unit);
    }

    public void SetSelectionMode(int unitIndex, bool abilityMode)
    {
        if (
            !CanEditPlan
            || !useUnitCards
            || unitIndex < 0
            || unitIndex >= teamCharacters.Count
        )
            return;

        TrySetSelectionMode(teamCharacters[unitIndex], abilityMode);
    }

    private bool TrySetSelectionMode(GameObject unit, bool abilityMode)
    {
        if (!CanEditPlan || !IsPlanningUnitAvailable(unit))
            return false;

        if (abilityMode)
        {
            Unit identity = unit.GetComponent<Unit>();
            if (unit.GetComponent<Ability>() == null || identity == null)
            {
                GameHUDController.Instance?.SetTargetFeedback(
                    "This unit can move only.",
                    true
                );
                return false;
            }
            if (!identity.CanUseAbility)
            {
                int rounds = identity.AbilityCooldownRoundsRemaining;
                string roundText = rounds == 1 ? "round" : "rounds";
                GameHUDController.Instance?.SetTargetFeedback(
                    $"{unit.GetComponent<Movement>()?.unitData?.abilityName ?? "Ability"} recharges in {rounds} {roundText}.",
                    true
                );
                return false;
            }
        }

        if (selectedUnit != unit)
            SwitchToUnit(unit);

        if (!plans.ContainsKey(unit))
        {
            plans[unit] = (false, new List<Vector3> { GridSystem.GetNearestGridCell(unit) });
        }

        if (plans[unit].Item1 != abilityMode)
        {
            PathSelection.Instance?.CancelCurrentDrag();
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
        if (!CanEditPlan)
            return;
        if (newSelectedUnit != null && !IsPlanningUnitAvailable(newSelectedUnit))
            return;

        GameObject previousUnit = selectedUnit;
        selectedUnit = newSelectedUnit;
        GameHUDController.Instance?.ClearTargetFeedback();
        if (selectedUnit == null)
        {
            ClearAbilityTargetIndicator();
            Destroy(moveOverlay);
            RefreshAllRibbons();
            RefreshUnitCards();
            return;
        }
        if (previousUnit != selectedUnit)
        {
            PathSelection.Instance?.CancelCurrentDrag();
            ClearAbilityTargetIndicator();
        }

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

        // Show the range for the card's current movement or ability face.
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
        RefreshAllRibbons();

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
        PathRibbon ribbon = EnsureRibbon(selectedUnit);
        currentRibbon = ribbon;
        ribbon?.Clear();
    }

    /// <summary>
    /// Roster slot drives both a unit's route colour and its lane, so a route stays tied to the
    /// same unit card all match. Falls back to team ordering if identity has not replicated yet.
    /// </summary>
    private int GetRosterSlot(GameObject unit)
    {
        Unit identity = unit != null ? unit.GetComponent<Unit>() : null;
        int slot = identity != null ? identity.RosterSlot : -1;
        return slot >= 0 ? slot : Mathf.Max(0, teamCharacters.IndexOf(unit));
    }

    private PathRibbon EnsureRibbon(GameObject unit)
    {
        if (unit == null)
            return null;
        if (planVisuals.TryGetValue(unit, out PathRibbon existing) && existing != null)
            return existing;

        int slot = GetRosterSlot(unit);
        PathRibbon ribbon = PathRibbon.Create(
            planVisualsFolder != null ? planVisualsFolder.transform : null,
            $"PlanRoute_{slot}",
            slot
        );
        planVisuals[unit] = ribbon;
        return ribbon;
    }

    /// <summary>
    /// Redraws after <see cref="PathSelection"/> extends or trims the selected route. Editing one
    /// route changes which cells are shared, so every route is rebuilt rather than just this one.
    /// </summary>
    public void NotifyCurrentRouteChanged()
    {
        RefreshAllRibbons();
    }

    /// <summary>
    /// Repaints every route so the unit being edited reads bright and full width while the rest
    /// stay dimmed but legible, and each route holds the centre of its cells except where it has
    /// to share them.
    /// </summary>
    private void RefreshAllRibbons()
    {
        currentRibbon = selectedUnit != null ? EnsureRibbon(selectedUnit) : null;
        RebuildLaneMap();
        foreach (KeyValuePair<GameObject, PathRibbon> pair in planVisuals)
        {
            if (pair.Value == null)
                continue;

            pair.Value.SetSelected(pair.Key == selectedUnit);
            DrawRoute(pair.Key, pair.Value);
        }
    }

    private void RebuildLaneMap()
    {
        laneMap.Clear();
        foreach (KeyValuePair<GameObject, (bool, List<Vector3>)> entry in plans)
        {
            // Ability plans hold a target square rather than a route, so they claim no lanes.
            if (entry.Value.Item1 || entry.Value.Item2 == null)
                continue;

            laneMap.AddRoute(GetRosterSlot(entry.Key), entry.Value.Item2);
        }
    }

    private void DrawRoute(GameObject unit, PathRibbon ribbon)
    {
        if (ribbon == null)
            return;

        bool hasPlan = plans.TryGetValue(unit, out (bool, List<Vector3>) plan);
        if (!hasPlan || plan.Item1 || plan.Item2 == null)
            ribbon.Clear();
        else
            ribbon.SetRoute(plan.Item2, laneMap);
    }

    //================
    void InitializeVisuals()
    {
        plans = new PathsDict();
        planVisuals = new Dictionary<GameObject, PathRibbon>();
        currentRibbon = null;
        AddCharacterOutlines();
        planVisualsFolder = new GameObject("PlanVisuals");
    }

    void ClearVisuals()
    {
        RemoveCharacterOutlines();
        if (planVisualsFolder != null)
        {
            planVisualsFolder.SetActive(false);
            Destroy(planVisualsFolder);
        }
        if (moveOverlay != null)
        {
            moveOverlay.SetActive(false);
            Destroy(moveOverlay);
        }
        ClearAbilityTargetIndicator();
        planVisuals.Clear();
        laneMap.Clear();
        currentRibbon = null;
        planVisualsFolder = null;
        moveOverlay = null;
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

    void AddCharacterOutlines()
    {
        foreach (GameObject character in teamCharacters)
        {
            if (character == null)
                continue;

            Renderer[] renderers = character.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in renderers)
            {
                if (rend != null)
                    rend.renderingLayerMask = 1 << 1;
            }
        }
    }

    void RemoveCharacterOutlines()
    {
        foreach (GameObject character in teamCharacters)
        {
            if (character == null)
                continue;

            Renderer[] renderers = character.GetComponentsInChildren<Renderer>();
            foreach (Renderer rend in renderers)
            {
                if (rend != null)
                    rend.renderingLayerMask = 1 << 0;
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
