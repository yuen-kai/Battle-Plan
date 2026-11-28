using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum ReplayPlanLayer
{
    Mine,
    Enemy,
    Both,
}

/// <summary>
/// Shows a recorded round on the live board, Chess.com style: step to a round and the board takes
/// its opening position, scrub and the round plays out. Nothing live is posed or moved — the real
/// units are only hidden, and stand-in copies stripped of every behaviour are posed in their place —
/// so closing the viewer hands back exactly the board that was there.
/// </summary>
public class ReplayViewer : MonoBehaviour
{
    // Stepping between rounds plays the recorded frames between them over this long, so a round
    // reads as time moving rather than as a cut, whatever its length.
    private const float TimelapseSeconds = 0.3f;

    // The live planning board is the end of the round that led to it, so the slider's far right
    // on the latest round is the live board rather than a frame of the replay.
    private const float LiveEndTolerance = 0.001f;
    private const float ScrubPlanAlpha = 0.35f;
    private const float RoundStartTolerance = 0.05f;
    private const float MovingThreshold = 0.05f;

    // A one-shot effect fires when the shown time passes it going forward. A jump further than
    // this is a seek, and firing everything it skipped would bury the board in explosions.
    private const float MaxCueCatchUpSeconds = 0.75f;
    private const float TrailBreakSeconds = 0.25f;
    private const float DefaultBulletSpeed = 30f;

    // How long a one-shot animation holds before its rig falls back to the held state, matching
    // AnimationHandler.TriggerAnimation's default.
    private const float OneShotAnimationSeconds = 1f;
    private const float CasterFlashSeconds = 0.4f;
    private const float CasterFlashIntensity = 1.5f;
    private const float BeamFadeSeconds = 0.2f;

    // How long the board has to rest on a round's opening frame before it is handed over to orders,
    // so stepping or dragging through a round start does not stand a whole board up on the way past.
    private const float BranchSettleSeconds = 0.3f;
    private static readonly Color BranchTint = new(0.56f, 0.38f, 1f, 0.13f);

    public static bool IsViewing { get; private set; }

    private GameLoop loop;
    private GameHUDController boundHud;
    private ReplayPlanLayer layer = ReplayPlanLayer.Both;
    private bool postMatch;

    private int shownRound = -1;
    private float shownTime;

    private readonly List<(int round, float from, float to)> timelapse = new();
    private float timelapseElapsed = -1f;
    private int timelapseSourceRound;
    private float timelapseSourceAlpha = 1f;
    private int landingRound = -1;
    private bool closeOnLanding;

    private Transform stage;
    private readonly Dictionary<(int team, int slot), StandIn> standIns = new();

    private GameObject planRoot;
    private GameObject minePlans;
    private GameObject enemyPlans;
    private int planRound = -1;
    private readonly List<(LineRenderer line, Color start, Color end)> planLines = new();
    private readonly List<(Material material, Color color)> planFills = new();

    private GameObject smokeVisual;
    private int smokeRound = -1;

    private readonly List<(Renderer renderer, bool wasOff)> hiddenRenderers = new();
    private readonly List<(Canvas canvas, bool wasEnabled)> hiddenCanvases = new();
    private readonly HashSet<Vector2Int> reshownWalls = new();
    private readonly HashSet<Vector2Int> wantedWalls = new();

    private readonly Dictionary<int, GameObject> eventVisuals = new();
    private readonly Dictionary<int, GameObject> trackVisuals = new();
    private readonly Dictionary<int, ParticleSystem[]> trackParticles = new();
    private readonly Dictionary<uint, GameObject> networkPrefabs = new();
    private readonly List<Vector3> strikePoints = new();
    private int effectsRound = -1;
    private int cueRound = -1;
    private float cueTime;
    private int poseRound = -1;
    private float poseTime;
    private int smokeChange = -2;

    private bool inBranchSession;
    private int settleTarget = -1;
    private float settleShownTime;
    private float settleSince;
    private GameObject branchTint;

    private readonly Dictionary<int, int> beamRush = new();
    private readonly HashSet<int> beamsFading = new();
    private readonly List<(Vector2Int cell, int range)> fogViewers = new();

    /// <summary>
    /// A unit's body for the replay, plus the presentation pieces its live components would have
    /// driven: the health bar, the charge dial, status pulses, the boost ring, the guard orb, the
    /// vision cone, the shield slab and the lock laser. Each is the same visual class the live unit
    /// uses, handed the state that was recorded instead of the state on the network.
    /// </summary>
    private sealed class StandIn
    {
        public GameObject body;
        public Animator rig;
        public string stateKey;
        public UnitData data;
        public bool hasAbility;
        public Transform canvas;
        public float canvasHeight;
        public AbilityStatusRing ring;
        public StunPulse stun;
        public DiveRecoveryPulse dive;
        public SpeedBoostIndicatorVisual boost;
        public GuardOrbVisual guard;
        public Transform shield;
        public Vector3 shieldLoweredPosition;
        public Quaternion shieldLoweredRotation;
        public TargetLaserVisual laser;
        public bool deathPlayed;
    }

    public void Bind(GameLoop owner) => loop = owner;

    private void OnDestroy()
    {
        if (IsViewing)
            Close(false);
    }

    private bool MatchOver => loop != null && loop.LastMatchResult.HasValue;

    private static bool RoundPlanningOpen =>
        PlanMovement.Instance != null && PlanMovement.Instance.IsRoundPlanningOpen;

    private ReplayArchive Archive => loop != null ? loop.Replays : null;

    private void Update()
    {
        if (loop == null)
            return;

        BindHud();

        if (IsViewing && !postMatch && (!RoundPlanningOpen || MatchOver))
            Close(false);
        else if (IsViewing && postMatch && !MatchOver)
            Close(false);

        if (IsViewing && timelapseElapsed >= 0f)
            AdvanceTimelapse(Time.unscaledDeltaTime);

        if (IsViewing && postMatch)
            SettleIntoBranch();
        else if (inBranchSession && !IsViewing)
            FollowBranch();

        RefreshBar();
        RefreshBranchTint();
    }

    // === BRANCHES ===

    private bool BranchPlanningHere(out int round)
    {
        round = loop.BranchPlanningRound;
        return inBranchSession && !IsViewing && round >= 0;
    }

    /// <summary>
    /// Resting on a position the board can be ordered from — a round's opening frame, or the end of
    /// the last round with both crews still standing — hands the live board over to orders for that
    /// round, Chess.com style: the next move made from here is a test of the line, not the line.
    /// </summary>
    private void SettleIntoBranch()
    {
        int target = -1;
        if (
            (loop.CanBranch || loop.CanCopyForBranch)
            && timelapseElapsed < 0f
            && Archive.TryGet(shownRound, out ReplayRound round)
        )
        {
            if (shownTime <= RoundStartTolerance)
                target = shownRound;
            else if (
                shownRound == Archive.LatestRoundNumber
                && shownTime >= round.Duration - RoundStartTolerance
                && BothCrewsStanding(round)
            )
                target = shownRound + 1;
        }

        if (target < 0)
        {
            settleTarget = -1;
            return;
        }
        if (settleTarget != target || !Mathf.Approximately(settleShownTime, shownTime))
        {
            settleTarget = target;
            settleShownTime = shownTime;
            settleSince = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime - settleSince < BranchSettleSeconds)
            return;

        settleTarget = -1;
        if (!loop.CanBranch)
        {
            loop.LeaveForReplayCopy(shownRound, shownTime);
            return;
        }
        inBranchSession = true;
        Close(false);
        loop.BeginBranchPlanning(target);
    }

