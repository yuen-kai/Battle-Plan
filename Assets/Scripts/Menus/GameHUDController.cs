using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class GameHUDController : MonoBehaviour
{
    public static GameHUDController Instance { get; private set; }
    private static readonly string[] PlanningCommitStateClasses =
    {
        "planning-commit--ready",
        "planning-commit--sending",
        "planning-commit--waiting",
        "planning-commit--unlocking",
        "planning-commit--locked",
    };
    private static readonly string[] FlashPerspectiveClasses =
    {
        "hud-flash--friendly",
        "hud-flash--enemy",
        "hud-flash--neutral",
    };
    private static readonly string[] ResultsOutcomeClasses =
    {
        "results-mark--win",
        "results-mark--loss",
        "results-mark--draw",
    };

    [SerializeField]
    private VisualTreeAsset unitCardTemplate;

    private readonly UnitCardElement[] cards = new UnitCardElement[RosterRules.UnitsPerPlayer];
    private readonly UnitCardElement[] enemyCards =
        new UnitCardElement[RosterRules.UnitsPerPlayer];
    private UIDocument document;
    private VisualElement root;
    private VisualElement screen;
    private VisualElement flash;
    private VisualElement cardsContainer;
    private VisualElement enemyCardsContainer;
    private VisualElement hudDock;
    private VisualElement hillStatusReadout;
    private VisualElement deploymentOverlay;
    private VisualElement resultsOverlay;
    private VisualElement resultsPanel;
    private VisualElement resultsMark;
    private VisualElement phaseMark;
    private List<VisualElement> hillPips;
    private Label phaseLabel;
    private Label timerLabel;
    private Label hillStatusLabel;
    private Label deploymentStatus;
    private Label resultsStatus;
    private Label targetFeedbackLabel;
    private VisualElement planningCommit;
    private Label planningCommitStatus;
    private Button lockInButton;
    private Button playAgainButton;
    private Button mainMenuButton;
    private Button controlsButton;
    private VisualElement controlsOverlay;
    private VisualElement controlsPanel;
    private Button controlsCloseButton;
    private VisualElement activeOverlay;
    private Action playAgainAction;
    private Action mainMenuAction;
    private Coroutine flashCoroutine;
    private bool callbacksRegistered;

    public string HillStatusText => hillStatusLabel?.text ?? string.Empty;

    private void OnEnable()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("[GameHUDController] Multiple active HUD controllers were found.");
            enabled = false;
            return;
        }
        Instance = this;

        document = GetComponent<UIDocument>();
        root = document != null ? document.rootVisualElement : null;
        if (root == null)
        {
            Debug.LogError("[GameHUDController] UIDocument has no visual tree.");
            Instance = null;
            return;
        }

        root.pickingMode = PickingMode.Ignore;
        CacheElements();
        RegisterCallbacks();
        BuildCards();
        ConsoleUiNavigation.ConfigureButtons(root);
        HideResults();
        CloseControlsOverlay(false);
        ClearTargetFeedback();
        HidePlanningCommit();
        ShowDeployment();
        SetCardsInteractable(false);
        SetMatchSummary(MatchOptions.Current);
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }
        flash?.RemoveFromClassList("hud-flash--active");
        if (flash != null)
        {
            foreach (string perspectiveClass in FlashPerspectiveClasses)
                flash.RemoveFromClassList(perspectiveClass);
        }

        foreach (UnitCardElement card in cards)
            card?.Dispose();
        foreach (UnitCardElement card in enemyCards)
            card?.Dispose();
        Array.Clear(cards, 0, cards.Length);
        Array.Clear(enemyCards, 0, enemyCards.Length);
        playAgainAction = null;
        mainMenuAction = null;
        activeOverlay = null;

        if (Instance == this)
            Instance = null;
    }

    private void CacheElements()
    {
        screen = RequireElement<VisualElement>("screen");
        flash = RequireElement<VisualElement>("hud-flash");
        cardsContainer = RequireElement<VisualElement>("unit-cards");
        enemyCardsContainer = RequireElement<VisualElement>("enemy-unit-cards");
        hudDock = RequireElement<VisualElement>("hud-dock");
        deploymentOverlay = RequireElement<VisualElement>("deployment-overlay");
        resultsOverlay = RequireElement<VisualElement>("results-overlay");
        resultsPanel = RequireElement<VisualElement>("results-panel");
        phaseLabel = RequireElement<Label>("phase-label");
        timerLabel = RequireElement<Label>("timer-label");
        hillStatusReadout = RequireElement<VisualElement>("hill-status-readout");
        hillStatusLabel = RequireElement<Label>("hill-status-label");
        deploymentStatus = RequireElement<Label>("deployment-status");
        resultsStatus = RequireElement<Label>("results-status");
        playAgainButton = RequireElement<Button>("play-again-button");
        mainMenuButton = RequireElement<Button>("main-menu-button");
        targetFeedbackLabel = root.Q<Label>("target-feedback-label");
        planningCommit = root.Q<VisualElement>("planning-commit");
        planningCommitStatus = root.Q<Label>("planning-commit-status");
        lockInButton = root.Q<Button>("lock-in-button");
        controlsButton = root.Q<Button>("controls-button");
        controlsOverlay = root.Q<VisualElement>("controls-overlay");
        controlsPanel = controlsOverlay?.Q<VisualElement>(className: "controls-panel");
        controlsCloseButton = root.Q<Button>("controls-close-button");
        resultsMark = resultsPanel?.Q<VisualElement>(className: "results-mark");
        phaseMark = root.Q<VisualElement>(className: "phase-cluster__mark");
        hillPips = hillStatusReadout?.Query<VisualElement>(className: "hill-pip").ToList();
    }

    private T RequireElement<T>(string elementName)
        where T : VisualElement
    {
        T element = root.Q<T>(elementName);
        if (element == null)
        {
            Debug.LogError(
                $"[GameHUDController] Missing required {typeof(T).Name} '{elementName}'."
            );
        }
        return element;
    }

    private void RegisterCallbacks()
    {
        if (callbacksRegistered)
            return;
        if (playAgainButton != null)
            playAgainButton.clicked += OnPlayAgainClicked;
        if (mainMenuButton != null)
            mainMenuButton.clicked += OnMainMenuClicked;
        if (controlsButton != null)
            controlsButton.clicked += ShowControlsOverlay;
        if (controlsCloseButton != null)
            controlsCloseButton.clicked += HideControlsOverlay;
        if (lockInButton != null)
            lockInButton.clicked += OnLockInClicked;
        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        root.RegisterCallback<NavigationCancelEvent>(
            OnNavigationCancel,
            TrickleDown.TrickleDown
        );
        root.RegisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
        callbacksRegistered = true;
    }

    private void UnregisterCallbacks()
    {
        if (!callbacksRegistered)
            return;
        if (playAgainButton != null)
            playAgainButton.clicked -= OnPlayAgainClicked;
        if (mainMenuButton != null)
            mainMenuButton.clicked -= OnMainMenuClicked;
        if (controlsButton != null)
            controlsButton.clicked -= ShowControlsOverlay;
        if (controlsCloseButton != null)
            controlsCloseButton.clicked -= HideControlsOverlay;
        if (lockInButton != null)
            lockInButton.clicked -= OnLockInClicked;
        root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        root.UnregisterCallback<NavigationCancelEvent>(
            OnNavigationCancel,
            TrickleDown.TrickleDown
        );
        root.UnregisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
        callbacksRegistered = false;
    }

    private void BuildCards()
    {
        if (cardsContainer == null || enemyCardsContainer == null)
            return;

        cardsContainer.Clear();
        enemyCardsContainer.Clear();
        if (unitCardTemplate == null)
        {
            Debug.LogError(
                "[GameHUDController] Unit card template is not assigned; using fallback UI."
            );
        }
        LetPointerThrough(cardsContainer);
        LetPointerThrough(enemyCardsContainer);
        for (int i = 0; i < cards.Length; i++)
        {
            VisualElement host =
                unitCardTemplate != null ? unitCardTemplate.Instantiate() : new VisualElement();
            host.name = $"unit-card-{i}";
            host.AddToClassList("unit-card-host");
            if (i == cards.Length - 1)
                host.AddToClassList("unit-card-host--last");
            cardsContainer.Add(host);
            cards[i] = new UnitCardElement(host, i);
        }

        for (int i = 0; i < enemyCards.Length; i++)
        {
            VisualElement host =
                unitCardTemplate != null ? unitCardTemplate.Instantiate() : new VisualElement();
            host.name = $"enemy-unit-card-{i}";
            host.AddToClassList("unit-card-host");
            host.AddToClassList("enemy-unit-card-host");
            if (i == enemyCards.Length - 1)
                host.AddToClassList("enemy-unit-card-host--last");
            enemyCardsContainer.Add(host);
            enemyCards[i] = new UnitCardElement(host, i, true);
        }
    }

    /// The card rows float over live gameplay, so only the cards themselves may
    /// take a pointer; the transparent scroll chrome would otherwise swallow
    /// board clicks across the full width of the screen.
    private static void LetPointerThrough(VisualElement container)
    {
        if (container == null)
            return;

        container.pickingMode = PickingMode.Ignore;
        if (container is ScrollView scrollView)
        {
            scrollView.contentViewport.pickingMode = PickingMode.Ignore;
            scrollView.contentContainer.pickingMode = PickingMode.Ignore;
        }
        else if (container.contentContainer != container)
        {
            container.contentContainer.pickingMode = PickingMode.Ignore;
        }
    }

    public void ConfigureCard(
        int cardIndex,
        UnitData data,
        bool hasAbility,
        int cooldownRoundsRemaining
    )
    {
        if (!TryGetCard(cardIndex, out UnitCardElement card))
            return;

        card.Configure(
            data,
            hasAbility,
            cooldownRoundsRemaining,
            () => PlanMovement.Instance?.TryActivateUnitCard(cardIndex)
        );
    }

    public void SetEnemyCardStatus(
        int cardIndex,
        UnitData data,
        bool hasAbility,
        int cooldownRoundsRemaining,
        float currentHealth,
        float maxHealth,
        bool alive
    )
    {
        if (!TryGetEnemyCard(cardIndex, out UnitCardElement card))
            return;

        card.ConfigureEnemy(
            data,
            hasAbility,
            cooldownRoundsRemaining,
            currentHealth,
            maxHealth,
            alive
        );
    }

    public void SetCardsInteractable(bool interactable)
    {
        foreach (UnitCardElement card in cards)
            card?.SetInteractable(interactable);

        ClearTargetFeedback();
    }

    public void ShowPlanningCommitReady()
    {
        SetPlanningCommitState("READY", "Lock in", true, "Lock current orders");
    }

    public void ShowPlanningCommitSending()
    {
        SetPlanningCommitState("SENDING", "Locking…", false, "Sending orders");
    }

    public void ShowPlanningCommitWaiting()
    {
        SetPlanningCommitState("WAITING", "Unlock", true, "Unlock to edit orders");
    }

    public void ShowPlanningCommitUnlocking()
    {
        SetPlanningCommitState("UNLOCKING", "Unlocking…", false, "Unlocking orders");
    }

    public void ShowPlanningCommitLocked()
    {
        SetPlanningCommitState("LOCKED", "Locked", false, "Orders are final");
    }

    public void HidePlanningCommit()
    {
        planningCommit?.AddToClassList("hidden");
        lockInButton?.SetEnabled(false);
    }

    private void SetPlanningCommitState(
        string status,
        string buttonText,
        bool buttonEnabled,
        string tooltip
    )
    {
        if (planningCommit != null)
        {
            foreach (string existingClass in PlanningCommitStateClasses)
                planningCommit.RemoveFromClassList(existingClass);

            string nextStateClass = status switch
            {
                "READY" => "planning-commit--ready",
                "SENDING" => "planning-commit--sending",
                "WAITING" => "planning-commit--waiting",
                "UNLOCKING" => "planning-commit--unlocking",
                "LOCKED" => "planning-commit--locked",
                _ => string.Empty,
            };
            if (!string.IsNullOrEmpty(nextStateClass))
                planningCommit.AddToClassList(nextStateClass);
        }
        if (planningCommitStatus != null)
            planningCommitStatus.text = status;
        if (lockInButton != null)
        {
            lockInButton.text = buttonText;
            lockInButton.tooltip = tooltip;
            lockInButton.SetEnabled(buttonEnabled);
        }
        planningCommit?.RemoveFromClassList("hidden");

        PlanningAudio.CommitStateChanged(status);
    }

    private void OnLockInClicked()
    {
        PlanMovement planner = PlanMovement.Instance;
        if (planner == null)
            return;

        if (planner.CanUnlockPlan)
            planner.TryUnlock();
        else
            planner.TryLockIn();
    }

    public void SetTargetFeedback(string message, bool isError = true)
    {
        if (targetFeedbackLabel == null)
            return;

        bool visible = !string.IsNullOrWhiteSpace(message);
        targetFeedbackLabel.text = visible ? message : string.Empty;
        targetFeedbackLabel.EnableInClassList("hidden", !visible);
        targetFeedbackLabel.EnableInClassList("target-feedback--visible", visible);
        targetFeedbackLabel.EnableInClassList("target-feedback--error", visible && isError);
        targetFeedbackLabel.EnableInClassList("target-feedback--success", visible && !isError);
        targetFeedbackLabel.EnableInClassList("label--danger", visible && isError);

        HudAudio.TargetFeedback(message, isError);
    }

    public void ClearTargetFeedback()
    {
        SetTargetFeedback(string.Empty, false);
    }

    public void SetCardDisabled(int cardIndex, bool disabled)
    {
        if (TryGetCard(cardIndex, out UnitCardElement card))
            card.SetDisabled(disabled);
    }

    public void SetCardAbilityCooldown(int cardIndex, int remainingRounds)
    {
        if (TryGetCard(cardIndex, out UnitCardElement card))
            card.SetAbilityCooldown(remainingRounds);
    }

    public void SetCardPlanningState(int cardIndex, bool selected, bool abilityMode)
    {
        if (TryGetCard(cardIndex, out UnitCardElement card))
            card.SetPlanningState(selected, abilityMode);
    }

    public void ClearCardPlanningStates()
    {
        foreach (UnitCardElement card in cards)
            card?.SetPlanningState(false, false);
    }

    public void SetPhase(string message, MessagePerspective perspective)
    {
        if (phaseLabel == null)
            return;

        bool friendly = perspective == MessagePerspective.Friendly;
        bool enemy = perspective == MessagePerspective.Enemy;
        bool neutral = perspective == MessagePerspective.Neutral;

        phaseLabel.text = message ?? string.Empty;
        phaseLabel.EnableInClassList("phase-label--friendly", friendly);
        phaseLabel.EnableInClassList("phase-label--enemy", enemy);
        phaseLabel.EnableInClassList("phase-label--neutral", neutral);

        // The mark carries the phase state so the phase text can stay at full
        // contrast no matter how long the string is.
        phaseMark?.EnableInClassList("phase-cluster__mark--friendly", friendly);
        phaseMark?.EnableInClassList("phase-cluster__mark--enemy", enemy);
        phaseMark?.EnableInClassList("phase-cluster__mark--neutral", neutral);

        HudAudio.PhaseChanged(message);
    }

    public void SetTimer(float seconds)
    {
        if (timerLabel == null)
            return;

        int displayedSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
        timerLabel.text = displayedSeconds > 0 ? displayedSeconds.ToString() : string.Empty;
        timerLabel.EnableInClassList(
            "timer-label--urgent",
            displayedSeconds > 0 && displayedSeconds <= 5
        );

        HudAudio.TimerUpdated(seconds);
    }

    public void Flash(MessagePerspective perspective, float duration = 0.2f)
    {
        if (flash == null)
            return;

        foreach (string perspectiveClass in FlashPerspectiveClasses)
            flash.RemoveFromClassList(perspectiveClass);
        flash.AddToClassList(
            perspective switch
            {
                MessagePerspective.Friendly => "hud-flash--friendly",
                MessagePerspective.Enemy => "hud-flash--enemy",
                _ => "hud-flash--neutral",
            }
        );

        if (flashCoroutine != null)
            StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(FlashRoutine(Mathf.Max(0.01f, duration)));
    }

    private IEnumerator FlashRoutine(float duration)
    {
        flash.AddToClassList("hud-flash--active");
        yield return new WaitForSecondsRealtime(duration);
        flash.RemoveFromClassList("hud-flash--active");
        flashCoroutine = null;
    }

    public void SetMatchSummary(MatchOptions options)
    {
        options = options.Sanitized();
        bool showHillStatus = options.IsKingOfTheHill;
        root?.EnableInClassList("koth", showHillStatus);
        hillStatusReadout?.EnableInClassList("hidden", !showHillStatus);
        if (showHillStatus && hillStatusLabel != null)
        {
            SetHillControl(
                GameLoop.Instance != null ? GameLoop.Instance.HillControl : HillControlState.Empty
            );
        }
    }

    public void SetHillControl(HillControlState state)
    {
        if (hillStatusLabel == null)
            return;

        bool hostControlled =
            state.Status == HillControlStatus.Controlled
            && state.ControllingTeamIndex == GameLoop.HostTeamIndex;
        bool opponentControlled =
            state.Status == HillControlStatus.Controlled
            && state.ControllingTeamIndex == GameLoop.OpponentTeamIndex;

        bool contested = state.Status == HillControlStatus.Contested;
        hillStatusLabel.EnableInClassList("hill-status--blue", hostControlled);
        hillStatusLabel.EnableInClassList("hill-status--red", opponentControlled);
        hillStatusLabel.EnableInClassList("hill-status--contested", contested);

        // The widget's left rule and the streak pips read the same state.
        hillStatusReadout?.EnableInClassList("hill-status--blue", hostControlled);
        hillStatusReadout?.EnableInClassList("hill-status--red", opponentControlled);
        hillStatusReadout?.EnableInClassList("hill-status--contested", contested);

        int filledPips =
            state.Status == HillControlStatus.Controlled
                ? Mathf.Clamp(state.Streak, 0, GameLoop.HillControlRoundsToWin)
                : 0;
        if (hillPips != null)
        {
            for (int i = 0; i < hillPips.Count; i++)
                hillPips[i].EnableInClassList("hill-pip--filled", i < filledPips);
        }

        hillStatusLabel.text = state.Status switch
        {
            HillControlStatus.Contested => "Contested · streak reset",
            HillControlStatus.Controlled =>
                $"{(hostControlled ? "Blue" : "Red")} control · "
                + $"{Mathf.Clamp(state.Streak, 0, GameLoop.HillControlRoundsToWin)}/"
                + GameLoop.HillControlRoundsToWin,
            _ => "No control · 0/" + GameLoop.HillControlRoundsToWin,
        };

        HudAudio.HillControlChanged(state);
    }

    public void ShowDeployment(string status = null)
    {
        if (deploymentStatus != null)
        {
            deploymentStatus.text = string.IsNullOrWhiteSpace(status)
                ? "Preparing the battlefield and both crews."
                : status;
        }
        deploymentOverlay?.RemoveFromClassList("hidden");
        deploymentOverlay?.BringToFront();
        ActivateOverlay(deploymentOverlay, deploymentOverlay);

        HudAudio.Deploying();
    }

    public void HideDeployment()
    {
        deploymentOverlay?.AddToClassList("hidden");
        ClearActiveOverlay(deploymentOverlay, null);
    }

    public void ShowResults(
        MatchResult result,
        int localTeamIndex,
        Action onPlayAgain,
        Action onMainMenu
    )
    {
        ShowResults(result.GetStatusForTeam(localTeamIndex), onPlayAgain, onMainMenu);
    }

    public void ShowResults(string status, Action onPlayAgain, Action onMainMenu)
    {
        string resolvedStatus = status ?? "Match complete.";
        if (resultsStatus != null)
            resultsStatus.text = resolvedStatus;
        SetResultsOutcome(resolvedStatus);

        playAgainAction = onPlayAgain;
        mainMenuAction = onMainMenu;
        SetResultButtonsEnabled(true, true);
        CloseControlsOverlay(false);
        HideDeployment();
        resultsOverlay?.RemoveFromClassList("hidden");
        resultsOverlay?.BringToFront();
        ActivateOverlay(resultsOverlay, playAgainButton);

        HudAudio.ResultsShown(resolvedStatus);
    }

    public void HideResults()
    {
        resultsOverlay?.AddToClassList("hidden");
        ClearActiveOverlay(resultsOverlay, null);
        playAgainAction = null;
        mainMenuAction = null;
    }

    /// The result strings are locked copy, so the mark can be tinted from them
    /// without the outcome having to travel separately. Anything unresolved
    /// keeps the default amber.
    private void SetResultsOutcome(string status)
    {
        if (resultsMark == null)
            return;

        foreach (string outcomeClass in ResultsOutcomeClasses)
            resultsMark.RemoveFromClassList(outcomeClass);

        if (string.IsNullOrEmpty(status))
            return;
        if (status.StartsWith("You win", StringComparison.Ordinal))
            resultsMark.AddToClassList("results-mark--win");
        else if (status.StartsWith("You lose", StringComparison.Ordinal))
            resultsMark.AddToClassList("results-mark--loss");
        else if (status.StartsWith("Draw", StringComparison.Ordinal))
            resultsMark.AddToClassList("results-mark--draw");
    }

    public void SetResultButtonsEnabled(bool playAgainEnabled, bool mainMenuEnabled)
    {
        if (playAgainEnabled)
            playAgainButton?.SetEnabled(true);
        if (mainMenuEnabled)
            mainMenuButton?.SetEnabled(true);

        VisualElement focused = root?.focusController?.focusedElement as VisualElement;
        if (
            !playAgainEnabled
            && focused == playAgainButton
        )
        {
            (mainMenuEnabled ? mainMenuButton : resultsPanel)?.Focus();
        }
        else if (!mainMenuEnabled && focused == mainMenuButton)
        {
            (playAgainEnabled ? playAgainButton : resultsPanel)?.Focus();
        }

        playAgainButton?.SetEnabled(playAgainEnabled);
        mainMenuButton?.SetEnabled(mainMenuEnabled);
    }

    private void OnPlayAgainClicked()
    {
        Action action = playAgainAction;
        SetResultButtonsEnabled(false, true);
        action?.Invoke();
    }

    private void OnMainMenuClicked()
    {
        Action action = mainMenuAction;
        SetResultButtonsEnabled(false, false);
        action?.Invoke();
    }

    private void ShowControlsOverlay()
    {
        if (
            controlsOverlay == null
            || (resultsOverlay != null && !resultsOverlay.ClassListContains("hidden"))
        )
            return;
        controlsOverlay.RemoveFromClassList("hidden");
        controlsOverlay.BringToFront();
        ActivateOverlay(controlsOverlay, controlsCloseButton);
    }

    private void HideControlsOverlay()
    {
        CloseControlsOverlay(true);
    }

    private void CloseControlsOverlay(bool restoreFocus)
    {
        controlsOverlay?.AddToClassList("hidden");
        ClearActiveOverlay(controlsOverlay, restoreFocus ? controlsButton : null);
    }

    private void ActivateOverlay(VisualElement overlay, VisualElement focusTarget)
    {
        if (overlay == null)
            return;

        activeOverlay = overlay;
        hudDock?.SetEnabled(false);
        root?.schedule.Execute(() =>
        {
            if (
                !isActiveAndEnabled
                || activeOverlay != overlay
                || overlay.panel == null
                || overlay.ClassListContains("hidden")
            )
            {
                return;
            }

            ResolveOverlayFocusTarget(overlay, focusTarget)?.Focus();
        });
    }

    private void ClearActiveOverlay(VisualElement overlay, VisualElement restoreFocusTarget)
    {
        if (activeOverlay != overlay)
            return;

        activeOverlay = null;
        hudDock?.SetEnabled(true);
        if (restoreFocusTarget == null)
            return;

        root?.schedule.Execute(() =>
        {
            if (
                !isActiveAndEnabled
                || activeOverlay != null
                || restoreFocusTarget.panel == null
                || !restoreFocusTarget.enabledInHierarchy
                || !restoreFocusTarget.canGrabFocus
            )
            {
                return;
            }

            restoreFocusTarget.Focus();
        });
    }

    private VisualElement ResolveOverlayFocusTarget(
        VisualElement overlay,
        VisualElement preferredTarget
    )
    {
        if (CanGrabFocus(preferredTarget))
            return preferredTarget;

        if (overlay == resultsOverlay)
        {
            if (CanGrabFocus(playAgainButton))
                return playAgainButton;
            if (CanGrabFocus(mainMenuButton))
                return mainMenuButton;
            if (CanGrabFocus(resultsPanel))
                return resultsPanel;
        }
        else if (overlay == controlsOverlay)
        {
            if (CanGrabFocus(controlsCloseButton))
                return controlsCloseButton;
            if (CanGrabFocus(controlsPanel))
                return controlsPanel;
        }

        return CanGrabFocus(overlay) ? overlay : null;
    }

    private static bool CanGrabFocus(VisualElement element)
    {
        return element != null
            && element.panel != null
            && element.enabledInHierarchy
            && element.canGrabFocus;
    }

    private void OnFocusIn(FocusInEvent evt)
    {
        VisualElement focused = evt.target as VisualElement;
        if (
            activeOverlay == null
            || activeOverlay.ClassListContains("hidden")
            || focused == null
            || focused == activeOverlay
            || activeOverlay.Contains(focused)
        )
        {
            return;
        }

        evt.StopImmediatePropagation();
        VisualElement overlay = activeOverlay;
        root?.schedule.Execute(() =>
        {
            if (activeOverlay != overlay || overlay.ClassListContains("hidden"))
                return;

            VisualElement target =
                ResolveOverlayFocusTarget(overlay, null);
            target?.Focus();
        });
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (
            evt.keyCode != KeyCode.Escape
            || controlsOverlay == null
            || controlsOverlay.ClassListContains("hidden")
        )
        {
            return;
        }

        HideControlsOverlay();
        evt.StopImmediatePropagation();
    }

    private void OnNavigationCancel(NavigationCancelEvent evt)
    {
        if (
            controlsOverlay == null
            || controlsOverlay.ClassListContains("hidden")
        )
        {
            return;
        }

        HideControlsOverlay();
        evt.StopImmediatePropagation();
    }

    public static bool IsPointerOverUI(Vector2 screenPosition)
    {
        if (Instance?.root?.panel == null)
            return false;

        Vector2 topLeftScreenPosition = new(screenPosition.x, Screen.height - screenPosition.y);
        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
            Instance.root.panel,
            topLeftScreenPosition
        );
        VisualElement picked = Instance.root.panel.Pick(panelPosition);
        return picked != null && picked != Instance.root && picked != Instance.screen;
    }

    private bool TryGetCard(int cardIndex, out UnitCardElement card)
    {
        if (cardIndex < 0 || cardIndex >= cards.Length)
        {
            Debug.LogWarning($"[GameHUDController] Card index {cardIndex} is out of range.");
            card = null;
            return false;
        }

        card = cards[cardIndex];
        return card != null;
    }

    private bool TryGetEnemyCard(int cardIndex, out UnitCardElement card)
    {
        if (cardIndex < 0 || cardIndex >= enemyCards.Length)
        {
            Debug.LogWarning($"[GameHUDController] Enemy card index {cardIndex} is out of range.");
            card = null;
            return false;
        }

        card = enemyCards[cardIndex];
        return card != null;
    }

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        float width = evt.newRect.width;
        float height = evt.newRect.height;
        root.EnableInClassList("compact", width < 1700f);
        root.EnableInClassList("narrow", width < 1280f);
        root.EnableInClassList("phone", width < 1120f);
        root.EnableInClassList("short", height < 960f);
    }
}
