using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum MessagePerspective
{
    Neutral,
    Friendly,
    Enemy,
}

public enum MatchOutcome : byte
{
    None,
    Win,
    Draw,
}

public enum MatchResultReason : byte
{
    None,
    Elimination,
    SimultaneousElimination,
    KingOfTheHill,
    DisconnectForfeit,
}

public struct MatchResult : INetworkSerializable, System.IEquatable<MatchResult>
{
    public MatchOutcome Outcome;
    public MatchResultReason Reason;
    public int WinningTeamIndex;

    public bool HasWinner => Outcome == MatchOutcome.Win;
    public bool IsValid =>
        Outcome switch
        {
            MatchOutcome.Win => (
                WinningTeamIndex == GameLoop.HostTeamIndex
                || WinningTeamIndex == GameLoop.OpponentTeamIndex
            )
                && Reason != MatchResultReason.None
                && Reason != MatchResultReason.SimultaneousElimination,
            MatchOutcome.Draw => WinningTeamIndex == GameLoop.NoHillController
                && Reason == MatchResultReason.SimultaneousElimination,
            _ => false,
        };

    private MatchResult(MatchOutcome outcome, MatchResultReason reason, int winningTeamIndex)
    {
        Outcome = outcome;
        Reason = reason;
        WinningTeamIndex = winningTeamIndex;
    }

    public static MatchResult ForWinner(int winningTeamIndex, MatchResultReason reason)
    {
        MatchResult result = new(MatchOutcome.Win, reason, winningTeamIndex);
        if (!result.IsValid)
            throw new System.ArgumentException(
                "Winner and result reason must describe a valid win."
            );
        return result;
    }

    public static MatchResult Draw(MatchResultReason reason)
    {
        MatchResult result = new(MatchOutcome.Draw, reason, GameLoop.NoHillController);
        if (!result.IsValid)
            throw new System.ArgumentException("Draw reason must describe a valid draw.");
        return result;
    }

    public string GetStatusForTeam(int localTeamIndex)
    {
        if (!IsValid)
            return "Match complete.";
        if (Outcome == MatchOutcome.Draw)
        {
            return Reason == MatchResultReason.SimultaneousElimination
                ? "Draw — both crews eliminated."
                : "Draw.";
        }

        string status = WinningTeamIndex == localTeamIndex ? "You win!" : "You lose!";
        return Reason switch
        {
            MatchResultReason.KingOfTheHill =>
                $"{status} Held the hill for {GameLoop.HillControlRoundsToWin} consecutive rounds.",
            MatchResultReason.DisconnectForfeit => $"{status} Opponent disconnected.",
            _ => status,
        };
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        byte serializedOutcome = (byte)Outcome;
        byte serializedReason = (byte)Reason;
        serializer.SerializeValue(ref serializedOutcome);
        serializer.SerializeValue(ref serializedReason);
        serializer.SerializeValue(ref WinningTeamIndex);

        if (serializer.IsReader)
        {
            Outcome = (MatchOutcome)serializedOutcome;
            Reason = (MatchResultReason)serializedReason;
        }
    }

    public bool Equals(MatchResult other)
    {
        return Outcome == other.Outcome
            && Reason == other.Reason
            && WinningTeamIndex == other.WinningTeamIndex;
    }

    public override bool Equals(object obj)
    {
        return obj is MatchResult other && Equals(other);
    }

    public override int GetHashCode()
    {
        return System.HashCode.Combine(Outcome, Reason, WinningTeamIndex);
    }
}

public enum HillControlStatus : byte
{
    Empty,
    Contested,
    Controlled,
}

public struct HillControlState : INetworkSerializable, System.IEquatable<HillControlState>
{
    public HillControlStatus Status;
    public int ControllingTeamIndex;
    public int Streak;

    public static HillControlState Empty => new(HillControlStatus.Empty, -1, 0);

    public HillControlState(HillControlStatus status, int controllingTeamIndex, int streak)
    {
        bool validControl =
            status == HillControlStatus.Controlled
            && (
                controllingTeamIndex == GameLoop.HostTeamIndex
                || controllingTeamIndex == GameLoop.OpponentTeamIndex
            );
        Status = validControl
            ? HillControlStatus.Controlled
            : (
                status == HillControlStatus.Contested
                    ? HillControlStatus.Contested
                    : HillControlStatus.Empty
            );
        ControllingTeamIndex = validControl ? controllingTeamIndex : -1;
        Streak = validControl ? Mathf.Max(1, streak) : 0;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        byte serializedStatus = (byte)Status;
        serializer.SerializeValue(ref serializedStatus);
        serializer.SerializeValue(ref ControllingTeamIndex);
        serializer.SerializeValue(ref Streak);

        if (serializer.IsReader)
            this = new HillControlState(
                (HillControlStatus)serializedStatus,
                ControllingTeamIndex,
                Streak
            );
    }

    public bool Equals(HillControlState other)
    {
        return Status == other.Status
            && ControllingTeamIndex == other.ControllingTeamIndex
            && Streak == other.Streak;
    }

    public override bool Equals(object obj)
    {
        return obj is HillControlState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return System.HashCode.Combine(Status, ControllingTeamIndex, Streak);
    }
}

public class GameLoop : NetworkBehaviour
{
    // === EXPLICIT DEV MODE (no-mouse input, fast-forward, self-run) ===
    // Master switch for server-driven gameplay testing (no mouse, compact spawns, fast-forward).
    // Local MPPM connectivity is configured separately and must never turn this on for a normal
    // interactive match.
    //
    // Defaults to OFF (opt-in). devMode alone selects the compact test spawn layout; normal
    // matches start at opposite ends of the board.
    public static bool devMode = false;

    // Execution fast-forward, applied via Time.timeScale while devMode is on; reset to 1 otherwise.
    public static float devSpeedMultiplier = 6f;

    // Diagnostic: measured length (server seconds) that the most recent dev planning phase waited
    // for the agent's explicit submit.
    public static float lastPlanningSeconds;

    // Server-only dev state: paths queued by DevInput for the current planning phase plus a flag
    // that ends the (otherwise unbounded) dev planning wait once the agent explicitly submits.
    private PathsDict devSubmittedPaths;
    private bool devEndPlanningNow;

    // Dev-mode dodge phase control (mirrors the planning model: wait for an explicit submit).
    private bool devDodgeSubmitted;

    // Current round-loop phase, observable by dev tooling: planning / dodging / executing / idle.
    public static string currentPhase = "idle";

    // === ABILITY / DODGE PHASE (server-only round state) ===
    // Ability plans ride in PathsDict as (true, [startCell, targetSquare]); movement plans are
    // (false, [cells...]). At execution start, enemies within an ability's responseRange get a
    // dodge window to submit short dive paths that REPLACE their planned move (and cancel their
    // own ability plan, if any).
    public Dictionary<int, HashSet<GameObject>> dodgeAlerted; // logical team -> alerted units
    private PathsDict dodgeDivePaths;
    private readonly HashSet<int> dodgeResponsesReceived = new();
    private int maxDiveRangeThisRound;
    private int runningAbilities;

    // A unit that walks into a rifle is shot at the whole way in, because movement is what holds
    // the round's weapons free. A unit an ability sets down arrives all at once, and can do so
    // after the last walker has stopped — so weapons stay free this long past a landing too. Long
    // enough for the unit beside it to turn all the way round and fire back, short enough that the
    // round is not left visibly waiting on it.
    public const float AbilityLandingReturnFireSeconds = 1.5f;

    private float returnFireWindowUntil;

    private bool IsReturnFireWindowOpen => Time.time < returnFireWindowUntil;

    // === UNIT OVERLAP (server-only round state) ===
    // No two units share a cell. Allied orders are pulled apart before execution, but the two
    // commanders plan blind to each other, so the round can still end with an enemy standing where
    // one of your units stopped; whoever walked in is shoved aside once everything has settled.

    // How far a unit may be shoved. Two or three cells still reads as being knocked aside; further
    // than that and the shove would move a unit more than its own orders did.
    public const int MaxDisplacementSteps = 3;

    // Long enough to read as being shoved rather than teleporting, short enough that the round
    // does not visibly stall on it.
    private const float DisplacementSlideSeconds = 0.3f;

    private bool resolvingOverlaps;

    // Server-authored round state. Smoke never enters wallLayout, physics, or pathing.
    private readonly HashSet<Vector2Int> activeSmokeCells = new();
    private bool acceptingSmokeRegistrations;

    // Client-side public smoke mirror plus telegraph visuals spawned by client RPCs.
    private readonly HashSet<Vector2Int> clientSmokeCells = new();
    private readonly List<GameObject> clientTelegraphs = new();
    private GameObject clientSmokeVisualRoot;

    public UnitDatabase allUnits;

    public List<Color> teamColors;

    public Color executingMoves;
    public List<Material> teamMaterials;

    // Logical teams. Participant IDs identify humans or the bot in match state only; the bot
    // sentinel is never passed to NGO ownership, RPC targeting, or connected-client APIs.
    public const int HostTeamIndex = 0;
    public const int OpponentTeamIndex = 1;
    public const int TeamCount = 2;
    public const ulong BotParticipantId = ulong.MaxValue;
    public const int NoHillController = -1;
    public const int HillControlRoundsToWin = 3;
    public const string ThreatenedDodgeGuidance = "DODGE now — drag flashing units to safety";
    public const string CasterDodgeGuidance = "Opponent is dodging your ability";
    public const string NeutralDodgeGuidance = "Waiting for dodge response";

    public static readonly int[] DefaultBotRoster = RosterRules.BuildPreferredRoster(
        2,
        3,
        4,
        0,
        1
    );
    public static readonly int[] DevHostRoster = RosterRules.BuildPreferredRoster(4, 1, 2, 0, 3);
    public static readonly int[] DevOpponentRoster = RosterRules.BuildPreferredRoster(
        2,
        3,
        4,
        0,
        1
    );
    public static readonly int[] DevBotHostRoster = RosterRules.BuildPreferredRoster(3, 4, 2, 0, 1);

    public static List<string> teamNames = new() { "BlueTeam", "RedTeam" };

    // Runtime unit objects are indexed by logical team, not participant/owner ID.
    public static Dictionary<int, GameObject[]> allTeamUnitObjects = new();
    private static readonly Dictionary<int, ulong> teamParticipants = new();
    private static readonly Dictionary<int, int[]> teamRosters = new();

    // Level Layout (col, row) from bottom left corner
    public static float cellSize = 2.7f;
    public static Rect gridBounds = new(
        new Vector2(0, 0),
        new Vector2(GridSystem.ColumnCount - 1, GridSystem.RowCount - 1) * cellSize
            + new Vector2(0.1f, 0.1f)
    );
    public static readonly HashSet<Vector2Int> KingOfTheHillCells = new()
    {
        new Vector2Int(6, 3),
        new Vector2Int(7, 3),
        new Vector2Int(8, 3),
        new Vector2Int(6, 4),
        new Vector2Int(7, 4),
        new Vector2Int(8, 4),
        new Vector2Int(6, 5),
        new Vector2Int(7, 5),
        new Vector2Int(8, 5),
        new Vector2Int(6, 6),
        new Vector2Int(7, 6),
        new Vector2Int(8, 6),
    };

    [SerializeField]
    private GameObject wallPrefab;
    public static HashSet<Vector2Int> wallLayout = new()
    {
        new Vector2Int(4, 1),
        new Vector2Int(10, 1),
        new Vector2Int(7, 2),
        new Vector2Int(2, 3),
        new Vector2Int(5, 3),
        new Vector2Int(9, 3),
        new Vector2Int(12, 3),
        new Vector2Int(0, 4),
        new Vector2Int(14, 4),
        new Vector2Int(0, 5),
        new Vector2Int(14, 5),
        new Vector2Int(2, 6),
        new Vector2Int(5, 6),
        new Vector2Int(9, 6),
        new Vector2Int(12, 6),
        new Vector2Int(7, 7),
        new Vector2Int(4, 8),
        new Vector2Int(10, 8),
    };

    private readonly List<Vector2Int[]> spawns = CreateSpawnLayout(devMode);

    private static List<Vector2Int[]> CreateSpawnLayout(bool useDevLayout)
    {
        List<Vector2Int[]> layout = new(TeamCount);
        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            layout.Add(
                CreateSpawnPositions(
                    useDevLayout,
                    teamIndex,
                    RosterRules.UnitsPerPlayer
                )
            );
        }
        return layout;
    }

    public static Vector2Int[] CreateSpawnPositions(
        bool useDevLayout,
        int teamIndex,
        int unitCount
    )
    {
        if (teamIndex < 0 || teamIndex >= TeamCount)
            throw new System.ArgumentOutOfRangeException(nameof(teamIndex));
        if (unitCount <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(unitCount));

        return useDevLayout
            ? CreateDevSpawnPositions(teamIndex, unitCount)
            : CreateProductionSpawnPositions(teamIndex, unitCount);
    }

    private static Vector2Int[] CreateProductionSpawnPositions(int teamIndex, int unitCount)
    {
        int minimumColumn = unitCount <= GridSystem.ColumnCount - 4 ? 2 : 0;
        int maximumColumn = GridSystem.ColumnCount - 1 - minimumColumn;
        int availableColumns = maximumColumn - minimumColumn + 1;
        if (unitCount > availableColumns)
        {
            throw new System.InvalidOperationException(
                $"Cannot place {unitCount} distinct units on a {GridSystem.ColumnCount}-column deployment row."
            );
        }

        int row = teamIndex == HostTeamIndex ? 0 : GridSystem.RowCount - 1;
        Vector2Int[] positions = new Vector2Int[unitCount];
        for (int index = 0; index < unitCount; index++)
        {
            float progress = unitCount == 1 ? 0.5f : index / (float)(unitCount - 1);
            int column = Mathf.RoundToInt(Mathf.Lerp(minimumColumn, maximumColumn, progress));
            if (teamIndex == OpponentTeamIndex)
                column = GridSystem.ColumnCount - 1 - column;
            positions[index] = new Vector2Int(column, row);
        }
        return positions;
    }

    private static Vector2Int[] CreateDevSpawnPositions(int teamIndex, int unitCount)
    {
        Vector2Int[] testAnchors =
            teamIndex == HostTeamIndex
                ? new[]
                {
                    new Vector2Int(4, 3),
                    new Vector2Int(7, 5),
                    new Vector2Int(10, 3),
                    new Vector2Int(2, 2),
                    new Vector2Int(12, 2),
                }
                : new[]
                {
                    new Vector2Int(8, 6),
                    new Vector2Int(7, 6),
                    new Vector2Int(6, 6),
                    new Vector2Int(12, 7),
                    new Vector2Int(2, 7),
                };

        List<Vector2Int> positions = new(unitCount);
        foreach (Vector2Int anchor in testAnchors)
        {
            if (positions.Count == unitCount)
                return positions.ToArray();
            positions.Add(anchor);
        }
        if (positions.Count == unitCount)
            return positions.ToArray();

        int halfRow = GridSystem.RowCount / 2;
        int preferredRow = teamIndex == HostTeamIndex ? halfRow - 2 : halfRow + 1;
        IEnumerable<int> candidateRows =
            teamIndex == HostTeamIndex
                ? Enumerable.Range(0, halfRow)
                : Enumerable.Range(halfRow, GridSystem.RowCount - halfRow);
        candidateRows = candidateRows
            .OrderBy(row => Mathf.Abs(row - preferredRow))
            .ThenBy(row => row);
        IEnumerable<int> candidateColumns = Enumerable
            .Range(0, GridSystem.ColumnCount)
            .OrderBy(column => Mathf.Abs(column - GridSystem.ColumnCount / 2))
            .ThenBy(column => column);

        foreach (int row in candidateRows)
        {
            foreach (int column in candidateColumns)
            {
                Vector2Int candidate = new(column, row);
                if (
                    positions.Contains(candidate)
                    || wallLayout.Contains(candidate)
                    || KingOfTheHillCells.Contains(candidate)
                )
                {
                    continue;
                }

                positions.Add(candidate);
                if (positions.Count == unitCount)
                    return positions.ToArray();
            }
        }

        throw new System.InvalidOperationException(
            $"The dev deployment area has fewer than {unitCount} valid spawn cells for team {teamIndex}."
        );
    }

    // Store camera positions and rotations as a list of (Vector3 position, Quaternion rotation) tuples
    private List<(Vector3 position, Quaternion rotation)> cameraPositions = new()
    {
        (new Vector3(18.9f, 24.4f, 3.4f), new Quaternion(0.59543306f, 0f, 0f, 0.8034049f)),
        (new Vector3(18.9f, 24.4f, 21.0f), new Quaternion(0f, 0.80340505f, -0.5954329f, 0f)),
    };

    // Game Settings
    const float planningTimePerUnit = 6f;
    const float minimumPlanningTime = 12f;
    const float planningUndoGraceSeconds = 3f;

    // Actions
    public static System.Action<bool> OrderAllowShooting;
    public static System.Action<bool> OrderStillShooting;
    public static System.Action OrderContinueShooting;

    private readonly Dictionary<int, PathsDict> submittedTeamPaths = new();
    private readonly Dictionary<int, int> latestTeamPlanVersions = new();
    private readonly Dictionary<int, PathsDict> retractedTeamPathFallbacks = new();
    private readonly Dictionary<
        GameObject,
        (Vector3 position, Quaternion rotation)
    > unitSpawnTransforms = new();
    private BotPlayer botPlayer;
    private int roundNumber;
    private bool planningChangesOpen;
    private double planningDeadline;

    // === KING OF THE HILL ===
    // Atomic, server-authored objective state on the always-visible GameLoop object. Keeping this
    // off unit NetworkObjects ensures fog NetworkHide never drops a control update.
    private readonly NetworkVariable<HillControlState> replicatedHillControl = new(
        HillControlState.Empty
    );
    private readonly List<Renderer> hillOverlayRenderers = new();
    private GameObject hillOverlayRoot;
    private Material hillOverlayMaterial;
    private MaterialPropertyBlock hillOverlayProperties;
    private static readonly int HillGlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int HillRingWidthId = Shader.PropertyToID("_RingWidth");
    private static readonly int HillEdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int HillIntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int HillPulseSpeedId = Shader.PropertyToID("_PulseSpeed");
    private static readonly int HillPulseAmountId = Shader.PropertyToID("_PulseAmount");
#if UNITY_EDITOR
    private readonly Dictionary<ulong, string> devHillPresentationReports = new();
