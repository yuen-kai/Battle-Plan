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
    AimedAtOwnCell,
}

/// <summary>
/// Manages interactive path planning on a grid for team units.
/// Handles mouse-driven path creation, visual feedback, range displays, and movement validation.
/// Supports both normal movement and dash mechanics with configurable distances.
/// </summary>
public class PlanMovement : MonoBehaviour
{
    private List<GameObject> teamCharacters = new();

    // Every living unit of the local team, which during a dodge window is more than
    // teamCharacters: only the alerted units may be given orders, but the rest of the team still
    // holds the cells it will be standing on when the round resolves.
    private List<GameObject> reservationUnits = new();
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
    public bool CanUnlockPlan => IsCommitAcknowledged && !HasPlanningDeadlineElapsed();

    /// <summary>
    /// Orders are locked in and the server has said so — the state the commit chip offers Unlock
    /// from. Whether that offer still stands is a separate question, since the server stops
    /// honouring unlock requests at the planning deadline.
    /// </summary>
    private bool IsCommitAcknowledged =>
        planningActive
        && planningInitialized
        && planningSubmitted
        && !planningLockPending
        && !planningUnlockPending
        && lockInAvailable;

    /// <summary>
    /// The unit whose orders are being worked on, which is nobody once they are committed. A locked
    /// board is a record rather than a workspace, so every plan on it draws the same way instead of
    /// leaving whichever card was touched last lit up as though it were still the one in hand.
    /// </summary>
    private GameObject HighlightedUnit => CanEditPlan ? selectedUnit : null;

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
        reservationUnits = units == null ? teamCharacters : PopulateTeamCharacters();
        while (
            sessionVersion == planningSessionVersion
            &&
            planningActive
            && units == null
            && teamCharacters.Count < GameLoop.UnitsPerTeamThisMatch
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

            // A dodge response has no lock-in chip, so it normally rides the window's clock out.
            // The tutorial opens that window wide instead and ends it here, on the release of the
            // drag, which is what lets a first-time player take the dodge at their own pace.
            if (ShouldCommitTutorialDodgeNow())
                SubmitCurrentPlan(sessionVersion);

            yield return null;
        }

        if (!planningActive || sessionVersion != planningSessionVersion)
            yield break;

        // Orders close on this deadline, but the phase stays up past it while the server collects
        // late submissions. Run the clock out here rather than when the phase ends, so the timer
        // does not sit reading "1" through a second the player can no longer act in.
        GameHUDController.Instance?.SetTimer(0f);
        if (networkManager == null || !networkManager.IsListening)
        {
            EndPlanningSession();
            yield break;
        }

        if (!planningSubmitted)
            SubmitCurrentPlan(sessionVersion);
        else if (IsCommitAcknowledged)
            // Orders locked in earlier are final now that the deadline has passed. Seal the chip
            // instead of leaving a live Unlock the server can only answer with "Locked".
            GameHUDController.Instance?.ShowPlanningCommitLocked();
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
        // The acknowledgement for a deadline auto-submit arrives after unlocking has closed, so
        // those orders are presented as final rather than as an offer the server would refuse.
        if (HasPlanningDeadlineElapsed())
            GameHUDController.Instance?.ShowPlanningCommitLocked();
        else
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
        ShowCommittedPlanningVisuals();
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

    /// <summary>
    /// True once a tutorial student has dragged an alerted unit somewhere new and let go. Only
    /// dodge sessions qualify: planning sessions have a lock-in chip to commit with.
    /// </summary>
    private bool ShouldCommitTutorialDodgeNow()
    {
        if (!TutorialSession.IsActive || useUnitCards || planningSubmitted)
            return false;
        // Held means the drag is still being drawn; the commit waits for the release rather than
        // firing on the first cell crossed.
        if (Input.GetMouseButton(0) || selectedUnit == null)
            return false;

        return plans.TryGetValue(selectedUnit, out (bool, List<Vector3>) plan)
            && !plan.Item1
            && (plan.Item2?.Count ?? 0) > 1;
    }

