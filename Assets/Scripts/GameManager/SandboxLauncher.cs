#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gets the sandbox onto the board. One menu item enters Play mode, stands up a loopback host and
/// loads straight onto a board built from the last setup the designer left behind; everything after
/// that happens in <see cref="SandboxPanel"/> on the running match.
///
/// The setup itself is remembered in <c>EditorPrefs</c> rather than in session state, so a board
/// worth coming back to survives closing the editor.
/// </summary>
public static class SandboxLauncher
{
    private const string AutoRunKey = "BattlePlan.Sandbox.AutoRun";
    private const string SetupKey = "BattlePlan.Sandbox.Setup";

    public static bool IsRunning => Application.isPlaying && SandboxSession.IsActive;

    [UnityEditor.MenuItem("Battle Plan/Sandbox")]
    public static void Launch()
    {
        if (Application.isPlaying)
        {
            BeginSessionAndLoadJoinScene();
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
        BeginSessionAndLoadJoinScene();
    }

    private static void BeginSessionAndLoadJoinScene()
    {
        if (SandboxLaunchRunner.Instance != null)
            Object.Destroy(SandboxLaunchRunner.Instance.gameObject);

        GameObject runner = new("SandboxLaunchRunner");
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<SandboxLaunchRunner>();
    }

    internal static void BeginSessionFromStoredSetup()
    {
        LoadSetup();
        SandboxSession.Begin();
    }

    /// <summary>
    /// Writes both crews down as <c>unit:col:row,… |immortal</c> per team. A hand-editable line
    /// rather than serialized JSON, because the only thing that ever reads it is this file.
    /// </summary>
    public static void SaveSetup()
    {
        StringBuilder setup = new();
        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            SandboxCrew crew = SandboxSession.Crew(teamIndex);
            if (teamIndex > 0)
                setup.Append(';');
            for (int slot = 0; slot < crew.Count; slot++)
            {
                if (slot > 0)
                    setup.Append(',');
                setup.Append(crew.unitIndices[slot])
                    .Append(':')
                    .Append(crew.cells[slot].x)
                    .Append(':')
                    .Append(crew.cells[slot].y);
            }
            setup.Append('|').Append(crew.immortal ? '1' : '0');
        }
        UnityEditor.EditorPrefs.SetString(SetupKey, setup.ToString());
    }

    private static void LoadSetup()
    {
        string stored = UnityEditor.EditorPrefs.GetString(SetupKey, string.Empty);
        if (string.IsNullOrEmpty(stored))
            return;

        string[] teams = stored.Split(';');
        for (int teamIndex = 0; teamIndex < teams.Length && teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            if (!TryParseCrew(teams[teamIndex], out List<(int unit, Vector2Int cell)> units, out bool immortal))
                continue;

            SandboxCrew crew = SandboxSession.Crew(teamIndex);
            crew.unitIndices.Clear();
            crew.cells.Clear();
            crew.immortal = immortal;
            foreach ((int unit, Vector2Int cell) entry in units)
            {
                crew.unitIndices.Add(entry.unit);
                crew.cells.Add(entry.cell);
            }
        }
    }

    private static bool TryParseCrew(
        string encoded,
        out List<(int unit, Vector2Int cell)> units,
        out bool immortal
    )
    {
        units = new List<(int, Vector2Int)>();
        immortal = false;

        string[] halves = encoded.Split('|');
        immortal = halves.Length > 1 && halves[1] == "1";
        foreach (string entry in halves[0].Split(','))
        {
            string[] parts = entry.Split(':');
            if (
                parts.Length != 3
                || !int.TryParse(parts[0], out int unitIndex)
                || !int.TryParse(parts[1], out int column)
                || !int.TryParse(parts[2], out int row)
            )
            {
                continue;
            }

            Vector2Int cell = new(column, row);
            if (!SandboxSession.IsCellPlaceable(cell))
                continue;
            units.Add((unitIndex, cell));
            if (units.Count == SandboxSession.MaxUnitsPerTeam)
                break;
        }
        return units.Count > 0;
    }
}

internal sealed class SandboxLaunchRunner : MonoBehaviour
{
    private const float ShutdownTimeoutSeconds = 10f;

    public static SandboxLaunchRunner Instance { get; private set; }

    private void Awake() => Instance = this;

    private System.Collections.IEnumerator Start()
    {
        SandboxSession.End();

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            if (networkManager.IsServer)
                NetworkHelper.CleanupAllNetworkObjects();
            networkManager.Shutdown();
            MatchOptions.Reset();
            GameLoop.ResetMatchState();

            float deadline = Time.realtimeSinceStartup + ShutdownTimeoutSeconds;
            while (
                NetworkManager.Singleton != null
                && (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress)
            )
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    Debug.LogWarning(
                        "[Sandbox] Previous match did not shut down in time; launching anyway."
                    );
                    break;
                }
                yield return null;
            }
        }

        SandboxLauncher.BeginSessionFromStoredSetup();

        MatchOptions.SetCurrent(SandboxSession.BuildMatchOptions());
        SceneManager.LoadScene("JoinGame", LoadSceneMode.Single);

        Instance = null;
        Destroy(gameObject);
    }
}
#endif
