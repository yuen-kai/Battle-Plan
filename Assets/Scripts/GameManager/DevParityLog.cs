#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// DEV: records real rounds so the Python training simulator (Training/battlesim) can replay
/// them and be compared against the game. Each line of the output is one round: every living
/// unit's position, facing, health and executed path at the start, and its health, position and
/// weapon shots at the end. Written to Training/parity/ so it never touches Assets.
/// </summary>
public static class DevParityLog
{
    private const string AutoRunKey = "BattlePlan.ParityLog.AutoRun";

    [System.Serializable]
    private class UnitRecord
    {
        public int team;
        public int slot;
        public string name;
        public float x;
        public float y;
        public float facing;
        public float hp;
        public List<int> path = new();
        public bool ability;
        public int targetX = -1;
        public int targetY = -1;
        public bool dive;
        public int cooldown;
        public float xAfter;
        public float yAfter;
        public float facingAfter;
        public float hpAfter;
        public bool aliveAfter;
        public int shots;
    }

    [System.Serializable]
    private class RoundRecord
    {
        public string map;
        public int match;
        public int round;
        public bool fog;
        public float duration;
        public int frames;
        public List<int> smoke = new();
        public List<UnitRecord> units = new();
    }

    public static bool Recording;
    public static string OutputPath;
    public static int MatchIndex;
    public static int RoundsWritten { get; private set; }

    private static RoundRecord round;
    private static readonly Dictionary<GameObject, UnitRecord> tracked = new();
    private static float roundStartTime;
    private static int roundStartFrame;

    public static bool IsDone =>
        DevParityLogRunner.Instance != null && DevParityLogRunner.Instance.Done;

    public static string Status =>
        DevParityLogRunner.Instance == null
            ? "<parity log not started>"
            : DevParityLogRunner.Instance.Status;

    public static void Run(
        int matches = 15,
        float speed = 4f,
        int seed = 1,
        bool exitPlayWhenDone = true
    )
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[PARITY] Enter Play mode first.");
            return;
        }
        if (DevParityLogRunner.Instance != null)
        {
            if (!DevParityLogRunner.Instance.Done)
                return;
            Object.Destroy(DevParityLogRunner.Instance.gameObject);
        }

        GameObject runner = new("DevParityLogRunner");
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<DevParityLogRunner>().Configure(matches, speed, seed, exitPlayWhenDone);
    }

    [UnityEditor.MenuItem("Battle Plan/Record Simulator Parity Rounds")]
    public static void RunFromMenu()
    {
        if (Application.isPlaying)
        {
            Run();
            return;
        }
        UnityEditor.SessionState.SetBool(AutoRunKey, true);
        UnityEditor.EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoRunAfterEnterPlay()
    {
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            return;
        if (!UnityEditor.SessionState.GetBool(AutoRunKey, false))
            return;

        UnityEditor.SessionState.EraseBool(AutoRunKey);
        Run();
    }

    public static void BeginRound(GameLoop loop, PathsDict paths)
    {
        if (!Recording)
            return;

        round = new RoundRecord
        {
            map = MapCatalog.Active.Id.ToString(),
            match = MatchIndex,
            round = loop.RoundNumber,
            fog = loop.FogOfWarEnabled,
        };
        tracked.Clear();
        foreach (Vector2Int cell in loop.ActiveSmokeCells)
        {
            round.smoke.Add(cell.x);
            round.smoke.Add(cell.y);
        }
        for (int team = 0; team < GameLoop.TeamCount; team++)
        {
            foreach (GameObject unit in GameLoop.GetTeamUnits(team).Where(IsLiving))
            {
                UnitRecord record = new()
                {
                    team = team,
                    slot = unit.GetComponent<Unit>().RosterSlot,
                    name = unit.GetComponent<Movement>().unitData.name,
                    facing = Facing(unit),
                    hp = unit.GetComponent<Health>().CurrentHealth,
                    dive = loop.IsDivingThisRound(unit),
                    cooldown = unit.GetComponent<Unit>().AbilityCooldownRoundsRemaining,
                };
                (record.x, record.y) = BoardPosition(unit);
                bool planned = paths.TryGetValue(unit, out var plan) && plan.Item2 != null && plan.Item2.Count > 0;
                record.ability = planned && plan.Item1;
                if (record.ability)
                {
                    Vector2Int target = GridSystem.ConvertToGridCoords(plan.Item2[^1]);
                    (record.targetX, record.targetY) = (target.x, target.y);
                }
                List<Vector3> cells =
                    planned && !plan.Item1
                        ? plan.Item2
                        : new List<Vector3> { GridSystem.GetNearestGridCell(unit) };
                foreach (Vector3 cell in cells)
                {
                    Vector2Int grid = GridSystem.ConvertToGridCoords(cell);
                    record.path.Add(grid.x);
                    record.path.Add(grid.y);
                }
                round.units.Add(record);
                tracked[unit] = record;
            }
        }
        roundStartTime = Time.time;
        roundStartFrame = Time.frameCount;
    }

    public static void NoteShot(GameObject shooter)
    {
        if (Recording && shooter != null && tracked.TryGetValue(shooter, out UnitRecord record))
            record.shots++;
    }

    public static void EndRound()
    {
        if (!Recording || round == null)
            return;

        foreach (var (unit, record) in tracked)
        {
            if (unit == null)
                continue;
            (record.xAfter, record.yAfter) = BoardPosition(unit);
            record.facingAfter = Facing(unit);
            Health health = unit.GetComponent<Health>();
            record.hpAfter = health.CurrentHealth;
            record.aliveAfter = health.IsAlive;
        }
        round.duration = Time.time - roundStartTime;
        round.frames = Time.frameCount - roundStartFrame;

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        File.AppendAllText(OutputPath, JsonUtility.ToJson(round) + "\n");
        RoundsWritten++;
        round = null;
        tracked.Clear();
    }

    public static void ResetCounters() => RoundsWritten = 0;

    private static bool IsLiving(GameObject unit)
    {
        return unit != null
            && unit.activeInHierarchy
            && unit.GetComponent<Health>() is Health health
            && health.IsAlive;
    }

    private static (float, float) BoardPosition(GameObject unit)
    {
        Vector3 position = unit.transform.position;
        return (position.x / GameLoop.cellSize, position.z / GameLoop.cellSize);
    }

    private static float Facing(GameObject unit)
    {
        Vector3 forward = unit.transform.forward;
        return Mathf.Atan2(forward.z, forward.x);
    }
}