    public bool TryGetPostMatchPosition(out int round, out float time)
    {
        round = IsViewing && postMatch ? shownRound : -1;
        time = round >= 0 ? shownTime : 0f;
        return round >= 0;
    }

    /// <summary>Reopens the full replay on a given moment, which is how a copied match picks up where its room left off.</summary>
    public void OpenPostMatchAt(int roundNumber, float time)
    {
        ReplayArchive archive = Archive;
        if (!MatchOver || archive == null || !archive.TryGet(roundNumber, out ReplayRound round))
            return;

        BindHud();
        boundHud?.HideResultsForReplay();
        Open(true);
        ShowAt(roundNumber, Mathf.Clamp(time, 0f, round.Duration));
    }

    private static bool BothCrewsStanding(ReplayRound round)
    {
        if (round.frames.Count == 0)
            return false;
        bool[] standing = new bool[GameLoop.TeamCount];
        ReplaySample[] last = round.frames[^1];
        for (int i = 0; i < round.units.Count && i < last.Length; i++)
        {
            if (last[i].Alive && round.units[i].teamIndex < standing.Length)
                standing[round.units[i].teamIndex] = true;
        }
        return System.Array.TrueForAll(standing, crew => crew);
    }

    /// <summary>A branch round that ended with a crew wiped out has no next orders; show how it ended.</summary>
    private void FollowBranch()
    {
        if (loop.IsBranchExecuting || loop.BranchPlanningRound >= 0)
            return;
        int last = Archive.LatestRoundNumber;
        if (!Archive.TryGet(last, out ReplayRound round))
            return;
        Open(true);
        ShowAt(last, round.Duration);
    }

    private void LeaveBranchPlanning()
    {
        loop.CancelBranchPlanning();
        Open(true);
    }

    private void ExitToResults()
    {
        if (IsViewing)
        {
            Close(true);
            return;
        }
        loop.EndBranching();
        inBranchSession = false;
        boundHud?.ReturnToResults();
    }

    private void RefreshBranchTint()
    {
        bool tinted =
            IsViewing ? Archive != null && Archive.IsBranchRound(shownRound)
            : inBranchSession
                && (
                    loop.IsBranchExecuting
                    || (
                        loop.BranchPlanningRound >= 0
                        && (
                            Archive.IsBranchRound(loop.BranchPlanningRound)
                            || (Archive.HasBranch && !Archive.TryGet(loop.BranchPlanningRound, out _))
                        )
                    )
                );

        if (tinted && branchTint == null)
            branchTint = BuildBranchTint();
        if (branchTint != null)
            branchTint.SetActive(tinted);
    }

    /// <summary>A violet wash over the deck: the board being shown is a test of the line, not the line.</summary>
    private static GameObject BuildBranchTint()
    {
        GameObject tint = GameObject.CreatePrimitive(PrimitiveType.Quad);
        tint.name = "ReplayBranchTint";
        Object.Destroy(tint.GetComponent<Collider>());
        Rect board = GameLoop.gridBounds;
        float half = GameLoop.cellSize * 0.5f;
        tint.transform.SetPositionAndRotation(
            new Vector3(board.center.x, 0.03f, board.center.y),
            Quaternion.Euler(90f, 0f, 0f)
        );
        tint.transform.localScale = new Vector3(board.width + half * 2f, board.height + half * 2f, 1f);
        Renderer renderer = tint.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Sprites/Default")) { color = BranchTint };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return tint;
    }

    private void LateUpdate()
    {
        if (!IsViewing)
            return;

        if (Archive != null && Archive.TryGet(shownRound, out ReplayRound shown))
            BillboardHealthBars(shown);

        // Fog and smoke concealment both write these flags on their own schedule.
        foreach ((Renderer renderer, bool _) in hiddenRenderers)
        {
            if (renderer != null)
                renderer.forceRenderingOff = true;
        }
    }

    private void BindHud()
    {
        GameHUDController hud = GameHUDController.Instance;
        if (hud == boundHud)
            return;

        boundHud = hud;
        if (hud == null)
            return;
        hud.SetReplayHandlers(Step, Scrub);
        hud.SetReplayLayerHandler(SetLayer);
        hud.SetWatchReplayHandler(OpenPostMatch);
    }

    private void RefreshBar()
    {
        if (boundHud == null)
            return;

        ReplayArchive archive = Archive;
        bool hasRounds = archive != null && archive.Count > 0;
        boundHud.SetWatchReplayAvailable(hasRounds);

        if (!IsViewing && inBranchSession)
        {
            boundHud.SetReplayLayer(layer, false);
            if (loop.IsBranchExecuting || !BranchPlanningHere(out int planning))
            {
                boundHud.SetReplayBar(false, false, false, 1f);
                return;
            }
            if (archive.TryGet(planning, out _))
                boundHud.SetReplayBar(PreviousRound(planning) >= 0, true, true, 0f);
            else
                boundHud.SetReplayBar(true, true, true, 1f);
            return;
        }

        if (!IsViewing)
        {
            bool canReplay = hasRounds && RoundPlanningOpen && !MatchOver;
            boundHud.SetReplayBar(canReplay, false, canReplay, 1f);
            boundHud.SetReplayLayer(layer, false);
            return;
        }

        float duration = archive.TryGet(shownRound, out ReplayRound round) ? round.Duration : 0f;
        bool canStepBack = PreviousRound(shownRound) >= 0 || shownTime > RoundStartTolerance;
        boundHud.SetReplayBar(
            canStepBack,
            true,
            timelapseElapsed < 0f,
            duration > 0f ? shownTime / duration : 0f
        );
        boundHud.SetReplayLayer(layer, true);
    }

    // === CONTROLS ===

    private void Step(int direction)
    {
        ReplayArchive archive = Archive;
        if (archive == null || archive.Count == 0)
            return;

        if (BranchPlanningHere(out int planning))
        {
            if (archive.TryGet(planning, out _))
            {
                LeaveBranchPlanning();
                ShowAt(planning, 0f);
                int previous = PreviousRound(planning);
                if (direction < 0 && previous >= 0)
                    BeginTimelapse(previous, 0f, false);
                else if (direction > 0)
                    BeginTimelapse(NextRound(planning) >= 0 ? NextRound(planning) : planning, 0f, NextRound(planning) < 0);
                return;
            }

            if (direction > 0)
            {
                ExitToResults();
                return;
            }
            int head = archive.LatestRoundNumber;
            if (!archive.TryGet(head, out ReplayRound headRound))
                return;
            LeaveBranchPlanning();
            ShowAt(head, headRound.Duration);
            BeginTimelapse(head, 0f, false);
            return;
        }

        if (!IsViewing)
        {
            if (direction >= 0 || !RoundPlanningOpen || MatchOver)
                return;

            int latest = archive.LatestRoundNumber;
            if (!archive.TryGet(latest, out ReplayRound live))
                return;
            Open(false);
            ShowAt(latest, live.Duration);
            BeginTimelapse(latest, 0f, false);
            return;
        }

        if (direction < 0)
        {
            int previous = PreviousRound(shownRound);
            if (shownTime > RoundStartTolerance || previous < 0)
                BeginTimelapse(shownRound, 0f, false);
            else
                BeginTimelapse(previous, 0f, false);
            return;
        }

        int next = NextRound(shownRound);
        BeginTimelapse(next >= 0 ? next : shownRound, 0f, next < 0);
    }

