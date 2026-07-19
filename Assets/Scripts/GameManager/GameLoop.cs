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

    // Client-side telegraph visuals spawned by ShowAbilityTelegraphClientRpc.
    private readonly List<GameObject> clientTelegraphs = new();

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

    public static readonly int[] DefaultBotRoster = { 2, 3, 4 };
    public static readonly int[] DevHostRoster = { 0, 1, 2 };
    public static readonly int[] DevOpponentRoster = { 2, 3, 4 };
    public static readonly int[] DevBotHostRoster = { 3, 4, 2 };

    public static List<string> teamNames = new() { "BlueTeam", "RedTeam" };

    // Runtime unit objects are indexed by logical team, not participant/owner ID.
    public static Dictionary<int, GameObject[]> allTeamUnitObjects = new();
    private static readonly Dictionary<int, ulong> teamParticipants = new();
    private static readonly Dictionary<int, int[]> teamRosters = new();

    // Level Layout (col, row) from bottom left corner
    public static float cellSize = 2.7f;
    public static Rect gridBounds = new(
        new Vector2(0, 0),
        new Vector2(8, 9) * cellSize + new Vector2(0.1f, 0.1f)
    );

    [SerializeField]
    private GameObject wallPrefab;
    public static HashSet<Vector2Int> wallLayout = new()
    {
        new Vector2Int(3, 2),
        new Vector2Int(5, 2),
        new Vector2Int(2, 3),
        new Vector2Int(6, 3),
        new Vector2Int(0, 4),
        new Vector2Int(8, 4),
        new Vector2Int(3, 7),
        new Vector2Int(5, 7),
        new Vector2Int(2, 6),
        new Vector2Int(6, 6),
        new Vector2Int(0, 5),
        new Vector2Int(8, 5),
    };

    List<Vector2Int[]> spawns = !devMode
        ? new List<Vector2Int[]>()
        {
            new[] //Blue Team Spawn Positions
            {
                new Vector2Int(0, 0),
                new Vector2Int(4, 0),
                new Vector2Int(8, 0),
            },
            new[] //Red Team Spawn Positions
            {
                new Vector2Int(8, 9),
                new Vector2Int(4, 9),
                new Vector2Int(0, 9),
            },
        }
        : new List<Vector2Int[]>()
        {
            new[] //Blue Team Spawn Positions
            {
                new Vector2Int(1, 3),
                new Vector2Int(4, 5),
                new Vector2Int(7, 3),
            },
            new[] //Red Team Spawn Positions
            {
                new Vector2Int(5, 6),
                new Vector2Int(4, 6),
                new Vector2Int(3, 6),
            },
        };

    // Store camera positions and rotations as a list of (Vector3 position, Quaternion rotation) tuples
    private List<(Vector3 position, Quaternion rotation)> cameraPositions = new()
    {
        (new Vector3(11.93f, 24.4f, 3.4f), new Quaternion(0.59543306f, 0f, 0f, 0.8034049f)),
        (new Vector3(11.93f, 24.4f, 21.0f), new Quaternion(0f, 0.80340505f, -0.5954329f, 0f)),
    };

    // Game Settings
    const float planningTimePerUnit = 5f;

    // Actions
    public static System.Action<bool> OrderAllowShooting;
    public static System.Action<bool> OrderStillShooting;
    public static System.Action OrderContinueShooting;

    private readonly Dictionary<int, PathsDict> submittedTeamPaths = new();
    private BotPlayer botPlayer;
    private int roundNumber;

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

    // Server: per-(unit, viewer client) temporary visibility overrides (locks and abilities).
    private readonly Dictionary<(GameObject unit, ulong clientId), double> forceRevealUntil = new();
    private const float AbilityActivationRevealSeconds = 1.75f;
    private readonly Dictionary<GameObject, Vector2Int> lastFogCells = new();
    private bool serverFogDirty = true;
    private Coroutine serverFogCoroutine;

    // Client: pooled 90-tile dark overlay computed purely from local-owned units.
    private readonly Dictionary<Vector2Int, Renderer> fogOverlayTiles = new();
    private MaterialPropertyBlock fogOverlayProperties;
    private GameObject fogOverlayRoot;
    private Coroutine clientFogCoroutine;

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
        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        matchEnded = false;
        disconnectRecoveryStarted = false;

        if (IsServer)
        {
            EnsureTeamConfiguration();
            replicatedMatchOptions.Value = MatchOptions.Current.Sanitized();
            MatchOptions.SetCurrent(replicatedMatchOptions.Value);
            teamZeroParticipant.Value = GetConfiguredParticipantId(HostTeamIndex);
            teamOneParticipant.Value = GetConfiguredParticipantId(OpponentTeamIndex);
            fogOfWarEnabled.Value = replicatedMatchOptions.Value.fogOfWar;
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
        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        StopServerFog(false);
        forceRevealUntil.Clear();
        StopClientFog();

        if (Instance == this)
            Instance = null;

        base.OnNetworkDespawn();
    }

    private void OnMatchOptionsChanged(MatchOptions previousValue, MatchOptions newValue)
    {
        MatchOptions.SetCurrent(newValue);
        GameHUDController.Instance?.SetMatchSummary(newValue);
    }

    private void OnTeamParticipantChanged(ulong previousValue, ulong newValue)
    {
        Unit.RefreshAllTeamPresentation();
    }

    public static void ResetMatchState()
    {
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
        if (roster == null || roster.Length != 3)
            throw new System.ArgumentException("A team roster must contain exactly three units.");
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
        if (teamUnits == null || teamUnits.Length != 3)
            throw new System.InvalidOperationException($"Team {teamIndex} has an invalid roster.");

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

            Vector3 heightOffset = Helper.heightOffset(unit.transform);
            unit.transform.position += heightOffset;
            NetworkHelper.SyncHeightAdjustedPositionStatic(unit, unit.transform.position);

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
                    unitIdentity.RemainingAbilityUses,
                    NetworkHelper.ToClient(participantId)
                );
            }
        }
    }

    [ClientRpc]
    void SetUnitCardClientRpc(
        int cardIndex,
        int unitIndex,
        int remainingAbilityUses,
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
            remainingAbilityUses
        );
    }

    [ClientRpc]
    private void SetCardsInteractableClientRpc(bool interactable)
    {
        GameHUDController.Instance?.SetCardsInteractable(interactable);
    }

    public void DisableUnitCard(GameObject unit)
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

            SetCardDisabledClientRpc(unitIndexInTeam, NetworkHelper.ToClient(clientId));
            return;
        }
    }

    [ClientRpc]
    private void SetCardDisabledClientRpc(int unitIndex, ClientRpcParams clientRpcParams = default)
    {
        GameHUDController.Instance?.SetCardDisabled(unitIndex, true);
    }

    IEnumerator GameLoopTemp()
    {
        if (!IsServer)
            yield break;
        yield return null;

        while (!matchEnded && teamNames.All(team => teamSize(team) > 0))
        {
            roundNumber++;
            submittedTeamPaths.Clear();
            devEndPlanningNow = false;
            currentPhase = "planning";

            // Fast-forward the whole simulation (movement/shooting/physics) in dev mode.
            Time.timeScale = devMode ? Mathf.Max(0.01f, devSpeedMultiplier) : 1f;

            SetCardsInteractableClientRpc(true);
            OrderStillShooting?.Invoke(true);

            float timerLength = planningTimePerUnit * teamNames.Max(teamSize);
            double startTime = NetworkManager.Singleton.ServerTime.Time;
            double endTime = startTime + timerLength;

            CameraEffects.Instance?.FlashClientRpc(MessagePerspective.Friendly);
            setOverlayUITextClientRpc("Planning", MessagePerspective.Friendly);

            if (botPlayer != null)
            {
                PathsDict botPlans = botPlayer.CreatePlanningContribution(roundNumber);
                submittedTeamPaths[botPlayer.TeamIndex] = SanitizePaths(
                    botPlans,
                    botPlayer.TeamIndex
                );
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
                StartPlanningClientRpc(endTime);

                while (
                    submittedTeamPaths.Count < TeamCount
                    && !matchEnded
                    && NetworkManager.Singleton.ServerTime.Time < endTime + 1
                )
                {
                    yield return null;
                }
            }

            if (matchEnded)
                yield break;

            lastPlanningSeconds = (float)(NetworkManager.Singleton.ServerTime.Time - startTime);

            CameraEffects.Instance?.FlashClientRpc(MessagePerspective.Neutral);
            SetCardsInteractableClientRpc(false);

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
            activations = ConsumeAbilityUses(activations);

            currentPhase = "executing";
            setOverlayUITextClientRpc("Executing Moves", MessagePerspective.Neutral);

            ExecuteMoves(paths);

            // Fire abilities alongside movement; each ability handles its own pauses/transitions.
            foreach (var activation in activations)
            {
                StartCoroutine(RunAbility(activation.unit, activation.square, activation.data));
            }

            while (!matchEnded && (CheckStillShooting() || runningAbilities > 0))
            {
                //THINKING: ability activates such that theres movement/gameplay extension
                //If moving continues during this time restart checkStillMoving
                if (CheckStillMoving())
                {
                    OrderContinueShooting();
                    while (CheckStillMoving())
                    {
                        yield return null;
                    }
                    yield return null; // Wait a bit before stopping shooting
                }
                OrderAllowShooting(false);

                yield return null;
            }

            HideAbilityTelegraphsClientRpc();
        }

        if (matchEnded)
            yield break;

        currentPhase = "idle";
        EndGame();
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

    private List<(GameObject unit, Vector3 square, UnitData data)> ConsumeAbilityUses(
        IEnumerable<(GameObject unit, Vector3 square, UnitData data)> activations
    )
    {
        List<(GameObject unit, Vector3 square, UnitData data)> consumed = new();
        foreach (var activation in activations)
        {
            Unit identity = activation.unit != null ? activation.unit.GetComponent<Unit>() : null;
            if (identity == null || !identity.TryConsumeAbilityUse())
                continue;

            consumed.Add(activation);
            NotifyAbilityUsesChanged(activation.unit, identity.RemainingAbilityUses);
        }
        return consumed;
    }

    private void NotifyAbilityUsesChanged(GameObject unit, int remainingUses)
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

            SetCardAbilityUsesClientRpc(cardIndex, remainingUses, NetworkHelper.ToClient(clientId));
            return;
        }
    }

    [ClientRpc]
    private void SetCardAbilityUsesClientRpc(
        int cardIndex,
        int remainingUses,
        ClientRpcParams clientRpcParams = default
    )
    {
        GameHUDController.Instance?.SetCardAbilityUses(cardIndex, remainingUses);
    }

    IEnumerator RunAbility(GameObject unit, Vector3 square, UnitData data)
    {
        if (unit == null || !unit.activeInHierarchy)
            yield break;

        NetworkObject networkObject = unit.GetComponent<NetworkObject>();
        Unit identity = unit.GetComponent<Unit>();
        ForceRevealToEnemyClients(unit, AbilityActivationRevealSeconds);
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
        Color teamColor =
            teamIndex == HostTeamIndex ? new Color(0.22f, 0.78f, 1f) : new Color(1f, 0.23f, 0.33f);
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
                data.responseDistLine
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
            yield break; // Telegraphs stay up; nobody can dodge.
        }

        // Alert icons on threatened units, network-wide.
        SetDodgeAlerts(true);

        dodgeDivePaths = new PathsDict();
        dodgeResponsesReceived.Clear();
        devDodgeSubmitted = false;

        setOverlayUITextClientRpc("Dodging", MessagePerspective.Enemy);

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
                .StartPlanning(paths => SendDodgePathsToServerRpc(paths), endTime, units, diveRange)
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
        bool line
    )
    {
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
        else
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(marker.GetComponent<Collider>());
            marker.name = "AbilityTelegraphMarker";
            float diameter = Mathf.Max(1f, 2f * radiusCells * cellSize);
            marker.transform.position = square + new Vector3(0, 0.15f, 0);
            marker.transform.localScale = new Vector3(diameter, 0.05f, diameter);
            var rend = marker.GetComponent<Renderer>();
            rend.material = new Material(Shader.Find("Sprites/Default"));
            rend.material.color = new Color(1f, 0.5f, 0f, 0.5f);
            clientTelegraphs.Add(marker);
        }
    }

    [ClientRpc]
    void HideAbilityTelegraphsClientRpc()
    {
        foreach (GameObject telegraph in clientTelegraphs)
        {
            if (telegraph != null)
                Destroy(telegraph);
        }
        clientTelegraphs.Clear();
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

        return expired.Count > 0;
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

        return GridSystem.ComputeVisibleCells(viewers);
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

    public void ForceReveal(GameObject unit, ulong toClientId, float seconds)
    {
        if (
            !IsServer
            || unit == null
            || seconds <= 0f
            || NetworkManager.Singleton == null
            || IsBotParticipant(toClientId)
            || GetTeamIndexForClient(toClientId) < 0
            || !NetworkManager.Singleton.ConnectedClients.ContainsKey(toClientId)
        )
            return;

        double until = NetworkManager.Singleton.ServerTime.Time + seconds;
        var key = (unit, toClientId);
        if (!forceRevealUntil.TryGetValue(key, out double existing) || until > existing)
            forceRevealUntil[key] = until;

        serverFogDirty = true;

        // Keep the deadline while fog is disabled so re-enabling mid-lock/ability restores the
        // correct reveal state. No visibility work is needed while every unit is already shown.
        if (!FogOfWarEnabled)
            return;

        // Show immediately so state changes/RPCs queued after this call are not sent to a hidden
        // object. The regular pass still re-evaluates everyone on the next 0.15 s tick.
        UpdateUnitVisibilityForClient(toClientId);
    }

    public void ForceRevealToEnemyClients(GameObject unit, float seconds)
    {
        if (!IsServer || unit == null)
            return;

        Unit identity = unit.GetComponent<Unit>();
        if (identity == null)
            return;

        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            if (
                teamIndex != identity.TeamIndex
                && TryGetHumanClientId(teamIndex, out ulong clientId)
            )
            {
                ForceReveal(unit, clientId, seconds);
            }
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

        HashSet<Vector2Int> visibleCells = GridSystem.ComputeVisibleCells(viewers);
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
    }

    void EndGame()
    {
        if (matchEnded)
            return;

        int? winner = GetWinnerTeamIndex();
        FinishGame(winner.HasValue, winner.GetValueOrDefault(), string.Empty);
    }

    private void FinishGame(bool hasWinner, int winnerValue, string detail)
    {
        if (matchEnded)
            return;

        matchEnded = true;
        currentPhase = "idle";
        Time.timeScale = 1f;
        SetFogOfWarEnabled(false);
        forceRevealUntil.Clear();
        SetCardsInteractableClientRpc(false);
        HideAbilityTelegraphsClientRpc();
        EndGameClientRpc(hasWinner, winnerValue, detail ?? string.Empty);
    }

    [ClientRpc]
    void EndGameClientRpc(bool hasWinner, int winnerValue, string detail)
    {
        string status = !hasWinner
            ? "No winner"
            : (winnerValue == LocalTeamIndex ? "You win!" : "You lose!");
        if (!string.IsNullOrWhiteSpace(detail))
            status = $"{status} {detail}";

        GameHUDController.Instance?.ShowResults(status, PlayAgain, ExitToMainMenu);
    }

    void PlayAgain()
    {
        GameHUDController.Instance?.SetResultButtonsEnabled(false, false);
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
        FinishGame(winnerTeam >= 0, winnerTeam, "Opponent disconnected.");
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

    int? GetWinnerTeamIndex()
    {
        for (int teamIndex = 0; teamIndex < TeamCount; teamIndex++)
        {
            if (teamSize(teamIndex) > 0)
                return teamIndex;
        }
        return null;
    }

    [ClientRpc]
    void StartPlanningClientRpc(double endTime)
    {
        if (LocalTeamIndex < 0)
            return;

        StartCoroutine(
            transform
                .GetComponent<PlanMovement>()
                .StartPlanning(paths => SendPathsToServerRpc(paths), endTime)
        );
    }

    [ServerRpc(RequireOwnership = false)]
    void SendPathsToServerRpc(PathsDict paths, ServerRpcParams rpcParams = default)
    {
        // In dev mode DevInput drives all input; ignore mouse-driven client submissions so they
        // can't overwrite dev plans (this leaves normal gameplay untouched when devMode is off).
        if (devMode)
            return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        int senderTeamIndex = GetTeamIndexForClient(senderClientId);
        if (senderTeamIndex < 0 || IsBotTeam(senderTeamIndex))
            return;

        submittedTeamPaths[senderTeamIndex] = SanitizePaths(paths, senderTeamIndex);
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

        if (data.selectAbilityDirection)
        {
            Vector2Int startCell = GridSystem.ConvertToGridCoords(start);
            Vector2Int selectedCell = GridSystem.ConvertToGridCoords(square);
            bool validDirection =
                data.abilityFixedDistance > 0
                && GridSystem.TryGetAdjacentDirection(startCell, selectedCell, out _);
            return inBounds && validDirection
                ? (true, new List<Vector3> { start, square })
                : (false, new List<Vector3> { start });
        }

        float manhattanCells =
            (Mathf.Abs(square.x - start.x) + Mathf.Abs(square.z - start.z)) / cellSize;
        bool inRange = manhattanCells <= data.abilitySquareRange + 0.1f;
        // Line abilities (AreaLock) use the square only as a direction anchor; others land there.
        bool wallOk =
            data.responseDistLine || !wallLayout.Contains(GridSystem.ConvertToGridCoords(square));

        if (!inBounds || !inRange || !wallOk)
            return (false, new List<Vector3> { start });

        return (true, new List<Vector3> { start, square });
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
    public void setOverlayUITextClientRpc(string message, MessagePerspective perspective)
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

    public static Vector3 gridCoordToWorld(Vector2Int coords)
    {
        return new Vector3(
            gridBounds.xMin + coords.x * cellSize,
            0,
            gridBounds.yMin + coords.y * cellSize
        );
    }
}
