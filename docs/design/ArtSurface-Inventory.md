# Battle Plan — Art Surface Inventory

Technical inventory of every art-relevant surface for a full redesign (palette, UI, HUD, map, bullets, SFX, VFX).
Read-only snapshot of the working tree. Paths are repo-relative. Values come from YAML / C# / USS source, not the Editor.

| Field | Value |
| --- | --- |
| Unity | 6000.3.1f1 (from project) |
| Pipeline | URP 17.x (`Assets/Scripts/Renderer/URP.asset` + quality variants) |
| Color space | Linear (`ProjectSettings/ProjectSettings.asset` → `m_ActiveColorSpace: 1`) |
| UI stack | UI Toolkit only (no world-space / TMP UI) |
| Companion content brief | `docs/design/UI-ContentInventory.md` |

---

## 1. UI (Unity UI Toolkit)

### 1.1 Screens → scenes → documents

| Screen | Scene | UXML | USS | Controller | UIDocument GameObject |
| --- | --- | --- | --- | --- | --- |
| Title | `Assets/Scenes/Title Screen.unity` | `Assets/UI/Title/TitleScreen.uxml` | `Assets/UI/Title/TitleScreen.uss` (+ shared) | `TitleScreenUIController` | `TitleScreenUI` |
| Match setup | `Assets/Scenes/JoinGame.unity` | `Assets/UI/Join/JoinGame.uxml` | `Assets/UI/Join/JoinGame.uss` (+ shared) | `JoinGameUIController` | `JoinGameUI` |
| Crew selection | `Assets/Scenes/HomeScreen.unity` | `Assets/UI/Home/CharacterSelection.uxml` | `Assets/UI/Home/CharacterSelectionToybox.uss` (+ shared) | `CharacterSelectionUIController` | `CharacterSelectionUI` |
| Match HUD | `Assets/Scenes/Game.unity` | `Assets/UI/Game/GameHUD.uxml` | `Assets/UI/Game/GameHUD.uss` (+ shared) | `GameHUDController` | `GameHUD` |

### 1.2 All UXML / USS / TSS under `Assets/UI/`

| Path | Purpose | Referenced by |
| --- | --- | --- |
| `Assets/UI/Title/TitleScreen.uxml` | Title tree | `Title Screen.unity` UIDocument |
| `Assets/UI/Title/TitleScreen.uss` | Title layout/skin | Title screen styles |
| `Assets/UI/Join/JoinGame.uxml` | Match setup tree | `JoinGame.unity` |
| `Assets/UI/Join/JoinGame.uss` | Join layout + stage BG | Join screen styles |
| `Assets/UI/Home/CharacterSelection.uxml` | Crew selection tree | `HomeScreen.unity` |
| `Assets/UI/Home/CharacterSelectionToybox.uss` | Roster / map preview skin | Home screen styles |
| `Assets/UI/Game/GameHUD.uxml` | In-match HUD tree | `Game.unity` |
| `Assets/UI/Game/GameHUD.uss` | HUD skin | Game HUD styles |
| `Assets/UI/Shared/Templates/UnitCard.uxml` | Friendly/enemy unit card template | Serialized on `GameHUDController.unitCardTemplate` in `Game.unity` |
| `Assets/UI/Shared/Templates/UnitOption.uxml` | Roster option template | Serialized on `CharacterSelectionUIController` in `HomeScreen.unity` |
| `Assets/UI/Shared/Templates/SelectedSlot.uxml` | Selected slot template | Same controller |
| `Assets/UI/Shared/TacticalToyboxTokens.uss` | **Token definitions** (`--toy-*`) | `@import` from `TacticalToybox.uss`, `BattlePlanRuntime.tss` |
| `Assets/UI/Shared/TacticalToybox.uss` | Shared toybox component library | Screen USS / theme |
| `Assets/UI/Shared/BattlePlan.uss` | Shared Cascadia styles | Shared |
| `Assets/UI/Shared/BattlePlanRuntime.tss` | Runtime ThemeStyleSheet (dropdown chrome + token import) | `BattlePlanPanelSettings.themeUss` |
| `Assets/UI/Shared/BattlePlanPanelSettings.asset` | Shared PanelSettings | All 4 scene UIDocuments |

### 1.3 PanelSettings / theme

| Property | Value |
| --- | --- |
| Asset | `Assets/UI/Shared/BattlePlanPanelSettings.asset` |
| Theme | `Assets/UI/Shared/BattlePlanRuntime.tss` (guid `a6b8e3f16c2d4a7395f0b1c7d8e94a21`) |
| Scale mode | `2` (Scale With Screen Size) |
| Reference resolution | 1920×1080 |
| Match | 0.5 |
| Clear color | off (`m_ClearColor: 0`) |
| Used by | Title / Join / Home / Game UIDocuments |

### 1.4 USS custom properties (tokens)

**Naming convention:** `--toy-<semantic>` (palette), `--toy-space-*`, `--toy-radius-*`, `--toy-target*`, `--toy-type-*`, `--toy-depth-*`, `--toy-focus-*`, `--toy-motion-*`, `--toy-icon-*`, `--toy-scroll-size`, `--toy-stroke*`.

**Defined only in** `Assets/UI/Shared/TacticalToyboxTokens.uss` on `:root`.

| Token | Value |
| --- | --- |
| `--toy-ink` | `rgb(27, 25, 39)` |
| `--toy-playmat` | `rgb(45, 46, 67)` |
| `--toy-playmat-raised` | `rgb(58, 59, 82)` |
| `--toy-playmat-hover` | `rgb(71, 72, 98)` |
| `--toy-playmat-pressed` | `rgb(40, 40, 58)` |
| `--toy-playmat-quiet` | `rgb(38, 36, 52)` |
| `--toy-cream` | `rgb(245, 236, 216)` |
| `--toy-cream-copy` | `rgb(232, 224, 210)` |
| `--toy-cream-border` | `rgba(245, 236, 216, 0.2)` |
| `--toy-cream-wash` | `rgba(245, 236, 216, 0.1)` |
| `--toy-body-copy` | `rgb(213, 205, 218)` |
| `--toy-dark-copy` | `rgb(48, 45, 56)` |
| `--toy-orange` | `rgb(242, 165, 74)` |
| `--toy-orange-hover` | `rgb(255, 186, 92)` |
| `--toy-orange-pressed` | `rgb(224, 143, 54)` |
| `--toy-orange-copy` | `rgb(255, 196, 116)` |
| `--toy-teal` | `rgb(85, 178, 157)` |
| `--toy-teal-hover` | `rgb(104, 198, 176)` |
| `--toy-teal-pressed` | `rgb(69, 158, 139)` |
| `--toy-sky` | `rgb(132, 211, 230)` |
| `--toy-tomato` | `rgb(233, 104, 93)` |
| `--toy-tomato-hover` | `rgb(247, 126, 115)` |
| `--toy-tomato-pressed` | `rgb(207, 83, 75)` |
| `--toy-tomato-copy` | `rgb(255, 184, 176)` |
| `--toy-muted` | `rgb(189, 182, 201)` |
| `--toy-muted-dark` | `rgb(91, 87, 107)` |
| `--toy-divider` | `rgb(78, 76, 99)` |
| `--toy-disabled-surface` | `rgb(43, 43, 58)` |
| `--toy-disabled-border` | `rgb(75, 73, 91)` |
| `--toy-shade` | `rgba(18, 17, 28, 0.88)` |
| `--toy-panel` | `rgba(45, 46, 67, 0.97)` |
| `--toy-transparent` | `rgba(0, 0, 0, 0)` |
| `--toy-space-xs` | `4px` |
| `--toy-space-sm` | `8px` |
| `--toy-space-md` | `12px` |
| `--toy-space-lg` | `20px` |
| `--toy-space-xl` | `32px` |
| `--toy-space-control-x` | `16px` |
| `--toy-space-control-top` | `10px` |
| `--toy-space-control-bottom` | `12px` |
| `--toy-space-control-active-top` | `13px` |
| `--toy-space-control-active-bottom` | `9px` |
| `--toy-space-panel` | `24px` |
| `--toy-space-panel-compact` | `18px` |
| `--toy-space-modal` | `28px` |
| `--toy-space-modal-phone` | `20px` |
| `--toy-radius-sm` | `6px` |
| `--toy-radius-md` | `10px` |
| `--toy-radius-lg` | `14px` |
| `--toy-target-status` | `28px` |
| `--toy-target-compact` | `44px` |
| `--toy-target` | `52px` |
| `--toy-target-phone` | `56px` |
| `--toy-icon-size` | `24px` |
| `--toy-icon-gap` | `11px` |
| `--toy-scroll-size` | `14px` |
| `--toy-stroke-thin` | `2px` |
| `--toy-stroke` | `3px` |
| `--toy-depth-rest` | `6px` |
| `--toy-depth-pressed` | `3px` |
| `--toy-depth-panel` | `5px` |
| `--toy-depth-modal` | `7px` |
| `--toy-focus-width` | `3px` |
| `--toy-focus-emphasis` | `5px` |
| `--toy-type-display` | `38px` |
| `--toy-type-title-lg` | `28px` |
| `--toy-type-title` | `20px` |
| `--toy-type-body-lg` | `16px` |
| `--toy-type-body` | `15px` |
| `--toy-type-label` | `13px` |
| `--toy-type-caption` | `12px` |
| `--toy-type-mono` | `20px` |
| `--toy-motion-fast` | `0.1s` |

**Consumption rule (enforced by tests):** screen/component USS must use `var(--toy-…)` — no raw `rgb(`/`rgba(` in `TacticalToybox.uss` or `BattlePlanRuntime.tss`.

Token consumers (files containing any `var(--toy-*)`):

| Consumer |
| --- |
| `Assets/UI/Game/GameHUD.uss` |
| `Assets/UI/Home/CharacterSelectionToybox.uss` |
| `Assets/UI/Join/JoinGame.uss` |
| `Assets/UI/Shared/BattlePlanRuntime.tss` |
| `Assets/UI/Shared/TacticalToybox.uss` |
| `Assets/UI/Title/TitleScreen.uss` |

### 1.5 Controller ↔ element contracts

Renaming any `name=` / USS class in the left column without updating the controller (and smoke tests) breaks the screen.

#### `Assets/Scripts/Menus/TitleScreenUIController.cs`

- **UXML:** TitleScreen.uxml
- **Role:** Modal open/close, quality dropdown, scene nav