    private void SubmitCurrentPlan(int sessionVersion)
    {
        if (
            sessionVersion != planningSessionVersion
            || !planningActive
            || planningSubmitted
        )
            return;

        // A route is free to be drawn across a team-mate's destination, and the clock can run out
        // while one is still resting there. Settle every route before it goes over the wire so the
        // orders the player sees committed are the ones that will actually be carried out.
        PathSelection.Instance?.CancelCurrentDrag();
        TrimAllRoutesToFreeCells();

        bool showCommitState = lockInAvailable;
        PathsDict submittedPlans = plans;
        System.Action<PathsDict, int> callback = planningCallback;
        planningSubmitted = true;
        planningLockPending = showCommitState;
        planningUnlockPending = false;
        planningCommitVersion++;
        if (showCommitState)
        {
            ShowCommittedPlanningVisuals();
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

    /// <summary>
    /// Turns the board from a workspace into a record of what was committed. The range overlay goes,
    /// since it offers squares that can no longer be picked, while the orders themselves — routes
    /// and ability previews alike — stay drawn so the turn can still be read as it plays out. They
    /// repaint on the way, which drops the last edited unit back in line with the rest.
    /// </summary>
    private void ShowCommittedPlanningVisuals()
    {
        if (moveOverlay != null)
        {
            moveOverlay.SetActive(false);
            Destroy(moveOverlay);
            moveOverlay = null;
        }
        RefreshAllRibbons();
        RefreshAbilityIndicators();
    }

    private void RestoreEditablePlanningVisuals()
    {
        if (!CanEditPlan)
            return;

        if (selectedUnit != null)
            ApplySelectedUnitModeVisuals();
        else
            // Ability plans belong to their own units rather than to the selection, so they come
            // back after a cancelled commit even with nothing selected.
            RefreshAbilityIndicators();
        RefreshUnitCards();
    }

    /// <summary>
    /// Planning-phase ability targeting: while a unit is in ability mode (card toggled yellow),
    /// clicks pick a target square inside the displayed range. Directional abilities use one of
    /// the eight adjacent cells as a direction anchor; self-targeted abilities need no square.
    /// The square is stored as the plan's second element: (true, [startCell, targetSquare]).
    /// Clicks that land on one of your own units are read as picking that unit instead; the
    /// square it stands on is still a square, and still aimable.
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

        // Pointing at a unit asks for that unit; pointing at the board names a square. What the
        // pointer is actually over decides which, so a team-mate standing inside an ability's
        // range can be given its orders without the click reading as an aim, while the ground it
        // stands on stays aimable — smoke underfoot is one of the Commander's better plays.
        //
        // Picking a unit and drawing its route is one press in movement mode, and aiming is no
        // reason for it to be two. The press on a team-mate goes straight to the route drag, which
        // finds and takes the unit itself and leaves the gesture live, so the press that moves the
        // selection off this unit is already laying the next one's route.
        GameObject pointedUnit = Mouse.GetFriendlyUnitUnderMouse();
        if (
            pointedUnit != null
            && pointedUnit != selectedUnit
            && PathSelection.Instance?.TryStartPath() == true
        )
        {
            return;
        }

        if (pointedUnit != null && TrySelectPlanningUnit(pointedUnit))
        {
            // The press landed on the unit that was aiming, which has just turned itself back to
            // movement. It gets a drag too, so that press draws a route rather than only deciding
            // what the next press will do.
            if (plans.TryGetValue(selectedUnit, out (bool, List<Vector3>) picked) && !picked.Item1)
                PathSelection.Instance?.BeginRouteDrag();
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
        RefreshAbilityIndicators();
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

        if (!unitData.CanTargetOwnCell && targetCell == startCell)
            return AbilityTargetValidationReason.AimedAtOwnCell;

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
            AbilityTargetValidationReason.AimedAtOwnCell =>
                "Aim away from the square you are standing on.",
            _ => string.Empty,
        };
    }

    private static void ShowInvalidTargetFeedback(AbilityTargetValidationReason reason)
    {
        GameHUDController.Instance?.SetTargetFeedback(GetAbilityTargetFeedback(reason), true);
    }

    // Runtime-generated ability target visuals (AOE disc, single/multi-cell square outline, or
    // preview line), mirroring how AreaLock builds its laser at runtime — no prefab/scene
    // references needed. Held per unit like route ribbons are, so an ability plan stays on the
    // board while its owner sits in the background and another unit takes its orders.
    private Dictionary<GameObject, GameObject> abilityVisuals = new();

    // The blast disc is a filled shape rather than a line, so it needs to sit further back than
    // the outlines it is drawn among to avoid swamping them.
    private const float AbilityDiscAlphaScale = 0.55f;

    private static readonly List<Vector3> abilityPathPoints = new();

    /// <summary>
    /// The colour a unit's ability plan draws in. This is the same per-roster-slot colour its
    /// movement route uses, so a plan on the board — move or ability — carries its owner's
    /// identity and shape alone signals move-versus-ability, leaving no need to read a label to
    /// tell whose grenade is landing where.
    /// </summary>
    private static Color GetAbilityPreviewColor(int rosterSlot, bool selected)
    {
        return PlanPathStyle.GetRouteColor(rosterSlot, selected);
    }

    // MoveOverlayCell's root plane is opaque (URP Lit, ZWrite on) at world y=0.2, and its "Inner"
    // highlight sits at y=0.227; both are shown under the target cell whenever an ability's range
    // is displayed. A transparent marker placed at or below that height fails the depth test
    // against the opaque tile and is fully hidden, even at full alpha. Keep ability target
    // markers clearly above both so they render on top of the range overlay.
    private const float AbilityIndicatorHeight = 0.26f;

    /// <summary>
    /// Redraws the whole team's ability plans. Every unit holding a target keeps its preview on the
    /// board, not just the selected one, so a turn can be judged as a whole — whether the grenade
    /// lands clear of where the Pogo Rider is about to come down — instead of one card at a time.
    /// The unit being edited draws bright and the rest step back.
    /// </summary>
    private void RefreshAbilityIndicators()
    {
        ClearAbilityIndicators();
        foreach (KeyValuePair<GameObject, (bool, List<Vector3>)> entry in plans)
        {
            GameObject unit = entry.Key;
            (bool abilityMode, List<Vector3> plan) = entry.Value;
            if (!abilityMode || plan == null || plan.Count < 2 || !IsPlanningUnitAvailable(unit))
                continue;

            Movement movement = unit.GetComponent<Movement>();
            UnitData unitData = movement != null ? movement.unitData : null;
            if (unitData == null || !unitData.selectAbilitySquare)
                continue;

            abilityVisuals[unit] = BuildAbilityIndicator(
                unit,
                plan[0],
                plan[1],
                unitData,
                unit == HighlightedUnit
            );
        }
    }

    /// <summary>
    /// Builds one unit's ability preview: the path the ability travels, plus a marker on whatever
    /// it arrives at. Everything hangs off a single host so one unit's plan can be dropped or
    /// rebuilt without disturbing anyone else's.
    /// </summary>
    GameObject BuildAbilityIndicator(
        GameObject unit,
        Vector3 start,
        Vector3 square,
        UnitData unitData,
        bool selected
    ) =>
        BuildAbilityPreview(
            planVisualsFolder != null ? planVisualsFolder.transform : null,
            unit,
            start,
            square,
            unitData,
            GetRosterSlot(unit),
            selected
        );

    /// <summary>
    /// The board half of <see cref="BuildAbilityIndicator"/>, with the planning screen's own state
    /// handed in rather than read off this component. Tooling that has to put a plan on the board
    /// without a planning screen behind it draws the shipping telegraph through here instead of a
    /// lookalike built to match it.
    /// </summary>
    public static GameObject BuildAbilityPreview(
        Transform parent,
        GameObject unit,
        Vector3 start,
        Vector3 square,
        UnitData unitData,
        int rosterSlot,
        bool selected
    )
    {
        GameObject host = new($"AbilityPlan_{rosterSlot}");
        host.transform.SetParent(parent, false);
        Color color = GetAbilityPreviewColor(rosterSlot, selected);

        // Ask before the directional branch below rewrites `square` into a resolved destination:
        // an ability is handed the square the player actually picked and works out for itself
        // where that leads.
        ShowAbilityPathPreview(host, unit, square, unitData, color);

        if (unit.GetComponent<Smoke>() != null)
        {
            CreateSquareFootprintIndicator(host, square, Smoke.FootprintRadius, color);
            return host;
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
            Vector3 casterPos = unit.transform.position;
            Vector3 direction = (
                square + Helper.heightOffset(unit.transform) - casterPos
            ).normalized;
            if (direction == Vector3.zero)
                return host;
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

            LineRenderer lr = host.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = color;
            lr.startWidth = lr.endWidth = 0.12f;
            lr.positionCount = 2;
            lr.SetPosition(0, casterPos);
            lr.SetPosition(1, end);
        }
        else if (unitData.abilityRadius > 0f)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(marker.GetComponent<Collider>());
            marker.transform.parent = host.transform;
            float diameter = 2f * unitData.abilityRadius * cellSize;
            marker.transform.position = square + new Vector3(0, AbilityIndicatorHeight, 0);
            marker.transform.localScale = new Vector3(diameter, 0.05f, diameter);
            Color discColor = color;
            discColor.a *= AbilityDiscAlphaScale;
            Renderer rend = marker.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Sprites/Default"));
            rend.material.color = discColor;
        }
        else
        {
            // Point-target abilities have no blast radius to draw as an AOE disc (e.g. the Pogo
            // Rider's Jump — it lands on exactly one cell). Outline that single target square
            // instead so the selection reads clearly, matching the other units' visible markers.
            CreateSquareFootprintIndicator(host, square, 0, color);
        }

        return host;
    }