#endif

    public GameObject teamCameraParent;
    public Camera TeamCamera;

    private readonly HashSet<ulong> playAgain = new();
    private bool matchEnded;
    private bool disconnectRecoveryStarted;

    // === FOG OF WAR ===
    [Header("Fog of War")]
    [SerializeField]
    private GameObject fogOverlayCellPrefab;

    private const float FogUpdateIntervalSeconds = 0.15f;
    private static int FogEdgeMaskId => Shader.PropertyToID("_EdgeMask");

    // Match-wide and server-authored so enemy NetworkHide state and every client's overlay
    // always switch together. This lives on the always-visible GameLoop scene object.
    private readonly NetworkVariable<MatchOptions> replicatedMatchOptions = new(
        MatchOptions.Default
    );
    private readonly NetworkVariable<ulong> teamZeroParticipant = new(BotParticipantId);
    private readonly NetworkVariable<ulong> teamOneParticipant = new(BotParticipantId);
    private readonly NetworkVariable<bool> fogOfWarEnabled = new(true);

    // Server: temporary visibility overrides by client (NGO) and team (targeting/bots).
    private readonly Dictionary<(GameObject unit, ulong clientId), double> forceRevealUntil = new();
    private readonly Dictionary<(GameObject unit, int teamIndex), double> forceRevealToTeamUntil =
        new();
    private readonly Dictionary<GameObject, Vector2Int> lastFogCells = new();
    private bool serverFogDirty = true;
    private Coroutine serverFogCoroutine;

    // Client: pooled 150-tile dark overlay from local-owned units plus the public smoke mirror.
    private readonly Dictionary<Vector2Int, Renderer> fogOverlayTiles = new();
    private MaterialPropertyBlock fogOverlayProperties;
    private GameObject fogOverlayRoot;
    private Coroutine clientFogCoroutine;

    // Last set the overlay above was drawn from. Presentation-only consumers (audio) read this so
    // a cue is heard exactly when the tile it happens on is lit, and never a tick out of step.
    private readonly HashSet<Vector2Int> clientVisibleCells = new();
    private bool clientVisibleCellsComputed;

    public bool FogOfWarEnabled => fogOfWarEnabled.Value;
    public MatchOptions Options => replicatedMatchOptions.Value.Sanitized();
    public int LocalTeamIndex =>
        GetTeamIndexForClient(
            NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClientId
                : BotParticipantId
        );
    public int BotPlanningContributionCount => botPlayer?.PlanningContributionCount ?? 0;
    public int BotDodgeContributionCount => botPlayer?.DodgeContributionCount ?? 0;
    public int BotAbilityContributionCount => botPlayer?.AbilityContributionCount ?? 0;
    public bool BotLastPlanUsedAbility => botPlayer?.LastPlanUsedAbility ?? false;
    public int RoundNumber => roundNumber;

    /// <summary>Deterministically ordered snapshot; callers cannot mutate the authoritative set.</summary>
    public IReadOnlyList<Vector2Int> ActiveSmokeCells =>
        activeSmokeCells.OrderBy(cell => cell.y).ThenBy(cell => cell.x).ToArray();
    public HillControlState HillControl => replicatedHillControl.Value;
    public MatchResult? LastMatchResult { get; private set; }
#if UNITY_EDITOR
    public int DevHillPresentationReportCount => devHillPresentationReports.Count;
    public string DevHillPresentationReport =>
        string.Join(
            " | ",
            devHillPresentationReports
                .OrderBy(entry => entry.Key)
                .Select(entry => $"client={entry.Key} {entry.Value}")
        );
