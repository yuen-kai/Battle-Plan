using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// DEV / TESTING no-mouse input API. Every method here is meant to be driven from C# (e.g. the
/// unityMCP execute_code channel) so an agent can run and observe a full match without a mouse.
///
/// All submission runs server-side (call these from the host/main editor). Plans are queued per
/// unit as grid cells (col,row) and validated with the same rules as mouse input before execution.
/// Nothing here affects normal gameplay unless <see cref="GameLoop.devMode"/> is on.
///
/// Planning model in dev mode: the planning phase waits INDEFINITELY (no wall-clock timer) so the
/// agent has unbounded time to set paths across multiple execute_code calls; the round advances the
/// instant SubmitPlans()/EndPlanning() is called. Execution + shooting stay fast via the speed
/// multiplier (Time.timeScale). Normal (non-dev) planning timing is unchanged.
///
/// Typical use from execute_code:
///   DevInput.StartMatch();                 // host + auto-join clone -> a match begins
///   DevInput.SetPath(0, 0, 1,4, 1,5, 1,6); // team 0 (Blue), unit 0, walk up 3 cells (no mouse)
///   DevInput.SubmitPlans();                // ends planning NOW; server auto-fills unset units
/// </summary>
public static class DevInput
{
    // Queued plans for the next SubmitPlans(): unit -> (useAbility, world-space path incl. start).
    private static readonly Dictionary<GameObject, (bool ability, List<Vector3> path)> pending =
        new();

    // ---- Match / mode control -------------------------------------------------------------

    /// <summary>Turn dev mode on and start hosting so a match begins with no menu clicks.</summary>
    public static void StartMatch(float speed = -1f)
    {
        GameLoop.devMode = true;
        if (speed > 0f)
            GameLoop.devSpeedMultiplier = speed;
        MatchOptions.SetCurrent(MatchOptions.Default);
        GameLoop.ResetMatchState();
#if UNITY_EDITOR
        DevMppmAutoJoin.EnableLocalAutoJoin();
#endif
        StartHost();
    }

    /// <summary>Start a one-client development match against the real authoritative bot.</summary>
    public static void StartBotMatch(float speed = -1f, bool fogOfWar = true)
    {
        GameLoop.devMode = true;
        if (speed > 0f)
            GameLoop.devSpeedMultiplier = speed;

#if UNITY_EDITOR
        DevMppmAutoJoin.DisableAutoJoin();
#endif
        MatchOptions options = MatchOptions.Default;
        options.opponentType = OpponentType.AI;
        options.fogOfWar = fogOfWar;
        MatchOptions.SetCurrent(options);
        GameLoop.ResetMatchState();
        StartHost();
    }

    /// <summary>Start an NGO host in this editor after configuring direct loopback transport.</summary>
    public static void StartHost()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
#if UNITY_EDITOR
            DevMppmAutoJoin.DisableAutoJoin();
#endif
            Debug.LogWarning("[DevInput] No NetworkManager in scene yet; enter Play mode first.");
            return;
        }
        if (nm.IsListening || nm.IsServer || nm.IsClient)
        {
#if UNITY_EDITOR
            DevMppmAutoJoin.DisableAutoJoin();
#endif
            Debug.Log("[DevInput] Host/client already running.");
            return;
        }
#if UNITY_EDITOR
        DevMppmAutoJoin.ConfigureLoopbackTransport(nm);