    /// <summary>
    /// Draws the route the ability itself will travel, from points the ability samples off its own
    /// execution maths — the Shotgunner's rush, the Soldier's grenade arc, the Pogo Rider's jump.
    /// Abilities that reach their target without travelling report nothing and draw nothing, so no
    /// new ability needs this method changed to be previewed.
    /// </summary>
    static void ShowAbilityPathPreview(
        GameObject host,
        GameObject unit,
        Vector3 selectedSquare,
        UnitData unitData,
        Color color
    )
    {
        Ability ability = unit.GetComponent<Ability>();
        if (ability == null)
            return;

        AbilityPathKind kind = ability.BuildPlannedPath(
            selectedSquare,
            unitData,
            abilityPathPoints
        );
        if (kind == AbilityPathKind.None)
            return;

        AbilityPathIndicator
            .Create(host.transform, "AbilityPath")
            .Show(abilityPathPoints, kind, color);
    }

    static void CreateSquareFootprintIndicator(
        GameObject host,
        Vector3 square,
        int radius,
        Color previewColor
    )
    {
        Vector2Int center = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(square)
        );
        Material previewMaterial = new(Shader.Find("Sprites/Default"));
        float halfCell = cellSize * 0.46f;
        float lineWidth = Mathf.Max(0.04f, cellSize * 0.04f);

