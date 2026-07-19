using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class GameHUDController : MonoBehaviour
{
    private const int CardCount = 3;

    public static GameHUDController Instance { get; private set; }

    private readonly UnitCardElement[] cards = new UnitCardElement[CardCount];
    private UIDocument document;
    private VisualElement root;
    private VisualElement screen;
    private VisualElement flash;
    private VisualElement cardsContainer;
    private VisualElement deploymentOverlay;
    private VisualElement resultsOverlay;
    private VisualElement resultsPanel;
    private Label phaseLabel;
    private Label timerLabel;
    private Label planningHelp;
    private Label matchTypeLabel;
    private Label fogLabel;
    private Label hillStatusLabel;
    private Label deploymentStatus;
    private Label resultsStatus;
    private Button playAgainButton;
    private Button mainMenuButton;
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
        HideResults();
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

        foreach (UnitCardElement card in cards)
            card?.Dispose();
        Array.Clear(cards, 0, cards.Length);
        playAgainAction = null;
        mainMenuAction = null;

        if (Instance == this)
            Instance = null;
    }

    private void CacheElements()
    {
        screen = RequireElement<VisualElement>("screen");
        flash = RequireElement<VisualElement>("hud-flash");
        cardsContainer = RequireElement<VisualElement>("unit-cards");
        deploymentOverlay = RequireElement<VisualElement>("deployment-overlay");
        resultsOverlay = RequireElement<VisualElement>("results-overlay");
        resultsPanel = RequireElement<VisualElement>("results-panel");
        phaseLabel = RequireElement<Label>("phase-label");
        timerLabel = RequireElement<Label>("timer-label");
        planningHelp = RequireElement<Label>("planning-help");
        matchTypeLabel = RequireElement<Label>("match-type-label");
        fogLabel = RequireElement<Label>("fog-label");
        hillStatusLabel = RequireElement<Label>("hill-status-label");
        deploymentStatus = RequireElement<Label>("deployment-status");
        resultsStatus = RequireElement<Label>("results-status");
        playAgainButton = RequireElement<Button>("play-again-button");
        mainMenuButton = RequireElement<Button>("main-menu-button");
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
        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
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
        root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        callbacksRegistered = false;
    }

    private void BuildCards()
    {
        if (cardsContainer == null)
            return;

        for (int i = 0; i < cards.Length; i++)
        {
            VisualElement host = cardsContainer.Q<VisualElement>($"unit-card-{i}");
            if (host == null)
            {
                host = new VisualElement { name = $"unit-card-{i}" };
                host.AddToClassList("unit-card-host");
                if (i == cards.Length - 1)
                    host.AddToClassList("unit-card-host--last");
                cardsContainer.Add(host);
            }
            cards[i] = new UnitCardElement(host, i);
        }
    }

    public void ConfigureCard(
        int cardIndex,
        UnitData data,
        bool hasAbility,
        int remainingAbilityUses
    )
    {
        if (!TryGetCard(cardIndex, out UnitCardElement card))
            return;

        card.Configure(
            data,
            hasAbility,
            remainingAbilityUses,
            () => PlanMovement.Instance?.SelectUnit(cardIndex),
            () => PlanMovement.Instance?.SetSelectionMode(cardIndex, false),
            () => PlanMovement.Instance?.SetSelectionMode(cardIndex, true)
        );
    }

    public void SetCardsInteractable(bool interactable)
    {
        foreach (UnitCardElement card in cards)
            card?.SetInteractable(interactable);

        SetPlanningHelp(
            interactable
                ? "Select a unit, then choose Move or Ability."
                : "Orders locked while the round resolves."
        );
    }

    public void SetPlanningHelp(string message)
    {
        if (planningHelp != null)
            planningHelp.text = message ?? string.Empty;
    }

    public void SetCardDisabled(int cardIndex, bool disabled)
    {
        if (TryGetCard(cardIndex, out UnitCardElement card))
            card.SetDisabled(disabled);
    }

    public void SetCardAbilityUses(int cardIndex, int remainingUses)
    {
        if (TryGetCard(cardIndex, out UnitCardElement card))
            card.SetAbilityUses(remainingUses);
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
        if (timerLabel == null)
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
                ? new Color(0.12f, 0.5f, 0.78f, 0.18f)
                : (
                    perspective == MessagePerspective.Enemy
                        ? new Color(0.8f, 0.12f, 0.16f, 0.2f)
                        : new Color(0.8f, 0.65f, 0.3f, 0.14f)
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
        if (matchTypeLabel != null)
        {
            string opponent = options.IsBotMatch ? "AI" : "PLAYER";
            matchTypeLabel.text = $"{options.GameModeDisplayName.ToUpperInvariant()} / {opponent}";
        }
        if (fogLabel != null)
            fogLabel.text = options.fogOfWar ? "FOG ON" : "FOG OFF";

        if (hillStatusLabel != null)
        {
            hillStatusLabel.EnableInClassList("hidden", !options.IsKingOfTheHill);
            if (options.IsKingOfTheHill)
            {
                SetHillControl(
                    GameLoop.Instance != null ? GameLoop.Instance.HillControl : HillControlState.Empty
                );
            }
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

        hillStatusLabel.EnableInClassList("hill-status--blue", hostControlled);
        hillStatusLabel.EnableInClassList("hill-status--red", opponentControlled);
        hillStatusLabel.EnableInClassList(
            "hill-status--contested",
            state.Status == HillControlStatus.Contested
        );

        hillStatusLabel.text = state.Status switch
        {
            HillControlStatus.Contested => "CONTESTED · STREAK RESET",
            HillControlStatus.Controlled =>
                $"{(hostControlled ? "BLUE" : "RED")} CONTROL · "
                + $"{Mathf.Clamp(state.Streak, 0, GameLoop.HillControlRoundsToWin)}/"
                + GameLoop.HillControlRoundsToWin,
            _ => "NO CONTROL · 0/" + GameLoop.HillControlRoundsToWin,
        };
    }

    public void ShowDeployment(string status = null)
    {
        if (deploymentStatus != null)
        {
            deploymentStatus.text = string.IsNullOrWhiteSpace(status)
                ? "Synchronizing both players and preparing the battlefield."
                : status;
        }
        deploymentOverlay?.RemoveFromClassList("hidden");
        deploymentOverlay?.BringToFront();
    }

    public void HideDeployment()
    {
        deploymentOverlay?.AddToClassList("hidden");
    }

    public void ShowResults(string status, Action onPlayAgain, Action onMainMenu)
    {
        if (resultsStatus != null)
            resultsStatus.text = status ?? "Match complete";

        playAgainAction = onPlayAgain;
        mainMenuAction = onMainMenu;
        SetResultButtonsEnabled(true, true);
        resultsOverlay?.RemoveFromClassList("hidden");
        resultsOverlay?.BringToFront();
        root?.schedule.Execute(() => playAgainButton?.Focus());
    }

    public void HideResults()
    {
        resultsOverlay?.AddToClassList("hidden");
        playAgainAction = null;
        mainMenuAction = null;
    }

    public void SetResultButtonsEnabled(bool playAgainEnabled, bool mainMenuEnabled)
    {
        playAgainButton?.SetEnabled(playAgainEnabled);
        mainMenuButton?.SetEnabled(mainMenuEnabled);
    }

    private void OnPlayAgainClicked()
    {
        Action action = playAgainAction;
        SetResultButtonsEnabled(false, false);
        action?.Invoke();
    }

    private void OnMainMenuClicked()
    {
        Action action = mainMenuAction;
        SetResultButtonsEnabled(false, false);
        action?.Invoke();
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

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        float width = evt.newRect.width;
        float height = evt.newRect.height;
        root.EnableInClassList("compact", width < 1500f);
        root.EnableInClassList("narrow", width < 1000f);
        root.EnableInClassList("phone", width < 680f);
        root.EnableInClassList("short", height < 800f);
    }
}
