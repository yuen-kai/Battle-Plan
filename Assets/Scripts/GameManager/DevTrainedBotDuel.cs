#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// DEV: plays the trained bot (opponent seat) against the scripted bot (host seat, driven through
/// the dev submission hooks) over several matches, alternating fog, and leaves Play mode when done.
/// Poll <see cref="IsDone"/> and read <see cref="Report"/>.
/// </summary>
public static class DevTrainedBotDuel
{
    public static bool IsDone => DevTrainedBotDuelRunner.Instance != null && DevTrainedBotDuelRunner.Instance.Done;
    public static string Report => DevTrainedBotDuelRunner.Instance?.Report ?? "<duel not started>";

    public static void Run(int matches = 8, float speed = 10f, bool exitPlayWhenDone = true, bool record = false)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[DUEL] Enter Play mode first.");
            return;
        }
        if (DevTrainedBotDuelRunner.Instance != null)
            Object.Destroy(DevTrainedBotDuelRunner.Instance.gameObject);

        GameObject runner = new("DevTrainedBotDuelRunner");
        Object.DontDestroyOnLoad(runner);
        DevTrainedBotDuelRunner duel = runner.AddComponent<DevTrainedBotDuelRunner>();
        duel.matches = matches;
        duel.speed = speed;
        duel.exitPlayWhenDone = exitPlayWhenDone;
        duel.record = record;
    }
}

public sealed class DevTrainedBotDuelRunner : MonoBehaviour
{
    private const int MaxRoundsPerMatch = 30;
    private const float MatchTimeoutSeconds = 300f;
    private const int OriginalRosterSize = 5;

    public static DevTrainedBotDuelRunner Instance { get; private set; }

    public int matches;
    public float speed;
    public bool exitPlayWhenDone;
    public bool record;
    public string RecordingPath { get; private set; }
    public bool Done { get; private set; }
    public string Report { get; private set; } = "running";

    private readonly System.Random rng = new(7);
    private readonly List<string> lines = new();
    private readonly List<double> planningMilliseconds = new();
    private int trainedWins;
    private int classicWins;
    private int draws;
    private int errors;

    private void Awake()
    {
        Instance = this;
        Application.logMessageReceived += CountErrors;
    }

    private void OnDestroy()
    {
        StopRecording();
        Application.logMessageReceived -= CountErrors;
        if (Instance == this)
            Instance = null;
    }

    private void CountErrors(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors++;
    }

    private StreamWriter recording;

    private void StopRecording()
    {
        TrainedBotPlayer.DevDecisionRecorded = null;
        recording?.Dispose();
        recording = null;
    }

