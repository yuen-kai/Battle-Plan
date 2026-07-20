#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Focused one-client gameplay verification for the authoritative bot path, including an
/// optional live Game HUD picking regression before the autonomous rounds.
/// </summary>
public static class DevBotE2ETest
{
    private const string AutoRunKey = "BattlePlan.BotE2E.AutoRun";

    public static bool IsDone =>
        DevBotE2ETestRunner.Instance != null && DevBotE2ETestRunner.Instance.Done;
    public static bool Passed => IsDone && DevBotE2ETestRunner.Instance.FailCount == 0;
    public static string Report =>
        DevBotE2ETestRunner.Instance == null
            ? "<bot E2E not started>"
            : DevBotE2ETestRunner.Instance.BuildReport();

    public static void Run()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[BOT-E2E] Enter Play mode first.");
            return;
        }
        if (DevBotE2ETestRunner.Instance != null)
        {
            if (!DevBotE2ETestRunner.Instance.Done)
                return;
            Object.Destroy(DevBotE2ETestRunner.Instance.gameObject);
        }

        GameObject runner = new("DevBotE2ETestRunner");
        Object.DontDestroyOnLoad(runner);
        runner.AddComponent<DevBotE2ETestRunner>();
    }

    public static void RunHudPickingOnly()
    {
        Run();
        if (DevBotE2ETestRunner.Instance != null)
            DevBotE2ETestRunner.Instance.HudPickingOnly = true;
    }

    [UnityEditor.MenuItem("Battle Plan/Run Focused Bot Gameplay Test")]
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
}

public sealed class DevBotE2ETestRunner : MonoBehaviour
{
    public static DevBotE2ETestRunner Instance { get; private set; }
    public bool Done { get; private set; }
    public int PassCount { get; private set; }
    public int FailCount { get; private set; }
    public bool HudPickingOnly { get; set; }

    private readonly List<string> reportLines = new();
    private bool timedOut;

    private void Awake()
    {
        Instance = this;
    }

    private IEnumerator Start()
    {
        yield return RunSuite();
        Done = true;
        Debug.Log("[BOT-E2E] " + BuildReport());
    }

    public string BuildReport()
    {
        StringBuilder report = new();
        report.AppendLine("=== Focused authoritative bot gameplay test ===");
        foreach (string line in reportLines)
            report.AppendLine(line);
        report.AppendLine(
            Done
                ? $"RESULT: {(FailCount == 0 ? "PASS" : "FAIL")} ({PassCount} passed, {FailCount} failed)"
                : $"RESULT: running ({PassCount} passed, {FailCount} failed)"
        );
        return report.ToString();
    }

    private void Check(bool condition, string message)
    {
        if (condition)
        {
            PassCount++;
            reportLines.Add("[PASS] " + message);
            Debug.Log("[BOT-E2E][PASS] " + message);
        }
        else
        {
            FailCount++;
            reportLines.Add("[FAIL] " + message);
            Debug.LogError("[BOT-E2E][FAIL] " + message);
        }
    }

