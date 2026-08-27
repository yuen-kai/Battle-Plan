#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The sandbox's controls, in the running game rather than in an Editor window: compose both crews,
/// hold the board open to be rearranged by hand, and put it back. A designer never leaves the board
/// to change what is on it.
///
/// It docks inside the HUD's own visual tree instead of standing up a second panel. That is what
/// makes it pick like the HUD — <see cref="GameHUDController.IsPointerOverUI"/> reads one panel, and
/// a click on a sandbox control must not also land on the board behind it — and it means the game's
/// stylesheets already apply.
///
/// Crew composition itself lives on the unit cards. The cards are already the crew, one per fielded
/// unit on each strip, so a slot is swapped or dropped where it is read rather than in a list here
/// that would have to be kept beside it.
/// </summary>
public sealed class SandboxPanel
{
    private const string LayoutPath = "Assets/UI/Game/SandboxPanel.uxml";
    private const string StylePath = "Assets/UI/Game/SandboxPanel.uss";

    private static bool layoutReported;

    private readonly VisualElement layer;
    private readonly VisualElement body;
    private readonly Button collapseButton;
    private readonly Button moveButton;
    private readonly Label ownCount;
    private readonly Label enemyCount;
    private readonly Toggle ownImmortal;
    private readonly Toggle enemyImmortal;
    private readonly Label hint;
    private readonly VisualElement picker;
    private readonly VisualElement pickerPanel;
    private readonly VisualElement pickerGrid;
    private readonly Label pickerTitle;

    private Button ownAddSlot;
    private Button enemyAddSlot;
    private Action<int> pickedAction;
    private bool collapsed;
    private string appliedSignature;

    private SandboxPanel(VisualElement layer)
    {
        this.layer = layer;
        body = layer.Q<VisualElement>("sandbox-body");
        collapseButton = layer.Q<Button>("sandbox-collapse");
        moveButton = layer.Q<Button>("sandbox-move");
        ownCount = layer.Q<Label>("sandbox-own-count");
        enemyCount = layer.Q<Label>("sandbox-enemy-count");
        ownImmortal = layer.Q<Toggle>("sandbox-own-immortal");
        enemyImmortal = layer.Q<Toggle>("sandbox-enemy-immortal");
        hint = layer.Q<Label>("sandbox-hint");
        picker = layer.Q<VisualElement>("sandbox-picker");
        pickerPanel = layer.Q<VisualElement>("sandbox-picker-panel");
        pickerGrid = layer.Q<VisualElement>("sandbox-picker-grid");
        pickerTitle = layer.Q<Label>("sandbox-picker-title");

        collapseButton.clicked += ToggleCollapsed;
        moveButton.clicked += ToggleBoardEdit;
        layer.Q<Button>("sandbox-reset").clicked += () => Rebuild();
        layer.Q<Button>("sandbox-clear").clicked += ClearCrews;
        layer.Q<Button>("sandbox-recharge").clicked += () =>
            GameLoop.Instance?.SandboxClearAbilityCooldowns();
        layer.Q<Button>("sandbox-picker-close").clicked += ClosePicker;
        ownImmortal.RegisterValueChangedCallback(evt => SetImmortal(OwnTeamIndex, evt.newValue));
        enemyImmortal.RegisterValueChangedCallback(evt =>
            SetImmortal(EnemyTeamIndex, evt.newValue)
        );
        picker.RegisterCallback<KeyDownEvent>(OnPickerKeyDown);
    }

    public static SandboxPanel TryCreate()
    {
        if (GameHUDController.Instance?.Root == null || GameLoop.Instance == null)
            return null;

        VisualTreeAsset layout = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            LayoutPath
        );
        if (layout == null)
        {
            // Creation is retried every frame until the HUD exists, so a missing asset is said once
            // rather than once per frame for the rest of the session.
            if (!layoutReported)
                Debug.LogError($"[Sandbox] No panel layout at {LayoutPath}.");
            layoutReported = true;
            return null;
        }

        // Instantiate wraps the tree in a container of its own, which would otherwise size itself to
        // its content and collapse the full-bleed layer inside it.
        VisualElement layer = layout.Instantiate();
        layer.style.position = Position.Absolute;
        layer.style.left = 0;
        layer.style.right = 0;
        layer.style.top = 0;
        layer.style.bottom = 0;
        layer.pickingMode = PickingMode.Ignore;
        StyleSheet style = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
        if (style != null)
            layer.styleSheets.Add(style);