| Query / class string | Element type | Op | Line | What code does |
| --- | --- | --- | --- | --- |
| `settings-modal` | VisualElement | `RequireElement` | 56 | Required bind; null → error log |
| `controls-modal` | VisualElement | `RequireElement` | 57 | Required bind; null → error log |
| `credits-modal` | VisualElement | `RequireElement` | 58 | Required bind; null → error log |
| `quality-dropdown` | DropdownField | `RequireElement` | 59 | Required bind; null → error log |
| `play-button` | Button | `RequireElement` | 60 | Required bind; null → error log |
| `settings-button` | Button | `RequireElement` | 61 | Required bind; null → error log |
| `controls-button` | Button | `RequireElement` | 62 | Required bind; null → error log |
| `credits-button` | Button | `RequireElement` | 63 | Required bind; null → error log |
| `quit-button` | Button | `RequireElement` | 64 | Required bind; null → error log |
| `settings-close-button` | Button | `RequireElement` | 65 | Required bind; null → error log |
| `controls-close-button` | Button | `RequireElement` | 66 | Required bind; null → error log |
| `credits-close-button` | Button | `RequireElement` | 67 | Required bind; null → error log |
| `hidden` | USS class | `RemoveFromClassList` | 196 | Remove USS class |
| `hidden` | USS class | `AddToClassList` | 208 | Add USS class |
| `compact` | USS class | `EnableInClassList` | 232 | Toggle USS class by bool |
| `narrow` | USS class | `EnableInClassList` | 233 | Toggle USS class by bool |
| `phone` | USS class | `EnableInClassList` | 234 | Toggle USS class by bool |
| `short` | USS class | `EnableInClassList` | 235 | Toggle USS class by bool |

#### `Assets/Scripts/Menus/JoinGameUIController.cs`

- **UXML:** JoinGame.uxml
- **Role:** Create/join panels, mode/opponent, relay, local MP

| Query / class string | Element type | Op | Line | What code does |
| --- | --- | --- | --- | --- |
| `create-panel` | VisualElement | `RequireElement` | 133 | Required bind; null → error log |
| `join-panel` | VisualElement | `RequireElement` | 134 | Required bind; null → error log |
| `relay-code-panel` | VisualElement | `RequireElement` | 135 | Required bind; null → error log |
| `local-multiplayer-row` | VisualElement | `RequireElement` | 136 | Required bind; null → error log |
| `connection-code-heading` | Label | `RequireElement` | 137 | Required bind; null → error log |
| `show-create-button` | Button | `RequireElement` | 138 | Required bind; null → error log |
| `show-join-button` | Button | `RequireElement` | 139 | Required bind; null → error log |
| `title-button` | Button | `RequireElement` | 140 | Required bind; null → error log |
| `elimination-button` | Button | `RequireElement` | 141 | Required bind; null → error log |
| `king-button` | Button | `RequireElement` | 142 | Required bind; null → error log |
| `flag-button` | Button | `RequireElement` | 143 | Required bind; null → error log |
| `create-match-button` | Button | `RequireElement` | 144 | Required bind; null → error log |
| `join-match-button` | Button | `RequireElement` | 145 | Required bind; null → error log |
| `cancel-host-button` | Button | `RequireElement` | 146 | Required bind; null → error log |
| `cancel-join-button` | Button | `RequireElement` | 147 | Required bind; null → error log |
| `player-opponent-button` | Button | `RequireElement` | 148 | Required bind; null → error log |
| `ai-opponent-button` | Button | `RequireElement` | 149 | Required bind; null → error log |
| `fog-toggle` | Toggle | `RequireElement` | 150 | Required bind; null → error log |
| `local-multiplayer-toggle` | Toggle | `RequireElement` | 151 | Required bind; null → error log |
| `join-code-input` | TextField | `RequireElement` | 152 | Required bind; null → error log |
| `relay-code-label` | Label | `RequireElement` | 153 | Required bind; null → error log |
| `relay-status-label` | Label | `RequireElement` | 154 | Required bind; null → error log |
| `create-error-label` | Label | `RequireElement` | 155 | Required bind; null → error log |
| `join-status-label` | Label | `RequireElement` | 156 | Required bind; null → error log |
| `hidden` | USS class | `EnableInClassList` | 352 | Toggle USS class by bool |
| `button--selected` | USS class | `EnableInClassList` | 354 | Toggle USS class by bool |
| `button--selected` | USS class | `EnableInClassList` | 355 | Toggle USS class by bool |
| `button--selected` | USS class | `EnableInClassList` | 389 | Toggle USS class by bool |
| `button--selected` | USS class | `EnableInClassList` | 393 | Toggle USS class by bool |
| `button--selected` | USS class | `EnableInClassList` | 411 | Toggle USS class by bool |
| `button--selected` | USS class | `EnableInClassList` | 412 | Toggle USS class by bool |
| `hidden` | USS class | `AddToClassList` | 829 | Add USS class |
| `hidden` | USS class | `ClassListContains` | 875 | Read class presence |
| `hidden` | USS class | `RemoveFromClassList` | 1034 | Remove USS class |
| `status-line--danger` | USS class | `EnableInClassList` | 1081 | Toggle USS class by bool |
| `status-line--danger` | USS class | `EnableInClassList` | 1089 | Toggle USS class by bool |
| `compact` | USS class | `EnableInClassList` | 1149 | Toggle USS class by bool |
| `narrow` | USS class | `EnableInClassList` | 1150 | Toggle USS class by bool |
| `phone` | USS class | `EnableInClassList` | 1151 | Toggle USS class by bool |
| `short` | USS class | `EnableInClassList` | 1152 | Toggle USS class by bool |
| `text-field--caret-hidden` | USS class (const CaretHiddenClass) | `ClassListContains` | 529 | Read class presence |
| `text-field--caret-hidden` | USS class (const CaretHiddenClass) | `EnableInClassList` | 530 | Toggle USS class by bool |
| `text-field--caret-hidden` | USS class (const CaretHiddenClass) | `RemoveFromClassList` | 535 | Remove USS class |

#### `Assets/Scripts/Menus/CharacterSelectionUIController.cs`

- **UXML:** CharacterSelection.uxml + UnitOption/SelectedSlot templates
- **Role:** Roster pick / confirm / network ready

| Query / class string | Element type | Op | Line | What code does |
| --- | --- | --- | --- | --- |
| `roster-options` | ScrollView | `RequireElement` | 125 | Required bind; null → error log |
| `selected-roster` | VisualElement | `RequireElement` | 126 | Required bind; null → error log |
| `roster-instruction` | Label | `RequireElement` | 127 | Required bind; null → error log |
| `roster-count-label` | Label | `RequireElement` | 128 | Required bind; null → error log |
| `confirm-selection-button` | Button | `RequireElement` | 129 | Required bind; null → error log |
| `selection-status` | Label | `RequireElement` | 130 | Required bind; null → error log |
| `mode-summary` | Label | `RequireElement` | 131 | Required bind; null → error log |
| `opponent-summary` | Label | `RequireElement` | 132 | Required bind; null → error log |
| `fog-summary` | Label | `RequireElement` | 133 | Required bind; null → error log |
| `unit-option-button` | Button | `Q` | 236 | Optional/required query |
| `unit-option-portrait` | VisualElement | `Q` | 253 | Optional/required query |
| `unit-option-name` | Label | `Q` | 254 | Optional/required query |
| `unit-option-description` | Label | `Q` | 255 | Optional/required query |
| `unit-option-ability` | Label | `Q` | 256 | Optional/required query |
| `unit-option-status` | Label | `Q` | 257 | Optional/required query |
| `selected-slot-button` | Button | `Q` | 344 | Optional/required query |
| `selected-slot-index` | Label | `Q` | 362 | Optional/required query |
| `selected-slot-portrait` | VisualElement | `Q` | 369 | Optional/required query |
| `selected-slot-name` | Label | `Q` | 370 | Optional/required query |
| `selected-slot-detail` | Label | `Q` | 371 | Optional/required query |
| `unit-option__status` | USS class | `AddToClassList` | 261 | Add USS class |
| `unit-option` | USS class | `AddToClassList` | 284 | Add USS class |
| `unit-option__portrait` | USS class | `AddToClassList` | 287 | Add USS class |
| `unit-option__copy` | USS class | `AddToClassList` | 291 | Add USS class |
| `unit-option__name` | USS class | `AddToClassList` | 295 | Add USS class |
| `unit-option__description` | USS class | `AddToClassList` | 297 | Add USS class |
| `unit-option__ability-row` | USS class | `AddToClassList` | 300 | Add USS class |
| `unit-option__ability-mark` | USS class | `AddToClassList` | 302 | Add USS class |
| `unit-option__ability` | USS class | `AddToClassList` | 304 | Add USS class |
| `selected-slot` | USS class | `AddToClassList` | 378 | Add USS class |
| `selected-slot__index` | USS class | `AddToClassList` | 381 | Add USS class |
| `selected-slot__portrait` | USS class | `AddToClassList` | 383 | Add USS class |
| `selected-slot__copy` | USS class | `AddToClassList` | 385 | Add USS class |
| `selected-slot__name` | USS class | `AddToClassList` | 387 | Add USS class |
| `selected-slot__detail` | USS class | `AddToClassList` | 389 | Add USS class |
| `label--danger` | USS class | `EnableInClassList` | 777 | Toggle USS class by bool |
| `compact` | USS class | `EnableInClassList` | 794 | Toggle USS class by bool |
| `narrow` | USS class | `EnableInClassList` | 795 | Toggle USS class by bool |
| `phone` | USS class | `EnableInClassList` | 796 | Toggle USS class by bool |
| `short` | USS class | `EnableInClassList` | 797 | Toggle USS class by bool |
| `unit-option--selected` | USS class | `EnableInClassList` | 826 | Toggle USS class by bool |
| `unit-option--unavailable` | USS class | `EnableInClassList` | 827 | Toggle USS class by bool |
| `unit-option--selected` | USS class | `EnableInClassList` | 828 | Toggle USS class by bool |
| `unit-option--unavailable` | USS class | `EnableInClassList` | 829 | Toggle USS class by bool |
| `hidden` | USS class | `EnableInClassList` | 838 | Toggle USS class by bool |
| `selected-slot--filled` | USS class | `EnableInClassList` | 887 | Toggle USS class by bool |
| `Pick {UnitsPerPlayer}. Repeats are allowed. Select a filled slot to remove it.` | name pattern | `interpolated` | 137 | Runtime-generated name pattern |
| `unit-option-{index}` | name pattern | `interpolated` | 265 | Runtime-generated name pattern |
| `selected-slot-{index}` | name pattern | `interpolated` | 361 | Runtime-generated name pattern |
| `unit-option-status` | created element | `name=` | 260 | Assigns name when building fallback DOM |
| `unit-option-button` | created element | `name=` | 283 | Assigns name when building fallback DOM |
| `unit-option-portrait` | created element | `name=` | 286 | Assigns name when building fallback DOM |
| `unit-option-name` | created element | `name=` | 294 | Assigns name when building fallback DOM |
| `unit-option-description` | created element | `name=` | 296 | Assigns name when building fallback DOM |
| `unit-option-ability` | created element | `name=` | 303 | Assigns name when building fallback DOM |
| `selected-slot-button` | created element | `name=` | 377 | Assigns name when building fallback DOM |
| `selected-slot-index` | created element | `name=` | 380 | Assigns name when building fallback DOM |
| `selected-slot-portrait` | created element | `name=` | 382 | Assigns name when building fallback DOM |
| `selected-slot-name` | created element | `name=` | 386 | Assigns name when building fallback DOM |
| `selected-slot-detail` | created element | `name=` | 388 | Assigns name when building fallback DOM |

