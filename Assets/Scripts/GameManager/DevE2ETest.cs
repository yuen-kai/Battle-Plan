#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ONE-SHOT END-TO-END GAMEPLAY TEST for Battle-Plan. Re-runnable, self-asserting, editor-only.
///
/// What it covers (in order, one live 2-client match on the explicit dev spawn layout):
///   R0  match start via DevInput.StartMatch() (devMode is opt-in), roster + spawn verification
///   R1  legal multi-step movement (3-step and 5-step paths), wall-step rejection, moving units
///       on BOTH teams (unit switching), speed control (SetSpeed 30 during execution, back to 6)
///   R2  non-adjacent (illegal) move rejected; AreaLock (line ability) telegraph + dodge alert on
///       the correct unit; declined dodge; beam kill (death + damage application)
///   R3  Grenade centered on defender: correct unit alerted, straight two-cell dodge dive REPLACES
///       the planned move (unit asserted on the dive cell), no grenade damage taken
///   R4  Shield Rush rounds a one-wall diagonal corner, widens its live shield by one cell per
///       side, boosts and marks a nearby moving ally, and leaves a distant ally unboosted/unmarked
///   R5  cooling Area Lock is rejected server-side; its cooldown advances and it deals no damage
///   R6+ drives chase rounds (BFS toward nearest enemy) until elimination ends the match
///       (win or simultaneous-wipe draw, phase idle, timeScale restored to 1)
///
/// HOW TO RUN (either):
///   - Editor menu: Battle Plan > Run End-To-End Test (enters Play mode and auto-runs), or
///   - unityMCP: execute_menu_item "Battle Plan/Run End-To-End Test" (not in Play mode), or
///   - In Play mode: execute_code  DevE2ETest.Run();
/// Then poll:      execute_code  return DevE2ETest.Report;   (or read [E2E] console lines)
/// Requires the MPPM virtual player to be active (TempMppmAutoJoin auto-joins it as client 2).
///
/// Results: every step logs [E2E][PASS]/[E2E][FAIL]; the final report aggregates pass/fail.
/// Screenshots are saved outside Assets so Play Mode does not trigger an import/refresh cycle.
/// </summary>
public static class DevE2ETest
{
    const string AutoRunKey = "BattlePlan.E2E.AutoRun";

    public static bool IsRunning =>
        DevE2ETestRunner.Instance != null && !DevE2ETestRunner.Instance.Done;
    public static bool IsDone =>
        DevE2ETestRunner.Instance != null && DevE2ETestRunner.Instance.Done;
    public static bool Passed => IsDone && DevE2ETestRunner.Instance.FailCount == 0;
    public static string Report =>
        DevE2ETestRunner.Instance == null
            ? "<E2E not started>"
            : DevE2ETestRunner.Instance.BuildReport();

    /// <summary>Start the suite (Play mode required). Safe to call again after a run finishes.</summary>
    public static void Run()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "[E2E] Enter Play mode first, or use menu Battle Plan/Run End-To-End Test."
            );
            return;
        }
        if (DevE2ETestRunner.Instance != null)
        {
            if (!DevE2ETestRunner.Instance.Done)
            {
                Debug.LogWarning("[E2E] A test run is already in progress.");
                return;
            }
            Object.Destroy(DevE2ETestRunner.Instance.gameObject);
        }
        var go = new GameObject("DevE2ETestRunner");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<DevE2ETestRunner>();
    }

    [UnityEditor.MenuItem("Battle Plan/Run End-To-End Test")]
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
    static void AutoRunAfterEnterPlay()
    {
        // Only the main editor runs the suite; the MPPM clone just auto-joins as client 2.
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            return;
        if (!UnityEditor.SessionState.GetBool(AutoRunKey, false))
            return;
        UnityEditor.SessionState.EraseBool(AutoRunKey);
        Run();
    }
}

public class DevE2ETestRunner : MonoBehaviour
{
    public static DevE2ETestRunner Instance { get; private set; }

    public bool Done { get; private set; }
    public int PassCount { get; private set; }
    public int FailCount { get; private set; }

    readonly List<string> lines = new();
    bool timedOut;

    void Awake()
    {
        Instance = this;
    }

    IEnumerator Start()
    {
        yield return RunSuite();
        Finish();
    }

    // ---- assertion / reporting helpers ------------------------------------------------------

