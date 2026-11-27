using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class UnitCardElement : IDisposable
{
    private const float ShakeSeconds = 0.36f;
    private const float ShakeFrequency = 21f;
    private const float FlipSeconds = 1.15f;
    private const float FlipCrouchEnd = 0.12f;
    private const float FlipTurnEnd = 0.7f;
    private const float FlipLandStart = 0.8f;
    private const float LossHoldSeconds = 0.32f;
    private const float LossDrainPerSecond = 1.6f;

    private readonly VisualElement container;
    private readonly VisualElement cardRoot;
    private readonly bool enemyCard;
    private readonly Button selectButton;
    private readonly VisualElement portrait;
    private readonly Label unitName;
    private readonly VisualElement healthFill;
    private readonly VisualElement healthLoss;
    private readonly Label healthValue;
    private readonly Label abilityName;
    private readonly Label stateLabel;
    private readonly VisualElement flipIndicator;
    private readonly Label cooldownLabel;
    private readonly VisualElement shadow;
    private readonly VisualElement back;
    private readonly Label backName;
    private readonly VisualElement shade;
    private readonly float tiltDirection;

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

    private bool liveKnown;
    private float liveHealth;
    private float liveMaxHealth;
    private bool liveAlive = true;
    private bool replayShown;

    private bool shownKnown;
    private float shownFraction = 1f;
    private bool shownAlive = true;
    private bool faceDown;
    private float lossFraction = 1f;
    private float lossHoldUntil;
    private float shakeStartedAt = float.NegativeInfinity;
    private float shakeAmplitude;
    private float flipStartedAt = float.NegativeInfinity;
    private bool flipToBack;
    private IVisualElementScheduledItem animator;

    private bool sandboxCommandable;
    private Action sandboxSlotAction;
    private bool sandboxPortraitBound;

    public VisualElement Root => container;
    public bool IsEnemyConfigured => enemyCard && enemyConfigured;
    public bool IsEnemyAlive => IsEnemyConfigured && enemyAlive;
    public bool IsFaceDown => faceDown;
    public bool IsAnimating => ShakeActive || FlipActive || lossFraction > shownFraction;

    private static float Now => Time.unscaledTime;
    private bool ShakeActive => Now - shakeStartedAt < ShakeSeconds;
    private bool FlipActive => Now - flipStartedAt < FlipSeconds;

    public UnitCardElement(VisualElement host, int index, bool isEnemyCard = false)
    {
        container = host ?? new VisualElement();
        enemyCard = isEnemyCard;
        tiltDirection = index % 2 == 0 ? 1f : -1f;
        cardRoot =
            container.Q<VisualElement>("unit-card-root")
            ?? (container.ClassListContains("unit-card") ? container : BuildFallbackCard(container));
        if (cardRoot.parent == null && cardRoot != container)
            container.Add(cardRoot);

        string namePrefix = enemyCard ? "enemy-unit-card" : "unit-card";
        cardRoot.name = $"{namePrefix}-{index}-root";
        selectButton = cardRoot.Q<Button>("unit-card-select");
        portrait = cardRoot.Q<VisualElement>("unit-card-portrait");
        unitName = cardRoot.Q<Label>("unit-card-name");
        healthFill = cardRoot.Q<VisualElement>("unit-card-health-fill");
        healthLoss = cardRoot.Q<VisualElement>("unit-card-health-loss");
        healthValue = cardRoot.Q<Label>("unit-card-health-value");
        abilityName = cardRoot.Q<Label>("unit-card-ability");
        stateLabel = cardRoot.Q<Label>("unit-card-state");
        flipIndicator = cardRoot.Q<VisualElement>("unit-card-flip-indicator");
        cooldownLabel = cardRoot.Q<Label>("unit-card-cooldown");
        back = cardRoot.Q<VisualElement>("unit-card-back");
        backName = cardRoot.Q<Label>("unit-card-back-name");
        shade = cardRoot.Q<VisualElement>("unit-card-shade");
        shadow = container.Q<VisualElement>("unit-card-shadow");

        if (
            selectButton == null
            || portrait == null
            || unitName == null
            || healthFill == null
            || healthLoss == null
            || healthValue == null
            || abilityName == null
            || stateLabel == null
            || cardRoot.Q<VisualElement>("unit-card-ability-vignette") == null
            || flipIndicator == null
            || cardRoot.Q<VisualElement>("unit-card-flip-icon") == null
            || cooldownLabel == null
            || back == null
            || backName == null
            || shade == null
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

        cardRoot.AddToClassList(enemyCard ? "unit-card--enemy" : "unit-card--friendly");

        if (enemyCard)
        {
            SetName("Enemy");
            if (stateLabel != null)
                stateLabel.text = "LINKING";
        }
        PresentHealth(0f, 0f, true, false);
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
        ApplyIdentity(data, abilityPresent, cooldownRoundsRemaining);
        isDisabled = false;
        if (stateLabel != null)
            stateLabel.text = string.Empty;

        ApplyDisabledClass();
        if (!liveKnown && data != null)
            SetHealth(data.maxHealth, data.maxHealth, true, false);
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
        bool alive,
        bool animate = false
    )
    {
        if (!enemyCard)
            return;

        enemyConfigured = true;
        enemyAlive = alive;
        ApplyIdentity(data, abilityPresent, cooldownRoundsRemaining);
        SetHealth(currentHealth, maxHealth, alive, animate);
        RefreshAbilityState();
        RefreshEnemyState();
    }

    /// <summary>The unit's live condition, as the server reports it.</summary>
    public void SetHealth(float currentHealth, float maxHealth, bool alive, bool animate)
    {
        liveKnown = true;
        liveHealth = currentHealth;
        liveMaxHealth = maxHealth;
        liveAlive = alive;
        if (!replayShown)
            PresentHealth(currentHealth, maxHealth, alive, animate);
    }

    /// <summary>Shows a recorded moment over the live condition until <see cref="ClearReplayHealth"/>.</summary>
    public void ShowReplayHealth(float currentHealth, float maxHealth, bool alive, bool animate)
    {
        replayShown = true;
        ApplyDisabledClass();
        PresentHealth(currentHealth, maxHealth, alive, animate);
    }

    public void ClearReplayHealth()
    {
        if (!replayShown)
            return;
        replayShown = false;
        ApplyDisabledClass();
        PresentHealth(liveHealth, liveMaxHealth, liveAlive || !liveKnown, false);
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
        ApplyDisabledClass();
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

    private void ApplyIdentity(UnitData data, bool abilityPresent, int cooldownRoundsRemaining)
    {
        hasAbility = abilityPresent;
        abilityCooldownRoundsRemaining = Mathf.Max(0, cooldownRoundsRemaining);
        configuredAbilityName =
            data != null && !string.IsNullOrWhiteSpace(data.abilityName)
                ? data.abilityName
                : "Ability";
        SetName(data != null ? data.unitName : enemyCard ? "Enemy" : "Unit");

        Sprite cardSprite =
            data != null
                ? (data.abilitySprite != null ? data.abilitySprite : data.unitSprite)
                : null;
        SetBackgroundImage(portrait, cardSprite);
    }

    private void ApplyDisabledClass() =>
        cardRoot.EnableInClassList("unit-card--disabled", isDisabled && !replayShown);

    private void SetName(string text)
    {
        if (unitName != null)
            unitName.text = text;
        if (backName != null)
            backName.text = text;
    }

    private void PresentHealth(float currentHealth, float maxHealth, bool alive, bool animate)
    {
        float safeMaxHealth = Mathf.Max(0f, maxHealth);
        float safeCurrentHealth =
            safeMaxHealth > 0f
                ? Mathf.Clamp(currentHealth, 0f, safeMaxHealth)
                : Mathf.Max(0f, currentHealth);
        float fraction = safeMaxHealth > 0f ? safeCurrentHealth / safeMaxHealth : 0f;
        bool known = safeMaxHealth > 0f;

        bool tookDamage = animate && shownKnown && known && fraction < shownFraction - 0.0001f;
        if (tookDamage)
        {
            if (!ShakeActive)
                lossFraction = Mathf.Max(lossFraction, shownFraction);
            lossHoldUntil = Now + LossHoldSeconds;
            shakeAmplitude = Mathf.Lerp(2.5f, 6f, Mathf.Clamp01((shownFraction - fraction) * 2.5f));
            shakeStartedAt = Now;
        }
        else
        {
            lossFraction = fraction;
        }

        shownKnown = known;
        shownFraction = fraction;
        if (healthFill != null)
        {
            healthFill.style.width = Length.Percent(fraction * 100f);
            string step = TeamPalette.HealthClassSuffix(fraction);
            healthFill.EnableInClassList("unit-card__health-fill--high", step == "high");
            healthFill.EnableInClassList("unit-card__health-fill--warning", step == "warning");
            healthFill.EnableInClassList("unit-card__health-fill--critical", step == "critical");
        }
        if (healthValue != null)
        {
            healthValue.text = known
                ? $"{Mathf.RoundToInt(safeCurrentHealth)} / {Mathf.RoundToInt(safeMaxHealth)} HP"
                : "HP UNKNOWN";
        }

        if (alive != shownAlive)
        {
            shownAlive = alive;
            if (animate)
            {
                flipToBack = !alive;
                flipStartedAt = Now;
            }
            else
            {
                flipStartedAt = float.NegativeInfinity;
                SetFaceDown(!alive);
            }
        }
        else if (!animate && !FlipActive)
        {
            SetFaceDown(!alive);
        }

        if (IsAnimating)
            StartAnimating();
        else
            ApplyMotion();
    }

    private void SetFaceDown(bool down)
    {
        faceDown = down;
        cardRoot.EnableInClassList("unit-card--down", down);
        back?.EnableInClassList("hidden", !down);
        if (selectButton != null)
            selectButton.style.visibility = down
                ? new StyleEnum<Visibility>(Visibility.Hidden)
                : new StyleEnum<Visibility>(StyleKeyword.Null);
    }

    private void StartAnimating()
    {
        animator ??= cardRoot.schedule.Execute(ApplyMotion).Every(0);
        animator.Resume();
        ApplyMotion();
    }

    private void ApplyMotion()
    {
        float now = Now;
        float x = 0f;
        float y = 0f;
        float scaleX = 1f;
        float scaleY = 1f;
        float tilt = 0f;
        float shadeOpacity = 0f;
        float shadowOpacity = 1f;

        if (ShakeActive)
        {
            float t = (now - shakeStartedAt) / ShakeSeconds;
            float decay = (1f - t) * (1f - t);
            float wave = Mathf.Sin(t * ShakeSeconds * ShakeFrequency * Mathf.PI * 2f);
            x += shakeAmplitude * decay * wave;
            tilt += 0.9f * decay * wave * tiltDirection;
        }

        if (FlipActive)
        {
            float t = (now - flipStartedAt) / FlipSeconds;
            float turn = Smooth(Mathf.InverseLerp(FlipCrouchEnd, FlipTurnEnd, t));
            float angle = turn * Mathf.PI;
            float edge = Mathf.Abs(Mathf.Cos(angle));
            SetFaceDown(flipToBack ? angle > Mathf.PI * 0.5f : angle <= Mathf.PI * 0.5f);

            float air = t < FlipCrouchEnd
                ? -0.25f * Mathf.Sin(Mathf.PI * t / FlipCrouchEnd)
                : t < FlipLandStart
                    ? Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.InverseLerp(FlipCrouchEnd, FlipLandStart, t), 0.8f))
                    : 0f;
            float land = Mathf.InverseLerp(FlipLandStart, 1f, t);
            float squash = t >= FlipLandStart ? Mathf.Sin(Mathf.PI * land) * (1f - land) : 0f;
            float lift = 14f * LiftScale();
            float grow = 1f + 0.12f * Mathf.Max(0f, air);

            y -= lift * air;
            y += 2f * squash;
            scaleX = Mathf.Max(0.02f, edge) * grow * (1f + 0.05f * squash);
            scaleY = grow * (1f - 0.08f * squash);
            tilt += 7f * Mathf.Sin(angle) * tiltDirection;
            x += 3f * squash * Mathf.Sin(land * Mathf.PI * 6f);
            shadeOpacity = (1f - edge) * 0.55f;
            shadowOpacity = 1f - 0.65f * Mathf.Clamp01(air);
        }

        if (lossFraction > shownFraction && now >= lossHoldUntil)
            lossFraction = Mathf.Max(shownFraction, lossFraction - LossDrainPerSecond * Time.unscaledDeltaTime);
        if (healthLoss != null)
            healthLoss.style.width = Length.Percent(Mathf.Max(lossFraction, shownFraction) * 100f);

        bool moving = ShakeActive || FlipActive;
        cardRoot.style.translate = moving ? new StyleTranslate(new Translate(x, y)) : new StyleTranslate(StyleKeyword.Null);
        cardRoot.style.scale = moving ? new StyleScale(new Scale(new Vector2(scaleX, scaleY))) : new StyleScale(StyleKeyword.Null);
        cardRoot.style.rotate = moving
            ? new StyleRotate(new Rotate(new Angle(tilt, AngleUnit.Degree)))
            : new StyleRotate(StyleKeyword.Null);
        if (shade != null)
            shade.style.opacity = shadeOpacity;
        if (shadow != null)
        {
            shadow.style.opacity = moving ? new StyleFloat(shadowOpacity) : new StyleFloat(StyleKeyword.Null);
            shadow.style.scale = moving ? new StyleScale(new Scale(new Vector2(scaleX, 1f))) : new StyleScale(StyleKeyword.Null);
        }

        if (!FlipActive)
            SetFaceDown(!shownAlive);
        if (!IsAnimating)
            animator?.Pause();
    }

    private float LiftScale()
    {
        for (VisualElement node = container; node != null; node = node.parent)
        {
            if (node.ClassListContains("phone"))
                return 0.35f;
        }
        return 1f;
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private void RefreshEnemyState()
    {
        if (!enemyCard)
            return;

        cardRoot.EnableInClassList("unit-card--enemy-active", enemyConfigured && enemyAlive);
        cardRoot.EnableInClassList("unit-card--enemy-eliminated", enemyConfigured && !enemyAlive);
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
        if (abilityName != null)
            abilityName.text = hasAbility ? configuredAbilityName : "Move only";

        bool coolingDown = hasAbility && abilityCooldownRoundsRemaining > 0;
        if (flipIndicator != null)
        {
            flipIndicator.EnableInClassList("hidden", !hasAbility);
            flipIndicator.EnableInClassList("unit-card__flip-indicator--cooldown", coolingDown);
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
    /// The portrait lives inside the card's own press, which orders the ability, so the press is
    /// claimed here and stopped before it reaches the button. A face-down card answers on its back,
    /// so a fallen unit can still be swapped out.
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
        if (back != null)
        {
            back.pickingMode = editable ? PickingMode.Position : PickingMode.Ignore;
            back.tooltip = portrait.tooltip;
        }
        if (sandboxPortraitBound || !editable)
            return;

        sandboxPortraitBound = true;
        foreach (VisualElement handle in new[] { portrait, back })
        {
            if (handle == null)
                continue;
            handle.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                evt.StopPropagation();
                sandboxSlotAction?.Invoke();
            });
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        if (selectButton != null)
            selectButton.clicked -= OnSelectClicked;
        animator?.Pause();
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

    private static VisualElement Part(string className, string name = null)
    {
        VisualElement element = new() { name = name, pickingMode = PickingMode.Ignore };
        element.AddToClassList(className);
        return element;
    }

    private static Label Text(string className, string name, string text = "")
    {
        Label label = new(text) { name = name, pickingMode = PickingMode.Ignore };
        label.AddToClassList(className);
        return label;
    }

    private static VisualElement BuildFallbackCard(VisualElement host)
    {
        host.Add(Part("unit-card-shadow", "unit-card-shadow"));

        VisualElement root = new() { name = "unit-card-root" };
        root.AddToClassList("unit-card");

        Button select = new() { name = "unit-card-select" };
        select.AddToClassList("unit-card__select");
        select.Add(Part("unit-card__ability-vignette", "unit-card-ability-vignette"));
        select.Add(Part("unit-card__portrait", "unit-card-portrait"));

        VisualElement copy = Part("unit-card__copy");
        copy.Add(Text("unit-card__name", "unit-card-name", "Unit"));
        copy.Add(Text("unit-card__ability", "unit-card-ability", "Move only"));
        copy.Add(Text("unit-card__state", "unit-card-state"));
        select.Add(copy);

        VisualElement flipIndicator = Part("unit-card__flip-indicator", "unit-card-flip-indicator");
        flipIndicator.Add(Part("unit-card__flip-icon", "unit-card-flip-icon"));
        Label cooldown = Text("unit-card__cooldown", "unit-card-cooldown");
        cooldown.AddToClassList("hidden");
        flipIndicator.Add(cooldown);
        select.Add(flipIndicator);

        VisualElement health = Part("unit-card__health", "unit-card-health");
        VisualElement track = Part("unit-card__health-track");
        track.Add(Part("unit-card__health-loss", "unit-card-health-loss"));
        track.Add(Part("unit-card__health-fill", "unit-card-health-fill"));
        health.Add(track);
        health.Add(Text("unit-card__health-value", "unit-card-health-value", "HP UNKNOWN"));
        select.Add(health);
        root.Add(select);

        VisualElement back = Part("unit-card__back", "unit-card-back");
        back.AddToClassList("hidden");
        back.Add(Part("unit-card__back-hatch"));
        back.Add(Text("unit-card__back-name", "unit-card-back-name"));
        root.Add(back);
        root.Add(Part("unit-card__shade", "unit-card-shade"));
        return root;
    }
}