#### `Assets/Scripts/Menus/GameHUDController.cs`

- **UXML:** GameHUD.uxml + UnitCard template
- **Role:** Phase/timer, cards, lock-in, overlays

| Query / class string | Element type | Op | Line | What code does |
| --- | --- | --- | --- | --- |
| `screen` | VisualElement | `RequireElement` | 118 | Required bind; null → error log |
| `hud-flash` | VisualElement | `RequireElement` | 119 | Required bind; null → error log |
| `unit-cards` | VisualElement | `RequireElement` | 120 | Required bind; null → error log |
| `enemy-unit-cards` | VisualElement | `RequireElement` | 121 | Required bind; null → error log |
| `hud-dock` | VisualElement | `RequireElement` | 122 | Required bind; null → error log |
| `deployment-overlay` | VisualElement | `RequireElement` | 123 | Required bind; null → error log |
| `results-overlay` | VisualElement | `RequireElement` | 124 | Required bind; null → error log |
| `results-panel` | VisualElement | `RequireElement` | 125 | Required bind; null → error log |
| `phase-label` | Label | `RequireElement` | 126 | Required bind; null → error log |
| `timer-label` | Label | `RequireElement` | 127 | Required bind; null → error log |
| `hill-status-readout` | VisualElement | `RequireElement` | 128 | Required bind; null → error log |
| `hill-status-label` | Label | `RequireElement` | 129 | Required bind; null → error log |
| `deployment-status` | Label | `RequireElement` | 130 | Required bind; null → error log |
| `results-status` | Label | `RequireElement` | 131 | Required bind; null → error log |
| `play-again-button` | Button | `RequireElement` | 132 | Required bind; null → error log |
| `main-menu-button` | Button | `RequireElement` | 133 | Required bind; null → error log |
| `target-feedback-label` | Label | `Q` | 134 | Optional/required query |
| `planning-commit` | VisualElement | `Q` | 135 | Optional/required query |
| `planning-commit-status` | Label | `Q` | 136 | Optional/required query |
| `lock-in-button` | Button | `Q` | 137 | Optional/required query |
| `controls-button` | Button | `Q` | 138 | Optional/required query |
| `controls-overlay` | VisualElement | `Q` | 139 | Optional/required query |
| `controls-close-button` | Button | `Q` | 141 | Optional/required query |
| `controls-panel` | VisualElement | `Qclass` | 140 | Query by USS class |
| `hud-flash--active` | USS class | `RemoveFromClassList` | 100 | Remove USS class |
| `unit-card-host` | USS class | `AddToClassList` | 223 | Add USS class |
| `unit-card-host--last` | USS class | `AddToClassList` | 225 | Add USS class |
| `unit-card-host` | USS class | `AddToClassList` | 235 | Add USS class |
| `enemy-unit-card-host` | USS class | `AddToClassList` | 236 | Add USS class |
| `enemy-unit-card-host--last` | USS class | `AddToClassList` | 238 | Add USS class |
| `hidden` | USS class | `AddToClassList` | 320 | Add USS class |
| `hidden` | USS class | `RemoveFromClassList` | 356 | Remove USS class |
| `hidden` | USS class | `EnableInClassList` | 378 | Toggle USS class by bool |
| `target-feedback--visible` | USS class | `EnableInClassList` | 379 | Toggle USS class by bool |
| `target-feedback--error` | USS class | `EnableInClassList` | 380 | Toggle USS class by bool |
| `target-feedback--success` | USS class | `EnableInClassList` | 381 | Toggle USS class by bool |
| `label--danger` | USS class | `EnableInClassList` | 382 | Toggle USS class by bool |
| `phase-label--friendly` | USS class | `EnableInClassList` | 420 | Toggle USS class by bool |
| `phase-label--enemy` | USS class | `EnableInClassList` | 424 | Toggle USS class by bool |
| `phase-label--neutral` | USS class | `EnableInClassList` | 425 | Toggle USS class by bool |
| `timer-label--urgent` | USS class | `EnableInClassList` | 438 | Toggle USS class by bool |
| `hud-flash--active` | USS class | `AddToClassList` | 466 | Add USS class |
| `hud-flash--active` | USS class | `RemoveFromClassList` | 468 | Remove USS class |
| `koth` | USS class | `EnableInClassList` | 476 | Toggle USS class by bool |
| `hill-status--blue` | USS class | `EnableInClassList` | 498 | Toggle USS class by bool |
| `hill-status--red` | USS class | `EnableInClassList` | 499 | Toggle USS class by bool |
| `hill-status--contested` | USS class | `EnableInClassList` | 500 | Toggle USS class by bool |
| `hidden` | USS class | `ClassListContains` | 610 | Read class presence |
| `compact` | USS class | `EnableInClassList` | 815 | Toggle USS class by bool |
| `narrow` | USS class | `EnableInClassList` | 816 | Toggle USS class by bool |
| `phone` | USS class | `EnableInClassList` | 817 | Toggle USS class by bool |
| `short` | USS class | `EnableInClassList` | 818 | Toggle USS class by bool |
| `unit-card-{i}` | name pattern | `interpolated` | 222 | Runtime-generated name pattern |
| `enemy-unit-card-{i}` | name pattern | `interpolated` | 234 | Runtime-generated name pattern |
| `{(hostControlled ? "Blue" : "Red")} control · ` | name pattern | `interpolated` | 509 | Runtime-generated name pattern |

#### `Assets/Scripts/Menus/UnitCardElement.cs`

- **UXML:** UnitCard.uxml (or runtime fallback)
- **Role:** Card state machine: select/ability/enemy/cooldown

| Query / class string | Element type | Op | Line | What code does |
| --- | --- | --- | --- | --- |
| `unit-card-root` | VisualElement | `Q` | 42 | Optional/required query |
| `unit-card-select` | Button | `Q` | 49 | Optional/required query |
| `unit-card-portrait` | VisualElement | `Q` | 50 | Optional/required query |
| `unit-card-name` | Label | `Q` | 51 | Optional/required query |
| `unit-card-health` | VisualElement | `Q` | 52 | Optional/required query |
| `unit-card-health-fill` | VisualElement | `Q` | 53 | Optional/required query |
| `unit-card-health-value` | Label | `Q` | 54 | Optional/required query |
| `unit-card-ability` | Label | `Q` | 55 | Optional/required query |
| `unit-card-state` | Label | `Q` | 56 | Optional/required query |
| `unit-card-ability-vignette` | VisualElement | `Q` | 57 | Optional/required query |
| `unit-card-flip-indicator` | VisualElement | `Q` | 58 | Optional/required query |
| `unit-card-flip-icon` | VisualElement | `Q` | 59 | Optional/required query |
| `unit-card-cooldown` | Label | `Q` | 60 | Optional/required query |
| `unit-card` | USS class | `ClassListContains` | 43 | Read class presence |
| `hidden` | USS class | `AddToClassList` | 95 | Add USS class |
| `unit-card--enemy` | USS class | `AddToClassList` | 99 | Add USS class |
| `hidden` | USS class | `RemoveFromClassList` | 100 | Remove USS class |
| `unit-card--disabled` | USS class | `RemoveFromClassList` | 144 | Remove USS class |
| `unit-card--disabled` | USS class | `EnableInClassList` | 213 | Toggle USS class by bool |
| `unit-card--selected` | USS class | `RemoveFromClassList` | 225 | Remove USS class |
| `unit-card--ability` | USS class | `RemoveFromClassList` | 226 | Remove USS class |
| `unit-card--enemy-active` | USS class | `EnableInClassList` | 273 | Toggle USS class by bool |
| `unit-card--enemy-eliminated` | USS class | `EnableInClassList` | 277 | Toggle USS class by bool |
| `unit-card--disabled` | USS class | `RemoveFromClassList` | 281 | Remove USS class |
| `hidden` | USS class | `EnableInClassList` | 314 | Toggle USS class by bool |
| `unit-card__flip-indicator--cooldown` | USS class | `EnableInClassList` | 315 | Toggle USS class by bool |
| `unit-card--ability-cooldown` | USS class | `EnableInClassList` | 328 | Toggle USS class by bool |
| `unit-card--selected` | USS class | `EnableInClassList` | 340 | Toggle USS class by bool |
| `unit-card--ability` | USS class | `EnableInClassList` | 341 | Toggle USS class by bool |
| `unit-card` | USS class | `AddToClassList` | 404 | Add USS class |
| `unit-card__select` | USS class | `AddToClassList` | 407 | Add USS class |
| `unit-card__ability-vignette` | USS class | `AddToClassList` | 409 | Add USS class |
| `unit-card__portrait` | USS class | `AddToClassList` | 411 | Add USS class |
| `unit-card__copy` | USS class | `AddToClassList` | 413 | Add USS class |
| `unit-card__name` | USS class | `AddToClassList` | 415 | Add USS class |
| `unit-card__health` | USS class | `AddToClassList` | 417 | Add USS class |
| `unit-card__health-track` | USS class | `AddToClassList` | 420 | Add USS class |
| `unit-card__health-fill` | USS class | `AddToClassList` | 422 | Add USS class |
| `unit-card__health-value` | USS class | `AddToClassList` | 424 | Add USS class |
| `unit-card__ability` | USS class | `AddToClassList` | 429 | Add USS class |
| `unit-card__state` | USS class | `AddToClassList` | 431 | Add USS class |
| `unit-card__flip-indicator` | USS class | `AddToClassList` | 437 | Add USS class |
| `unit-card__flip-icon` | USS class | `AddToClassList` | 439 | Add USS class |
| `unit-card__cooldown` | USS class | `AddToClassList` | 441 | Add USS class |
| `unit-card-root` | created element | `name=` | 403 | Assigns name when building fallback DOM |
| `unit-card-select` | created element | `name=` | 406 | Assigns name when building fallback DOM |
| `unit-card-ability-vignette` | created element | `name=` | 408 | Assigns name when building fallback DOM |
| `unit-card-portrait` | created element | `name=` | 410 | Assigns name when building fallback DOM |
| `unit-card-name` | created element | `name=` | 414 | Assigns name when building fallback DOM |
| `unit-card-health` | created element | `name=` | 416 | Assigns name when building fallback DOM |
| `unit-card-health-fill` | created element | `name=` | 421 | Assigns name when building fallback DOM |
| `unit-card-health-value` | created element | `name=` | 423 | Assigns name when building fallback DOM |
| `unit-card-ability` | created element | `name=` | 428 | Assigns name when building fallback DOM |
| `unit-card-state` | created element | `name=` | 430 | Assigns name when building fallback DOM |
| `unit-card-flip-indicator` | created element | `name=` | 436 | Assigns name when building fallback DOM |
| `unit-card-flip-icon` | created element | `name=` | 438 | Assigns name when building fallback DOM |
| `unit-card-cooldown` | created element | `name=` | 440 | Assigns name when building fallback DOM |

