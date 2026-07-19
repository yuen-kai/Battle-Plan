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
///   R3  Grenade (targeted ability) dodge phase: correct unit alerted, dodge dive REPLACES the
///       planned move (unit asserted on the dive cell), no grenade damage taken
///   R4  Shield Rush dashes three cells and toggles the caster's Shield child on then off; a unit
///       moves and casts on the same team in the same round
///   R5  Grenade damage application on a non-dodging target (HP drop >= 80 or death; grenade
///       damage rebalanced 50 -> 80)
///   R6+ drives chase rounds (BFS toward nearest enemy) until the win condition ends the match
///       (exactly one team wiped, phase idle, timeScale restored to 1)
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

    /// <summary>4-directional BFS over the 9x10 grid avoiding walls; returns start..goal cells.</summary>
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
                if (next.x < 0 || next.x > 8 || next.y < 0 || next.y > 9)
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

        GameObject cmd = U(0, 0),
            pogo = U(0, 1),
            bShot = U(0, 2);
        GameObject rShot = U(1, 0),
            sniper = U(1, 1),
            soldier = U(1, 2);

        Check(
            cmd.name.StartsWith("Commander")
                && pogo.name.StartsWith("PogoRider")
                && bShot.name.StartsWith("Shotgunner")
                && rShot.name.StartsWith("Shotgunner")
                && sniper.name.StartsWith("Sniper")
                && soldier.name.StartsWith("Soldier"),
            "R0 roster: Blue=Commander/PogoRider/Shotgunner, Red=Shotgunner/Sniper/Soldier",
            $"actual: {cmd.name},{pogo.name},{bShot.name} | {rShot.name},{sniper.name},{soldier.name}"
        );
        Check(GameLoop.Instance.FogOfWarEnabled, "R0 fog of war enabled from match options");
        Check(
            Cell(cmd) == new Vector2Int(1, 3)
                && Cell(pogo) == new Vector2Int(4, 5)
                && Cell(bShot) == new Vector2Int(7, 3)
                && Cell(rShot) == new Vector2Int(5, 6)
                && Cell(sniper) == new Vector2Int(4, 6)
                && Cell(soldier) == new Vector2Int(3, 6),
            "R0 spawns match dev layout",
            $"blue {Cell(cmd)},{Cell(pogo)},{Cell(bShot)} red {Cell(rShot)},{Cell(sniper)},{Cell(soldier)}"
        );
        Snap("r0-match-start");

        // ================= R1: movement (legal + illegal wall step), both teams, speed =========
        // Commander: legal 3-step (1,3)->(1,2)->(0,2)->(0,1).
        DevInput.SetPath(0, 0, 1, 2, 0, 2, 0, 1);
        // PogoRider: legal 5-step escape straight down column 4 to (4,0) (beam lane for R2).
        DevInput.SetPath(0, 1, 4, 4, 4, 3, 4, 2, 4, 1, 4, 0);
        // Blue Shotgunner: (8,3) legal, then (8,4) is a WALL -> must be truncated at (8,3).
        DevInput.SetPath(0, 2, 8, 3, 8, 4);
        // Red Shotgunner: legal 3-step away from the Pogo escape lane (also proves dev input
        // drives BOTH teams / all unit indices -- the no-mouse analog of unit switching).
        DevInput.SetPath(1, 0, 5, 5, 6, 5, 7, 5);
        // Red Sniper: retreat up the lane to (4,9), keeping the Pogo out of the new weapon range
        // (9 > 5 at (4,0)) while leaving it on the beam lane for R2.
        DevInput.SetPath(1, 1, 4, 7, 4, 8, 4, 9);

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
            Cell(cmd) == new Vector2Int(0, 1),
            "R1 legal 3-step path executed (Commander at (0,1))",
            $"actual {Cell(cmd)}"
        );
        Check(
            Cell(pogo) == new Vector2Int(4, 0),
            "R1 legal 5-step path executed (PogoRider at (4,0))",
            $"actual {Cell(pogo)}"
        );
        Check(
            Cell(bShot) == new Vector2Int(8, 3),
            "R1 illegal wall step (8,4) rejected by server sanitizer (Shotgunner stopped at (8,3))",
            $"actual {Cell(bShot)}"
        );
        Check(
            Cell(rShot) == new Vector2Int(7, 5),
            "R1 red-team unit moved by dev input (RedShotgunner at (7,5))",
            $"actual {Cell(rShot)}"
        );
        Check(
            Cell(sniper) == new Vector2Int(4, 9),
            "R1 red Sniper retreated to (4,9) (out of weapon range of the pogo lane)",
            $"actual {Cell(sniper)}"
        );
        Check(
            Cell(soldier) == new Vector2Int(3, 6),
            "R1 auto-filled unit stayed put (Soldier (3,6))",
            $"actual {Cell(soldier)}"
        );
        Info(
            $"R1 HP after round: cmd={HP(cmd)} pogo={HP(pogo)} bShot={HP(bShot)} | rShot={HP(rShot)} sniper={HP(sniper)} soldier={HP(soldier)}"
        );
        Snap("r1-movement");

        // ================= R2: illegal non-adjacent move + AreaLock line ability + death =======
        // Commander submits a NON-ADJACENT jump (0,1)->(2,1): must be fully rejected (stay put).
        DevInput.SetPath(0, 0, 2, 1);
        // Sniper line ability straight down column 4 at the PogoRider -- a dodge alert must fire
        // for the PogoRider; we DECLINE the dodge, so the beam kills it (death + damage proof).
        DevInput.SetAbility(1, 1, 4, 0);
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
            Cell(cmd) == new Vector2Int(0, 1),
            "R2 illegal non-adjacent move rejected (Commander still at (0,1))",
            $"actual {Cell(cmd)}"
        );
        Snap("r2-arealock-kill");

        // ================= R3: Grenade + dodge dive that REPLACES the planned move ==============
        // Soldier (3,6) throws a grenade at (1,3): Manhattan 5 = max range, radius 2, response 3.
        // Commander (0,1) is within response range of the square -> alerted. He dives 2 cells to
        // (1,0) (3 cells from the blast, outside radius 2): must end on the dive cell, undamaged.
        float cmdHpBeforeR3 = HP(cmd);
        DevInput.SetAbility(1, 2, 1, 3);
        yield return RunRoundToCompletion(
            "R3",
            true,
            () =>
            {
                var alerted = AlertedUnits();
                Check(
                    alerted.Count == 1 && alerted.Contains(cmd),
                    "R3 grenade dodge alert targeted exactly the Commander",
                    "alerted: " + string.Join(",", alerted.Select(u => u.name))
                );
                DevInput.SetDodgePath(0, 0, 0, 0, 1, 0); // (0,1) -> (0,0) -> (1,0)
                Snap("r3-grenade-telegraph");
                DevInput.SubmitDodge();
            }
        );
        Check(
            Cell(cmd) == new Vector2Int(1, 0),
            "R3 dodge dive replaced the planned move (Commander ended on dive cell (1,0))",
            $"actual {Cell(cmd)}"
        );
        Check(
            Alive(cmd) && HP(cmd) >= cmdHpBeforeR3 - 0.01f,
            "R3 successful dodge: no grenade damage taken",
            $"hp {cmdHpBeforeR3} -> {HP(cmd)}"
        );
        Snap("r3-dodged");

        // ================= R4: Shield Rush + a simultaneous safe move ============================
        // The Commander survived R3 by assertion and is outside every red unit's weapon range.
        // Moving the already-wounded Soldier here made this check depend on nondeterministic
        // crossfire from prior rounds rather than movement correctness.
        DevInput.SetPath(0, 0, 2, 0);
        Vector2Int rushStart = Cell(rShot);
        Vector2Int rushDirectionTarget = rushStart + Vector2Int.left;
        Vector2Int expectedRushDestination = new(4, 5);
        Unit rushIdentity = rShot.GetComponent<Unit>();
        int rushUsesBefore = rushIdentity.RemainingAbilityUses;
        Check(
            rushStart == new Vector2Int(7, 5),
            "R4 precondition: red Shotgunner starts at (7,5)",
            $"actual {rushStart}"
        );
        DevInput.SetAbility(1, 0, rushDirectionTarget.x, rushDirectionTarget.y);
        Transform shield = rShot.transform.Find("Shield");
        Check(shield != null, "R4 red Shotgunner has a Shield child object");

        DevInput.SubmitPlans();
        yield return WaitFor(() => Phase == "executing", 30f, "R4 execution start");
        bool shieldSeenOn = false,
            shieldSeenOffAfterOn = false;
        {
            float deadline = Time.realtimeSinceStartup + 120f;
            while (Phase != "planning" && Phase != "idle" && Time.realtimeSinceStartup < deadline)
            {
                bool active = shield != null && shield.gameObject.activeSelf;
                if (active)
                    shieldSeenOn = true;
                else if (shieldSeenOn)
                    shieldSeenOffAfterOn = true;
                yield return null;
            }
        }
        if (shieldSeenOn && (shield == null || !shield.gameObject.activeSelf))
            shieldSeenOffAfterOn = true;
        Check(shieldSeenOn, "R4 Shield Rush activated the Shield child during execution");
        Check(shieldSeenOffAfterOn, "R4 Shield Rush deactivated again after its duration");
        Check(
            Cell(rShot) == expectedRushDestination,
            "R4 Shield Rush reached its fixed three-cell destination",
            $"expected {expectedRushDestination}, actual {Cell(rShot)}"
        );
        Check(
            rushUsesBefore == 1 && rushIdentity.RemainingAbilityUses == 0,
            "R4 Shield Rush consumed its single ability charge",
            $"uses {rushUsesBefore} -> {rushIdentity.RemainingAbilityUses}"
        );
        Check(
            Cell(cmd) == new Vector2Int(2, 0),
            "R4 Commander completed its simultaneous move to (2,0)",
            $"actual {Cell(cmd)}"
        );
        Snap("r4-shield-rush");

        // ================= R5: exhausted ability is rejected server-side =========================
        if (!Alive(cmd) || !Alive(sniper))
        {
            Check(
                false,
                "R5 precondition: Commander and Sniper alive for ability-exhaustion round",
                $"commander alive={Alive(cmd)}, sniper alive={Alive(sniper)}"
            );
        }
        else
        {
            float cmdHpBeforeR5 = HP(cmd);
            Vector2Int cmdCell = Cell(cmd);
            DevInput.SetAbility(1, 1, cmdCell.x, cmdCell.y);
            yield return RunRoundToCompletion("R5", false, null);
            Check(
                sniper.GetComponent<Unit>().RemainingAbilityUses == 0,
                "R5 Sniper Area Lock charge remained exhausted after a rejected second use"
            );
            Check(
                Alive(cmd) && HP(cmd) >= cmdHpBeforeR5 - 0.01f,
                "R5 exhausted Area Lock caused no damage",
                "hp " + cmdHpBeforeR5 + " -> " + (Alive(cmd) ? HP(cmd).ToString() : "dead")
            );
            Snap("r5-ability-exhausted");
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
            Phase == "idle" && (blueLeft == 0 ^ redLeft == 0),
            "Win condition reached: exactly one team wiped, game loop ended",
            $"phase={Phase} blue={blueLeft} red={redLeft} (chase rounds used: {guard})"
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
