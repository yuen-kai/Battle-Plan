using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages interactive path planning on a grid for team units.
/// Handles mouse-driven path creation, visual feedback, range displays, and movement validation.
/// Supports both normal movement and dash mechanics with configurable distances.
/// </summary>
public partial class PlanMovement : MonoBehaviour
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

    private static float cellSize => GameLoop.cellSize;

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
}
