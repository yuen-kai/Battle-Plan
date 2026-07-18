using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum MessagePerspective
{
    Neutral,
    Friendly,
    Enemy,
}

public class GameLoop : NetworkBehaviour
{
    public const bool TESTING = true;

    // === DEV / TESTING MODE (no-mouse input, fast-forward, self-run) ===
    // Master switch for ALL dev tooling, including whether the main editor auto-hosts on Play
    // (see DevAutoHost in DevInput.cs). There is deliberately only ONE flag so auto-host can never
    // drift out of sync with the rest of dev mode — when this is false, multiplayer starts exactly
    // like the original manual host/join flow, unaffected by any of this.
    //
    // Defaults to OFF (opt-in), independent of TESTING (a separate, pre-existing flag for the
    // streamlined test flow and shorter planning timer). devMode alone selects the compact test
    // spawn layout; normal matches start at opposite ends of the board. Runtime-toggleable from
    // DevInput for live control from the editor/MCP.
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
    public Dictionary<ulong, HashSet<GameObject>> dodgeAlerted; // client -> alerted units (null outside dodge phase)
    private PathsDict dodgeDivePaths;
    private int dodgeResponsesReceived;
    private int maxDiveRangeThisRound;
    private int runningAbilities;

    // Client-side telegraph visuals spawned by ShowAbilityTelegraphClientRpc.
    private readonly List<GameObject> clientTelegraphs = new();

    public UnitDatabase allUnits;

    // UI Components
    [SerializeField]
    private TMP_Text overlayUIText;
    public List<Color> teamColors;

    public Color executingMoves;
    public List<Material> teamMaterials;

    public GameObject unitCards;

    //Teams
    public static List<string> teamNames = new() { "BlueTeam", "RedTeam" };
    public static Dictionary<ulong, int[]> allTeamUnits = new();
    public static Dictionary<ulong, GameObject[]> allTeamUnitObjects = new();

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

    List<HashSet<Vector2Int>> spawns = !devMode
        ? new List<HashSet<Vector2Int>>()
        {
            new() //Blue Team Spawn Positions
            {
                new Vector2Int(0, 0),
                new Vector2Int(4, 0),
                new Vector2Int(8, 0),
            },
            new() //Red Team Spawn Positions
            {
                new Vector2Int(8, 9),
                new Vector2Int(4, 9),
                new Vector2Int(0, 9),
            },
        }
        : new List<HashSet<Vector2Int>>()
        {
            new() //Blue Team Spawn Positions
            {
                new Vector2Int(1, 3),
                new Vector2Int(4, 5),
                new Vector2Int(7, 3),
            },
            new() //Red Team Spawn Positions
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
    float planningTimePerUnit = TESTING ? 3f : 5f;

    // Actions
    public static System.Action<bool> OrderAllowShooting;
    public static System.Action<bool> OrderStillShooting;
    public static System.Action OrderContinueShooting;

    List<PathsDict> pathsList = new();

    public GameObject teamCameraParent;
    public Camera TeamCamera;

    //End Game UI
    public GameObject endGameUI;
    public GameObject endGameStatusText;
    public GameObject playAgainButton;
    public GameObject mainMenuButton;
    private List<ulong> playAgain = new();

    // === FOG OF WAR ===
    [Header("Fog of War")]
    [SerializeField]
    [Tooltip("Whether matches start with fog of war enabled. The host can change it at runtime.")]
    private bool fogOfWarEnabledByDefault = true;

    [SerializeField]
    private GameObject fogOverlayCellPrefab;

    private const float FogUpdateIntervalSeconds = 0.15f;
    private static readonly int FogEdgeMaskId = Shader.PropertyToID("_EdgeMask");

    // Match-wide and server-authored so enemy NetworkHide state and every client's overlay
    // always switch together. This lives on the always-visible GameLoop scene object.
    private NetworkVariable<bool> fogOfWarEnabled = new(true);

    // Server: per-(unit, viewer client) temporary visibility overrides (target lock, Area Lock).
    private readonly Dictionary<(GameObject unit, ulong clientId), double>
        forceRevealUntil = new();
    private readonly Dictionary<GameObject, Vector2Int> lastFogCells = new();
    private bool serverFogDirty = true;
    private Coroutine serverFogCoroutine;

    // Client: pooled 90-tile dark overlay computed purely from local-owned units.
    private readonly Dictionary<Vector2Int, Renderer> fogOverlayTiles = new();
    private MaterialPropertyBlock fogOverlayProperties;
    private GameObject fogOverlayRoot;
    private Coroutine clientFogCoroutine;

    public bool FogOfWarEnabled => fogOfWarEnabled.Value;


    public static GameLoop Instance { get; private set; }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        TeamCamera = teamCameraParent.GetComponent<Camera>();
        fogOfWarEnabled.OnValueChanged += OnFogOfWarEnabledChanged;

        if (IsServer)
            fogOfWarEnabled.Value = fogOfWarEnabledByDefault;

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
        fogOfWarEnabled.OnValueChanged -= OnFogOfWarEnabledChanged;
        StopServerFog(false);
        forceRevealUntil.Clear();
        StopClientFog();

        if (Instance == this)
            Instance = null;

        base.OnNetworkDespawn();
    }