    /// <summary>
    /// Scrubs the round on the bar. Live planning is the latest round's last frame, so dragging off
    /// the right end opens the replay on that round and dragging back onto it hands the board back.
    /// </summary>
    private void Scrub(float progress)
    {
        ReplayArchive archive = Archive;
        if (archive == null)
            return;

        bool atLiveEnd = progress >= 1f - LiveEndTolerance;
        if (BranchPlanningHere(out int planning))
        {
            if (archive.TryGet(planning, out ReplayRound planned))
            {
                if (progress <= LiveEndTolerance)
                    return;
                LeaveBranchPlanning();
                ShowAt(planning, Mathf.Clamp01(progress) * planned.Duration);
                return;
            }
            if (atLiveEnd || !archive.TryGet(archive.LatestRoundNumber, out ReplayRound last))
                return;
            LeaveBranchPlanning();
            ShowAt(last.roundNumber, Mathf.Clamp01(progress) * last.Duration);
            return;
        }

        if (!IsViewing)
        {
            if (atLiveEnd || !RoundPlanningOpen || MatchOver)
                return;
            int latest = archive.LatestRoundNumber;
            if (!archive.TryGet(latest, out ReplayRound live))
                return;
            Open(false);
            ShowAt(latest, Mathf.Clamp01(progress) * live.Duration);
            return;
        }

        if (!archive.TryGet(shownRound, out ReplayRound round))
            return;
        if (atLiveEnd && !postMatch && shownRound == archive.LatestRoundNumber)
        {
            Close(false);
            return;
        }

        timelapseElapsed = -1f;
        ShowAt(shownRound, Mathf.Clamp01(progress) * round.Duration);
    }

    private void SetLayer(ReplayPlanLayer chosen)
    {
        layer = chosen;
        ApplyLayer();
    }

    private void OpenPostMatch()
    {
        ReplayArchive archive = Archive;
        if (!MatchOver || archive == null || archive.Count == 0)
            return;

        int first = -1;
        foreach (int number in archive.RoundNumbers)
        {
            first = number;
            break;
        }

        loop.NotifyRecapStarted();
        boundHud?.HideResultsForReplay();
        Open(true);
        ShowAt(first, 0f);
    }

    private int PreviousRound(int roundNumber)
    {
        int previous = -1;
        foreach (int number in Archive.RoundNumbers)
        {
            if (number >= roundNumber)
                break;
            previous = number;
        }
        return previous;
    }

    private int NextRound(int roundNumber)
    {
        foreach (int number in Archive.RoundNumbers)
        {
            if (number > roundNumber)
                return number;
        }
        return -1;
    }

    // === OPEN / CLOSE ===

    private void Open(bool afterMatch)
    {
        postMatch = afterMatch;
        if (IsViewing)
            return;

        IsViewing = true;
        stage = new GameObject("ReplayStage").transform;
        PathSelection.Instance?.CancelCurrentDrag();
        PlanMovement.Instance?.SetPlanningVisualsHidden(true);
        loop.SetLiveBoardOverlaysVisible(false);
        HideLiveUnits();
    }

    private void Close(bool returnToResults)
    {
        if (!IsViewing)
            return;

        IsViewing = false;
        timelapseElapsed = -1f;
        timelapse.Clear();
        closeOnLanding = false;

        RestoreLiveUnits();
        boundHud?.ClearReplayCardHealth();
        foreach (Vector2Int cell in reshownWalls)
        {
            if (loop != null && loop.TryGetWallInstance(cell, out GameObject wall))
            {
                wall.SetActive(false);
                loop.RefreshWallCornerPlugsAround(cell);
            }
        }
        reshownWalls.Clear();

        if (loop != null)
            loop.RestoreLiveBoard();
        PlanMovement.Instance?.SetPlanningVisualsHidden(false);

        if (stage != null)
            Destroy(stage.gameObject);
        stage = null;
        standIns.Clear();
        planRoot = null;
        planRound = -1;
        planLines.Clear();
        planFills.Clear();
        smokeVisual = null;
        smokeRound = -1;
        smokeChange = -2;
        eventVisuals.Clear();
        trackVisuals.Clear();
        trackParticles.Clear();
        beamRush.Clear();
        beamsFading.Clear();
        effectsRound = -1;
        cueRound = -1;
        poseRound = -1;
        shownRound = -1;

        if (returnToResults && postMatch)
        {
            if (inBranchSession)
            {
                loop.EndBranching();
                inBranchSession = false;
            }
            boundHud?.ReturnToResults();
        }
        postMatch = false;
    }

