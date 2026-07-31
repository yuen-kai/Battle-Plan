#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Drives the sandboxed tutorial end to end from the title screen: launches it, plays the three
/// scripted lessons by writing plans straight into the client planner, and checks that each
/// coaching prompt appeared at the moment it was meant to. Self-stopping, so a verification run
/// never leaves the editor sitting in Play mode.
/// </summary>
public static class DevTutorialE2ETest
{
    private const string AutoRunKey = "BattlePlan.TutorialE2E.AutoRun";

    /// <summary>
    /// Exiting Play mode reloads the domain and takes the console buffer with it, so the report is
    /// parked in session state where it can still be read once the editor is back.
    /// </summary>
    public const string LastReportKey = "BattlePlan.TutorialE2E.LastReport";

    public static string LastReport => UnityEditor.SessionState.GetString(LastReportKey, string.Empty);

    public static bool IsDone =>
        DevTutorialE2ETestRunner.Instance != null && DevTutorialE2ETestRunner.Instance.Done;
    public static bool Passed => IsDone && DevTutorialE2ETestRunner.Instance.FailCount == 0;
    public static string Report =>
        DevTutorialE2ETestRunner.Instance == null
            ? "<tutorial E2E not started>"
            : DevTutorialE2ETestRunner.Instance.BuildReport();

    public static void Run(bool stopWhenDone = false)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[TUT-E2E] Enter Play mode first.");
            return;
        }
        if (DevTutorialE2ETestRunner.Instance != null)
        {
            if (!DevTutorialE2ETestRunner.Instance.Done)
                return;
            Object.Destroy(DevTutorialE2ETestRunner.Instance.gameObject);
        }

        GameObject runner = new("DevTutorialE2ETestRunner");
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<DevTutorialE2ETestRunner>().StopWhenDone = stopWhenDone;
    }

    [UnityEditor.MenuItem("Battle Plan/Run Tutorial Sandbox Test")]
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
        Run(true);
    }
}

public sealed class DevTutorialE2ETestRunner : MonoBehaviour
{
    public static DevTutorialE2ETestRunner Instance { get; private set; }
    public bool Done { get; private set; }
    public bool StopWhenDone { get; set; }
    public int PassCount { get; private set; }
    public int FailCount { get; private set; }

    private readonly List<string> reportLines = new();
    private readonly HashSet<string> observedLessons = new();
    private bool timedOut;

    private void Awake()
    {
        Instance = this;
    }

    private IEnumerator Start()
    {
        yield return RunSuite();
        Done = true;
        string report = BuildReport();
        UnityEditor.SessionState.SetString(DevTutorialE2ETest.LastReportKey, report);
        Debug.Log("[TUT-E2E] " + report);

        if (StopWhenDone)
            UnityEditor.EditorApplication.isPlaying = false;
    }

    /// <summary>
    /// Executions run at wall-clock speed in a normal match, which makes an unattended verification
    /// run look like a hung editor. GameLoop only writes timeScale at the top of each round, so
    /// re-applying it here holds for the execution without fighting the round loop.
    /// </summary>
    private const float ExecutionFastForward = 3f;

    private void Update()
    {
        string lesson = LessonText();
        if (!string.IsNullOrEmpty(lesson))
            observedLessons.Add(lesson);

        if (GameLoop.currentPhase == "executing" && Time.timeScale < ExecutionFastForward)
            Time.timeScale = ExecutionFastForward;
    }

    public string BuildReport()
    {
        StringBuilder report = new();
        report.AppendLine("=== Tutorial sandbox end-to-end test ===");
        foreach (string line in reportLines)
            report.AppendLine(line);
        report.AppendLine(
            Done
                ? $"RESULT: {(FailCount == 0 ? "PASS" : "FAIL")} ({PassCount} passed, {FailCount} failed)"
                : $"RESULT: running ({PassCount} passed, {FailCount} failed)"
        );
        return report.ToString();
    }