    private IEnumerator WaitFor(
        System.Func<bool> predicate,
        float timeoutSeconds,
        string description
    )
    {
        timedOut = false;
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (!predicate())
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                timedOut = true;
                Debug.LogWarning("[BOT-E2E] Timed out waiting for " + description);
                yield break;
            }
            yield return null;
        }
    }

    private static Vector2Int Cell(GameObject unit) =>
        GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));

    // Screenshots land outside Assets so Play mode never triggers an import/refresh cycle.
    private static void Snap(string name)
    {
        try
        {
            string dir = System.IO.Path.Combine(
                Application.temporaryCachePath,
                "BattlePlanBotE2E"
            );
            System.IO.Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[BOT-E2E] screenshot '" + name + "' failed: " + e.Message);
        }
    }

    private static Vector2 PanelToInputScreen(IPanel panel, Vector2 panelPosition)
    {
        Vector2 panelMin = RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero);
        Vector2 panelMax = RuntimePanelUtils.ScreenToPanel(
            panel,
            new Vector2(Screen.width, Screen.height)
        );
        float topLeftScreenX =
            Mathf.InverseLerp(panelMin.x, panelMax.x, panelPosition.x) * Screen.width;
        float topLeftScreenY =
            Mathf.InverseLerp(panelMin.y, panelMax.y, panelPosition.y) * Screen.height;
        return new Vector2(topLeftScreenX, Screen.height - topLeftScreenY);
    }

    private IEnumerator VerifyHudPointerPicking(GameObject[] humanUnits)
    {
        yield return WaitFor(
            () =>
            {
                UIDocument document = GameHUDController.Instance?.GetComponent<UIDocument>();
                VisualElement root = document?.rootVisualElement;
                VisualElement card = root?.Q<VisualElement>("unit-card-1");
                Button selectButton = root?.Q<Button>("unit-card-select-1");
                return root?.panel != null
                    && card != null
                    && card.worldBound.width > 1f
                    && card.worldBound.height > 1f
                    && selectButton?.enabledInHierarchy == true
                    && PlanMovement.Instance != null;
            },
            10f,
            "live Game HUD layout"
        );
        Check(!timedOut, "live Game HUD panel laid out with interactive unit cards");
        if (timedOut)
            yield break;

        UIDocument document = GameHUDController.Instance.GetComponent<UIDocument>();
        VisualElement root = document.rootVisualElement;
        VisualElement screen = root.Q<VisualElement>("screen");
        VisualElement card = root.Q<VisualElement>("unit-card-1");
        Button selectButton = root.Q<Button>("unit-card-select-1");
        IPanel panel = root.panel;

        Vector2 cardPanelCenter = card.worldBound.center;
        Vector2 cardInputPosition = PanelToInputScreen(panel, cardPanelCenter);
        Check(
            GameHUDController.IsPointerOverUI(cardInputPosition),
            $"Input-style unit-card center picks HUD UI (panel={cardPanelCenter}, input={cardInputPosition})"
        );

        Vector2 boardPanelPosition = new(
            root.worldBound.xMin + root.worldBound.width * 0.15f,
            root.worldBound.yMin + root.worldBound.height * 0.2f
        );
        VisualElement boardPick = panel.Pick(boardPanelPosition);
        bool boardPointIsNonUi = boardPick == null || boardPick == root || boardPick == screen;
        Check(
            boardPointIsNonUi,
            $"top-board regression point is non-UI (picked={boardPick?.name ?? "<none>"})"
        );
        Vector2 boardInputPosition = PanelToInputScreen(panel, boardPanelPosition);
        Check(
            boardPointIsNonUi && !GameHUDController.IsPointerOverUI(boardInputPosition),
            $"Input-style top-board point is not swallowed (panel={boardPanelPosition}, input={boardInputPosition})"
        );

        // Full bot E2E uses server-driven dev planning, which intentionally ignores client card
        // selections. The dedicated HUD-only run disables dev mode and covers live activation.
        if (GameLoop.devMode)
            yield break;

        PlanMovement planner = PlanMovement.Instance;
        GameObject cardUnit = humanUnits[1];
        bool hadPlanBefore = planner.plans.TryGetValue(
            cardUnit,
            out (bool, List<Vector3>) beforePlan
        );
        List<Vector3> pathBefore =
            hadPlanBefore && beforePlan.Item2 != null ? new List<Vector3>(beforePlan.Item2) : null;
        selectButton.Focus();
        using (NavigationSubmitEvent submitEvent = new() { target = selectButton })
            selectButton.SendEvent(submitEvent);
        yield return null;

        bool hasPlanAfter = planner.plans.TryGetValue(
            cardUnit,
            out (bool, List<Vector3>) afterPlan
        );
        List<Vector3> pathAfter = hasPlanAfter ? afterPlan.Item2 : null;
        Vector3 cardUnitCell = GridSystem.GetNearestGridCell(cardUnit);
        bool pathWasNotPainted =
            pathBefore != null
                ? pathAfter != null && pathAfter.SequenceEqual(pathBefore)
                : pathAfter != null && pathAfter.Count == 1 && pathAfter[0] == cardUnitCell;
        Check(planner.selectedUnit == cardUnit, "live unit-card interaction selected slot 1");
        Check(
            pathWasNotPainted,
            $"unit-card interaction only initialized or preserved its path ({pathBefore?.Count ?? 0}->{pathAfter?.Count ?? 0})"
        );
    }

    private IEnumerator ResolveRound()
    {
        DevInput.SubmitPlans();
        yield return WaitFor(() => GameLoop.currentPhase != "planning", 20f, "planning submission");
        if (timedOut)
            yield break;

        float deadline = Time.realtimeSinceStartup + 60f;
        bool submittedDodge = false;
        while (
            GameLoop.currentPhase != "planning"
            && GameLoop.currentPhase != "idle"
            && Time.realtimeSinceStartup < deadline
        )
        {
            if (GameLoop.currentPhase == "dodging" && !submittedDodge)
            {
                Snap("02-dodge");
                DevInput.SubmitDodge();
                submittedDodge = true;
            }
            yield return null;
        }
        timedOut = Time.realtimeSinceStartup >= deadline;
    }

    private IEnumerator RunSuite()
    {
        if (NetworkManager.Singleton == null)
        {
            SceneManager.LoadScene("JoinGame", LoadSceneMode.Single);
            yield return WaitFor(
                () => NetworkManager.Singleton != null,
                30f,
                "network bootstrap scene"
            );
            if (timedOut)
            {
                Check(false, "network bootstrap scene loaded");
                yield break;
            }
        }

        DevInput.StartBotMatch(20f, true);
        if (HudPickingOnly)
            DevInput.SetDevMode(false);
        yield return WaitFor(
            () =>
                NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsServer
                && NetworkManager.Singleton.ConnectedClients.Count == 1
                && GameLoop.Instance != null
                && GameLoop.currentPhase == "planning"
                && GameLoop.GetTeamUnits(0).Length == 3
                && GameLoop.GetTeamUnits(1).Length == 3,
            90f,
            "solo bot match bootstrap"
        );
        Check(!timedOut, "one-client AI match reached planning");
        if (timedOut)
            yield break;

        GameLoop loop = GameLoop.Instance;
        GameObject[] humanUnits = GameLoop.GetTeamUnits(GameLoop.HostTeamIndex);
        GameObject[] botUnits = GameLoop.GetTeamUnits(GameLoop.OpponentTeamIndex);
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        Check(
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval,
            "AI host requires connection approval"
        );
        Check(
            transport != null
                && transport.Protocol == UnityTransport.ProtocolType.UnityTransport
                && transport.ConnectionData.Address == DevMppmAutoJoin.LoopbackAddress
                && transport.ConnectionData.ServerListenAddress == DevMppmAutoJoin.LoopbackAddress,
            "AI host listens only on the loopback direct endpoint"
        );
        Check(loop.Options.IsBotMatch, "authoritative options identify an AI match");
        Check(loop.FogOfWarEnabled, "selected fog option seeded the match");
        Check(loop.LocalTeamIndex == GameLoop.HostTeamIndex, "host maps to logical team zero");
        Check(loop.IsBotTeam(GameLoop.OpponentTeamIndex), "opponent maps to the bot participant");
        Check(
            !loop.TryGetHumanClientId(GameLoop.OpponentTeamIndex, out _)
                && loop.GetTeamIndexForClient(GameLoop.BotParticipantId) == -1,
            "bot sentinel is neither a human client nor a controllable client team"
        );
        Check(
            humanUnits.All(unit => unit.GetComponent<Unit>().TeamIndex == 0)
                && botUnits.All(unit => unit.GetComponent<Unit>().TeamIndex == 1),
            "replicated unit identities use distinct logical teams"
        );
        Check(
            botUnits.All(unit =>
                unit.GetComponent<NetworkObject>().OwnerClientId == NetworkManager.ServerClientId
            ),
            "bot units are server-owned without assigning the sentinel to NGO"
        );
        Check(
            loop.BotPlanningContributionCount == 1,
            "bot contributed exactly once to the first planning phase"
        );

        yield return VerifyHudPointerPicking(humanUnits);
        if (HudPickingOnly)
            yield break;

        // Typed HUD copy: the readout reflects the authoritative AI/Elimination match, and the
        // slot-1 ability card advertises a match-long charge (never a per-round refresh).
        UIDocument hudDocument = GameHUDController.Instance != null
            ? GameHUDController.Instance.GetComponent<UIDocument>()
            : null;
        VisualElement hudRoot = hudDocument != null ? hudDocument.rootVisualElement : null;
        Check(hudRoot != null, "live Game HUD root is available for typed copy checks");
        if (hudRoot != null)
        {
            Label matchTypeLabel = hudRoot.Q<Label>("match-type-label");
            Check(
                matchTypeLabel != null && matchTypeLabel.text == "ELIMINATION / AI",
                $"HUD match readout reflects the typed AI elimination match (got '{matchTypeLabel?.text ?? "<none>"}')"
            );
            Label fogLabel = hudRoot.Q<Label>("fog-label");
            Check(
                fogLabel != null && fogLabel.text == "FOG ON",
                $"HUD fog readout reflects the seeded fog option (got '{fogLabel?.text ?? "<none>"}')"
            );

            VisualElement soldierCard = hudRoot.Q<VisualElement>("unit-card-1");
            Label soldierAbility = soldierCard?.Q<Label>("unit-card-ability");
            string abilityCopy = soldierAbility?.text ?? string.Empty;
            string abilityCopyLower = abilityCopy.ToLowerInvariant();
            Check(
                soldierAbility != null
                    && !abilityCopyLower.Contains("per round")
                    && !abilityCopyLower.Contains("per turn")
                    && (
                        abilityCopyLower.Contains("match")
                        || abilityCopyLower.Contains("use")
                        || abilityCopyLower.Contains("move only")
                    ),
                $"slot-1 ability card shows honest match-long copy (got '{abilityCopy}')"
            );
        }
        Snap("01-planning-hud");

        // The focused roster puts Soldier (Grenade) in slot 1. Threaten a bot in the first round
        // so the dodge contribution is observed before either side can eliminate the caster.
        GameObject soldier = humanUnits[1];
        GameObject nearestBot = botUnits
            .Where(unit => unit != null && unit.activeSelf)
            .OrderBy(unit =>
                Mathf.Abs(Cell(unit).x - Cell(soldier).x)
                + Mathf.Abs(Cell(unit).y - Cell(soldier).y)
            )
            .FirstOrDefault();
        bool submittedThreat = false;
        int dodgeCountBefore = loop.BotDodgeContributionCount;
        if (soldier != null && soldier.activeSelf && nearestBot != null)
        {
            int distance =
                Mathf.Abs(Cell(nearestBot).x - Cell(soldier).x)
                + Mathf.Abs(Cell(nearestBot).y - Cell(soldier).y);
            if (distance <= soldier.GetComponent<Movement>().unitData.abilitySquareRange)
            {
                Vector2Int target = Cell(nearestBot);
                DevInput.SetAbility(0, 1, target.x, target.y);
                submittedThreat = true;
            }
        }

        Vector2Int[] initialBotCells = botUnits.Select(Cell).ToArray();
        yield return ResolveRound();
        Check(!timedOut, "first autonomous bot round resolved");
        Check(
            loop.BotPlanningContributionCount == 2,
            "bot contributed exactly once to the next planning phase"
        );
        Check(
            botUnits
                .Select(
                    (unit, index) =>
                        unit != null && unit.activeSelf && Cell(unit) != initialBotCells[index]
                )
                .Any(moved => moved)
                || loop.BotAbilityContributionCount > 0,
            "bot moved toward knowledge or used one legal ability"
        );

        DevInput.SetFog(false);
        yield return null;
        Check(!loop.FogOfWarEnabled, "runtime fog-off transition replicated on host");
        DevInput.SetFog(true);
        yield return null;
        Check(loop.FogOfWarEnabled, "runtime fog-on transition restored");

        Check(submittedThreat, "focused test submitted a legal human threat to the bot");
        Check(
            loop.BotDodgeContributionCount > dodgeCountBefore,
            "bot answered the dodge phase exactly through its server contribution"
        );
        Check(
            loop.BotAbilityContributionCount > 0,
            "bot opportunistically used an ability from visible information"
        );
        Check(
            loop.BotAbilityContributionCount <= loop.BotPlanningContributionCount,
            "bot never contributed more than one ability per planning phase"
        );

        Snap("03-result-final");
    }
}
#endif