    private void HideLiveUnits()
    {
        hiddenRenderers.Clear();
        hiddenCanvases.Clear();
        foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            foreach (Renderer renderer in unit.GetComponentsInChildren<Renderer>(true))
            {
                hiddenRenderers.Add((renderer, renderer.forceRenderingOff));
                renderer.forceRenderingOff = true;
            }
            foreach (Canvas canvas in unit.GetComponentsInChildren<Canvas>(true))
            {
                hiddenCanvases.Add((canvas, canvas.enabled));
                canvas.enabled = false;
            }
        }
    }

    private void RestoreLiveUnits()
    {
        foreach ((Renderer renderer, bool wasOff) in hiddenRenderers)
        {
            if (renderer != null)
                renderer.forceRenderingOff = wasOff;
        }
        foreach ((Canvas canvas, bool wasEnabled) in hiddenCanvases)
        {
            if (canvas != null)
                canvas.enabled = wasEnabled;
        }
        hiddenRenderers.Clear();
        hiddenCanvases.Clear();
    }

    // === TIMELAPSE ===

    /// <summary>
    /// Runs the board from where it is now to the start of <paramref name="targetRound"/> through
    /// the recorded frames in between, so stepping reads as time moving rather than a cut. Asked
    /// again mid-flight, it simply sets off from wherever the board has got to.
    /// </summary>
    private void BeginTimelapse(int targetRound, float targetTime, bool closeAfter)
    {
        timelapse.Clear();
        timelapseSourceRound = shownRound;
        timelapseSourceAlpha = PlanAlphaAt(shownTime);
        landingRound = targetRound;
        closeOnLanding = closeAfter;

        if (closeAfter)
        {
            if (Archive.TryGet(shownRound, out ReplayRound current))
                timelapse.Add((shownRound, shownTime, current.Duration));
        }
        else if (targetRound == shownRound)
        {
            timelapse.Add((shownRound, shownTime, targetTime));
        }
        else if (targetRound < shownRound)
        {
            timelapse.Add((shownRound, shownTime, 0f));
            if (Archive.TryGet(targetRound, out ReplayRound earlier))
                timelapse.Add((targetRound, earlier.Duration, targetTime));
        }
        else if (Archive.TryGet(shownRound, out ReplayRound current))
        {
            timelapse.Add((shownRound, shownTime, current.Duration));
            timelapse.Add((targetRound, 0f, targetTime));
        }

        timelapseElapsed = 0f;
        AdvanceTimelapse(0f);
    }

    private void AdvanceTimelapse(float deltaTime)
    {
        timelapseElapsed += deltaTime;
        float total = 0f;
        foreach ((int _, float from, float to) in timelapse)
            total += Mathf.Abs(to - from);

        float progress = total > 0f ? Mathf.Clamp01(timelapseElapsed / TimelapseSeconds) : 1f;
        float travelled = progress * total;
        int round = landingRound;
        float time = 0f;
        foreach ((int segmentRound, float from, float to) in timelapse)
        {
            float length = Mathf.Abs(to - from);
            round = segmentRound;
            if (travelled <= length)
            {
                time = length > 0f ? Mathf.Lerp(from, to, travelled / length) : to;
                break;
            }
            travelled -= length;
            time = to;
        }

        float fade = progress < 0.5f ? 1f - progress * 2f : progress * 2f - 1f;
        int planFor = progress < 0.5f ? timelapseSourceRound : closeOnLanding ? -1 : landingRound;
        float planAlpha = fade * (progress < 0.5f ? timelapseSourceAlpha : 1f);
        ShowAt(round, time, planFor, planAlpha);

        if (progress < 1f)
            return;

        timelapseElapsed = -1f;
        timelapse.Clear();
        if (closeOnLanding)
        {
            Close(true);
            return;
        }
        ShowAt(landingRound, 0f);
    }

    // === POSING ===

    private static float PlanAlphaAt(float time) => time <= RoundStartTolerance ? 1f : ScrubPlanAlpha;

    private void ShowAt(int roundNumber, float time) =>
        ShowAt(roundNumber, time, roundNumber, PlanAlphaAt(time));

    private void ShowAt(int roundNumber, float time, int planForRound, float planAlpha)
    {
        if (stage == null || !Archive.TryGet(roundNumber, out ReplayRound round))
            return;

        shownRound = roundNumber;
        shownTime = Mathf.Clamp(time, 0f, round.Duration);

        bool continuous = poseRound == roundNumber && Mathf.Abs(shownTime - poseTime) <= TrailBreakSeconds;
        bool advancing =
            poseRound == roundNumber
            && shownTime > poseTime
            && shownTime - poseTime <= MaxCueCatchUpSeconds;
        if (poseRound != roundNumber && round.hasHill)
            loop.ShowReplayHill(round.hill);
        PoseStandIns(round, shownTime);
        ShowCardHealth(round, shownTime, advancing);
        ShowWallsAt(roundNumber, shownTime);
        ShowSmoke(round, shownTime);
        ShowFog(round, shownTime);
        ShowEffects(round, shownTime);
        if (!continuous)
            BreakTrails();
        poseRound = roundNumber;
        poseTime = shownTime;
        ShowPlans(planForRound, planAlpha);
    }

    private void PoseStandIns(ReplayRound round, float time)
    {
        foreach (StandIn standIn in standIns.Values)
        {
            if (standIn.body != null)
                standIn.body.SetActive(false);
        }

        int frame = Mathf.Clamp(Mathf.RoundToInt(time / ReplayRound.FrameInterval), 0, round.frames.Count - 1);
        for (int i = 0; i < round.units.Count; i++)
        {
            ReplayUnit unit = round.units[i];
            if (!round.TrySample(i, time, out Vector3 position, out float yaw, out ReplaySample nearest) || !nearest.Alive)
                continue;

            StandIn standIn = GetStandIn(unit);
            if (standIn == null)
                continue;

            standIn.body.SetActive(true);
            if (standIn.deathPlayed)
            {
                // A replayed death hides the body it fell out of; scrubbing back before it has to
                // stand that body up again.
                foreach (Renderer renderer in standIn.body.GetComponentsInChildren<Renderer>(true))
                    renderer.forceRenderingOff = false;
                standIn.deathPlayed = false;
            }
            standIn.body.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            PoseAnimation(round, i, time, standIn);
            PoseStatus(round, i, frame, time, nearest, standIn);
            Health.ShowHealthBar(
                standIn.body.transform,
                standIn.canvas,
                standIn.canvasHeight,
                nearest.health,
                unit.maxHealth
            );
        }
    }

    /// <summary>
    /// Puts every card on both strips at its unit's recorded health and life, so a hit replayed
    /// knocks the card and a death replayed turns it over, exactly as the live round did. Only
    /// playback moving forward animates; a scrub or a cut simply sets the cards.
    /// </summary>
    private void ShowCardHealth(ReplayRound round, float time, bool advancing)
    {
        if (boundHud == null)
            return;

        for (int i = 0; i < round.units.Count; i++)
        {
            ReplayUnit unit = round.units[i];
            if (
                unit.rosterSlot >= RosterRules.UnitsPerPlayer
                || !round.TrySample(i, time, out _, out _, out ReplaySample sample)
            )
            {
                continue;
            }

            boundHud.SetReplayCardHealth(
                GameLoop.IsTeamFriendlyToLocalPlayer(unit.teamIndex),
                unit.rosterSlot,
                sample.health,
                unit.maxHealth,
                sample.Alive,
                advancing
            );
        }
    }

    /// <summary>
    /// The rig's state at this moment, from the states the server asked for: a one-shot for as long
    /// as it plays, otherwise whatever was last held. A round recorded before states were kept
    /// falls back to reading movement off the samples.
    /// </summary>
    private void PoseAnimation(ReplayRound round, int unitIndex, float time, StandIn standIn)
    {
        string held = null;
        string oneShot = null;
        int oneShotEvent = -1;
        for (int e = 0; e < round.events.Count; e++)
        {
            ReplayEvent replayEvent = round.events[e];
            if (replayEvent.kind != ReplayEventKind.Animation || replayEvent.unitIndex != unitIndex)
                continue;
            if (replayEvent.Time > time)
                break;

            int nameIndex = Mathf.RoundToInt(replayEvent.value);
            if (nameIndex < 0 || nameIndex >= round.animationNames.Count)
                continue;
            string name = round.animationNames[nameIndex];
            if ((replayEvent.detail & ReplayDetail.OneShotAnimation) != 0)
            {
                oneShot = name;
                oneShotEvent = e;
                if (time - replayEvent.Time >= OneShotAnimationSeconds)
                    oneShot = null;
            }
            else
            {
                held = name;
                oneShot = null;
            }
        }

        if (held == null && oneShot == null)
        {
            bool moving =
                round.TrySample(unitIndex, Mathf.Max(0f, time - ReplayRound.FrameInterval), out Vector3 earlier, out _, out _)
                && round.TrySample(unitIndex, time, out Vector3 now, out _, out _)
                && (now - earlier).sqrMagnitude > MovingThreshold * MovingThreshold;
            held = moving ? "Moving" : "Idle";
        }

        string state = oneShot ?? held;
        string key = oneShot != null ? $"{state}#{oneShotEvent}" : state;
        if (standIn.stateKey == key)
            return;
        standIn.stateKey = key;
        AnimationHandler.EnterState(standIn.rig, state);
    }

    private void PoseStatus(
        ReplayRound round,
        int unitIndex,
        int frame,
        float time,
        ReplaySample sample,
        StandIn standIn
    )
    {
        double serverNow = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0d;

        if (sample.Has(ReplayStatus.Stunned))
        {
            (float started, float length) = StatusRun(round, unitIndex, frame, ReplayStatus.Stunned);
            standIn.stun ??= StunPulse.Attach(standIn.body);
            standIn.stun.SetStun(new StunState(serverNow - (time - started), length));
        }
        else
        {
            standIn.stun?.SetStun(default);
        }

        if (sample.Has(ReplayStatus.DiveRecovering))
        {
            (float started, float length) = StatusRun(round, unitIndex, frame, ReplayStatus.DiveRecovering);
            standIn.dive ??= DiveRecoveryPulse.Attach(standIn.body);
            standIn.dive.SetRecovery(new DiveRecoveryState(serverNow - (time - started), length));
        }
        else
        {
            standIn.dive?.SetRecovery(default);
        }

        if (sample.Has(ReplayStatus.SpeedBoost))
            standIn.boost ??= SpeedBoostIndicatorVisual.Create(standIn.body.transform);
        standIn.boost?.SetVisible(sample.Has(ReplayStatus.SpeedBoost));

        if (sample.Has(ReplayStatus.Guarded))
            standIn.guard ??= GuardOrbVisual.Attach(standIn.body);
        standIn.guard?.SetGuarded(sample.Has(ReplayStatus.Guarded));

        int configured = Unit.GetConfiguredAbilityCooldownRounds(standIn.data, standIn.hasAbility);
        if (configured > 0)
        {
            standIn.ring ??= AbilityStatusRing.Create(standIn.body.transform);
            standIn.ring?.SetCharge(sample.cooldown, configured, false);
        }

        ShieldStance.PoseShield(
            standIn.shield,
            standIn.shieldLoweredPosition,
            standIn.shieldLoweredRotation,
            sample.ShieldFacing,
            sample.Has(ReplayStatus.ShieldUp)
        );

        int target = sample.lockTarget - 1;
        if (
            standIn.laser != null
            && sample.Has(ReplayStatus.Locking)
            && round.TrySample(target, time, out Vector3 targetPosition, out _, out ReplaySample targetSample)
            && targetSample.Alive
        )
        {
            (float started, float length) = StatusRun(round, unitIndex, frame, ReplayStatus.Locking);
            standIn.laser.ShowAt(
                standIn.body.transform.position,
                targetPosition,
                length > 0f ? Mathf.Clamp01((time - started) / length) : 1f
            );
        }
        else
        {
            standIn.laser?.Hide();
        }
    }

    /// <summary>When the status lit at <paramref name="frame"/> came on, and how long it stayed on.</summary>
    private static (float started, float length) StatusRun(ReplayRound round, int unitIndex, int frame, ReplayStatus flag)
    {
        int first = frame;
        while (first > 0 && round.frames[first - 1][unitIndex].Has(flag))
            first--;
        int last = frame;
        while (last < round.frames.Count - 1 && round.frames[last + 1][unitIndex].Has(flag))
            last++;
        float started = first * ReplayRound.FrameInterval;
        return (started, Mathf.Max(ReplayRound.FrameInterval, (last + 1) * ReplayRound.FrameInterval - started));
    }

    private void BillboardHealthBars(ReplayRound round)
    {
        for (int i = 0; i < round.units.Count; i++)
        {
            ReplayUnit unit = round.units[i];
            if (
                !standIns.TryGetValue((unit.teamIndex, unit.rosterSlot), out StandIn standIn)
                || standIn.body == null
                || !standIn.body.activeSelf
                || !round.TrySample(i, shownTime, out _, out _, out ReplaySample sample)
            )
            {
                continue;
            }
            Health.ShowHealthBar(standIn.body.transform, standIn.canvas, standIn.canvasHeight, sample.health, unit.maxHealth);
        }
    }

    private StandIn GetStandIn(ReplayUnit unit)
    {
        if (standIns.TryGetValue((unit.teamIndex, unit.rosterSlot), out StandIn existing))
            return existing;

        UnitData data =
            loop.allUnits != null
            && unit.catalogIndex >= 0
            && unit.catalogIndex < loop.allUnits.units.Count
                ? loop.allUnits.units[unit.catalogIndex]
                : null;
        GameObject prefab = data != null ? data.unitModel : null;
        if (prefab == null)
            return null;

        bool friendly = GameLoop.IsTeamFriendlyToLocalPlayer(unit.teamIndex);
        GameObject body = MakeDisplayCopy(prefab, $"ReplayStandIn_{unit.teamIndex}_{unit.rosterSlot}", true);
        Unit.ApplyTeamIndicators(body, prefab.GetComponent<Unit>()?.teamMaterials, friendly);

        StandIn standIn = new()
        {
            body = body,
            rig = body.GetComponentInChildren<Animator>(),
            data = data,
            hasAbility = prefab.GetComponent<Ability>() != null,
            canvas = body.transform.Find("UnitCanvas"),
        };

        if (standIn.canvas != null)
        {
            standIn.canvas.gameObject.SetActive(true);
            standIn.canvasHeight = Health.AuthoredCanvasHeight(body.transform, standIn.canvas);
            foreach (Canvas canvas in standIn.canvas.GetComponentsInChildren<Canvas>(true))
                canvas.enabled = true;
            Transform alert = standIn.canvas.Find("Alert");
            if (alert != null)
                alert.gameObject.SetActive(false);
        }

        Transform cone = body.transform.Find("VisionCone");
        if (cone != null)
        {
            cone.gameObject.SetActive(true);
            Unit.ConfigureVisionCone(cone.gameObject.AddComponent<VisionConeVisual>(), friendly, data);
        }

        standIn.shield = body.transform.Find("Shield");
        if (standIn.shield != null)
        {
            standIn.shieldLoweredPosition = standIn.shield.localPosition;
            standIn.shieldLoweredRotation = standIn.shield.localRotation;
            ShieldRush.TryExpandShieldFootprint(standIn.shield);
            ShieldMesh.Apply(standIn.shield);
            standIn.shield.gameObject.SetActive(false);
        }

        if (prefab.GetComponent<Shooting>() != null)
        {
            if (body.GetComponent<LineRenderer>() == null)
                body.AddComponent<LineRenderer>();
            standIn.laser = body.AddComponent<TargetLaserVisual>();
        }

        standIns[(unit.teamIndex, unit.rosterSlot)] = standIn;
        return standIn;
    }

    /// <summary>
    /// Stands back up every wall that was still standing at this moment: the ones brought down
    /// later in this round, and every one brought down in a later round.
    /// </summary>
    private void ShowWallsAt(int roundNumber, float time)
    {
        wantedWalls.Clear();
        foreach (int number in Archive.RoundNumbers)
        {
            if (number < roundNumber || !Archive.TryGet(number, out ReplayRound round))
                continue;
            foreach (ReplayEvent replayEvent in round.events)
            {
                if (
                    replayEvent.kind == ReplayEventKind.WallDestroyed
                    && (number > roundNumber || replayEvent.Time > time)
                )
                {
                    wantedWalls.Add(GridSystem.ConvertToGridCoords(replayEvent.from));
                }
            }
        }

        reshownWalls.RemoveWhere(cell =>
        {
            if (wantedWalls.Contains(cell))
                return false;
            if (loop.TryGetWallInstance(cell, out GameObject wall))
            {
                wall.SetActive(false);
                loop.RefreshWallCornerPlugsAround(cell);
            }
            return true;
        });

        foreach (Vector2Int cell in wantedWalls)
        {
            if (
                !reshownWalls.Contains(cell)
                && loop.TryGetWallInstance(cell, out GameObject wall)
                && !wall.activeSelf
            )
            {
                wall.SetActive(true);
                reshownWalls.Add(cell);
                loop.RefreshWallCornerPlugsAround(cell);
            }
        }
    }

    /// <summary>
    /// What this screen's crew could see at this moment, worked out the way the live fog works it
    /// out — their vision ranges from where they stood, cut by walls and the smoke then standing —
    /// and drawn on the live fog tiles and the replayed smoke alike.
    /// </summary>
    private void ShowFog(ReplayRound round, float time)
    {
        int localTeam = loop.LocalTeamIndex;
        fogViewers.Clear();
        for (int i = 0; i < round.units.Count; i++)
        {
            ReplayUnit unit = round.units[i];
            if (
                unit.teamIndex != localTeam
                || !round.TrySample(i, time, out Vector3 position, out _, out ReplaySample sample)
                || !sample.Alive
                || !standIns.TryGetValue((unit.teamIndex, unit.rosterSlot), out StandIn standIn)
                || standIn.data == null
            )
            {
                continue;
            }
            fogViewers.Add((GridSystem.ConvertToGridCoords(position), standIn.data.visionRange));
        }

        int change = round.smokeChanges.FindLastIndex(candidate => candidate.Time <= time);
        HashSet<Vector2Int> smoke = new(change >= 0 ? round.smokeChanges[change].cells : round.smokeCells);
        HashSet<Vector2Int> visible = GridSystem.ComputeVisibleCells(fogViewers, smoke);

        if (loop.FogOfWarEnabled && !postMatch)
            loop.ShowReplayFog(visible);
        if (smokeVisual != null)
            smokeVisual.GetComponent<SmokeScreenVisual>()?.SetSeenCells(visible);
    }

    /// <summary>The smoke standing at this moment: the round's opening cloud, then each one thrown.</summary>
    private void ShowSmoke(ReplayRound round, float time)
    {
        int change = round.smokeChanges.FindLastIndex(candidate => candidate.Time <= time);
        if (smokeRound == round.roundNumber && smokeChange == change)
            return;

        smokeRound = round.roundNumber;
        smokeChange = change;
        if (smokeVisual != null)
            Destroy(smokeVisual);
        smokeVisual = null;

        List<Vector2Int> cells = change >= 0 ? round.smokeChanges[change].cells : round.smokeCells;
        if (cells.Count == 0)
            return;

        List<Vector3> positions = new(cells.Count);
        foreach (Vector2Int cell in cells)
            positions.Add(GameLoop.gridCoordToWorld(cell));
        smokeVisual = SmokeScreenVisual.Create(stage, positions, GameLoop.cellSize).gameObject;
    }

    // === EFFECTS ===

    /// <summary>
    /// What flew and what went off. Anything that has a position at every instant — a bullet, a
    /// thrown grenade, a held beam — is posed from the time shown, so a scrub either way draws the
    /// same frame. One-shot effects can only be played, so they fire as the shown time passes them
    /// going forward.
    /// </summary>
    private void ShowEffects(ReplayRound round, float time)
    {
        if (effectsRound != round.roundNumber)
        {
            foreach (GameObject visual in eventVisuals.Values)
            {
                if (visual != null)
                    Destroy(visual);
            }
            foreach (GameObject visual in trackVisuals.Values)
            {
                if (visual != null)
                    Destroy(visual);
            }
            eventVisuals.Clear();
            trackVisuals.Clear();
            trackParticles.Clear();
            effectsRound = round.roundNumber;
        }

        bool advancing =
            cueRound == round.roundNumber
            && time > cueTime
            && time - cueTime <= MaxCueCatchUpSeconds;

        for (int i = 0; i < round.events.Count; i++)
        {
            ReplayEvent replayEvent = round.events[i];
            if (replayEvent.kind == ReplayEventKind.Shot)
                ShowBullet(round, i, replayEvent, time);
            else if (replayEvent.kind == ReplayEventKind.Fx && replayEvent.cue == ReplayFxCue.LaserBeam)
                ShowBeam(round, i, replayEvent, time, advancing);
        }

        for (int i = 0; i < round.tracks.Count; i++)
            ShowTrack(i, round.tracks[i], time);

        if (advancing)
        {
            foreach (ReplayEvent replayEvent in round.events)
            {
                if (replayEvent.Time > cueTime && replayEvent.Time <= time)
                    PlayCue(round, replayEvent);
            }
        }
        cueRound = round.roundNumber;
        cueTime = time;
    }

    private void ShowBullet(ReplayRound round, int index, ReplayEvent shot, float time)
    {
        float speed = shot.value > 0f ? shot.value : DefaultBulletSpeed;
        Vector3 path = shot.to - shot.from;
        float length = path.magnitude;
        float travelled = (time - shot.Time) * speed;
        bool live = length > 1e-3f && travelled >= 0f && travelled <= length;

        if (!eventVisuals.TryGetValue(index, out GameObject bullet))
        {
            if (!live)
                return;
            bullet = shot.HasUnit ? MakeBulletCopy(round.units[shot.unitIndex], shot) : null;
            eventVisuals[index] = bullet;
        }
        if (bullet == null)
            return;

        bullet.SetActive(live);
        if (!live)
            return;
        Vector3 direction = path / length;
        bullet.transform.SetPositionAndRotation(
            shot.from + direction * travelled,
            Quaternion.LookRotation(direction)
        );
    }

    /// <summary>
    /// The held laser, as the live one draws it: pulsing while it is armed, surging down to its
    /// victim when it fires, and fading as it is put away. A scrub lands on the posed beam rather
    /// than replaying the surge.
    /// </summary>
    private void ShowBeam(ReplayRound round, int index, ReplayEvent beam, float time, bool advancing)
    {
        // The live beam hangs off its caster, so it goes when the caster does.
        float end = beam.value > 0f ? beam.Time + beam.value : round.Duration;
        bool live =
            time >= beam.Time
            && time <= end
            && round.TrySample(beam.unitIndex, time, out _, out _, out ReplaySample caster)
            && caster.Alive;

        eventVisuals.TryGetValue(index, out GameObject visual);
        if (!live)
        {
            if (visual == null)
                return;
            if (advancing && visual.activeSelf && time > beam.Time)
            {
                BeamVFX fading = visual.GetComponent<BeamVFX>();
                eventVisuals.Remove(index);
                beamRush.Remove(index);
                fading.StartCoroutine(fading.FadeOut(BeamFadeSeconds));
                return;
            }
            visual.SetActive(false);
            return;
        }

        if (visual == null)
        {
            BeamVFX created = BeamVFX.Create(stage, beam.from, beam.to, AreaLock.LaserGlowColor);
            created.Pulse();
            visual = created.gameObject;
            eventVisuals[index] = visual;
            beamRush.Remove(index);
        }
        visual.SetActive(true);

        BeamVFX beamFx = visual.GetComponent<BeamVFX>();
        int rush = round.events.FindIndex(candidate =>
            candidate.kind == ReplayEventKind.Fx
            && candidate.cue == ReplayFxCue.LaserRush
            && candidate.unitIndex == beam.unitIndex
            && candidate.Time >= beam.Time
            && candidate.Time <= time
        );
        if (rush >= 0)
        {
            if (!beamRush.TryGetValue(index, out int started) || started != rush)
            {
                beamRush[index] = rush;
                ReplayEvent surge = round.events[rush];
                beamFx.SetPositions(surge.from, surge.to);
                if (advancing && time - surge.Time < surge.value)
                    beamFx.StartCoroutine(beamFx.Rush(surge.value));
            }
        }
        else if (beamRush.Remove(index))
        {
            beamFx.SetPositions(beam.from, beam.to);
        }
    }

    private void ShowTrack(int index, ReplayTrack track, float time)
    {
        bool live = track.TrySample(time, out Vector3 position, out Quaternion rotation, out float scale);

        if (!trackVisuals.TryGetValue(index, out GameObject visual))
        {
            if (!live)
                return;
            visual = MakeDisplayCopy(ResolveNetworkPrefab(track.prefabHash), "ReplayTrack");
            trackVisuals[index] = visual;
            if (visual != null)
                trackParticles[index] = visual.GetComponentsInChildren<ParticleSystem>(true);
        }
        if (visual == null)
            return;

        visual.SetActive(live);
        if (!live)
            return;

        visual.transform.SetPositionAndRotation(position, rotation);
        if (scale > 0f)
            visual.transform.localScale = Vector3.one * scale;

        // Particles are simulated to the object's age rather than left to run, which is what lets an
        // explosion be scrubbed back through as well as forward.
        float age = time - track.StartTime;
        foreach (ParticleSystem particles in trackParticles[index])
        {
            if (particles != null)
                particles.Simulate(age, false, true);
        }
    }

    private void PlayCue(ReplayRound round, ReplayEvent cue)
    {
        if (cue.kind == ReplayEventKind.Fx && !cue.HasUnit)
        {
            if (cue.cue == ReplayFxCue.BulletBlast)
                Bullet.PlayImpactExplosion(cue.to, cue.value);
            else if (cue.cue == ReplayFxCue.CameraShake)
                CameraEffects.PunchAtViewCentre(cue.value);
            return;
        }
        if (!cue.HasUnit || cue.unitIndex >= round.units.Count)
            return;

        ReplayUnit caster = round.units[cue.unitIndex];
        Color team = GameLoop.GetTeamColorForViewer(caster.teamIndex);
        standIns.TryGetValue((caster.teamIndex, caster.rosterSlot), out StandIn body);
        bool bodyShown = body?.body != null && body.body.activeInHierarchy;

        if (cue.kind == ReplayEventKind.Damage)
        {
            PlayHit(caster, cue, body);
            return;
        }
        if (cue.kind == ReplayEventKind.Ability)
        {
            GameLoop.PlayAbilityActivationFx(cue.from, team);
            if (bodyShown)
                HitFlash.FlashTarget(body.body, CasterFlashSeconds, CasterFlashIntensity);
            return;
        }
        if (cue.kind == ReplayEventKind.Death)
        {
            DeathCollapse.Spawn(cue.from);
            return;
        }
        if (cue.kind != ReplayEventKind.Fx)
            return;

        switch (cue.cue)
        {
            case ReplayFxCue.GrenadeExplosion:
                Grenade.PlayExplosionFx(cue.to, cue.value);
                break;
            case ReplayFxCue.RocketExplosion:
                BunkerBuster.PlayExplosionFx(cue.to, cue.value);
                break;
            case ReplayFxCue.LaserImpact:
                ImpactShockwave.Spawn(cue.to, AreaLock.LaserGlowColor, 3f);
                break;
            case ReplayFxCue.ArcCharge:
                ArcSurge.PlayChargeFx(ulong.MaxValue - cue.unitIndex, cue.from, cue.to, cue.value);
                break;
            case ReplayFxCue.ArcZap:
                ArcSurge.PlayZapFx(cue.from, cue.to, StrikePointsFor(round, cue), cue.value, true);
                break;
            case ReplayFxCue.ArcCancel:
                ArcSurgeVFX.CancelCharge(ulong.MaxValue - cue.unitIndex);
                break;
            case ReplayFxCue.ShieldBlock:
                if (bodyShown)
                    ShieldBlockPulse.Play(body.body, cue.to);
                break;
            case ReplayFxCue.BulletBlast:
                Bullet.PlayImpactExplosion(cue.to, cue.value);
                break;
            case ReplayFxCue.CameraShake:
                CameraEffects.PunchAtViewCentre(cue.value);
                break;
            case ReplayFxCue.PogoLanding:
                Pogo.PlayLandingFx(cue.to, team);
                break;
            case ReplayFxCue.LeapLaunch:
                ShatterLeap.PlayLaunchFx(
                    cue.to,
                    cue.value,
                    ShatterLeap.AbilityTime,
                    team,
                    bodyShown ? body.body.transform : null
                );
                break;
            case ReplayFxCue.LeapImpact:
                ShatterLeap.PlayImpactFx(cue.to, cue.value, team);
                break;
            case ReplayFxCue.BarrageMuzzle:
                SuppressingFire.PlayBarrageMuzzleFx(cue.from, cue.to - cue.from);
                break;
        }
    }

    /// <summary>Where a surge's arcs landed, recorded strike by strike alongside the zap.</summary>
    private Vector3[] StrikePointsFor(ReplayRound round, ReplayEvent zap)
    {
        strikePoints.Clear();
        foreach (ReplayEvent candidate in round.events)
        {
            if (
                candidate.kind == ReplayEventKind.Fx
                && candidate.cue == ReplayFxCue.ArcStrike
                && candidate.unitIndex == zap.unitIndex
                && Mathf.Abs(candidate.Time - zap.Time) <= 0.02f
            )
            {
                strikePoints.Add(candidate.to);
            }
        }
        return strikePoints.ToArray();
    }

    /// <summary>
    /// A landed hit, as the victim's own health callback draws it live: the body takes the blow
    /// (or falls, if it was the last) and the number rises off it.
    /// </summary>
    private static void PlayHit(ReplayUnit victim, ReplayEvent hit, StandIn standIn)
    {
        bool lethal = (hit.detail & ReplayDetail.LethalHit) != 0;
        bool crit = (hit.detail & ReplayDetail.CritHit) != 0;
        float severity = hit.value / Mathf.Max(1f, victim.maxHealth);
        Vector3 position = standIn?.body != null ? standIn.body.transform.position : hit.from;

        if (standIn?.body != null)
        {
            if (lethal)
            {
                HitReaction.PlayDeath(
                    standIn.body,
                    hit.to,
                    TeamPalette.BrightForViewer(GameLoop.IsTeamFriendlyToLocalPlayer(victim.teamIndex))
                );
                standIn.deathPlayed = true;
            }
            else if (standIn.body.activeInHierarchy)
            {
                HitReaction.Play(standIn.body, hit.to, severity);
            }
        }
        DamagePopup.Spawn(position, hit.value, Health.ToneFor(severity, lethal), crit);
    }

    private void BreakTrails()
    {
        if (stage == null)
            return;
        foreach (TrailRenderer trail in stage.GetComponentsInChildren<TrailRenderer>(true))
            trail.Clear();
    }

    private GameObject ResolveBulletPrefab(ReplayUnit shooter)
    {
        if (
            loop.allUnits == null
            || shooter.catalogIndex < 0
            || shooter.catalogIndex >= loop.allUnits.units.Count
        )
        {
            return null;
        }

        UnitData data = loop.allUnits.units[shooter.catalogIndex];
        return shooter.teamIndex == GameLoop.HostTeamIndex ? data.blueBulletPrefab : data.redBulletPrefab;
    }

    private GameObject ResolveNetworkPrefab(uint prefabHash)
    {
        if (networkPrefabs.TryGetValue(prefabHash, out GameObject cached))
            return cached;

        GameObject found = null;
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.NetworkConfig?.Prefabs != null)
        {
            foreach (NetworkPrefab candidate in network.NetworkConfig.Prefabs.Prefabs)
            {
                NetworkObject networkObject =
                    candidate.Prefab != null ? candidate.Prefab.GetComponent<NetworkObject>() : null;
                if (networkObject != null && networkObject.PrefabIdHash == prefabHash)
                {
                    found = candidate.Prefab;
                    break;
                }
            }
        }
        networkPrefabs[prefabHash] = found;
        return found;
    }

    /// <summary>
    /// A copy of a prefab with every behaviour, collider and network identity stripped out, built
    /// under an inactive parent so not one of its behaviours wakes up first — a copy that ran
    /// Unit.Awake or a Bullet's Start would join the match it is only depicting.
    /// </summary>
    private GameObject MakeDisplayCopy(GameObject prefab, string copyName, bool keepPresentation = false)
    {
        if (prefab == null || stage == null)
            return null;

        GameObject holder = new("ReplayCopyHolder");
        holder.SetActive(false);
        holder.transform.SetParent(stage, false);
        GameObject copy = Instantiate(prefab, holder.transform);
        copy.name = copyName;
        if (keepPresentation)
            StripGameplay(copy);
        else
            CharacterCarousel.StripForDisplay(copy);
        foreach (Rigidbody body in copy.GetComponentsInChildren<Rigidbody>(true))
            DestroyImmediate(body);
        copy.transform.SetParent(stage, true);
        Destroy(holder);
        return copy;
    }

    /// <summary>
    /// Takes the game's own behaviours and the network identity off a unit copy and leaves the rest:
    /// its canvas and UI graphics, which the health bar is drawn with, and its colliders, which the
    /// guard orb, the boost ring and the shield footprint all size themselves from and which a
    /// replayed blast is clipped against.
    /// </summary>
    private static void StripGameplay(GameObject copy)
    {
        System.Reflection.Assembly game = typeof(ReplayViewer).Assembly;
        foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;
            System.Type type = behaviour.GetType();
            if (type.Assembly == game || (type.Namespace?.StartsWith("Unity.Netcode") ?? false))
                DestroyImmediate(behaviour);
        }
    }

    private GameObject MakeBulletCopy(ReplayUnit shooter, ReplayEvent shot)
    {
        GameObject prefab = ResolveBulletPrefab(shooter);
        GameObject copy = MakeDisplayCopy(prefab, "ReplayBullet");
        if (copy == null)
            return null;

        List<Material> materials = prefab.GetComponent<Bullet>()?.teamMaterials;
        Renderer renderer = copy.GetComponentInChildren<Renderer>();
        if (renderer != null && materials != null && materials.Count >= 2)
            renderer.sharedMaterial = GameLoop.IsTeamFriendlyToLocalPlayer(shooter.teamIndex)
                ? materials[0]
                : materials[1];
        if ((shot.detail & ReplayDetail.VolleyShot) != 0)
            TripleVolley.ApplyVolleyVisualDimensions(copy);
        return copy;
    }

    // === PLANS ===

    private void ShowPlans(int roundNumber, float alpha)
    {
        if (planRound != roundNumber)
            BuildPlans(roundNumber);

        if (planRoot == null)
            return;

        planRoot.SetActive(alpha > 0.01f);
        foreach ((LineRenderer line, Color start, Color end) in planLines)
        {
            if (line == null)
                continue;
            line.startColor = start.WithAlpha(start.a * alpha);
            line.endColor = end.WithAlpha(end.a * alpha);
        }
        foreach ((Material material, Color color) in planFills)
        {
            if (material != null)
                material.color = color.WithAlpha(color.a * alpha);
        }
    }

    private void ApplyLayer()
    {
        if (minePlans != null)
            minePlans.SetActive(layer != ReplayPlanLayer.Enemy);
        if (enemyPlans != null)
            enemyPlans.SetActive(layer != ReplayPlanLayer.Mine);
    }

    private void BuildPlans(int roundNumber)
    {
        if (planRoot != null)
            Destroy(planRoot);
        planRoot = null;
        planLines.Clear();
        planFills.Clear();
        planRound = roundNumber;
        if (roundNumber < 0 || stage == null || !Archive.TryGet(roundNumber, out ReplayRound round))
            return;

        planRoot = new GameObject("ReplayPlans");
        planRoot.transform.SetParent(stage, false);
        minePlans = new GameObject("Mine");
        minePlans.transform.SetParent(planRoot.transform, false);
        enemyPlans = new GameObject("Enemy");
        enemyPlans.transform.SetParent(planRoot.transform, false);

        foreach (ReplayPlan plan in round.plans)
        {
            if (plan.kind is ReplayPlanKind.Held or ReplayPlanKind.Eliminated)
                continue;

            ReplayUnit unit = round.units[plan.unitIndex];
            bool friendly = GameLoop.IsTeamFriendlyToLocalPlayer(unit.teamIndex);
            Transform parent = (friendly ? minePlans : enemyPlans).transform;
            Color color = friendly
                ? PlanPathStyle.GetRouteColor(unit.rosterSlot, true)
                : TeamPalette.EnemyBright;

            if (plan.IsAbility)
                BuildAbilityPlan(plan, parent, color);
            else
                BuildRoutePlan(plan, unit, parent, friendly, color);
        }

        foreach (LineRenderer line in planRoot.GetComponentsInChildren<LineRenderer>(true))
            planLines.Add((line, line.startColor, line.endColor));
        foreach (Renderer renderer in planRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is LineRenderer)
                continue;
            Material material = renderer.material;
            planFills.Add((material, material.color));
        }
        ApplyLayer();
    }

    private static void BuildRoutePlan(
        ReplayPlan plan,
        ReplayUnit unit,
        Transform parent,
        bool friendly,
        Color color
    )
    {
        if (plan.route.Count < 2)
            return;

        List<Vector3> cells = new(plan.route.Count);
        foreach (Vector2Int cell in plan.route)
            cells.Add(GameLoop.gridCoordToWorld(cell));

        PathRibbon ribbon = PathRibbon.Create(parent, $"ReplayRoute_{unit.teamIndex}_{unit.rosterSlot}", unit.rosterSlot);
        ribbon.SetSelected(true);
        ribbon.SetRoute(cells, null);
        if (friendly)
            return;

        foreach (LineRenderer line in ribbon.GetComponentsInChildren<LineRenderer>(true))
            line.startColor = line.endColor = color;
    }

    private static void BuildAbilityPlan(ReplayPlan plan, Transform parent, Color color)
    {
        if (plan.abilityPath.Count >= 2)
        {
            AbilityPathIndicator
                .Create(parent, "ReplayAbilityPath")
                .Show(plan.abilityPath, plan.abilityPathKind, color);
        }

        Vector3 square = plan.abilitySquare;
        if (plan.abilityCone)
        {
            if (!plan.originKnown)
                return;
            Vector3 direction = square - plan.abilityOrigin;
            direction.y = 0f;
            if (direction.sqrMagnitude > 1e-6f)
            {
                SuppressingFire.CreateConePreview(
                    parent,
                    plan.abilityOrigin,
                    direction,
                    color.WithAlpha(0.8f),
                    plan.abilityRadiusCells,
                    "ReplayAbilityCone",
                    plan.abilityConeHalfSpread
                );
            }
        }
        else if (plan.abilityLine)
        {
            if (plan.originKnown && plan.abilityPath.Count < 2)
            {
                GameObject laser = new("ReplayAbilityLine");
                laser.transform.SetParent(parent, false);
                LineRenderer line = laser.AddComponent<LineRenderer>();
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = line.endColor = color.WithAlpha(0.8f);
                line.startWidth = line.endWidth = 0.15f;
                line.positionCount = 2;
                line.SetPosition(0, plan.abilityOrigin);
                line.SetPosition(1, square);
            }
            if (plan.abilityRadiusCells > 0f)
                GameLoop.BuildTelegraphBlastDisc(parent, square, plan.abilityRadiusCells, color);
        }
        else if (plan.abilitySmoke)
        {
            Vector2Int centre = GridSystem.ConvertToGridCoords(square);
            foreach (Vector2Int cell in GridSystem.GetSquareFootprint(centre, Smoke.FootprintRadius))
                GameLoop.BuildTelegraphCellOutline(parent, cell, color);
        }
        else if (plan.abilityShield)
        {
            GameLoop.BuildTelegraphCellOutline(parent, GridSystem.ConvertToGridCoords(plan.abilityPicked), color);
        }
        else if (plan.abilityRadiusCells > 0f)
        {
            GameLoop.BuildTelegraphBlastDisc(parent, square, plan.abilityRadiusCells, color);
        }
        else
        {
            GameLoop.BuildTelegraphCellOutline(parent, GridSystem.ConvertToGridCoords(square), color);
        }
    }
}
