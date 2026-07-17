using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class UnitCardElement : IDisposable
{
    private readonly VisualElement container;
    private readonly VisualElement cardRoot;
    private readonly bool enemyCard;
    private readonly Button selectButton;
    private readonly VisualElement portrait;
    private readonly Label unitName;
    private readonly VisualElement healthRow;
    private readonly VisualElement healthFill;
    private readonly Label healthValue;
    private readonly Label abilityName;
    private readonly Label stateLabel;
    private readonly VisualElement modes;
    private readonly Button moveButton;
    private readonly Button abilityButton;
    private readonly Label abilityActionLabel;
    private readonly Label abilityChargeLabel;

    private Action selectAction;
    private Action moveAction;
    private Action abilityAction;
    private bool hasAbility;
    private int remainingAbilityUses;
    private string configuredAbilityName = "Ability";
    private bool isDisabled;
    private bool enemyConfigured;
    private bool enemyAlive = true;
    private bool interactionRequested;
    private bool disposed;

    public VisualElement Root => container;
    public bool IsEnemyConfigured => enemyCard && enemyConfigured;
    public bool IsEnemyAlive => IsEnemyConfigured && enemyAlive;

    public UnitCardElement(VisualElement host, int index, bool isEnemyCard = false)
    {
        container = host ?? new VisualElement();
        enemyCard = isEnemyCard;
        cardRoot =
            container.Q<VisualElement>("unit-card-root")
            ?? (container.ClassListContains("unit-card") ? container : BuildFallbackCard());
        if (cardRoot.parent == null && cardRoot != container)
            container.Add(cardRoot);

        string namePrefix = enemyCard ? "enemy-unit-card" : "unit-card";
        cardRoot.name = $"{namePrefix}-{index}-root";
        selectButton = cardRoot.Q<Button>("unit-card-select");
        portrait = cardRoot.Q<VisualElement>("unit-card-portrait");
        unitName = cardRoot.Q<Label>("unit-card-name");
        healthRow = cardRoot.Q<VisualElement>("unit-card-health");
        healthFill = cardRoot.Q<VisualElement>("unit-card-health-fill");
        healthValue = cardRoot.Q<Label>("unit-card-health-value");
        abilityName = cardRoot.Q<Label>("unit-card-ability");
        stateLabel = cardRoot.Q<Label>("unit-card-state");
        modes = cardRoot.Q<VisualElement>("unit-card-modes");
        moveButton = cardRoot.Q<Button>("unit-card-move");
        abilityButton = cardRoot.Q<Button>("unit-card-ability-button");
        abilityActionLabel = abilityButton?.Q<Label>("unit-card-ability-action");
        abilityChargeLabel = abilityButton?.Q<Label>("unit-card-ability-charge");

        if (
            selectButton == null
            || portrait == null
            || unitName == null
            || healthRow == null
            || healthFill == null
            || healthValue == null
            || abilityName == null
            || stateLabel == null
            || modes == null
            || moveButton == null
            || abilityButton == null
            || abilityActionLabel == null
            || abilityChargeLabel == null
        )
        {
            Debug.LogError(
                $"[UnitCardElement] Unit card {index} is missing one or more required elements."
            );
        }

        if (selectButton != null)
        {
            selectButton.name = $"{namePrefix}-select-{index}";
            if (enemyCard)
            {
                selectButton.focusable = false;
                selectButton.pickingMode = PickingMode.Ignore;
            }
            else
            {
                selectButton.clicked += OnSelectClicked;
            }
        }
        if (moveButton != null)
        {
            moveButton.name = $"{namePrefix}-move-{index}";
            if (!enemyCard)
                moveButton.clicked += OnMoveClicked;
        }
        if (abilityButton != null)
        {
            abilityButton.name = $"{namePrefix}-ability-{index}";
            if (!enemyCard)
                abilityButton.clicked += OnAbilityClicked;
        }

        if (enemyCard)
        {
            cardRoot.AddToClassList("unit-card--enemy");
            healthRow?.RemoveFromClassList("hidden");
            modes?.SetEnabled(false);
            if (unitName != null)
                unitName.text = "Enemy";
            if (healthValue != null)
                healthValue.text = "-- / -- HP";
            if (abilityName != null)
                abilityName.text = "Awaiting status";
            if (stateLabel != null)
                stateLabel.text = "LINKING";
        }
        SetInteractable(false);
        SetPlanningState(false, false);
    }

    public void Configure(
        UnitData data,
        bool abilityPresent,
        int abilityUses,
        Action onSelect,
        Action onMove,
        Action onAbility
    )
    {
        if (enemyCard)
            return;

        selectAction = onSelect;
        moveAction = onMove;
        abilityAction = onAbility;
        hasAbility = abilityPresent;
        remainingAbilityUses = Mathf.Max(0, abilityUses);
        configuredAbilityName =
            data != null && !string.IsNullOrWhiteSpace(data.abilityName)
                ? data.abilityName
                : "Ability";
        isDisabled = false;

        if (unitName != null)
            unitName.text = data != null ? data.unitName : "Unit";
        if (stateLabel != null)
            stateLabel.text = string.Empty;

        Sprite cardSprite =
            data != null
                ? (data.abilitySprite != null ? data.abilitySprite : data.unitSprite)
                : null;
        SetBackgroundImage(portrait, cardSprite);

        cardRoot.RemoveFromClassList("unit-card--disabled");
        RefreshAbilityState();
        SetInteractable(false);
        SetPlanningState(false, false);
    }

    public void ConfigureEnemy(
        UnitData data,
        bool abilityPresent,
        int abilityUses,
        float currentHealth,
        float maxHealth,
        bool alive
    )
    {
        if (!enemyCard)
            return;

        enemyConfigured = true;
        enemyAlive = alive;
        hasAbility = abilityPresent;
        remainingAbilityUses = Mathf.Max(0, abilityUses);
        configuredAbilityName =
            data != null && !string.IsNullOrWhiteSpace(data.abilityName)
                ? data.abilityName
                : "Ability";

        if (unitName != null)
            unitName.text = data != null ? data.unitName : "Enemy";

        Sprite cardSprite =
            data != null ? (data.unitSprite != null ? data.unitSprite : data.abilitySprite) : null;
        SetBackgroundImage(portrait, cardSprite);
        healthRow?.RemoveFromClassList("hidden");
        SetHealth(currentHealth, maxHealth);
        RefreshAbilityState();
        RefreshEnemyState();
    }

    public void SetInteractable(bool interactable)
    {
        if (enemyCard)
        {
            interactionRequested = false;
            selectButton?.SetEnabled(false);
            moveButton?.SetEnabled(false);
            abilityButton?.SetEnabled(false);
            return;
        }

        interactionRequested = interactable;
        bool enabled = interactionRequested && !isDisabled;
        selectButton?.SetEnabled(enabled);
        moveButton?.SetEnabled(enabled);
        abilityButton?.SetEnabled(enabled && AbilityAvailable);
    }

    public void SetAbilityUses(int remainingUses)
    {
        remainingAbilityUses = Mathf.Max(0, remainingUses);
        RefreshAbilityState();
        if (!AbilityAvailable)
            abilityButton?.RemoveFromClassList("unit-card__mode--armed");
    }

    public void SetDisabled(bool disabled)
    {
        if (enemyCard)
        {
            enemyAlive = !disabled;
            RefreshEnemyState();
            return;
        }

        isDisabled = disabled;
        cardRoot.EnableInClassList("unit-card--disabled", isDisabled);
        if (stateLabel != null)
            stateLabel.text = isDisabled ? "ELIMINATED" : string.Empty;
        if (isDisabled)
            SetPlanningState(false, false);
        SetInteractable(interactionRequested);
    }

    public void SetPlanningState(bool selected, bool abilityMode)
    {
        if (enemyCard)
        {
            cardRoot.RemoveFromClassList("unit-card--selected");
            return;
        }

        selected &= !isDisabled;
        abilityMode &= AbilityAvailable;

        cardRoot.EnableInClassList("unit-card--selected", selected);
        moveButton?.EnableInClassList("unit-card__mode--chosen", !abilityMode);
        abilityButton?.EnableInClassList("unit-card__mode--chosen", abilityMode);
        moveButton?.EnableInClassList("unit-card__mode--armed", selected && !abilityMode);
        abilityButton?.EnableInClassList("unit-card__mode--armed", selected && abilityMode);
    }

    public void Focus()
    {
        if (!enemyCard)
            selectButton?.Focus();
    }

    private void OnSelectClicked()
    {
        selectAction?.Invoke();
    }

    private void OnMoveClicked()
    {
        moveAction?.Invoke();
    }

    private void OnAbilityClicked()
    {
        if (AbilityAvailable)
            abilityAction?.Invoke();
    }

    private bool AbilityAvailable => hasAbility && remainingAbilityUses > 0;

    private void SetHealth(float currentHealth, float maxHealth)
    {
        float safeMaxHealth = Mathf.Max(0f, maxHealth);
        float safeCurrentHealth =
            safeMaxHealth > 0f
                ? Mathf.Clamp(currentHealth, 0f, safeMaxHealth)
                : Mathf.Max(0f, currentHealth);
        float healthRatio = safeMaxHealth > 0f ? safeCurrentHealth / safeMaxHealth : 0f;

        if (healthFill != null)
            healthFill.style.width = Length.Percent(healthRatio * 100f);
        if (healthValue != null)
        {
            healthValue.text =
                safeMaxHealth > 0f
                    ? $"{Mathf.RoundToInt(safeCurrentHealth)} / {Mathf.RoundToInt(safeMaxHealth)} HP"
                    : "HP UNKNOWN";
        }
    }

    private void RefreshEnemyState()
    {
        if (!enemyCard)
            return;

        cardRoot.EnableInClassList(
            "unit-card--enemy-active",
            enemyConfigured && enemyAlive
        );
        cardRoot.EnableInClassList(
            "unit-card--enemy-eliminated",
            enemyConfigured && !enemyAlive
        );
        cardRoot.RemoveFromClassList("unit-card--disabled");

        if (stateLabel != null)
            stateLabel.text = !enemyConfigured ? "LINKING" : (enemyAlive ? "ACTIVE" : "ELIMINATED");
        if (selectButton != null)
        {
            selectButton.tooltip =
                enemyConfigured && !enemyAlive
                    ? "Enemy unit eliminated."
                    : "Live enemy health and ability status.";
        }
    }

    private void RefreshAbilityState()
    {
        string remainingUseText =
            remainingAbilityUses == 1 ? "1 use" : $"{remainingAbilityUses} uses";

        if (abilityName != null)
        {
            abilityName.text =
                !hasAbility ? "Move only"
                : remainingAbilityUses > 0
                    ? $"{configuredAbilityName} · {remainingUseText} this match"
                    : $"{configuredAbilityName} · spent this match";
        }

        if (abilityActionLabel != null)
            abilityActionLabel.text = "ABILITY";

        if (abilityChargeLabel != null)
        {
            abilityChargeLabel.text =
                !hasAbility ? "NOT AVAILABLE"
                : remainingAbilityUses > 0
                    ? $"{remainingUseText.ToUpperInvariant()} THIS MATCH"
                    : "SPENT THIS MATCH";
        }

        if (abilityButton != null)
        {
            abilityButton.tooltip =
                !hasAbility ? "This unit can move only."
                : remainingAbilityUses > 0
                    ? $"{configuredAbilityName}: {remainingUseText} remaining this match"
                    : $"{configuredAbilityName} is spent for this match";
        }

        SetInteractable(interactionRequested);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        if (selectButton != null)
            selectButton.clicked -= OnSelectClicked;
        if (moveButton != null)
            moveButton.clicked -= OnMoveClicked;
        if (abilityButton != null)
            abilityButton.clicked -= OnAbilityClicked;
        selectAction = null;
        moveAction = null;
        abilityAction = null;
    }

    private static void SetBackgroundImage(VisualElement element, Sprite sprite)
    {
        if (element == null)
            return;
        if (sprite != null)
            element.style.backgroundImage = new StyleBackground(sprite);
        else
            element.style.backgroundImage = StyleKeyword.None;
    }

    private static VisualElement BuildFallbackCard()
    {
        VisualElement root = new() { name = "unit-card-root" };
        root.AddToClassList("unit-card");

        VisualElement stateMark = new() { name = "unit-card-state-mark" };
        stateMark.AddToClassList("unit-card__state-mark");

        Button select = new() { name = "unit-card-select" };
        select.AddToClassList("unit-card__select");
        VisualElement portrait = new() { name = "unit-card-portrait" };
        portrait.AddToClassList("unit-card__portrait");
        VisualElement copy = new();
        copy.AddToClassList("unit-card__copy");
        Label name = new("Unit") { name = "unit-card-name" };
        name.AddToClassList("unit-card__name");
        VisualElement health = new() { name = "unit-card-health" };
        health.AddToClassList("unit-card__health");
        health.AddToClassList("hidden");
        VisualElement healthTrack = new();
        healthTrack.AddToClassList("unit-card__health-track");
        VisualElement healthFill = new() { name = "unit-card-health-fill" };
        healthFill.AddToClassList("unit-card__health-fill");
        Label healthValue = new("HP UNKNOWN") { name = "unit-card-health-value" };
        healthValue.AddToClassList("unit-card__health-value");
        healthTrack.Add(healthFill);
        health.Add(healthTrack);
        health.Add(healthValue);
        Label ability = new("Move only") { name = "unit-card-ability" };
        ability.AddToClassList("unit-card__ability");
        Label state = new() { name = "unit-card-state" };
        state.AddToClassList("unit-card__state");
        copy.Add(name);
        copy.Add(health);
        copy.Add(ability);
        copy.Add(state);
        select.Add(portrait);
        select.Add(copy);

        VisualElement modes = new() { name = "unit-card-modes" };
        modes.AddToClassList("unit-card__modes");
        Button move = new() { name = "unit-card-move", text = "MOVE" };
        move.AddToClassList("unit-card__mode");
        move.AddToClassList("unit-card__mode--move");
        Button abilityButton = new() { name = "unit-card-ability-button" };
        abilityButton.AddToClassList("unit-card__mode");
        Label abilityAction = new("ABILITY") { name = "unit-card-ability-action" };
        abilityAction.AddToClassList("unit-card__mode-label");
        Label abilityCharge = new("1 USE THIS MATCH") { name = "unit-card-ability-charge" };
        abilityCharge.AddToClassList("unit-card__mode-charge");
        abilityButton.Add(abilityAction);
        abilityButton.Add(abilityCharge);
        modes.Add(move);
        modes.Add(abilityButton);

        root.Add(stateMark);
        root.Add(select);
        root.Add(modes);
        return root;
    }
}