        foreach (Vector2Int cell in GridSystem.GetSquareFootprint(center, radius))
        {
            GameObject cellOutline = new($"AbilityTargetCell_{cell.x}_{cell.y}");
            cellOutline.transform.SetParent(host.transform, true);

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

    /// <summary>Drops one unit's ability preview, leaving the rest of the team's plans drawn.</summary>
    void ClearAbilityIndicator(GameObject unit)
    {
        if (unit == null || !abilityVisuals.TryGetValue(unit, out GameObject indicator))
            return;

        DestroyAbilityIndicator(indicator);
        abilityVisuals.Remove(unit);
    }

    void ClearAbilityIndicators()
    {
        foreach (GameObject indicator in abilityVisuals.Values)
            DestroyAbilityIndicator(indicator);
        abilityVisuals.Clear();
    }

    private void DestroyAbilityIndicator(GameObject indicator)
    {
        if (indicator == null)
            return;

        // Destroy is deferred; hide the private planning preview before the phase can advance.
        indicator.SetActive(false);
        Destroy(indicator);
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

    /// <summary>
    /// Turns the unit being edited to its ability, for the board-side click on a unit that is not
    /// going anywhere. A unit holding a route keeps it, so the click that gives up a route is never
    /// also the one that changes what the unit is doing with its round.
    /// </summary>
    public bool TrySwitchToAbilityPlan(GameObject unit)
    {
        // A dodge response is movement only — the server throws away an ability-flagged plan — so
        // the gesture is offered in the same window the cards are, and nowhere else.
        if (!CanEditPlan || !useUnitCards || unit == null || unit != selectedUnit)
            return false;
        if (!plans.TryGetValue(unit, out (bool, List<Vector3>) plan))
            return false;
        if (plan.Item1 || (plan.Item2?.Count ?? 0) > 1)
            return false;

        return TrySetSelectionMode(unit, true);
    }

    /// <summary>
    /// Resolves a click that landed on one of your own units rather than on the board. Pointing at
    /// a unit asks for that unit: another unit takes over the selection, and the unit already
    /// holding it turns over to its other order. The dock reaches the same two states from the
    /// ability's side, so a player who never discovers this gesture is not locked out of anything.
    /// Returns true when the click was spent on the selection and must not also be read as naming
    /// a square.
    /// </summary>
    public bool TrySelectPlanningUnit(GameObject unit)
    {
        // teamCharacters is the set that may be given orders, which during a dodge window is only
        // the alerted units — the rest of the team is on the board but is not taking any.
        if (!CanEditPlan || unit == null || !teamCharacters.Contains(unit))
            return false;
        if (!IsPlanningUnitAvailable(unit))
            return false;

        if (unit != selectedUnit)
        {
            SwitchToUnit(unit);
            return selectedUnit == unit;
        }

        bool abilityMode = plans.TryGetValue(unit, out (bool, List<Vector3>) plan) && plan.Item1;
        return abilityMode
            ? TrySetSelectionMode(unit, false)
            // Turning the other way has rules of its own — no abilities in a dodge, and a drawn
            // route is given up before it is replaced — and they are kept in one place.
            : TrySwitchToAbilityPlan(unit);
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
        reservationUnits = teamCharacters;
        return true;
    }

    /// <summary>
    /// Which entry of a plan holds the cell its unit is standing on when the round resolves: the
    /// last step of a route, or the first cell for a unit spending the round on an ability, since
    /// a caster does not march anywhere during execution. -1 for a plan with nothing in it.
    /// </summary>
    public static int GetPlannedEndIndex(bool abilityMode, int planLength)
    {
        if (planLength <= 0)
            return -1;

        return abilityMode ? 0 : planLength - 1;
    }

    /// <summary>The cell a plan leaves its unit standing on; the unit's own cell without one.</summary>
    private Vector3 GetPlannedEndCell(GameObject unit)
    {
        if (plans.TryGetValue(unit, out (bool, List<Vector3>) plan))
        {
            int endIndex = GetPlannedEndIndex(plan.Item1, plan.Item2?.Count ?? 0);
            if (endIndex >= 0)
                return plan.Item2[endIndex];
        }
        return GridSystem.GetNearestGridCell(unit);
    }

    /// <summary>
    /// Whether another unit of this team already finishes the round standing on the given cell.
    /// Two units cannot share a square, so this is the cell a route may pass over but not stop on.
    /// </summary>
    public bool IsEndCellHeldByAnotherUnit(Vector3 cell, GameObject excludedUnit)
    {
        Vector2Int target = GridSystem.ConvertToGridCoords(cell);
        foreach (GameObject unit in reservationUnits)
        {
            if (unit == excludedUnit || !IsPlanningUnitAvailable(unit))
                continue;
            if (GridSystem.ConvertToGridCoords(GetPlannedEndCell(unit)) == target)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a unit's route currently stops on a square a team-mate finishes on. Routes are free
    /// to run through those squares, so this end state is reachable mid-drag and is drawn as
    /// unusable until the route is settled.
    /// </summary>
    public bool IsRouteEndBlocked(GameObject unit)
    {
        if (unit == null || !plans.TryGetValue(unit, out (bool, List<Vector3>) plan))
            return false;
        if (plan.Item1 || plan.Item2 == null || plan.Item2.Count < 2)
            return false;

        return IsEndCellHeldByAnotherUnit(plan.Item2[^1], unit);
    }

    /// <summary>
    /// Gives up the trailing cells of a route until it stops somewhere its team leaves free. A
    /// route may be drawn across a team-mate's destination, so this is what holds the player to a
    /// destination they can actually keep, rather than letting the server silently cut orders the
    /// player already believed were given.
    /// </summary>
    public void TrimRouteToLastFreeCell(GameObject unit)
    {
        if (unit == null || !plans.TryGetValue(unit, out (bool, List<Vector3>) plan))
            return;

        List<Vector3> route = plan.Item2;
        if (plan.Item1 || route == null || route.Count < 2)
            return;

        int endIndex = route.Count - 1;
        while (endIndex > 0 && IsEndCellHeldByAnotherUnit(route[endIndex], unit))
            endIndex--;
        if (endIndex == route.Count - 1)
            return;

        route.RemoveRange(endIndex + 1, route.Count - (endIndex + 1));
        if (unit == selectedUnit)
            currentPlan = route;
        RefreshAllRibbons();
    }

    /// <summary>
    /// Settles every unit's route so no two of them finish on the same square, matching the order
    /// the server resolves them in so the board a player commits is the board they get. Shorter
    /// orders are honoured first, which leaves a unit holding its ground on the cell it occupies
    /// and cuts short the one walking into it.
    /// </summary>
    private void TrimAllRoutesToFreeCells()
    {
        HashSet<Vector2Int> claimed = new();
        List<(GameObject unit, int rosterSlot, List<Vector3> route)> movers = new();
        foreach (GameObject unit in reservationUnits)
        {
            if (!IsPlanningUnitAvailable(unit))
                continue;

            bool hasPlan = plans.TryGetValue(unit, out (bool, List<Vector3>) plan);
            if (!hasPlan || plan.Item1 || plan.Item2 == null || plan.Item2.Count < 2)
            {
                claimed.Add(GridSystem.ConvertToGridCoords(GetPlannedEndCell(unit)));
                continue;
            }
            movers.Add((unit, GetRosterSlot(unit), plan.Item2));
        }

        // Must match the order the server resolves these in, or the board the player is shown at
        // commit is not the board the round is run from.
        movers.Sort(
            (left, right) =>
            {
                int lengthComparison = left.route.Count.CompareTo(right.route.Count);
                return lengthComparison != 0
                    ? lengthComparison
                    : left.rosterSlot.CompareTo(right.rosterSlot);
            }
        );
        List<int> lengths = GameLoop.ResolveUniqueEndCellLengths(
            movers
                .Select(mover =>
                    (IReadOnlyList<Vector2Int>)
                        mover.route.Select(GridSystem.ConvertToGridCoords).ToList()
                )
                .ToList(),
            claimed
        );

        bool trimmedAny = false;
        for (int i = 0; i < movers.Count; i++)
        {
            List<Vector3> route = movers[i].route;
            if (lengths[i] >= route.Count)
                continue;

            route.RemoveRange(lengths[i], route.Count - lengths[i]);
            trimmedAny = true;
            if (movers[i].unit == selectedUnit)
                currentPlan = route;
        }

        if (trimmedAny)
            RefreshAllRibbons();
    }

    public void SwitchToUnit(int unitIndex)
    {
        SelectUnit(unitIndex);
    }

    /// <summary>
    /// Presses a unit's ability card. The card is the ability, so one press orders it: the unit is
    /// taken over if it was not the one being edited, and its round is spent on the ability instead
    /// of on movement. Pressing the card of a unit already set to its ability puts that unit back
    /// on movement.
    /// </summary>
    /// <remarks>
    /// This used to take two presses, the first only selecting the unit, because the card was a
    /// roster entry that happened to have an ability on its back. A player who wanted an ability
    /// had to know the card turned over. Selecting a unit to move it is what the board is for —
    /// <see cref="TrySelectPlanningUnit"/> handles the click that lands on one — so the dock is
    /// free to be the abilities and nothing else.
    /// </remarks>
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

        bool abilityMode =
            plans.TryGetValue(unit, out (bool, List<Vector3>) plan) && plan.Item1;
        if (abilityMode)
            return TrySetSelectionMode(unit, false);

        bool hadRoute = (plan.Item2?.Count ?? 0) > 1;

        // Picked up first, and deliberately before the attempt rather than after it. A press on a
        // unit whose ability is still recharging is worth something even though it cannot order
        // anything — that unit is now the one taking a route — and selecting afterwards would wipe
        // the very message explaining why the ability did not fire.
        if (selectedUnit != unit)
            SwitchToUnit(unit);

        if (!TrySetSelectionMode(unit, true))
            return false;

        // The two orders are exclusive, so arming the ability throws the route away. The press
        // named which one the player wants, but a route that vanishes without a word reads as the
        // card having eaten it, so the swap is said out loud.
        if (hadRoute)
        {
            string abilityName =
                unit.GetComponent<Movement>()?.unitData?.abilityName ?? "This ability";
            GameHUDController.Instance?.SetTargetFeedback(
                $"{abilityName} replaces this unit's route.",
                false
            );
        }
        return true;
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
            // Only this unit's target is being abandoned; the rest of the team keeps its plans.
            ClearAbilityIndicator(unit);
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
            Destroy(moveOverlay);
            RefreshAllRibbons();
            // Deselecting dims the team's ability plans rather than erasing them.
            RefreshAbilityIndicators();
            RefreshUnitCards();
            return;
        }
        if (previousUnit != selectedUnit)
            PathSelection.Instance?.CancelCurrentDrag();

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
            // The same overlay prefab draws both faces of the card, so the mode has to pick the
            // tint here — otherwise "where I can walk" and "where I can aim" are the same colour.
            DisplayMoveRange(
                abilityMode
                    ? (unitData.selectAbilitySquare ? unitData.abilitySquareRange : 0)
                    : movementRange,
                abilityMode ? TeamPalette.AbilityRange : TeamPalette.MoveRange,
                !abilityMode || unitData.CanTargetOwnCell
            );

        currentPlan = plans[selectedUnit].Item2;
        RefreshAllRibbons();
        RefreshAbilityIndicators();
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

            pair.Value.SetSelected(pair.Key == HighlightedUnit);
            pair.Value.SetEndBlocked(IsRouteEndBlocked(pair.Key));
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
        abilityVisuals = new Dictionary<GameObject, GameObject>();
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
        ClearAbilityIndicators();
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

    void DisplayMoveRange(int range, Color tint, bool includeOwnCell = true)
    {
        Destroy(moveOverlay);
        if (selectedUnit == null)
            return;

        moveOverlay = GridSystem.DisplayGridRange(
            GridSystem.GetNearestGridCell(selectedUnit),
            range,
            moveOverlayCellPrefab,
            tint,
            includeOwnCell
        );
    }

    void DisplayAbilityDirections()
    {
        Destroy(moveOverlay);
        if (selectedUnit == null)
            return;

        moveOverlay = GridSystem.DisplayGridDirections(
            GridSystem.GetNearestGridCell(selectedUnit),
            moveOverlayCellPrefab,
            TeamPalette.AbilityRange
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