#### `Assets/Scripts/Menus/ConsoleUiNavigation.cs`

| Query | Type | Notes |
| --- | --- | --- |
| `Query<Button>()` (all buttons) | Button | Configures console/gamepad focus navigation; no name contract |

### 1.6 Planning-commit state classes (HUD)

Hardcoded array in `GameHUDController`:
`planning-commit--ready`, `planning-commit--sending`, `planning-commit--waiting`, `planning-commit--unlocking`, `planning-commit--locked`.

### 1.7 UI editor tests asserting names / classes / strings

| File | What it asserts |
| --- | --- |
| `Assets/UI/Tests/Editor/UIToolkitAssetSmokeTests.cs` (`BattlePlan.UI.Editor.Tests`) | Full screen/template **name contracts**; stylesheet import; MapPreview 1080×720 + hash vs generator + caption `15 × 10`; Cascadia/Rubik SDF font asset paths; SVG VectorImage icons; JoinToyboxStage 1579×885; token file contains `--toy-ink/target/focus-emphasis/motion-fast`; no raw rgb in toybox/runtime theme; GameHUD must not hardcode `name="unit-card-` in UXML; lock-in exposes `Unlock` + `.planning-commit--unlocking`; PanelSettings theme + 1920×1080 |
| `Assets/Scripts/GameManager/Editor/GameplayNetworkEditModeTests.cs` | Runtime HUD card host names `unit-card-{i}`, `enemy-unit-card-{i}`, classes `unit-card-host--last`, `enemy-unit-card-host--last`, card states `unit-card--ability/selected/enemy-active/enemy-eliminated`, labels `ACTIVE`/`ELIMINATED`/`Move only`/HP formats, flip cooldown class |
| `Assets/Scripts/GameManager/Editor/ConsoleUiNavigationEditModeTests.cs` | Console nav behaviour on UI Toolkit buttons |

### 1.8 UI images / icons

| Path | Dims | Referenced from |
| --- | --- | --- |
| `Assets/Images/MapPreview.png` | 1080×720 | `CharacterSelectionToybox.uss`; hash-locked by smoke test |
| `Assets/Images/JoinToyboxStage.png` | 1579×885 | `JoinGame.uss` background |
| `Assets/Images/Portraits/Portrait_*.png` (5) | 512×512 each | Unit card / option portraits |
| `Assets/UI/Shared/Icons/*.svg` (16) | vector | `TacticalToybox.uss` / `GameHUD.uss` icon classes |
| Legacy PNG buttons under `Assets/Images/` (Play/Quit/Settings/etc.) | various | Mostly **orphaned** by toybox redesign (still on disk) |
| `Assets/Images/Unity_g0I8iFX1Y3.png` | 1579×885 | Explicitly forbidden by smoke test (replaced by JoinToyboxStage) |

SVG icon set: `ability-flare`, `back`, `bot`, `check`, `controls`, `create`, `credits`, `elimination`, `flag`, `flip-card`, `fog`, `hill`, `join`, `play`, `player`, `settings`.

---

## 2. Fonts

| Path | Format | Role / references |
| --- | --- | --- |
| `Assets/Fonts/CascadiaCode-VariableFont_wght.ttf` | TTF source | Source for SDF assets |
| `Assets/Fonts/CascadiaCode-VariableFont_wght SDF.asset` | TextCore SDF | Legacy/world text if any |
| `Assets/Fonts/CascadiaCode-VariableFont_wght UI SDF.asset` | UI SDF (SDFAA, Dynamic) | `BattlePlan.uss`, `JoinGame.uss`, `GameHUD.uss` — smoke-tested path |
| `Assets/Fonts/Rubik/Rubik-VariableFont_wght.ttf` | TTF | Source |
| `Assets/Fonts/Rubik/Rubik-VariableFont_wght UI SDF.asset` | UI SDF | `TacticalToybox.uss`, `BattlePlanRuntime.tss` primary UI face |
| `Assets/Fonts/Rubik/OFL.txt` | License | Rubik OFL |
| `Assets/TextMesh Pro/Fonts/LiberationSans.ttf` | TTF | TMP default package font |
| `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF*.asset` | TMP SDF | TMP defaults (not used for UI Toolkit screens) |
| `Assets/TextMesh Pro/Resources/TMP Settings.asset` | Settings | TMP global |

UI Toolkit uses **TextCore FontAssets via `-unity-font-definition: url(project://database/...)`**, not TMP Text components.

---

## 3. Materials & shaders

### 3.1 Custom shaders (`Assets/Shaders/`)

| File | Shader path string | Queue | Blend | Key properties (defaults) |
| --- | --- | --- | --- | --- |
| `BP_FogOverlay.shader` | `BattlePlan/FogOverlay` | Transparent | SrcAlpha OneMinusSrcAlpha | `_FogColor (0.006,0.009,0.016,0.76)`, `_EdgeSoftness 0.22`, `_BoundaryOpacity 0.32`, instanced `_EdgeMask` |
| `BP_EnergyBeam.shader` | `BattlePlan/EnergyBeam` | Transparent+50 | One One (additive) | HDR `_GlowColor (1,0.14,0.25,1)`, HDR `_CoreColor (4,4,4,1)`, `_CoreWidth 0.25`, `_EdgeSoftness 0.5`, `_ScrollSpeed 3`, `_NoiseScale 8`, `_NoiseStrength 0.35`, `_Intensity 1` |
| `BP_GroundGlow.shader` | `BattlePlan/GroundGlow` | Transparent+40 | One One | HDR `_GlowColor (1,0.14,0.25,1)`, `_RingWidth 1`, `_EdgeSoftness 0.5`, `_Intensity 1`, `_PulseSpeed 0`, `_PulseAmount 0.3` |
| `BP_VisionCone.shader` | `BattlePlan/VisionCone` | Transparent+30 | One One | HDR `_ConeColor (0.55,0.8,1,1)`, `_ApexIntensity 0.25`, `_FarFade 0.55`, `_SideSoftness 0.35`, `_FlickerSpeed 0`, `_FlickerAmount 0.1` |

Runtime also uses package shaders by string: `Shader.Find("Sprites/Default")`, `Shader.Find("BattlePlan/…")`, URP Lit via materials.

### 3.2 Materials by folder

Surface: `0`=Opaque, `1`=Transparent. Emission listed when `_EMISSION` keyword or non-black emission present.

#### `Assets/Materials/Abilities/`

| Material | Shader | Base / primary color | Emission | Smooth | Metal | Surface | Queue | Referenced by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `Shield.mat` | Universal Render Pipeline/Lit | rgba(0.343, 0.760, 0.943, 0.325) | off | 0.5 | 0.0 | 1.0 | 3000 | `Assets/Prefabs/Units/Shotgunner.prefab` |

#### `Assets/Materials/Characters/`