#endif
        if (!nm.StartHost())
        {
#if UNITY_EDITOR
            DevMppmAutoJoin.DisableAutoJoin();
#endif
            Debug.LogError("[DevInput] StartHost() failed.");
            return;
        }

        string waitState = MatchOptions.Current.IsBotMatch
            ? "authoritative bot match started"
            : "waiting for explicit Player 2 auto-join";
        Debug.Log($"[DevInput] StartHost() called; {waitState}.");
    }

    /// <summary>Toggle dev mode at runtime and keep Time.timeScale consistent.</summary>
    public static void SetDevMode(bool on)
    {
        GameLoop.devMode = on;
        Time.timeScale = on ? Mathf.Max(0.01f, GameLoop.devSpeedMultiplier) : 1f;
#if UNITY_EDITOR
        if (!on)
            DevMppmAutoJoin.DisableAutoJoin();
#endif
        Debug.Log($"[DevInput] devMode={on} timeScale={Time.timeScale}");
    }

    /// <summary>Enable or disable fog of war match-wide from the host.</summary>
    public static void SetFog(bool on)
    {
        if (GameLoop.Instance == null)
        {
            Debug.LogWarning("[DevInput] No GameLoop; is a match running?");
            return;
        }

        GameLoop.Instance.SetFogOfWarEnabled(on);
    }

    /// <summary>Toggle fog of war match-wide from the host.</summary>
    public static void ToggleFog()
    {
        if (GameLoop.Instance == null)
        {
            Debug.LogWarning("[DevInput] No GameLoop; is a match running?");
            return;
        }

        SetFog(!GameLoop.Instance.FogOfWarEnabled);
    }

    /// <summary>Set the execution fast-forward multiplier (movement/shooting/physics).</summary>
    public static void SetSpeed(float multiplier)
    {
        GameLoop.devSpeedMultiplier = Mathf.Max(0.01f, multiplier);
        if (GameLoop.devMode)
            Time.timeScale = GameLoop.devSpeedMultiplier;
        Debug.Log($"[DevInput] speed={GameLoop.devSpeedMultiplier} timeScale={Time.timeScale}");
    }

    /// <summary>Restore real-time speed (Time.timeScale = 1).</summary>
    public static void ResetSpeed() => Time.timeScale = 1f;

    // ---- Plan queueing --------------------------------------------------------------------

    /// <summary>
    /// Queue a movement path for team/unit using flat col,row pairs, e.g.
    /// DevInput.SetPath(0, 0, 1,4, 1,5, 1,6). The unit's current cell is prepended automatically.
    /// </summary>
    public static void SetPath(int team, int unitIndex, params int[] colRowPairs)
    {
        var cells = new List<Vector2Int>();
        for (int i = 0; i + 1 < colRowPairs.Length; i += 2)
            cells.Add(new Vector2Int(colRowPairs[i], colRowPairs[i + 1]));
        SetPath(team, unitIndex, cells);
    }

    /// <summary>Queue a movement path for team/unit as a list of grid cells (col,row).</summary>
    public static void SetPath(
        int team,
        int unitIndex,
        List<Vector2Int> cells,
        bool ability = false
    )
    {
        GameObject unit = GetUnit(team, unitIndex);
        if (unit == null)
            return;

        var path = new List<Vector3> { GridSystem.GetNearestGridCell(unit) };
        if (cells != null)
            foreach (var c in cells)
                path.Add(GameLoop.gridCoordToWorld(c));

        pending[unit] = (ability, path);
        Debug.Log(
            $"[DevInput] Queued team {team} unit {unitIndex} ({unit.name}) path: "
                + string.Join(
                    " -> ",
                    path.Select(p =>
                    {
                        var g = GridSystem.ConvertToGridCoords(p);
                        return $"({g.x},{g.y})";
                    })
                )
        );
    }

    /// <summary>
    /// Queue an ability activation targeting grid cell (col,row) — the unit will use its ability
    /// instead of moving this round (the planning-phase move-or-ability choice). The opposing
    /// team gets a dodge window at the start of execution when its units are in response range.
    /// </summary>
    public static void SetAbility(int team, int unitIndex, int targetCol, int targetRow)
    {
        GameObject unit = GetUnit(team, unitIndex);
        if (unit == null)
            return;
        pending[unit] = (
            true,
            new List<Vector3>
            {
                GridSystem.GetNearestGridCell(unit),
                GameLoop.gridCoordToWorld(new Vector2Int(targetCol, targetRow)),
            }
        );
        Debug.Log(
            $"[DevInput] Queued ability at ({targetCol},{targetRow}) for team {team} unit {unitIndex}."
        );
    }

    /// <summary>Queue a self-targeted ability (e.g. Shield) — no target square needed.</summary>
    public static void SetAbility(int team, int unitIndex)
    {
        GameObject unit = GetUnit(team, unitIndex);
        if (unit == null)
            return;
        pending[unit] = (true, new List<Vector3> { GridSystem.GetNearestGridCell(unit) });
        Debug.Log($"[DevInput] Queued self-targeted ability for team {team} unit {unitIndex}.");
    }

    // ---- Dodge phase -----------------------------------------------------------------------

    private static readonly Dictionary<GameObject, List<Vector3>> pendingDodges = new();

    /// <summary>
    /// Queue a dodge dive path (flat col,row pairs) for an ALERTED unit during a dodge window
    /// (check Dump() — phase must be "dodging"). Dives are capped at the ability's diveRange
    /// and replace the unit's planned move.
    /// </summary>
    public static void SetDodgePath(int team, int unitIndex, params int[] colRowPairs)
    {
        GameObject unit = GetUnit(team, unitIndex);
        if (unit == null)
            return;
        var path = new List<Vector3> { GridSystem.GetNearestGridCell(unit) };
        for (int i = 0; i + 1 < colRowPairs.Length; i += 2)
            path.Add(GameLoop.gridCoordToWorld(new Vector2Int(colRowPairs[i], colRowPairs[i + 1])));
        pendingDodges[unit] = path;
        Debug.Log(
            $"[DevInput] Queued dodge for team {team} unit {unitIndex} ({path.Count - 1} step(s))."
        );
    }

    /// <summary>
    /// Submit queued dodges and end the dodge window NOW (dev-mode dodge windows wait
    /// indefinitely for this call). Call with nothing queued to decline dodging entirely.
    /// </summary>
    public static void SubmitDodge()
    {
        var gl = GameLoop.Instance;
        if (gl == null)
        {
            Debug.LogWarning("[DevInput] No GameLoop; is a match running?");
            return;
        }

        var dict = new PathsDict();
        foreach (var kv in pendingDodges)
            dict[kv.Key] = (false, kv.Value);

        gl.DevSubmitDodgeServer(dict);
        pendingDodges.Clear();
    }

    /// <summary>Give every unit without a queued plan a stationary (stay-put) plan.</summary>
    public static void AutoFillRemaining()
    {
        for (int t = 0; t < GameLoop.teamNames.Count; t++)
            ForEachUnit(
                t,
                (unit, i) =>
                {
                    if (!pending.ContainsKey(unit))
                        pending[unit] = (
                            false,
                            new List<Vector3> { GridSystem.GetNearestGridCell(unit) }
                        );
                }
            );
    }

    // ---- Submission -----------------------------------------------------------------------

    /// <summary>
    /// Submit all queued plans to the server and end the planning phase NOW. Dev-mode planning
    /// waits indefinitely until this is called, so the agent is never cut off; the moment it is
    /// called the round advances. The server auto-fills every unit/team the agent did not set with
    /// a no-move plan, so a single unit's path is enough to resolve the whole round.
    /// </summary>
    public static void SubmitPlans()
    {
        var gl = GameLoop.Instance;
        if (gl == null)
        {
            Debug.LogWarning(
                "[DevInput] No GameLoop yet; start a match and wait for the Game scene."
            );
            return;
        }

        var dict = new PathsDict();
        foreach (var kv in pending)
            dict[kv.Key] = (kv.Value.ability, kv.Value.path);

        gl.DevSubmitPlansServer(dict);
        Debug.Log(
            $"[DevInput] SubmitPlans() -> {dict.Count} explicit unit(s); server auto-fills the rest."
        );
        pending.Clear();
    }

    /// <summary>End the planning phase now with whatever is queued (all unset units stay put).</summary>
    public static void EndPlanning() => SubmitPlans();

    /// <summary>Alias of SubmitPlans: the server already auto-fills unset units, so this just submits.</summary>
    public static void ResolveRound() => SubmitPlans();

    // ---- Observation ----------------------------------------------------------------------

    /// <summary>Grid cell (col,row) of a unit, or (-1,-1) if unavailable.</summary>
    public static Vector2Int GridOf(int team, int unitIndex)
    {
        GameObject unit = GetUnit(team, unitIndex);
        return unit == null
            ? new Vector2Int(-1, -1)
            : GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));
    }

    /// <summary>Human-readable snapshot of dev state and every unit's cell (alive/dead). Return this.</summary>
    public static string Dump()
    {
        var sb = new StringBuilder();
        string fogState =
            GameLoop.Instance == null ? "n/a" : (GameLoop.Instance.FogOfWarEnabled ? "on" : "off");
        sb.AppendLine(
            $"devMode={GameLoop.devMode} phase={GameLoop.currentPhase} "
                + $"fog={fogState} "
                + $"speed={GameLoop.devSpeedMultiplier} timeScale={Time.timeScale} "
                + $"lastPlanningWait={GameLoop.lastPlanningSeconds:0.###}s"
        );

        // During a dodge window, show which units may dive (SetDodgePath + SubmitDodge).
        var alerted = GameLoop.Instance != null ? GameLoop.Instance.dodgeAlerted : null;
        if (alerted != null)
        {
            foreach (var kvp in alerted)
            {
                sb.AppendLine(
                    $"dodge window: team {kvp.Key} may dive: "
                        + string.Join(", ", kvp.Value.Select(u => u == null ? "<null>" : u.name))
                );
            }
        }

        var nm = NetworkManager.Singleton;
        sb.AppendLine(
            $"network: listening={nm?.IsListening} server={nm?.IsServer} clients={nm?.ConnectedClients?.Count}"
        );

        for (int t = 0; t < GameLoop.TeamCount; t++)
        {
            sb.AppendLine($"Team {t} ({GameLoop.teamNames[t]}):");
            ForEachUnit(
                t,
                (unit, i) =>
                {
                    bool alive = unit != null && unit.activeSelf;
                    Vector2Int c =
                        unit == null
                            ? new Vector2Int(-1, -1)
                            : GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));
                    sb.AppendLine(
                        $"  [{i}] {(unit == null ? "<null>" : unit.name)} cell=({c.x},{c.y}) alive={alive}"
                    );
                }
            );
        }
        return sb.ToString();
    }

    // ---- Internals ------------------------------------------------------------------------

    private static GameObject GetUnit(int team, int unitIndex)
    {
        if (team < 0 || team >= GameLoop.TeamCount)
        {
            Debug.LogWarning($"[DevInput] Team {team} not available (is a match running?).");
            return null;
        }
        GameObject[] units = GameLoop.GetTeamUnits(team);
        if (units.Length == 0)
        {
            Debug.LogWarning($"[DevInput] No unit objects for team {team}.");
            return null;
        }
        if (unitIndex < 0 || unitIndex >= units.Length)
        {
            Debug.LogWarning($"[DevInput] Unit index {unitIndex} out of range for team {team}.");
            return null;
        }
        return units[unitIndex];
    }

    private static void ForEachUnit(int team, System.Action<GameObject, int> action)
    {
        if (team < 0 || team >= GameLoop.TeamCount)
            return;
        GameObject[] units = GameLoop.GetTeamUnits(team);
        if (units.Length == 0)
            return;
        for (int i = 0; i < units.Length; i++)
            action(units[i], i);
    }
}

