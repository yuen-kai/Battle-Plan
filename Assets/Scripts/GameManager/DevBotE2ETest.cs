#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
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

    private static int FindUnitIndex(GameObject[] units, string unitName)
    {
        return System.Array.FindIndex(
            units,
            unit =>
                unit != null
                && unit.name.StartsWith(unitName, System.StringComparison.Ordinal)
        );
    }

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
            // The frame-rate policy is allowed to skip drawing frames nobody is looking at, and
            // an unfocused editor is exactly when these run — the shot would be of whatever was
            // last left in the framebuffer. Held for the rest of the run rather than released after
            // the capture: the write is asynchronous, and a run taking shots wants every frame
            // drawn throughout anyway. The hold is dropped with the rest of the runtime state.
            FrameRatePolicy.HoldFullRate = true;
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
        GameObject initiallySelected = PlanMovement.Instance?.selectedUnit;
        int cardIndex = -1;
        for (int index = 0; index < humanUnits.Length; index++)
        {
            GameObject candidate = humanUnits[index];
            if (
                candidate != null
                && candidate != initiallySelected
                && candidate.GetComponent<Unit>()?.CanUseAbility == true
            )
            {
                cardIndex = index;
                break;
            }
        }
        if (cardIndex < 0)
            cardIndex = FindUnitIndex(humanUnits, "Soldier");
        if (cardIndex < 0)
            cardIndex = 0;
        yield return WaitFor(
            () =>
            {
                UIDocument document = GameHUDController.Instance?.GetComponent<UIDocument>();
                VisualElement root = document?.rootVisualElement;
                VisualElement card = root?.Q<VisualElement>($"unit-card-{cardIndex}");
                Button selectButton = root?.Q<Button>($"unit-card-select-{cardIndex}");
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
        VisualElement card = root.Q<VisualElement>($"unit-card-{cardIndex}");
        Button selectButton = root.Q<Button>($"unit-card-select-{cardIndex}");
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
        GameObject cardUnit = humanUnits[cardIndex];
        bool hadPlanBefore = planner.plans.TryGetValue(
            cardUnit,
            out (bool, List<Vector3>) beforePlan
        );
        List<Vector3> pathBefore =
            hadPlanBefore && beforePlan.Item2 != null ? new List<Vector3>(beforePlan.Item2) : null;
        bool abilityModeBefore = hadPlanBefore && beforePlan.Item1;
        selectButton.Focus();
        using (NavigationSubmitEvent submitEvent = new() { target = selectButton })
            selectButton.SendEvent(submitEvent);
        yield return null;

        bool hasPlanAfter = planner.plans.TryGetValue(
            cardUnit,
            out (bool, List<Vector3>) afterPlan
        );
        List<Vector3> pathAfter = hasPlanAfter ? afterPlan.Item2 : null;
        // A card press names an order; it never draws one. Arming an ability collapses the route
        // to the unit's own cell, so the invariant is that the path cannot get longer.
        bool pathWasNotPainted =
            pathAfter != null && pathAfter.Count <= Mathf.Max(1, pathBefore?.Count ?? 1);
        Unit cardIdentity = cardUnit.GetComponent<Unit>();
        bool abilityWasOrderable =
            !abilityModeBefore
            && cardUnit.GetComponent<Ability>() != null
            && cardIdentity != null
            && cardIdentity.CanUseAbility;
        Check(
            planner.selectedUnit == cardUnit,
            $"live unit-card interaction selected roster slot {cardIndex}"
        );
        Check(
            pathWasNotPainted,
            $"unit-card interaction never paints a route ({pathBefore?.Count ?? 0}->{pathAfter?.Count ?? 0})"
        );
        Check(
            hasPlanAfter && afterPlan.Item1 == abilityWasOrderable,
            abilityWasOrderable
                ? "one press on an ability card ordered that unit's ability"
                : "a card whose ability cannot be ordered left the unit on movement"
        );

        bool canToggleAbility = cardUnit.GetComponent<Unit>()?.CanUseAbility == true;
        Check(canToggleAbility, "HUD toggle regression target has a ready ability");
        if (canToggleAbility)
        {
            using (NavigationSubmitEvent submitEvent = new() { target = selectButton })
                selectButton.SendEvent(submitEvent);
            yield return null;

            bool toggled = planner.plans.TryGetValue(
                cardUnit,
                out (bool, List<Vector3>) toggledPlan
            );
            Check(
                toggled && toggledPlan.Item1 != abilityModeBefore,
                "reselecting the selected card toggles its planner-owned order mode"
            );

            using (NavigationSubmitEvent submitEvent = new() { target = selectButton })
                selectButton.SendEvent(submitEvent);
            yield return null;

            bool restored = planner.plans.TryGetValue(
                cardUnit,
                out (bool, List<Vector3>) restoredPlan
            );
            Check(
                restored && restoredPlan.Item1 == abilityModeBefore,
                "reselecting again returns to the original order mode"
            );
            pathAfter = restored ? restoredPlan.Item2 : null;
        }

        Button lockInButton = root.Q<Button>("lock-in-button");
        PathsDict draftContainer = planner.plans;
        List<Vector3> draftPath =
            pathAfter != null ? new List<Vector3>(pathAfter) : new List<Vector3>();
        Check(lockInButton != null, "reversible Lock In button is present");
        Check(planner.TryLockIn(), "normal planning accepts Lock In");
        yield return WaitFor(
            () =>
                planner.CanUnlockPlan
                && lockInButton != null
                && lockInButton.enabledInHierarchy
                && lockInButton.text == "Unlock",
            3f,
            "server acknowledgement for reversible Lock In"
        );
        Check(!timedOut, "locked orders expose an enabled Unlock action");
        if (timedOut)
            yield break;

        Check(planner.TryUnlock(), "locked orders request server-authoritative unlock");
        yield return WaitFor(
            () =>
                planner.CanEditPlan
                && lockInButton != null
                && lockInButton.enabledInHierarchy
                && lockInButton.text == "Lock in",
            3f,
            "server acknowledgement for planning unlock"
        );
        Check(!timedOut, "unlock restores editing before the shared deadline");
        if (timedOut)
            yield break;

        bool draftPreserved =
            ReferenceEquals(planner.plans, draftContainer)
            && planner.plans.TryGetValue(cardUnit, out (bool, List<Vector3>) unlockedPlan)
            && unlockedPlan.Item2 != null
            && unlockedPlan.Item2.SequenceEqual(draftPath);
        Check(draftPreserved, "unlock preserves the exact local draft and path");

        Check(planner.TryLockIn(), "unlocked orders can be locked again");
        yield return WaitFor(
            () => planner.CanUnlockPlan && lockInButton != null && lockInButton.text == "Unlock",
            3f,
            "server acknowledgement for relocked orders"
        );
        Check(!timedOut, "relocked orders advance to the reversible waiting state");
    }

    private void VerifyEnemyStatusCards(
        VisualElement hudRoot,
        IReadOnlyList<GameObject> enemyUnits,
        string checkpoint
    )
    {
        if (hudRoot == null || enemyUnits == null)
        {
            Check(false, $"{checkpoint} enemy status cards are available");
            return;
        }

        for (int index = 0; index < enemyUnits.Count; index++)
        {
            GameObject enemy = enemyUnits[index];
            Unit identity = enemy != null ? enemy.GetComponent<Unit>() : null;
            Health health = enemy != null ? enemy.GetComponent<Health>() : null;
            UnitData data = enemy != null ? enemy.GetComponent<Movement>()?.unitData : null;
            VisualElement card = hudRoot.Q<VisualElement>($"enemy-unit-card-{index}");
            Label healthLabel = card?.Q<Label>("unit-card-health-value");
            Label abilityLabel = card?.Q<Label>("unit-card-ability");
            Label stateLabel = card?.Q<Label>("unit-card-state");
            Button selectButton = card?.Q<Button>($"enemy-unit-card-select-{index}");

            bool alive = health != null && health.IsAlive;
            string expectedHealth =
                health != null
                    ? $"{Mathf.RoundToInt(health.CurrentHealth)} / {Mathf.RoundToInt(health.MaxHealth)} HP"
                    : string.Empty;
            bool hasAbility = enemy != null && enemy.GetComponent<Ability>() != null;
            int cooldown = identity != null ? identity.AbilityCooldownRoundsRemaining : 0;
            string cooldownText = cooldown == 1 ? "1 round" : $"{cooldown} rounds";
            string expectedAbility =
                !hasAbility
                    ? "Move only"
                    : cooldown == 0
                        ? $"{data?.abilityName ?? "Ability"} · ready"
                        : $"{data?.abilityName ?? "Ability"} · ready in {cooldownText}";

            Check(
                card != null && selectButton != null && !selectButton.enabledInHierarchy,
                $"{checkpoint} enemy slot {index} is a read-only card"
            );
            Check(
                healthLabel != null && healthLabel.text == expectedHealth,
                $"{checkpoint} enemy slot {index} health is live (got '{healthLabel?.text ?? "<none>"}')"
            );
            Check(
                abilityLabel != null && abilityLabel.text == expectedAbility,
                $"{checkpoint} enemy slot {index} ability cooldown is live (got '{abilityLabel?.text ?? "<none>"}')"
            );
            Check(
                stateLabel != null && stateLabel.text == (alive ? "ACTIVE" : "ELIMINATED"),
                $"{checkpoint} enemy slot {index} alive state is live (got '{stateLabel?.text ?? "<none>"}')"
            );
        }
    }

    private IEnumerator ResolveRound()
    {
        DevInput.SubmitPlans();
        yield return WaitFor(() => GameLoop.currentPhase != "planning", 20f, "planning submission");
        if (timedOut)
            yield break;

        float deadline = Time.realtimeSinceStartup + 60f;
        bool submittedDodge = false;
        bool checkedExecutionTelegraphs = false;
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
            if (GameLoop.currentPhase == "executing" && !checkedExecutionTelegraphs)
            {
                bool hasActiveTelegraph = Object
                    .FindObjectsByType<Transform>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None
                    )
                    .Any(transform =>
                        transform.name == "AbilityTelegraphLine"
                        || transform.name == "AbilityTelegraphMarker"
                        || transform.name.StartsWith("SmokeTelegraphCell_")
                        || transform.name.StartsWith("AbilityTelegraphCell_")
                    );
                Check(
                    !hasActiveTelegraph,
                    "ability dodge telegraphs are inactive before execution begins"
                );
                checkedExecutionTelegraphs = true;
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
                && GameLoop.GetTeamUnits(0).Length == RosterRules.UnitsPerPlayer
                && GameLoop.GetTeamUnits(1).Length == RosterRules.UnitsPerPlayer,
            90f,
            "solo bot match bootstrap"
        );
        Check(!timedOut, "one-client AI match reached planning");
        if (timedOut)
            yield break;

        GameLoop loop = GameLoop.Instance;
        GameObject[] humanUnits = GameLoop.GetTeamUnits(GameLoop.HostTeamIndex);
        GameObject[] botUnits = GameLoop.GetTeamUnits(GameLoop.OpponentTeamIndex);
        int soldierIndex = FindUnitIndex(humanUnits, "Soldier");

        Check(
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval,
            "AI host requires connection approval"
        );
        // Stronger than the loopback endpoint this used to assert: a solo host now opens
        // no endpoint at all, which is what lets the same path run in a browser.
        Check(
            NetworkManager.Singleton.NetworkConfig.NetworkTransport is OfflineTransport,
            "AI host runs socketless and opens no endpoint"
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
            humanUnits
                    .Select(unit => unit.GetComponent<Unit>().RosterSlot)
                    .SequenceEqual(Enumerable.Range(0, RosterRules.UnitsPerPlayer))
                && botUnits
                    .Select(unit => unit.GetComponent<Unit>().RosterSlot)
                    .SequenceEqual(Enumerable.Range(0, RosterRules.UnitsPerPlayer)),
            "replicated roster slots are complete, unique, and ordered"
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
        Check(soldierIndex >= 0, "focused human roster contains Soldier");
        if (soldierIndex < 0)
            yield break;

        yield return VerifyHudPointerPicking(humanUnits);
        if (HudPickingOnly)
            yield break;

        // The HUD omits setup options once play begins, while the Soldier's ability card
        // advertises its current cooldown state.
        UIDocument hudDocument = GameHUDController.Instance != null
            ? GameHUDController.Instance.GetComponent<UIDocument>()
            : null;
        VisualElement hudRoot = hudDocument != null ? hudDocument.rootVisualElement : null;
        Check(hudRoot != null, "live Game HUD root is available for typed copy checks");
        if (hudRoot != null)
        {
            Check(
                hudRoot.Q<Label>("match-type-label") == null
                    && hudRoot.Q<Label>("fog-label") == null,
                "HUD omits static match settings during play"
            );

            VisualElement soldierCard = hudRoot.Q<VisualElement>($"unit-card-{soldierIndex}");
            Label soldierAbility = soldierCard?.Q<Label>("unit-card-ability");
            string abilityCopy = soldierAbility?.text ?? string.Empty;
            string abilityCopyLower = abilityCopy.ToLowerInvariant();
            Check(
                soldierAbility != null
                    && (
                        abilityCopyLower.Contains("ready")
                        || abilityCopyLower.Contains("round")
                        || abilityCopyLower.Contains("move only")
                    ),
                $"Soldier ability card shows authoritative cooldown copy (got '{abilityCopy}')"
            );
            VerifyEnemyStatusCards(hudRoot, botUnits, "initial fog-on");
        }
        Snap("01-planning-hud");

        // Threaten a bot with Soldier in the first round so the dodge contribution is observed
        // before either side can eliminate the caster.
        GameObject soldier = humanUnits[soldierIndex];
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
                DevInput.SetAbility(0, soldierIndex, target.x, target.y);
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
        VerifyEnemyStatusCards(hudRoot, botUnits, "post-round fog-on");

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

        GameObject abilityStatusProbe = botUnits.FirstOrDefault(unit =>
            unit != null
            && unit.activeInHierarchy
            && unit.GetComponent<Unit>()?.CanUseAbility == true
        );
        Check(abilityStatusProbe != null, "enemy status probe found a ready ability");
        if (abilityStatusProbe != null)
        {
            Unit probeIdentity = abilityStatusProbe.GetComponent<Unit>();
            bool startedCooldown = probeIdentity.TryStartAbilityCooldown();
            loop.NotifyEnemyUnitStatusChanged(abilityStatusProbe);
            yield return null;

            Label probeAbility = hudRoot
                ?.Q<VisualElement>($"enemy-unit-card-{probeIdentity.RosterSlot}")
                ?.Q<Label>("unit-card-ability");
            string expectedSuffix =
                probeIdentity.AbilityCooldownRoundsRemaining == 1
                    ? "ready in 1 round"
                    : $"ready in {probeIdentity.AbilityCooldownRoundsRemaining} rounds";
            Check(startedCooldown, "enemy status probe started an authoritative cooldown");
            Check(
                probeAbility != null
                    && probeAbility.text.EndsWith(expectedSuffix, System.StringComparison.Ordinal),
                $"enemy status card reflected the cooldown (got '{probeAbility?.text ?? "<none>"}')"
            );
        }

        Snap("03-result-final");
    }
}
#endif
