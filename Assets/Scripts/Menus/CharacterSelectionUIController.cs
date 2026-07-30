using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class CharacterSelectionUIController : NetworkBehaviour
{
    private const int UnitsPerPlayer = RosterRules.UnitsPerPlayer;
    private static readonly int[] BotRoster = GameLoop.DefaultBotRoster;

    [SerializeField]
    private UnitDatabase allUnits;

    [SerializeField]
    private VisualTreeAsset unitOptionTemplate;

    [SerializeField]
    private VisualTreeAsset selectedSlotTemplate;

    private readonly NetworkVariable<MatchOptions> replicatedOptions = new(MatchOptions.Default);
    private readonly NetworkVariable<int> confirmedHumanCount = new(0);
    private readonly Dictionary<ulong, int[]> teamSelections = new();
    private readonly int[] selectedUnits = CreateEmptyRoster();
    private readonly List<UnitOptionView> optionViews = new();
    private readonly List<SelectedSlotView> slotViews = new();

    private UIDocument document;
    private VisualElement root;
    private ScrollView rosterOptions;
    private VisualElement selectedRoster;
    private Label rosterInstruction;
    private Label rosterCountLabel;
    private Button confirmButton;
    private Label selectionStatus;
    private Label modeSummary;
    private Label opponentSummary;
    private Label fogSummary;
    private VisualElement mapPreview;
    private Label mapCaption;
    private Texture2D previewTexture;
    private MapDefinition shownMap;
    private bool localSelectionSubmitted;
    private bool sceneLoadRequested;
    private bool uiCallbacksRegistered;
    private bool networkCallbacksRegistered;
    private bool disconnectRecoveryStarted;
    private string localStatusOverride;

    private static int[] CreateEmptyRoster()
    {
        int[] roster = new int[UnitsPerPlayer];
        Array.Fill(roster, -1);
        return roster;
    }

    private void OnEnable()
    {
        document = GetComponent<UIDocument>();
        root = document != null ? document.rootVisualElement : null;
        if (root == null)
        {
            Debug.LogError("[CharacterSelectionUIController] UIDocument has no visual tree.");
            return;
        }

        CacheElements();
        RegisterUiCallbacks();
        BuildRosterOptions();
        BuildSelectedSlots();
        ConsoleUiNavigation.ConfigureButtons(root);
        UpdateSummary(IsSpawned ? replicatedOptions.Value : MatchOptions.Current);
        UpdateSelectionState();
        root.schedule.Execute(FocusFirstEnabledRosterOption);
    }

    private void OnDisable()
    {
        UnregisterUiCallbacks();
        DisposeGeneratedViews();
        ReleasePreviewTexture();
        shownMap = null;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        RegisterNetworkCallbacks();
        localSelectionSubmitted = false;
        localStatusOverride = null;
        sceneLoadRequested = false;
        disconnectRecoveryStarted = false;

        if (IsServer)
        {
            teamSelections.Clear();
            confirmedHumanCount.Value = 0;
            replicatedOptions.Value = MatchOptions.Current.Sanitized();
        }
        NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

        MatchOptions.SetCurrent(replicatedOptions.Value);
        UpdateSummary(replicatedOptions.Value);
        UpdateSelectionState();
        if (
            IsServer
            && !replicatedOptions.Value.Sanitized().IsBotMatch
            && NetworkManager.ConnectedClients.Count < 2
        )
        {
            BeginDisconnectRecovery("The other player disconnected.");
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        UnregisterNetworkCallbacks();
        teamSelections.Clear();
        sceneLoadRequested = false;
        base.OnNetworkDespawn();
    }

    private void CacheElements()
    {
        rosterOptions = RequireElement<ScrollView>("roster-options");
        selectedRoster = RequireElement<VisualElement>("selected-roster");
        rosterInstruction = RequireElement<Label>("roster-instruction");
        rosterCountLabel = RequireElement<Label>("roster-count-label");
        confirmButton = RequireElement<Button>("confirm-selection-button");
        selectionStatus = RequireElement<Label>("selection-status");
        modeSummary = RequireElement<Label>("mode-summary");
        opponentSummary = RequireElement<Label>("opponent-summary");
        fogSummary = RequireElement<Label>("fog-summary");
        mapPreview = RequireElement<VisualElement>("map-preview");
        mapCaption = RequireElement<Label>("map-caption");
        if (rosterInstruction != null)
        {
            rosterInstruction.text =
                $"Pick {UnitsPerPlayer}. Repeats are allowed. Select a filled slot to remove it.";
        }
        if (rosterCountLabel != null)
            rosterCountLabel.text = $"Pick {UnitsPerPlayer} units";
    }

    private T RequireElement<T>(string elementName)
        where T : VisualElement
    {
        T element = root.Q<T>(elementName);
        if (element == null)
        {
            Debug.LogError(
                $"[CharacterSelectionUIController] Missing required {typeof(T).Name} '{elementName}'."
            );
        }
        return element;
    }

    private void RegisterUiCallbacks()
    {
        if (uiCallbacksRegistered)
            return;

        if (confirmButton != null)
            confirmButton.clicked += ConfirmSelection;
        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        uiCallbacksRegistered = true;
    }

    private void UnregisterUiCallbacks()
    {
        if (!uiCallbacksRegistered)
            return;

        if (confirmButton != null)
            confirmButton.clicked -= ConfirmSelection;
        root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        uiCallbacksRegistered = false;
    }

    private void RegisterNetworkCallbacks()
    {
        if (networkCallbacksRegistered)
            return;
        replicatedOptions.OnValueChanged += OnOptionsChanged;
        confirmedHumanCount.OnValueChanged += OnConfirmedCountChanged;
        networkCallbacksRegistered = true;
    }

    private void UnregisterNetworkCallbacks()
    {
        if (!networkCallbacksRegistered)
            return;
        replicatedOptions.OnValueChanged -= OnOptionsChanged;
        confirmedHumanCount.OnValueChanged -= OnConfirmedCountChanged;
        networkCallbacksRegistered = false;
    }

    private void BuildRosterOptions()
    {
        foreach (UnitOptionView view in optionViews)
            view.Dispose();
        optionViews.Clear();
        rosterOptions?.Clear();

        if (rosterOptions == null)
            return;
        if (allUnits?.units == null || allUnits.units.Count == 0)
        {
            SetStatus("No units are available.", true);
            return;
        }
        RosterValidationResult catalogValidation = RosterRules.ValidateCatalog(allUnits.units);
        if (!catalogValidation.IsValid)
        {
            SetStatus(RosterRules.GetUserMessage(catalogValidation), true);
            return;
        }

        for (int i = 0; i < allUnits.units.Count; i++)
        {
            int unitIndex = i;
            UnitData data = allUnits.units[i];
            UnitOptionView view = CreateUnitOptionView(data, i);
            view.ClickAction = () => SelectUnit(unitIndex);
            view.Button.clicked += view.ClickAction;
            optionViews.Add(view);
            rosterOptions.Add(view.Root);
        }
    }

    private UnitOptionView CreateUnitOptionView(UnitData data, int index)
    {
        VisualElement viewRoot;
        Button button;
        if (unitOptionTemplate != null)
        {
            viewRoot = unitOptionTemplate.Instantiate();
            button = viewRoot.Q<Button>("unit-option-button");
        }
        else
        {
            button = BuildFallbackUnitOption();
            viewRoot = button;
        }

        if (button == null)
        {
            Debug.LogError(
                "[CharacterSelectionUIController] UnitOption template is missing 'unit-option-button'."
            );
            button = BuildFallbackUnitOption();
            viewRoot = button;
        }

        VisualElement portrait = button.Q<VisualElement>("unit-option-portrait");
        Label unitName = button.Q<Label>("unit-option-name");
        Label description = button.Q<Label>("unit-option-description");
        Label ability = button.Q<Label>("unit-option-ability");
        Label optionStatus = button.Q<Label>("unit-option-status");
        if (optionStatus == null)
        {
            optionStatus = new Label { name = "unit-option-status" };
            optionStatus.AddToClassList("unit-option__status");
            button.Add(optionStatus);
        }

        button.name = $"unit-option-{index}";
        button.tooltip = data != null ? $"Add {data.unitName} to the crew" : "Add unit";
        if (unitName != null)
            unitName.text = data != null ? data.unitName : "Unknown unit";
        if (description != null)
            description.text = data != null ? data.unitDescription : "Unit data unavailable.";
        if (ability != null)
            ability.text =
                data != null && !string.IsNullOrWhiteSpace(data.abilityName)
                    ? data.abilityName
                    : "Move only";
        SetBackgroundImage(portrait, data != null ? data.unitSprite : null);

        return new UnitOptionView(viewRoot, button, data, optionStatus);
    }

    private static Button BuildFallbackUnitOption()
    {
        Button button = new() { name = "unit-option-button" };
        button.AddToClassList("unit-option");

        VisualElement portrait = new() { name = "unit-option-portrait" };
        portrait.AddToClassList("unit-option__portrait");
        portrait.pickingMode = PickingMode.Ignore;

        VisualElement copy = new();
        copy.AddToClassList("unit-option__copy");
        copy.pickingMode = PickingMode.Ignore;

        Label unitName = new("Unit") { name = "unit-option-name" };
        unitName.AddToClassList("unit-option__name");
        Label description = new() { name = "unit-option-description" };
        description.AddToClassList("unit-option__description");

        VisualElement abilityRow = new();
        abilityRow.AddToClassList("unit-option__ability-row");
        VisualElement abilityMark = new();
        abilityMark.AddToClassList("unit-option__ability-mark");
        Label ability = new("Ability") { name = "unit-option-ability" };
        ability.AddToClassList("unit-option__ability");
        abilityRow.Add(abilityMark);
        abilityRow.Add(ability);

        copy.Add(unitName);
        copy.Add(description);
        copy.Add(abilityRow);
        button.Add(portrait);
        button.Add(copy);
        return button;
    }

    private void BuildSelectedSlots()
    {
        foreach (SelectedSlotView view in slotViews)
            view.Dispose();
        slotViews.Clear();
        selectedRoster?.Clear();

        if (selectedRoster == null)
            return;

        for (int i = 0; i < UnitsPerPlayer; i++)
        {
            int slotIndex = i;
            SelectedSlotView view = CreateSelectedSlotView(i);
            view.ClickAction = () => ClearSlot(slotIndex);
            view.Button.clicked += view.ClickAction;
            slotViews.Add(view);
            selectedRoster.Add(view.Root);
        }
    }

    private SelectedSlotView CreateSelectedSlotView(int index)
    {
        VisualElement viewRoot;
        Button button;
        if (selectedSlotTemplate != null)
        {
            viewRoot = selectedSlotTemplate.Instantiate();
            button = viewRoot.Q<Button>("selected-slot-button");
        }
        else
        {
            button = BuildFallbackSelectedSlot();
            viewRoot = button;
        }

        if (button == null)
        {
            Debug.LogError(
                "[CharacterSelectionUIController] SelectedSlot template is missing 'selected-slot-button'."
            );
            button = BuildFallbackSelectedSlot();
            viewRoot = button;
        }

        button.name = $"selected-slot-{index}";
        Label indexLabel = button.Q<Label>("selected-slot-index");
        if (indexLabel != null)
            indexLabel.text = (index + 1).ToString();

        return new SelectedSlotView(
            viewRoot,
            button,
            button.Q<VisualElement>("selected-slot-portrait"),
            button.Q<Label>("selected-slot-name"),
            button.Q<Label>("selected-slot-detail")
        );
    }

    private static Button BuildFallbackSelectedSlot()
    {
        Button button = new() { name = "selected-slot-button" };
        button.AddToClassList("selected-slot");

        Label index = new("1") { name = "selected-slot-index" };
        index.AddToClassList("selected-slot__index");
        VisualElement portrait = new() { name = "selected-slot-portrait" };
        portrait.AddToClassList("selected-slot__portrait");
        VisualElement copy = new();
        copy.AddToClassList("selected-slot__copy");
        Label unitName = new("Open slot") { name = "selected-slot-name" };
        unitName.AddToClassList("selected-slot__name");
        Label detail = new("Choose a unit") { name = "selected-slot-detail" };
        detail.AddToClassList("selected-slot__detail");

        copy.Add(unitName);
        copy.Add(detail);
        button.Add(index);
        button.Add(portrait);
        button.Add(copy);
        return button;
    }

    private void DisposeGeneratedViews()
    {
        foreach (UnitOptionView view in optionViews)
            view.Dispose();
        foreach (SelectedSlotView view in slotViews)
            view.Dispose();
        optionViews.Clear();
        slotViews.Clear();
    }

    private void SelectUnit(int unitIndex)
    {
        if (localSelectionSubmitted)
            return;

        if (allUnits?.units == null || unitIndex < 0 || unitIndex >= allUnits.units.Count)
        {
            localStatusOverride =
                "That unit is no longer available. Choose another unit.";
            UpdateSelectionState();
            return;
        }

        if (!RosterRules.IsUnitEligible(allUnits.units, unitIndex))
        {
            localStatusOverride =
                "That unit is unavailable for deployment. Choose another unit.";
            UpdateSelectionState();
            return;
        }

        int emptySlot = Array.IndexOf(selectedUnits, -1);
        if (emptySlot < 0)
        {
            localStatusOverride = "Crew full. Remove a unit before choosing another.";
            UpdateSelectionState();
            return;
        }

        localStatusOverride = null;
        selectedUnits[emptySlot] = unitIndex;
        UpdateSelectionState();
        if (Array.IndexOf(selectedUnits, -1) < 0)
            confirmButton?.Focus();
    }

    private void ClearSlot(int slotIndex)
    {
        if (
            localSelectionSubmitted
            || slotIndex < 0
            || slotIndex >= selectedUnits.Length
            || selectedUnits[slotIndex] < 0
        )
        {
            return;
        }

        localStatusOverride = null;
        selectedUnits[slotIndex] = -1;
        UpdateSelectionState();
        root?.schedule.Execute(FocusFirstEnabledRosterOption);
    }

    private void FocusFirstEnabledRosterOption()
    {
        optionViews.FirstOrDefault(view => view.CanReceiveFocus)?.Button?.Focus();
    }

    private void UpdateSelectionState()
    {
        bool selectionComplete = selectedUnits.All(index => index >= 0);
        RosterValidationResult validation = selectionComplete
            ? RosterRules.Validate(selectedUnits, allUnits?.units)
            : RosterValidationResult.Invalid(RosterValidationReason.IncorrectUnitCount);
        bool selectionValid = selectionComplete && validation.IsValid;
        bool canEdit = !localSelectionSubmitted && !sceneLoadRequested;

        for (int unitIndex = 0; unitIndex < optionViews.Count; unitIndex++)
        {
            int pickedCount = selectedUnits.Count(index => index == unitIndex);
            bool eligible = RosterRules.IsUnitEligible(allUnits?.units, unitIndex);
            optionViews[unitIndex].Configure(
                pickedCount,
                eligible,
                canEdit && !selectionComplete && eligible
            );
        }

        for (int i = 0; i < slotViews.Count && i < selectedUnits.Length; i++)
        {
            int unitIndex = selectedUnits[i];
            bool filled =
                allUnits?.units != null && unitIndex >= 0 && unitIndex < allUnits.units.Count;
            UnitData data = filled ? allUnits.units[unitIndex] : null;
            slotViews[i].Configure(data, filled, canEdit);
        }

        if (confirmButton != null)
        {
            confirmButton.SetEnabled(IsSpawned && IsClient && selectionValid && canEdit);
        }
        UpdateStatusText();
    }

    private void ConfirmSelection()
    {
        if (localSelectionSubmitted || !IsSpawned || !IsClient)
            return;

        RosterValidationResult validation = RosterRules.Validate(
            selectedUnits,
            allUnits?.units
        );
        if (!validation.IsValid)
        {
            localStatusOverride = RosterRules.GetUserMessage(validation);
            UpdateSelectionState();
            return;
        }

        localSelectionSubmitted = true;
        localStatusOverride = null;
        UpdateSelectionState();
        ConfirmSelectionServerRpc((int[])selectedUnits.Clone());
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConfirmSelectionServerRpc(
        int[] submittedUnits,
        ServerRpcParams rpcParams = default
    )
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(sender))
        {
            return;
        }

        if (sceneLoadRequested)
        {
            RejectSelection(
                sender,
                $"That crew is not valid. Choose {UnitsPerPlayer} units again."
            );
            return;
        }

        RosterValidationResult validation = RosterRules.Validate(
            submittedUnits,
            allUnits?.units
        );
        if (!validation.IsValid)
        {
            RejectSelection(sender, RosterRules.GetUserMessage(validation));
            return;
        }

        MatchOptions options = replicatedOptions.Value.Sanitized();
        if (options.IsBotMatch && sender != NetworkManager.ServerClientId)
        {
            RejectSelection(sender, "Only the host selects a crew in an AI match.");
            return;
        }

        teamSelections[sender] = (int[])submittedUnits.Clone();
        confirmedHumanCount.Value = teamSelections.Count;

        int expectedHumans = options.IsBotMatch ? 1 : 2;
        if (teamSelections.Count >= expectedHumans)
            FinalizeTeams(options);
    }

    private void RejectSelection(ulong clientId, string reason)
    {
        if (NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(clientId))
        {
            return;
        }
        SelectionRejectedClientRpc(reason, NetworkHelper.ToClient(clientId));
    }

    [ClientRpc]
    private void SelectionRejectedClientRpc(
        string reason,
        ClientRpcParams clientRpcParams = default
    )
    {
        localSelectionSubmitted = false;
        localStatusOverride = reason;
        UpdateSelectionState();
        confirmButton?.Focus();
    }

    private void FinalizeTeams(MatchOptions options)
    {
        if (sceneLoadRequested || NetworkManager == null)
            return;

        ulong hostId = NetworkManager.ServerClientId;
        if (!teamSelections.TryGetValue(hostId, out int[] hostRoster))
            return;
        if (!RevalidateStoredSelection(hostId, hostRoster))
            return;

        int[] opponentRoster;
        ulong opponentId;
        if (options.IsBotMatch)
        {
            RosterValidationResult botValidation = RosterRules.Validate(
                BotRoster,
                allUnits?.units
            );
            if (!botValidation.IsValid)
            {
                DeploymentFailedClientRpc(
                    $"The AI crew could not be created. {RosterRules.GetUserMessage(botValidation)}"
                );
                return;
            }
            opponentId = GameLoop.BotParticipantId;
            opponentRoster = BotRoster;
        }
        else
        {
            opponentId = teamSelections
                .Keys.Where(clientId => clientId != hostId)
                .OrderBy(clientId => clientId)
                .FirstOrDefault();
            if (opponentId == hostId || !teamSelections.TryGetValue(opponentId, out opponentRoster))
            {
                return;
            }
            if (!RevalidateStoredSelection(opponentId, opponentRoster))
                return;
        }

        sceneLoadRequested = true;
        GameLoop.ResetMatchState();
        GameLoop.ConfigureTeam(GameLoop.HostTeamIndex, hostId, hostRoster);
        GameLoop.ConfigureTeam(GameLoop.OpponentTeamIndex, opponentId, opponentRoster);
        NetworkManager.SceneManager.LoadScene("Game", LoadSceneMode.Single);
    }

    private bool RevalidateStoredSelection(ulong clientId, int[] roster)
    {
        RosterValidationResult validation = RosterRules.Validate(roster, allUnits?.units);
        if (validation.IsValid)
            return true;

        teamSelections.Remove(clientId);
        confirmedHumanCount.Value = teamSelections.Count;
        RejectSelection(clientId, RosterRules.GetUserMessage(validation));
        return false;
    }

    [ClientRpc]
    private void DeploymentFailedClientRpc(string reason, ClientRpcParams clientRpcParams = default)
    {
        localSelectionSubmitted = false;
        localStatusOverride = reason;
        UpdateSelectionState();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager == null || disconnectRecoveryStarted)
            return;

        if (!IsServer)
        {
            BeginDisconnectRecovery("The host disconnected.");
            return;
        }

        teamSelections.Remove(clientId);
        confirmedHumanCount.Value = teamSelections.Count;
        if (
            replicatedOptions.Value.Sanitized().IsBotMatch
            || clientId == NetworkManager.ServerClientId
        )
        {
            return;
        }

        BeginDisconnectRecovery("The other player disconnected.");
    }

    private void BeginDisconnectRecovery(string reason)
    {
        if (disconnectRecoveryStarted)
            return;

        disconnectRecoveryStarted = true;
        sceneLoadRequested = true;
        localStatusOverride = $"{reason} Returning to match setup...";
        UpdateSelectionState();
        StartCoroutine(ReturnToJoinGameAfterShutdown());
    }

    private IEnumerator ReturnToJoinGameAfterShutdown()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening && !manager.ShutdownInProgress)
            manager.Shutdown();

        while (manager != null && (manager.IsListening || manager.ShutdownInProgress))
        {
            yield return null;
        }

        MatchOptions.Reset();
        GameLoop.ResetMatchState();
        SceneManager.LoadScene("JoinGame");
    }

    private void OnOptionsChanged(MatchOptions previousValue, MatchOptions newValue)
    {
        MatchOptions.SetCurrent(newValue);
        UpdateSummary(newValue);
        UpdateStatusText();
    }

    private void OnConfirmedCountChanged(int previousValue, int newValue)
    {
        UpdateStatusText();
    }

    private void UpdateSummary(MatchOptions options)
    {
        options = options.Sanitized();
        if (modeSummary != null)
            modeSummary.text = options.GameModeDisplayName;
        if (opponentSummary != null)
            opponentSummary.text = options.IsBotMatch ? "AI" : "Player";
        if (fogSummary != null)
            fogSummary.text = options.fogOfWar ? "Fog on" : "Fog off";
        UpdateMapPanel(options.Map);
    }

    /// <summary>
    /// Draws the board the lobby actually chose. The UXML carries a baked thumbnail so the screen
    /// is never blank while this runs, but leaving it in place would show Concourse's cover on
    /// every map.
    /// </summary>
    private void UpdateMapPanel(MapDefinition map)
    {
        if (mapCaption != null)
            mapCaption.text = $"{map.DisplayName} · {GridSystem.ColumnCount} × {GridSystem.RowCount}";

        if (mapPreview == null || map == shownMap)
            return;

        Texture2D next = MapPreviewImage.CreateTexture(map);
        mapPreview.style.backgroundImage = new StyleBackground(next);
        ReleasePreviewTexture();
        previewTexture = next;
        shownMap = map;
    }

    private void ReleasePreviewTexture()
    {
        if (previewTexture == null)
            return;
        Destroy(previewTexture);
        previewTexture = null;
    }

    private void UpdateStatusText()
    {
        if (selectionStatus == null)
            return;

        if (!string.IsNullOrEmpty(localStatusOverride))
        {
            SetStatus(localStatusOverride, true);
            return;
        }

        if (!IsSpawned)
        {
            SetStatus("Connecting to match...", false);
            return;
        }

        if (!localSelectionSubmitted)
        {
            int selectedCount = selectedUnits.Count(index => index >= 0);
            SetStatus($"{selectedCount} / {UnitsPerPlayer} selected", false);
            return;
        }

        int expected = MatchOptions.Current.IsBotMatch ? 1 : 2;
        int confirmed = confirmedHumanCount.Value;
        SetStatus(
            confirmed >= expected
                ? "Deploying..."
                : $"Waiting for players ({confirmed} / {expected})",
            false
        );
    }

    private void SetStatus(string message, bool isError)
    {
        if (selectionStatus == null)
            return;
        selectionStatus.text = message;
        selectionStatus.EnableInClassList("label--danger", isError);
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

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        float width = evt.newRect.width;
        float height = evt.newRect.height;
        root.EnableInClassList("compact", width < 1500f);
        root.EnableInClassList("narrow", width < 1000f);
        root.EnableInClassList("phone", width < 680f);
        root.EnableInClassList("short", height < 800f);
    }

    private sealed class UnitOptionView : IDisposable
    {
        public VisualElement Root { get; }
        public Button Button { get; }
        public bool CanReceiveFocus { get; private set; }
        public Action ClickAction { get; set; }

        private readonly UnitData data;
        private readonly Label optionStatus;

        public UnitOptionView(
            VisualElement root,
            Button button,
            UnitData data,
            Label optionStatus
        )
        {
            Root = root;
            Button = button;
            this.data = data;
            this.optionStatus = optionStatus;
        }

        public void Configure(int pickedCount, bool eligible, bool canChoose)
        {
            bool selected = pickedCount > 0;
            Root.EnableInClassList("unit-option--selected", selected);
            Root.EnableInClassList("unit-option--unavailable", !eligible);
            Button.EnableInClassList("unit-option--selected", selected);
            Button.EnableInClassList("unit-option--unavailable", !eligible);
            CanReceiveFocus = canChoose;
            Button.SetEnabled(canChoose);

            if (optionStatus != null)
            {
                optionStatus.text = !eligible
                    ? "Unavailable"
                    : (pickedCount > 1 ? $"Selected \u00d7{pickedCount}" : (selected ? "Selected" : string.Empty));
                optionStatus.EnableInClassList("hidden", eligible && !selected);
            }

            string unitName =
                data != null && !string.IsNullOrWhiteSpace(data.unitName) ? data.unitName : "unit";
            Button.tooltip = !eligible
                ? $"{unitName} is unavailable for deployment"
                : (
                    canChoose
                        ? (selected ? $"Add another {unitName} to the crew" : $"Add {unitName} to the crew")
                        : "Crew selection is locked"
                );
        }

        public void Dispose()
        {
            if (Button != null && ClickAction != null)
                Button.clicked -= ClickAction;
            ClickAction = null;
        }
    }

    private sealed class SelectedSlotView : IDisposable
    {
        public VisualElement Root { get; }
        public Button Button { get; }
        public Action ClickAction { get; set; }

        private readonly VisualElement portrait;
        private readonly Label unitName;
        private readonly Label detail;

        public SelectedSlotView(
            VisualElement root,
            Button button,
            VisualElement portrait,
            Label unitName,
            Label detail
        )
        {
            Root = root;
            Button = button;
            this.portrait = portrait;
            this.unitName = unitName;
            this.detail = detail;
        }

        public void Configure(UnitData data, bool filled, bool canEdit)
        {
            Button.EnableInClassList("selected-slot--filled", filled);
            Button.SetEnabled(filled && canEdit);
            Button.tooltip =
                filled && data != null
                    ? $"Remove {data.unitName} from the crew"
                    : "Open crew slot";
            if (unitName != null)
                unitName.text = filled && data != null ? data.unitName : "Open slot";
            if (detail != null)
            {
                detail.text =
                    filled && data != null && !string.IsNullOrWhiteSpace(data.abilityName)
                        ? data.abilityName
                        : "Choose a unit";
            }
            SetBackgroundImage(portrait, filled && data != null ? data.unitSprite : null);
        }

        public void Dispose()
        {
            if (Button != null && ClickAction != null)
                Button.clicked -= ClickAction;
            ClickAction = null;
        }
    }
}