        SandboxPanel panel = new(layer);
        panel.Attach();
        return panel;
    }

    public void Dispose()
    {
        ownAddSlot?.RemoveFromHierarchy();
        enemyAddSlot?.RemoveFromHierarchy();
        ownAddSlot = null;
        enemyAddSlot = null;
        layer.RemoveFromHierarchy();
    }

    /// <summary>
    /// Keeps the panel on the live HUD and its readouts on the live setup. The HUD rebuilds its tree
    /// whenever the Game scene is re-enabled, which takes the panel and the add-unit tiles with it,
    /// so being attached is checked rather than assumed.
    /// </summary>
    public void Tick()
    {
        if (GameHUDController.Instance?.Root == null)
            return;

        if (layer.parent == null || ownAddSlot?.parent == null || enemyAddSlot?.parent == null)
            Attach();

        string signature = BuildSignature();
        if (signature == appliedSignature)
            return;

        appliedSignature = signature;
        Refresh();
    }

    private void Attach()
    {
        GameHUDController hud = GameHUDController.Instance;
        if (layer.parent != hud.Root)
        {
            layer.RemoveFromHierarchy();
            hud.Root.Add(layer);
        }

        hud.EnableSandboxEnemyCardPresses();
        ownAddSlot = AttachAddSlot(ownAddSlot, hud.CardsStrip, OwnTeamIndex);
        enemyAddSlot = AttachAddSlot(enemyAddSlot, hud.EnemyCardsStrip, EnemyTeamIndex);
        appliedSignature = null;
    }

    /// <summary>
    /// The tile that closes a card strip. It belongs on the strip rather than in the panel: the
    /// strip is the crew, so the slot that is not filled yet is read at the end of it.
    /// </summary>
    private Button AttachAddSlot(Button existing, VisualElement strip, int teamIndex)
    {
        if (strip == null)
            return existing;

        Button slot = existing;
        if (slot == null)
        {
            slot = new Button(() => OpenPicker($"{CrewName(teamIndex)} · new unit", catalogIndex =>
            {
                SandboxSession.TryAddUnit(teamIndex, catalogIndex);
                Rebuild();
            }))
            {
                text = "+ Add",
                tooltip = "Field another unit on this side",
                name = $"sandbox-add-slot-{teamIndex}",
            };
            slot.AddToClassList("sandbox-add-slot");
        }
        if (slot.parent != strip)
        {
            slot.RemoveFromHierarchy();
            strip.Add(slot);
        }
        return slot;
    }

    private void Refresh()
    {
        GameHUDController hud = GameHUDController.Instance;
        SandboxCrew own = SandboxSession.Crew(OwnTeamIndex);
        SandboxCrew enemy = SandboxSession.Crew(EnemyTeamIndex);

        ownCount.text = $"{own.Count} / {SandboxSession.MaxUnitsPerTeam}";
        enemyCount.text = $"{enemy.Count} / {SandboxSession.MaxUnitsPerTeam}";
        ownImmortal.SetValueWithoutNotify(own.immortal);
        enemyImmortal.SetValueWithoutNotify(enemy.immortal);

        bool editing = SandboxSession.BoardEditActive;
        moveButton.EnableInClassList("sandbox-button--armed", editing);
        moveButton.text = editing ? "Moving units" : "Move units";
        hint.text = editing
            ? "Drag any unit to an empty square, between rounds. Turn this off to give orders again."
            : "Click a unit on either side to give it a route or an ability, then lock in to run the "
                + "round. Unplanned units hold. Swap or drop a unit on its own card.";

        ownAddSlot?.SetEnabled(own.Count < SandboxSession.MaxUnitsPerTeam);
        enemyAddSlot?.SetEnabled(enemy.Count < SandboxSession.MaxUnitsPerTeam);

        hud.SetSandboxCrewControls(false, own.Count, slot => OpenSlotMenu(OwnTeamIndex, slot));
        hud.SetSandboxCrewControls(true, enemy.Count, slot => OpenSlotMenu(EnemyTeamIndex, slot));
    }

    /// <summary>
    /// What pressing a character's portrait offers: any other character for that slot, or taking it
    /// off the board. One list rather than a menu in front of a list — changing and removing are the
    /// same decision about the same slot.
    /// </summary>
    private void OpenSlotMenu(int teamIndex, int slot)
    {
        SandboxCrew crew = SandboxSession.Crew(teamIndex);
        if (!crew.HasSlot(slot))
            return;

        OpenPicker(
            $"{CrewName(teamIndex)} · slot {slot + 1}",
            catalogIndex =>
            {
                SandboxSession.ReplaceUnit(teamIndex, slot, catalogIndex);
                Rebuild();
            },
            crew.Count > 1 ? () => RemoveUnit(teamIndex, slot) : null
        );
    }

    /// <summary>
    /// What the panel is showing right now. Comparing it is what keeps the panel off the per-frame
    /// path: rebinding the cards' controls every frame would re-close the picker under the pointer.
    /// </summary>
    private string BuildSignature()
    {
        SandboxCrew own = SandboxSession.Crew(OwnTeamIndex);
        SandboxCrew enemy = SandboxSession.Crew(EnemyTeamIndex);
        return $"{own.Count}:{own.immortal}:{enemy.Count}:{enemy.immortal}:"
            + $"{SandboxSession.BoardEditActive}:{GameLoop.currentPhase}";
    }

    private void RemoveUnit(int teamIndex, int slot)
    {
        if (!SandboxSession.RemoveUnit(teamIndex, slot))
        {
            GameHUDController.Instance?.SetTargetFeedback(
                "A side needs at least one unit on it.",
                true
            );
            return;
        }
        Rebuild();
    }

    private void ClearCrews()
    {
        SandboxSession.ResetAllCrews();
        Rebuild();
    }

    private void Rebuild()
    {
        SandboxLauncher.SaveSetup();
        GameLoop.Instance?.RequestSandboxRebuild();
        appliedSignature = null;
    }

    private void SetImmortal(int teamIndex, bool immortal)
    {
        SandboxSession.Crew(teamIndex).immortal = immortal;
        SandboxLauncher.SaveSetup();
        appliedSignature = null;
    }

    private void ToggleBoardEdit()
    {
        SandboxSession.BoardEditActive = !SandboxSession.BoardEditActive;
        appliedSignature = null;
    }

    private void ToggleCollapsed()
    {
        collapsed = !collapsed;
        body.EnableInClassList("hidden", collapsed);
        collapseButton.text = collapsed ? "+" : "–";
    }

    private void OpenPicker(string title, Action<int> onPicked, Action onRemove = null)
    {
        List<UnitData> catalog = GameLoop.Instance?.allUnits?.units;
        if (catalog == null)
            return;

        pickedAction = onPicked;
        pickerTitle.text = title;
        pickerGrid.Clear();
        if (onRemove != null)
            pickerGrid.Add(BuildRemoveTile(onRemove));
        for (int catalogIndex = 0; catalogIndex < catalog.Count; catalogIndex++)
        {
            UnitData unit = catalog[catalogIndex];
            if (unit == null || unit.unitModel == null)
                continue;
            pickerGrid.Add(BuildPick(catalogIndex, unit));
        }

        picker.RemoveFromClassList("hidden");
        pickerPanel.Focus();
    }

    private VisualElement BuildRemoveTile(Action onRemove)
    {
        Button remove = new(() =>
        {
            ClosePicker();
            onRemove();
        })
        {
            tooltip = "Take this unit off the board",
        };
        remove.AddToClassList("sandbox-pick");
        remove.AddToClassList("sandbox-pick--remove");

        Label glyph = new("×");
        glyph.AddToClassList("sandbox-pick__glyph");
        remove.Add(glyph);

        Label name = new("Remove");
        name.AddToClassList("sandbox-pick__name");
        remove.Add(name);

        Label note = new("off the board");
        note.AddToClassList("sandbox-pick__note");
        remove.Add(note);
        return remove;
    }

    private VisualElement BuildPick(int catalogIndex, UnitData unit)
    {
        Button pick = new(() =>
        {
            Action<int> picked = pickedAction;
            ClosePicker();
            picked?.Invoke(catalogIndex);
        })
        {
            tooltip = string.IsNullOrWhiteSpace(unit.abilityName)
                ? "No ability"
                : unit.abilityName,
        };
        pick.AddToClassList("sandbox-pick");

        VisualElement portrait = new();
        portrait.AddToClassList("sandbox-pick__portrait");
        if (unit.unitSprite != null)
            portrait.style.backgroundImage = new StyleBackground(unit.unitSprite);
        pick.Add(portrait);

        Label name = new(unit.unitName);
        name.AddToClassList("sandbox-pick__name");
        pick.Add(name);

        Label note = new(
            string.IsNullOrWhiteSpace(unit.abilityName) ? "Move only" : unit.abilityName
        );
        note.AddToClassList("sandbox-pick__note");
        pick.Add(note);
        return pick;
    }

    private void ClosePicker()
    {
        pickedAction = null;
        picker.AddToClassList("hidden");
    }

    private void OnPickerKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Escape)
            return;
        ClosePicker();
        evt.StopPropagation();
    }

    private static int OwnTeamIndex
    {
        get
        {
            int localTeamIndex =
                GameLoop.Instance != null ? GameLoop.Instance.LocalTeamIndex : -1;
            return localTeamIndex >= 0 ? localTeamIndex : GameLoop.HostTeamIndex;
        }
    }

    private static int EnemyTeamIndex => GameLoop.TeamCount - 1 - OwnTeamIndex;

    private static string CrewName(int teamIndex) =>
        teamIndex == OwnTeamIndex ? "Your crew" : "Enemy crew";
}
#endif