    private void Check(bool condition, string message, string detail = null)
    {
        if (condition)
        {
            PassCount++;
            reportLines.Add($"  PASS  {message}");
            return;
        }

        FailCount++;
        reportLines.Add($"  FAIL  {message}{(detail == null ? string.Empty : $" [{detail}]")}");
    }

    private IEnumerator WaitFor(System.Func<bool> predicate, float timeoutSeconds, string label)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!predicate() && Time.realtimeSinceStartup < deadline)
            yield return null;

        timedOut = !predicate();
        if (timedOut)
            reportLines.Add($"  ....  timed out waiting for {label}");
    }

    private static VisualElement HudRoot()
    {
        UIDocument document =
            GameHUDController.Instance != null
                ? GameHUDController.Instance.GetComponent<UIDocument>()
                : null;
        return document != null ? document.rootVisualElement : null;
    }

    private static string CoachText()
    {
        return HudRoot()?.Q<Label>("coach-prompt-label")?.text ?? string.Empty;
    }

    private static string LessonText()
    {
        return HudRoot()?.Q<Label>("lesson-popup-label")?.text ?? string.Empty;
    }

    private static bool LessonCardVisible()
    {
        VisualElement card = HudRoot()?.Q<VisualElement>("lesson-popup");
        return card != null && !card.ClassListContains("hidden");
    }

    private static string LessonDismissLabel()
    {
        return HudRoot()?.Q<Button>("lesson-popup-button")?.text ?? string.Empty;
    }

    private static bool DismissLesson()
    {
        Button button = HudRoot()?.Q<Button>("lesson-popup-button");
        if (button == null)
            return false;

        using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
        {
            submit.target = button;
            button.SendEvent(submit);
        }
        return true;
    }

    private static GameObject Unit(int teamIndex)
    {
        return GameLoop.GetTeamUnits(teamIndex).FirstOrDefault();
    }

    private static Vector2Int Cell(GameObject unit)
    {
        return GridSystem.ConvertToGridCoords(unit.transform.position);
    }

    private static int GridDistance(Vector2Int from, Vector2Int to)
    {
        return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
    }

    private static bool Alive(GameObject unit)
    {
        return unit != null && unit.GetComponent<Health>()?.IsAlive == true;
    }

    private static void SetMovementPlan(GameObject unit, params Vector2Int[] cells)
    {
        List<Vector3> path = new() { GameLoop.gridCoordToWorld(Cell(unit)) };
        path.AddRange(cells.Select(GameLoop.gridCoordToWorld));
        PlanMovement.Instance.plans[unit] = (false, path);
    }

    private static void SetAbilityPlan(GameObject unit, Vector2Int target)
    {
        PlanMovement.Instance.plans[unit] = (
            true,
            new List<Vector3>
            {
                GameLoop.gridCoordToWorld(Cell(unit)),
                GameLoop.gridCoordToWorld(target),
            }
        );
    }

    /// <summary>
    /// Walks the alerted unit directly away from the thrower, as far as the dive allows, stopping
    /// at the first cell that is out of bounds, walled, or already taken.
    /// </summary>
    private static bool TryDive(GameObject unit, GameObject thrower)
    {
        PlanMovement planner = PlanMovement.Instance;
        if (planner == null || unit == null || thrower == null)
            return false;
        if (!planner.plans.ContainsKey(unit))
            return false;

        Vector2Int from = Cell(unit);
        Vector2Int away = from - Cell(thrower);
        Vector2Int step =
            Mathf.Abs(away.x) >= Mathf.Abs(away.y)
                ? new Vector2Int(away.x >= 0 ? 1 : -1, 0)
                : new Vector2Int(0, away.y >= 0 ? 1 : -1);

        List<Vector3> path = new() { GameLoop.gridCoordToWorld(from) };
        Vector2Int cursor = from;
        for (int i = 0; i < 2; i++)
        {
            Vector2Int next = cursor + step;
            if (!GridSystem.IsCellInBounds(next) || GameLoop.wallLayout.Contains(next))
                break;
            cursor = next;
            path.Add(GameLoop.gridCoordToWorld(cursor));
        }

        if (path.Count <= 1)
            return false;

        planner.plans[unit] = (false, path);
        return true;
    }

    private IEnumerator WaitForPhase(string phase, float timeoutSeconds)
    {
        yield return WaitFor(() => GameLoop.currentPhase == phase, timeoutSeconds, $"phase {phase}");
    }

    private IEnumerator RunSuite()
    {
        // === Launch from the title screen ===
        SceneManager.LoadScene("Title Screen", LoadSceneMode.Single);
        yield return WaitFor(
            () => Object.FindFirstObjectByType<TitleScreenUIController>() != null,
            30f,
            "title screen"
        );
        if (timedOut)
        {
            Check(false, "title screen loaded");
            yield break;
        }

        TitleScreenUIController title = Object.FindFirstObjectByType<TitleScreenUIController>();
        VisualElement titleRoot = title.GetComponent<UIDocument>()?.rootVisualElement;
        Button tutorialButton = titleRoot?.Q<Button>("tutorial-button");
        Check(tutorialButton != null, "title screen exposes a tutorial button");
        Check(
            tutorialButton?.Q<Label>()?.text == "Tutorial",
            "tutorial button is labelled for the player"
        );
        if (tutorialButton == null)
            yield break;

        typeof(TitleScreenUIController)
            .GetMethod("StartTutorial", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(title, null);

        yield return WaitFor(
            () =>
                GameLoop.Instance != null
                && GameLoop.currentPhase == "planning"
                && GameLoop.GetTeamUnits(GameLoop.HostTeamIndex).Length == 1
                && GameLoop.GetTeamUnits(GameLoop.OpponentTeamIndex).Length == 1,
            120f,
            "tutorial match reaching planning"
        );
        Check(!timedOut, "tutorial reached planning with one unit a side");
        if (timedOut)
            yield break;

        // === Board and chrome ===
        GameObject player = Unit(GameLoop.HostTeamIndex);
        GameObject enemy = Unit(GameLoop.OpponentTeamIndex);
        GameLoop loop = GameLoop.Instance;

        Check(
            NetworkManager.Singleton.ConnectedClients.Count == 1,
            "sandbox runs as a single-client host"
        );
        Check(loop.Options.IsBotMatch, "sandbox uses the AI match path");
        Check(!loop.FogOfWarEnabled, "fog is off so the lesson stays visible");
        Check(
            Cell(player) == TutorialSession.PlayerSpawn && Cell(enemy) == TutorialSession.EnemySpawn,
            "crews spawned on the scripted cells",
            $"player {Cell(player)} enemy {Cell(enemy)}"
        );
        Check(
            string.IsNullOrEmpty(HudRoot()?.Q<Label>("timer-label")?.text),
            "planning countdown is hidden while the student reads"
        );
        Check(
            HudRoot()
                ?.Q<VisualElement>("unit-cards")
                ?.Children()
                .Count(card => !card.ClassListContains("hidden")) == 1,
            "only the fielded unit keeps a card"
        );

        // === Lesson one: draw a route, then lock in ===
        // Prompts are pushed from the director's own Update, so every prompt assertion waits a
        // beat rather than sampling the exact frame a phase flipped.
        yield return WaitFor(
            () => CoachText() == TutorialDirector.DrawRoutePrompt,
            5f,
            "the opening prompt"
        );
        Check(!timedOut, "opens by asking for a route", CoachText());

        Vector2Int lane = TutorialSession.PlayerSpawn;
        SetMovementPlan(
            player,
            new Vector2Int(lane.x, lane.y + 1),
            new Vector2Int(lane.x, lane.y + 2),
            new Vector2Int(lane.x, lane.y + 3)
        );
        yield return null;
        yield return null;
        Check(
            CoachText() == TutorialDirector.LockInPrompt,
            "a drawn route asks for the lock-in",
            CoachText()
        );

        Check(PlanMovement.Instance.TryLockIn(), "orders submitted");
        yield return WaitForPhase("executing", 60f);
        Check(!timedOut, "round one executed");
        if (timedOut)
            yield break;

        yield return WaitFor(
            () => observedLessons.Contains(TutorialDirector.AutoShootLesson),
            15f,
            "the automatic fire lesson"
        );
        Check(!timedOut, "automatic fire is explained on the lesson card while it happens");
        Check(
            CoachText() != TutorialDirector.AutoShootLesson,
            "lessons stay on the card instead of the instruction line"
        );
        Check(LessonDismissLabel() == "Got it", "the card is dismissed by the player, not a clock");

        Check(DismissLesson(), "the lesson card can be dismissed");
        yield return null;
        Check(!LessonCardVisible(), "dismissing clears the lesson card");

        // === Lesson two: the ability, and watching the opponent step clear ===
        yield return WaitForPhase("planning", 90f);
        Check(!timedOut, "round two reached planning");
        if (timedOut)
            yield break;

        yield return null;
        Check(
            CoachText() == TutorialDirector.UseAbilityPrompt,
            "the second round asks for the ability",
            CoachText()
        );

        Vector2Int enemyCellBeforeThrow = Cell(enemy);
        SetAbilityPlan(player, enemyCellBeforeThrow);
        yield return null;
        yield return null;
        Check(
            CoachText() == TutorialDirector.LockInPrompt,
            "a targeted ability asks for the lock-in",
            CoachText()
        );
        Check(PlanMovement.Instance.TryLockIn(), "ability orders submitted");

        yield return WaitFor(
            () => GameLoop.currentPhase == "dodging" || GameLoop.currentPhase == "executing",
            60f,
            "the ability round resolving"
        );
        Check(
            GameLoop.currentPhase == "dodging",
            "throwing at the opponent opened a dodge window for them",
            GameLoop.currentPhase
        );

        yield return WaitForPhase("executing", 60f);
        Check(!timedOut, "round two executed");
        if (timedOut)
            yield break;

        yield return WaitFor(
            () => observedLessons.Contains(TutorialDirector.EnemyDodgedLesson),
            15f,
            "the dodged-ability lesson"
        );
        Check(!timedOut, "the opponent's dodge is explained after the throw");

        Check(DismissLesson(), "the dodged-ability card can be dismissed");

        // === Lesson three: the scripted opponent throws back ===
        yield return WaitForPhase("planning", 90f);
        Check(!timedOut, "round three reached planning");
        if (timedOut)
            yield break;

        Check(Alive(player) && Alive(enemy), "both crews survived to the dodge lesson");

        // Measured now rather than at execution start, because the dive is movement and takes the
        // round to play out.
        Vector2Int enemyAfterDive = Cell(enemy);
        int abilityReach = enemy.GetComponent<Movement>().unitData.abilitySquareRange;
        string dive = $"{enemyCellBeforeThrow} -> {enemyAfterDive}, player {Cell(player)}";
        Check(enemyAfterDive != enemyCellBeforeThrow, "the opponent actually dived", dive);
        Check(
            Vector2Int.Distance(enemyAfterDive, enemyCellBeforeThrow)
                > enemy.GetComponent<Movement>().unitData.abilityRadius,
            "the dive cleared the blast rather than shuffling inside it",
            dive
        );
        // The point of biasing the dive toward the student: whatever cell it lands on has to keep
        // the opponent's own throw in range for the next round.
        Check(
            GridDistance(enemyAfterDive, Cell(player)) <= abilityReach,
            "the dive left the opponent in range to throw back",
            $"{dive}, reach {abilityReach}"
        );

        SetMovementPlan(player);
        Check(PlanMovement.Instance.TryLockIn(), "holding orders submitted");

        yield return WaitFor(
            () => GameLoop.currentPhase == "dodging" || GameLoop.currentPhase == "executing",
            60f,
            "the opponent's throw"
        );
        Check(
            GameLoop.currentPhase == "dodging",
            "the scripted opponent threw its own ability at the student",
            GameLoop.currentPhase
        );
        Check(
            loop.dodgeAlerted?.ContainsKey(GameLoop.HostTeamIndex) == true,
            "the student's unit was alerted to dodge"
        );

        yield return WaitFor(
            () => CoachText() == TutorialDirector.DodgeNowPrompt,
            30f,
            "the dodge instruction"
        );
        Check(!timedOut, "the dodge instruction appeared inside the window");

        // Answer the alert the way a player would: the dodge session takes a movement path for the
        // alerted unit and submits it when the window closes.
        Check(TryDive(player, enemy), "a dive was drawn away from the blast");

        // === The sandbox closes itself once the last lesson has been read ===
        yield return WaitFor(
            () => LessonText() == TutorialDirector.ClosingLesson && LessonCardVisible(),
            120f,
            "the closing lesson"
        );
        Check(!timedOut, "the closing lesson is shown on the card");
        if (timedOut)
            yield break;

        Check(LessonDismissLabel() == "Finish", "the closing card offers to finish the tutorial");

        // Nothing may end the sandbox on a clock: it waits here until the card is dismissed.
        float held = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < held && !loop.LastMatchResult.HasValue)
            yield return null;
        Check(
            !loop.LastMatchResult.HasValue && LessonCardVisible(),
            "the closing card waits for the player instead of timing out"
        );

        Check(DismissLesson(), "the closing card can be dismissed");
        yield return WaitFor(
            () => loop.LastMatchResult.HasValue,
            30f,
            "the tutorial ending itself"
        );
        Check(!timedOut, "the tutorial ends once everything has been taught");
        Check(
            Alive(player) && Alive(enemy),
            "neither crew can be eliminated inside the sandbox",
            $"player {player.GetComponent<Health>()?.CurrentHealth} "
                + $"enemy {enemy.GetComponent<Health>()?.CurrentHealth}"
        );
        Check(
            loop.LastMatchResult?.Reason == MatchResultReason.TutorialComplete,
            "the sandbox reports its own completion rather than a win",
            loop.LastMatchResult?.Reason.ToString()
        );
        Check(
            HudRoot()?.Q<Label>("results-status")?.text == "Tutorial complete.",
            "the results overlay reads as a finished tutorial",
            HudRoot()?.Q<Label>("results-status")?.text
        );
        Check(TutorialSession.HasCompleted, "completion is remembered for next launch");

        // === Play again replays the tutorial, not a crew-selection screen ===
        Button playAgain = HudRoot()?.Q<Button>("play-again-button");
        Check(playAgain != null, "the results overlay offers to play again");
        if (playAgain == null)
            yield break;

        using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
        {
            submit.target = playAgain;
            playAgain.SendEvent(submit);
        }

        yield return WaitFor(
            () =>
                SceneManager.GetActiveScene().name == "Game"
                && GameLoop.Instance != null
                && GameLoop.currentPhase == "planning",
            90f,
            "the tutorial replaying"
        );
        Check(!timedOut, "play again restarts the tutorial rather than crew selection");
        if (timedOut)
            yield break;

        Check(TutorialSession.IsActive, "the replay runs as a sandbox again");
        Check(
            GameLoop.GetTeamUnits(GameLoop.HostTeamIndex).Length == 1
                && Cell(Unit(GameLoop.HostTeamIndex)) == TutorialSession.PlayerSpawn,
            "the replay redeploys the one-unit board"
        );
        yield return WaitFor(
            () => CoachText() == TutorialDirector.DrawRoutePrompt,
            5f,
            "the replayed opening prompt"
        );
        Check(!timedOut, "the replay starts the script from the beginning", CoachText());
    }
}
#endif
