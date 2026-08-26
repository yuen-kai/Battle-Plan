using System;
using System.Collections;
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

    [SerializeField]
    private VisualTreeAsset unitCardTemplate;

    [SerializeField]
    private Camera boardCamera;

    // Below this the reserved bars would leave no usable board, so the fit is abandoned rather
    // than collapsing the viewport to a slit.
    private const float MinBoardViewportHeight = 0.35f;

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
    private VisualElement enemyStatusStrip;
    private VisualElement hillStatusReadout;
    private VisualElement escortStatusReadout;
    private VisualElement deploymentOverlay;
    private VisualElement resultsOverlay;
    private VisualElement resultsPanel;
    private Label phaseLabel;
    private Label timerLabel;
    private Label hillStatusLabel;
    private Label escortStatusKey;
    private Label escortStatusLabel;
    private Label deploymentStatus;
    private Label resultsStatus;
    private Label targetFeedbackLabel;
    private VisualElement coachPrompt;
    private Label coachPromptLabel;
    private VisualElement lessonPopup;
    private Label lessonPopupLabel;
    private Button lessonPopupButton;
    private Action lessonDismissAction;
    private VisualElement planningCommit;
    private Label planningCommitStatus;
    private Button lockInButton;
    private Button playAgainButton;
    private Button mainMenuButton;
    private VisualElement reportReveal;
    private VisualElement reportBoardElement;
    private VisualElement reportOrders;
    private Label reportEmpty;
    private Label reportRoundLabel;
    private Label reportSummary;
    private Button reportPrevButton;
    private Button reportNextButton;
    private BattleReportBoard reportBoard;
    private BattleReport activeReport;
    private int reportRoundIndex;
    private int reportLocalTeamIndex;
    private Button exitMatchButton;
    private Button controlsButton;
    private VisualElement controlsOverlay;
    private VisualElement controlsPanel;
    private Button controlsCloseButton;
    private Button settingsButton;
    private VisualElement settingsOverlay;
    private VisualElement settingsPanel;
    private Button settingsCloseButton;
    private SettingsPanelBinder settings;
    private VisualElement activeOverlay;
    private VisualElement rejoinNotice;
    private Label rejoinNoticeTitle;
    private Label rejoinNoticeStatus;
    private Action playAgainAction;
    private Action mainMenuAction;
    private Action exitMatchAction;
    private Coroutine flashCoroutine;
    private Coroutine rejoinNoticeCoroutine;
    private bool callbacksRegistered;
    private bool timerSuppressed;

    public string HillStatusText => hillStatusLabel?.text ?? string.Empty;
    public string EscortStatusText =>
        $"{escortStatusKey?.text ?? string.Empty} · {escortStatusLabel?.text ?? string.Empty}";
    public string RejoinNoticeText => rejoinNoticeStatus?.text ?? string.Empty;

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
        if (boardCamera == null)
            boardCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        CacheElements();
        RegisterCallbacks();
        BuildCards();
        ConsoleUiNavigation.ConfigureButtons(root);
        HideResults();
        CloseControlsOverlay(false);
        CloseSettingsOverlay(false);
        ClearTargetFeedback();
        HideRejoinNotice();
        HidePlanningCommit();
        ShowDeployment();
        SetCardsInteractable(false);
        SetMatchSummary(MatchOptions.Current);
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
        // A level chosen mid-match survives leaving it, even if the sheet never got closed.
        GameSettings.Flush();
        ReleaseBoardViewport();
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }
        if (rejoinNoticeCoroutine != null)
        {
            StopCoroutine(rejoinNoticeCoroutine);
            rejoinNoticeCoroutine = null;
        }
        flash?.RemoveFromClassList("hud-flash--active");

        foreach (UnitCardElement card in cards)
            card?.Dispose();
        foreach (UnitCardElement card in enemyCards)
            card?.Dispose();
        Array.Clear(cards, 0, cards.Length);
        Array.Clear(enemyCards, 0, enemyCards.Length);
        playAgainAction = null;
        mainMenuAction = null;
        exitMatchAction = null;
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
        enemyStatusStrip = RequireElement<VisualElement>("enemy-status-strip");
        deploymentOverlay = RequireElement<VisualElement>("deployment-overlay");
        resultsOverlay = RequireElement<VisualElement>("results-overlay");
        resultsPanel = RequireElement<VisualElement>("results-panel");
        phaseLabel = RequireElement<Label>("phase-label");
        timerLabel = RequireElement<Label>("timer-label");
        hillStatusReadout = RequireElement<VisualElement>("hill-status-readout");
        hillStatusLabel = RequireElement<Label>("hill-status-label");
        escortStatusReadout = RequireElement<VisualElement>("escort-status-readout");
        escortStatusKey = RequireElement<Label>("escort-status-key");
        escortStatusLabel = RequireElement<Label>("escort-status-label");
        rejoinNotice = RequireElement<VisualElement>("rejoin-notice");
        rejoinNoticeTitle = RequireElement<Label>("rejoin-notice-title");
        rejoinNoticeStatus = RequireElement<Label>("rejoin-notice-status");
        deploymentStatus = RequireElement<Label>("deployment-status");
        resultsStatus = RequireElement<Label>("results-status");
        playAgainButton = RequireElement<Button>("play-again-button");
        mainMenuButton = RequireElement<Button>("main-menu-button");
        reportReveal = RequireElement<VisualElement>("report-reveal");
        reportBoardElement = RequireElement<VisualElement>("report-board");
        reportOrders = RequireElement<VisualElement>("report-orders");
        reportEmpty = RequireElement<Label>("report-empty");
        reportRoundLabel = RequireElement<Label>("report-round-label");
        reportSummary = RequireElement<Label>("report-summary");
        reportPrevButton = RequireElement<Button>("report-prev-button");
        reportNextButton = RequireElement<Button>("report-next-button");
        reportBoard = new BattleReportBoard(reportBoardElement);
        targetFeedbackLabel = root.Q<Label>("target-feedback-label");
        coachPrompt = root.Q<VisualElement>("coach-prompt");
        coachPromptLabel = root.Q<Label>("coach-prompt-label");
        lessonPopup = root.Q<VisualElement>("lesson-popup");
        lessonPopupLabel = root.Q<Label>("lesson-popup-label");
        lessonPopupButton = root.Q<Button>("lesson-popup-button");
        planningCommit = root.Q<VisualElement>("planning-commit");
        planningCommitStatus = root.Q<Label>("planning-commit-status");
        lockInButton = root.Q<Button>("lock-in-button");
        exitMatchButton = root.Q<Button>("exit-match-button");
        controlsButton = root.Q<Button>("controls-button");
        controlsOverlay = root.Q<VisualElement>("controls-overlay");
        controlsPanel = controlsOverlay?.Q<VisualElement>(className: "controls-panel");
        controlsCloseButton = root.Q<Button>("controls-close-button");
        settingsButton = root.Q<Button>("settings-button");
        settingsOverlay = RequireElement<VisualElement>("settings-overlay");
        settingsPanel = RequireElement<VisualElement>("settings-panel");
        settingsCloseButton = RequireElement<Button>("settings-close-button");
        settings = new SettingsPanelBinder(settingsOverlay, nameof(GameHUDController));
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
        if (reportPrevButton != null)
            reportPrevButton.clicked += OnReportPreviousClicked;
        if (reportNextButton != null)
            reportNextButton.clicked += OnReportNextClicked;
        resultsPanel?.RegisterCallback<KeyDownEvent>(OnResultsKeyDown);
        if (exitMatchButton != null)
            exitMatchButton.clicked += OnExitMatchClicked;
        if (controlsButton != null)
            controlsButton.clicked += ShowControlsOverlay;
        if (controlsCloseButton != null)
            controlsCloseButton.clicked += HideControlsOverlay;
        if (settingsButton != null)
            settingsButton.clicked += ShowSettingsOverlay;
        if (settingsCloseButton != null)
            settingsCloseButton.clicked += HideSettingsOverlay;
        settings.Bind();
        if (lockInButton != null)
            lockInButton.clicked += OnLockInClicked;
        if (lessonPopupButton != null)
            lessonPopupButton.clicked += OnLessonDismissClicked;
        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        // The bars resize on their own when breakpoints or card content change, which the root's
        // geometry event does not report.
        enemyStatusStrip?.RegisterCallback<GeometryChangedEvent>(OnChromeGeometryChanged);
        hudDock?.RegisterCallback<GeometryChangedEvent>(OnChromeGeometryChanged);
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
        if (reportPrevButton != null)
            reportPrevButton.clicked -= OnReportPreviousClicked;
        if (reportNextButton != null)
            reportNextButton.clicked -= OnReportNextClicked;
        resultsPanel?.UnregisterCallback<KeyDownEvent>(OnResultsKeyDown);
        if (exitMatchButton != null)
            exitMatchButton.clicked -= OnExitMatchClicked;
        if (controlsButton != null)
            controlsButton.clicked -= ShowControlsOverlay;
        if (controlsCloseButton != null)
            controlsCloseButton.clicked -= HideControlsOverlay;
        if (settingsButton != null)
            settingsButton.clicked -= ShowSettingsOverlay;
        if (settingsCloseButton != null)
            settingsCloseButton.clicked -= HideSettingsOverlay;
        settings.Unbind();
        if (lockInButton != null)
            lockInButton.clicked -= OnLockInClicked;
        if (lessonPopupButton != null)
            lessonPopupButton.clicked -= OnLessonDismissClicked;
        root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        enemyStatusStrip?.UnregisterCallback<GeometryChangedEvent>(OnChromeGeometryChanged);
        hudDock?.UnregisterCallback<GeometryChangedEvent>(OnChromeGeometryChanged);
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
    }

    public void ClearTargetFeedback()
    {
        SetTargetFeedback(string.Empty, false);
    }

    /// <summary>
    /// Shows the tutorial's current instruction, or clears it when the message is empty. Only one
    /// instruction is ever on screen: the caller replaces it rather than stacking prompts.
    /// </summary>
    public void SetCoachPrompt(string message)
    {
        if (coachPrompt == null || coachPromptLabel == null)
            return;

        bool visible = !string.IsNullOrWhiteSpace(message);
        coachPromptLabel.text = visible ? message : string.Empty;
        coachPrompt.EnableInClassList("hidden", !visible);
        coachPrompt.EnableInClassList("coach-prompt--visible", visible);
    }

    /// <summary>
    /// Shows a tutorial lesson, the larger card used for the points worth stopping to read, as
    /// opposed to the instruction line telling the player what to do right now. The card has no
    /// timer: it stays up until <paramref name="onDismiss"/> is triggered from its own button.
    /// </summary>
    public void SetLessonPopup(string message, string dismissLabel = "Got it", Action onDismiss = null)
    {
        if (lessonPopup == null || lessonPopupLabel == null)
            return;

        bool visible = !string.IsNullOrWhiteSpace(message);
        lessonDismissAction = visible ? onDismiss : null;
        lessonPopupLabel.text = visible ? message : string.Empty;
        lessonPopup.EnableInClassList("hidden", !visible);
        lessonPopup.EnableInClassList("lesson-popup--visible", visible);
        if (lessonPopupButton != null)
            lessonPopupButton.text = dismissLabel;
    }

    private void OnLessonDismissClicked()
    {
        Action dismiss = lessonDismissAction;
        SetLessonPopup(string.Empty);
        dismiss?.Invoke();
    }

    /// <summary>
    /// Hides the planning countdown for modes paced by the player rather than a clock. The planner
    /// keeps pushing values every frame, so the suppression lives here rather than at the source.
    /// </summary>
    public void SuppressTimer(bool suppressed)
    {
        if (timerSuppressed == suppressed)
            return;

        timerSuppressed = suppressed;
        if (suppressed && timerLabel != null)
        {
            timerLabel.text = string.Empty;
            timerLabel.RemoveFromClassList("timer-label--urgent");
        }
    }

    /// <summary>
    /// Trims both strips to the crew size actually fielded, so a sandbox match with fewer units
    /// does not leave unconfigured cards standing in the dock.
    /// </summary>
    public void SetFieldedCardCount(int fieldedCount)
    {
        for (int i = 0; i < cards.Length; i++)
            cards[i]?.Root?.EnableInClassList("hidden", i >= fieldedCount);
        for (int i = 0; i < enemyCards.Length; i++)
            enemyCards[i]?.Root?.EnableInClassList("hidden", i >= fieldedCount);
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

        phaseLabel.text = message ?? string.Empty;
        phaseLabel.EnableInClassList(
            "phase-label--friendly",
            perspective == MessagePerspective.Friendly
        );
        phaseLabel.EnableInClassList("phase-label--enemy", perspective == MessagePerspective.Enemy);
        phaseLabel.EnableInClassList(
            "phase-label--neutral",
            perspective == MessagePerspective.Neutral
        );
    }

    public void SetTimer(float seconds)
    {
        if (timerLabel == null || timerSuppressed)
            return;

        int displayedSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
        timerLabel.text = displayedSeconds > 0 ? displayedSeconds.ToString() : string.Empty;
        timerLabel.EnableInClassList(
            "timer-label--urgent",
            displayedSeconds > 0 && displayedSeconds <= 5
        );
    }

    public void Flash(MessagePerspective perspective, float duration = 0.2f)
    {
        if (flash == null)
            return;

        Color color =
            perspective == MessagePerspective.Friendly
                ? TeamPalette.FlashFriendly
                : (
                    perspective == MessagePerspective.Enemy
                        ? TeamPalette.FlashEnemy
                        : TeamPalette.FlashNeutral
                );
        flash.style.backgroundColor = color;

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

        bool showEscortStatus = options.IsEscort;
        root?.EnableInClassList("escort", showEscortStatus);
        if (!showEscortStatus)
        {
            escortStatusReadout?.AddToClassList("hidden");
            return;
        }

        SetEscortState(
            GameLoop.Instance != null ? GameLoop.Instance.EscortStatus : EscortState.Empty,
            GameLoop.Instance != null ? GameLoop.Instance.LocalTeamIndex : -1
        );
    }

    public void SetEscortState(EscortState state, int localTeamIndex)
    {
        if (escortStatusReadout == null || escortStatusLabel == null || escortStatusKey == null)
            return;

        bool running = state.IsRunning && localTeamIndex >= 0;
        escortStatusReadout.EnableInClassList("hidden", !running);
        if (!running)
            return;

        bool localEscorts = EscortSeries.IsEscortingTeam(state.LegNumber, localTeamIndex);
        bool enemyEscorts = EscortSeries.IsEscortingTeam(
            state.LegNumber,
            GameLoop.GetEnemyTeamIndex(localTeamIndex)
        );
        string role = localEscorts && enemyEscorts ? "Both escort"
            : localEscorts ? "You escort"
            : "You defend";
        escortStatusKey.text =
            $"Leg {Mathf.Clamp(state.LegNumber, 1, EscortSeries.DeciderLeg)}"
            + $"/{EscortSeries.DeciderLeg} · {role}";

        int rounds = state.RoundsRemaining;
        escortStatusLabel.text =
            $"{state.LegWinsFor(localTeamIndex)}–"
            + $"{state.LegWinsFor(GameLoop.GetEnemyTeamIndex(localTeamIndex))} · "
            + (rounds == 1 ? "1 round left" : $"{rounds} rounds left");

        bool decider = localEscorts && enemyEscorts;
        bool hostEscorts = EscortSeries.IsEscortingTeam(
            state.LegNumber,
            GameLoop.HostTeamIndex
        );
        escortStatusLabel.EnableInClassList("hill-status--contested", decider);
        escortStatusLabel.EnableInClassList("hill-status--blue", !decider && hostEscorts);
        escortStatusLabel.EnableInClassList("hill-status--red", !decider && !hostEscorts);
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

        hillStatusLabel.EnableInClassList("hill-status--blue", hostControlled);
        hillStatusLabel.EnableInClassList("hill-status--red", opponentControlled);
        hillStatusLabel.EnableInClassList(
            "hill-status--contested",
            state.Status == HillControlStatus.Contested
        );

        hillStatusLabel.text = state.Status switch
        {
            HillControlStatus.Contested => "Contested · streak reset",
            HillControlStatus.Controlled =>
                $"{(hostControlled ? "Blue" : "Red")} control · "
                + $"{Mathf.Clamp(state.Streak, 0, GameLoop.HillControlRoundsToWin)}/"
                + GameLoop.HillControlRoundsToWin,
            _ => "No control · 0/" + GameLoop.HillControlRoundsToWin,
        };
    }

    /// <summary>
    /// Raises the "someone dropped" readout with a live countdown to the end of the rejoin window.
    /// The remaining player has to be able to decide whether to keep waiting, so the seconds are
    /// shown rather than an indefinite spinner.
    /// </summary>
    public void ShowRejoinNotice(string headline, float secondsRemaining)
    {
        if (rejoinNotice == null)
            return;

        if (rejoinNoticeTitle != null)
            rejoinNoticeTitle.text = string.IsNullOrWhiteSpace(headline)
                ? "Opponent dropped"
                : headline;
        rejoinNotice.RemoveFromClassList("hidden");
        SetRejoinCountdown(secondsRemaining);

        if (rejoinNoticeCoroutine != null)
            StopCoroutine(rejoinNoticeCoroutine);
        rejoinNoticeCoroutine = StartCoroutine(RejoinNoticeCountdown(secondsRemaining));
    }

    public void HideRejoinNotice()
    {
        if (rejoinNoticeCoroutine != null)
        {
            StopCoroutine(rejoinNoticeCoroutine);
            rejoinNoticeCoroutine = null;
        }

        rejoinNotice?.AddToClassList("hidden");
    }

    public static string FormatRejoinCountdown(float secondsRemaining)
    {
        int seconds = Mathf.Max(0, Mathf.CeilToInt(secondsRemaining));
        return seconds > 0 ? $"Rejoin window · {seconds}s" : "Rejoin window closed";
    }

    private void SetRejoinCountdown(float secondsRemaining)
    {
        if (rejoinNoticeStatus != null)
            rejoinNoticeStatus.text = FormatRejoinCountdown(secondsRemaining);
    }

    private IEnumerator RejoinNoticeCountdown(float secondsRemaining)
    {
        // Unscaled: the window is wall-clock on the server too, and dev fast-forward must not make
        // the readout disagree with the deadline it is counting to.
        while (secondsRemaining > 0f)
        {
            yield return null;
            secondsRemaining -= Time.unscaledDeltaTime;
            SetRejoinCountdown(secondsRemaining);
        }

        SetRejoinCountdown(0f);
        rejoinNoticeCoroutine = null;
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
        SetVerdictTone(result, localTeamIndex);
    }

    public void ShowResults(string status, Action onPlayAgain, Action onMainMenu)
    {
        if (resultsStatus != null)
            resultsStatus.text = status ?? "Match complete";
        // Neutral until a typed result says otherwise. The string overload is the tutorial and the
        // dev harness, neither of which won anything.
        resultsStatus?.RemoveFromClassList("results-status--victory");
        resultsStatus?.RemoveFromClassList("results-status--defeat");

        playAgainAction = onPlayAgain;
        mainMenuAction = onMainMenu;
        // The round is over, so nothing may still be offering to commit orders for it. The planning
        // cluster is normally retired when planning ends, but a match can also finish from a
        // forfeit or a disconnect mid-phase, and a live "Lock in" behind a result reads as a HUD
        // that has not noticed the game stopped.
        HidePlanningCommit();
        SetResultButtonsEnabled(true, true);
        CloseControlsOverlay(false);
        CloseSettingsOverlay(false);
        HideDeployment();
        resultsOverlay?.RemoveFromClassList("hidden");
        resultsOverlay?.BringToFront();
        ActivateOverlay(resultsOverlay, playAgainButton);
    }

    /// <summary>
    /// Colours the verdict. A draw and the tutorial's own ending stay neutral for the same reason
    /// the copy does: nobody won, and a gold headline over "Draw" would say otherwise.
    /// </summary>
    private void SetVerdictTone(MatchResult result, int localTeamIndex)
    {
        if (resultsStatus == null)
            return;

        bool decided =
            result.IsValid
            && result.HasWinner
            && result.Reason != MatchResultReason.TutorialComplete;
        bool won = decided && result.WinningTeamIndex == localTeamIndex;
        resultsStatus.EnableInClassList("results-status--victory", won);
        resultsStatus.EnableInClassList("results-status--defeat", decided && !won);
    }

    // === BATTLE REPORT REVEAL ===

    /// <summary>
    /// Loads the match reveal and opens it on the last round, which is the one the player just
    /// lived through and the only one they have a question about yet.
    /// </summary>
    public void SetBattleReport(BattleReport report, int localTeamIndex)
    {
        activeReport = report != null && report.HasContent ? report : null;
        reportLocalTeamIndex = localTeamIndex;
        reportRoundIndex = activeReport != null ? activeReport.rounds.Count - 1 : 0;

        bool hasReport = activeReport != null;
        reportReveal?.EnableInClassList("hidden", !hasReport);
        reportEmpty?.EnableInClassList("hidden", hasReport);
        RenderReportRound();
    }

    private void OnReportPreviousClicked() => StepReportRound(-1);

    private void OnReportNextClicked() => StepReportRound(1);

    private void StepReportRound(int delta)
    {
        if (activeReport == null)
            return;

        int next = Mathf.Clamp(reportRoundIndex + delta, 0, activeReport.rounds.Count - 1);
        if (next == reportRoundIndex)
            return;

        reportRoundIndex = next;
        RenderReportRound();
    }

    /// <summary>Arrow keys step rounds so the reveal is usable without aiming at two small buttons.</summary>
    private void OnResultsKeyDown(KeyDownEvent evt)
    {
        if (activeReport == null)
            return;

        if (evt.keyCode == KeyCode.LeftArrow)
            StepReportRound(-1);
        else if (evt.keyCode == KeyCode.RightArrow)
            StepReportRound(1);
        else
            return;

        evt.StopPropagation();
    }

    private void RenderReportRound()
    {
        if (activeReport == null)
        {
            reportBoard?.SetRound(null, null, reportLocalTeamIndex);
            reportOrders?.Clear();
            if (reportRoundLabel != null)
                reportRoundLabel.text = string.Empty;
            if (reportSummary != null)
                reportSummary.text = string.Empty;
            return;
        }

        BattleReportRound round = activeReport.rounds[reportRoundIndex];
        reportBoard?.SetRound(activeReport, round, reportLocalTeamIndex);

        if (reportRoundLabel != null)
        {
            reportRoundLabel.text =
                $"Round {round.roundNumber} of {activeReport.rounds[^1].roundNumber}";
        }

        reportPrevButton?.SetEnabled(reportRoundIndex > 0);
        reportNextButton?.SetEnabled(reportRoundIndex < activeReport.rounds.Count - 1);

        if (reportSummary != null)
            reportSummary.text = BuildRoundSummary(round);

        BuildOrderRows(round);
    }

    private string BuildRoundSummary(BattleReportRound round)
    {
        int friendlyLosses = 0;
        int enemyLosses = 0;
        foreach (BattleReportEntry entry in round.entries)
        {
            if (!entry.diedThisRound)
                continue;
            if (entry.teamIndex == reportLocalTeamIndex)
                friendlyLosses++;
            else
                enemyLosses++;
        }

        string losses = (friendlyLosses, enemyLosses) switch
        {
            (0, 0) => "No one was eliminated.",
            (0, _) => Pluralize(enemyLosses, "enemy unit", "enemy units") + " eliminated.",
            (_, 0) => "You lost " + Pluralize(friendlyLosses, "unit", "units") + ".",
            _ =>
                $"You lost {Pluralize(friendlyLosses, "unit", "units")}; "
                + $"{Pluralize(enemyLosses, "enemy unit", "enemy units")} eliminated.",
        };

        if (activeReport.gameMode != GameMode.KingOfTheHill)
            return losses;

        string hill;
        if (round.hillContested)
            hill = "The hill was contested.";
        else if (round.hillControllerTeamIndex == GameLoop.NoHillController)
            hill = "The hill was empty.";
        else
        {
            string holder = round.hillControllerTeamIndex == reportLocalTeamIndex ? "You" : "They";
            hill =
                $"{holder} held the hill "
                + $"({round.hillStreak}/{GameLoop.HillControlRoundsToWin}).";
        }

        return $"{losses} {hill}";
    }

    private static string Pluralize(int count, string singular, string plural)
    {
        return count == 1 ? $"1 {singular}" : $"{count} {plural}";
    }

    private void BuildOrderRows(BattleReportRound round)
    {
        if (reportOrders == null)
            return;

        reportOrders.Clear();
        AddOrderTeamSection(round, reportLocalTeamIndex, "Your crew");
        AddOrderTeamSection(round, GetEnemyReportTeamIndex(), "Their crew");
    }

    private int GetEnemyReportTeamIndex()
    {
        return reportLocalTeamIndex == GameLoop.HostTeamIndex
            ? GameLoop.OpponentTeamIndex
            : GameLoop.HostTeamIndex;
    }

    private void AddOrderTeamSection(BattleReportRound round, int teamIndex, string heading)
    {
        Label headingLabel = new(heading) { pickingMode = PickingMode.Ignore };
        headingLabel.AddToClassList("report-team-heading");
        reportOrders.Add(headingLabel);

        foreach (BattleReportEntry entry in round.entries)
        {
            if (entry.teamIndex != teamIndex)
                continue;
            reportOrders.Add(BuildOrderRow(entry));
        }
    }

    private VisualElement BuildOrderRow(BattleReportEntry entry)
    {
        bool friendly = entry.teamIndex == reportLocalTeamIndex;
        bool gone = entry.order == BattleReportOrder.Eliminated;

        VisualElement row = new() { pickingMode = PickingMode.Ignore };
        row.AddToClassList("report-order-row");
        row.EnableInClassList("report-order-row--enemy", !friendly);
        row.EnableInClassList("report-order-row--dead", entry.diedThisRound);
        row.EnableInClassList("report-order-row--gone", gone);

        Label badge = new((entry.rosterSlot + 1).ToString()) { pickingMode = PickingMode.Ignore };
        badge.AddToClassList("report-order-row__badge");
        row.Add(badge);

        Label name = new(ResolveUnitName(entry)) { pickingMode = PickingMode.Ignore };
        name.AddToClassList("report-order-row__name");
        row.Add(name);

        Label order = new(DescribeOrder(entry)) { pickingMode = PickingMode.Ignore };
        order.AddToClassList("report-order-row__order");
        row.Add(order);

        Label state = new(DescribeState(entry)) { pickingMode = PickingMode.Ignore };
        state.AddToClassList("report-order-row__state");
        row.Add(state);

        return row;
    }

    private string ResolveUnitName(BattleReportEntry entry)
    {
        if (
            activeReport == null
            || !activeReport.TryGetCrewMember(
                entry.teamIndex,
                entry.rosterSlot,
                out BattleReportCrewMember member
            )
        )
        {
            return $"Unit {entry.rosterSlot + 1}";
        }

        UnitDatabase catalog = GameLoop.Instance != null ? GameLoop.Instance.allUnits : null;
        if (
            catalog?.units == null
            || member.catalogIndex < 0
            || member.catalogIndex >= catalog.units.Count
            || catalog.units[member.catalogIndex] == null
        )
        {
            return $"Unit {entry.rosterSlot + 1}";
        }

        return catalog.units[member.catalogIndex].unitName;
    }

    private static string DescribeOrder(BattleReportEntry entry)
    {
        switch (entry.order)
        {
            case BattleReportOrder.Eliminated:
                return "Already eliminated";
            case BattleReportOrder.Ability:
                return entry.hasAbilityTarget
                    ? $"Ability on ({entry.abilityTarget.x}, {entry.abilityTarget.y})"
                    : "Ability";
            case BattleReportOrder.Dodge:
                return $"Dodged to ({entry.endCell.x}, {entry.endCell.y})";
            case BattleReportOrder.Move:
                int steps = Mathf.Max(0, (entry.path?.Count ?? 1) - 1);
                return $"Moved {Pluralize(steps, "cell", "cells")} "
                    + $"to ({entry.endCell.x}, {entry.endCell.y})";
            default:
                return $"Held ({entry.startCell.x}, {entry.startCell.y})";
        }
    }

    private string DescribeState(BattleReportEntry entry)
    {
        if (entry.order == BattleReportOrder.Eliminated)
            return string.Empty;
        if (entry.diedThisRound)
            return "Eliminated";

        if (
            activeReport != null
            && activeReport.TryGetCrewMember(
                entry.teamIndex,
                entry.rosterSlot,
                out BattleReportCrewMember member
            )
            && member.maxHealth > 0
        )
        {
            return $"{entry.healthAtRoundEnd}/{member.maxHealth} HP";
        }

        return $"{entry.healthAtRoundEnd} HP";
    }

    public void HideResults()
    {
        resultsOverlay?.AddToClassList("hidden");
        ClearActiveOverlay(resultsOverlay, null);
        playAgainAction = null;
        mainMenuAction = null;
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

    /// <summary>
    /// Puts a way out in the dock's action row, for the modes that have no other one.
    ///
    /// <para>
    /// An ordinary match is left from the results overlay, which only exists once somebody has
    /// won. The tutorial has no opponent that can win and no clock — its planning window is ten
    /// minutes and the HUD hides the countdown — so a player who wants to stop partway through has
    /// nothing to press. This is that button, and it is asked for rather than always present so
    /// the two modes that do have an ending are not given a second, quieter way to quit mid-round.
    /// </para>
    /// </summary>
    public void ShowExitMatch(string label, Action onExit)
    {
        exitMatchAction = onExit;
        if (exitMatchButton == null)
            return;

        if (!string.IsNullOrEmpty(label))
            exitMatchButton.text = label;
        exitMatchButton.SetEnabled(true);
        exitMatchButton.RemoveFromClassList("hidden");
    }

    public void HideExitMatch()
    {
        exitMatchAction = null;
        exitMatchButton?.AddToClassList("hidden");
    }

    private void OnExitMatchClicked()
    {
        Action action = exitMatchAction;
        // The button is leaving with the scene either way; disabling it stops a second press
        // landing during the shutdown the first one starts.
        exitMatchButton?.SetEnabled(false);
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

    /// <summary>
    /// Opens the levels over a running round. The match is not paused and nothing is re-sent on
    /// close: the sheet only borrows the dock the way the controls sheet does, and hands it back.
    /// </summary>
    private void ShowSettingsOverlay()
    {
        if (settingsOverlay == null || IsShowing(resultsOverlay))
            return;

        settings.ShowStoredSettings();
        settingsOverlay.RemoveFromClassList("hidden");
        settingsOverlay.BringToFront();
        ActivateOverlay(settingsOverlay, settings.FirstControl);
    }

    private void HideSettingsOverlay()
    {
        CloseSettingsOverlay(true);
    }

    /// <summary>
    /// Card interactability is deliberately left alone. The overlay disables the dock as a whole and
    /// re-enables that one flag, so each card keeps whatever the phase or a pending rejoin last set
    /// on it and closing the sheet cannot hand back a card the match wanted disabled.
    /// </summary>
    private void CloseSettingsOverlay(bool restoreFocus)
    {
        // Volume writes stream in while a slider is dragged, so the disk write waits for the exit.
        if (IsShowing(settingsOverlay))
            GameSettings.Flush();

        settingsOverlay?.AddToClassList("hidden");
        ClearActiveOverlay(settingsOverlay, restoreFocus ? settingsButton : null);
    }

    private static bool IsShowing(VisualElement overlay)
    {
        return overlay != null && !overlay.ClassListContains("hidden");
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
        else if (overlay == settingsOverlay)
        {
            if (CanGrabFocus(settings?.FirstControl))
                return settings.FirstControl;
            if (CanGrabFocus(settingsCloseButton))
                return settingsCloseButton;
            if (CanGrabFocus(settingsPanel))
                return settingsPanel;
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

    /// <summary>
    /// A dropdown builds its list outside the sheet that opened it, so the guard below has to let
    /// that list through; otherwise picking a quality level would snap focus back to the first row.
    /// </summary>
    private static bool IsInRuntimeDropdown(VisualElement element)
    {
        for (VisualElement current = element; current != null; current = current.parent)
        {
            if (current.ClassListContains("unity-base-dropdown"))
                return true;
        }

        return false;
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
            || IsInRuntimeDropdown(focused)
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
        if (evt.keyCode != KeyCode.Escape || !TryDismissOpenSheet(true))
            return;

        evt.StopImmediatePropagation();
    }

    private void OnNavigationCancel(NavigationCancelEvent evt)
    {
        if (!TryDismissOpenSheet(true))
            return;

        evt.StopImmediatePropagation();
    }

    /// <summary>
    /// Closes whichever sheet the player left open because the match now needs the board. Called
    /// when a dodge prompt begins: that window is a few seconds long, so a panel someone is reading
    /// costs them units. Focus is dropped rather than handed back, because the button that reopens
    /// the sheet must not be sitting under the next keypress and an arrow key must not still reach a
    /// slider inside a sheet that has gone away. Card interactability stays the match's to set.
    /// </summary>
    public void DismissOpenSheets()
    {
        VisualElement focused = root?.focusController?.focusedElement as VisualElement;
        bool focusWasInASheet =
            focused != null
            && (
                (settingsOverlay != null && settingsOverlay.Contains(focused))
                || (controlsOverlay != null && controlsOverlay.Contains(focused))
            );

        if (!TryDismissOpenSheet(false))
            return;

        if (focusWasInASheet)
            focused.Blur();
    }

    /// <summary>
    /// Dismisses the sheet the player opened, innermost first. Reports whether anything was open so
    /// Escape and cancel stay untouched during a round and keep reaching the match itself, and so a
    /// dismissal with nothing open never touches the dock.
    /// </summary>
    private bool TryDismissOpenSheet(bool restoreFocus)
    {
        if (IsShowing(settingsOverlay))
        {
            CloseSettingsOverlay(restoreFocus);
            return true;
        }

        if (IsShowing(controlsOverlay))
        {
            CloseControlsOverlay(restoreFocus);
            return true;
        }

        return false;
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
        FitBoardViewport();
    }

    private void OnChromeGeometryChanged(GeometryChangedEvent evt) => FitBoardViewport();

    /// <summary>
    /// Gives the board the screen it is not sharing with the HUD by shrinking the camera viewport
    /// to the band between the enemy strip and the dock, so the bars occupy real estate instead of
    /// covering the grid. Both bar heights move with the responsive breakpoints and with the
    /// panel's scale factor, so the band is measured from live layout rather than authored as a
    /// fixed rect in Game.unity. The hanging phase tab and the transient target-feedback bar are
    /// deliberately left outside this reservation: they are designed to break the bar edge, and the
    /// camera does not clear the strip it does not draw, so reserving a band the opaque bars do not
    /// cover would expose an unpainted gap.
    /// </summary>
    private void FitBoardViewport()
    {
        if (boardCamera == null || root == null)
            return;

        float panelHeight = root.worldBound.height;
        if (float.IsNaN(panelHeight) || panelHeight <= 0f)
            return;

        float top = BandFraction(BarEdge(enemyStatusStrip, true), panelHeight);
        float bottom = BandFraction(panelHeight - BarEdge(hudDock, false), panelHeight);
        float height = 1f - top - bottom;
        if (height < MinBoardViewportHeight)
            return;

        var fitted = new Rect(0f, bottom, 1f, height);
        if (boardCamera.rect != fitted)
            boardCamera.rect = fitted;
    }

    // Panel space runs downwards from the top edge, so the top bar contributes its lower edge and
    // the dock its upper edge. A dock lifted off the bottom by a breakpoint reserves that gap too.
    private static float BarEdge(VisualElement bar, bool useLowerEdge)
    {
        if (bar == null || bar.resolvedStyle.display == DisplayStyle.None)
            return float.NaN;
        Rect bounds = bar.worldBound;
        return useLowerEdge ? bounds.yMax : bounds.yMin;
    }

    private static float BandFraction(float band, float panelHeight)
    {
        if (float.IsNaN(band))
            return 0f;
        return Mathf.Clamp(band / panelHeight, 0f, 1f);
    }

    private void ReleaseBoardViewport()
    {
        if (boardCamera != null)
            boardCamera.rect = new Rect(0f, 0f, 1f, 1f);
    }
}
