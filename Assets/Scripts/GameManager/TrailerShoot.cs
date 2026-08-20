#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-command trailer shoot: enters Play, stands up a bot match, films every
/// <see cref="TrailerStudio"/> beat, writes a report and leaves Play again.
///
/// The whole session is scripted up front because Play mode is the expensive resource here — the
/// editor must never sit running while the next step is being decided. Nothing waits on a fixed
/// sleep either; each stage polls the condition that actually gates it and the run stops itself the
/// moment the last beat lands.
/// </summary>
public static class TrailerShoot
{
    const string AutoRunKey = "BattlePlan.TrailerShoot.AutoRun";
    const string TagKey = "BattlePlan.TrailerShoot.Tag";
    const string OnlyKey = "BattlePlan.TrailerShoot.Only";
    const string MapKey = "BattlePlan.TrailerShoot.Map";
    const string ScaleKey = "BattlePlan.TrailerShoot.Scale";
    const string ReportPath = "Captures/Trailer/shoot-report.txt";

    // Entering Play reloads the domain, so the shot selection has to outlive statics to be
    // configurable from outside the editor.
    /// <summary>Tag the frames land under, so an earlier take is never silently overwritten.</summary>
    public static string Tag
    {
        get => UnityEditor.SessionState.GetString(TagKey, "r1");
        set => UnityEditor.SessionState.SetString(TagKey, value);
    }

    /// <summary>Beat names to film; empty films the whole shot list.</summary>
    public static string[] Only
    {
        get
        {
            string packed = UnityEditor.SessionState.GetString(OnlyKey, "");
            return string.IsNullOrEmpty(packed)
                ? System.Array.Empty<string>()
                : packed.Split(',');
        }
        set =>
            UnityEditor.SessionState.SetString(
                OnlyKey,
                value == null ? "" : string.Join(",", value)
            );
    }

    /// <summary>Capture size multiplier; 2 films at 4K so the edit can punch in losslessly.</summary>
    public static int Scale
    {
        get => Mathf.Clamp(UnityEditor.SessionState.GetInt(ScaleKey, 1), 1, 2);
        set => UnityEditor.SessionState.SetInt(ScaleKey, Mathf.Clamp(value, 1, 2));
    }

    /// <summary>Board the shoot plays on.</summary>
    public static MapId Map
    {
        get => (MapId)UnityEditor.SessionState.GetInt(MapKey, (int)MatchOptions.Default.mapId);
        set => UnityEditor.SessionState.SetInt(MapKey, (int)value);
    }

    /// <summary>Configures and launches a shoot in one call, for driving from outside the editor.</summary>
    public static void Shoot(string tag, params string[] beats)
    {
        Tag = tag;
        Only = beats;
        RunFromMenu();
    }

    /// <summary>Same, on a named board.</summary>
    public static void ShootOn(string tag, MapId map, params string[] beats)
    {
        Map = map;
        Shoot(tag, beats);
    }

    /// <summary>Same, on a named board, at a capture size multiplier.</summary>
    public static void ShootBig(string tag, MapId map, int scale, params string[] beats)
    {
        Scale = scale;
        ShootOn(tag, map, beats);
    }

    public static void Run()
    {
        if (TrailerShootRunner.Instance != null)
            return;

        GameObject runner = new("TrailerShootRunner");
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<TrailerShootRunner>();
    }

    [UnityEditor.MenuItem("Battle Plan/Shoot Trailer")]
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
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            return;
        if (!UnityEditor.SessionState.GetBool(AutoRunKey, false))
            return;

        UnityEditor.SessionState.EraseBool(AutoRunKey);
        Run();
    }

    internal static void WriteReport(string body)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
        File.WriteAllText(ReportPath, body);
    }
}

public sealed class TrailerShootRunner : MonoBehaviour
{
    public static TrailerShootRunner Instance { get; private set; }

    readonly StringBuilder report = new();

    void Awake() => Instance = this;