    private void InitializeCameraPosition()
    {
        // For each client in allTeamUnits, initialize the client by calling InitializeCameraPositionClientRpc with their team index
        for (int i = 0; i < allTeamUnits.Count; i++)
        {
            ulong clientId = allTeamUnits.ElementAt(i).Key;
            InitializeCameraPositionClientRpc(i, NetworkHelper.ToClient(clientId));
        }
    }

    [ClientRpc]
    private void InitializeCameraPositionClientRpc(int teamIndex, ClientRpcParams clientRpcParams = default)
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

        //Setup teams
        for (int i = 0; i < teamNames.Count; i++)
        {
            SetupUnitsAndCards(
                allTeamUnits.ElementAt(i).Key,
                allTeamUnits.ElementAt(i).Value,
                spawns[i],
                Quaternion.Euler(0, 180 * i, 0),
                teamNames[i]
            );
        }
        // yield return new WaitForSeconds(1f);
    }

    void SetupUnitsAndCards(
        ulong clientId,
        int[] teamUnits,
        HashSet<Vector2Int> spawnPositions,
        Quaternion rotation,
        string team
    )
    {
        allTeamUnitObjects[clientId] = new GameObject[teamUnits.Length];
        for (int i = 0; i < teamUnits.Length; i++)
        {
            GameObject unit = NetworkHelper.Spawn(
                allUnits.units[teamUnits[i]].unitModel,
                gridCoordToWorld(spawnPositions.ElementAt(i)),
                rotation,
                clientId
            );
            allTeamUnitObjects[clientId][i] = unit;
            Vector3 heightOffset = Helper.heightOffset(unit.transform);
            unit.transform.position += heightOffset;
            NetworkHelper.SyncHeightAdjustedPositionStatic(unit, unit.transform.position);

            unit.tag = team;
            SetGroupLayerGlobal(unit, LayerMask.NameToLayer(team));

            SetUnitCardClientRpc(i, teamUnits[i], NetworkHelper.ToClient(clientId));
        }
    }

    [ClientRpc]
    void SetUnitCardClientRpc(int cardIndex, int unitIndex, ClientRpcParams clientRpcParams = default)
    {
        CardHandler card = unitCards.transform.GetChild(cardIndex).GetComponent<CardHandler>();
        UnitData unitData = allUnits.units[unitIndex];
        card.uses = unitData.uses;
        card.setImage(unitData.abilitySprite);
        bool hasAbility =
            unitData.unitModel != null && unitData.unitModel.GetComponent<Ability>() != null;
        card.setText(hasAbility ? unitData.abilityName : "MOVE ONLY");
        card.ConfigurePlanningControls(
            () => PlanMovement.Instance.SelectUnit(cardIndex),
            () => PlanMovement.Instance.SetSelectionMode(cardIndex, false),
            () => PlanMovement.Instance.SetSelectionMode(cardIndex, true),
            hasAbility
        );
    }

    IEnumerator GameLoopTemp()
    {
        if (!IsServer)
            yield break;
        yield return null;

        while (teamNames.All(team => teamSize(team) > 0))
        {
            pathsList = new List<PathsDict>();
            devEndPlanningNow = false;
            currentPhase = "planning";

            // Fast-forward the whole simulation (movement/shooting/physics) in dev mode.
            Time.timeScale = devMode ? Mathf.Max(0.01f, devSpeedMultiplier) : 1f;

            unitCards.GetComponent<UnitCardContainer>().SetUnitCardsInteractable(true);
            OrderStillShooting?.Invoke(true);

            float timerLength = planningTimePerUnit * teamNames.Max(teamSize);
            double startTime = NetworkManager.Singleton.ServerTime.Time;
            double endTime = startTime + timerLength;

            CameraEffects.Instance?.FlashClientRpc(MessagePerspective.Friendly);
            setOverlayUITextClientRpc($"Planning", MessagePerspective.Friendly);

            if (devMode)
            {
                // Agent-driven planning: DevInput submits plans server-side, so the client mouse
                // planning coroutine is skipped. The agent drives input via execute_code, which
                // takes real seconds, so planning must NOT expire on a wall-clock timer — wait
                // indefinitely for an explicit DevInput.SubmitPlans()/EndPlanning(). Plans queued
                // before this round began (during the prior execution) end planning immediately.
                if (devSubmittedPaths != null)
                    devEndPlanningNow = true;

                while (!devEndPlanningNow && devMode)
                    yield return null;
            }
            else
            {
                StartPlanningClientRpc(endTime);

                while (
                    pathsList.Count < teamNames.Count
                    && NetworkManager.Singleton.ServerTime.Time < endTime + 1
                )
                {
                    yield return null;
                }
            }

            lastPlanningSeconds = (float)(NetworkManager.Singleton.ServerTime.Time - startTime);


            CameraEffects.Instance?.FlashClientRpc(MessagePerspective.Neutral);
            unitCards.GetComponent<UnitCardContainer>().SetUnitCardsInteractable(false);

            // Flatten pathsList into a single PathsDict
            PathsDict paths = new();
            foreach (PathsDict teamPaths in pathsList)
            {
                foreach (var kvp in teamPaths)
                {
                    paths[kvp.Key] = kvp.Value;
                }
            }

            // Dev-submitted plans (any team, no mouse) take priority and drive execution.
            if (devMode && devSubmittedPaths != null)
            {
                foreach (var kvp in devSubmittedPaths)
                {
                    paths[kvp.Key] = kvp.Value;
                }
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

            currentPhase = "executing";
            setOverlayUITextClientRpc("Executing Moves", MessagePerspective.Neutral);

            ExecuteMoves(paths);

            // Fire abilities alongside movement; each ability handles its own pauses/transitions.
            foreach (var activation in activations)
            {
                StartCoroutine(RunAbility(activation.unit, activation.square, activation.data));
            }

            while (CheckStillShooting() || runningAbilities > 0)
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

        currentPhase = "idle";
        EndGame();
    }

    /// <summary>
    /// Extracts valid ability activations from sanitized plans: entries flagged (Item1) whose
    /// path is [startCell, targetSquare] ([startCell] for self-targeted abilities) on a living
    /// unit that actually has an Ability component.
    /// </summary>
    List<(GameObject unit, Vector3 square, UnitData data)> CollectAbilityActivations(PathsDict paths)
    {
        List<(GameObject, Vector3, UnitData)> activations = new();
        foreach (var kvp in paths)
        {
            GameObject unit = kvp.Key;
            if (unit == null || !unit.activeInHierarchy || !kvp.Value.Item1)
                continue;
            if (unit.GetComponent<Ability>() == null)
                continue;

            var movement = unit.GetComponent<Movement>();
            if (movement == null || movement.unitData == null)
                continue;

            UnitData data = movement.unitData;
            List<Vector3> plan = kvp.Value.Item2;
            // Self-targeted abilities (Shield) target the unit's own cell.
            Vector3 square = !data.selectAbilitySquare || plan.Count < 2 ? plan[0] : plan[^1];
            activations.Add((unit, square, data));
        }
        return activations;
    }

    IEnumerator RunAbility(GameObject unit, Vector3 square, UnitData data)
    {
        if (unit == null || !unit.activeInHierarchy)
            yield break;
        runningAbilities++;
        yield return StartCoroutine(
            unit.GetComponent<Ability>().ExecuteAbility(square, data.abilityRadius)
        );
        runningAbilities--;
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
            // Fog: only line telegraphs render the caster position. For non-line abilities,
            // don't put a possibly-hidden caster's cell on the wire (RPC payloads reach the
            // enemy client even though the marker branch never reads casterPos).
            Vector3 telegraphOrigin = data.responseDistLine ? unit.transform.position : square;
            ShowAbilityTelegraphClientRpc(
                telegraphOrigin,
                square,
                data.abilityRadius,
                data.responseDistLine
            );
        }

        // Compute alerted units per client and the widest dive allowance this round.
        dodgeAlerted = new Dictionary<ulong, HashSet<GameObject>>();
        maxDiveRangeThisRound = 0;
        foreach (var (unit, square, data) in activations)
        {
            string enemyTeam = GetEnemyTeam(unit.tag);
            if (enemyTeam == null || data.responseRange <= 0f)
                continue;

            List<GameObject> threatened = data.responseDistLine
                ? GetUnitsInRangeOfLine(unit.transform.position, square, enemyTeam, data.responseRange)
                : Helper.GetObjectsInRange(square, enemyTeam, data.responseRange);

            if (threatened.Count == 0)
                continue;

            maxDiveRangeThisRound = Mathf.Max(maxDiveRangeThisRound, data.diveRange);
            ulong enemyClient = GetClient(enemyTeam);
            if (!dodgeAlerted.TryGetValue(enemyClient, out var set))
                dodgeAlerted[enemyClient] = set = new HashSet<GameObject>();
            foreach (GameObject t in threatened)
            {
                if (t != null && t.activeInHierarchy)
                    set.Add(t);
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
        dodgeResponsesReceived = 0;
        devDodgeSubmitted = false;

        setOverlayUITextClientRpc("Dodging", MessagePerspective.Enemy);

        if (devMode)
        {
            // Same model as dev planning: wait indefinitely for the agent's explicit submit so
            // it is never cut off mid-thought. DevInput.SubmitDodge() applies queued dive paths.
            while (!devDodgeSubmitted && devMode)
                yield return null;
        }
        else
        {
            int mostAlerted = dodgeAlerted.Values.Max(set => set.Count);
            float window = activations[0].data.timeDivePerUnit * mostAlerted;
            double endTime = NetworkManager.Singleton.ServerTime.Time + window;

            foreach (var kvp in dodgeAlerted)
            {
                NetworkObjectReference[] refs = kvp.Value
                    .Select(u => (NetworkObjectReference)u.GetComponent<NetworkObject>())
                    .ToArray();
                StartDodgePlanningClientRpc(
                    endTime,
                    refs,
                    maxDiveRangeThisRound,
                    NetworkHelper.ToClient(kvp.Key)
                );
            }

            while (
                dodgeResponsesReceived < dodgeAlerted.Count
                && NetworkManager.Singleton.ServerTime.Time < endTime + 1
            )
            {
                yield return null;
            }
        }

        // Apply dives: replace the dodger's planned move and cancel its own ability plan.
        foreach (var kvp in dodgeDivePaths)
        {
            if (kvp.Value.Item2 == null || kvp.Value.Item2.Count <= 1)
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

    void SetDodgeAlerts(bool active)
    {
        if (dodgeAlerted == null)
            return;
        foreach (var set in dodgeAlerted.Values)
        {
            foreach (GameObject unit in set)
            {
                var netObj = unit != null ? unit.GetComponent<NetworkObject>() : null;
                if (netObj != null)
                    NetworkHelper.Instance.SetActiveClientRpc(netObj, "UnitCanvas/Alert", active);
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
        if (dodgeAlerted == null || !dodgeAlerted.TryGetValue(sender, out var allowed))
            return;

        PathsDict sanitized = SanitizePaths(paths, sender, maxDiveRangeThisRound);
        foreach (var kvp in sanitized)
        {
            if (allowed.Contains(kvp.Key))
                dodgeDivePaths[kvp.Key] = kvp.Value;
        }
        dodgeResponsesReceived++;
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

        HashSet<GameObject> allAlerted = new();
        foreach (var set in dodgeAlerted.Values)
            allAlerted.UnionWith(set);

        PathsDict sanitized = SanitizePaths(rawPaths ?? new PathsDict(), null, maxDiveRangeThisRound);
        foreach (var kvp in sanitized)
        {
            if (allAlerted.Contains(kvp.Key))
                dodgeDivePaths[kvp.Key] = kvp.Value;
        }
        devDodgeSubmitted = true;
        Debug.Log($"[GameLoop] Dev dodge submitted for {dodgeDivePaths.Count} unit(s); ending dodge window.");
    }

    ulong GetClient(string team)
    {
        return allTeamUnits.ElementAt(teamNames.IndexOf(team)).Key;
    }

    /// <summary>Enemies within `range` cells of the caster→square line (AreaLock-style threats).</summary>
    List<GameObject> GetUnitsInRangeOfLine(Vector3 casterPos, Vector3 square, string team, float range)
    {
        List<GameObject> unitsInRange = new();
        Vector3 direction = (square - casterPos).normalized;
        if (direction == Vector3.zero)
            return unitsInRange;

        Vector3 end = square + direction * 50f;
        if (Physics.Raycast(casterPos, direction, out RaycastHit hit, Mathf.Infinity, LayerMask.GetMask("Walls")))
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
    void ShowAbilityTelegraphClientRpc(Vector3 casterPos, Vector3 square, float radiusCells, bool line)
    {
        if (line)
        {
            Vector3 direction = (square - casterPos).normalized;
            if (direction == Vector3.zero)
                return;
            Vector3 end = square + direction * 50f;
            if (Physics.Raycast(casterPos, direction, out RaycastHit hit, Mathf.Infinity, LayerMask.GetMask("Walls")))
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

        foreach (GameObject stale in lastFogCells.Keys.Where(unit => !livingUnits.Contains(unit)).ToList())
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
        foreach (ulong viewerClientId in allTeamUnits.Keys.ToList())
            UpdateUnitVisibilityForClient(viewerClientId);
    }

    private void UpdateUnitVisibilityForClient(ulong viewerClientId)
    {
        if (
            NetworkManager.Singleton == null
            || !NetworkManager.Singleton.ConnectedClients.ContainsKey(viewerClientId)
        )
        {
            return;
        }

        HashSet<Vector2Int> visibleCells = ComputeServerVisibleCells(viewerClientId);

        foreach (var targetTeam in allTeamUnitObjects)
        {
            if (targetTeam.Key == viewerClientId || targetTeam.Value == null)
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
                        )
                        || IsForceRevealed(unit, viewerClientId)
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

    private HashSet<Vector2Int> ComputeServerVisibleCells(ulong viewerClientId)
    {
        List<(Vector2Int cell, int range)> viewers = new();
        if (!allTeamUnitObjects.TryGetValue(viewerClientId, out GameObject[] units) || units == null)
            return new HashSet<Vector2Int>();

        foreach (GameObject unit in units)
        {
            if (!IsLivingUnit(unit))
                continue;

            Movement movement = unit.GetComponent<Movement>();
            if (movement?.unitData == null)
                continue;

            viewers.Add((
                GridSystem.ConvertToGridCoords(unit.transform.position),
                movement.unitData.visionRange
            ));
        }

        return GridSystem.ComputeVisibleCells(viewers);
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

        NetworkObject netObj = unit.GetComponent<NetworkObject>();
        if (netObj == null)
            return;

        foreach (ulong clientId in allTeamUnits.Keys.ToList())
        {
            if (clientId != netObj.OwnerClientId)
                ForceReveal(unit, clientId, seconds);
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
        foreach (ulong viewerClientId in allTeamUnits.Keys.ToList())
        {
            if (
                NetworkManager.Singleton == null
                || !NetworkManager.Singleton.ConnectedClients.ContainsKey(viewerClientId)
            )
            {
                continue;
            }

            foreach (var targetTeam in allTeamUnitObjects)
            {
                if (targetTeam.Key == viewerClientId || targetTeam.Value == null)
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

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        List<(Vector2Int cell, int range)> viewers = new();

        foreach (NetworkObject netObj in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (
                netObj == null
                || netObj.OwnerClientId != localClientId
                || netObj.GetComponent<Unit>() == null
            )
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

            viewers.Add((
                GridSystem.ConvertToGridCoords(unit.transform.position),
                movement.unitData.visionRange
            ));
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
            fogOverlayProperties.SetVector(
                FogEdgeMaskId,
                GetFogEdgeMask(tile.Key, visibleCells)
            );
            tileRenderer.SetPropertyBlock(fogOverlayProperties);
        }
    }

    private static Vector4 GetFogEdgeMask(
        Vector2Int cell,
        HashSet<Vector2Int> visibleCells
    )
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
        Time.timeScale = 1f;
        SetFogOfWarEnabled(false);
        forceRevealUntil.Clear();

        ulong? winner = getWinner();
        bool hasWinner = winner.HasValue;
        ulong winnerValue = winner.GetValueOrDefault();
        EndGameClientRpc(hasWinner, winnerValue);
    }

    [ClientRpc]
    void EndGameClientRpc(bool hasWinner, ulong winnerValue)
    {
        endGameStatusText.GetComponent<TMP_Text>().text = !hasWinner
            ? "No winner"
            : (winnerValue == NetworkManager.Singleton.LocalClientId ? "You win!" : "You lose!");
        playAgainButton.GetComponent<Button>().onClick.AddListener(() => PlayAgain());
        mainMenuButton
            .GetComponent<Button>()
            .onClick.AddListener(() => ExitToMainMenu());
        endGameUI.SetActive(true);
    }

    void PlayAgain()
    {
        playAgainButton.GetComponent<Button>().interactable = false;
        mainMenuButton.GetComponent<Button>().interactable = false;
        PlayAgainServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    void PlayAgainServerRpc(ServerRpcParams rpcParams = default)
    {
        playAgain.Add(rpcParams.Receive.SenderClientId);
        if (playAgain.Count == allTeamUnits.Count)
        {
            playAgain.Clear();
            NetworkHelper.CleanupAllNetworkObjects();
            NetworkManager.Singleton.SceneManager.LoadScene("HomeScreen", LoadSceneMode.Single);
        }
    }

    void ExitToMainMenu()
    {
        playAgainButton.GetComponent<Button>().interactable = false;
        mainMenuButton.GetComponent<Button>().interactable = false;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            ExitServerRpc(NetworkManager.Singleton.LocalClientId);
        }
        SceneManager.LoadScene("Title Screen");
    }

    [ServerRpc(RequireOwnership = false)]
    void ExitServerRpc(ulong id)
    {
        DisablePlayAgainButtonClientRpc();
        if (id == NetworkManager.ServerClientId)
        {
            NetworkHelper.CleanupAllNetworkObjects();
            NetworkManager.Singleton.Shutdown();
        }
        else
        {
            NetworkManager.Singleton.DisconnectClient(id);
        }
    }

    [ClientRpc]
    void DisablePlayAgainButtonClientRpc()
    {
        playAgainButton.GetComponent<Button>().interactable = false;
        mainMenuButton.GetComponent<Button>().interactable = true;
    }

    ulong? getWinner()
    {
        foreach (var team in teamNames)
        {
            if (teamSize(team) > 0)
            {
                return allTeamUnits.ElementAt(teamNames.IndexOf(team)).Key;
            }
        }
        return null;
    }

    [ClientRpc]
    void StartPlanningClientRpc(double endTime)
    {
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

        // Validate and sanitize client-submitted paths on the server, requiring ownership.
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        pathsList.Add(SanitizePaths(paths, senderClientId));
    }

    /// <summary>
    /// Validates and snaps a set of submitted unit paths to legal grid moves (adjacency, walls,
    /// move-distance, no-repeat, in-bounds). When <paramref name="ownerFilter"/> has a value the
    /// unit must be owned by that client (normal play); pass null for dev input to allow any team.
    /// Plans flagged as abilities (Item1) are validated as [startCell, targetSquare] instead:
    /// target within abilitySquareRange (Manhattan), in bounds, and not a wall for non-line
    /// abilities. Invalid ability plans degrade to a stay-put movement plan.
    /// <paramref name="maxStepsOverride"/> caps movement length (used for dodge dives).
    /// </summary>
    PathsDict SanitizePaths(PathsDict paths, ulong? ownerFilter, int maxStepsOverride = -1)
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

            // Ensure the sender owns this unit (skipped for dev input)
            if (ownerFilter.HasValue && netObj.OwnerClientId != ownerFilter.Value)
                continue;

            var movement = unit.GetComponent<Movement>();
            if (movement == null || movement.unitData == null)
                continue;

            // Ability plan: (true, [startCell, targetSquare]).
            if (kvp.Value.Item1)
            {
                sanitized[unit] = SanitizeAbilityPlan(unit, movement.unitData, kvp.Value.Item2);
                continue;
            }

            int maxSteps = maxStepsOverride >= 0
                ? maxStepsOverride
                : Mathf.Max(0, movement.unitData.moveDist);
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
    (bool, List<Vector3>) SanitizeAbilityPlan(GameObject unit, UnitData data, List<Vector3> submitted)
    {
        Vector3 start = GridSystem.GetNearestGridCell(unit);

        if (unit.GetComponent<Ability>() == null)
            return (false, new List<Vector3> { start });

        // Self-targeted abilities (e.g. Shield) need no square.
        if (!data.selectAbilitySquare)
            return (true, new List<Vector3> { start });

        if (submitted == null || submitted.Count < 2)
            return (false, new List<Vector3> { start });

        Vector3 square = GridSystem.GetNearestGridCell(submitted[^1]);

        bool inBounds = gridBounds.Contains(new Vector2(square.x, square.z));
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
    /// DEV/TESTING: server-side entry point for programmatic (no-mouse) plan submission. Accepts
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

        PathsDict sanitized = SanitizePaths(rawPaths ?? new PathsDict(), null);
        // Auto-fill every other living unit with a stay-put plan so ALL teams are covered at once
        // and the planning phase never waits on a team the agent didn't explicitly set.
        DevFillStationaryForAllTeams(sanitized);
        devSubmittedPaths = sanitized;
        devEndPlanningNow = true;
        Debug.Log($"[GameLoop] Dev plans submitted for {devSubmittedPaths.Count} unit(s); ending planning immediately.");
    }

    /// <summary>DEV: gives every living unit missing a plan a stay-put (current cell only) plan.</summary>
    void DevFillStationaryForAllTeams(PathsDict dict)
    {
        foreach (var kvp in allTeamUnitObjects)
        {
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
                unit.GetComponent<Movement>().StartMovement(movementPath, diveUnitsThisRound.Contains(unit));
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
        overlayUIText.text = message;

        if (perspective == MessagePerspective.Friendly)
        {
            overlayUIText.color = new Color(0.94f, 0.7f, 0.2f, 1f);
        }
        else if (perspective == MessagePerspective.Enemy)
        {
            overlayUIText.color = new Color(1f, 0.3f, 0.26f, 1f);
        }
        else
        {
            overlayUIText.color = new Color(0.88f, 0.9f, 0.92f, 1f);
        }
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
        if (!teamNames.Contains(team))
        {
            Debug.LogWarning(
                $"[GameLoop] Team '{team}' not found in teams list, cannot determine enemy team, falling back to null"
            );
            return null;
        }
        return teamNames.FirstOrDefault(t => t != team);
    }

    public static int teamSize(string team)
    {
        return GameObject.FindGameObjectsWithTag(team).Length;
    }

    public static Vector3 gridCoordToWorld(Vector2Int coords)
    {
        return new Vector3(
            gridBounds.xMin + coords.x * cellSize,
            0,
            gridBounds.yMin + coords.y * cellSize
        );
    }

    // [ClientRpc]
    // public void setOverlayUITextClientRpc(string message, string team = "neutral")
    // {
    //     overlayUIText.text = message;

    //     if (team == "neutral")
    //     {
    //         overlayUIText.color = executingMoves;
    //     }
    //     else
    //     {
    //         // Determine if this message is about the client's own team or enemy team
    //         ulong localClientId = NetworkManager.Singleton.LocalClientId;
    //         string localTeam = GetLocalClientTeam(localClientId);

    //         if (localTeam == team)
    //         {
    //             overlayUIText.color = teamColors[0];
    //         }
    //         else
    //         {
    //             overlayUIText.color = teamColors[1];
    //         }
    //     }
    // }

    /// <summary>
    /// Gets the perspective for a specific team relative to the local client
    /// </summary>
    // public MessagePerspective GetTeamPerspective(string team)
    // {
    //     ulong localClientId = NetworkManager.Singleton.LocalClientId;
    //     string localTeam = getTeamName(localClientId);

    //     if (team == localTeam)
    //         return MessagePerspective.Friendly;
    //     else if (team == "neutral" || team == "")
    //         return MessagePerspective.Neutral;
    //     else
    //         return MessagePerspective.Enemy;
    // }

    // public string getTeamName(ulong clientId)
    // {
    //     var entry = allTeamUnits.Keys
    //         .Select((key, idx) => new { key, idx })
    //         .FirstOrDefault(pair => pair.key == clientId);
    //     return entry != null ? teamNames[entry.idx] : null;
    // }

    // public static int GetTeamIndex(string team)
    // {
    //     if (!teamNames.Contains(team))
    //         return -1;
    //     return teamNames.IndexOf(team);
    // }
}
