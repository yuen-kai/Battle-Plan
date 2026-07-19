using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class UnitCardElement : IDisposable
{
    private readonly VisualElement container;
    private readonly VisualElement cardRoot;
    private readonly Button selectButton;
    private readonly VisualElement portrait;
    private readonly Label unitName;
    private readonly Label abilityName;
    private readonly Label stateLabel;
    private readonly Button moveButton;
    private readonly Button abilityButton;

    private Action selectAction;
    private Action moveAction;
    private Action abilityAction;
    private bool hasAbility;
    private int remainingAbilityUses;
    private string configuredAbilityName = "Ability";
    private bool isDisabled;
    private bool interactionRequested;
    private bool disposed;

    public VisualElement Root => container;

    public UnitCardElement(VisualElement host, int index)
    {
        container = host ?? new VisualElement();
        cardRoot =
            container.Q<VisualElement>("unit-card-root")
            ?? (container.ClassListContains("unit-card") ? container : BuildFallbackCard());
        if (cardRoot.parent == null && cardRoot != container)
            container.Add(cardRoot);

        cardRoot.name = $"unit-card-{index}-root";
        selectButton = cardRoot.Q<Button>("unit-card-select");
        portrait = cardRoot.Q<VisualElement>("unit-card-portrait");
        unitName = cardRoot.Q<Label>("unit-card-name");
        abilityName = cardRoot.Q<Label>("unit-card-ability");
        stateLabel = cardRoot.Q<Label>("unit-card-state");
        moveButton = cardRoot.Q<Button>("unit-card-move");
        abilityButton = cardRoot.Q<Button>("unit-card-ability-button");

        if (
            selectButton == null
            || portrait == null
            || unitName == null
            || abilityName == null
            || stateLabel == null
            || moveButton == null
            || abilityButton == null
        )
        {
            Debug.LogError(
                $"[UnitCardElement] Unit card {index} is missing one or more required elements."
            );
        }

        if (selectButton != null)
        {
            selectButton.name = $"unit-card-select-{index}";
            selectButton.clicked += OnSelectClicked;
        }
        if (moveButton != null)
        {
            moveButton.name = $"unit-card-move-{index}";
            moveButton.clicked += OnMoveClicked;
        }
        if (abilityButton != null)
        {
            abilityButton.name = $"unit-card-ability-{index}";
            abilityButton.clicked += OnAbilityClicked;
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

    public void SetInteractable(bool interactable)
    {
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

    private void RefreshAbilityState()
    {
        if (abilityName != null)
        {
            abilityName.text =
                !hasAbility ? "Move only"
                : remainingAbilityUses > 0 ? $"{configuredAbilityName} · {remainingAbilityUses} use"
                : $"{configuredAbilityName} · spent";
        }

        if (abilityButton != null)
        {
            abilityButton.text =
                !hasAbility ? "N/A"
                : remainingAbilityUses > 0 ? $"ABILITY · {remainingAbilityUses}"
                : "SPENT";
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
        Label ability = new("Move only") { name = "unit-card-ability" };
        ability.AddToClassList("unit-card__ability");
        Label state = new() { name = "unit-card-state" };
        state.AddToClassList("unit-card__state");
        copy.Add(name);
        copy.Add(ability);
        copy.Add(state);
        select.Add(portrait);
        select.Add(copy);

        VisualElement modes = new();
        modes.AddToClassList("unit-card__modes");
        Button move = new() { name = "unit-card-move", text = "MOVE" };
        move.AddToClassList("unit-card__mode");
        move.AddToClassList("unit-card__mode--move");
        Button abilityButton = new() { name = "unit-card-ability-button", text = "ABILITY" };
        abilityButton.AddToClassList("unit-card__mode");
        modes.Add(move);
        modes.Add(abilityButton);

        root.Add(stateMark);
        root.Add(select);
        root.Add(modes);
        return root;
    }
}