#if UNITY_EDITOR
/// <summary>
/// DEV: auto-starts an NGO host in the main editor on Play when gameplay dev mode was explicitly
/// retained (for example, with domain reload disabled). Player 2 still requires a fresh,
/// project-scoped directive for this Play session.
/// </summary>
public static class DevAutoHost
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        // Only the main editor hosts. Virtual players wait for an explicit session directive.
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            return;
        if (!GameLoop.devMode)
            return;

        var runner = new GameObject("DevAutoHost").AddComponent<DevAutoHostRunner>();
        Object.DontDestroyOnLoad(runner.gameObject);
    }
}

public class DevAutoHostRunner : MonoBehaviour
{
    IEnumerator Start()
    {
        // Give the boot scene a moment so NetworkManager is fully initialized before hosting.
        yield return new WaitForSecondsRealtime(0.5f);

        float deadline = Time.realtimeSinceStartup + 30f;
        while (Time.realtimeSinceStartup < deadline)
        {
            // Re-checked every iteration (not just once at spawn) so toggling dev mode off during
            // this window is honored immediately instead of racing ahead and hosting anyway.
            if (!GameLoop.devMode)
                yield break;

            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                if (nm.IsListening || nm.IsServer || nm.IsClient)
                    yield break;

                if (MatchOptions.Current.IsBotMatch)
                    DevMppmAutoJoin.DisableAutoJoin();
                else
                    DevMppmAutoJoin.EnableLocalAutoJoin();
                DevMppmAutoJoin.ConfigureLoopbackTransport(nm);
                Debug.Log("[DevAutoHost] Starting host...");
                if (!nm.StartHost())
                {
                    DevMppmAutoJoin.DisableAutoJoin();
                    Debug.LogError("[DevAutoHost] StartHost() failed.");
                }
                yield break;
            }
            yield return null;
        }
    }
}
#endif
