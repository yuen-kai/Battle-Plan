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
    private readonly VisualElement flipIndicator;
    private readonly Label cooldownLabel;

    private Action activateAction;
    private bool hasAbility;
    private int abilityCooldownRoundsRemaining;
    private string configuredAbilityName = "Ability";
    private bool isDisabled;
    private bool enemyConfigured;
    private bool enemyAlive = true;
    private bool interactionRequested;
    private bool isSelected;
    private bool isAbilityMode;
    private bool disposed;

    // Set on an enemy card the sandbox has handed to the designer. The card keeps its enemy
    // styling and health readout — it is still the other crew — but answers a press and shows a
    // planning state, because in the sandbox that strip is a second dock rather than a readout.
    private bool sandboxCommandable;
    private Action sandboxSlotAction;
    private bool sandboxPortraitBound;

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
        VisualElement abilityVignette = cardRoot.Q<VisualElement>("unit-card-ability-vignette");
        flipIndicator = cardRoot.Q<VisualElement>("unit-card-flip-indicator");
        VisualElement flipIcon = cardRoot.Q<VisualElement>("unit-card-flip-icon");
        cooldownLabel = cardRoot.Q<Label>("unit-card-cooldown");

        if (
            selectButton == null
            || portrait == null
            || unitName == null
            || healthRow == null
            || healthFill == null
            || healthValue == null
            || abilityName == null
            || stateLabel == null
            || abilityVignette == null
            || flipIndicator == null
            || flipIcon == null
            || cooldownLabel == null
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
        flipIndicator?.AddToClassList("hidden");

        // The two strips are different components sharing one template: a contact readout for the
        // enemy, an ability button for your own crew. Each is styled from its own modifier rather
        // than one being the deviation from the other.
        cardRoot.AddToClassList(enemyCard ? "unit-card--enemy" : "unit-card--friendly");

        if (enemyCard)
        {
            healthRow?.RemoveFromClassList("hidden");
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
        int cooldownRoundsRemaining,
        Action onActivate
    )
    {
        if (enemyCard)
            return;

        activateAction = onActivate;
        hasAbility = abilityPresent;
        abilityCooldownRoundsRemaining = Mathf.Max(0, cooldownRoundsRemaining);
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
        int cooldownRoundsRemaining,
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
        abilityCooldownRoundsRemaining = Mathf.Max(0, cooldownRoundsRemaining);
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
        if (enemyCard && !sandboxCommandable)
        {
            interactionRequested = false;
            selectButton?.SetEnabled(false);
            return;
        }

        interactionRequested = interactable;
        bool enabled = interactionRequested && !isDisabled;
        selectButton?.SetEnabled(enabled);
    }

    public void SetAbilityCooldown(int remainingRounds)
    {
        abilityCooldownRoundsRemaining = Mathf.Max(0, remainingRounds);
        RefreshAbilityState();
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
        if (enemyCard && !sandboxCommandable)
        {
            cardRoot.RemoveFromClassList("unit-card--selected");
            cardRoot.RemoveFromClassList("unit-card--ability");
            return;
        }

        isSelected = selected && !isDisabled;
        isAbilityMode = abilityMode && AbilityAvailable && !isDisabled;
        ApplyPlanningVisualState();
    }

    public void Focus()
    {
        if (!enemyCard || sandboxCommandable)
            selectButton?.Focus();
    }

    private void OnSelectClicked()
    {
        activateAction?.Invoke();
    }

    private bool AbilityAvailable => hasAbility && abilityCooldownRoundsRemaining == 0;

    private void SetHealth(float currentHealth, float maxHealth)
    {
        float safeMaxHealth = Mathf.Max(0f, maxHealth);
        float safeCurrentHealth =
            safeMaxHealth > 0f
                ? Mathf.Clamp(currentHealth, 0f, safeMaxHealth)
                : Mathf.Max(0f, currentHealth);
        float healthRatio = safeMaxHealth > 0f ? safeCurrentHealth / safeMaxHealth : 0f;

        if (healthFill != null)
        {
            healthFill.style.width = Length.Percent(healthRatio * 100f);

            // The bar used to be danger red at every level on both teams, so its colour said
            // nothing the width had not already said. Stepping it means a glance reads condition
            // rather than only quantity, and the thresholds live in TeamPalette so the world-space
            // bar over the unit cannot disagree with the card.
            string step = TeamPalette.HealthClassSuffix(healthRatio);
            healthFill.EnableInClassList("unit-card__health-fill--high", step == "high");
            healthFill.EnableInClassList("unit-card__health-fill--warning", step == "warning");
            healthFill.EnableInClassList("unit-card__health-fill--critical", step == "critical");
        }
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
        if (selectButton != null && !sandboxCommandable)
        {
            selectButton.tooltip =
                enemyConfigured && !enemyAlive
                    ? "Enemy unit eliminated."
                    : "Live enemy health and ability status.";
        }
    }

    private void RefreshAbilityState()
    {
        string remainingRoundText =
            abilityCooldownRoundsRemaining == 1
                ? "1 round"
                : $"{abilityCooldownRoundsRemaining} rounds";

        if (abilityName != null)
        {
            abilityName.text =
                !hasAbility ? "Move only"
                : !enemyCard ? configuredAbilityName
                : abilityCooldownRoundsRemaining == 0
                    ? $"{configuredAbilityName} · ready"
                    : $"{configuredAbilityName} · ready in {remainingRoundText}";
        }

        bool coolingDown = hasAbility && abilityCooldownRoundsRemaining > 0;
        if (flipIndicator != null)
        {
            flipIndicator.EnableInClassList(
                "hidden",
                (enemyCard && !sandboxCommandable) || !hasAbility
            );
            flipIndicator.EnableInClassList(
                "unit-card__flip-indicator--cooldown",
                coolingDown
            );
        }
        if (cooldownLabel != null)
        {
            cooldownLabel.text = coolingDown
                ? abilityCooldownRoundsRemaining.ToString()
                : string.Empty;
            cooldownLabel.EnableInClassList("hidden", !coolingDown);
        }

        cardRoot.EnableInClassList("unit-card--ability-cooldown", coolingDown);
        if (!AbilityAvailable)
            isAbilityMode = false;
        ApplyPlanningVisualState();
        SetInteractable(interactionRequested);
    }

    private void ApplyPlanningVisualState()
    {
        if (enemyCard && !sandboxCommandable)
            return;

        cardRoot.EnableInClassList("unit-card--selected", isSelected);
        cardRoot.EnableInClassList("unit-card--ability", isAbilityMode);
        UpdateSelectTooltip();
    }

    private void UpdateSelectTooltip()
    {
        if ((enemyCard && !sandboxCommandable) || selectButton == null)
            return;

        if (!hasAbility)
        {
            selectButton.tooltip = "This unit has no ability. Press to select it.";
            return;
        }

        if (abilityCooldownRoundsRemaining > 0)
        {
            selectButton.tooltip =
                abilityCooldownRoundsRemaining == 1
                    ? $"{configuredAbilityName} recharges after 1 more completed round."
                    : $"{configuredAbilityName} recharges after {abilityCooldownRoundsRemaining} more completed rounds.";
            return;
        }

        // Whether the card happens to be the selected one is not what the press does, so it is not
        // what the tooltip reports. Every state here names the order the press will give.
        selectButton.tooltip = isAbilityMode
            ? $"{configuredAbilityName} ordered. Press again to move instead."
            : $"Order {configuredAbilityName} instead of moving.";
    }

    /// <summary>
    /// Hands an enemy card to the sandbox designer: it starts answering presses and showing which
    /// order it holds, exactly as a card on their own dock does.
    /// </summary>
    public void EnableSandboxCommand(Action onActivate)
    {
        if (!enemyCard || sandboxCommandable)
            return;

        sandboxCommandable = true;
        activateAction = onActivate;
        if (selectButton != null)
        {
            selectButton.focusable = true;
            selectButton.pickingMode = PickingMode.Position;
            selectButton.clicked += OnSelectClicked;
        }
        RefreshAbilityState();
    }

    /// <summary>
    /// Makes the card's portrait the way into the sandbox's crew composing: press the character to
    /// change it or take it off the board. Pass null to take the affordance away.
    /// <para>
    /// The portrait is the natural handle — it is the character, and it is what a designer points at
    /// when they mean "this one" — but it lives inside the card's own press, which orders the
    /// ability. So the press is claimed here and stopped before it reaches the button: composing the
    /// crew and ordering its round never share a click.
    /// </para>
    /// </summary>
    public void SetSandboxSlotAction(Action onOpen)
    {
        sandboxSlotAction = onOpen;
        if (portrait == null)
            return;

        bool editable = onOpen != null;
        portrait.EnableInClassList("unit-card__portrait--editable", editable);
        portrait.pickingMode = editable ? PickingMode.Position : PickingMode.Ignore;
        portrait.tooltip = editable ? "Change or remove this unit" : string.Empty;
        if (sandboxPortraitBound || !editable)
            return;

        sandboxPortraitBound = true;
        portrait.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
        portrait.RegisterCallback<PointerUpEvent>(evt =>
        {
            evt.StopPropagation();
            sandboxSlotAction?.Invoke();
        });
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        if (selectButton != null)
            selectButton.clicked -= OnSelectClicked;
        activateAction = null;
        sandboxSlotAction = null;
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

        Button select = new() { name = "unit-card-select" };
        select.AddToClassList("unit-card__select");
        VisualElement abilityVignette = new() { name = "unit-card-ability-vignette" };
        abilityVignette.AddToClassList("unit-card__ability-vignette");
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
        VisualElement flipIndicator = new() { name = "unit-card-flip-indicator" };
        flipIndicator.AddToClassList("unit-card__flip-indicator");
        VisualElement flipIcon = new() { name = "unit-card-flip-icon" };
        flipIcon.AddToClassList("unit-card__flip-icon");
        Label cooldown = new() { name = "unit-card-cooldown" };
        cooldown.AddToClassList("unit-card__cooldown");
        cooldown.AddToClassList("hidden");
        flipIndicator.Add(flipIcon);
        flipIndicator.Add(cooldown);
        select.Add(abilityVignette);
        select.Add(portrait);
        select.Add(copy);
        select.Add(flipIndicator);

        root.Add(select);
        return root;
    }
}