#endif

    public static GameLoop Instance { get; private set; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Instance = this;
        TeamCamera = teamCameraParent != null ? teamCameraParent.GetComponent<Camera>() : null;

        replicatedMatchOptions.OnValueChanged += OnMatchOptionsChanged;
        teamZeroParticipant.OnValueChanged += OnTeamParticipantChanged;
        teamOneParticipant.OnValueChanged += OnTeamParticipantChanged;
        fogOfWarEnabled.OnValueChanged += OnFogOfWarEnabledChanged;
        replicatedHillControl.OnValueChanged += OnHillControlChanged;
        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        matchEnded = false;
        LastMatchResult = null;
        disconnectRecoveryStarted = false;
        activeSmokeCells.Clear();
        ClearSmokeScreenVisualsLocal();

        if (IsServer)
        {
            EnsureTeamConfiguration();
            replicatedMatchOptions.Value = MatchOptions.Current.Sanitized();
            MatchOptions.SetCurrent(replicatedMatchOptions.Value);
            teamZeroParticipant.Value = GetConfiguredParticipantId(HostTeamIndex);
            teamOneParticipant.Value = GetConfiguredParticipantId(OpponentTeamIndex);
            fogOfWarEnabled.Value = replicatedMatchOptions.Value.fogOfWar;
            replicatedHillControl.Value = HillControlState.Empty;
            botPlayer = replicatedMatchOptions.Value.IsBotMatch
                ? new BotPlayer(this, OpponentTeamIndex)
                : null;
        }
        else
        {
            MatchOptions.SetCurrent(replicatedMatchOptions.Value);
        }

        if (
            IsServer
            && GetHumanClientIds()
                .Any(clientId => !NetworkManager.ConnectedClients.ContainsKey(clientId))
        )
        {
            disconnectRecoveryStarted = true;
            currentPhase = "idle";
            StartCoroutine(ReturnHostToJoinGameAfterShutdown());
            return;
        }

        GameHUDController.Instance?.SetMatchSummary(replicatedMatchOptions.Value);
        RefreshKingOfTheHillPresentation(replicatedHillControl.Value);
        Unit.RefreshAllTeamPresentation();

        if (IsClient && FogOfWarEnabled)
            StartClientFog();

        if (IsServer)
        {
            StartGame();
            StartCoroutine(StartGameLoopAfterFogSetup());
            InitializeCameraPosition();
        }
    }

    private IEnumerator StartGameLoopAfterFogSetup()
    {
        // Allow unit spawns and one-time setup RPCs to be queued before the first hide pass.
        yield return null;
        if (FogOfWarEnabled)
            StartServerFog();
        yield return StartCoroutine(GameLoopTemp());
    }

    public override void OnNetworkDespawn()
    {
        replicatedMatchOptions.OnValueChanged -= OnMatchOptionsChanged;
        teamZeroParticipant.OnValueChanged -= OnTeamParticipantChanged;
        teamOneParticipant.OnValueChanged -= OnTeamParticipantChanged;
        fogOfWarEnabled.OnValueChanged -= OnFogOfWarEnabledChanged;
        replicatedHillControl.OnValueChanged -= OnHillControlChanged;
        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        StopServerFog(false);
        forceRevealUntil.Clear();
        forceRevealToTeamUntil.Clear();
        StopClientFog();
        ClearKingOfTheHillOverlay();
        unitSpawnTransforms.Clear();
        ClearActiveSmokeCells(notifyClients: false);

        if (Instance == this)
            Instance = null;

        base.OnNetworkDespawn();
    }

    private void OnMatchOptionsChanged(MatchOptions previousValue, MatchOptions newValue)
    {
        MatchOptions.SetCurrent(newValue);
        GameHUDController.Instance?.SetMatchSummary(newValue);
        RefreshKingOfTheHillPresentation(replicatedHillControl.Value);
    }

    private void OnHillControlChanged(HillControlState previousValue, HillControlState newValue)
    {
        RefreshKingOfTheHillPresentation(newValue);
    }

    private void OnTeamParticipantChanged(ulong previousValue, ulong newValue)
    {
        Unit.RefreshAllTeamPresentation();
    }

    public static void ResetMatchState()
    {
        Instance?.ClearActiveSmokeCells();
        allTeamUnitObjects.Clear();
        teamParticipants.Clear();
        teamRosters.Clear();
    }

    public static void ConfigureTeam(int teamIndex, ulong participantId, int[] roster)
    {
        if (teamIndex < 0 || teamIndex >= TeamCount)
            throw new System.ArgumentOutOfRangeException(nameof(teamIndex));
        if (participantId == BotParticipantId && teamIndex != OpponentTeamIndex)
            throw new System.ArgumentException("The bot may only occupy the opponent team.");
        if (roster == null || roster.Length != RosterRules.UnitsPerPlayer)
            throw new System.ArgumentException(
                $"A team roster must contain exactly {RosterRules.UnitsPerPlayer} units."
            );
        if (roster.Any(unitIndex => unitIndex < 0))
            throw new System.ArgumentException("A team roster contains an invalid unit index.");
        if (teamParticipants.Any(entry => entry.Key != teamIndex && entry.Value == participantId))
        {
            throw new System.ArgumentException(
                "A participant cannot be assigned to more than one logical team."
            );
        }

        teamParticipants[teamIndex] = participantId;
        teamRosters[teamIndex] = (int[])roster.Clone();
    }

    private void EnsureTeamConfiguration()
    {
        RosterValidationResult catalogValidation = RosterRules.ValidateCatalog(allUnits?.units);
        if (!catalogValidation.IsValid)
        {
            throw new System.InvalidOperationException(
                $"Unit catalog cannot support {RosterRules.UnitsPerPlayer} units per player "
                    + $"({catalogValidation.Reason})."
            );
        }
        if (teamParticipants.Count == TeamCount && teamRosters.Count == TeamCount)
            return;

        throw new System.InvalidOperationException(
            "Both logical teams must be configured before the Game scene loads."
        );
    }

    private static ulong GetConfiguredParticipantId(int teamIndex)
    {
        return teamParticipants.TryGetValue(teamIndex, out ulong participantId)
            ? participantId
            : BotParticipantId;
    }

    public static bool TryGetConfiguredParticipantId(int teamIndex, out ulong participantId)
    {
        return teamParticipants.TryGetValue(teamIndex, out participantId);
    }

    private static int[] GetConfiguredRoster(int teamIndex)
    {
        return teamRosters.TryGetValue(teamIndex, out int[] roster) ? roster : null;
    }

    public bool TryGetParticipantId(int teamIndex, out ulong participantId)
    {
        if (teamIndex < 0 || teamIndex >= TeamCount)
        {
            participantId = BotParticipantId;
            return false;
        }

        if (IsSpawned)
        {
            participantId =
                teamIndex == HostTeamIndex ? teamZeroParticipant.Value : teamOneParticipant.Value;
            return true;
        }

        return teamParticipants.TryGetValue(teamIndex, out participantId);
    }

    public bool TryGetHumanClientId(int teamIndex, out ulong clientId)
    {
        if (!TryGetParticipantId(teamIndex, out clientId) || IsBotParticipant(clientId))
        {
            clientId = default;
            return false;
        }
        return true;
    }

    public int GetTeamIndexForClient(ulong clientId)
    {
        if (IsBotParticipant(clientId))
            return -1;

        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            if (
                TryGetParticipantId(teamIndex, out ulong participantId)
                && participantId == clientId
            )
            {
                return teamIndex;
            }
        }
        return -1;
    }

    public bool IsBotTeam(int teamIndex)
    {
        return TryGetParticipantId(teamIndex, out ulong participantId)
            && IsBotParticipant(participantId);
    }

    public static bool IsBotParticipant(ulong participantId)
    {
        return participantId == BotParticipantId;
    }

    public static GameObject[] GetTeamUnits(int teamIndex)
    {
        return allTeamUnitObjects.TryGetValue(teamIndex, out GameObject[] units)
            ? units
            : System.Array.Empty<GameObject>();
    }

    public IEnumerable<ulong> GetHumanClientIds()
    {
        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            if (TryGetHumanClientId(teamIndex, out ulong clientId))
                yield return clientId;
        }
    }

    public int HumanTeamCount => GetHumanClientIds().Distinct().Count();

    private void InitializeCameraPosition()
    {
        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            if (
                !TryGetHumanClientId(teamIndex, out ulong clientId)
                || NetworkManager == null
                || !NetworkManager.ConnectedClients.ContainsKey(clientId)
            )
            {
                continue;
            }

            InitializeCameraPositionClientRpc(teamIndex, NetworkHelper.ToClient(clientId));
        }
    }

    [ClientRpc]
    private void InitializeCameraPositionClientRpc(
        int teamIndex,
        ClientRpcParams clientRpcParams = default
    )
    {
        if (teamIndex >= 0 && teamIndex < spawns.Count && teamIndex < cameraPositions.Count)
        {
            if (teamCameraParent != null)
            {
                teamCameraParent.transform.position = cameraPositions[teamIndex].position;
                teamCameraParent.transform.rotation = cameraPositions[teamIndex].rotation;
            }
        }
    }

    void StartGame()
    {
        unitSpawnTransforms.Clear();
        foreach (var pos in wallLayout)
        {
            Vector3 worldPos = gridCoordToWorld(pos);
            GameObject wall = NetworkHelper.Spawn(wallPrefab, worldPos, Quaternion.identity);
            Vector3 heightOffset = Helper.heightOffset(wall.transform);
            wall.transform.position += heightOffset;

            // Sync the height-adjusted position to all clients
            NetworkHelper.SyncHeightAdjustedPositionStatic(wall, wall.transform.position);
        }

        //Destroy all units
        foreach (string team in teamNames)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                Destroy(unit);
            }
        }

        // Setup teams by explicit logical index.
        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            ulong participantId = GetConfiguredParticipantId(teamIndex);
            int[] roster = GetConfiguredRoster(teamIndex);
            SetupUnitsAndCards(
                teamIndex,
                participantId,
                roster,
                spawns[teamIndex],
                Quaternion.Euler(0, 180 * teamIndex, 0),
                teamNames[teamIndex]
            );
        }
        RefreshAllEnemyUnitCards();
    }

    void SetupUnitsAndCards(
        int teamIndex,
        ulong participantId,
        int[] teamUnits,
        IReadOnlyList<Vector2Int> spawnPositions,
        Quaternion rotation,
        string team
    )
    {
        RosterValidationResult rosterValidation = RosterRules.Validate(teamUnits, allUnits?.units);
        if (!rosterValidation.IsValid)
        {
            throw new System.InvalidOperationException(
                $"Team {teamIndex} has an invalid roster ({rosterValidation.Reason})."
            );
        }
        if (spawnPositions == null || spawnPositions.Count != RosterRules.UnitsPerPlayer)
        {
            throw new System.InvalidOperationException(
                $"Team {teamIndex} requires exactly {RosterRules.UnitsPerPlayer} spawn positions."
            );
        }

        allTeamUnitObjects[teamIndex] = new GameObject[teamUnits.Length];
        for (int i = 0; i < teamUnits.Length; i++)
        {
            NetworkObject.VisibilityDelegate visibility = IsAuthorizedGameplayObserver;
            GameObject unit = IsBotParticipant(participantId)
                ? NetworkHelper.Spawn(
                    allUnits.units[teamUnits[i]].unitModel,
                    gridCoordToWorld(spawnPositions[i]),
                    rotation,
                    visibility: visibility
                )
                : NetworkHelper.Spawn(
                    allUnits.units[teamUnits[i]].unitModel,
                    gridCoordToWorld(spawnPositions[i]),
                    rotation,
                    ownerClientId: participantId,
                    visibility: visibility
                );
            allTeamUnitObjects[teamIndex][i] = unit;

            Unit unitIdentity = unit.GetComponent<Unit>();
            if (unitIdentity == null)
                throw new System.InvalidOperationException($"{unit.name} is missing Unit.");
            unitIdentity.SetTeamIndex(teamIndex);
            unitIdentity.SetRosterSlot(i);

            Vector3 heightOffset = Helper.heightOffset(unit.transform);
            unit.transform.position += heightOffset;
            NetworkHelper.SyncHeightAdjustedPositionStatic(unit, unit.transform.position);
            unitSpawnTransforms[unit] = (unit.transform.position, unit.transform.rotation);

            unit.tag = team;
            SetGroupLayerGlobal(unit, LayerMask.NameToLayer(team));

            if (
                !IsBotParticipant(participantId)
                && NetworkManager != null
                && NetworkManager.ConnectedClients.ContainsKey(participantId)
            )
            {
                SetUnitCardClientRpc(
                    i,
                    teamUnits[i],
                    unitIdentity.AbilityCooldownRoundsRemaining,
                    NetworkHelper.ToClient(participantId)
                );
            }
        }
    }

    [ClientRpc]
    void SetUnitCardClientRpc(
        int cardIndex,
        int unitIndex,
        int cooldownRoundsRemaining,
        ClientRpcParams clientRpcParams = default
    )
    {
        UnitData unitData = allUnits.units[unitIndex];
        bool hasAbility =
            unitData.unitModel != null && unitData.unitModel.GetComponent<Ability>() != null;

        GameHUDController.Instance?.ConfigureCard(
            cardIndex,
            unitData,
            hasAbility,
            cooldownRoundsRemaining
        );
    }

    private void RefreshAllEnemyUnitCards()
    {
        if (!IsServer || NetworkManager == null)
            return;

        for (int viewerTeamIndex = 0; viewerTeamIndex < TeamCount; viewerTeamIndex++)
        {
            if (
                !TryGetHumanClientId(viewerTeamIndex, out ulong viewerClientId)
                || !NetworkManager.ConnectedClients.ContainsKey(viewerClientId)
            )
            {
                continue;
            }

            int enemyTeamIndex = GetEnemyTeamIndex(viewerTeamIndex);
            foreach (GameObject enemyUnit in GetTeamUnits(enemyTeamIndex))
                SendEnemyUnitCardStatus(enemyUnit, viewerClientId);
        }
    }

    public void NotifyEnemyUnitStatusChanged(GameObject unit)
    {
        if (
            !IsServer
            || unit == null
            || NetworkManager == null
            || unit.GetComponent<Unit>() is not Unit identity
        )
        {
            return;
        }

        for (int viewerTeamIndex = 0; viewerTeamIndex < TeamCount; viewerTeamIndex++)
        {
            if (
                viewerTeamIndex == identity.TeamIndex
                || !TryGetHumanClientId(viewerTeamIndex, out ulong viewerClientId)
                || !NetworkManager.ConnectedClients.ContainsKey(viewerClientId)
            )
            {
                continue;
            }

            SendEnemyUnitCardStatus(unit, viewerClientId);
        }
    }

    private void SendEnemyUnitCardStatus(GameObject unit, ulong viewerClientId)
    {
        Unit identity = unit != null ? unit.GetComponent<Unit>() : null;
        Health health = unit != null ? unit.GetComponent<Health>() : null;
        if (
            identity == null
            || health == null
            || identity.RosterSlot < 0
            || identity.RosterSlot >= RosterRules.UnitsPerPlayer
            || !teamRosters.TryGetValue(identity.TeamIndex, out int[] roster)
            || roster == null
            || identity.RosterSlot >= roster.Length
        )
        {
            return;
        }

        int unitIndex = roster[identity.RosterSlot];
        if (allUnits?.units == null || unitIndex < 0 || unitIndex >= allUnits.units.Count)
            return;

        SetEnemyUnitCardStatusClientRpc(
            identity.RosterSlot,
            unitIndex,
            health.CurrentHealth,
            health.MaxHealth,
            health.IsAlive,
            unit.GetComponent<Ability>() != null,
            identity.AbilityCooldownRoundsRemaining,
            NetworkHelper.ToClient(viewerClientId)
        );
    }

    [ClientRpc]
    private void SetEnemyUnitCardStatusClientRpc(
        int cardIndex,
        int unitIndex,
        float currentHealth,
        float maxHealth,
        bool alive,
        bool hasAbility,
        int cooldownRoundsRemaining,
        ClientRpcParams clientRpcParams = default
    )
    {
        if (allUnits?.units == null || unitIndex < 0 || unitIndex >= allUnits.units.Count)
            return;

        GameHUDController.Instance?.SetEnemyCardStatus(
            cardIndex,
            allUnits.units[unitIndex],
            hasAbility,
            cooldownRoundsRemaining,
            currentHealth,
            maxHealth,
            alive
        );
    }

    [ClientRpc]
    private void SetCardsInteractableClientRpc(bool interactable)
    {
        GameHUDController.Instance?.SetCardsInteractable(interactable);
    }

    public void DisableUnitCard(GameObject unit)
    {
        SetUnitCardDisabled(unit, true);
    }

    private void SetUnitCardDisabled(GameObject unit, bool disabled)
    {
        if (!IsServer || unit == null || NetworkManager == null)
            return;

        foreach (var teamEntry in allTeamUnitObjects)
        {
            int unitIndexInTeam = System.Array.IndexOf(teamEntry.Value, unit);
            if (
                unitIndexInTeam < 0
                || !TryGetHumanClientId(teamEntry.Key, out ulong clientId)
                || !NetworkManager.ConnectedClients.ContainsKey(clientId)
            )
            {
                continue;
            }

            SetCardDisabledClientRpc(unitIndexInTeam, disabled, NetworkHelper.ToClient(clientId));
            return;
        }
    }

    [ClientRpc]
    private void SetCardDisabledClientRpc(
        int unitIndex,
        bool disabled,
        ClientRpcParams clientRpcParams = default
    )
    {
        GameHUDController.Instance?.SetCardDisabled(unitIndex, disabled);
    }

    public static float GetPlanningDurationSeconds(int largestLivingTeamSize)
    {
        return Mathf.Max(
            minimumPlanningTime,
            planningTimePerUnit * Mathf.Max(0, largestLivingTeamSize)
        );
    }

    IEnumerator GameLoopTemp()
    {
        if (!IsServer)
            yield break;
        yield return null;

        while (!matchEnded && Enumerable.Range(0, TeamCount).All(HasLivingTeamUnits))
        {
            ClearActiveSmokeCells();
            roundNumber++;
            submittedTeamPaths.Clear();
            latestTeamPlanVersions.Clear();
            retractedTeamPathFallbacks.Clear();
            planningChangesOpen = false;
            planningDeadline = 0d;
            devEndPlanningNow = false;
            resolvingOverlaps = false;
            returnFireWindowUntil = 0f;
            currentPhase = "planning";

            // Fast-forward the whole simulation (movement/shooting/physics) in dev mode.
            Time.timeScale = devMode ? Mathf.Max(0.01f, devSpeedMultiplier) : 1f;

            SetCardsInteractableClientRpc(true);
            OrderStillShooting?.Invoke(true);

            int largestLivingTeamSize = Enumerable
                .Range(0, TeamCount)
                .Max(teamIndex => GetTeamUnits(teamIndex).Count(IsLivingUnit));
            float timerLength = GetPlanningDurationSeconds(largestLivingTeamSize);
            double startTime = NetworkManager.Singleton.ServerTime.Time;
            double endTime = startTime + timerLength;
            planningDeadline = endTime;

            CameraEffects.Instance?.FlashClientRpc(MessagePerspective.Friendly);
            setOverlayUITextClientRpc("Planning", MessagePerspective.Friendly);

            if (botPlayer != null)
            {
                PathsDict botPlans = botPlayer.CreatePlanningContribution(roundNumber);
                submittedTeamPaths[botPlayer.TeamIndex] = SanitizePaths(
                    botPlans,
                    botPlayer.TeamIndex
                );
                latestTeamPlanVersions[botPlayer.TeamIndex] = 0;
            }

            if (devMode)
            {
                // Agent-driven planning: DevInput submits plans server-side, so the client mouse
                // planning coroutine is skipped. The agent drives input via execute_code, which
                // takes real seconds, so planning must NOT expire on a wall-clock timer — wait
                // indefinitely for an explicit DevInput.SubmitPlans()/EndPlanning(). Plans queued
                // before this round began (during the prior execution) end planning immediately.
                if (devSubmittedPaths != null)
                    devEndPlanningNow = true;

                while (!devEndPlanningNow && devMode && !matchEnded)
                    yield return null;
            }
            else
            {
                planningChangesOpen = true;
                StartPlanningClientRpc(endTime, roundNumber);

                double allTeamsCommittedAt = -1d;
                while (
                    !matchEnded
                    && NetworkManager.Singleton != null
                    && NetworkManager.Singleton.ServerTime.Time < endTime + 1
                )
                {
                    double now = NetworkManager.Singleton.ServerTime.Time;
                    if (submittedTeamPaths.Count >= TeamCount)
                    {
                        if (allTeamsCommittedAt < 0d)
                            allTeamsCommittedAt = now;
                        else if (now - allTeamsCommittedAt >= planningUndoGraceSeconds)
                            break;
                    }
                    else
                    {
                        allTeamsCommittedAt = -1d;
                    }
                    yield return null;
                }
            }
            planningChangesOpen = false;

            if (
                matchEnded
                || !IsSpawned
                || NetworkManager.Singleton == null
                || !NetworkManager.Singleton.IsListening
            )
            {
                yield break;
            }

            if (!devMode)
                SetPlanningCommitLockedForHumanTeams();
            FinishPlanningClientRpc();
            lastPlanningSeconds = (float)(NetworkManager.Singleton.ServerTime.Time - startTime);

            CameraEffects.Instance?.FlashClientRpc(MessagePerspective.Neutral);
            SetCardsInteractableClientRpc(false);

            RestoreRetractedPlanningFallbacks();
            PathsDict paths = new();
            foreach (
                PathsDict teamPaths in submittedTeamPaths
                    .OrderBy(entry => entry.Key)
                    .Select(entry => entry.Value)
            )
            {
                foreach (var kvp in teamPaths)
                    paths[kvp.Key] = kvp.Value;
            }

            if (devMode && devSubmittedPaths != null)
            {
                foreach (var kvp in devSubmittedPaths)
                    paths[kvp.Key] = kvp.Value;
                devSubmittedPaths = null;
            }

            // === ABILITY DODGE PHASE (start of execution, when applicable) ===
            // Ability plans were chosen during planning; before any movement, threatened enemies
            // get a chance to dodge. Dive paths returned here have already replaced the dodgers'
            // planned moves inside `paths` (and cancelled their own ability plans).
            List<(GameObject unit, Vector3 square, UnitData data)> activations =
                CollectAbilityActivations(paths);
            if (activations.Count > 0)
            {
                yield return StartCoroutine(RunDodgePhase(activations, paths));
                // Dodging cancels the dodger's own ability plan; re-collect what survived.
                activations = CollectAbilityActivations(paths);
            }
            if (matchEnded)
                yield break;
            HashSet<Unit> cooldownsStartedThisRound = new();
            activations = StartAbilityCooldowns(activations, cooldownsStartedThisRound);

            // Dodge telegraphs are planning aids, not execution VFX. Clear them on every client
            // before movement and abilities start so lines/discs cannot linger into resolution.
            HideAbilityTelegraphsClientRpc(executionStarting: true);
            currentPhase = "executing";
            // Smoke is thrown now rather than placed, so each screen registers when its own
            // canister lands instead of all of them up front. The window has to stay open for as
            // long as abilities are still resolving.
            acceptingSmokeRegistrations = true;
            setOverlayUITextClientRpc("Executing Moves", MessagePerspective.Neutral);

            // Last word on allied destinations before anyone marches: dodge dives and bot orders
            // reach this point without ever having passed through the planning-phase rule.
            ApplyFriendlyEndCellSeparation(paths);
            Dictionary<GameObject, Vector2Int> cellsBeforeExecution = CaptureUnitCells();

            ExecuteMoves(paths);

            // Fire abilities alongside movement; each ability handles its own pauses/transitions.
            foreach (var activation in activations)
            {
                StartCoroutine(RunAbility(activation.unit, activation.square, activation.data));
            }

            StartCoroutine(ResolveUnitOverlaps(cellsBeforeExecution));

            while (
                !matchEnded
                && (
                    CheckStillShooting()
                    || runningAbilities > 0
                    || resolvingOverlaps
                    || IsReturnFireWindowOpen
                )
            )
            {
                //THINKING: ability activates such that theres movement/gameplay extension
                //If moving continues during this time restart checkStillMoving
                if (CheckStillMoving() || IsReturnFireWindowOpen)
                {
                    OrderContinueShooting();
                    while (CheckStillMoving() || IsReturnFireWindowOpen)
                    {
                        yield return null;
                    }
                    yield return null; // Wait a bit before stopping shooting
                }
                OrderAllowShooting(false);

                yield return null;
            }

            if (matchEnded)
                yield break;

            // A dead shooter is inactive, so its own shooting coroutine can no longer hold the
            // round open. Wait on the server-wide projectile registry before round-end arbitration.
            while (!matchEnded && Bullet.ActiveServerBulletCount > 0)
                yield return null;

            if (matchEnded)
                yield break;

            acceptingSmokeRegistrations = false;
            HideAbilityTelegraphsClientRpc(executionStarting: false);
            TickAbilityCooldownsAfterRound(cooldownsStartedThisRound);

            // Elimination takes precedence over objective control. The existing post-loop EndGame
            // path resolves a survivor or simultaneous-wipe draw.
            int livingTeamCount = Enumerable.Range(0, TeamCount).Count(HasLivingTeamUnits);
            if (livingTeamCount < TeamCount)
                break;

            if (Options.IsKingOfTheHill && ResolveKingOfTheHillRound())
                yield break;

            if (ShouldRespawnEliminatedUnits(Options.gameMode, livingTeamCount))
                RespawnEliminatedUnits();
        }

        if (matchEnded)
            yield break;

        currentPhase = "idle";
        EndGame();
    }

    /// <summary>
    /// Server-only mutation seam used by Smoke when its thrown canister lands. Deployment follows
    /// the throw rather than the round, so the cloud a player can see and the occluder that stops a
    /// shot begin together; the window stays open for as long as abilities are resolving.
    /// <para>
    /// Clients are handed the whole active set rather than the newly added cells, because both
    /// commanders can land a canister in the same round and the visual is rebuilt from scratch each
    /// time it is sent.
    /// </para>
    /// </summary>
    public bool TryRegisterSmokeFootprint(Vector2Int center)
    {
        if (
            !IsServer
            || matchEnded
            || currentPhase != "executing"
            || !acceptingSmokeRegistrations
            || !GridSystem.IsSquareFootprintInBounds(center, Smoke.FootprintRadius)
        )
        {
            return false;
        }

        foreach (Vector2Int cell in GridSystem.GetSquareFootprint(center, Smoke.FootprintRadius))
        {
            activeSmokeCells.Add(cell);
        }

        RefreshServerFogForSmokeChange();
        if (IsSpawned && NetworkManager != null && NetworkManager.IsListening)
        {
            ShowSmokeScreenClientRpc(
                activeSmokeCells
                    .OrderBy(cell => cell.y)
                    .ThenBy(cell => cell.x)
                    .Select(gridCoordToWorld)
                    .ToArray()
            );
        }
        return true;
    }

    /// <summary>
    /// Server-only seam for an ability that sets its caster down somewhere new. The caster starts
    /// shooting the moment it lands, so without this the round would let a jump come down among
    /// units that have already been ordered to cease fire and empty a magazine into them unanswered.
    /// Holding the window open gives whoever it landed next to the same chance to shoot back.
    /// </summary>
    public void HoldReturnFireWindow()
    {
        if (!IsServer || matchEnded || currentPhase != "executing")
            return;

        returnFireWindowUntil = Mathf.Max(
            returnFireWindowUntil,
            Time.time + AbilityLandingReturnFireSeconds
        );
    }

    public bool IsSmokeCellActive(Vector2Int cell)
    {
        return activeSmokeCells.Contains(cell);
    }

    public bool DoesCellSegmentCrossActiveSmoke(Vector2Int start, Vector2Int end)
    {
        return GridSystem.DoesCellSegmentCrossCells(start, end, activeSmokeCells);
    }

    public bool DoesWorldSegmentCrossActiveSmoke(Vector3 start, Vector3 end)
    {
        return GridSystem.DoesWorldSegmentCrossCells(start, end, activeSmokeCells);
    }

    private void RefreshServerFogForSmokeChange()
    {
        serverFogDirty = true;
        if (
            !IsServer
            || !IsSpawned
            || !FogOfWarEnabled
            || NetworkManager == null
            || !NetworkManager.IsListening
            || NetworkManager.ShutdownInProgress
        )
        {
            return;
        }

        RefreshFogCellCache();
        RemoveExpiredForceReveals();
        UpdateAllUnitVisibility();
        serverFogDirty = false;
    }

    private void ClearActiveSmokeCells(bool notifyClients = true)
    {
        acceptingSmokeRegistrations = false;
        bool hadActiveSmoke = activeSmokeCells.Count > 0;
        activeSmokeCells.Clear();
        if (hadActiveSmoke)
            RefreshServerFogForSmokeChange();

        bool canNotifyClients =
            notifyClients
            && IsServer
            && IsSpawned
            && NetworkManager != null
            && NetworkManager.IsListening
            && !NetworkManager.ShutdownInProgress;
        if (canNotifyClients)
            HideSmokeScreenClientRpc();
        else
        {
            ClearSmokeScreenVisualsLocal();
            RefreshClientFogForSmokeChange();
        }
    }

    [ClientRpc]
    private void ShowSmokeScreenClientRpc(Vector3[] cellWorldPositions)
    {
        ClearSmokeScreenVisualsLocal();
        if (!IsClient)
            return;
        if (cellWorldPositions == null || cellWorldPositions.Length == 0)
        {
            RefreshClientFogForSmokeChange();
            return;
        }

        foreach (Vector3 cellWorldPosition in cellWorldPositions)
            clientSmokeCells.Add(GridSystem.ConvertToGridCoords(cellWorldPosition));

        clientSmokeVisualRoot = SmokeScreenVisual
            .Create(transform, cellWorldPositions, cellSize)
            .gameObject;

        // A screen changes sightlines for both players and is already public, so its bloom is
        // audible to both, placed at the footprint's centre.
        BattlePlanAudio.PlayAt(AudioCueId.SmokeBloom, AverageCell(cellWorldPositions));

        RefreshClientFogForSmokeChange();
    }

    private static Vector3 AverageCell(Vector3[] cellWorldPositions)
    {
        Vector3 total = Vector3.zero;
        foreach (Vector3 position in cellWorldPositions)
            total += position;
        return total / cellWorldPositions.Length;
    }

    /// <summary>
    /// Impact sounds for collisions the server resolves and then immediately despawns. A bullet
    /// decides what it hit inside a server-only <c>OnCollisionEnter</c> and is despawned on the same
    /// frame, so the cue cannot ride on the projectile the way a unit's damage sound rides on its
    /// replicated health. Hits on units need none of this and do not come through here.
    ///
    /// Deliberately narrow: it carries a position and a fixed two-value kind, never an arbitrary
    /// cue, so it cannot grow into a general "play any sound" network channel. Purely presentation
    /// — nothing in the simulation reads it, and clients cannot raise it.
    /// </summary>
    public enum ImpactAudioKind : byte
    {
        Surface = 0,
        ShieldBlock = 1,
    }

    public void BroadcastImpactAudio(Vector3 position, ImpactAudioKind kind)
    {
        if (!IsServer)
            return;

        PlayImpactAudioClientRpc(position, (byte)kind);
    }

    [ClientRpc]
    private void PlayImpactAudioClientRpc(Vector3 position, byte kind)
    {
        AudioCueId cue = (ImpactAudioKind)kind switch
        {
            ImpactAudioKind.ShieldBlock => AudioCueId.ShieldBlock,
            _ => AudioCueId.ImpactSurface,
        };
        BattlePlanAudio.PlayAt(cue, position);
    }

    [ClientRpc]
    private void HideSmokeScreenClientRpc()
    {
        ClearSmokeScreenVisualsLocal();
        RefreshClientFogForSmokeChange();
    }

    private void ClearSmokeScreenVisualsLocal()
    {
        clientSmokeCells.Clear();

        if (clientSmokeVisualRoot != null)
        {
            // The visual owns its runtime materials and releases them with the object.
            clientSmokeVisualRoot.SetActive(false);
            Destroy(clientSmokeVisualRoot);
            clientSmokeVisualRoot = null;
        }
    }

    private void RefreshClientFogForSmokeChange()
    {
        if (!IsClient || !IsSpawned || !FogOfWarEnabled || fogOverlayTiles.Count == 0)
            return;

        UpdateClientFogOverlay();
    }

    /// <summary>
    /// Extracts valid ability activations from sanitized plans: entries flagged (Item1) whose
    /// path is [startCell, targetSquare] ([startCell] for self-targeted abilities) on a living
    /// unit that actually has an Ability component.
    /// </summary>
    List<(GameObject unit, Vector3 square, UnitData data)> CollectAbilityActivations(
        PathsDict paths
    )
    {
        List<(GameObject, Vector3, UnitData)> activations = new();
        foreach (var kvp in paths)
        {
            GameObject unit = kvp.Key;
            if (unit == null || !unit.activeInHierarchy || !kvp.Value.Item1)
                continue;
            if (unit.GetComponent<Ability>() == null)
                continue;
            Unit identity = unit.GetComponent<Unit>();
            if (identity == null || !identity.CanUseAbility)
                continue;

            var movement = unit.GetComponent<Movement>();
            if (movement == null || movement.unitData == null)
                continue;

            UnitData data = movement.unitData;
            List<Vector3> plan = kvp.Value.Item2;
            // Self-targeted abilities target the unit's own cell.
            Vector3 square = !data.selectAbilitySquare || plan.Count < 2 ? plan[0] : plan[^1];
            activations.Add((unit, square, data));
        }
        return activations;
    }

    private List<(GameObject unit, Vector3 square, UnitData data)> StartAbilityCooldowns(
        IEnumerable<(GameObject unit, Vector3 square, UnitData data)> activations,
        ISet<Unit> startedThisRound
    )
    {
        List<(GameObject unit, Vector3 square, UnitData data)> started = new();
        foreach (var activation in activations)
        {
            Unit identity = activation.unit != null ? activation.unit.GetComponent<Unit>() : null;
            if (identity == null || !identity.TryStartAbilityCooldown())
                continue;

            started.Add(activation);
            startedThisRound?.Add(identity);
            NotifyAbilityCooldownChanged(
                activation.unit,
                identity.AbilityCooldownRoundsRemaining
            );
        }
        return started;
    }

    private void TickAbilityCooldownsAfterRound(ISet<Unit> startedThisRound)
    {
        if (!IsServer)
            return;

        foreach (var teamEntry in allTeamUnitObjects.OrderBy(entry => entry.Key))
        {
            foreach (GameObject unit in teamEntry.Value ?? System.Array.Empty<GameObject>())
            {
                Unit identity = unit != null ? unit.GetComponent<Unit>() : null;
                if (
                    identity == null
                    || (startedThisRound != null && startedThisRound.Contains(identity))
                    || !identity.TickAbilityCooldownRound()
                )
                {
                    continue;
                }

                NotifyAbilityCooldownChanged(unit, identity.AbilityCooldownRoundsRemaining);
            }
        }
    }

    private void NotifyAbilityCooldownChanged(GameObject unit, int remainingRounds)
    {
        if (!IsServer || unit == null || NetworkManager == null)
            return;

        foreach (var teamEntry in allTeamUnitObjects)
        {
            int cardIndex = System.Array.IndexOf(teamEntry.Value, unit);
            if (
                cardIndex < 0
                || !TryGetHumanClientId(teamEntry.Key, out ulong clientId)
                || !NetworkManager.ConnectedClients.ContainsKey(clientId)
            )
            {
                continue;
            }

            SetCardAbilityCooldownClientRpc(
                cardIndex,
                remainingRounds,
                NetworkHelper.ToClient(clientId)
            );
            break;
        }

        NotifyEnemyUnitStatusChanged(unit);
    }

    [ClientRpc]
    private void SetCardAbilityCooldownClientRpc(
        int cardIndex,
        int remainingRounds,
        ClientRpcParams clientRpcParams = default
    )
    {
        GameHUDController.Instance?.SetCardAbilityCooldown(cardIndex, remainingRounds);
    }

    IEnumerator RunAbility(GameObject unit, Vector3 square, UnitData data)
    {
        if (unit == null || !unit.activeInHierarchy)
            yield break;

        NetworkObject networkObject = unit.GetComponent<NetworkObject>();
        Unit identity = unit.GetComponent<Unit>();
        if (networkObject != null && networkObject.IsSpawned)
        {
            ShowAbilityActivationFxClientRpc(
                networkObject,
                unit.transform.position,
                identity != null ? identity.TeamIndex : -1
            );
        }

        runningAbilities++;
        yield return StartCoroutine(
            unit.GetComponent<Ability>().ExecuteAbility(square, data.abilityRadius)
        );
        runningAbilities--;
    }

    [ClientRpc]
    private void ShowAbilityActivationFxClientRpc(
        NetworkObjectReference unitReference,
        Vector3 activationPosition,
        int teamIndex
    )
    {
        Color teamColor = GetTeamColorForViewer(teamIndex);
        StartCoroutine(FlashAbilityCasterWhenVisible(unitReference));
        ImpactShockwave.Spawn(activationPosition, teamColor, 1.8f, 0.45f);
    }

    private static IEnumerator FlashAbilityCasterWhenVisible(NetworkObjectReference unitReference)
    {
        // NGO applies NetworkShow at the end of the frame. A caster that was hidden by fog may
        // therefore be absent when this GameLoop RPC first arrives; retry briefly until its spawn
        // message has been processed instead of silently dropping the activation highlight.
        float deadline = Time.realtimeSinceStartup + 1f;
        do
        {
            if (unitReference.TryGet(out NetworkObject networkObject))
            {
                HitFlash.FlashTarget(networkObject.gameObject, 0.4f, 1.5f);
                yield break;
            }

            yield return null;
        } while (Time.realtimeSinceStartup < deadline);
    }

    private static Vector3 ResolveAbilityEffectSquare(
        GameObject unit,
        Vector3 selectedSquare,
        UnitData data
    )
    {
        if (unit == null || data == null || !data.selectAbilityDirection)
            return selectedSquare;

        Vector2Int start = GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));
        Vector2Int selected = GridSystem.ConvertToGridCoords(selectedSquare);
        if (!GridSystem.TryGetAdjacentDirection(start, selected, out Vector2Int direction))
            return selectedSquare;

        Vector2Int destination = GridSystem.GetDirectionalDestination(
            start,
            direction,
            data.abilityFixedDistance,
            wallLayout
        );
        return gridCoordToWorld(destination);
    }

    /// <summary>
    /// The ability dodge window at the start of execution. Telegraphs every activation to both
    /// clients, alerts enemies inside each ability's responseRange (radius, or line for
    /// responseDistLine abilities), and gives each threatened client timeDivePerUnit × alerted
    /// units to draw dive paths (max diveRange cells, executed at diveSpeed). A submitted dive
    /// replaces that unit's planned move and cancels its own ability plan. In dev mode the window
    /// waits indefinitely for DevInput.SubmitDodge() instead of a wall-clock timer.
    /// </summary>
    IEnumerator RunDodgePhase(
        List<(GameObject unit, Vector3 square, UnitData data)> activations,
        PathsDict paths
    )
    {
        currentPhase = "dodging";
        HidePlanningCommitClientRpc();

        // Telegraph every activation to all clients (both players see what's coming — the
        // counterplay window is the point; the Sniper's lock laser is the model).
        foreach (var (unit, square, data) in activations)
        {
            Vector3 effectSquare = ResolveAbilityEffectSquare(unit, square, data);
            // Fog: only line telegraphs render the caster position. For non-line abilities,
            // don't put a possibly-hidden caster's cell on the wire (RPC payloads reach the
            // enemy client even though the marker branch never reads casterPos).
            Vector3 telegraphOrigin = data.responseDistLine
                ? unit.transform.position
                : effectSquare;
            ShowAbilityTelegraphClientRpc(
                telegraphOrigin,
                effectSquare,
                data.abilityRadius,
                data.responseDistLine,
                unit.GetComponent<Smoke>() != null
            );
        }

        // Compute alerted units per logical team and the widest dive allowance this round.
        dodgeAlerted = new Dictionary<int, HashSet<GameObject>>();
        maxDiveRangeThisRound = 0;
        foreach (var (unit, square, data) in activations)
        {
            Unit casterIdentity = unit.GetComponent<Unit>();
            int enemyTeamIndex =
                casterIdentity != null ? GetEnemyTeamIndex(casterIdentity.TeamIndex) : -1;
            string enemyTeam = GetTeamName(enemyTeamIndex);
            if (enemyTeamIndex < 0 || enemyTeam == null || data.responseRange <= 0f)
                continue;

            Vector3 effectSquare = ResolveAbilityEffectSquare(unit, square, data);
            List<GameObject> threatened = data.responseDistLine
                ? GetUnitsInRangeOfLine(
                    unit.transform.position,
                    square,
                    enemyTeam,
                    data.responseRange
                )
                : Helper.GetObjectsInRange(effectSquare, enemyTeam, data.responseRange);

            if (threatened.Count == 0)
                continue;

            maxDiveRangeThisRound = Mathf.Max(maxDiveRangeThisRound, data.diveRange);
            if (!dodgeAlerted.TryGetValue(enemyTeamIndex, out var set))
                dodgeAlerted[enemyTeamIndex] = set = new HashSet<GameObject>();
            foreach (GameObject t in threatened)
            {
                if (
                    t != null
                    && t.activeInHierarchy
                    && t.GetComponent<Unit>()?.TeamIndex == enemyTeamIndex
                )
                {
                    set.Add(t);
                }
            }
        }
        dodgeAlerted = dodgeAlerted
            .Where(kvp => kvp.Value.Count > 0)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        if (dodgeAlerted.Count == 0)
        {
            dodgeAlerted = null;
            yield break; // The caller still clears telegraphs at the execution boundary.
        }

        // Alert icons and unit references are sent only to each threatened team's client.
        SetDodgeAlerts(true);

        dodgeDivePaths = new PathsDict();
        dodgeResponsesReceived.Clear();
        devDodgeSubmitted = false;

        HashSet<int> casterTeamsAwaitingDodge = activations
            .Select(activation => activation.unit.GetComponent<Unit>())
            .Where(identity =>
                identity != null && dodgeAlerted.ContainsKey(GetEnemyTeamIndex(identity.TeamIndex))
            )
            .Select(identity => identity.TeamIndex)
            .ToHashSet();
        SetDodgeGuidanceForHumanTeams(casterTeamsAwaitingDodge);

        if (
            botPlayer != null
            && dodgeAlerted.TryGetValue(botPlayer.TeamIndex, out HashSet<GameObject> botAlerted)
        )
        {
            PathsDict botDodges = SanitizePaths(
                botPlayer.CreateDodgeContribution(botAlerted, activations, maxDiveRangeThisRound),
                botPlayer.TeamIndex,
                maxDiveRangeThisRound
            );
            foreach (var kvp in botDodges)
            {
                if (botAlerted.Contains(kvp.Key))
                    dodgeDivePaths[kvp.Key] = kvp.Value;
            }
            dodgeResponsesReceived.Add(botPlayer.TeamIndex);
        }

        if (devMode)
        {
            bool humanResponseNeeded = dodgeAlerted.Keys.Any(teamIndex =>
                TryGetHumanClientId(teamIndex, out _)
            );
            if (humanResponseNeeded)
            {
                // DevInput drives human dodges. An alerted bot has already answered above.
                while (!devDodgeSubmitted && devMode && !matchEnded)
                    yield return null;
            }
        }
        else
        {
            int mostAlerted = dodgeAlerted.Values.Max(set => set.Count);
            float window = activations.Max(entry => entry.data.timeDivePerUnit) * mostAlerted;
            double endTime = NetworkManager.Singleton.ServerTime.Time + window;

            foreach (var kvp in dodgeAlerted)
            {
                if (IsBotTeam(kvp.Key))
                    continue;
                if (
                    !TryGetHumanClientId(kvp.Key, out ulong clientId)
                    || !NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)
                )
                {
                    dodgeResponsesReceived.Add(kvp.Key);
                    continue;
                }

                NetworkObjectReference[] refs = kvp
                    .Value.Select(u => (NetworkObjectReference)u.GetComponent<NetworkObject>())
                    .ToArray();
                StartDodgePlanningClientRpc(
                    endTime,
                    refs,
                    maxDiveRangeThisRound,
                    NetworkHelper.ToClient(clientId)
                );
            }

            while (
                dodgeResponsesReceived.Count < dodgeAlerted.Count
                && !matchEnded
                && NetworkManager.Singleton.ServerTime.Time < endTime + 1
            )
            {
                yield return null;
            }
        }

        // Apply dives: replace the dodger's planned move and cancel its own ability plan.
        foreach (var kvp in dodgeDivePaths)
        {
            if (!IsValidDodgeMovementPlan(kvp.Value.Item1, kvp.Value.Item2))
                continue;
            paths[kvp.Key] = (false, kvp.Value.Item2);
            diveUnitsThisRound.Add(kvp.Key);
        }

        SetDodgeAlerts(false);
        dodgeAlerted = null;
        dodgeDivePaths = null;
    }

    public static string GetDodgeGuidance(bool isThreatened, bool isCaster)
    {
        if (isThreatened)
            return ThreatenedDodgeGuidance;
        return isCaster ? CasterDodgeGuidance : NeutralDodgeGuidance;
    }

    public static MessagePerspective GetDodgeGuidancePerspective(bool isThreatened, bool isCaster)
    {
        if (isThreatened)
            return MessagePerspective.Enemy;
        return isCaster ? MessagePerspective.Friendly : MessagePerspective.Neutral;
    }

    private void SetDodgeGuidanceForHumanTeams(HashSet<int> casterTeamsAwaitingDodge)
    {
        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            if (
                !TryGetHumanClientId(teamIndex, out ulong clientId)
                || NetworkManager == null
                || !NetworkManager.ConnectedClients.ContainsKey(clientId)
            )
            {
                continue;
            }

            bool isThreatened = dodgeAlerted.ContainsKey(teamIndex);
            bool isCaster = casterTeamsAwaitingDodge.Contains(teamIndex);
            setOverlayUITextClientRpc(
                GetDodgeGuidance(isThreatened, isCaster),
                GetDodgeGuidancePerspective(isThreatened, isCaster),
                NetworkHelper.ToClient(clientId)
            );
        }
    }

    // Units whose movement this round is a dodge dive (executed at diveSpeed).
    private readonly HashSet<GameObject> diveUnitsThisRound = new();

    /// <summary>
    /// Dodge responses are movement-only. Rejecting ability-flagged plans prevents their looser
    /// target-square rules from being reinterpreted as movement.
    /// </summary>
    public static bool IsValidDodgeMovementPlan(
        bool isAbilityPlan,
        IReadOnlyCollection<Vector3> path
    )
    {
        return !isAbilityPlan && path != null && path.Count > 1;
    }

    void SetDodgeAlerts(bool active)
    {
        if (dodgeAlerted == null)
            return;
        foreach (var teamEntry in dodgeAlerted)
        {
            if (
                !TryGetHumanClientId(teamEntry.Key, out ulong clientId)
                || NetworkManager.Singleton == null
                || !NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)
            )
            {
                continue;
            }

            ClientRpcParams targetClient = NetworkHelper.ToClient(clientId);
            foreach (GameObject unit in teamEntry.Value)
            {
                var netObj = unit != null ? unit.GetComponent<NetworkObject>() : null;
                if (netObj != null && NetworkHelper.Instance != null)
                {
                    NetworkHelper.Instance.SetActiveClientRpc(
                        netObj,
                        "UnitCanvas/Alert",
                        active,
                        targetClient
                    );
                }
            }
        }
    }

    [ClientRpc]
    void StartDodgePlanningClientRpc(
        double endTime,
        NetworkObjectReference[] unitRefs,
        int diveRange,
        ClientRpcParams clientRpcParams = default
    )
    {
        List<GameObject> units = new();
        foreach (NetworkObjectReference unitRef in unitRefs)
        {
            if (unitRef.TryGet(out NetworkObject netObj))
                units.Add(netObj.gameObject);
        }
        if (units.Count == 0)
            return;

        StartCoroutine(
            transform
                .GetComponent<PlanMovement>()
                .StartPlanning(
                    (paths, _) =>
                    {
                        CombatAudio.LocalDivesSubmitted(paths);
                        SendDodgePathsToServerRpc(paths);
                    },
                    endTime,
                    units,
                    diveRange
                )
        );
    }

    [ServerRpc(RequireOwnership = false)]
    void SendDodgePathsToServerRpc(PathsDict paths, ServerRpcParams rpcParams = default)
    {
        if (devMode)
            return; // DevInput drives dodges in dev mode.

        ulong sender = rpcParams.Receive.SenderClientId;
        int senderTeamIndex = GetTeamIndexForClient(sender);
        if (
            senderTeamIndex < 0
            || dodgeAlerted == null
            || !dodgeAlerted.TryGetValue(senderTeamIndex, out var allowed)
            || dodgeResponsesReceived.Contains(senderTeamIndex)
        )
        {
            return;
        }

        PathsDict sanitized = SanitizePaths(paths, senderTeamIndex, maxDiveRangeThisRound);
        foreach (var kvp in sanitized)
        {
            if (allowed.Contains(kvp.Key))
                dodgeDivePaths[kvp.Key] = kvp.Value;
        }
        dodgeResponsesReceived.Add(senderTeamIndex);
    }

    /// <summary>DEV: queue-free server-side dodge submission (any alerted unit, no mouse).</summary>
    public void DevSubmitDodgeServer(PathsDict rawPaths)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[GameLoop] DevSubmitDodgeServer must be called on the server/host.");
            return;
        }
        if (dodgeAlerted == null)
        {
            Debug.LogWarning("[GameLoop] No dodge phase is active.");
            return;
        }

        foreach (var teamEntry in dodgeAlerted)
        {
            if (!TryGetHumanClientId(teamEntry.Key, out _))
                continue;

            PathsDict sanitized = SanitizePaths(
                rawPaths ?? new PathsDict(),
                teamEntry.Key,
                maxDiveRangeThisRound
            );
            foreach (var kvp in sanitized)
            {
                if (teamEntry.Value.Contains(kvp.Key))
                    dodgeDivePaths[kvp.Key] = kvp.Value;
            }
        }
        devDodgeSubmitted = true;
        Debug.Log(
            $"[GameLoop] Dev dodge submitted for {dodgeDivePaths.Count} unit(s); ending dodge window."
        );
    }

    /// <summary>Enemies within `range` cells of the caster→square line (AreaLock-style threats).</summary>
    List<GameObject> GetUnitsInRangeOfLine(
        Vector3 casterPos,
        Vector3 square,
        string team,
        float range
    )
    {
        List<GameObject> unitsInRange = new();
        Vector3 direction = (square - casterPos).normalized;
        if (direction == Vector3.zero)
            return unitsInRange;

        Vector3 end = square + direction * 50f;
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

        foreach (GameObject targetUnit in GameObject.FindGameObjectsWithTag(team))
        {
            float distanceToLine = DistancePointToLineSegment(
                targetUnit.transform.position,
                casterPos,
                end
            );
            if (distanceToLine <= range * cellSize)
                unitsInRange.Add(targetUnit);
        }
        return unitsInRange;
    }

    static float DistancePointToLineSegment(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        Vector2 point2D = new(point.x, point.z);
        Vector2 lineStart2D = new(lineStart.x, lineStart.z);
        Vector2 lineEnd2D = new(lineEnd.x, lineEnd.z);

        Vector2 lineDirection = lineEnd2D - lineStart2D;
        float lineLength = lineDirection.magnitude;
        if (lineDirection == Vector2.zero)
            return Vector2.Distance(point2D, lineStart2D);

        Vector2 lineDirectionNormalized = lineDirection.normalized;
        float projectionLength = Vector2.Dot(point2D - lineStart2D, lineDirectionNormalized);

        if (projectionLength < 0f)
            return float.MaxValue; // Behind the caster: not threatened.
        if (projectionLength > lineLength)
            return Vector2.Distance(point2D, lineEnd2D);

        Vector2 closestPointOnLine = lineStart2D + lineDirectionNormalized * projectionLength;
        return Vector2.Distance(point2D, closestPointOnLine);
    }

    // === ABILITY TELEGRAPH VISUALS (client-side, runtime-generated like AreaLock's laser) ===

    [ClientRpc]
    void ShowAbilityTelegraphClientRpc(
        Vector3 casterPos,
        Vector3 square,
        float radiusCells,
        bool line,
        bool isSmokeScreen
    )
    {
        // Telegraphs are the counterplay window and are shown to both players through fog by
        // design (§6), so the warning is audible on the same terms — placed on the target square,
        // which is already public, never on the caster's cell.
        BattlePlanAudio.PlayAt(AudioCueId.AbilityTelegraph, square);

        if (line)
        {
            Vector3 direction = (square - casterPos).normalized;
            if (direction == Vector3.zero)
                return;
            Vector3 end = square + direction * 50f;
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

            GameObject laserObject = new("AbilityTelegraphLine");
            LineRenderer lr = laserObject.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = new Color(1f, 0.5f, 0f, 0.8f);
            lr.startWidth = lr.endWidth = 0.15f;
            lr.positionCount = 2;
            lr.SetPosition(0, casterPos);
            lr.SetPosition(1, end);
            clientTelegraphs.Add(laserObject);
        }
        else if (isSmokeScreen)
        {
            Vector2Int center = GridSystem.ConvertToGridCoords(square);
            foreach (
                Vector2Int cell in GridSystem.GetSquareFootprint(center, Smoke.FootprintRadius)
            )
            {
                CreateTelegraphCellOutline(cell);
            }
        }
        else if (radiusCells > 0f)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Collider markerCollider = marker.GetComponent<Collider>();
            markerCollider.enabled = false;
            Destroy(markerCollider);
            marker.name = "AbilityTelegraphMarker";
            float diameter = 2f * radiusCells * cellSize;
            marker.transform.position = square + new Vector3(0, 0.15f, 0);
            marker.transform.localScale = new Vector3(diameter, 0.05f, diameter);
            var rend = marker.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Sprites/Default"));
            rend.material.color = new Color(1f, 0.5f, 0f, 0.5f);
            clientTelegraphs.Add(marker);
        }
        else
        {
            // Point-target abilities (no blast radius, e.g. the Pogo Rider's Jump) have nothing
            // to show as a disc; outline the single target cell instead so the telegraph reads
            // clearly for the opponent too.
            CreateTelegraphCellOutline(GridSystem.ConvertToGridCoords(square));
        }
    }

    // Shared single-cell square outline for ability telegraphs (Smoke's per-cell footprint and
    // any zero-radius point-target ability, e.g. Pogo's Jump).
    void CreateTelegraphCellOutline(Vector2Int cell)
    {
        GameObject marker = new($"AbilityTelegraphCell_{cell.x}_{cell.y}");
        LineRenderer lineRenderer = marker.AddComponent<LineRenderer>();
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = lineRenderer.endColor = new Color(1f, 0.5f, 0f, 0.85f);
        lineRenderer.startWidth = lineRenderer.endWidth = 0.08f;
        lineRenderer.loop = true;
        lineRenderer.positionCount = 4;

        Vector3 cellCenter = gridCoordToWorld(cell) + Vector3.up * 0.17f;
        float halfSize = cellSize * 0.46f;
        lineRenderer.SetPositions(
            new[]
            {
                cellCenter + new Vector3(-halfSize, 0f, -halfSize),
                cellCenter + new Vector3(-halfSize, 0f, halfSize),
                cellCenter + new Vector3(halfSize, 0f, halfSize),
                cellCenter + new Vector3(halfSize, 0f, -halfSize),
            }
        );
        clientTelegraphs.Add(marker);
    }

    [ClientRpc]
    void HideAbilityTelegraphsClientRpc(bool executionStarting)
    {
        foreach (GameObject telegraph in clientTelegraphs)
        {
            if (telegraph != null)
            {
                // Destroy is deferred until the end of the frame; deactivate now so the planning
                // artifact is already gone when execution begins in this frame.
                telegraph.SetActive(false);
                Destroy(telegraph);
            }
        }
        clientTelegraphs.Clear();

        // Blast scorches are round-scoped: a burn from last round on a cell someone is about to
        // walk through reads as a live hazard.
        ScorchDecal.ClearAll();

        // The deck pulse marks the planning-to-execution handover (§9.4). This RPC already runs on
        // every peer at exactly that moment, so the flag distinguishes it from the two cleanup
        // calls rather than needing a broadcast of its own.
        if (executionStarting)
            DeckPulseFX.Pulse();
    }

    // === KING OF THE HILL ===

    public static HillControlStatus DetermineHillControl(
        IEnumerable<Vector2Int> hostTeamCells,
        IEnumerable<Vector2Int> opponentTeamCells,
        out int controllingTeamIndex
    )
    {
        bool hostPresent = (hostTeamCells ?? Enumerable.Empty<Vector2Int>()).Any(
            KingOfTheHillCells.Contains
        );
        bool opponentPresent = (opponentTeamCells ?? Enumerable.Empty<Vector2Int>()).Any(
            KingOfTheHillCells.Contains
        );

        if (hostPresent && opponentPresent)
        {
            controllingTeamIndex = NoHillController;
            return HillControlStatus.Contested;
        }

        if (hostPresent)
        {
            controllingTeamIndex = HostTeamIndex;
            return HillControlStatus.Controlled;
        }

        if (opponentPresent)
        {
            controllingTeamIndex = OpponentTeamIndex;
            return HillControlStatus.Controlled;
        }

        controllingTeamIndex = NoHillController;
        return HillControlStatus.Empty;
    }

    public static HillControlState AdvanceHillControlState(
        HillControlState previous,
        HillControlStatus status,
        int controllingTeamIndex
    )
    {
        if (
            status != HillControlStatus.Controlled
            || (controllingTeamIndex != HostTeamIndex && controllingTeamIndex != OpponentTeamIndex)
        )
        {
            return new HillControlState(status, NoHillController, 0);
        }

        int nextStreak =
            previous.Status == HillControlStatus.Controlled
            && previous.ControllingTeamIndex == controllingTeamIndex
                ? previous.Streak + 1
                : 1;
        return new HillControlState(
            HillControlStatus.Controlled,
            controllingTeamIndex,
            Mathf.Min(HillControlRoundsToWin, nextStreak)
        );
    }

    // Respawning stays opt-in by mode so future modes can reuse the lifecycle without making
    // King of the Hill casualties temporary.
    private static readonly HashSet<GameMode> RespawnEnabledGameModes = new();

    public static bool ShouldRespawnEliminatedUnits(GameMode gameMode, int livingTeamCount)
    {
        return livingTeamCount == TeamCount && RespawnEnabledGameModes.Contains(gameMode);
    }

    private bool ResolveKingOfTheHillRound()
    {
        HillControlStatus status = DetermineHillControl(
            GetLivingUnitCells(HostTeamIndex),
            GetLivingUnitCells(OpponentTeamIndex),
            out int controllingTeamIndex
        );
        HillControlState next = AdvanceHillControlState(
            replicatedHillControl.Value,
            status,
            controllingTeamIndex
        );
        replicatedHillControl.Value = next;

        if (next.Status != HillControlStatus.Controlled || next.Streak < HillControlRoundsToWin)
        {
            return false;
        }

        FinishGame(
            MatchResult.ForWinner(next.ControllingTeamIndex, MatchResultReason.KingOfTheHill)
        );
        return true;
    }

    private void RespawnEliminatedUnits()
    {
        List<(
            GameObject unit,
            int teamIndex,
            NetworkObject networkObject,
            Vector3 position,
            Quaternion rotation
        )> respawned = new();
        foreach (var team in allTeamUnitObjects.OrderBy(entry => entry.Key))
        {
            foreach (GameObject unit in team.Value ?? System.Array.Empty<GameObject>())
            {
                Health health = unit != null ? unit.GetComponent<Health>() : null;
                if (health == null || health.IsAlive)
                    continue;

                if (
                    !unitSpawnTransforms.TryGetValue(
                        unit,
                        out (Vector3 position, Quaternion rotation) spawn
                    )
                )
                {
                    Debug.LogError($"[GameLoop] No spawn transform recorded for {unit.name}.");
                    continue;
                }

                if (!health.RespawnAt(spawn.position, spawn.rotation))
                    continue;

                ClearForceReveals(unit);
                lastFogCells.Remove(unit);
                NetworkObject networkObject = unit.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.IsSpawned)
                {
                    respawned.Add((unit, team.Key, networkObject, spawn.position, spawn.rotation));
                }
                SetUnitCardDisabled(unit, false);
            }
        }

        if (respawned.Count == 0)
            return;

        serverFogDirty = true;
        if (FogOfWarEnabled)
        {
            RefreshFogCellCache();
            RemoveExpiredForceReveals();
            UpdateAllUnitVisibility();
            serverFogDirty = false;
        }

        foreach (var entry in respawned)
        {
            ulong[] observers = GetRespawnObserverClientIds(
                entry.unit,
                entry.teamIndex,
                GridSystem.ConvertToGridCoords(entry.position)
            );
            if (observers.Length == 0)
                continue;

            RespawnUnitClientRpc(
                entry.networkObject,
                entry.position,
                entry.rotation,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = observers },
                }
            );
        }

        Debug.Log(
            $"[GameLoop] Respawned {respawned.Count} unit(s) without resetting ability cooldowns."
        );
    }

    private ulong[] GetRespawnObserverClientIds(
        GameObject unit,
        int unitTeamIndex,
        Vector2Int spawnCell
    )
    {
        if (NetworkManager == null)
            return System.Array.Empty<ulong>();

        List<ulong> observers = new();
        foreach (ulong clientId in GetConnectedHumanClientIds())
        {
            int viewerTeamIndex = GetTeamIndexForClient(clientId);
            bool shouldSee =
                !FogOfWarEnabled
                || viewerTeamIndex == unitTeamIndex
                || (
                    viewerTeamIndex >= 0
                    && (
                        ComputeVisibleCellsForTeam(viewerTeamIndex).Contains(spawnCell)
                        || IsForceRevealed(unit, clientId)
                    )
                );
            if (shouldSee)
                observers.Add(clientId);
        }
        return observers.ToArray();
    }

    [ClientRpc]
    private void RespawnUnitClientRpc(
        NetworkObjectReference unitReference,
        Vector3 position,
        Quaternion rotation,
        ClientRpcParams clientRpcParams = default
    )
    {
        if (!unitReference.TryGet(out NetworkObject networkObject))
            return;

        networkObject.gameObject.SetActive(true);
        networkObject.transform.SetPositionAndRotation(position, rotation);
        networkObject.GetComponent<Ability>()?.ResetForRespawn();
        BattlePlanAudio.PlayAt(AudioCueId.UnitSpawn, position, networkObject.gameObject);
        CombatFX.UnitSpawn(networkObject.gameObject);
    }

    private IEnumerable<Vector2Int> GetLivingUnitCells(int teamIndex)
    {
        return GetTeamUnits(teamIndex)
            .Where(IsLivingUnit)
            .Select(unit => GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit)));
    }

    private void RefreshKingOfTheHillPresentation(HillControlState state)
    {
        if (!IsClient)
            return;

        if (!Options.IsKingOfTheHill)
        {
            ClearKingOfTheHillOverlay();
            return;
        }

        EnsureKingOfTheHillOverlay();
        UpdateKingOfTheHillOverlay(state);
        GameHUDController.Instance?.SetHillControl(state);
    }

    private void EnsureKingOfTheHillOverlay()
    {
        if (hillOverlayRoot != null)
            return;

        Shader glowShader = Shader.Find("BattlePlan/GroundGlow");
        if (glowShader == null)
        {
            Debug.LogError(
                "[GameLoop] BattlePlan/GroundGlow shader is required for the hill overlay."
            );
            return;
        }

        hillOverlayRoot = new GameObject("KingOfTheHillOverlay");
        hillOverlayRoot.transform.SetParent(transform, true);
        hillOverlayMaterial = new Material(glowShader) { name = "KingOfTheHillOverlay (Runtime)" };
        hillOverlayProperties = new MaterialPropertyBlock();

        foreach (
            Vector2Int cell in KingOfTheHillCells.OrderBy(cell => cell.y).ThenBy(cell => cell.x)
        )
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            marker.name = $"HillCell_{cell.x}_{cell.y}";
            marker.transform.SetParent(hillOverlayRoot.transform, true);
            marker.transform.position = gridCoordToWorld(cell) + Vector3.up * 0.075f;
            marker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            marker.transform.localScale = Vector3.one * (cellSize * 0.92f);
            Destroy(marker.GetComponent<Collider>());

            Renderer markerRenderer = marker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = hillOverlayMaterial;
            markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;
            hillOverlayRenderers.Add(markerRenderer);
        }
    }

    private void UpdateKingOfTheHillOverlay(HillControlState state)
    {
        if (hillOverlayProperties == null || hillOverlayRenderers.Count == 0)
            return;

        bool controlled =
            state.Status == HillControlStatus.Controlled
            && state.ControllingTeamIndex >= 0
            && state.ControllingTeamIndex < teamColors.Count;
        Color color = controlled
            ? teamColors[state.ControllingTeamIndex]
            : new Color(0.95f, 0.64f, 0.2f, 1f);
        color.a = controlled ? 0.58f : 0.36f;

        hillOverlayProperties.Clear();
        hillOverlayProperties.SetColor(HillGlowColorId, color);
        hillOverlayProperties.SetFloat(HillRingWidthId, 1f);
        hillOverlayProperties.SetFloat(HillEdgeSoftnessId, controlled ? 0.32f : 0.45f);
        hillOverlayProperties.SetFloat(HillIntensityId, controlled ? 1.25f : 0.72f);
        hillOverlayProperties.SetFloat(HillPulseSpeedId, controlled ? 0.7f : 0f);
        hillOverlayProperties.SetFloat(HillPulseAmountId, controlled ? 0.16f : 0f);

        foreach (Renderer markerRenderer in hillOverlayRenderers)
        {
            if (markerRenderer != null)
                markerRenderer.SetPropertyBlock(hillOverlayProperties);
        }
    }