    IEnumerator Start()
    {
        report.AppendLine("=== Trailer shoot ===");

        yield return Bootstrap();
        yield return Shoot();

        report.AppendLine(
            TrailerStudio.Finished && string.IsNullOrEmpty(TrailerStudio.LastError)
                ? $"RESULT: PASS ({TrailerStudio.FramesWritten} frames -> {TrailerStudio.LastRunDir})"
                : $"RESULT: FAIL ({TrailerStudio.LastError})"
        );

        string body = report.ToString();
        TrailerShoot.WriteReport(body);
        Debug.Log("[Trailer] " + body);

        // Leave the editor stopped: a shoot that finishes should not keep the machine busy.
        UnityEditor.EditorApplication.isPlaying = false;
    }

    IEnumerator Bootstrap()
    {
        if (NetworkManager.Singleton == null)
        {
            SceneManager.LoadScene("JoinGame", LoadSceneMode.Single);
            yield return WaitFor(() => NetworkManager.Singleton != null, 30f, "NetworkManager");
        }

        // Production spawns, dev-speed rounds. Dev mode has to stay on — it is what stands the
        // match up without a menu and lets a round advance the moment plans are submitted — but
        // its deployment starts the crews almost on top of each other, which is the wrong opening
        // to film. This asks for the real one.
        //
        // A live take is otherwise deterministic: the same board plays out the same way every
        // time. Changing the map is the honest way to get a different match rather than a
        // different advantage, so which board to play is part of the shoot config.
        // Every wall shows the plain block silhouette. The container and stack variants are depot
        // detail that reads as black striping from the board camera's distance.
        CoverVariant.devForcedVariant = 0;

        GameLoop.devUseProductionSpawns = true;
        GameLoop.devMode = true;

        // Real time, not the dev fast-forward. GameLoop drives Time.timeScale from this the moment
        // execution starts, overriding the capture rig, so at its default of 6 the footage is the
        // game at six times speed and a whole round goes by in about three seconds.
        GameLoop.devSpeedMultiplier = 1f;
        DevMppmAutoJoin.DisableAutoJoin();
        MatchOptions options = MatchOptions.Default;

        // King of the Hill, not Elimination, because of where it puts the fighting. Both crews
        // deploy along opposite edges, so in Elimination each unit simply engages whoever is
        // opposite it and the round becomes four or five separate duels strung out from one end of
        // the board to the other — impossible to frame, since half the action is at the top of the
        // screen and half at the bottom. The hill gives both sides the same reason to converge on
        // the middle, so the fight happens in one place and a single shot can hold all of it.
        options.gameMode = GameMode.KingOfTheHill;
        options.opponentType = OpponentType.AI;
        // Fog is a per-beat decision inside the rig, so the match itself starts without it.
        options.fogOfWar = false;
        options.mapId = TrailerShoot.Map;
        MatchOptions.SetCurrent(options);
        GameLoop.ResetMatchState();
        DevInput.StartHost();

        yield return WaitFor(
            () =>
                GameLoop.Instance != null
                && GameLoop.currentPhase == "planning"
                && GameLoop.GetTeamUnits(0).Length == RosterRules.UnitsPerPlayer
                && GameLoop.GetTeamUnits(1).Length == RosterRules.UnitsPerPlayer,
            90f,
            "bot match in planning with both crews spawned"
        );
    }

    IEnumerator Shoot()
    {
        if (GameLoop.Instance == null)
        {
            report.AppendLine("no live match; nothing filmed");
            yield break;
        }

        int scale = TrailerShoot.Scale;
        TrailerStudio.FrameWidth = 1920 * scale;
        TrailerStudio.FrameHeight = 1080 * scale;

        if (TrailerShoot.Only != null && TrailerShoot.Only.Length > 0)
            TrailerStudio.Capture(TrailerShoot.Tag, TrailerShoot.Only);
        else
            TrailerStudio.CaptureAll(TrailerShoot.Tag);

        // Dev-mode planning waits indefinitely, so the board stays parked while the rig films and
        // the only thing worth polling is the rig's own completion flag.
        while (!TrailerStudio.Finished)
            yield return null;

        foreach (string line in TrailerStudio.Log)
            report.AppendLine("  " + line);
    }

    IEnumerator WaitFor(System.Func<bool> condition, float timeoutSeconds, string what)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;

        report.AppendLine(condition() ? $"  ok: {what}" : $"  TIMEOUT: {what}");
    }
}
#endif