    private IEnumerator Start()
    {
        yield return null;
        GameLoop.devUseProductionSpawns = true;
        if (record)
        {
            RecordingPath = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "Training", "runs",
                $"unity-decisions-{System.DateTime.Now:yyyyMMdd-HHmmss}.jsonl"
            ));
            Directory.CreateDirectory(Path.GetDirectoryName(RecordingPath));
            recording = new StreamWriter(RecordingPath);
            TrainedBotPlayer.DevDecisionRecorded = recording.WriteLine;
        }

        for (int match = 0; match < matches; match++)
        {
            yield return TearDownMatch();
            MapDefinition map = MapCatalog.All[match % MapCatalog.All.Count];
            bool fog = match % 2 == 0;
            GameLoop.devHostRosterOverride = RandomRoster();
            GameLoop.devOpponentRosterOverride = RandomRoster();
            DevInput.StartBotMatch(speed, fog, map.Id, trainedBot: true);

            float readyDeadline = Time.realtimeSinceStartup + 90f;
            while (!MatchReady() && Time.realtimeSinceStartup < readyDeadline)
                yield return null;
            if (!MatchReady())
            {
                lines.Add($"match {match + 1}: never reached planning");
                continue;
            }

            GameLoop loop = GameLoop.Instance;
            BotPlayer classic = new(loop, GameLoop.HostTeamIndex);
            TrainedBotPlayer probe = new(loop, GameLoop.OpponentTeamIndex);
            int rounds = 0;
            int dodgedRound = -1;
            float matchDeadline = Time.realtimeSinceStartup + MatchTimeoutSeconds;
            while (
                GameLoop.Instance != null
                && GameLoop.currentPhase != GameLoop.Phase.Idle
                && rounds <= MaxRoundsPerMatch
                && Time.realtimeSinceStartup < matchDeadline
            )
            {
                if (GameLoop.currentPhase == GameLoop.Phase.Planning)
                {
                    if (!record)
                    {
                        Stopwatch timer = Stopwatch.StartNew();
                        probe.CreatePlanningContribution(loop.RoundNumber, out _);
                        planningMilliseconds.Add(timer.Elapsed.TotalMilliseconds);
                    }
                    loop.DevSubmitPlansServer(classic.CreatePlanningContribution(loop.RoundNumber));
                    rounds++;
                    while (GameLoop.currentPhase == GameLoop.Phase.Planning && Time.realtimeSinceStartup < matchDeadline)
                        yield return null;
                }
                if (GameLoop.currentPhase == GameLoop.Phase.Dodging && dodgedRound != loop.RoundNumber)
                {
                    dodgedRound = loop.RoundNumber;
                    PathsDict dives = new();
                    if (
                        loop.dodgeAlerted != null
                        && loop.dodgeAlerted.TryGetValue(GameLoop.HostTeamIndex, out HashSet<GameObject> alerted)
                    )
                    {
                        dives = classic.CreateDodgeContribution(alerted, loop.DevDodgeActivations.ToList(), loop.DevMaxDiveRange);
                    }
                    loop.DevSubmitDodgeServer(dives);
                }
                yield return null;
            }

            MatchResult? result = loop != null ? loop.LastMatchResult : null;
            string outcome;
            if (result is { HasWinner: true } decided)
            {
                bool trainedWon = decided.WinningTeamIndex == GameLoop.OpponentTeamIndex;
                if (trainedWon)
                    trainedWins++;
                else
                    classicWins++;
                outcome = trainedWon ? "trained bot won" : "scripted bot won";
            }
            else
            {
                draws++;
                outcome = result.HasValue ? "draw" : "unfinished (round cap or timeout)";
            }
            lines.Add(
                $"match {match + 1} {map.DisplayName} fog {(fog ? "on" : "off")}: {outcome} after {rounds} rounds, "
                    + $"trained abilities {loop?.BotAbilityContributionCount ?? 0}, dodges {loop?.BotDodgeContributionCount ?? 0}"
            );
            Debug.Log("[DUEL] " + lines[^1]);
        }

        GameLoop.devUseProductionSpawns = false;
        GameLoop.devHostRosterOverride = null;
        GameLoop.devOpponentRosterOverride = null;
        StopRecording();
        yield return TearDownMatch();
        Time.timeScale = 1f;

        double meanMs = planningMilliseconds.Count > 0 ? planningMilliseconds.Average() : 0;
        double maxMs = planningMilliseconds.Count > 0 ? planningMilliseconds.Max() : 0;
        lines.Add(
            $"RESULT: trained {trainedWins} / scripted {classicWins} / draws {draws}; "
                + $"planning {meanMs:F0} ms mean, {maxMs:F0} ms max; errors {errors}"
                + (RecordingPath != null ? $"; decisions in {RecordingPath}" : "")
        );
        Report = string.Join("\n", lines);
        Debug.Log("[DUEL] " + lines[^1]);
        Done = true;
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
            && GameLoop.GetTeamUnits(GameLoop.OpponentTeamIndex).Length == RosterRules.UnitsPerPlayer;
    }

    private int[] RandomRoster()
    {
        return Enumerable.Range(0, RosterRules.UnitsPerPlayer).Select(_ => rng.Next(OriginalRosterSize)).ToArray();
    }

    private static IEnumerator TearDownMatch()
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
                && (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress)
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
        float sceneDeadline = Time.realtimeSinceStartup + 10f;
        while (NetworkManager.Singleton == null && Time.realtimeSinceStartup < sceneDeadline)
            yield return null;
    }
}
#endif