    void Check(bool ok, string name, string detail = "")
    {
        string suffix = string.IsNullOrEmpty(detail) ? "" : " -- " + detail;
        if (ok)
        {
            PassCount++;
            lines.Add("[PASS] " + name + suffix);
            Debug.Log("[E2E][PASS] " + name + suffix);
        }
        else
        {
            FailCount++;
            lines.Add("[FAIL] " + name + suffix);
            Debug.LogError("[E2E][FAIL] " + name + suffix);
        }
    }

    void Info(string message)
    {
        lines.Add("[info] " + message);
        Debug.Log("[E2E] " + message);
    }

    public string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Battle-Plan E2E gameplay test ===");
        foreach (string line in lines)
            sb.AppendLine(line);
        sb.AppendLine(
            Done
                ? $"RESULT: {(FailCount == 0 ? "PASS" : "FAIL")} ({PassCount} passed, {FailCount} failed)"
                : $"RESULT: still running ({PassCount} passed, {FailCount} failed so far)"
        );
        return sb.ToString();
    }

    void Finish()
    {
        Done = true;
        Snap("final");
        Debug.Log("[E2E] " + BuildReport());

        // Opt-in zero-idle mode for agent-driven runs: exit Play mode the moment the suite is
        // done (the report above persists in the console). Set the flag before launching.
        if (UnityEditor.SessionState.GetBool("BattlePlan.E2E.AutoExit", false))
        {
            UnityEditor.SessionState.EraseBool("BattlePlan.E2E.AutoExit");
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }

    // ---- wait / world helpers ----------------------------------------------------------------

    IEnumerator WaitFor(System.Func<bool> condition, float timeoutRealSeconds, string what)
    {
        timedOut = false;
        float deadline = Time.realtimeSinceStartup + timeoutRealSeconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                timedOut = true;
                Debug.LogWarning("[E2E] TIMEOUT waiting for: " + what);
                yield break;
            }
            yield return null;
        }
    }

    static string Phase => GameLoop.currentPhase;

    static GameObject U(int team, int index)
    {
        return GameLoop.GetTeamUnits(team)[index];
    }

    static Vector2Int Cell(GameObject unit) =>
        GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));

    static float HP(GameObject unit) => unit.GetComponent<Health>().CurrentHealth;

    static bool Alive(GameObject unit) => unit != null && unit.activeSelf;

    static HashSet<GameObject> AlertedUnits()
    {
        var set = new HashSet<GameObject>();
        var alerted = GameLoop.Instance != null ? GameLoop.Instance.dodgeAlerted : null;
        if (alerted != null)
            foreach (var kvp in alerted)
                set.UnionWith(kvp.Value);
        return set;
    }

    /// <summary>Queue a BFS chase path toward the nearest living enemy for every living unit.</summary>
    static void QueueChasePlans()
    {
        for (int t = 0; t < GameLoop.teamNames.Count; t++)
        {
            GameObject[] units = GameLoop.GetTeamUnits(t);
            for (int i = 0; i < units.Length; i++)
            {
                GameObject unit = units[i];
                if (!Alive(unit))
                    continue;
                GameObject enemy = NearestLivingEnemy(unit);
                if (enemy == null)
                    continue;
                List<Vector2Int> path = BfsPath(Cell(unit), Cell(enemy));
                if (path == null || path.Count < 3)
                    continue; // unreachable or already adjacent: stand and shoot
                int steps = Mathf.Min(
                    unit.GetComponent<Movement>().unitData.moveDist,
                    path.Count - 2
                ); // stop on the cell next to the enemy, not on it
                if (steps <= 0)
                    continue;
                DevInput.SetPath(t, i, path.GetRange(1, steps));
            }
        }
    }

    static GameObject NearestLivingEnemy(GameObject unit)
    {
        GameObject nearest = null;
        float best = float.MaxValue;
        foreach (
            GameObject enemy in GameObject.FindGameObjectsWithTag(GameLoop.GetEnemyTeam(unit.tag))
        )
        {
            float d = Vector3.Distance(unit.transform.position, enemy.transform.position);
            if (d < best)
            {
                best = d;
                nearest = enemy;
            }
        }
        return nearest;
    }

    /// <summary>4-directional BFS over the configured grid avoiding walls; returns start..goal cells.</summary>
    static List<Vector2Int> BfsPath(Vector2Int start, Vector2Int goal)
    {
        var cameFrom = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            if (cell == goal)
                break;
            foreach (Vector2Int dir in dirs)
            {
                Vector2Int next = cell + dir;
                if (!GridSystem.IsCellInBounds(next))
                    continue;
                if (GameLoop.wallLayout.Contains(next) || cameFrom.ContainsKey(next))
                    continue;
                cameFrom[next] = cell;
                queue.Enqueue(next);
            }
        }

        if (!cameFrom.ContainsKey(goal))
            return null;
        var path = new List<Vector2Int> { goal };
        while (path[0] != start)
            path.Insert(0, cameFrom[path[0]]);
        return path;
    }

    static void Snap(string name)
    {
        try
        {
            string dir = System.IO.Path.Combine(Application.temporaryCachePath, "BattlePlanE2E");
            System.IO.Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[E2E] screenshot '" + name + "' failed: " + e.Message);
        }
    }

    /// <summary>SubmitPlans, then wait out the round until the next planning phase (or idle).</summary>
    IEnumerator RunRoundToCompletion(string label, bool expectDodge, System.Action onDodge)
    {
        DevInput.SubmitPlans();
        yield return WaitFor(() => Phase != "planning", 30f, label + ": planning to end");

        if (expectDodge)
        {
            yield return WaitFor(() => Phase == "dodging", 30f, label + ": dodge phase");
            Check(!timedOut && Phase == "dodging", label + ": dodge phase started");
            if (Phase == "dodging")
                onDodge?.Invoke();
            else
                DevInput.SubmitDodge(); // best effort so the round can still resolve
        }
        else
        {
            Check(Phase != "dodging", label + ": exhausted ability did not open a dodge phase");
        }

        yield return WaitFor(
            () => Phase == "planning" || Phase == "idle",
            120f,
            label + ": round end"
        );
        Check(!timedOut, label + $": round resolved (phase={Phase})");
    }

    // ---- the suite -----------------------------------------------------------------------------

    IEnumerator RunSuite()
    {
        Info("Suite starting. Speed 6x. Requires MPPM clone for client 2.");

        // ================= R0: match start =================
        if (NetworkManager.Singleton == null)
            SceneManager.LoadScene("JoinGame", LoadSceneMode.Single);
        yield return WaitFor(
            () => NetworkManager.Singleton != null,
            30f,
            "network bootstrap scene"
        );
        Check(!timedOut, "R0 network bootstrap scene loaded");
        if (timedOut)
            yield break;

        DevInput.StartMatch(6f);
        yield return WaitFor(
            () =>
                NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsServer
                && NetworkManager.Singleton.ConnectedClients.Count == 2
                && GameLoop.Instance != null
                && GameLoop.allTeamUnitObjects.Count == 2
                && GameLoop.GetTeamUnits(0).Length == RosterRules.UnitsPerPlayer
                && GameLoop.GetTeamUnits(1).Length == RosterRules.UnitsPerPlayer
                && Phase == "planning",
            120f,
            "match start (host + MPPM clone join + Game scene + first planning phase)"
        );
        Check(!timedOut, "R0 match start: host up, 2 clients, Game scene, planning phase");
        if (timedOut)
            yield break; // Nothing else can run.

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        Check(
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval,
            "R0 local PvP host requires connection approval"
        );
        Check(
            transport != null
                && transport.Protocol == UnityTransport.ProtocolType.UnityTransport
                && transport.ConnectionData.Address == DevMppmAutoJoin.LoopbackAddress
                && transport.ConnectionData.ServerListenAddress == DevMppmAutoJoin.LoopbackAddress,
            "R0 local PvP host uses the loopback direct endpoint"
        );

        GameObject bSoldier = U(0, 0),
            pogo = U(0, 1),
            bShot = U(0, 2),
            bCommander = U(0, 3),
            bSniper = U(0, 4);
        GameObject rShot = U(1, 0),
            sniper = U(1, 1),
            rSoldier = U(1, 2),
            rCommander = U(1, 3),
            rPogo = U(1, 4);

        Check(
            bSoldier.name.StartsWith("Soldier")
                && pogo.name.StartsWith("PogoRider")
                && bShot.name.StartsWith("Shotgunner")
                && bCommander.name.StartsWith("Commander")
                && bSniper.name.StartsWith("Sniper")
                && rShot.name.StartsWith("Shotgunner")
                && sniper.name.StartsWith("Sniper")
                && rSoldier.name.StartsWith("Soldier")
                && rCommander.name.StartsWith("Commander")
                && rPogo.name.StartsWith("PogoRider"),
            "R0 roster includes every configured slot for both teams",
            $"actual: {string.Join(",", GameLoop.GetTeamUnits(0).Select(unit => unit.name))} | {string.Join(",", GameLoop.GetTeamUnits(1).Select(unit => unit.name))}"
        );
        Check(GameLoop.Instance.FogOfWarEnabled, "R0 fog of war enabled from match options");
        Check(
            Cell(bSoldier) == new Vector2Int(4, 3)
                && Cell(pogo) == new Vector2Int(7, 5)
                && Cell(bShot) == new Vector2Int(10, 3)
                && Cell(bCommander) == new Vector2Int(2, 2)
                && Cell(bSniper) == new Vector2Int(12, 2)
                && Cell(rShot) == new Vector2Int(8, 6)
                && Cell(sniper) == new Vector2Int(7, 6)
                && Cell(rSoldier) == new Vector2Int(6, 6)
                && Cell(rCommander) == new Vector2Int(12, 7)
                && Cell(rPogo) == new Vector2Int(2, 7),
            "R0 spawns match dev layout",
            $"blue {string.Join(",", GameLoop.GetTeamUnits(0).Select(Cell))} red {string.Join(",", GameLoop.GetTeamUnits(1).Select(Cell))}"
        );
        Snap("r0-match-start");

        // ================= R1: movement (legal + illegal wall step), both teams, speed =========
        // Blue Soldier: legal 3-step (4,3)->(4,2)->(3,2)->(3,1).
        DevInput.SetPath(0, 0, 4, 2, 3, 2, 3, 1);
        // PogoRider: legal 5-step escape down the clear column-8 lane to (8,1).
        DevInput.SetPath(0, 1, 8, 5, 8, 4, 8, 3, 8, 2, 8, 1);
        // Blue Shotgunner: (11,3) legal, then (12,3) is a WALL -> truncate at (11,3).
        DevInput.SetPath(0, 2, 11, 3, 12, 3);
        // Red Shotgunner: legal 3-step away from the Pogo escape lane (also proves dev input
        // drives BOTH teams / all unit indices -- the no-mouse analog of unit switching).
        DevInput.SetPath(1, 0, 8, 7, 9, 7, 9, 8);
        // Red Sniper follows into column 8 and retreats to row 8, outside auto-fire range while
        // preserving a clear vertical Area Lock lane to the PogoRider for R2.
        DevInput.SetPath(1, 1, 8, 6, 8, 7, 8, 8);

        DevInput.SubmitPlans();
        yield return WaitFor(() => Phase == "executing", 30f, "R1 execution start");
        Check(!timedOut, "R1 planning ended on SubmitPlans, execution started");

        // Speed control mid-execution.
        DevInput.SetSpeed(30f);
        Check(
            Mathf.Approximately(Time.timeScale, 30f),
            "R1 SetSpeed(30) applied to Time.timeScale"
        );
        yield return WaitFor(() => Phase == "planning", 120f, "R1 round end");
        Check(!timedOut, "R1 round resolved");
        DevInput.SetSpeed(6f);
        Check(Mathf.Approximately(Time.timeScale, 6f), "R1 SetSpeed(6) restored");

        Check(
            Cell(bSoldier) == new Vector2Int(3, 1),
            "R1 legal 3-step path executed (blue Soldier at (3,1))",
            $"actual {Cell(bSoldier)}"
        );
        Check(
            Cell(pogo) == new Vector2Int(8, 1),
            "R1 legal 5-step path executed (PogoRider at (8,1))",
            $"actual {Cell(pogo)}"
        );
        Check(
            Cell(bShot) == new Vector2Int(11, 3),
            "R1 illegal wall step (12,3) rejected by server sanitizer (Shotgunner stopped at (11,3))",
            $"actual {Cell(bShot)}"
        );
        Check(
            Cell(rShot) == new Vector2Int(9, 8),
            "R1 red-team unit moved by dev input (RedShotgunner at (9,8))",
            $"actual {Cell(rShot)}"
        );
        Check(
            Cell(sniper) == new Vector2Int(8, 8),
            "R1 red Sniper retreated to (8,8) (out of weapon range of the Pogo lane)",
            $"actual {Cell(sniper)}"
        );
        Check(
            Cell(rSoldier) == new Vector2Int(6, 6),
            "R1 auto-filled unit stayed put (Soldier (6,6))",
            $"actual {Cell(rSoldier)}"
        );
        Info(
            $"R1 HP after round: bSoldier={HP(bSoldier)} pogo={HP(pogo)} bShot={HP(bShot)} | rShot={HP(rShot)} sniper={HP(sniper)} rSoldier={HP(rSoldier)}"
        );
        Snap("r1-movement");

        // ================= R2: movement validation + AreaLock line ability + death ==============
        // Reposition the Blue Soldier to the center-target fixture used by R3.
        DevInput.SetPath(0, 0, 3, 2, 4, 2, 5, 2);
        // Blue Shotgunner submits a NON-ADJACENT jump (11,3)->(13,3): reject and stay put.
        DevInput.SetPath(0, 2, 13, 3);
        // Sniper line ability straight down column 8 at the PogoRider -- a dodge alert must fire
        // for the PogoRider; we DECLINE the dodge, so the beam kills it (death + damage proof).
        DevInput.SetAbility(1, 1, 8, 1);
        yield return RunRoundToCompletion(
            "R2",
            true,
            () =>
            {
                var alerted = AlertedUnits();
                bool pogoWasAlerted = alerted.Contains(pogo);
                var icon = pogo.transform.Find("UnitCanvas/Alert");
                bool pogoAlertIconOn = icon != null && icon.gameObject.activeSelf;
                Check(
                    alerted.Count == 1 && pogoWasAlerted,
                    "R2 dodge alert targeted exactly the threatened unit (PogoRider on the beam line)",
                    "alerted: " + string.Join(",", alerted.Select(u => u.name))
                );
                Check(
                    pogoAlertIconOn,
                    "R2 alert icon (UnitCanvas/Alert) enabled on threatened unit"
                );
                Snap("r2-dodge-window");
                DevInput.SubmitDodge(); // decline: no dive queued -> PogoRider holds still on the line
            }
        );
        Check(
            !Alive(pogo),
            "R2 AreaLock beam killed the non-dodging PogoRider (death + damage applied)",
            $"alive={Alive(pogo)}"
        );
        Check(
            Cell(bSoldier) == new Vector2Int(5, 2),
            "R2 legal reposition completed (blue Soldier at the R3 grenade center)",
            $"actual {Cell(bSoldier)}"
        );
        Check(
            Cell(bShot) == new Vector2Int(11, 3),
            "R2 illegal non-adjacent move rejected (blue Shotgunner still at (11,3))",
            $"actual {Cell(bShot)}"
        );
        Snap("r2-arealock-kill");

        // ================= R3: Grenade + dodge dive that REPLACES the planned move ==============
        // Soldier (6,6) throws at the Blue Soldier's exact cell (5,2): Manhattan 5 = max range.
        // The defender replaces its planned move with a straight two-cell dive to (3,2), proving
        // that the 1.6-cell blast can be escaped from its center.
        float bSoldierHpBeforeR3 = HP(bSoldier);
        DevInput.SetPath(0, 0, 6, 2);
        DevInput.SetAbility(1, 2, 5, 2);
        yield return RunRoundToCompletion(
            "R3",
            true,
            () =>
            {
                var alerted = AlertedUnits();
                Check(
                    alerted.Count == 1 && alerted.Contains(bSoldier),
                    "R3 grenade dodge alert targeted exactly the blue Soldier",
                    "alerted: " + string.Join(",", alerted.Select(u => u.name))
                );
                DevInput.SetDodgePath(0, 0, 4, 2, 3, 2); // (5,2) -> (4,2) -> (3,2)
                Snap("r3-grenade-telegraph");
                DevInput.SubmitDodge();
            }
        );
        Check(
            Cell(bSoldier) == new Vector2Int(3, 2),
            "R3 center-target dive replaced the planned move and ended two cells away at (3,2)",
            $"actual {Cell(bSoldier)}"
        );
        Check(
            Alive(bSoldier) && HP(bSoldier) >= bSoldierHpBeforeR3 - 0.01f,
            "R3 successful dodge: no grenade damage taken",
            $"hp {bSoldierHpBeforeR3} -> {HP(bSoldier)}"
        );
        Snap("r3-dodged");

        // ================= R4: diagonal Shield Rush + allied speed boost =========================
        // The blue Soldier survived R3 by assertion and is outside every red unit's weapon range.
        // Moving the already-wounded Soldier here made this check depend on nondeterministic
        // crossfire from prior rounds rather than movement correctness.
        DevInput.SetPath(0, 0, 3, 1, 3, 0);
        // The nearby red Sniper moves while its Shotgunner casts, exercising the boost on a real
        // ordinary-movement coroutine. The distant Commander must remain at normal speed.
        DevInput.SetPath(1, 1, 8, 9);
        Vector2Int rushStart = Cell(rShot);
        Vector2Int rushDirectionTarget = rushStart + new Vector2Int(-1, -1);
        Vector2Int expectedRushDestination = new(6, 5);
        Unit rushIdentity = rShot.GetComponent<Unit>();
        int rushCooldownBefore = rushIdentity.AbilityCooldownRoundsRemaining;
        Check(
            rushStart == new Vector2Int(9, 8),
            "R4 precondition: red Shotgunner starts at (9,8)",
            $"actual {rushStart}"
        );
        Check(
            Alive(sniper) && Alive(rCommander),
            "R4 precondition: nearby and distant speed-boost probes are both alive"
        );
        Check(
            GameLoop.wallLayout.Contains(new Vector2Int(7, 7))
                && !GameLoop.wallLayout.Contains(new Vector2Int(7, 6))
                && !GameLoop.wallLayout.Contains(new Vector2Int(8, 6)),
            "R4 precondition: one side of the diagonal path is walled while the other side and diagonal stay open"
        );
        DevInput.SetAbility(1, 0, rushDirectionTarget.x, rushDirectionTarget.y);
        Transform shield = rShot.transform.Find("Shield");
        Check(shield != null, "R4 red Shotgunner has a Shield child object");
        BoxCollider shieldCollider = shield != null ? shield.GetComponent<BoxCollider>() : null;
        GameObject shotgunnerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Units/Shotgunner.prefab"
        );
        Transform serializedShield =
            shotgunnerPrefab != null ? shotgunnerPrefab.transform.Find("Shield") : null;
        BoxCollider serializedShieldCollider =
            serializedShield != null ? serializedShield.GetComponent<BoxCollider>() : null;
        float liveShieldWidth =
            shieldCollider != null
                ? Mathf.Abs(shieldCollider.size.x * shield.lossyScale.x)
                : 0f;
        float serializedShieldWidth =
            serializedShieldCollider != null
                ? Mathf.Abs(serializedShieldCollider.size.x * serializedShield.lossyScale.x)
                : 0f;
        Check(
            shieldCollider != null
                && serializedShieldCollider != null
                && Mathf.Abs(
                    liveShieldWidth
                        - serializedShieldWidth
                        - Shield.ShieldWidthIncreaseCellsPerSide * 2f * GameLoop.cellSize
                ) <= 0.001f,
            "R4 spawned Shield visual and collider are one grid cell wider on each side",
            $"serialized={serializedShieldWidth:F3}, live={liveShieldWidth:F3}"
        );
        Movement nearbyAllyMovement = sniper.GetComponent<Movement>();
        Movement distantAllyMovement = rCommander.GetComponent<Movement>();

        DevInput.SubmitPlans();
        yield return WaitFor(() => Phase == "executing", 30f, "R4 execution start");
        bool shieldSeenOn = false,
            shieldSeenOffAfterOn = false,
            nearbyBoostSeenWhileMoving = false,
            distantBoostSeen = false,
            nearbyBoostIndicatorSeen = false,
            nearbyBoostIndicatorSeenOffAfterOn = false,
            distantBoostIndicatorSeen = false;
        {
            float deadline = Time.realtimeSinceStartup + 120f;
            while (Phase != "planning" && Phase != "idle" && Time.realtimeSinceStartup < deadline)
            {
                bool active = shield != null && shield.gameObject.activeSelf;
                if (active)
                    shieldSeenOn = true;
                else if (shieldSeenOn)
                    shieldSeenOffAfterOn = true;
                if (
                    nearbyAllyMovement != null
                    && nearbyAllyMovement.moving
                    && Mathf.Approximately(
                        nearbyAllyMovement.CurrentMoveSpeedMultiplier,
                        Shield.AllySpeedBoostMultiplier
                    )
                )
                {
                    nearbyBoostSeenWhileMoving = true;
                }
                bool nearbyBoostIndicatorActive =
                    nearbyAllyMovement != null
                    && nearbyAllyMovement.IsSpeedBoostIndicatorActive;
                if (
                    nearbyBoostIndicatorActive
                    && Mathf.Approximately(
                        nearbyAllyMovement.CurrentMoveSpeedMultiplier,
                        Shield.AllySpeedBoostMultiplier
                    )
                )
                {
                    nearbyBoostIndicatorSeen = true;
                }
                else if (nearbyBoostIndicatorSeen && !nearbyBoostIndicatorActive)
                {
                    nearbyBoostIndicatorSeenOffAfterOn = true;
                }
                if (
                    distantAllyMovement != null
                    && !Mathf.Approximately(distantAllyMovement.CurrentMoveSpeedMultiplier, 1f)
                )
                {
                    distantBoostSeen = true;
                }
                if (
                    distantAllyMovement != null
                    && distantAllyMovement.IsSpeedBoostIndicatorActive
                )
                {
                    distantBoostIndicatorSeen = true;
                }
                yield return null;
            }
        }
        if (shieldSeenOn && (shield == null || !shield.gameObject.activeSelf))
            shieldSeenOffAfterOn = true;
        if (
            nearbyBoostIndicatorSeen
            && (
                nearbyAllyMovement == null
                || !nearbyAllyMovement.IsSpeedBoostIndicatorActive
            )
        )
        {
            nearbyBoostIndicatorSeenOffAfterOn = true;
        }
        Check(shieldSeenOn, "R4 Shield Rush activated the Shield child during execution");
        Check(shieldSeenOffAfterOn, "R4 Shield Rush deactivated again after its duration");
        Check(
            Cell(rShot) == expectedRushDestination,
            "R4 Shield Rush crossed the one-wall diagonal corner and reached its three-cell destination",
            $"expected {expectedRushDestination}, actual {Cell(rShot)}"
        );
        Check(
            nearbyBoostSeenWhileMoving,
            "R4 nearby living ally received the Shield Rush speed boost while moving"
        );
        Check(!distantBoostSeen, "R4 distant ally remained at normal movement speed");
        Check(
            nearbyBoostIndicatorSeen,
            "R4 nearby boosted ally displayed the Shield Rush speed indicator while boosted"
        );
        Check(
            nearbyBoostIndicatorSeenOffAfterOn,
            "R4 nearby ally's Shield Rush speed indicator deactivated after the boost"
        );
        Check(
            !distantBoostIndicatorSeen,
            "R4 distant unboosted ally never displayed the Shield Rush speed indicator"
        );
        Check(
            nearbyAllyMovement != null
                && distantAllyMovement != null
                && Mathf.Approximately(nearbyAllyMovement.CurrentMoveSpeedMultiplier, 1f)
                && Mathf.Approximately(distantAllyMovement.CurrentMoveSpeedMultiplier, 1f)
                && !nearbyAllyMovement.IsSpeedBoostIndicatorActive
                && !distantAllyMovement.IsSpeedBoostIndicatorActive,
            "R4 allied movement speed and indicator state returned to normal after the shield window"
        );
        Check(
            Cell(sniper) == new Vector2Int(8, 9),
            "R4 boosted ally completed its simultaneous ordinary move",
            $"actual {Cell(sniper)}"
        );
        Check(
            rushCooldownBefore == 0 && rushIdentity.AbilityCooldownRoundsRemaining == 2,
            "R4 Shield Rush started its two-round cooldown",
            $"cooldown {rushCooldownBefore} -> {rushIdentity.AbilityCooldownRoundsRemaining}"
        );
        Check(
            Cell(bSoldier) == new Vector2Int(3, 0),
            "R4 blue Soldier completed its simultaneous move to (3,0)",
            $"actual {Cell(bSoldier)}"
        );
        Snap("r4-shield-rush");

        // ================= R5: cooling ability is rejected server-side ===========================
        if (!Alive(bSoldier) || !Alive(sniper))
        {
            Check(
                false,
                "R5 precondition: blue Soldier and Sniper alive for ability-exhaustion round",
                $"blue Soldier alive={Alive(bSoldier)}, sniper alive={Alive(sniper)}"
            );
        }
        else
        {
            float bSoldierHpBeforeR5 = HP(bSoldier);
            Vector2Int bSoldierCell = Cell(bSoldier);
            Unit sniperIdentity = sniper.GetComponent<Unit>();
            int sniperCooldownBeforeR5 = sniperIdentity.AbilityCooldownRoundsRemaining;
            DevInput.SetAbility(1, 1, bSoldierCell.x, bSoldierCell.y);
            yield return RunRoundToCompletion("R5", false, null);
            Check(
                sniperCooldownBeforeR5 > 0
                    && sniperIdentity.AbilityCooldownRoundsRemaining
                        == Mathf.Max(0, sniperCooldownBeforeR5 - 1),
                "R5 cooling Area Lock was rejected while its cooldown advanced normally",
                $"cooldown {sniperCooldownBeforeR5} -> {sniperIdentity.AbilityCooldownRoundsRemaining}"
            );
            Check(
                Alive(bSoldier) && HP(bSoldier) >= bSoldierHpBeforeR5 - 0.01f,
                "R5 exhausted Area Lock caused no damage",
                "hp "
                    + bSoldierHpBeforeR5
                    + " -> "
                    + (Alive(bSoldier) ? HP(bSoldier).ToString() : "dead")
            );
            Snap("r5-ability-cooling");
        }

        // ================= R6+: chase rounds until the win condition ends the match =============
        // Stay-put rounds can stalemate (walls/range gaps), so every living unit BFS-chases its
        // nearest enemy each round until one team is wiped. Either winner is a valid outcome —
        // simultaneous fire makes the survivor legitimately non-deterministic.
        DevInput.SetSpeed(30f);
        int guard = 0;
        while (Phase != "idle" && guard < 12)
        {
            guard++;
            yield return WaitFor(
                () => Phase == "planning" || Phase == "idle",
                60f,
                $"chase round {guard}: planning"
            );
            if (Phase == "idle" || timedOut)
                break;
            QueueChasePlans();
            DevInput.SubmitPlans();
            yield return WaitFor(() => Phase != "planning", 30f, $"chase round {guard}: execution");
            if (Phase == "dodging")
                DevInput.SubmitDodge();
            yield return WaitFor(
                () => Phase == "planning" || Phase == "idle",
                120f,
                $"chase round {guard}: round end"
            );
            if (timedOut)
                break;
        }
        int blueLeft = GameLoop.teamSize("BlueTeam");
        int redLeft = GameLoop.teamSize("RedTeam");
        Check(
            Phase == "idle" && (blueLeft == 0 || redLeft == 0),
            "Elimination result reached: at least one team wiped, game loop ended",
            $"phase={Phase} blue={blueLeft} red={redLeft} (chase rounds used: {guard})"
        );
        MatchResult? finalResult = GameLoop.Instance?.LastMatchResult;
        bool simultaneousWipe = blueLeft == 0 && redLeft == 0;
        int expectedWinner = blueLeft == 0 ? GameLoop.OpponentTeamIndex : GameLoop.HostTeamIndex;
        bool resultMatchesElimination =
            finalResult.HasValue
            && (
                simultaneousWipe
                    ? finalResult.Value.Outcome == MatchOutcome.Draw
                        && finalResult.Value.Reason == MatchResultReason.SimultaneousElimination
                    : finalResult.Value.Outcome == MatchOutcome.Win
                        && finalResult.Value.Reason == MatchResultReason.Elimination
                        && finalResult.Value.WinningTeamIndex == expectedWinner
            );
        Check(
            resultMatchesElimination,
            "Typed elimination result distinguishes a win from a simultaneous-wipe draw",
            finalResult.HasValue
                ? $"outcome={finalResult.Value.Outcome} reason={finalResult.Value.Reason} winner={finalResult.Value.WinningTeamIndex}"
                : "no match result"
        );
        // EndGame resets Time.timeScale — assert BEFORE touching speed controls again.
        Check(
            Mathf.Approximately(Time.timeScale, 1f),
            "EndGame restored Time.timeScale to 1",
            $"actual {Time.timeScale}"
        );
        Check(!GameLoop.Instance.FogOfWarEnabled, "EndGame disabled fog of war");
        Snap("r6-endgame");

        Info("Final state dump:\n" + DevInput.Dump());
    }
}
#endif