| Material | Shader | Base / primary color | Emission | Smooth | Metal | Surface | Queue | Referenced by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `Char_AmberPads.mat` | Universal Render Pipeline/Lit | rgba(0.720, 0.550, 0.160, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/Shotgunner.prefab` |
| `Char_Black.mat` | Universal Render Pipeline/Lit | rgba(0.055, 0.060, 0.070, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/PogoRider.prefab`, `Assets/Prefabs/Units/Commander.prefab`, `Assets/Prefabs/Units/Sniper.prefab`, `Assets/Prefabs/Units/Shotgunner.prefab`, `Assets/Prefabs/Units/Soldier.prefab`, `Assets/Prefabs/Units/Unit.prefab` |
| `Char_CloakGray.mat` | Universal Render Pipeline/Lit | rgba(0.620, 0.660, 0.740, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | *(unreferenced in prefabs/scenes/assets scanned)* |
| `Char_CoatNavy.mat` | Universal Render Pipeline/Lit | rgba(0.130, 0.150, 0.190, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/Commander.prefab` |
| `Char_Gunmetal.mat` | Universal Render Pipeline/Lit | rgba(0.130, 0.140, 0.160, 1.000) | off | 0.35 | 0.4 | 0.0 | -1 | `Assets/Prefabs/Units/Commander.prefab`, `Assets/Prefabs/Units/Sniper.prefab`, `Assets/Prefabs/Units/Shotgunner.prefab`, `Assets/Prefabs/Units/Soldier.prefab` |
| `Char_HairBrown.mat` | Universal Render Pipeline/Lit | rgba(0.230, 0.150, 0.090, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/PogoRider.prefab`, `Assets/Prefabs/Units/Commander.prefab`, `Assets/Prefabs/Units/Shotgunner.prefab` |
| `Char_JumpsuitOrange.mat` | Universal Render Pipeline/Lit | rgba(0.380, 0.330, 0.250, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/PogoRider.prefab` |
| `Char_KhakiGear.mat` | Universal Render Pipeline/Lit | rgba(0.420, 0.390, 0.310, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | *(unreferenced in prefabs/scenes/assets scanned)* |
| `Char_OliveFatigues.mat` | Universal Render Pipeline/Lit | rgba(0.240, 0.270, 0.230, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/Shotgunner.prefab`, `Assets/Prefabs/Units/Soldier.prefab` |
| `Char_Skin.mat` | Universal Render Pipeline/Lit | rgba(0.850, 0.645, 0.500, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/PogoRider.prefab`, `Assets/Prefabs/Units/Commander.prefab`, `Assets/Prefabs/Units/Sniper.prefab`, `Assets/Prefabs/Units/Shotgunner.prefab`, `Assets/Prefabs/Units/Soldier.prefab` |
| `Char_TealAccent.mat` | Universal Render Pipeline/Lit | rgba(0.240, 0.290, 0.310, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/PogoRider.prefab` |
| `Char_TeamBase.mat` | Universal Render Pipeline/Lit | rgba(0.250, 0.270, 0.310, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/PogoRider.prefab`, `Assets/Prefabs/Units/Commander.prefab`, `Assets/Prefabs/Units/Sniper.prefab`, `Assets/Prefabs/Units/Shotgunner.prefab`, `Assets/Prefabs/Units/Soldier.prefab` |
| `Char_White.mat` | Universal Render Pipeline/Lit | rgba(0.920, 0.940, 0.970, 1.000) | off | 0.12 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/Commander.prefab`, `Assets/Prefabs/Units/Sniper.prefab`, `Assets/Prefabs/Units/Shotgunner.prefab`, `Assets/Prefabs/Units/Soldier.prefab` |

#### `Assets/Materials/FX/`

| Material | Shader | Base / primary color | Emission | Smooth | Metal | Surface | Queue | Referenced by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `FX_BeamBlue.mat` | BattlePlan/EnergyBeam | rgba(0.440, 1.600, 2.400, 1.000) | off | — | — | — | -1 | *(unreferenced in prefabs/scenes/assets scanned)* |
| `FX_BeamRed.mat` | BattlePlan/EnergyBeam | rgba(2.400, 0.350, 0.600, 1.000) | off | — | — | — | -1 | *(unreferenced in prefabs/scenes/assets scanned)* |
| `FX_GroundGlow.mat` | BattlePlan/GroundGlow | rgba(1.000, 0.140, 0.250, 1.000) | off | — | — | — | -1 | *(unreferenced in prefabs/scenes/assets scanned)* |
| `FX_VisionCone.mat` | BattlePlan/VisionCone | rgba(0.550, 0.800, 1.000, 0.280) | off | — | — | — | -1 | `Assets/Prefabs/Units/Unit.prefab` |
| `Map_FloorDark.mat` | Universal Render Pipeline/Lit | rgba(0.430, 0.450, 0.480, 1.000) | off | 0.18 | 0.0 | 0.0 | -1 | *(unreferenced in prefabs/scenes/assets scanned)* |
| `Map_FloorDarkAlt.mat` | Universal Render Pipeline/Lit | rgba(0.500, 0.520, 0.550, 1.000) | off | 0.18 | 0.0 | 0.0 | -1 | *(unreferenced in prefabs/scenes/assets scanned)* |
| `Map_WallDark.mat` | Universal Render Pipeline/Lit | rgba(0.220, 0.250, 0.300, 1.000) | off | 0.32 | 0.15 | 0.0 | -1 | *(unreferenced in prefabs/scenes/assets scanned)* |

#### `Assets/Materials/Map/`

| Material | Shader | Base / primary color | Emission | Smooth | Metal | Surface | Queue | Referenced by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `GridCell.mat` | Universal Render Pipeline/Lit | rgba(1.000, 1.000, 1.000, 1.000) | off | 0.5 | 0.0 | 0.0 | -1 | *(unreferenced in prefabs/scenes/assets scanned)* |
| `GridCellOutline.mat` | Universal Render Pipeline/Lit | rgba(0.240, 0.340, 0.390, 1.000) | off | — | — | — | -1 | `Assets/Prefabs/Map/GridCell.prefab`, `Assets/Scenes/Game.unity` |
| `Map_Backdrop.mat` | Universal Render Pipeline/Lit | rgba(0.500, 0.660, 0.740, 1.000) | off | 0.08 | 0.0 | 0.0 | -1 | `Assets/Scenes/Game.unity` |
| `Map_FloorDay.mat` | Universal Render Pipeline/Lit | rgba(0.580, 0.690, 0.750, 1.000) | off | 0.24 | 0.02 | 0.0 | -1 | `Assets/Prefabs/Map/GridCell.prefab`, `Assets/Scenes/Game.unity` |
| `Map_FloorDayAlt.mat` | Universal Render Pipeline/Lit | rgba(0.660, 0.750, 0.800, 1.000) | off | 0.24 | 0.02 | 0.0 | -1 | `Assets/Scenes/Game.unity` |
| `Map_GlowPool.mat` | BattlePlan/GroundGlow | rgba(0.210, 0.560, 0.530, 1.000) | off | — | — | — | -1 | `Assets/Scenes/Game.unity` |
| `Map_HazardBlack.mat` | Universal Render Pipeline/Lit | rgba(0.035, 0.060, 0.075, 1.000) | off | 0.18 | 0.0 | 0.0 | -1 | `Assets/Scenes/Game.unity` |
| `Map_HazardYellow.mat` | Universal Render Pipeline/Lit | rgba(0.920, 0.580, 0.045, 1.000) | off | 0.22 | 0.0 | 0.0 | -1 | `Assets/Scenes/Game.unity` |
| `Map_Trim.mat` | Universal Render Pipeline/Lit | rgba(0.040, 0.340, 0.420, 1.000) | off | 0.38 | 0.28 | 0.0 | -1 | `Assets/Scenes/Game.unity` |
| `Map_WallCap.mat` | Universal Render Pipeline/Lit | rgba(0.680, 0.780, 0.820, 1.000) | off | 0.32 | 0.12 | 0.0 | -1 | `Assets/Prefabs/Map/Wall.prefab` |
| `Map_WallDay.mat` | Universal Render Pipeline/Lit | rgba(0.200, 0.320, 0.400, 1.000) | off | 0.36 | 0.22 | 0.0 | -1 | `Assets/Prefabs/Map/Wall.prefab` |
| `Wall.mat` | Universal Render Pipeline/Lit | rgba(0.453, 0.421, 0.421, 1.000) | off | 0.5 | 0.0 | 0.0 | -1 | *(unreferenced in prefabs/scenes/assets scanned)* |

#### `Assets/Materials/Projectiles/`

| Material | Shader | Base / primary color | Emission | Smooth | Metal | Surface | Queue | Referenced by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `BulletBlue.mat` | Universal Render Pipeline/Lit | rgba(0.220, 0.780, 1.000, 1.000) | rgba(0.440, 1.560, 2.000, 1.000) | 0.5 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Projectiles/BulletBlue.prefab` |
| `BulletRed.mat` | Universal Render Pipeline/Lit | rgba(1.000, 0.230, 0.330, 1.000) | rgba(2.000, 0.460, 0.660, 1.000) | — | — | — | -1 | `Assets/Prefabs/Projectiles/BulletRed.prefab`, `Assets/Prefabs/Projectiles/BulletBlue.prefab` |
| `Grenade.mat` | Universal Render Pipeline/Lit | rgba(0.000, 0.792, 0.257, 1.000) | off | 0.5 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Projectiles/Grenade.prefab` |
| `SmokeCanister.mat` | Universal Render Pipeline/Lit | rgba(0.340, 0.390, 0.400, 1.000) | off | 0.5 | 0.5 | 0.0 | -1 | `Assets/Prefabs/Projectiles/SmokeCanister.prefab` |
| `SniperSuperBullet.mat` | Universal Render Pipeline/Lit | rgba(0.980, 0.712, 0.200, 1.000) | rgba(0.877, 0.705, 0.000, 1.000) | 0.5 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Projectiles/SniperSuperRed.prefab` |

#### `Assets/Materials/Teams/`

| Material | Shader | Base / primary color | Emission | Smooth | Metal | Surface | Queue | Referenced by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `TeamBlue.mat` | Universal Render Pipeline/Lit | rgba(0.186, 0.264, 0.962, 1.000) | off | 0.5 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/Commander.prefab`, `Assets/Scenes/Game.unity` |
| `TeamBlueGlow.mat` | Universal Render Pipeline/Lit | rgba(0.186, 0.264, 0.962, 1.000) | rgba(0.120, 0.500, 0.700, 1.000) | 0.15 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/Unit.prefab` |
| `TeamRed.mat` | Universal Render Pipeline/Lit | rgba(0.972, 0.206, 0.279, 1.000) | off | 0.5 | 0.0 | 0.0 | -1 | `Assets/Scenes/Game.unity` |
| `TeamRedGlow.mat` | Universal Render Pipeline/Lit | rgba(0.972, 0.206, 0.279, 1.000) | rgba(0.700, 0.100, 0.180, 1.000) | 0.15 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Units/Unit.prefab` |

#### `Assets/Materials/Visuals/`

| Material | Shader | Base / primary color | Emission | Smooth | Metal | Surface | Queue | Referenced by |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `AOEoverlay.mat` | Universal Render Pipeline/Lit | rgba(1.000, 0.680, 0.495, 0.035) | off | 0.5 | 0.0 | 1.0 | 3000 | *(unreferenced in prefabs/scenes/assets scanned)* |
| `AbilityRangeCell.mat` | Universal Render Pipeline/Lit | rgba(0.189, 0.520, 0.934, 0.498) | off | 0.5 | 0.0 | 1.0 | 3000 | `Assets/Prefabs/Visuals/AbilityRangeOverlay.prefab` |
| `AbilityRangeOutline.mat` | Universal Render Pipeline/Lit | rgba(1.000, 1.000, 1.000, 1.000) | off | — | — | — | 2000 | `Assets/Prefabs/Visuals/AbilityRangeOverlay.prefab` |
| `AttackOverlay.mat` | Universal Render Pipeline/Lit | rgba(1.000, 0.680, 0.495, 0.761) | off | 0.5 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Visuals/AttackOverlay.prefab`, `Assets/Prefabs/Visuals/AOEoverlay.prefab` |
| `FogOverlayCell.mat` | BattlePlan/FogOverlay | rgba(0.009, 0.033, 0.047, 0.720) | off | — | — | — | 2980 | `Assets/Prefabs/Visuals/FogOverlayCell.prefab` |
| `MoveOverlayCell.mat` | Universal Render Pipeline/Lit | rgba(0.189, 0.520, 0.934, 0.498) | off | 0.5 | 0.0 | 1.0 | 3000 | `Assets/Prefabs/Visuals/MoveOverlayCell.prefab` |
| `MoveOverlayCellOutline.mat` | Universal Render Pipeline/Lit | rgba(1.000, 1.000, 1.000, 1.000) | off | — | — | — | 2000 | `Assets/Prefabs/Visuals/MoveOverlayCell.prefab` |
| `PathEdge.mat` | Universal Render Pipeline/Lit | rgba(0.580, 0.634, 0.784, 1.000) | off | 0.5 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Visuals/PathEdge.prefab` |
| `PathNode.mat` | Universal Render Pipeline/Lit | rgba(0.310, 0.925, 0.749, 1.000) | off | 0.5 | 0.0 | 0.0 | -1 | `Assets/Prefabs/Visuals/AbilityIndicator.prefab`, `Assets/Prefabs/Visuals/PathNode.prefab` |

### 3.3 Materials referenced from code (by shader name / MPB, not path)

| Script | Mechanism |
| --- | --- |
| `BeamVFX.cs` | `Shader.Find("BattlePlan/EnergyBeam")` → runtime `Material` |
| `ImpactShockwave.cs` | `Shader.Find("BattlePlan/GroundGlow")` + CreatePrimitive Quad |
| `VisionConeVisual.cs` | `Shader.Find("BattlePlan/VisionCone")` or assigned mat |
| `Movement.cs` | `BattlePlan/GroundGlow` for boost rings/streaks |
| `Shooting.cs` | `BattlePlan/EnergyBeam` on target-lock LineRenderer |
| `SmokeScreenVisual.cs` | `Sprites/Default` runtime mats + generated soft texture |
| `PathRibbon.cs` / `AbilityPathIndicator.cs` / `PlanMovement.cs` | `Sprites/Default` LineRenderer mats |
| `GameLoop.cs` | Serialized `teamMaterials[2]`; fog MPB `_EdgeMask` on fog overlay renderers |
| `HitFlash.cs` | MPB `_BaseColor` / `_EmissionColor` on unit renderers |

---

## 4. Prefabs

**Network-registered** (in `Assets/DefaultNetworkPrefabs.asset`) — replacing these has Netcode spawn implications:

- `Assets/Prefabs/Projectiles/BulletBlue.prefab`
- `Assets/Prefabs/Projectiles/BulletRed.prefab`
- `Assets/Prefabs/Projectiles/SniperSuperBlue.prefab`
- `Assets/Prefabs/Projectiles/SniperSuperRed.prefab`
- `Assets/Prefabs/Units/Commander.prefab`
- `Assets/Prefabs/Units/PogoRider.prefab`
- `Assets/Prefabs/Units/Shotgunner.prefab`
- `Assets/Prefabs/Units/Sniper.prefab`
- `Assets/Prefabs/Units/Soldier.prefab`
- `Assets/Prefabs/Units/Unit.prefab`
- `Assets/Prefabs/Projectiles/Grenade.prefab`
- `Assets/Prefabs/Projectiles/GrenadeExplosion.prefab`
- `Assets/Prefabs/Map/Wall.prefab`
- `Assets/Prefabs/Projectiles/SmokeCanister.prefab`

### 4.1 Prefab catalogue

| Path | Root / variant | Scripts | Materials | Meshes / source | Particles | Network? | Spawn / reference |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `Assets/Prefabs/AudioManager.prefab` | AudioManager | AudioManager | — | — | — | no | Stub manager; `buttonClick` clip wired; music/SFX sources null |
| `Assets/Prefabs/Color Grade.prefab` | Color Grade | 172515602e62fb746b5d573b38a5fe58 | — | — | — | no | Volume → Title Global Volume Profile |
| `Assets/Prefabs/Map/GridCell.prefab` | GridCell | — | `GridCellOutline.mat`<br>`Map_FloorDay.mat` | `0000000000000000e000000000000000` | — | no | Authored floor tile; GridSystem.CreateGrid commented out — board is scene-authored |
| `Assets/Prefabs/Map/Wall.prefab` | TopCap | d5a57f767e5e46a458fc5d3c628d0cbb | `Map_WallCap.mat`<br>`Map_WallDay.mat` | `0000000000000000e000000000000000`, `Assets/Prefabs/Map/WallColliderOctagon.asset` | — | YES | GameLoop.wallPrefab via NetworkHelper.Spawn at StartGame |
| `Assets/Prefabs/Music.prefab` | Music | — | — | — | — | no | AudioSource → Character_Selections.wav |
| `Assets/Prefabs/Projectiles/BulletBlue.prefab` | BulletBlue | Bullet, d5a57f767e5e46a458fc5d3c628d0cbb, e96cb6065543e43c4a752faaa1468eb1 | `BulletBlue.mat`<br>`BulletRed.mat` | `0000000000000000e000000000000000` | — | YES | Network bullet (host team) |
| `Assets/Prefabs/Projectiles/BulletRed.prefab` | VARIANT ← `Assets/Prefabs/Projectiles/BulletBlue.prefab` | — | `BulletRed.mat` | — | — | YES | Variant of BulletBlue |
| `Assets/Prefabs/Projectiles/Grenade.prefab` | Grenade | d5a57f767e5e46a458fc5d3c628d0cbb, e96cb6065543e43c4a752faaa1468eb1 | `Grenade.mat` | `0000000000000000e000000000000000` | — | YES | Soldier ability projectile |
| `Assets/Prefabs/Projectiles/GrenadeExplosion.prefab` | GrenadeExplosion | d5a57f767e5e46a458fc5d3c628d0cbb | — | `Builtin:0` | GrenadeExplosion life=0.5 size=0.2 rate=0 colOL=False | YES | Particle burst on detonation |
| `Assets/Prefabs/Projectiles/SmokeCanister.prefab` | SmokeCanister | d5a57f767e5e46a458fc5d3c628d0cbb, e96cb6065543e43c4a752faaa1468eb1 | `SmokeCanister.mat` | `0000000000000000e000000000000000` | — | YES | Commander smoke projectile |
| `Assets/Prefabs/Projectiles/SniperSuperBlue.prefab` | VARIANT ← `Assets/Prefabs/Projectiles/SniperSuperRed.prefab` | — | — | — | — | YES | Variant chain → BulletBlue |
| `Assets/Prefabs/Projectiles/SniperSuperRed.prefab` | VARIANT ← `Assets/Prefabs/Projectiles/BulletBlue.prefab` | — | `SniperSuperBullet.mat` | — | — | YES | Variant of BulletBlue |
| `Assets/Prefabs/SFX.prefab` | SFX | — | — | — | — | no | AudioSource, no clip |
| `Assets/Prefabs/Units/Commander.prefab` | VARIANT ← `Assets/Models and stuff/Pistol.fbx` | Smoke | `Char_Black.mat`<br>`Char_CoatNavy.mat`<br>`Char_Gunmetal.mat`<br>`Char_HairBrown.mat`<br>`Char_Skin.mat`<br>`Char_TeamBase.mat`<br>`Char_White.mat`<br>`TeamBlue.mat` | — | — | YES | Roster / NetworkPrefabs |
| `Assets/Prefabs/Units/PogoRider.prefab` | VARIANT ← `Assets/Prefabs/Units/Unit.prefab` | Pogo | `Char_Black.mat`<br>`Char_HairBrown.mat`<br>`Char_JumpsuitOrange.mat`<br>`Char_Skin.mat`<br>`Char_TealAccent.mat`<br>`Char_TeamBase.mat` | `Assets/Models and stuff/PogoRiderOld.fbx` | — | YES | Roster / NetworkPrefabs |
| `Assets/Prefabs/Units/Shotgunner.prefab` | VARIANT ← `Assets/Models and stuff/Shotgunner.fbx` | Shield | `Shield.mat`<br>`Char_AmberPads.mat`<br>`Char_Black.mat`<br>`Char_Gunmetal.mat`<br>`Char_HairBrown.mat`<br>`Char_OliveFatigues.mat`<br>`Char_Skin.mat`<br>`Char_TeamBase.mat`<br>`Char_White.mat` | `0000000000000000e000000000000000` | — | YES | Roster / NetworkPrefabs |
| `Assets/Prefabs/Units/Sniper.prefab` | VARIANT ← `Assets/Prefabs/Units/Unit.prefab` | AreaLock | `Char_Black.mat`<br>`Char_Gunmetal.mat`<br>`Char_Skin.mat`<br>`Char_TeamBase.mat`<br>`Char_White.mat` | `Assets/Models and stuff/SniperOld.fbx` | — | YES | Roster / NetworkPrefabs |
| `Assets/Prefabs/Units/Soldier.prefab` | VARIANT ← `Assets/Prefabs/Units/Unit.prefab` | Grenade | `Char_Black.mat`<br>`Char_Gunmetal.mat`<br>`Char_OliveFatigues.mat`<br>`Char_Skin.mat`<br>`Char_TeamBase.mat`<br>`Char_White.mat` | — | — | YES | Roster / NetworkPrefabs |
| `Assets/Prefabs/Units/Unit.prefab` | Bar | 0cd44c1031e13a943bb63640046fad76, AnimationHandler, Health, Movement, Shooting, Unit, VisionConeVisual, d5a57f767e5e46a458fc5d3c628d0cbb, dc42784cf147c0c48a680349fa168899, e96cb6065543e43c4a752faaa1468eb1, fe87c0e1cc204ed48ad3b37840f39efc | `Char_Black.mat`<br>`FX_VisionCone.mat`<br>`TeamBlueGlow.mat`<br>`TeamRedGlow.mat` | `0000000000000000e000000000000000`, `Builtin:0` | — | YES | Base networked unit; variants inherit |
| `Assets/Prefabs/Visuals/AOEoverlay.prefab` | AOEoverlay | — | `AttackOverlay.mat` | `0000000000000000e000000000000000` | — | no | AOE telegraph |
| `Assets/Prefabs/Visuals/AbilityIndicator.prefab` | AbilityIndicator | — | `PathNode.mat` | `0000000000000000e000000000000000` | — | no | Ability marker |
| `Assets/Prefabs/Visuals/AbilityRangeOverlay.prefab` | AbilityRangeOverlay | — | `AbilityRangeCell.mat`<br>`AbilityRangeOutline.mat` | `0000000000000000e000000000000000` | — | no | Ability range cells |
| `Assets/Prefabs/Visuals/AttackOverlay.prefab` | AttackOverlay | — | `AttackOverlay.mat` | `0000000000000000e000000000000000` | — | no | Attack/ability overlays |
| `Assets/Prefabs/Visuals/FogOverlayCell.prefab` | FogOverlayCell | — | `FogOverlayCell.mat` | `0000000000000000e000000000000000` | — | no | GameLoop.fogOverlayCellPrefab instantiated in fog grid |
| `Assets/Prefabs/Visuals/MoveOverlayCell.prefab` | MoveOverlayCell | — | `MoveOverlayCell.mat`<br>`MoveOverlayCellOutline.mat` | `0000000000000000e000000000000000` | — | no | GridSystem.DisplayGridRange etc. |
| `Assets/Prefabs/Visuals/PathEdge.prefab` | PathEdge | — | `PathEdge.mat` | `0000000000000000e000000000000000` | — | no | Path edge |
| `Assets/Prefabs/Visuals/PathNode.prefab` | VARIANT ← `Assets/Models and stuff/PathNode.fbx` | — | `PathNode.mat` | `0000000000000000e000000000000000` | — | no | Path preview node |
| `Assets/Prefabs/Visuals/PathSection.prefab` | VARIANT ← `Assets/Prefabs/Visuals/PathNode.prefab` | — | — | — | — | no | Variant of PathNode |

### 4.2 Notable prefab details

- **Wall:** children `Wall` + `TopCap`; mats `Map_WallDay` + `Map_WallCap`; octagon mesh collider `Assets/Prefabs/Map/WallColliderOctagon.asset`; NetworkObject.
- **Unit base:** children include `BasePuck`, `BasePuckRim`, `VisionCone`, `HealthBar`/`HealthFill` (world canvas), `Alert`; mats include team glow + vision cone.
- **BulletBlue** lists both `BulletBlue.mat` and `BulletRed.mat` in YAML (likely multi-renderer / leftover) — verify before palette swap.
- **GrenadeExplosion:** ParticleSystem lifetime≈0.5, size≈0.2, rateOverTime≈0.
- **No Addressables** usage found for these prefabs; spawning is Netcode list + serialized fields + `Instantiate`.

---

## 5. Scenes

### `Assets/Scenes/HomeScreen.unity`

**Camera `Main Camera`**

| Prop | Value |
| --- | --- |
| Position | ['0', '1', '-10'] |
| Rotation (quat) | ['0', '0', '0', '1'] |
| Projection | Perspective |
| FOV | 60 |
| Near/Far | 0.3 / 1000 |
| Clear flags | Solid Color |
| Background | rgba(0.549, 0.439, 0.643, 1.000) |
| Post-process (URP camera flag in YAML) | False |

**Lights**

| Name | Type | Color | Intensity | Shadows | Rotation (quat) |
| --- | --- | --- | --- | --- | --- |
| Directional Light | Directional | rgba(1.000, 0.957, 0.839, 1.000) | 1 | Soft | ['0.40821788', '-0.23456968', '0.10938163', '0.8754261'] |

**Environment:** ambientMode=0, ambientSky=['0.212', '0.227', '0.259'], fog=0, skybox=`None`

**UIDocument**

| GO | UXML | PanelSettings |
| --- | --- | --- |
| `CharacterSelectionUI` | `Assets/UI/Home/CharacterSelection.uxml` | `Assets/UI/Shared/BattlePlanPanelSettings.asset` |

**Volume components:** none as classic Volume YAML blocks in this parse; Game scene GUID-references `GameDaylight Volume Profile`. Title Global profile is used by `Color Grade.prefab`.

### `Assets/Scenes/Title Screen.unity`

**Camera `Main Camera`**

| Prop | Value |
| --- | --- |
| Position | ['0', '1', '-15.12'] |
| Rotation (quat) | ['0', '0', '0', '1'] |
| Projection | Perspective |
| FOV | 48.34965 |
| Near/Far | 0.3 / 1000 |
| Clear flags | Solid Color |
| Background | rgba(0.459, 0.486, 0.718, 1.000) |
| Post-process (URP camera flag in YAML) | False |

**Lights**

| Name | Type | Color | Intensity | Shadows | Rotation (quat) |
| --- | --- | --- | --- | --- | --- |
| Directional Light | Directional | rgba(1.000, 0.957, 0.839, 1.000) | 1 | Soft | ['0.40821788', '-0.23456968', '0.10938163', '0.8754261'] |

**Environment:** ambientMode=0, ambientSky=['0.212', '0.227', '0.259'], fog=0, skybox=`None`

**UIDocument**

| GO | UXML | PanelSettings |
| --- | --- | --- |
| `TitleScreenUI` | `Assets/UI/Title/TitleScreen.uxml` | `Assets/UI/Shared/BattlePlanPanelSettings.asset` |

**Volume components:** none as classic Volume YAML blocks in this parse; Game scene GUID-references `GameDaylight Volume Profile`. Title Global profile is used by `Color Grade.prefab`.

### `Assets/Scenes/Game.unity`

**Camera `Camera`**

| Prop | Value |
| --- | --- |
| Position | ['18.9', '24.4', '3.4'] |
| Rotation (quat) | ['0.59543306', '-0', '-0', '0.8034049'] |
| Projection | Perspective |
| FOV | 60 |
| Near/Far | 0.3 / 1000 |
| Clear flags | Solid Color |
| Background | rgba(0.550, 0.740, 0.820, 1.000) |
| Post-process (URP camera flag in YAML) | False |

**Lights**

| Name | Type | Color | Intensity | Shadows | Rotation (quat) |
| --- | --- | --- | --- | --- | --- |
| Directional Light | Directional | rgba(1.000, 0.970, 0.910, 1.000) | 1.25 | Soft | ['0.40821788', '-0.23456968', '0.10938163', '0.8754261'] |

**Environment:** ambientMode=1, ambientSky=['0.72', '0.84', '0.91'], fog=0, skybox=`Assets/Images/skyboc.mat`

**UIDocument**

| GO | UXML | PanelSettings |
| --- | --- | --- |
| `GameHUD` | `Assets/UI/Game/GameHUD.uxml` | `Assets/UI/Shared/BattlePlanPanelSettings.asset` |

**Volume components:** none as classic Volume YAML blocks in this parse; Game scene GUID-references `GameDaylight Volume Profile`. Title Global profile is used by `Color Grade.prefab`.

### `Assets/Scenes/JoinGame.unity`

**Camera `Main Camera`**

| Prop | Value |
| --- | --- |
| Position | ['0', '1', '-10'] |
| Rotation (quat) | ['0', '0', '0', '1'] |
| Projection | Perspective |
| FOV | 60 |
| Near/Far | 0.3 / 1000 |
| Clear flags | Don't Clear |
| Background | rgba(0.000, 0.604, 0.906, 1.000) |
| Post-process (URP camera flag in YAML) | False |

**Lights**

| Name | Type | Color | Intensity | Shadows | Rotation (quat) |
| --- | --- | --- | --- | --- | --- |
| Directional Light | Directional | rgba(1.000, 0.957, 0.839, 1.000) | 1 | Soft | ['0.40821788', '-0.23456968', '0.10938163', '0.8754261'] |

**Environment:** ambientMode=0, ambientSky=['0.212', '0.227', '0.259'], fog=0, skybox=`None`

**UIDocument**

| GO | UXML | PanelSettings |
| --- | --- | --- |
| `JoinGameUI` | `Assets/UI/Join/JoinGame.uxml` | `Assets/UI/Shared/BattlePlanPanelSettings.asset` |

**Volume components:** none as classic Volume YAML blocks in this parse; Game scene GUID-references `GameDaylight Volume Profile`. Title Global profile is used by `Color Grade.prefab`.

### 5.1 Game board / grid construction

| Fact | Value | Source |
| --- | --- | --- |
| Cell size (world) | **2.7** | `GameLoop.cellSize` |
| Board dimensions | **15 × 10** cells | `GridSystem.ColumnCount` / `RowCount` |
| World extent | `(14, 9) * 2.7` | `GameLoop` gridBounds |
| Floor / backdrop | **Scene-authored** in `Game.unity` (mats `Map_FloorDay`, `Map_FloorDayAlt`, `Map_Backdrop`, hazards, trim, glow pools) | Scene YAML material refs |
| `GridSystem.CreateGrid` | **Commented out** — runtime no longer stamps `GridCell` prefab | `GridSystem.cs` |
| Walls | **Runtime spawned** from static `GameLoop.wallLayout` (18 cells, mirrored) via `wallPrefab` → `Assets/Prefabs/Map/Wall.prefab` + Netcode | `GameLoop.StartGame` |
| Fog overlay | Runtime grid of `fogOverlayCellPrefab` (`FogOverlayCell.prefab`) with `BattlePlan/FogOverlay` MPB edges | `GameLoop` |
| Map preview | Rendered from the real board by `Battle Plan ▸ Art ▸ Capture Board Preview` (`ArenaCapture`) → `Assets/Images/MapPreview.png` at 1080 × 720; caption asserts `15 × 10` | Editor + smoke test |

---

## 6. Render pipeline & project settings

### 6.1 Quality levels → URP assets

| Quality name (UI) | QualitySettings index | URP asset | HDR | MSAA (URP) | Render scale | Soft shadows | Shared renderer |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Low | 0 | `Assets/Scripts/Renderer/Low.asset` | on | 1 | 0.37 | off | `URP_Renderer.asset` |
| Medium | 1 | `Assets/Scripts/Renderer/Medium.asset` | on | 1 | 1 | on | same |
| High (default `m_CurrentQuality: 2`) | 2 | `Assets/Scripts/Renderer/High.asset` | on | 8 | 2 | on | same |
| (base template) | — | `Assets/Scripts/Renderer/URP.asset` | on | 2 | 2 | on | same |

**`URP.asset` / High highlights:** main+additional shadow maps 4096, shadow distance 50, 2 cascades, soft shadow quality 3, color grading HDR, additional lights per object 8.

### 6.2 Renderer features (`Assets/Scripts/Renderer/URP_Renderer.asset`)

| Feature | Active | Settings asset | Notes |
| --- | --- | --- | --- |
| Linework Lite **FreeOutline** | yes | `Assets/Free Outline Settings.asset` | Only renderer feature listed |

**Free Outline Settings** (`Outline` child):

| Prop | Value |
| --- | --- |
| Rendering layer mask bits | `2` |
| Layer mask | everything (`4294967295`) |
| Color | rgba(0, 0.560, 1, 1) — cyan/blue outline |
| Occluded color | rgba(1, 0, 0, 1) (occlusion disabled) |
| Width | 3 |
| Extrusion method | 4 |
| Injection point | 600 |
| Blend mode | 0 |

No additional URP Render Objects features found on this renderer.

### 6.3 Volume profiles

#### `Assets/Settings/GameDaylight Volume Profile.asset` (active in Game scene)

| Override | Key values |
| --- | --- |
| Tonemapping | mode 2 |
| Bloom | threshold 1.15, intensity 0.35, scatter 0.62 |
| Vignette | color (0.18,0.25,0.29), intensity 0.025, smoothness 0.3 |
| Color Adjustments | postExposure 0.12, contrast 6, saturation 1 |
| Film Grain | intensity 0 |

#### `Assets/Settings/GameNoir Volume Profile.asset` (present, **not referenced by Game.unity**)

| Override | Key values |
| --- | --- |
| Tonemapping | mode 2 |
| Bloom | threshold 1.1, intensity 0.55, scatter 0.7 |
| Vignette | (0.02,0.03,0.06), intensity 0.08 |
| Color Adjustments | postExposure 0.35, contrast 8, sat -5, warm filter |
| Film Grain | intensity 0.1, response 0.8 |

#### `Assets/Scenes/Title Screen/Global Volume Profile.asset` (Color Grade prefab)

| Override | Key values |
| --- | --- |
| Shadows/Midtones/Highlights | shadows w=-0.049, midtones w=-0.059, highlights w=0.615 |
| White Balance | temperature -11 |
| Tonemapping | mode 2 |
| Color Curves | red/green/blue overrides on |

#### `Assets/DefaultVolumeProfile.asset`

Project-wide default with many override slots (Bloom, Vignette, ColorAdjustments, DoF, ChromaticAberration, etc.). Gameplay look is driven by GameDaylight / Title Global profiles.

### 6.4 Other project settings

| Setting | Value |
| --- | --- |
| Color space | Linear |
| Product | Battle Plan |
| Quality UI labels | Low / Medium / High (must match QualitySettings names — Title dropdown) |
| Lighting settings asset | `Assets/New Lighting Settings.lighting` |

---

## 7. Audio

### 7.1 Clips

| Path | Referenced by |
| --- | --- |
| `Assets/Music/Battle_Plan_Main_Menu trimmed.wav` | `Title Screen.unity` |
| `Assets/Music/Battle_Plan_Main_Menu.wav` | **Unreferenced** |
| `Assets/Music/Battle_Plan_Character_Selections.wav` | `Music.prefab` |
| `Assets/Music/Battle_plan_Settings.wav` | `HomeScreen.unity` |
| `Assets/Music/In_game.wav` | `Game.unity` |
| `Assets/Music/Button click.wav` | `AudioManager.prefab` → `buttonClick` |
| `Assets/Music/Character select click.wav` | `HomeScreen.unity` |

No other `.wav`/`.mp3`/`.ogg` under `Assets/`. **No AudioMixer assets** found.

### 7.2 Prefabs / manager API

| Prefab | Components | Notes |
| --- | --- | --- |
| `AudioManager.prefab` | Transform + `AudioManager` | `musicSource`/`SFXSource` **null**; only `buttonClick` assigned |
| `Music.prefab` | AudioSource | Clip=Character_Selections; volume 1; PlayOnAwake 1; Loop 0 |
| `SFX.prefab` | AudioSource | No clip; PlayOnAwake 1 |

**`Assets/Scripts/Camera/AudioManager.cs` public surface:**

| Member | Type |
| --- | --- |
| `musicSource` | AudioSource |
| `SFXSource` | AudioSource |
| `buttonClick` | AudioClip |

**No public Play methods.** Grep found **zero** call sites into `AudioManager` from gameplay scripts. Music is scene/prefab AudioSources, not a centralized event API. SFX redesign must invent wiring or extend this stub.

---

## 8. VFX / gameplay-visual scripts

### 8.1 `Assets/Scripts/VFX/`

| Script | Public API | Spawns / depends on |
| --- | --- | --- |
| `BeamVFX.cs` | `Create(parent,start,end,teamGlowColor)`, update/fade helpers | LineRenderers + `BattlePlan/EnergyBeam` |
| `ImpactShockwave.cs` | `Spawn(pos, color, maxRadius=…, duration=…)` | Quad + `BattlePlan/GroundGlow` + point light |
| `HitFlash.cs` | `Flash()`, `FlashTarget(go,…)`, Flash(duration,strength) | MPB white flash on renderers |
| `VisionConeVisual.cs` | weapon envelope setters; mesh rebuild | Fan mesh + `BattlePlan/VisionCone` / `FX_VisionCone.mat` |
| `SmokeScreenVisual.cs` | `Create(parent, cells, cellSize)` | Quads + `Sprites/Default` stain/puff mats |

### 8.2 Other visual creators

| Script | What it draws |
| --- | --- |
| `CameraEffects.cs` | `CameraShake` / `CameraShakeClientRpc` |
| `Shooting.cs` | Target-lock energy beam LineRenderer |
| `AreaLock.cs` | Server/client `BeamVFX`, shockwave, camera shake; `LaserGlow=(1,0.14,0.25)` |
| `Movement.cs` | Boost ground glow rings/streaks (`BoostColor` HDR cyan) |
| `PathRibbon.cs` | Planned move ribbons (slot colors from `PlanPathStyle`) |
| `AbilityPathIndicator.cs` | Ability path LineRenderers |
| `PlanMovement.cs` | Move overlays, preview lines/cylinders (`Sprites/Default`) |
| `GridSystem.cs` | Instantiates overlay prefabs for ranges |
| `GameLoop.cs` | Fog overlay grid, AOE/ability markers, KOTH hill tint, orange telegraph colors |
| `Health.cs` | HitFlash + ImpactShockwave on damage/death |
| `Grenade.cs` | Explosion particle prefab + orange shockwave + shake |
| `Pogo.cs` | Landing shockwave + shake |
| `Unit.cs` | Vision cone team tint setup |

### 8.3 Projectile visuals

- Prefabs under `Assets/Prefabs/Projectiles/` registered in DefaultNetworkPrefabs.
- Team bullets: Blue/Red mats with emission; sniper supers use `SniperSuperBullet.mat` (gold).
- Grenade: green mat; explosion: particle prefab; smoke canister: grey metal mat + `SmokeScreenVisual` for the screen body.
- Beams/lasers often bypass mesh bullets (`BeamVFX` / LineRenderer).

### 8.4 Hardcoded on-screen `Color` values in C# (palette-critical)

| File | Location | Value | Role |
| --- | --- | --- | --- |
| `PlanPathStyle.cs` | SlotColors[0..4] | (1,0.76,0.24), (0.32,0.80,1), (1,0.44,0.70), (0.52,0.93,0.44), (0.72,0.62,1) | Route identity per roster slot |
| `PlanPathStyle.cs` | GetBlockedEndColor | (1, 0.29, 0.31, α) | Illegal destination |
| `GameLoop.cs` | ~2241,2270,2289 | (1, 0.5, 0, 0.8/0.5/0.85) | Orange ability telegraph lines/markers |
| `GameLoop.cs` | ~2619 | (0.95, 0.64, 0.2) | Contested hill fallback |
| `GameHUDController.cs` | ~451–455 | blue/red/amber flash overlays | HUD damage/phase flash |
| `Health.cs` | ~149–150 | (0.22,0.78,1) / (1,0.23,0.33) | Team shockwave colors |
| `Unit.cs` | ~172–173 | blue/red translucent cone tints | Vision cone team colors |
| `AreaLock.cs` | LaserGlow | (1, 0.14, 0.25) | Magenta-red laser |
| `Movement.cs` | BoostColor | (0.08, 1.15, 1.35, 0.72) HDR | Movement boost FX |
| `Grenade.cs` | ~91 | (1, 0.77, 0) | Explosion shockwave |
| `Shooting.cs` | start/endAnimColor | white → `Color.red` | Lock-on ramp |
| `SmokeScreenVisual.cs` | StainColor / PuffColor | (0.62,0.68,0.72,0.55) / (0.80,0.84,0.87,0.45) | Smoke look |
| `BeamVFX.cs` / `HitFlash.cs` | white cores | `Color.white` | Additive cores / flash |
| `Game.unity` GameLoop | teamColors | ≈(0.065,0.528,0.925) / (0.925,0.109,0.109) | Authoritative team colors |
| Free Outline Settings | outline color | (0, 0.56, 1) | Selection outline |

---

## 9. Risk register

### 9.1 Touching X breaks Y

| Hazard | Why |
| --- | --- |
| Rename any UXML `name=` in smoke-test contracts | `UIToolkitAssetSmokeTests` + controller `RequireElement` fail |
| Rename USS classes used in C# (`hidden`, `button--selected`, `unit-card--*`, `planning-commit--*`, …) | Controllers toggle by string |
| Change `--toy-*` token names | Entire toybox + runtime theme + tests |
| Replace Rubik/Cascadia SDF assets or font URL strings | Font smoke tests + USS urls |
| Edit `MapPreview.png` without recapturing via menu, or change its importer size/NPOT settings | Smoke test asserts 1080 × 720 and continuous-tone content |
| Change Join stage away from `JoinToyboxStage.png` size 1579×885 | Dimension + path asserts |
| Replace NetworkPrefabs list entries / GUIDs casually | Netcode spawn hashes break multiplayer |
| Rename `Shader "BattlePlan/…"` strings | `Shader.Find` returns null → missing VFX |
| Change `GameLoop.cellSize` / 15×10 / wallLayout | Pathing, preview, wall tests, camera framing |
| Quality level rename away from Low/Medium/High | Title settings dropdown + ProjectSettings |
| Outline rendering layer bit `2` | FreeOutline won't see units if layers change |
| Team material array order on GameLoop | Team tint wrong for units |

### 9.2 Inconsistencies / stale surfaces today

| Issue | Detail |
| --- | --- |
| `Char_JumpsuitOrange.mat` is brown | Base ≈(0.38,0.33,0.25) — name says orange |
| `Char_TealAccent.mat` is grey-teal muted | Base ≈(0.24,0.29,0.31) — weak accent |
| `AudioManager` is a stub | No Play API; sources null; music wired ad hoc per scene |
| `Battle_Plan_Main_Menu.wav` unused | Trimmed variant used on Title instead |
| `GameNoir Volume Profile` unused | Daylight is what Game references |
| Legacy UI PNGs still in `Assets/Images/` | Buttons/logos from pre-toybox era |
| `BulletBlue.prefab` references BulletRed.mat too | Likely leftover assignment |
| Dual visual languages | Toybox UI (`--toy-*`) vs daylight tactical board vs HDR magenta beams |
| `GridSystem` CreateGrid dead | Board is scene-authored; easy to drift from consts |
| AOEoverlay.prefab uses `AttackOverlay.mat` | Name mismatch vs `AOEoverlay.mat` asset |
| Skybox asset named `skyboc.mat` | `Assets/Images/skyboc.mat` — typo filename, referenced by Game scene |

---

## Appendix A — Models (`Assets/Models and stuff/`)

FBX sources used by unit/projectile prefabs (folder name is literal):

- `Assets/Models and stuff/Battle Plan Shotgun 2.fbx`
- `Assets/Models and stuff/Battle Plan Shotgunner 2.fbx`
- `Assets/Models and stuff/Battle Plan character Pogostick Rider.fbx`
- `Assets/Models and stuff/Bullet.fbx`
- `Assets/Models and stuff/Commander.fbx`
- `Assets/Models and stuff/PathNode.fbx`
- `Assets/Models and stuff/Pistol.fbx`
- `Assets/Models and stuff/PogoRider.fbx`
- `Assets/Models and stuff/PogoRiderOld.fbx`
- `Assets/Models and stuff/Rifle.fbx`
- `Assets/Models and stuff/Shield.fbx`
- `Assets/Models and stuff/Shotgun.fbx`
- `Assets/Models and stuff/ShotgunBullet.fbx`
- `Assets/Models and stuff/Shotgunner.fbx`
- `Assets/Models and stuff/Sniper test.fbx`
- `Assets/Models and stuff/Sniper.fbx`
- `Assets/Models and stuff/SniperOld.fbx`
- `Assets/Models and stuff/Soldier.fbx`

## Appendix B — Environment textures

| Path | Size |
| --- | --- |
| `Assets/Textures/Environment/T_Concrete.png` | 256×256 |
| `Assets/Textures/Environment/T_Concrete_N.png` | 256×256 |
| `Assets/Textures/Environment/T_BrushedSteel.png` | 128×128 |

## Appendix C — How to re-enumerate

If this document falls behind:

1. GUID→path: scan `Assets/**/*.meta` for `guid:`.
2. Materials: read `m_Shader` guid + `_BaseColor`/`_EmissionColor`/`_Smoothness`/`_Metallic`/`_Surface`/`m_CustomRenderQueue` from each `.mat`.
3. Prefab material assignment: GUID hits inside `.prefab`/`.unity`.
4. UI contracts: regex `RequireElement<T>("…")`, `.Q<T>("…")`, `*ClassList*("…")` under `Assets/Scripts/Menus/`.
5. Network list: `Assets/DefaultNetworkPrefabs.asset` Prefab guids.

---

*Generated for the art redesign kickoff. Do not treat as an editable design brief for copy — see `UI-ContentInventory.md` for strings.*