#if UNITY_EDITOR
    public void DevRequestHillPresentationReports()
    {
        if (!IsServer)
        {
            Debug.LogWarning(
                "[GameLoop] Hill presentation reports can only be requested by the server."
            );
            return;
        }

        devHillPresentationReports.Clear();
        DevReportHillPresentationClientRpc();
    }

    [ClientRpc]
    private void DevReportHillPresentationClientRpc()
    {
        HillControlState state = replicatedHillControl.Value;
        int localTeamIndex = LocalTeamIndex;
        string localUnits = string.Join(
            ",",
            FindObjectsByType<Unit>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(unit => unit.TeamIndex == localTeamIndex)
                .OrderBy(unit => unit.name)
                .Select(unit =>
                {
                    Health health = unit.GetComponent<Health>();
                    Collider unitCollider = unit.GetComponent<Collider>();
                    Vector2Int cell = GridSystem.ConvertToGridCoords(unit.transform.position);
                    return $"{unit.name}@{cell} active={unit.gameObject.activeSelf} "
                        + $"alive={health != null && health.IsAlive} "
                        + $"cooldown={unit.AbilityCooldownRoundsRemaining} "
                        + $"collider={unitCollider == null || unitCollider.enabled}";
                })
        );
        DevSubmitHillPresentationReportServerRpc(
            Options.gameMode,
            hillOverlayRenderers.Count,
            (byte)state.Status,
            state.ControllingTeamIndex,
            state.Streak,
            GameHUDController.Instance?.HillStatusText ?? string.Empty,
            localUnits
        );
    }

    [ServerRpc(RequireOwnership = false)]
    private void DevSubmitHillPresentationReportServerRpc(
        GameMode gameMode,
        int markerCount,
        byte status,
        int controllingTeamIndex,
        int streak,
        string hudText,
        string localUnits,
        ServerRpcParams rpcParams = default
    )
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        string report =
            $"mode={gameMode} markers={markerCount} status={(HillControlStatus)status} "
            + $"controller={controllingTeamIndex} streak={streak} hud=\"{hudText}\" "
            + $"localUnits=[{localUnits}]";
        devHillPresentationReports[clientId] = report;
        Debug.Log($"[GameLoop] Hill presentation report: client={clientId} {report}");
    }
