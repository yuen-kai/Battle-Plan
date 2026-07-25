using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Gives the whole interface a voice from one place.
///
/// <para>Every screen in Battle Plan is a UI Toolkit panel, so instead of a click handler per
/// button this binds a small set of callbacks to each panel's root and reads the element the event
/// landed on. Buttons created at runtime — unit cards, roster options, selected slots — are covered
/// automatically because the events reach the root either way.</para>
///
/// <para>Panels are discovered on scene load, so no controller, prefab or scene needs to change.
/// A few elements are deliberately silent here because a gameplay hook already speaks for them
/// (the lock-in button, unit cards); see <see cref="SilentElementNames"/>.</para>
/// </summary>
public static class BattlePlanUIAudio
{
    /// <summary>Controls whose sound is owned by a gameplay hook instead of the generic press.</summary>
    private static readonly HashSet<string> SilentElementNames = new()
    {
        "lock-in-button",
    };

    private static readonly HashSet<string> SilentClassNames = new()
    {
        "unit-card__select",
    };

    /// <summary>
    /// Press cues keyed by the element names the screens already guarantee (the same names the
    /// UI smoke tests lock down), so a button's sound follows its meaning rather than its skin.
    /// </summary>
    private static readonly Dictionary<string, AudioCueId> PressCueByName = new()
    {
        // Opens a modal
        { "settings-button", AudioCueId.UiDialogOpen },
        { "controls-button", AudioCueId.UiDialogOpen },
        { "credits-button", AudioCueId.UiDialogOpen },
        // Closes a modal
        { "settings-close-button", AudioCueId.UiDialogClose },
        { "controls-close-button", AudioCueId.UiDialogClose },
        { "credits-close-button", AudioCueId.UiDialogClose },
        // Commits
        { "play-button", AudioCueId.UiPressPrimary },
        { "create-match-button", AudioCueId.UiPressPrimary },
        { "join-match-button", AudioCueId.UiPressPrimary },
        { "confirm-selection-button", AudioCueId.RosterConfirm },
        { "play-again-button", AudioCueId.UiPressPrimary },
        // Switches a mode or a panel
        { "show-create-button", AudioCueId.UiTabChange },
        { "show-join-button", AudioCueId.UiTabChange },
        { "elimination-button", AudioCueId.UiTabChange },
        { "king-button", AudioCueId.UiTabChange },
        { "flag-button", AudioCueId.UiTabChange },
        { "player-opponent-button", AudioCueId.UiTabChange },
        { "ai-opponent-button", AudioCueId.UiTabChange },
        // Backs out
        { "cancel-host-button", AudioCueId.UiDialogClose },
        { "cancel-join-button", AudioCueId.UiDialogClose },
        { "title-button", AudioCueId.UiDialogClose },
        { "main-menu-button", AudioCueId.UiDialogClose },
        // Crew selection
        { "unit-option-button", AudioCueId.RosterPick },
        { "selected-slot-button", AudioCueId.RosterRemove },
    };

    private static readonly ConditionalWeakTable<VisualElement, object> boundRoots = new();

    /// <summary>Finds every live UI Toolkit panel and gives it a voice. Idempotent.</summary>
    public static void BindLoadedPanels()
    {
        foreach (
            UIDocument document in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)
        )
        {
            Bind(document != null ? document.rootVisualElement : null);
        }
    }

    /// <summary>Binds one panel root. Safe to call repeatedly with the same root.</summary>
    public static void Bind(VisualElement root)
    {
        if (root == null || boundRoots.TryGetValue(root, out _))
            return;

        boundRoots.Add(root, null);

        root.RegisterCallback<PointerEnterEvent>(OnPointerEnter, TrickleDown.TrickleDown);
        root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        root.RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit, TrickleDown.TrickleDown);
        root.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
        root.RegisterCallback<ChangeEvent<bool>>(OnToggleChanged);
        root.RegisterCallback<ChangeEvent<string>>(OnStringChanged);
        root.RegisterCallback<DetachFromPanelEvent>(OnRootDetached);
    }

    private static void OnRootDetached(DetachFromPanelEvent evt)
    {
        if (evt.target is VisualElement root)
            boundRoots.Remove(root);
    }

    private static void OnPointerEnter(PointerEnterEvent evt)
    {
        VisualElement control = ResolveControl(evt.target as VisualElement);
        if (control == null || !control.enabledInHierarchy || IsSilent(control))
            return;

        BattlePlanAudio.Play(AudioCueId.UiHover);
    }

    private static void OnPointerDown(PointerDownEvent evt)
    {
        PlayPress(ResolveControl(evt.target as VisualElement));
    }

    private static void OnNavigationSubmit(NavigationSubmitEvent evt)
    {
        PlayPress(ResolveControl(evt.target as VisualElement));
    }

    private static void OnNavigationMove(NavigationMoveEvent evt)
    {
        BattlePlanAudio.Play(AudioCueId.UiNavigate);
    }

    private static void OnToggleChanged(ChangeEvent<bool> evt)
    {
        if (evt.target is not VisualElement element || IsSilent(element))
            return;

        BattlePlanAudio.Play(evt.newValue ? AudioCueId.UiToggleOn : AudioCueId.UiToggleOff);
    }

    private static void OnStringChanged(ChangeEvent<string> evt)
    {
        // Dropdown selections only. Text fields fire this on every keystroke and must stay silent.
        if (evt.target is DropdownField)
            BattlePlanAudio.Play(AudioCueId.UiConfirm);
    }

    private static void PlayPress(VisualElement control)
    {
        if (control == null || IsSilent(control))
            return;

        if (!control.enabledInHierarchy)
        {
            BattlePlanAudio.Play(AudioCueId.UiPressDisabled);
            return;
        }

        if (!string.IsNullOrEmpty(control.name) && PressCueByName.TryGetValue(control.name, out AudioCueId named))
        {
            BattlePlanAudio.Play(named);
            return;
        }

        BattlePlanAudio.Play(
            control.ClassListContains("button--primary")
                ? AudioCueId.UiPressPrimary
                : AudioCueId.UiPress
        );
    }

    /// <summary>
    /// Walks up from whatever the event landed on to the interactive control that owns it, so a
    /// label inside a button still reads as that button.
    /// </summary>
    private static VisualElement ResolveControl(VisualElement target)
    {
        for (VisualElement element = target; element != null; element = element.parent)
        {
            if (element is Button or Toggle or DropdownField)
                return element;
            if (element.ClassListContains("button") || element.ClassListContains("unit-option"))
                return element;
        }
        return null;
    }

    private static bool IsSilent(VisualElement element)
    {
        for (VisualElement current = element; current != null; current = current.parent)
        {
            if (!string.IsNullOrEmpty(current.name) && SilentElementNames.Contains(current.name))
                return true;

            foreach (string silentClass in SilentClassNames)
            {
                if (current.ClassListContains(silentClass))
                    return true;
            }

            if (current is Button)
                break;
        }
        return false;
    }
}
