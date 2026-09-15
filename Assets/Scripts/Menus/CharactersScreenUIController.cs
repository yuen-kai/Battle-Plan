using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Read-only gallery of the whole roster: a class rail on the left, the roster itself on a
/// turntable in the middle, and a dossier on the right that follows whichever unit is facing the
/// camera. Nothing here picks a crew, so the screen stays off the network entirely.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class CharactersScreenUIController : MonoBehaviour
{
    /// <summary>One notch of the wheel is worth one unit.</summary>
    private const float WheelStepThreshold = 0.01f;

    [SerializeField]
    private UnitDatabase allUnits;

    [SerializeField]
    private CharacterCarousel carousel;

    private readonly List<UnitData> orderedUnits = new();
    private readonly List<UnitData> visibleUnits = new();
    private readonly List<ClassFilterView> filterViews = new();
    private readonly List<VisualElement> dots = new();

    private UIDocument document;
    private VisualElement root;
    private VisualElement stage;
    private VisualElement classFilters;
    private VisualElement dotStrip;
    private Button titleButton;
    private Button prevButton;
    private Button nextButton;
    private Label rosterCount;
    private Label unitClassLabel;
    private Label unitNameLabel;
    private Label abilityNameLabel;
    private Label abilityCooldownLabel;
    private Label abilityDetailLabel;

    private UnitClass? activeFilter;
    private bool callbacksRegistered;
    private bool draggingStage;
    private int dragPointerId;
    private float lastDragX;
    private float lastDragTime;
    private float dragVelocity;

    private void OnEnable()
    {
        document = GetComponent<UIDocument>();
        root = document != null ? document.rootVisualElement : null;
        if (root == null)
        {
            Debug.LogError("[CharactersScreenUIController] UIDocument has no visual tree.");
            return;
        }

        CacheElements();
        BuildCatalog();
        BuildClassFilters();
        RegisterCallbacks();
        ApplyFilter(null);
        ConsoleUiNavigation.ConfigureButtons(root);
        MobileDisplay.ConfigureScreen(document);
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
        MobileDisplay.ForgetScreen(document);
        DisposeGeneratedViews();
    }

    private void CacheElements()
    {
        stage = RequireElement<VisualElement>("carousel-stage");
        titleButton = RequireElement<Button>("title-button");
        prevButton = RequireElement<Button>("carousel-prev-button");
        nextButton = RequireElement<Button>("carousel-next-button");
        classFilters = RequireElement<VisualElement>("class-filters");
        dotStrip = RequireElement<VisualElement>("carousel-dots");
        rosterCount = RequireElement<Label>("roster-count");
        unitClassLabel = RequireElement<Label>("unit-class");
        unitNameLabel = RequireElement<Label>("unit-name");
        abilityNameLabel = RequireElement<Label>("ability-name");
        abilityCooldownLabel = RequireElement<Label>("ability-cooldown");
        abilityDetailLabel = RequireElement<Label>("ability-detail");
    }

    private T RequireElement<T>(string elementName)
        where T : VisualElement
    {
        T element = root.Q<T>(elementName);
        if (element == null)
        {
            Debug.LogError(
                $"[CharactersScreenUIController] Missing required {typeof(T).Name} '{elementName}'."
            );
        }
        return element;
    }

    private void RegisterCallbacks()
    {
        if (callbacksRegistered)
            return;

        if (titleButton != null)
            titleButton.clicked += ReturnToTitle;
        if (prevButton != null)
            prevButton.clicked += SelectPrevious;
        if (nextButton != null)
            nextButton.clicked += SelectNext;
        if (carousel != null)
            carousel.FocusChanged += OnFocusChanged;

        if (stage != null)
        {
            stage.RegisterCallback<PointerDownEvent>(OnStagePointerDown);
            stage.RegisterCallback<PointerMoveEvent>(OnStagePointerMove);
            stage.RegisterCallback<PointerUpEvent>(OnStagePointerUp);
            stage.RegisterCallback<PointerCaptureOutEvent>(OnStagePointerCaptureOut);
            stage.RegisterCallback<WheelEvent>(OnStageWheel);
        }

        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        callbacksRegistered = true;
    }

    private void UnregisterCallbacks()
    {
        if (!callbacksRegistered)
            return;

        if (titleButton != null)
            titleButton.clicked -= ReturnToTitle;
        if (prevButton != null)
            prevButton.clicked -= SelectPrevious;
        if (nextButton != null)
            nextButton.clicked -= SelectNext;
        if (carousel != null)
            carousel.FocusChanged -= OnFocusChanged;

        if (stage != null)
        {
            stage.UnregisterCallback<PointerDownEvent>(OnStagePointerDown);
            stage.UnregisterCallback<PointerMoveEvent>(OnStagePointerMove);
            stage.UnregisterCallback<PointerUpEvent>(OnStagePointerUp);
            stage.UnregisterCallback<PointerCaptureOutEvent>(OnStagePointerCaptureOut);
            stage.UnregisterCallback<WheelEvent>(OnStageWheel);
        }

        root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        callbacksRegistered = false;
    }

    private void BuildCatalog()
    {
        orderedUnits.Clear();

        if (allUnits?.units == null || allUnits.units.Count == 0)
        {
            Debug.LogError("[CharactersScreenUIController] No unit catalog is assigned.");
            return;
        }

        foreach (UnitData data in allUnits.units)
        {
            if (data != null)
                orderedUnits.Add(data);
        }

        // Grouping is the sort: class first in the enum's front-line-to-back-line order, then the
        // catalog order inside a class so two units of one class never swap seats between visits.
        List<UnitData> catalogOrder = new(orderedUnits);
        orderedUnits.Sort(
            (left, right) =>
            {
                int byClass = left.unitClass.CompareTo(right.unitClass);
                return byClass != 0
                    ? byClass
                    : catalogOrder.IndexOf(left).CompareTo(catalogOrder.IndexOf(right));
            }
        );
    }

    private void BuildClassFilters()
    {
        foreach (ClassFilterView view in filterViews)
            view.Dispose();
        filterViews.Clear();
        classFilters?.Clear();

        if (classFilters == null)
            return;

        AddClassFilter(null, "All units");
        foreach (UnitClass unitClass in (UnitClass[])Enum.GetValues(typeof(UnitClass)))
        {
            if (orderedUnits.Exists(data => data.unitClass == unitClass))
                AddClassFilter(unitClass, unitClass.ToString());
        }
    }

    private void AddClassFilter(UnitClass? unitClass, string label)
    {
        Button button = new()
        {
            text = label,
            name = unitClass == null ? "class-filter-all" : $"class-filter-{unitClass.Value}",
            tooltip = unitClass == null ? "Show every unit" : $"Show only {label} units",
        };
        button.AddToClassList("class-filter");

        ClassFilterView view = new(button, unitClass);
        view.ClickAction = () =>
        {
            AudioManager.Instance?.PlayButtonClick();
            ApplyFilter(unitClass);
        };
        button.clicked += view.ClickAction;

        filterViews.Add(view);
        classFilters.Add(button);
    }

    private void ApplyFilter(UnitClass? unitClass)
    {
        UnitData previousFocus = carousel != null ? carousel.FocusUnit : null;
        activeFilter = unitClass;

        visibleUnits.Clear();
        foreach (UnitData data in orderedUnits)
        {
            if (activeFilter == null || data.unitClass == activeFilter.Value)
                visibleUnits.Add(data);
        }

        foreach (ClassFilterView view in filterViews)
            view.SetActive(view.UnitClass == activeFilter);

        if (rosterCount != null)
            rosterCount.text = visibleUnits.Count == 1 ? "1 unit" : $"{visibleUnits.Count} units";

        BuildDots();
        // Narrowing to the class you are already reading about should not throw the carousel back
        // to the first seat, so the focus only moves when it filters itself out.
        carousel?.SetUnits(visibleUnits, previousFocus);
        bool canStep = visibleUnits.Count > 1;
        prevButton?.SetEnabled(canStep);
        nextButton?.SetEnabled(canStep);
        ShowDossier(carousel != null ? carousel.FocusUnit : null);
    }

    private void BuildDots()
    {
        dots.Clear();
        dotStrip?.Clear();
        if (dotStrip == null)
            return;

        for (int i = 0; i < visibleUnits.Count; i++)
        {
            VisualElement dot = new() { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("carousel-dot");
            dotStrip.Add(dot);
            dots.Add(dot);
        }
    }

    private void OnFocusChanged()
    {
        ShowDossier(carousel.FocusUnit);
    }

    private void SelectPrevious()
    {
        AudioManager.Instance?.PlayButtonClick();
        carousel?.StepBy(-1);
    }

    private void SelectNext()
    {
        AudioManager.Instance?.PlayButtonClick();
        carousel?.StepBy(1);
    }

    private void ShowDossier(UnitData data)
    {
        int focus = carousel != null ? carousel.FocusIndex : -1;
        for (int i = 0; i < dots.Count; i++)
            dots[i].EnableInClassList("carousel-dot--current", i == focus);

        // The class and the weapon together answer "what is this and how does it shoot" in one
        // line, which is the whole job of an eyebrow above the name.
        if (unitClassLabel != null)
        {
            string role = data != null ? data.unitClass.ToString() : string.Empty;
            unitClassLabel.text =
                data != null && !string.IsNullOrWhiteSpace(data.weaponName)
                    ? $"{role}  ·  {data.weaponName}"
                    : role;
        }
        if (unitNameLabel != null)
            unitNameLabel.text = data != null ? data.unitName : "No unit";

        bool hasAbility = data != null && !string.IsNullOrWhiteSpace(data.abilityName);
        if (abilityNameLabel != null)
            abilityNameLabel.text = hasAbility ? data.abilityName : "None";
        if (abilityDetailLabel != null)
        {
            abilityDetailLabel.text = hasAbility
                ? data.abilityDescription
                : "Plans a move every round.";
        }
        if (abilityCooldownLabel != null)
        {
            int rounds = hasAbility ? Mathf.Max(1, data.abilityCooldownRounds) : 0;
            abilityCooldownLabel.text =
                rounds == 1 ? "Every other round" : $"Every {rounds + 1} rounds";
            abilityCooldownLabel.EnableInClassList("hidden", !hasAbility);
        }
    }

    private void OnStagePointerDown(PointerDownEvent evt)
    {
        // Children of the stage are the carousel's own controls; only the open backdrop drags.
        if (carousel == null || evt.target != stage || visibleUnits.Count <= 1)
            return;

        draggingStage = true;
        dragPointerId = evt.pointerId;
        lastDragX = evt.position.x;
        lastDragTime = Time.unscaledTime;
        dragVelocity = 0f;
        stage.CapturePointer(evt.pointerId);
        carousel.BeginDrag();
        evt.StopPropagation();
    }

    private void OnStagePointerMove(PointerMoveEvent evt)
    {
        if (!draggingStage || evt.pointerId != dragPointerId)
            return;

        float delta = evt.position.x - lastDragX;
        float elapsed = Time.unscaledTime - lastDragTime;
        lastDragX = evt.position.x;
        lastDragTime = Time.unscaledTime;

        // Smoothed so the throw speed comes from the whole gesture rather than the last frame,
        // which is often a near-stationary sample taken as the finger lifts.
        if (elapsed > 0f)
            dragVelocity = Mathf.Lerp(dragVelocity, delta / elapsed, 0.55f);

        carousel.DragBy(delta);
        evt.StopPropagation();
    }

    private void OnStagePointerUp(PointerUpEvent evt)
    {
        if (!draggingStage || evt.pointerId != dragPointerId)
            return;

        stage.ReleasePointer(evt.pointerId);
        EndStageDrag();
        evt.StopPropagation();
    }

    private void OnStagePointerCaptureOut(PointerCaptureOutEvent evt)
    {
        EndStageDrag();
    }

    private void EndStageDrag()
    {
        if (!draggingStage)
            return;

        draggingStage = false;
        carousel?.EndDrag(dragVelocity);
        dragVelocity = 0f;
    }

    private void OnStageWheel(WheelEvent evt)
    {
        if (carousel == null || Mathf.Abs(evt.delta.y) < WheelStepThreshold)
            return;

        carousel.StepBy(evt.delta.y > 0f ? 1 : -1);
        evt.StopPropagation();
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        switch (evt.keyCode)
        {
            case KeyCode.Escape:
                ReturnToTitle();
                break;
            case KeyCode.LeftArrow:
                carousel?.StepBy(-1);
                break;
            case KeyCode.RightArrow:
                carousel?.StepBy(1);
                break;
            default:
                return;
        }

        evt.StopImmediatePropagation();
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

    private void ReturnToTitle()
    {
        AudioManager.Instance?.PlayButtonClick();
        SceneManager.LoadScene("Title Screen");
    }

    private void DisposeGeneratedViews()
    {
        foreach (ClassFilterView view in filterViews)
            view.Dispose();
        filterViews.Clear();
        dots.Clear();
    }

    private sealed class ClassFilterView : IDisposable
    {
        public Button Button { get; }
        public UnitClass? UnitClass { get; }
        public Action ClickAction { get; set; }

        public ClassFilterView(Button button, UnitClass? unitClass)
        {
            Button = button;
            UnitClass = unitClass;
        }

        public void SetActive(bool active)
        {
            Button.EnableInClassList("button--selected", active);
        }

        public void Dispose()
        {
            if (Button != null && ClickAction != null)
                Button.clicked -= ClickAction;
            ClickAction = null;
        }
    }
}