/// <summary>
/// DEV: plays solo bot matches back to back with random original-five crews across every map,
/// planning both crews itself (the bot's own orders, random legal walks and random legal ability
/// casts) and answering the host crew's dodge windows with random dives, while
/// <see cref="DevParityLog"/> records each round. Leaves Play mode when finished.
/// </summary>
public sealed class DevParityLogRunner : MonoBehaviour
{
    private const int MaxRoundsPerMatch = 30;
    private const float MatchTimeoutSeconds = 300f;
    private const float BotPathShare = 0.5f;
    private const float RandomAbilityShare = 0.3f;
    private const float DiveShare = 0.7f;
    private const int MaxDive = 2;
    private const int OriginalRosterSize = 5;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left,
    };

    public static DevParityLogRunner Instance { get; private set; }
    public bool Done { get; private set; }
    public string Status { get; private set; } = "starting";

    private int matches;
    private float speed;
    private bool exitPlayWhenDone;
    private System.Random rng;

    public void Configure(int matchCount, float simulationSpeed, int seed, bool exitWhenDone)
    {
        matches = matchCount;
        speed = simulationSpeed;
        rng = new System.Random(seed);
        exitPlayWhenDone = exitWhenDone;
    }

    private void Awake()
    {
        Instance = this;
    }

    private IEnumerator Start()
    {
        yield return null;
        string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
        DevParityLog.OutputPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Training", "parity", $"rounds-{stamp}.jsonl")
        );
        DevParityLog.ResetCounters();
        GameLoop.devUseProductionSpawns = true;
        GameLoop.devOverrideBotPlans = true;

        for (int match = 0; match < matches; match++)
        {
            yield return TearDownMatch();
            if (NetworkManager.Singleton == null)
            {
                Status = "network bootstrap scene never loaded";
                break;
            }

            MapDefinition map = MapCatalog.All[match % MapCatalog.All.Count];
            GameLoop.devHostRosterOverride = RandomRoster();
            GameLoop.devOpponentRosterOverride = RandomRoster();
            DevParityLog.MatchIndex = match;
            DevParityLog.Recording = true;
            DevInput.StartBotMatch(speed, true, map.Id);

            float readyDeadline = Time.realtimeSinceStartup + 90f;
            while (!MatchReady() && Time.realtimeSinceStartup < readyDeadline)
                yield return null;
            if (!MatchReady())
            {
                Debug.LogWarning($"[PARITY] match {match} on {map.DisplayName} never reached planning.");
                continue;
            }

            BotPlayer[] planners =
            {
                new(GameLoop.Instance, GameLoop.HostTeamIndex),
                new(GameLoop.Instance, GameLoop.OpponentTeamIndex),
            };
            int roundsPlanned = 0;
            int dodgedRound = -1;
            float matchDeadline = Time.realtimeSinceStartup + MatchTimeoutSeconds;
            while (
                GameLoop.Instance != null
                && GameLoop.currentPhase != GameLoop.Phase.Idle
                && Time.realtimeSinceStartup < matchDeadline
            )
            {
                if (GameLoop.currentPhase == GameLoop.Phase.Planning)
                {
                    if (roundsPlanned >= MaxRoundsPerMatch)
                        break;
                    GameLoop.Instance.DevSubmitPlansServer(
                        BuildPlans(planners, GameLoop.Instance.RoundNumber)
                    );
                    roundsPlanned++;
                    while (
                        GameLoop.currentPhase == GameLoop.Phase.Planning
                        && Time.realtimeSinceStartup < matchDeadline
                    )
                    {
                        yield return null;
                    }
                }
                if (
                    GameLoop.currentPhase == GameLoop.Phase.Dodging
                    && dodgedRound != GameLoop.Instance.RoundNumber
                )
                {
                    dodgedRound = GameLoop.Instance.RoundNumber;
                    GameLoop.Instance.DevSubmitDodgeServer(BuildDives());
                }
                yield return null;
            }

            Status =
                $"match {match + 1}/{matches} on {map.DisplayName}: {roundsPlanned} rounds, "
                + $"{DevParityLog.RoundsWritten} recorded in total";
            Debug.Log("[PARITY] " + Status);
        }

        DevParityLog.Recording = false;
        GameLoop.devUseProductionSpawns = false;
        GameLoop.devOverrideBotPlans = false;
        GameLoop.devHostRosterOverride = null;
        GameLoop.devOpponentRosterOverride = null;
        yield return TearDownMatch();
        Time.timeScale = 1f;

        Done = true;
        Status =
            $"RESULT: {DevParityLog.RoundsWritten} rounds from {matches} matches -> {DevParityLog.OutputPath}";
        Debug.Log("[PARITY] " + Status);
        if (exitPlayWhenDone)
            UnityEditor.EditorApplication.isPlaying = false;
    }

    private static bool MatchReady()
    {
        return NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsServer
            && GameLoop.Instance != null
            && GameLoop.currentPhase == GameLoop.Phase.Planning
            && GameLoop.GetTeamUnits(GameLoop.HostTeamIndex).Length == RosterRules.UnitsPerPlayer
            && GameLoop.GetTeamUnits(GameLoop.OpponentTeamIndex).Length
                == RosterRules.UnitsPerPlayer;
    }

    private int[] RandomRoster()
    {
        return Enumerable
            .Range(0, RosterRules.UnitsPerPlayer)
            .Select(_ => rng.Next(OriginalRosterSize))
            .ToArray();
    }

    private PathsDict BuildPlans(BotPlayer[] planners, int roundNumber)
    {
        PathsDict plans = new();
        foreach (BotPlayer planner in planners)
        {
            PathsDict botPlans = planner.CreatePlanningContribution(roundNumber);
            foreach (GameObject unit in GameLoop.GetTeamUnits(planner.TeamIndex))
            {
                if (unit == null || !unit.activeInHierarchy || !unit.GetComponent<Health>().IsAlive)
                    continue;
                Vector3 start = GridSystem.GetNearestGridCell(unit);
                bool hasBotPlan = botPlans.TryGetValue(unit, out var botPlan);
                if (
                    unit.GetComponent<Unit>().CanUseAbility
                    && rng.NextDouble() < RandomAbilityShare
                    && TryRandomAbilityTarget(unit, out Vector2Int target)
                )
                {
                    plans[unit] = (true, new List<Vector3> { start, GameLoop.gridCoordToWorld(target) });
                }
                else if (hasBotPlan && botPlan.Item1)
                {
                    plans[unit] = botPlan;
                }
                else
                {
                    bool useBotPath = hasBotPlan && rng.NextDouble() < BotPathShare;
                    plans[unit] = (false, useBotPath ? botPlan.Item2 : RandomPath(unit, MoveDistance(unit)));
                }
            }
        }
        return plans;
    }

    private PathsDict BuildDives()
    {
        PathsDict dives = new();
        if (
            GameLoop.Instance.dodgeAlerted == null
            || !GameLoop.Instance.dodgeAlerted.TryGetValue(GameLoop.HostTeamIndex, out var alerted)
        )
        {
            return dives;
        }
        foreach (GameObject unit in alerted)
        {
            if (unit != null && unit.activeInHierarchy && rng.NextDouble() < DiveShare)
                dives[unit] = (false, RandomPath(unit, MaxDive, minimumSteps: 1));
        }
        return dives;
    }

    private static int MoveDistance(GameObject unit) =>
        unit.GetComponent<Movement>().unitData.moveDist;

    private bool TryRandomAbilityTarget(GameObject unit, out Vector2Int target)
    {
        UnitData data = unit.GetComponent<Movement>().unitData;
        Vector2Int start = GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));
        bool smoke = unit.GetComponent<Smoke>() != null;
        List<Vector2Int> options = new();
        for (int x = 0; x < GridSystem.ColumnCount; x++)
        {
            for (int y = 0; y < GridSystem.RowCount; y++)
            {
                Vector2Int cell = new(x, y);
                if (smoke && !GridSystem.IsSquareFootprintInBounds(cell, Smoke.FootprintRadius))
                    continue;
                if (data.selectAbilityDirection)
                {
                    if (data.abilityFixedDistance > 0 && GridSystem.TryGetAdjacentDirection(start, cell, out _))
                        options.Add(cell);
                    continue;
                }
                if (Mathf.Abs(cell.x - start.x) + Mathf.Abs(cell.y - start.y) > data.abilitySquareRange)
                    continue;
                if (!data.responseDistLine && GameLoop.wallLayout.Contains(cell))
                    continue;
                if (data.responseDistLine && cell == start)
                    continue;
                options.Add(cell);
            }
        }
        target = options.Count > 0 ? options[rng.Next(options.Count)] : default;
        return options.Count > 0;
    }

    private List<Vector3> RandomPath(GameObject unit, int maxSteps, int minimumSteps = 0)
    {
        Vector2Int start = GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));
        List<Vector2Int> cells = new() { start };
        int steps = rng.Next(minimumSteps, maxSteps + 1);
        for (int step = 0; step < steps; step++)
        {
            List<Vector2Int> options = Directions
                .Select(direction => cells[^1] + direction)
                .Where(cell =>
                    GridSystem.IsCellInBounds(cell)
                    && !GameLoop.wallLayout.Contains(cell)
                    && !cells.Contains(cell)
                )
                .ToList();
            if (options.Count == 0)
                break;
            cells.Add(options[rng.Next(options.Count)]);
        }
        return cells.Select(GameLoop.gridCoordToWorld).ToList();
    }

    private IEnumerator TearDownMatch()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening)
        {
            if (manager.IsServer)
                NetworkHelper.CleanupAllNetworkObjects();
            manager.Shutdown();
            float shutdownDeadline = Time.realtimeSinceStartup + 10f;
            while (
                NetworkManager.Singleton != null
                && (
                    NetworkManager.Singleton.IsListening
                    || NetworkManager.Singleton.ShutdownInProgress
                )
                && Time.realtimeSinceStartup < shutdownDeadline
            )
            {
                yield return null;
            }
        }

        MatchOptions.Reset();
        GameLoop.ResetMatchState();
        SceneManager.LoadScene("JoinGame", LoadSceneMode.Single);
        yield return null;
        float sceneDeadline = Time.realtimeSinceStartup + 30f;
        while (NetworkManager.Singleton == null && Time.realtimeSinceStartup < sceneDeadline)
            yield return null;
    }
}
#endif