#endif

    private void ClearKingOfTheHillOverlay()
    {
        hillOverlayRenderers.Clear();
        hillOverlayProperties = null;

        if (hillOverlayRoot != null)
        {
            Destroy(hillOverlayRoot);
            hillOverlayRoot = null;
        }

        if (hillOverlayMaterial != null)
        {
            Destroy(hillOverlayMaterial);
            hillOverlayMaterial = null;
        }
    }

    // === FOG OF WAR (server: authoritative per-client visibility) ===

    /// <summary>
    /// Enables or disables fog for the whole match. Only the server may author the shared state;
    /// clients react through the NetworkVariable on this always-visible scene object.
    /// </summary>
    public void SetFogOfWarEnabled(bool enabled)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[GameLoop] Only the server can toggle fog of war.");
            return;
        }
        if (FogOfWarEnabled == enabled)
            return;

        fogOfWarEnabled.Value = enabled;
        Debug.Log($"[GameLoop] Fog of war {(enabled ? "enabled" : "disabled")}.");
    }

    private void OnFogOfWarEnabledChanged(bool previousValue, bool newValue)
    {
        if (IsServer)
        {
            if (newValue)
                StartServerFog();
            else
                StopServerFog(true);
        }

        if (IsClient)
        {
            if (newValue)
                StartClientFog();
            else
                StopClientFog();
        }
    }

    private void StartServerFog()
    {
        if (!IsServer || !FogOfWarEnabled || serverFogCoroutine != null)
            return;

        serverFogDirty = true;
        RefreshFogCellCache();
        RemoveExpiredForceReveals();
        UpdateAllUnitVisibility();
        serverFogDirty = false;
        serverFogCoroutine = StartCoroutine(ServerFogLoop());
    }

    private IEnumerator ServerFogLoop()
    {
        while (IsServer && IsSpawned)
        {
            yield return new WaitForSeconds(FogUpdateIntervalSeconds);

            bool cellsChanged = RefreshFogCellCache();
            bool revealsExpired = RemoveExpiredForceReveals();
            if (!cellsChanged && !revealsExpired && !serverFogDirty)
                continue;

            UpdateAllUnitVisibility();
            serverFogDirty = false;
        }
    }

    private void StopServerFog(bool restoreVisibility)
    {
        if (serverFogCoroutine != null)
        {
            StopCoroutine(serverFogCoroutine);
            serverFogCoroutine = null;
        }

        if (restoreVisibility && IsServer)
            ShowAllLivingUnits();

        lastFogCells.Clear();
        serverFogDirty = true;
    }

    private bool RefreshFogCellCache()
    {
        bool changed = false;
        HashSet<GameObject> livingUnits = new();

        foreach (GameObject[] teamUnits in allTeamUnitObjects.Values)
        {
            if (teamUnits == null)
                continue;

            foreach (GameObject unit in teamUnits)
            {
                if (!IsLivingUnit(unit))
                    continue;

                livingUnits.Add(unit);
                Vector2Int cell = GridSystem.ConvertToGridCoords(unit.transform.position);
                if (!lastFogCells.TryGetValue(unit, out Vector2Int previous) || previous != cell)
                {
                    lastFogCells[unit] = cell;
                    changed = true;
                }
            }
        }

        foreach (
            GameObject stale in lastFogCells
                .Keys.Where(unit => !livingUnits.Contains(unit))
                .ToList()
        )
        {
            lastFogCells.Remove(stale);
            changed = true;
        }

        return changed;
    }

    private bool RemoveExpiredForceReveals()
    {
        if (NetworkManager.Singleton == null)
            return false;

        double now = NetworkManager.Singleton.ServerTime.Time;
        List<(GameObject unit, ulong clientId)> expired = forceRevealUntil
            .Where(entry => entry.Key.unit == null || entry.Value <= now)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var key in expired)
            forceRevealUntil.Remove(key);

        List<(GameObject unit, int teamIndex)> expiredTeams = forceRevealToTeamUntil
            .Where(entry => entry.Key.unit == null || entry.Value <= now)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var key in expiredTeams)
            forceRevealToTeamUntil.Remove(key);

        return expired.Count > 0 || expiredTeams.Count > 0;
    }

    private void ClearForceReveals(GameObject unit)
    {
        foreach (var key in forceRevealUntil.Keys.Where(key => key.unit == unit).ToList())
            forceRevealUntil.Remove(key);

        foreach (var key in forceRevealToTeamUntil.Keys.Where(key => key.unit == unit).ToList())
            forceRevealToTeamUntil.Remove(key);
    }

    private void UpdateAllUnitVisibility()
    {
        HideUnitsFromUnassignedClients();
        for (int viewerTeamIndex = 0; viewerTeamIndex < TeamCount; viewerTeamIndex++)
        {
            if (TryGetHumanClientId(viewerTeamIndex, out ulong viewerClientId))
                UpdateUnitVisibilityForClient(viewerClientId);
        }
    }

    private bool IsAuthorizedGameplayObserver(ulong clientId)
    {
        return NetworkManager != null
            && IsAuthorizedGameplayObserver(
                clientId,
                NetworkManager.ServerClientId,
                GetHumanClientIds()
            );
    }

    public static bool IsAuthorizedGameplayObserver(
        ulong clientId,
        ulong serverClientId,
        IEnumerable<ulong> assignedHumanClients
    )
    {
        return clientId == serverClientId
            || (
                assignedHumanClients != null
                && assignedHumanClients.Any(id => !IsBotParticipant(id) && id == clientId)
            );
    }

    private void HideUnitsFromUnassignedClients()
    {
        if (NetworkManager == null)
            return;

        foreach (
            ulong clientId in NetworkManager.ConnectedClientsIds.Where(clientId =>
                clientId != NetworkManager.ServerClientId && GetTeamIndexForClient(clientId) < 0
            )
        )
        {
            foreach (
                GameObject unit in allTeamUnitObjects.Values.SelectMany(units =>
                    units ?? System.Array.Empty<GameObject>()
                )
            )
            {
                NetworkObject networkObject = unit?.GetComponent<NetworkObject>();
                if (
                    networkObject != null
                    && networkObject.IsSpawned
                    && networkObject.IsNetworkVisibleTo(clientId)
                )
                {
                    networkObject.NetworkHide(clientId);
                }
            }
        }
    }

    private void UpdateUnitVisibilityForClient(ulong viewerClientId)
    {
        int viewerTeamIndex = GetTeamIndexForClient(viewerClientId);
        if (
            viewerTeamIndex < 0
            || IsBotParticipant(viewerClientId)
            || NetworkManager.Singleton == null
            || !NetworkManager.Singleton.ConnectedClients.ContainsKey(viewerClientId)
        )
        {
            return;
        }

        HashSet<Vector2Int> visibleCells = ComputeVisibleCellsForTeam(viewerTeamIndex);

        foreach (var targetTeam in allTeamUnitObjects)
        {
            if (targetTeam.Key == viewerTeamIndex || targetTeam.Value == null)
                continue;

            foreach (GameObject unit in targetTeam.Value)
            {
                if (unit == null)
                    continue;

                bool shouldSee =
                    IsLivingUnit(unit)
                    && (
                        visibleCells.Contains(
                            GridSystem.ConvertToGridCoords(unit.transform.position)
                        ) || IsForceRevealed(unit, viewerClientId)
                    );

                if (viewerClientId == NetworkManager.ServerClientId)
                {
                    // Host special case: NGO can't NetworkHide from the server, so suppress
                    // presentation only. forceRenderingOff preserves each renderer's own state
                    // (Shield child, lasers, intentionally disabled variant meshes).
                    SetUnitVisualsLocal(unit, shouldSee);
                    continue;
                }

                NetworkObject netObj = unit.GetComponent<NetworkObject>();
                if (netObj == null || !netObj.IsSpawned)
                    continue;

                bool currentlyVisible = netObj.IsNetworkVisibleTo(viewerClientId);
                if (shouldSee && !currentlyVisible)
                    netObj.NetworkShow(viewerClientId);
                else if (!shouldSee && currentlyVisible)
                    netObj.NetworkHide(viewerClientId);
            }
        }
    }

    public HashSet<Vector2Int> ComputeVisibleCellsForTeam(int teamIndex)
    {
        List<(Vector2Int cell, int range)> viewers = new();
        if (!allTeamUnitObjects.TryGetValue(teamIndex, out GameObject[] units) || units == null)
            return new HashSet<Vector2Int>();

        foreach (GameObject unit in units)
        {
            if (!IsLivingUnit(unit))
                continue;

            Movement movement = unit.GetComponent<Movement>();
            if (movement?.unitData == null)
                continue;

            viewers.Add(
                (
                    GridSystem.ConvertToGridCoords(unit.transform.position),
                    movement.unitData.visionRange
                )
            );
        }

        return GridSystem.ComputeVisibleCells(viewers, activeSmokeCells);
    }

    /// <summary>
    /// The observation boundary used by BotPlayer. Hidden enemy coordinates are filtered here,
    /// alongside the authoritative fog implementation, before any data reaches bot decisions.
    /// </summary>
    public HashSet<Vector2Int> GetObservableCellsForTeam(int teamIndex)
    {
        if (FogOfWarEnabled)
            return ComputeVisibleCellsForTeam(teamIndex);

        HashSet<Vector2Int> allCells = new();
        for (int row = 0; row < GridSystem.RowCount; row++)
        {
            for (int column = 0; column < GridSystem.ColumnCount; column++)
                allCells.Add(new Vector2Int(column, row));
        }
        return allCells;
    }

    public bool CanTeamObserveUnit(int observerTeamIndex, GameObject target)
    {
        if (target == null)
            return false;

        Vector2Int targetCell = GridSystem.ConvertToGridCoords(target.transform.position);
        HashSet<Vector2Int> visibleCells =
            FogOfWarEnabled ? ComputeVisibleCellsForTeam(observerTeamIndex) : null;
        return IsTargetObservable(
            FogOfWarEnabled,
            visibleCells,
            targetCell,
            IsForceRevealedToTeam(target, observerTeamIndex)
        );
    }

    public static bool IsTargetObservable(
        bool fogEnabled,
        ISet<Vector2Int> visibleCells,
        Vector2Int targetCell,
        bool forceRevealed
    )
    {
        return !fogEnabled
            || forceRevealed
            || (visibleCells != null && visibleCells.Contains(targetCell));
    }

    public List<BotEnemySighting> GetVisibleEnemySightingsForTeam(
        int observerTeamIndex,
        ISet<Vector2Int> observableCells
    )
    {
        List<BotEnemySighting> authoritativeSightings = new();
        int enemyTeamIndex = GetEnemyTeamIndex(observerTeamIndex);
        foreach (GameObject enemy in GetTeamUnits(enemyTeamIndex))
        {
            if (!IsLivingUnit(enemy))
                continue;

            Vector2Int cell = GridSystem.ConvertToGridCoords(enemy.transform.position);
            NetworkObject networkObject = enemy.GetComponent<NetworkObject>();
            ulong enemyId =
                networkObject != null && networkObject.IsSpawned
                    ? networkObject.NetworkObjectId
                    : unchecked((ulong)(uint)enemy.GetInstanceID());
            authoritativeSightings.Add(new BotEnemySighting(enemyId, cell));
        }

        return FilterObservableEnemySightings(
            authoritativeSightings,
            observableCells,
            FogOfWarEnabled
        );
    }

    public static List<BotEnemySighting> FilterObservableEnemySightings(
        IEnumerable<BotEnemySighting> authoritativeSightings,
        ISet<Vector2Int> observableCells,
        bool fogOfWarEnabled
    )
    {
        return (authoritativeSightings ?? Enumerable.Empty<BotEnemySighting>())
            .Where(sighting =>
                !fogOfWarEnabled
                || (observableCells != null && observableCells.Contains(sighting.Cell))
            )
            .OrderBy(sighting => sighting.EnemyId)
            .ToList();
    }

    private static bool IsLivingUnit(GameObject unit)
    {
        if (unit == null)
            return false;

        Health health = unit.GetComponent<Health>();
        return health != null ? health.IsAlive : unit.activeSelf;
    }

    private bool IsForceRevealed(GameObject unit, ulong clientId)
    {
        return NetworkManager.Singleton != null
            && forceRevealUntil.TryGetValue((unit, clientId), out double until)
            && NetworkManager.Singleton.ServerTime.Time < until;
    }

    private bool IsForceRevealedToTeam(GameObject unit, int teamIndex)
    {
        return NetworkManager.Singleton != null
            && forceRevealToTeamUntil.TryGetValue((unit, teamIndex), out double until)
            && NetworkManager.Singleton.ServerTime.Time < until;
    }

    public void ForceReveal(GameObject unit, ulong toClientId, float seconds)
    {
        int observerTeamIndex = GetTeamIndexForClient(toClientId);
        if (
            !IsServer
            || unit == null
            || seconds <= 0f
            || NetworkManager.Singleton == null
            || IsBotParticipant(toClientId)
            || observerTeamIndex < 0
            || !NetworkManager.Singleton.ConnectedClients.ContainsKey(toClientId)
        )
            return;

        double until = NetworkManager.Singleton.ServerTime.Time + seconds;
        var key = (unit, toClientId);
        if (!forceRevealUntil.TryGetValue(key, out double existing) || until > existing)
            forceRevealUntil[key] = until;
        var teamKey = (unit, observerTeamIndex);
        if (
            !forceRevealToTeamUntil.TryGetValue(teamKey, out double existingTeam)
            || until > existingTeam
        )
        {
            forceRevealToTeamUntil[teamKey] = until;
        }

        serverFogDirty = true;

        // Keep the deadline while fog is disabled so re-enabling mid-lock/ability restores the
        // correct reveal state. No visibility work is needed while every unit is already shown.
        if (!FogOfWarEnabled)
            return;

        // Show immediately so state changes/RPCs queued after this call are not sent to a hidden
        // object. The regular pass still re-evaluates everyone on the next 0.15 s tick.
        UpdateUnitVisibilityForClient(toClientId);
    }

    public void ForceRevealToTeam(GameObject unit, int observerTeamIndex, float seconds)
    {
        if (
            !IsServer
            || unit == null
            || observerTeamIndex < 0
            || observerTeamIndex >= TeamCount
            || seconds <= 0f
            || NetworkManager.Singleton == null
        )
        {
            return;
        }

        double until = NetworkManager.Singleton.ServerTime.Time + seconds;
        var key = (unit, observerTeamIndex);
        if (!forceRevealToTeamUntil.TryGetValue(key, out double existing) || until > existing)
            forceRevealToTeamUntil[key] = until;

        serverFogDirty = true;
        if (TryGetHumanClientId(observerTeamIndex, out ulong clientId))
            ForceReveal(unit, clientId, seconds);
    }

    public void ForceRevealToEnemyTeams(GameObject unit, float seconds)
    {
        if (!IsServer || unit == null)
            return;

        Unit identity = unit.GetComponent<Unit>();
        if (identity == null)
            return;

        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            if (teamIndex != identity.TeamIndex)
                ForceRevealToTeam(unit, teamIndex, seconds);
        }
    }

    private void SetUnitVisualsLocal(GameObject unit, bool visible)
    {
        if (unit == null)
            return;

        // forceRenderingOff preserves intentionally disabled renderers and later Shield/laser
        // state changes; blindly assigning Renderer.enabled would not.
        foreach (Renderer unitRenderer in unit.GetComponentsInChildren<Renderer>(true))
            unitRenderer.forceRenderingOff = !visible;

        Canvas unitCanvas = unit.transform.Find("UnitCanvas")?.GetComponent<Canvas>();
        if (unitCanvas != null)
            unitCanvas.enabled = visible;
    }

    private void ShowAllLivingUnits()
    {
        for (int viewerTeamIndex = 0; viewerTeamIndex < TeamCount; viewerTeamIndex++)
        {
            if (
                !TryGetHumanClientId(viewerTeamIndex, out ulong viewerClientId)
                || NetworkManager.Singleton == null
                || !NetworkManager.Singleton.ConnectedClients.ContainsKey(viewerClientId)
            )
            {
                continue;
            }

            foreach (var targetTeam in allTeamUnitObjects)
            {
                if (targetTeam.Key == viewerTeamIndex || targetTeam.Value == null)
                    continue;

                foreach (GameObject unit in targetTeam.Value)
                {
                    if (!IsLivingUnit(unit))
                        continue;

                    if (viewerClientId == NetworkManager.ServerClientId)
                    {
                        SetUnitVisualsLocal(unit, true);
                        continue;
                    }

                    NetworkObject netObj = unit.GetComponent<NetworkObject>();
                    if (
                        netObj != null
                        && netObj.IsSpawned
                        && !netObj.IsNetworkVisibleTo(viewerClientId)
                    )
                    {
                        netObj.NetworkShow(viewerClientId);
                    }
                }
            }
        }
    }

    // === FOG OF WAR (client: local pooled dark overlay, zero server traffic) ===

    private void StartClientFog()
    {
        if (!IsClient || !FogOfWarEnabled || clientFogCoroutine != null)
            return;
        if (fogOverlayCellPrefab == null)
        {
            Debug.LogError("[GameLoop] Fog overlay cell prefab is not assigned.");
            return;
        }

        fogOverlayProperties ??= new MaterialPropertyBlock();
        fogOverlayRoot = new GameObject("FogOverlay");
        fogOverlayTiles.Clear();

        for (int row = 0; row < GridSystem.RowCount; row++)
        {
            for (int column = 0; column < GridSystem.ColumnCount; column++)
            {
                Vector2Int cell = new(column, row);
                GameObject tile = Instantiate(
                    fogOverlayCellPrefab,
                    gridCoordToWorld(cell) + Vector3.up * 0.05f,
                    fogOverlayCellPrefab.transform.rotation,
                    fogOverlayRoot.transform
                );
                tile.name = $"FogCell_{column}_{row}";
                tile.SetActive(true);
                fogOverlayTiles[cell] = tile.GetComponent<Renderer>();
            }
        }

        clientFogCoroutine = StartCoroutine(ClientFogLoop());
    }

    private IEnumerator ClientFogLoop()
    {
        while (IsClient && IsSpawned)
        {
            UpdateClientFogOverlay();
            yield return new WaitForSeconds(FogUpdateIntervalSeconds);
        }
    }

    private void UpdateClientFogOverlay()
    {
        if (NetworkManager.Singleton?.SpawnManager == null)
            return;

        int localTeamIndex = LocalTeamIndex;
        if (localTeamIndex < 0)
            return;
        List<(Vector2Int cell, int range)> viewers = new();

        foreach (NetworkObject netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null || netObj.GetComponent<Unit>()?.TeamIndex != localTeamIndex)
            {
                continue;
            }

            GameObject unit = netObj.gameObject;
            Health health = unit.GetComponent<Health>();
            Movement movement = unit.GetComponent<Movement>();
            if (
                !unit.activeInHierarchy
                || (health != null && !health.IsAlive)
                || movement?.unitData == null
            )
            {
                continue;
            }

            viewers.Add(
                (
                    GridSystem.ConvertToGridCoords(unit.transform.position),
                    movement.unitData.visionRange
                )
            );
        }

        HashSet<Vector2Int> visibleCells = GridSystem.ComputeVisibleCells(
            viewers,
            clientSmokeCells
        );
        clientVisibleCells.Clear();
        clientVisibleCells.UnionWith(visibleCells);
        clientVisibleCellsComputed = true;
        foreach (var tile in fogOverlayTiles)
        {
            Renderer tileRenderer = tile.Value;
            if (tileRenderer == null)
                continue;

            bool isFogged = !visibleCells.Contains(tile.Key);
            tileRenderer.gameObject.SetActive(isFogged);
            if (!isFogged)
                continue;

            fogOverlayProperties.Clear();
            fogOverlayProperties.SetVector(FogEdgeMaskId, GetFogEdgeMask(tile.Key, visibleCells));
            tileRenderer.SetPropertyBlock(fogOverlayProperties);
        }
    }

    private static Vector4 GetFogEdgeMask(Vector2Int cell, HashSet<Vector2Int> visibleCells)
    {
        return new Vector4(
            visibleCells.Contains(cell + Vector2Int.left) ? 1f : 0f,
            visibleCells.Contains(cell + Vector2Int.right) ? 1f : 0f,
            visibleCells.Contains(cell + Vector2Int.down) ? 1f : 0f,
            visibleCells.Contains(cell + Vector2Int.up) ? 1f : 0f
        );
    }

    private void StopClientFog()
    {
        if (clientFogCoroutine != null)
        {
            StopCoroutine(clientFogCoroutine);
            clientFogCoroutine = null;
        }

        if (fogOverlayRoot != null)
        {
            fogOverlayRoot.SetActive(false);
            Destroy(fogOverlayRoot);
            fogOverlayRoot = null;
        }

        fogOverlayTiles.Clear();
        clientVisibleCells.Clear();
        clientVisibleCellsComputed = false;
    }

    /// <summary>
    /// Whether the local player's team can currently see <paramref name="cell"/>, read from the
    /// same set the fog overlay is drawn from. Presentation-only: this is what stops a positional
    /// sound from announcing an enemy the player has not been shown. With fog off — or on a peer
    /// that never runs the overlay, such as a headless/bot participant — everything is visible.
    /// </summary>
    public bool IsCellVisibleToLocalTeam(Vector2Int cell)
    {
        if (!FogOfWarEnabled || !clientVisibleCellsComputed)
            return true;

        return clientVisibleCells.Contains(cell);
    }

    void EndGame()
    {
        if (matchEnded)
            return;

        FinishGame(
            ResolveEliminationResult(
                HasLivingTeamUnits(HostTeamIndex),
                HasLivingTeamUnits(OpponentTeamIndex)
            )
        );
    }

    private void FinishGame(MatchResult result)
    {
        if (matchEnded)
            return;
        if (!result.IsValid)
            throw new System.ArgumentException("Cannot finish a match with an invalid result.");

        matchEnded = true;
        LastMatchResult = result;
        currentPhase = "idle";
        Time.timeScale = 1f;
        ClearActiveSmokeCells();
        SetFogOfWarEnabled(false);
        forceRevealUntil.Clear();
        forceRevealToTeamUntil.Clear();
        SetCardsInteractableClientRpc(false);
        HideAbilityTelegraphsClientRpc(executionStarting: false);
        EndGameClientRpc(result);
    }

    [ClientRpc]
    void EndGameClientRpc(MatchResult result)
    {
        LastMatchResult = result;
        MatchEndFX.Play();
        GameHUDController.Instance?.ShowResults(result, LocalTeamIndex, PlayAgain, ExitToMainMenu);
    }

    void PlayAgain()
    {
        GameHUDController.Instance?.SetResultButtonsEnabled(false, true);
        PlayAgainServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    void PlayAgainServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!matchEnded)
            return;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (GetTeamIndexForClient(sender) < 0)
            return;

        playAgain.Add(sender);
        TryStartReplayIfReady();
    }

    private void TryStartReplayIfReady()
    {
        if (!IsServer || disconnectRecoveryStarted || NetworkManager == null)
            return;

        ulong[] connectedHumans = GetConnectedHumanClientIds().ToArray();
        if (!HasReplayQuorum(connectedHumans, playAgain))
            return;

        playAgain.Clear();
        if (!Options.IsBotMatch && connectedHumans.Length < 2)
        {
            disconnectRecoveryStarted = true;
            StartCoroutine(ReturnHostToJoinGameAfterShutdown());
            return;
        }

        ResetMatchState();
        NetworkHelper.CleanupAllNetworkObjects();
        NetworkManager.SceneManager.LoadScene("HomeScreen", LoadSceneMode.Single);
    }

    void ExitToMainMenu()
    {
        GameHUDController.Instance?.SetResultButtonsEnabled(false, false);
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            disconnectRecoveryStarted = true;
            if (networkManager.IsServer)
            {
                DisablePlayAgainButtonClientRpc();
                NetworkHelper.CleanupAllNetworkObjects();
                networkManager.Shutdown();
            }
            else
            {
                ExitServerRpc();
                networkManager.Shutdown();
            }
        }

        ReturnToTitleScreen();
    }

    private static void ReturnToTitleScreen()
    {
        MatchOptions.Reset();
        ResetMatchState();
        SceneManager.LoadScene("Title Screen");
    }

    [ServerRpc(RequireOwnership = false)]
    void ExitServerRpc(ServerRpcParams rpcParams = default)
    {
        if (NetworkManager == null)
            return;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (
            sender == NetworkManager.ServerClientId
            || GetTeamIndexForClient(sender) < 0
            || !NetworkManager.ConnectedClients.ContainsKey(sender)
        )
        {
            return;
        }

        DisablePlayAgainButtonClientRpc();
        NetworkManager.DisconnectClient(sender);
    }

    [ClientRpc]
    void DisablePlayAgainButtonClientRpc()
    {
        GameHUDController.Instance?.SetResultButtonsEnabled(false, true);
    }

    public static bool HasReplayQuorum(
        IEnumerable<ulong> humanParticipants,
        IEnumerable<ulong> votes
    )
    {
        if (humanParticipants == null)
            return false;

        HashSet<ulong> humans = new(humanParticipants.Where(id => !IsBotParticipant(id)));
        if (humans.Count == 0)
            return false;

        HashSet<ulong> receivedVotes =
            votes != null ? new HashSet<ulong>(votes) : new HashSet<ulong>();
        return humans.All(receivedVotes.Contains);
    }

    public static IEnumerable<ulong> FilterConnectedHumanParticipants(
        IEnumerable<ulong> configuredParticipants,
        IEnumerable<ulong> connectedClients
    )
    {
        if (configuredParticipants == null || connectedClients == null)
            return System.Array.Empty<ulong>();

        HashSet<ulong> connected = new(connectedClients);
        return configuredParticipants
            .Where(id => !IsBotParticipant(id) && connected.Contains(id))
            .Distinct()
            .ToArray();
    }

    private IEnumerable<ulong> GetConnectedHumanClientIds()
    {
        return FilterConnectedHumanParticipants(
            GetHumanClientIds(),
            NetworkManager != null
                ? NetworkManager.ConnectedClientsIds
                : System.Array.Empty<ulong>()
        );
    }

    private void OnClientDisconnected(ulong clientId)
    {
        playAgain.Remove(clientId);
        if (disconnectRecoveryStarted || NetworkManager == null)
            return;

        if (!IsServer)
        {
            disconnectRecoveryStarted = true;
            GameHUDController.Instance?.SetPhase("Host disconnected", MessagePerspective.Enemy);
            StartCoroutine(ReturnClientToJoinGameAfterShutdown());
            return;
        }

        int disconnectedTeam = GetTeamIndexForClient(clientId);
        if (disconnectedTeam < 0 || clientId == NetworkManager.ServerClientId)
            return;

        if (matchEnded)
        {
            TryStartReplayIfReady();
            return;
        }

        int winnerTeam = GetEnemyTeamIndex(disconnectedTeam);
        FinishGame(MatchResult.ForWinner(winnerTeam, MatchResultReason.DisconnectForfeit));
    }

    private IEnumerator ReturnHostToJoinGameAfterShutdown()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsServer)
            NetworkHelper.CleanupAllNetworkObjects();
        if (manager != null && manager.IsListening && !manager.ShutdownInProgress)
        {
            manager.Shutdown();
        }

        while (manager != null && (manager.IsListening || manager.ShutdownInProgress))
        {
            yield return null;
        }

        MatchOptions.Reset();
        ResetMatchState();
        SceneManager.LoadScene("JoinGame");
    }

    private IEnumerator ReturnClientToJoinGameAfterShutdown()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening && !manager.ShutdownInProgress)
        {
            manager.Shutdown();
        }

        while (manager != null && (manager.IsListening || manager.ShutdownInProgress))
        {
            yield return null;
        }

        MatchOptions.Reset();
        ResetMatchState();
        SceneManager.LoadScene("JoinGame");
    }

    public static MatchResult ResolveEliminationResult(
        bool hostTeamHasLivingUnits,
        bool opponentTeamHasLivingUnits
    )
    {
        if (hostTeamHasLivingUnits && opponentTeamHasLivingUnits)
        {
            throw new System.InvalidOperationException(
                "Elimination cannot resolve while both crews still have living units."
            );
        }
        if (hostTeamHasLivingUnits)
            return MatchResult.ForWinner(HostTeamIndex, MatchResultReason.Elimination);
        if (opponentTeamHasLivingUnits)
            return MatchResult.ForWinner(OpponentTeamIndex, MatchResultReason.Elimination);
        return MatchResult.Draw(MatchResultReason.SimultaneousElimination);
    }

    [ClientRpc]
    void StartPlanningClientRpc(double endTime, int planningRound)
    {
        if (LocalTeamIndex < 0)
            return;

        StartCoroutine(
            transform
                .GetComponent<PlanMovement>()
                .StartPlanning(
                    (paths, commitVersion) =>
                        SendPathsToServerRpc(paths, planningRound, commitVersion),
                    endTime,
                    allowLockIn: true,
                    unlockCallback: commitVersion =>
                        RetractPathsServerRpc(planningRound, commitVersion),
                    planningRound: planningRound
                )
        );
    }

    [ClientRpc]
    void FinishPlanningClientRpc()
    {
        // Clear editing immediately, but leave the acknowledged LOCKED state visible for one
        // rendered frame. Dodge also hides it synchronously before dodge planning begins.
        transform.GetComponent<PlanMovement>()?.EndPlanningSession(hideCommit: false);
        StartCoroutine(HidePlanningCommitAfterFrame());
    }

    private IEnumerator HidePlanningCommitAfterFrame()
    {
        yield return null;
        GameHUDController.Instance?.HidePlanningCommit();
    }

    [ClientRpc]
    void HidePlanningCommitClientRpc()
    {
        GameHUDController.Instance?.HidePlanningCommit();
    }

    [ClientRpc]
    void SetPlanningCommitWaitingClientRpc(
        int planningRound,
        int commitVersion,
        ClientRpcParams clientRpcParams = default
    )
    {
        transform
            .GetComponent<PlanMovement>()
            ?.NotifyPlanningCommitAccepted(planningRound, commitVersion);
    }

    [ClientRpc]
    void SetPlanningCommitRetractedClientRpc(
        int planningRound,
        int commitVersion,
        ClientRpcParams clientRpcParams = default
    )
    {
        transform
            .GetComponent<PlanMovement>()
            ?.NotifyPlanningCommitRetracted(planningRound, commitVersion);
    }

    [ClientRpc]
    void SetPlanningCommitLockedClientRpc(
        int planningRound,
        ClientRpcParams clientRpcParams = default
    )
    {
        transform
            .GetComponent<PlanMovement>()
            ?.NotifyPlanningCommitFinalized(planningRound);
    }

    [ServerRpc(RequireOwnership = false)]
    void SendPathsToServerRpc(
        PathsDict paths,
        int planningRound,
        int commitVersion,
        ServerRpcParams rpcParams = default
    )
    {
        // In dev mode DevInput drives all input; ignore mouse-driven client submissions so they
        // can't overwrite dev plans (this leaves normal gameplay untouched when devMode is off).
        if (devMode)
            return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        int senderTeamIndex = GetTeamIndexForClient(senderClientId);
        if (
            currentPhase != "planning"
            || planningRound != roundNumber
            || commitVersion <= 0
            || !planningChangesOpen
            || NetworkManager == null
            || !NetworkManager.ConnectedClients.ContainsKey(senderClientId)
            || senderTeamIndex < 0
            || IsBotTeam(senderTeamIndex)
            || submittedTeamPaths.ContainsKey(senderTeamIndex)
            || !IsNewerPlanningCommitVersion(senderTeamIndex, commitVersion)
        )
        {
            return;
        }

        submittedTeamPaths[senderTeamIndex] = SanitizePaths(
            paths ?? new PathsDict(),
            senderTeamIndex
        );
        latestTeamPlanVersions[senderTeamIndex] = commitVersion;
        retractedTeamPathFallbacks.Remove(senderTeamIndex);
        NotifyPlanningCommitAccepted(senderTeamIndex, commitVersion);
    }

    private bool IsNewerPlanningCommitVersion(int teamIndex, int commitVersion)
    {
        return !latestTeamPlanVersions.TryGetValue(teamIndex, out int latestVersion)
            || commitVersion > latestVersion;
    }

    [ServerRpc(RequireOwnership = false)]
    void RetractPathsServerRpc(
        int planningRound,
        int commitVersion,
        ServerRpcParams rpcParams = default
    )
    {
        if (devMode)
            return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        int senderTeamIndex = GetTeamIndexForClient(senderClientId);
        bool senderIsValid =
            NetworkManager != null
            && NetworkManager.ConnectedClients.ContainsKey(senderClientId)
            && senderTeamIndex >= 0
            && !IsBotTeam(senderTeamIndex);
        if (
            !senderIsValid
            || currentPhase != "planning"
            || planningRound != roundNumber
            || commitVersion <= 0
            || !planningChangesOpen
            || NetworkManager.Singleton == null
            || NetworkManager.Singleton.ServerTime.Time >= planningDeadline
        )
        {
            if (senderIsValid)
            {
                SetPlanningCommitLockedClientRpc(
                    planningRound,
                    NetworkHelper.ToClient(senderClientId)
                );
            }
            return;
        }

        if (!TryRetractPlanningSubmission(senderTeamIndex, commitVersion))
            return;

        SetPlanningCommitRetractedClientRpc(
            planningRound,
            commitVersion,
            NetworkHelper.ToClient(senderClientId)
        );
    }

    private bool TryRetractPlanningSubmission(int teamIndex, int commitVersion)
    {
        if (
            !latestTeamPlanVersions.TryGetValue(teamIndex, out int submittedVersion)
            || submittedVersion != commitVersion
            || !submittedTeamPaths.TryGetValue(teamIndex, out PathsDict submittedPaths)
        )
        {
            return false;
        }

        retractedTeamPathFallbacks[teamIndex] = submittedPaths;
        return submittedTeamPaths.Remove(teamIndex);
    }

    private void RestoreRetractedPlanningFallbacks()
    {
        foreach (var entry in retractedTeamPathFallbacks)
        {
            if (!submittedTeamPaths.ContainsKey(entry.Key))
                submittedTeamPaths[entry.Key] = entry.Value;
        }
        retractedTeamPathFallbacks.Clear();
    }

    private void NotifyPlanningCommitAccepted(int senderTeamIndex, int commitVersion)
    {
        if (
            TryGetHumanClientId(senderTeamIndex, out ulong senderClientId)
            && NetworkManager != null
            && NetworkManager.ConnectedClients.ContainsKey(senderClientId)
        )
        {
            SetPlanningCommitWaitingClientRpc(
                roundNumber,
                commitVersion,
                NetworkHelper.ToClient(senderClientId)
            );
        }
    }

    private void SetPlanningCommitLockedForHumanTeams()
    {
        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            if (
                TryGetHumanClientId(teamIndex, out ulong clientId)
                && NetworkManager != null
                && NetworkManager.ConnectedClients.ContainsKey(clientId)
            )
            {
                SetPlanningCommitLockedClientRpc(
                    roundNumber,
                    NetworkHelper.ToClient(clientId)
                );
            }
        }
    }

    /// <summary>
    /// Validates and snaps a set of submitted unit paths to legal grid moves (adjacency, walls,
    /// move-distance, no-repeat, in-bounds). When <paramref name="teamFilter"/> has a value the
    /// unit must belong to that logical team.
    /// Plans flagged as abilities (Item1) are validated as [startCell, targetSquare] instead.
    /// Standard targets use abilitySquareRange (Manhattan); directional targets use one of the
    /// eight adjacent cells as an anchor. Invalid plans degrade to a stay-put movement plan.
    /// <paramref name="maxStepsOverride"/> caps movement length (used for dodge dives).
    /// </summary>
    PathsDict SanitizePaths(PathsDict paths, int? teamFilter, int maxStepsOverride = -1)
    {
        PathsDict sanitized = new();

        foreach (var kvp in paths)
        {
            GameObject unit = kvp.Key;
            if (unit == null || !unit.activeInHierarchy)
                continue;

            var netObj = unit.GetComponent<NetworkObject>();
            if (netObj == null || !netObj.IsSpawned)
                continue;

            Unit identity = unit.GetComponent<Unit>();
            if (
                identity == null
                || identity.TeamIndex < 0
                || (teamFilter.HasValue && identity.TeamIndex != teamFilter.Value)
            )
            {
                continue;
            }

            var movement = unit.GetComponent<Movement>();
            if (movement == null || movement.unitData == null)
                continue;

            // Ability plan: (true, [startCell, targetSquare]).
            if (kvp.Value.Item1)
            {
                sanitized[unit] = SanitizeAbilityPlan(unit, movement.unitData, kvp.Value.Item2);
                continue;
            }

            int maxSteps =
                maxStepsOverride >= 0 ? maxStepsOverride : Mathf.Max(0, movement.unitData.moveDist);
            List<Vector3> submitted = kvp.Value.Item2 ?? new List<Vector3>();

            // Build a sanitized path: start at current cell, then step by step adjacents, up to maxSteps
            List<Vector3> clean = new();
            Vector3 start = GridSystem.GetNearestGridCell(unit);
            clean.Add(start);

            int stepsAdded = 0;
            Vector3 last = start;
            foreach (var point in submitted)
            {
                if (stepsAdded >= maxSteps)
                    break;

                // snap to grid
                Vector3 snapped = GridSystem.GetNearestGridCell(point);

                // must be exactly one cell away (no diagonals) and not already in path
                float dist = Mathf.Abs(snapped.x - last.x) + Mathf.Abs(snapped.z - last.z);
                bool isAdjacent = Mathf.Abs(dist - cellSize) <= 0.1f; // Manhanttan 1 step
                if (!isAdjacent)
                    continue;
                if (clean.Contains(snapped))
                    continue;

                // must not enter a wall cell
                if (wallLayout.Contains(GridSystem.ConvertToGridCoords(snapped)))
                    continue;

                // optionally: ensure within grid bounds
                if (!gridBounds.Contains(new Vector2(snapped.x, snapped.z)))
                    continue;

                clean.Add(snapped);
                last = snapped;
                stepsAdded++;
            }

            sanitized[unit] = (kvp.Value.Item1, clean);
        }

        return sanitized;
    }

    /// <summary>
    /// Validates an ability plan. Returns (true, [start, square]) when the target is legal,
    /// (true, [start]) for self-targeted abilities, or degrades to a stay-put movement plan
    /// (false, [start]) when the plan is invalid so the round still resolves predictably.
    /// </summary>
    (bool, List<Vector3>) SanitizeAbilityPlan(
        GameObject unit,
        UnitData data,
        List<Vector3> submitted
    )
    {
        Vector3 start = GridSystem.GetNearestGridCell(unit);

        Unit identity = unit.GetComponent<Unit>();
        if (unit.GetComponent<Ability>() == null || identity == null || !identity.CanUseAbility)
            return (false, new List<Vector3> { start });

        // Self-targeted abilities need no square.
        if (!data.selectAbilitySquare)
            return (true, new List<Vector3> { start });

        if (submitted == null || submitted.Count < 2)
            return (false, new List<Vector3> { start });

        Vector3 square = GridSystem.GetNearestGridCell(submitted[^1]);
        bool inBounds = gridBounds.Contains(new Vector2(square.x, square.z));
        Vector2Int targetCell = GridSystem.ConvertToGridCoords(square);
        bool footprintInBounds =
            unit.GetComponent<Smoke>() == null
            || GridSystem.IsSquareFootprintInBounds(targetCell, Smoke.FootprintRadius);

        if (data.selectAbilityDirection)
        {
            Vector2Int startCell = GridSystem.ConvertToGridCoords(start);
            bool validDirection =
                data.abilityFixedDistance > 0
                && GridSystem.TryGetAdjacentDirection(startCell, targetCell, out _);
            return inBounds && footprintInBounds && validDirection
                ? (true, new List<Vector3> { start, square })
                : (false, new List<Vector3> { start });
        }

        float manhattanCells =
            (Mathf.Abs(square.x - start.x) + Mathf.Abs(square.z - start.z)) / cellSize;
        bool inRange = manhattanCells <= data.abilitySquareRange + 0.1f;
        // Line abilities (AreaLock) use the square only as a direction anchor; others land there.
        bool wallOk =
            data.responseDistLine || !wallLayout.Contains(GridSystem.ConvertToGridCoords(square));
        bool aimOk = data.CanTargetOwnCell || targetCell != GridSystem.ConvertToGridCoords(start);

        if (!inBounds || !footprintInBounds || !inRange || !wallOk || !aimOk)
            return (false, new List<Vector3> { start });

        return (true, new List<Vector3> { start, square });
    }

    /// <summary>
    /// How many cells of each route may be kept so that no two of them finish on the same cell.
    /// Routes are resolved in the order given: an earlier one keeps its destination and a later
    /// one gives up steps until it stops somewhere unclaimed, falling back to its own starting
    /// cell when every step of it is already spoken for. <paramref name="claimedCells"/> arrives
    /// holding the cells of units that are not moving at all and collects each destination as it
    /// is handed out.
    /// </summary>
    public static List<int> ResolveUniqueEndCellLengths(
        IReadOnlyList<IReadOnlyList<Vector2Int>> routes,
        ISet<Vector2Int> claimedCells = null
    )
    {
        List<int> lengths = new();
        if (routes == null)
            return lengths;

        ISet<Vector2Int> claimed = claimedCells ?? new HashSet<Vector2Int>();
        foreach (IReadOnlyList<Vector2Int> route in routes)
        {
            if (route == null || route.Count == 0)
            {
                lengths.Add(0);
                continue;
            }

            int endIndex = route.Count - 1;
            while (endIndex > 0 && claimed.Contains(route[endIndex]))
                endIndex--;

            claimed.Add(route[endIndex]);
            lengths.Add(endIndex + 1);
        }
        return lengths;
    }

    /// <summary>
    /// Cuts each team's routes short until no two of its own units finish the round on the same
    /// cell. Planning settles its own routes the same way before submitting and the bot reserves
    /// its own destinations, so this is the authoritative backstop: a tampered submission, or a
    /// dodge dive drawn against only the alerted part of a team, still resolves to one unit per
    /// cell. Shorter orders are honoured first, so a unit holding its ground keeps its cell and the
    /// one walking into it is the one cut short.
    /// </summary>
    void ApplyFriendlyEndCellSeparation(PathsDict paths)
    {
        if (paths == null)
            return;

        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            HashSet<Vector2Int> claimed = new();
            List<(GameObject unit, int rosterSlot, List<Vector3> route)> movers = new();
            GameObject[] teamUnits = GetTeamUnits(teamIndex);

            for (int rosterSlot = 0; rosterSlot < teamUnits.Length; rosterSlot++)
            {
                GameObject unit = teamUnits[rosterSlot];
                if (unit == null || !unit.activeInHierarchy || !IsLivingUnit(unit))
                    continue;

                // A unit with no orders, or one spending the round on an ability, never marches
                // anywhere: ExecuteMoves keeps casters on their own cell, and the repositioning a
                // rush or a jump does is settled afterwards by the overlap pass. Either way the
                // cell it is standing on is held against every route.
                if (
                    !paths.TryGetValue(unit, out (bool, List<Vector3>) plan)
                    || plan.Item1
                    || plan.Item2 == null
                    || plan.Item2.Count < 2
                )
                {
                    claimed.Add(
                        GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit))
                    );
                    continue;
                }

                movers.Add((unit, rosterSlot, plan.Item2));
            }

            // A shorter route has fewer cells to retreat along, so it picks its destination first.
            // Roster slot settles two equally long routes so the outcome never depends on
            // dictionary order.
            movers.Sort(
                (left, right) =>
                {
                    int lengthComparison = left.route.Count.CompareTo(right.route.Count);
                    return lengthComparison != 0
                        ? lengthComparison
                        : left.rosterSlot.CompareTo(right.rosterSlot);
                }
            );

            List<int> lengths = ResolveUniqueEndCellLengths(
                movers
                    .Select(mover =>
                        (IReadOnlyList<Vector2Int>)
                            mover.route.Select(GridSystem.ConvertToGridCoords).ToList()
                    )
                    .ToList(),
                claimed
            );

            for (int i = 0; i < movers.Count; i++)
            {
                List<Vector3> route = movers[i].route;
                if (lengths[i] >= route.Count)
                    continue;

                Debug.Log(
                    $"[GameLoop] {movers[i].unit.name} stops {route.Count - lengths[i]} cell(s) "
                        + "short of its order: an allied unit already ends the round there."
                );
                route.RemoveRange(lengths[i], route.Count - lengths[i]);
            }
        }
    }

    /// <summary>
    /// DEV: server-side entry point for programmatic (no-mouse) plan submission. Accepts
    /// paths for ANY team's units, sanitizes them with the same rules as mouse input, and ends the
    /// current planning phase immediately so a full round can resolve solo. Server-only.
    /// </summary>
    public void DevSubmitPlansServer(PathsDict rawPaths)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[GameLoop] DevSubmitPlansServer must be called on the server/host.");
            return;
        }

        PathsDict sanitized = new();
        PathsDict submitted = rawPaths ?? new PathsDict();
        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            // A solo-bot dev run exercises the real bot. Dev input may not replace its orders.
            if (botPlayer != null && teamIndex == botPlayer.TeamIndex)
                continue;

            foreach (var kvp in SanitizePaths(submitted, teamIndex))
                sanitized[kvp.Key] = kvp.Value;
        }

        DevFillStationaryForControlledTeams(sanitized);
        devSubmittedPaths = sanitized;
        devEndPlanningNow = true;
        Debug.Log(
            $"[GameLoop] Dev plans submitted for {devSubmittedPaths.Count} unit(s); ending planning immediately."
        );
    }

    /// <summary>DEV: gives every agent-controlled living unit a deterministic stay-put fallback.</summary>
    void DevFillStationaryForControlledTeams(PathsDict dict)
    {
        foreach (var kvp in allTeamUnitObjects)
        {
            if (botPlayer != null && kvp.Key == botPlayer.TeamIndex)
                continue;

            foreach (GameObject unit in kvp.Value)
            {
                if (unit == null || !unit.activeSelf || dict.ContainsKey(unit))
                    continue;
                dict[unit] = (false, new List<Vector3> { GridSystem.GetNearestGridCell(unit) });
            }
        }
    }

    void ExecuteMoves(PathsDict paths)
    {
        // Get all units from all teams
        foreach (var team in teamNames)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                List<Vector3> movementPath = new();

                // Check if this unit has a path in the dictionary
                if (paths.ContainsKey(unit))
                {
                    // Ability units stay put during movement; their Ability coroutine (started
                    // right after ExecuteMoves) owns any repositioning (e.g. Pogo's jump).
                    movementPath = paths[unit].Item1
                        ? new List<Vector3> { paths[unit].Item2[0] }
                        : new List<Vector3>(paths[unit].Item2); // Create a new list copy
                }

                // Start movement with either the actual path or empty list.
                // Dodge dives run at diveSpeed instead of moveSpeed.
                unit.GetComponent<Movement>()
                    .StartMovement(movementPath, diveUnitsThisRound.Contains(unit));
            }
        }
        diveUnitsThisRound.Clear();
    }

    /// <summary>Living units of both teams, in team then roster order.</summary>
    private static IEnumerable<GameObject> EnumerateLivingUnits()
    {
        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            foreach (GameObject unit in GetTeamUnits(teamIndex))
            {
                if (unit != null && unit.activeInHierarchy && IsLivingUnit(unit))
                    yield return unit;
            }
        }
    }

    /// <summary>
    /// The cell every living unit is standing on. Taken before execution so the overlap pass can
    /// tell who walked into a contested cell apart from whoever was already there.
    /// </summary>
    private static Dictionary<GameObject, Vector2Int> CaptureUnitCells()
    {
        Dictionary<GameObject, Vector2Int> cells = new();
        foreach (GameObject unit in EnumerateLivingUnits())
            cells[unit] = GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));
        return cells;
    }

    /// <summary>
    /// Two units may not finish the round on the same cell. Allied orders are pulled apart before
    /// execution, but the two commanders plan blind to each other and a rush or a jump sets its
    /// caster down wherever it lands, so a round can still end with units stacked. Once nothing is
    /// moving any more, whoever arrived on a contested cell is slid off it.
    /// </summary>
    IEnumerator ResolveUnitOverlaps(Dictionary<GameObject, Vector2Int> cellsBeforeExecution)
    {
        resolvingOverlaps = true;

        // Rushes and jumps reposition their caster from inside their own coroutine, so a unit's
        // cell is only final once movement AND every ability have finished.
        while (!matchEnded && (CheckStillMoving() || runningAbilities > 0))
            yield return null;

        if (!matchEnded)
        {
            yield return StartCoroutine(
                SlideUnitsToCells(BuildOverlapDisplacements(cellsBeforeExecution))
            );
        }

        resolvingOverlaps = false;
    }

    /// <summary>
    /// Who has to give up the cell they are standing on, and where each of them goes. A unit that
    /// never left the cell this round keeps it; anyone who walked, rushed or landed on top of it
    /// is moved off. When nobody was standing there first, every contender is moved off and the
    /// cell is left empty for the rest of the round, so racing an enemy to a cell is not something
    /// either side can win by virtue of being sorted first. Cells are resolved bottom to top and
    /// contenders in team then roster order, so the pass gives the same answer every time it runs.
    /// </summary>
    private List<(GameObject unit, Vector2Int cell)> BuildOverlapDisplacements(
        IReadOnlyDictionary<GameObject, Vector2Int> cellsBeforeExecution
    )
    {
        Dictionary<Vector2Int, List<GameObject>> occupants = new();
        HashSet<Vector2Int> claimed = new();
        foreach (GameObject unit in EnumerateLivingUnits())
        {
            Vector2Int cell = GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));
            if (!occupants.TryGetValue(cell, out List<GameObject> sharing))
                occupants[cell] = sharing = new List<GameObject>();

            sharing.Add(unit);
            claimed.Add(cell);
        }

        List<(GameObject unit, Vector2Int cell)> displacements = new();
        foreach (
            Vector2Int cell in occupants
                .Where(entry => entry.Value.Count > 1)
                .Select(entry => entry.Key)
                .OrderBy(contested => contested.y)
                .ThenBy(contested => contested.x)
                .ToList()
        )
        {
            List<GameObject> sharing = occupants[cell];
            GameObject holder = sharing.FirstOrDefault(candidate =>
                cellsBeforeExecution != null
                && cellsBeforeExecution.TryGetValue(candidate, out Vector2Int cellBefore)
                && cellBefore == cell
            );

            foreach (GameObject unit in sharing)
            {
                if (unit == holder)
                    continue;

                // Pulling the shove back toward where the unit set out from makes it read as
                // giving ground rather than as being flung somewhere arbitrary.
                Vector2Int anchor = cell;
                if (
                    cellsBeforeExecution != null
                    && cellsBeforeExecution.TryGetValue(unit, out Vector2Int cellBeforeMove)
                )
                {
                    anchor = cellBeforeMove;
                }

                if (
                    !GridSystem.TryFindDisplacementCell(
                        cell,
                        anchor,
                        claimed,
                        MaxDisplacementSteps,
                        out Vector2Int destination
                    )
                )
                {
                    Debug.LogWarning(
                        $"[GameLoop] {unit.name} shares cell ({cell.x},{cell.y}) and has no free "
                            + $"cell within {MaxDisplacementSteps} steps; it stays where it is."
                    );
                    continue;
                }

                claimed.Add(destination);
                displacements.Add((unit, destination));
            }
        }
        return displacements;
    }

    /// <summary>
    /// Slides displaced units onto their new cells together. Snapping would read as a bug; the
    /// slide stays server-side and reaches clients through each unit's NetworkTransform.
    /// </summary>
    IEnumerator SlideUnitsToCells(List<(GameObject unit, Vector2Int cell)> displacements)
    {
        List<(Transform unitTransform, Vector3 from, Vector3 to)> slides = new();
        foreach (var (unit, cell) in displacements)
        {
            if (unit == null)
                continue;

            Transform unitTransform = unit.transform;
            slides.Add(
                (
                    unitTransform,
                    unitTransform.position,
                    gridCoordToWorld(cell) + Helper.heightOffset(unitTransform)
                )
            );
        }

        if (slides.Count == 0)
            yield break;

        float elapsed = 0f;
        while (elapsed < DisplacementSlideSeconds)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / DisplacementSlideSeconds);
            foreach (var slide in slides)
            {
                if (slide.unitTransform != null)
                    slide.unitTransform.position = Vector3.Lerp(slide.from, slide.to, progress);
            }
            yield return null;
        }

        foreach (var slide in slides)
        {
            if (slide.unitTransform != null)
                slide.unitTransform.position = slide.to;
        }

        // Shooting acquires targets with casts against colliders. Units are moved by Transform, so
        // without this the shots taken right after a shove would still be aimed at where the
        // displaced unit used to stand.
        Physics.SyncTransforms();
    }

    public void SetGroupLayerGlobal(GameObject obj, int layer)
    {
        SetGroupLayer(obj, layer);
        var netObj = obj != null ? obj.GetComponent<NetworkObject>() : null;
        if (netObj != null)
        {
            NetworkObjectReference objRef = netObj;
            SetGroupLayerClientRpc(objRef, layer);
        }
    }

    [ClientRpc]
    public void SetGroupLayerClientRpc(NetworkObjectReference objRef, int layer)
    {
        if (IsServer)
            return;
        if (objRef.TryGet(out NetworkObject networkObject))
        {
            SetGroupLayer(networkObject.gameObject, layer);
        }
    }

    public static void SetGroupLayer(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetGroupLayer(child.gameObject, layer);
        }
    }

    bool CheckStillMoving()
    {
        foreach (var team in teamNames)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                if (unit.GetComponent<Movement>().moving == true)
                {
                    return true;
                }
            }
        }

        return false;
    }

    bool CheckStillShooting()
    {
        foreach (var team in teamNames)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                if (unit.GetComponent<Shooting>().stillShooting == true)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Wrapper function that accepts "friendly", "enemy", or "neutral" instead of specific team names
    /// </summary>
    [ClientRpc]
    public void setOverlayUITextClientRpc(
        string message,
        MessagePerspective perspective,
        ClientRpcParams clientRpcParams = default
    )
    {
        GameHUDController.Instance?.HideDeployment();
        GameHUDController.Instance?.SetPhase(message, perspective);
        GameHUDController.Instance?.Flash(perspective, 0.35f);
    }

    public Color GetTeamColor(string team)
    {
        if (!teamNames.Contains(team))
        {
            Debug.LogWarning(
                $"[GameLoop] Team '{team}' not found in teams list, falling back to executingMoves color"
            );
            return executingMoves;
        }
        return teamColors[teamNames.IndexOf(team)];
    }

    /// <summary>
    /// Both players read their own crew as blue and the enemy as red, so a team-coloured visual is
    /// chosen from the side of the board it is watched from rather than from the absolute team.
    /// Unit materials and vision cones already work this way; anything else that colours by team
    /// has to agree, or the same shot reads as friendly on one screen and hostile on the other.
    /// </summary>
    public static readonly Color FriendlyTeamColor = new(0.22f, 0.78f, 1f);
    public static readonly Color EnemyTeamColor = new(1f, 0.23f, 0.33f);

    public static bool IsTeamFriendlyToLocalPlayer(int teamIndex)
    {
        return Instance != null && teamIndex >= 0 && teamIndex == Instance.LocalTeamIndex;
    }

    public static Color GetTeamColorForViewer(int teamIndex)
    {
        return IsTeamFriendlyToLocalPlayer(teamIndex) ? FriendlyTeamColor : EnemyTeamColor;
    }

    public Material GetTeamMaterial(string team)
    {
        if (!teamNames.Contains(team))
        {
            Debug.LogWarning(
                $"[GameLoop] Team '{team}' not found in teams list, falling back to null material"
            );
            return null;
        }
        return teamMaterials[teamNames.IndexOf(team)];
    }

    public static string GetEnemyTeam(string team)
    {
        int teamIndex = GetTeamIndex(team);
        if (teamIndex < 0)
        {
            Debug.LogWarning(
                $"[GameLoop] Team '{team}' not found in teams list, cannot determine enemy team, falling back to null"
            );
            return null;
        }
        return GetTeamName(GetEnemyTeamIndex(teamIndex));
    }

    public static int GetTeamIndex(string team)
    {
        return string.IsNullOrEmpty(team) ? -1 : teamNames.IndexOf(team);
    }

    public static int GetTeamIndex(GameObject unit)
    {
        return unit != null && unit.TryGetComponent(out Unit identity) ? identity.TeamIndex : -1;
    }

    public static string GetTeamName(int teamIndex)
    {
        return teamIndex >= 0 && teamIndex < teamNames.Count ? teamNames[teamIndex] : null;
    }

    public static int GetEnemyTeamIndex(int teamIndex)
    {
        return teamIndex switch
        {
            HostTeamIndex => OpponentTeamIndex,
            OpponentTeamIndex => HostTeamIndex,
            _ => -1,
        };
    }

    public static int teamSize(string team)
    {
        return GameObject.FindGameObjectsWithTag(team).Length;
    }

    public static int teamSize(int teamIndex)
    {
        string team = GetTeamName(teamIndex);
        return team == null ? 0 : teamSize(team);
    }

    public static bool HasLivingTeamUnits(int teamIndex)
    {
        return GetTeamUnits(teamIndex).Any(IsLivingUnit);
    }

    public static Vector3 gridCoordToWorld(Vector2Int coords)
    {
        return new Vector3(
            gridBounds.xMin + coords.x * cellSize,
            0,
            gridBounds.yMin + coords.y * cellSize
        );
    }
}
