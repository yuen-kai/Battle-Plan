using System;
using System.Collections;
using System.Threading;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class JoinGameUIController : MonoBehaviour
{
    private const string LocalMultiplayerAddress = "127.0.0.1";
    private const ushort LocalMultiplayerPort = 7777;
    private const long CaretBlinkIntervalMilliseconds = 500;
    private const string CaretHiddenClass = "text-field--caret-hidden";

    private enum PanelState
    {
        Create,
        Join,
    }

    private enum NetworkOperationKind
    {
        None,
        Host,
        Client,
    }

    private UIDocument document;
    private VisualElement root;
    private VisualElement createPanel;
    private VisualElement joinPanel;
    private VisualElement relayCodePanel;
    private VisualElement localMultiplayerRow;
    private Label connectionCodeHeading;
    private Button showCreateButton;
    private Button showJoinButton;
    private Button titleButton;
    private Button eliminationButton;
    private Button kingButton;
    private Button flagButton;
    private Button createMatchButton;
    private Button joinMatchButton;
    private Button cancelHostButton;
    private Button cancelJoinButton;
    private Button playerOpponentButton;
    private Button aiOpponentButton;
    private Toggle fogToggle;
    private Toggle localMultiplayerToggle;
    private TextField joinCodeInput;
    private Label relayCodeLabel;
    private Label relayStatusLabel;
    private Label createErrorLabel;
    private Label joinStatusLabel;
    private IVisualElementScheduledItem joinCaretBlink;

    private MatchOptions pendingOptions;
    private PanelState panelState;
    private NetworkOperationKind activeOperationKind;
    private CancellationTokenSource networkCancellation;
    private NetworkManager callbackNetworkManager;
    private Action<ulong> clientConnectedCallback;
    private Action<ulong> clientDisconnectedCallback;
    private Action transportFailureCallback;
    private Coroutine networkStopCoroutine;
    private bool networkStartInProgress;
    private bool callbacksRegistered;
    private bool networkCallbacksRegistered;
    private int networkOperationVersion;
    private static string directAddress;
    private static ushort directPort;
    private static string directListenAddress;
    private static bool directUseWebSockets;
    private static UnityTransport directDefaultsSource;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetTransportDefaultsCache()
    {
        directAddress = null;
        directPort = 0;
        directListenAddress = null;
        directUseWebSockets = false;
        directDefaultsSource = null;
    }

    private void OnEnable()
    {
        document = GetComponent<UIDocument>();
        root = document != null ? document.rootVisualElement : null;
        if (root == null)
        {
            Debug.LogError("[JoinGameUIController] UIDocument has no visual tree.");
            return;
        }

        CacheElements();
        ConsoleUiNavigation.ConfigureButtons(root);
        CacheTransportDefaults();
        RegisterCallbacks();
        ConfigureInitialState();
        ShowPanel(PanelState.Create, true);
    }

    private void OnDisable()
    {
        networkOperationVersion++;
        CancelAsyncNetworkWork();
        NetworkManager networkManager = NetworkManager.Singleton;
        bool networkIsRunning =
            networkManager != null
            && (
                networkManager.ShutdownInProgress
                || networkManager.IsListening
                || networkManager.IsClient
                || networkManager.IsServer
            );
        if (!networkIsRunning)
        {
            RestoreDirectTransport();
            ClearMppmDirective();
        }
        networkStartInProgress = false;
        activeOperationKind = NetworkOperationKind.None;
        networkStopCoroutine = null;
        UnregisterNetworkCallbacks();
        UnregisterCallbacks();
    }

    private void CacheElements()
    {
        createPanel = RequireElement<VisualElement>("create-panel");
        joinPanel = RequireElement<VisualElement>("join-panel");
        relayCodePanel = RequireElement<VisualElement>("relay-code-panel");
        localMultiplayerRow = RequireElement<VisualElement>("local-multiplayer-row");
        connectionCodeHeading = RequireElement<Label>("connection-code-heading");
        showCreateButton = RequireElement<Button>("show-create-button");
        showJoinButton = RequireElement<Button>("show-join-button");
        titleButton = RequireElement<Button>("title-button");
        eliminationButton = RequireElement<Button>("elimination-button");
        kingButton = RequireElement<Button>("king-button");
        flagButton = RequireElement<Button>("flag-button");
        createMatchButton = RequireElement<Button>("create-match-button");
        joinMatchButton = RequireElement<Button>("join-match-button");
        cancelHostButton = RequireElement<Button>("cancel-host-button");
        cancelJoinButton = RequireElement<Button>("cancel-join-button");
        playerOpponentButton = RequireElement<Button>("player-opponent-button");
        aiOpponentButton = RequireElement<Button>("ai-opponent-button");
        fogToggle = RequireElement<Toggle>("fog-toggle");
        localMultiplayerToggle = RequireElement<Toggle>("local-multiplayer-toggle");
        joinCodeInput = RequireElement<TextField>("join-code-input");
        relayCodeLabel = RequireElement<Label>("relay-code-label");
        relayStatusLabel = RequireElement<Label>("relay-status-label");
        createErrorLabel = RequireElement<Label>("create-error-label");
        joinStatusLabel = RequireElement<Label>("join-status-label");
    }

    private T RequireElement<T>(string elementName)
        where T : VisualElement
    {
        T element = root.Q<T>(elementName);
        if (element == null)
        {
            Debug.LogError(
                $"[JoinGameUIController] Missing required {typeof(T).Name} '{elementName}'."
            );
        }
        return element;
    }

    private void RegisterCallbacks()
    {
        if (callbacksRegistered)
            return;

        if (showCreateButton != null)
            showCreateButton.clicked += ShowCreate;
        if (showJoinButton != null)
            showJoinButton.clicked += ShowJoin;
        if (titleButton != null)
            titleButton.clicked += NavigateBack;
        if (eliminationButton != null)
            eliminationButton.clicked += SelectEliminationMode;
        if (kingButton != null)
            kingButton.clicked += SelectKingOfTheHillMode;
        if (playerOpponentButton != null)
            playerOpponentButton.clicked += SelectPlayerOpponent;
        if (aiOpponentButton != null)
            aiOpponentButton.clicked += SelectAiOpponent;
        if (createMatchButton != null)
            createMatchButton.clicked += CreateMatch;
        if (joinMatchButton != null)
            joinMatchButton.clicked += JoinMatch;
        if (cancelHostButton != null)
            cancelHostButton.clicked += CancelHost;
        if (cancelJoinButton != null)
            cancelJoinButton.clicked += CancelJoin;
        if (fogToggle != null)
            fogToggle.RegisterValueChangedCallback(OnFogChanged);
        if (localMultiplayerToggle != null)
        {
            localMultiplayerToggle.RegisterValueChangedCallback(OnLocalMultiplayerChanged);
        }
        if (joinCodeInput != null)
        {
            joinCodeInput.RegisterValueChangedCallback(OnJoinCodeChanged);
            joinCodeInput.RegisterCallback<FocusInEvent>(OnJoinCodeFocusIn);
            joinCodeInput.RegisterCallback<FocusOutEvent>(OnJoinCodeFocusOut);
            joinCodeInput.RegisterCallback<KeyDownEvent>(
                OnJoinCodeKeyDown,
                TrickleDown.TrickleDown
            );
            joinCodeInput.RegisterCallback<PointerDownEvent>(
                OnJoinCodePointerDown,
                TrickleDown.TrickleDown
            );
            ConfigureJoinCaretBlink();
        }

        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        callbacksRegistered = true;
    }

    private void UnregisterCallbacks()
    {
        PauseJoinCaretBlink();
        if (!callbacksRegistered)
            return;

        if (showCreateButton != null)
            showCreateButton.clicked -= ShowCreate;
        if (showJoinButton != null)
            showJoinButton.clicked -= ShowJoin;
        if (titleButton != null)
            titleButton.clicked -= NavigateBack;
        if (eliminationButton != null)
            eliminationButton.clicked -= SelectEliminationMode;
        if (kingButton != null)
            kingButton.clicked -= SelectKingOfTheHillMode;
        if (playerOpponentButton != null)
            playerOpponentButton.clicked -= SelectPlayerOpponent;
        if (aiOpponentButton != null)
            aiOpponentButton.clicked -= SelectAiOpponent;
        if (createMatchButton != null)
            createMatchButton.clicked -= CreateMatch;
        if (joinMatchButton != null)
            joinMatchButton.clicked -= JoinMatch;
        if (cancelHostButton != null)
            cancelHostButton.clicked -= CancelHost;
        if (cancelJoinButton != null)
            cancelJoinButton.clicked -= CancelJoin;
        if (fogToggle != null)
            fogToggle.UnregisterValueChangedCallback(OnFogChanged);
        if (localMultiplayerToggle != null)
        {
            localMultiplayerToggle.UnregisterValueChangedCallback(OnLocalMultiplayerChanged);
        }
        if (joinCodeInput != null)
        {
            joinCodeInput.UnregisterValueChangedCallback(OnJoinCodeChanged);
            joinCodeInput.UnregisterCallback<FocusInEvent>(OnJoinCodeFocusIn);
            joinCodeInput.UnregisterCallback<FocusOutEvent>(OnJoinCodeFocusOut);
            joinCodeInput.UnregisterCallback<KeyDownEvent>(
                OnJoinCodeKeyDown,
                TrickleDown.TrickleDown
            );
            joinCodeInput.UnregisterCallback<PointerDownEvent>(
                OnJoinCodePointerDown,
                TrickleDown.TrickleDown
            );
        }

        root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        callbacksRegistered = false;
    }

    private void RegisterNetworkCallbacks(NetworkManager networkManager, int operationVersion)
    {
        if (networkManager == null)
            return;

        UnregisterNetworkCallbacks();
        callbackNetworkManager = networkManager;
        clientConnectedCallback = clientId => OnClientConnected(operationVersion, clientId);
        clientDisconnectedCallback = clientId => OnClientDisconnected(operationVersion, clientId);
        transportFailureCallback = () => OnTransportFailure(operationVersion);
        callbackNetworkManager.OnClientConnectedCallback += clientConnectedCallback;
        callbackNetworkManager.OnClientDisconnectCallback += clientDisconnectedCallback;
        callbackNetworkManager.OnTransportFailure += transportFailureCallback;
        networkCallbacksRegistered = true;
    }

    private void UnregisterNetworkCallbacks()
    {
        if (!networkCallbacksRegistered || callbackNetworkManager == null)
        {
            callbackNetworkManager = null;
            clientConnectedCallback = null;
            clientDisconnectedCallback = null;
            transportFailureCallback = null;
            networkCallbacksRegistered = false;
            return;
        }

        callbackNetworkManager.OnClientConnectedCallback -= clientConnectedCallback;
        callbackNetworkManager.OnClientDisconnectCallback -= clientDisconnectedCallback;
        callbackNetworkManager.OnTransportFailure -= transportFailureCallback;
        callbackNetworkManager = null;
        clientConnectedCallback = null;
        clientDisconnectedCallback = null;
        transportFailureCallback = null;
        networkCallbacksRegistered = false;
    }

    private void ConfigureInitialState()
    {
        pendingOptions = MatchOptions.Current.Sanitized();

        eliminationButton?.SetEnabled(true);
        kingButton?.SetEnabled(true);
        flagButton?.SetEnabled(false);
        fogToggle?.SetValueWithoutNotify(pendingOptions.fogOfWar);
        joinCodeInput?.SetValueWithoutNotify(string.Empty);
        localMultiplayerToggle?.SetValueWithoutNotify(false);
        SetGameMode(pendingOptions.gameMode);
        SetOpponent(pendingOptions.opponentType);
        SetCreateStatus(string.Empty, false);
        SetJoinStatus(string.Empty, false);
        ResetRelayState();
        ResetJoinControls();
    }

    private void ShowCreate()
    {
        ShowPanel(PanelState.Create, true);
    }

    private void ShowJoin()
    {
        ShowPanel(PanelState.Join, true);
    }

    private void ShowPanel(PanelState nextState, bool moveFocus)
    {
        if (networkStartInProgress)
            return;

        panelState = nextState;
        createPanel?.EnableInClassList("hidden", nextState != PanelState.Create);
        joinPanel?.EnableInClassList("hidden", nextState != PanelState.Join);
        showCreateButton?.EnableInClassList("button--selected", nextState == PanelState.Create);
        showJoinButton?.EnableInClassList("button--selected", nextState == PanelState.Join);

        if (!moveFocus)
            return;

        root.schedule.Execute(() =>
        {
            if (nextState == PanelState.Create)
                playerOpponentButton?.Focus();
            else
                joinCodeInput?.Focus();
        });
    }

    private void SelectPlayerOpponent()
    {
        SetOpponent(OpponentType.Player);
    }

    private void SelectEliminationMode()
    {
        SetGameMode(GameMode.Elimination);
    }

    private void SelectKingOfTheHillMode()
    {
        SetGameMode(GameMode.KingOfTheHill);
    }

    private void SetGameMode(GameMode gameMode)
    {
        pendingOptions.gameMode = gameMode;
        pendingOptions = pendingOptions.Sanitized();

        eliminationButton?.EnableInClassList(
            "button--selected",
            pendingOptions.gameMode == GameMode.Elimination
        );
        kingButton?.EnableInClassList(
            "button--selected",
            pendingOptions.gameMode == GameMode.KingOfTheHill
        );
        SetCreateStatus(string.Empty, false);
    }

    private void SelectAiOpponent()
    {
        SetOpponent(OpponentType.AI);
    }

    private void SetOpponent(OpponentType opponentType)
    {
        pendingOptions.opponentType = opponentType;
        pendingOptions = pendingOptions.Sanitized();

        bool playerSelected = pendingOptions.opponentType == OpponentType.Player;
        playerOpponentButton?.EnableInClassList("button--selected", playerSelected);
        aiOpponentButton?.EnableInClassList("button--selected", !playerSelected);
        if (!playerSelected)
        {
            localMultiplayerToggle?.SetValueWithoutNotify(false);
            ClearMppmDirective();
        }

        UpdateLocalMultiplayerAvailability();
        UpdateCreateActionText();
        ResetRelayState();
        SetCreateStatus(string.Empty, false);
    }

    private void OnFogChanged(ChangeEvent<bool> evt)
    {
        pendingOptions.fogOfWar = evt.newValue;
    }

    private void OnLocalMultiplayerChanged(ChangeEvent<bool> evt)
    {
        if (!CanOfferLocalMultiplayer())
        {
            localMultiplayerToggle?.SetValueWithoutNotify(false);
            return;
        }

        if (!evt.newValue)
            ClearMppmDirective();
        UpdateCreateActionText();
        ResetRelayState();
        SetCreateStatus(string.Empty, false);
    }

    private void OnJoinCodeChanged(ChangeEvent<string> evt)
    {
        string value = evt.newValue ?? string.Empty;
        string normalized = string.Empty;
        foreach (char character in value)
        {
            if (!char.IsWhiteSpace(character))
                normalized += char.ToUpperInvariant(character);
        }

        if (joinCodeInput != null && normalized != evt.newValue)
            joinCodeInput.SetValueWithoutNotify(normalized);
        SetJoinStatus(string.Empty, false);
    }

    private void ConfigureJoinCaretBlink()
    {
        if (joinCodeInput == null)
            return;

        if (joinCaretBlink != null && joinCaretBlink.element != joinCodeInput)
        {
            joinCaretBlink.Pause();
            joinCaretBlink = null;
        }

        if (joinCaretBlink == null)
        {
            joinCaretBlink = joinCodeInput
                .schedule.Execute(ToggleJoinCaretVisibility)
                .Every(CaretBlinkIntervalMilliseconds);
            joinCaretBlink.Pause();
        }

        SetJoinCaretVisible();
    }

    private void OnJoinCodeFocusIn(FocusInEvent evt)
    {
        RestartJoinCaretBlink();
    }

    private void OnJoinCodeFocusOut(FocusOutEvent evt)
    {
        PauseJoinCaretBlink();
    }

    private void OnJoinCodeKeyDown(KeyDownEvent evt)
    {
        RestartJoinCaretBlink();
    }

    private void OnJoinCodePointerDown(PointerDownEvent evt)
    {
        RestartJoinCaretBlink();
    }

    private void RestartJoinCaretBlink()
    {
        SetJoinCaretVisible();
        if (joinCaretBlink == null)
            return;

        joinCaretBlink.Resume();
        joinCaretBlink.ExecuteLater(CaretBlinkIntervalMilliseconds);
    }

    private void PauseJoinCaretBlink()
    {
        joinCaretBlink?.Pause();
        SetJoinCaretVisible();
    }

    private void ToggleJoinCaretVisibility()
    {
        if (
            joinCodeInput == null
            || root?.focusController?.focusedElement != joinCodeInput
        )
        {
            PauseJoinCaretBlink();
            return;
        }

        bool hidden = joinCodeInput.ClassListContains(CaretHiddenClass);
        joinCodeInput.EnableInClassList(CaretHiddenClass, !hidden);
    }

    private void SetJoinCaretVisible()
    {
        joinCodeInput?.RemoveFromClassList(CaretHiddenClass);
    }

    private async void CreateMatch()
    {
        if (networkStartInProgress)
            return;

        MatchOptions options = pendingOptions;
        options.fogOfWar = fogToggle?.value ?? options.fogOfWar;
        options = options.Sanitized();
        bool useLocalMultiplayer =
            options.opponentType == OpponentType.Player && ShouldUseLocalMultiplayer();

        MatchOptions.SetCurrent(options);
        GameLoop.devMode = false;
        Time.timeScale = 1f;
        GameLoop.ResetMatchState();
        ClearMppmDirective();

        int operationVersion = BeginNetworkOperation(NetworkOperationKind.Host);
        SetCreateStatus(string.Empty, false);

        try
        {
            NetworkManager networkManager = RequireNetworkManager();
            RegisterNetworkCallbacks(networkManager, operationVersion);
            CacheTransportDefaults();

            if (options.IsBotMatch)
            {
                RestoreDirectTransport();
                ConfigureLoopbackTransport(networkManager);
                SetCreateStatus("Starting local match…", false);
                EnsureCurrentOperation(operationVersion);
                bool botHostStarted = networkManager.StartHost();
                EnsureCurrentOperation(operationVersion);
                if (!botHostStarted)
                    throw new InvalidOperationException("The local host could not start.");
                return;
            }

            if (useLocalMultiplayer)
            {
                ShowHostStatusPanel(
                    "Local endpoint",
                    $"{LocalMultiplayerAddress}:{LocalMultiplayerPort}",
                    "Starting local host…"
                );
                RestoreDirectTransport();
                ConfigureLoopbackTransport(networkManager);
#if UNITY_EDITOR
                DevMppmAutoJoin.EnableLocalAutoJoin();
#endif
                EnsureCurrentOperation(operationVersion);
                bool localHostStarted = networkManager.StartHost();
                EnsureCurrentOperation(operationVersion);
                if (!localHostStarted)
                    throw new InvalidOperationException("The local host could not start.");
                SetRelayStatus("Waiting for MPPM Player 2.");
                return;
            }

            ShowHostStatusPanel("Relay code", string.Empty, "Creating Relay host…");
            RestoreDirectTransport();
            RelayManager relayManager = RequireRelayManager();
            string joinCode = await relayManager.PrepareHostAsync(1, networkCancellation.Token);
            EnsureCurrentOperation(operationVersion);

            bool relayHostStarted = networkManager.StartHost();
            EnsureCurrentOperation(operationVersion);
            if (!relayHostStarted)
                throw new InvalidOperationException("The Relay host could not start.");
            if (relayCodeLabel != null)
                relayCodeLabel.text = joinCode;
            SetRelayStatus("Waiting for second player.");
            BattlePlanAudio.Play(AudioCueId.RelayCodeReady);
        }
        catch (OperationCanceledException exception)
        {
            if (!IsCurrentOperation(operationVersion))
                return;
            Debug.LogWarning($"[JoinGameUIController] {exception.Message}");
            FailCurrentOperation("Match creation was cancelled.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (!IsCurrentOperation(operationVersion))
                return;
            FailCurrentOperation("Match creation failed. Check the connection and try again.");
        }
    }

    private async void JoinMatch()
    {
        if (networkStartInProgress)
            return;

        if (!RelayManager.TryNormalizeJoinCode(joinCodeInput?.value, out string joinCode))
        {
            SetJoinStatus("Enter a valid Relay code.", true);
            joinCodeInput?.Focus();
            return;
        }

        joinCodeInput?.SetValueWithoutNotify(joinCode);
        MatchOptions.SetCurrent(MatchOptions.Default);
        GameLoop.devMode = false;
        Time.timeScale = 1f;
        GameLoop.ResetMatchState();
        ClearMppmDirective();
        int operationVersion = BeginNetworkOperation(NetworkOperationKind.Client);
        SetJoinStatus("Preparing Relay connection…", false);
        ShowJoinCancel();

        try
        {
            NetworkManager networkManager = RequireNetworkManager();
            RegisterNetworkCallbacks(networkManager, operationVersion);
            CacheTransportDefaults();
            RestoreDirectTransport();

            RelayManager relayManager = RequireRelayManager();
            await relayManager.PrepareClientAsync(joinCode, networkCancellation.Token);
            EnsureCurrentOperation(operationVersion);

            SetJoinStatus("Connecting…", false);
            bool clientStarted = networkManager.StartClient();
            EnsureCurrentOperation(operationVersion);
            if (!clientStarted)
                throw new InvalidOperationException("The client could not start.");
        }
        catch (OperationCanceledException exception)
        {
            if (!IsCurrentOperation(operationVersion))
                return;
            Debug.LogWarning($"[JoinGameUIController] {exception.Message}");
            FailCurrentOperation("Connection was cancelled.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (!IsCurrentOperation(operationVersion))
                return;
            FailCurrentOperation("Join failed. Check the code and try again.");
        }
    }

    private int BeginNetworkOperation(NetworkOperationKind operationKind)
    {
        networkCancellation?.Dispose();
        networkCancellation = new CancellationTokenSource();
        networkStartInProgress = true;
        activeOperationKind = operationKind;
        int version = ++networkOperationVersion;
        SetControlsEnabled(false);
        return version;
    }

    private void CancelHost()
    {
        if (
            !networkStartInProgress
            || activeOperationKind != NetworkOperationKind.Host
            || !IsCancelHostVisible()
        )
        {
            return;
        }

        StopCurrentOperation(string.Empty, false);
    }

    private void CancelJoin()
    {
        if (
            !networkStartInProgress
            || activeOperationKind != NetworkOperationKind.Client
            || !IsCancelJoinVisible()
        )
        {
            return;
        }

        StopCurrentOperation("Connection cancelled.", false);
    }

    private void StopCurrentOperation(string message, bool isError)
    {
        if (!networkStartInProgress || networkStopCoroutine != null)
            return;

        NetworkOperationKind stoppedKind = activeOperationKind;
        networkOperationVersion++;
        CancelAsyncNetworkWork();
        UnregisterNetworkCallbacks();
        ClearMppmDirective();
        cancelHostButton?.SetEnabled(false);
        cancelJoinButton?.SetEnabled(false);
        networkStopCoroutine = StartCoroutine(
            ShutdownAndFinishOperation(stoppedKind, message, isError)
        );
    }

    private void FailCurrentOperation(string message)
    {
        StopCurrentOperation(message, true);
    }

    private IEnumerator ShutdownAndFinishOperation(
        NetworkOperationKind stoppedKind,
        string message,
        bool isError
    )
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (
            networkManager != null
            && !networkManager.ShutdownInProgress
            && (networkManager.IsListening || networkManager.IsClient || networkManager.IsServer)
        )
        {
            networkManager.Shutdown(true);
        }

        while (
            networkManager != null
            && (
                networkManager.ShutdownInProgress
                || networkManager.IsListening
                || networkManager.IsClient
                || networkManager.IsServer
            )
        )
        {
            yield return null;
        }

        RestoreDirectTransport();
        networkStartInProgress = false;
        activeOperationKind = NetworkOperationKind.None;
        networkStopCoroutine = null;
        SetControlsEnabled(true);

        if (stoppedKind == NetworkOperationKind.Host)
        {
            ResetRelayState();
            SetCreateStatus(message, isError);
            root?.schedule.Execute(() => createMatchButton?.Focus());
        }
        else
        {
            ResetJoinControls();
            SetJoinStatus(message, isError);
            root?.schedule.Execute(() => joinCodeInput?.Focus());
        }
    }

    private void CancelAsyncNetworkWork()
    {
        if (networkCancellation != null)
        {
            if (!networkCancellation.IsCancellationRequested)
                networkCancellation.Cancel();
            networkCancellation.Dispose();
            networkCancellation = null;
        }

        RelayManager.Instance?.CancelPendingPreparation();
    }

    private void OnClientConnected(int operationVersion, ulong clientId)
    {
        if (!IsCurrentOperation(operationVersion))
            return;

        NetworkManager networkManager = callbackNetworkManager;
        if (networkManager == null)
            return;

        if (activeOperationKind == NetworkOperationKind.Host)
        {
            if (clientId != NetworkManager.ServerClientId)
                SetRelayStatus("Second player connected.");
            return;
        }

        if (
            activeOperationKind == NetworkOperationKind.Client
            && clientId == networkManager.LocalClientId
            && networkManager.IsConnectedClient
        )
        {
            SetJoinStatus("Connected. Waiting for host.", false);
            cancelJoinButton?.AddToClassList("hidden");
        }
    }

    private void OnClientDisconnected(int operationVersion, ulong clientId)
    {
        if (!IsCurrentOperation(operationVersion))
            return;

        NetworkManager networkManager = callbackNetworkManager;
        if (networkManager == null)
            return;

        if (activeOperationKind == NetworkOperationKind.Host)
        {
            if (clientId != NetworkManager.ServerClientId)
                SetRelayStatus("Second player disconnected. Waiting for another player.");
            return;
        }

        if (
            activeOperationKind == NetworkOperationKind.Client
            && (clientId == networkManager.LocalClientId || !networkManager.IsConnectedClient)
        )
        {
            string reason = string.IsNullOrWhiteSpace(networkManager.DisconnectReason)
                ? "Connection closed before the match started."
                : networkManager.DisconnectReason;
            FailCurrentOperation(reason);
        }
    }

    private void OnTransportFailure(int operationVersion)
    {
        if (!IsCurrentOperation(operationVersion))
            return;

        string message =
            activeOperationKind == NetworkOperationKind.Host
                ? "Host transport failed. Try creating the match again."
                : "Connection transport failed. Check the code and try again.";
        FailCurrentOperation(message);
    }

    private bool IsCancelHostVisible()
    {
        return cancelHostButton != null && !cancelHostButton.ClassListContains("hidden");
    }

    private bool IsCancelJoinVisible()
    {
        return cancelJoinButton != null && !cancelJoinButton.ClassListContains("hidden");
    }

    private NetworkManager RequireNetworkManager()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
            throw new InvalidOperationException("NetworkManager is not available.");
        if (
            networkManager.ShutdownInProgress
            || networkManager.IsListening
            || networkManager.IsClient
            || networkManager.IsServer
        )
            throw new InvalidOperationException("A network session is already running.");
        return networkManager;
    }

    private static RelayManager RequireRelayManager()
    {
        RelayManager relayManager = RelayManager.Instance;
        if (relayManager == null)
            throw new InvalidOperationException("RelayManager is not available.");
        return relayManager;
    }

    private void CacheTransportDefaults()
    {
        UnityTransport transport =
            NetworkManager.Singleton != null
                ? NetworkManager.Singleton.GetComponent<UnityTransport>()
                : null;
        if (
            transport == null
            || transport == directDefaultsSource
            || transport.Protocol != UnityTransport.ProtocolType.UnityTransport
        )
        {
            return;
        }

        directAddress = transport.ConnectionData.Address;
        directPort = transport.ConnectionData.Port;
        directListenAddress = transport.ConnectionData.ServerListenAddress;
        directUseWebSockets = transport.UseWebSockets;
        directDefaultsSource = transport;
    }

    private void RestoreDirectTransport()
    {
        CacheTransportDefaults();
        if (NetworkManager.Singleton == null)
            return;

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null || transport != directDefaultsSource)
            return;

        transport.SetConnectionData(directAddress, directPort, directListenAddress);
        transport.UseWebSockets = directUseWebSockets;
    }

    private static void ConfigureLoopbackTransport(NetworkManager networkManager)
    {
        if (networkManager == null)
            throw new ArgumentNullException(nameof(networkManager));

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
            throw new InvalidOperationException("UnityTransport is not available.");

        transport.SetConnectionData(
            LocalMultiplayerAddress,
            LocalMultiplayerPort,
            LocalMultiplayerAddress
        );
        transport.UseWebSockets = false;
    }

    private void EnsureCurrentOperation(int operationVersion)
    {
        if (!IsCurrentOperation(operationVersion))
            throw new OperationCanceledException("Network operation was superseded.");
    }

    private bool IsCurrentOperation(int operationVersion)
    {
        return this != null
            && isActiveAndEnabled
            && networkStartInProgress
            && operationVersion == networkOperationVersion;
    }

    private void SetControlsEnabled(bool enabled)
    {
        showCreateButton?.SetEnabled(enabled);
        showJoinButton?.SetEnabled(enabled);
        titleButton?.SetEnabled(enabled);
        eliminationButton?.SetEnabled(enabled);
        kingButton?.SetEnabled(enabled);
        flagButton?.SetEnabled(false);
        createMatchButton?.SetEnabled(enabled);
        joinMatchButton?.SetEnabled(enabled);
        playerOpponentButton?.SetEnabled(enabled);
        aiOpponentButton?.SetEnabled(enabled);
        fogToggle?.SetEnabled(enabled);
        joinCodeInput?.SetEnabled(enabled);
        cancelHostButton?.SetEnabled(!enabled);
        cancelJoinButton?.SetEnabled(!enabled);
        UpdateLocalMultiplayerAvailability(enabled);
    }

    private bool CanOfferLocalMultiplayer()
    {
        if (pendingOptions.opponentType != OpponentType.Player)
            return false;
#if UNITY_EDITOR
        return DevMppmAutoJoin.IsMainEditorPlayer;
#else
        return false;
#endif
    }

    private bool ShouldUseLocalMultiplayer()
    {
        return CanOfferLocalMultiplayer()
            && localMultiplayerToggle != null
            && localMultiplayerToggle.value;
    }

    private void UpdateLocalMultiplayerAvailability(bool controlsEnabled = true)
    {
        bool visible = CanOfferLocalMultiplayer();
        localMultiplayerRow?.EnableInClassList("hidden", !visible);
        localMultiplayerToggle?.SetEnabled(visible && controlsEnabled);
        if (!visible)
            localMultiplayerToggle?.SetValueWithoutNotify(false);
    }

    private void UpdateCreateActionText()
    {
        if (createMatchButton == null)
            return;

        if (pendingOptions.opponentType == OpponentType.AI)
            createMatchButton.text = "Start vs AI";
        else
            createMatchButton.text = ShouldUseLocalMultiplayer()
                ? "Start local match"
                : "Create online match";
    }

    private void ShowHostStatusPanel(string heading, string code, string status)
    {
        relayCodePanel?.RemoveFromClassList("hidden");
        if (connectionCodeHeading != null)
            connectionCodeHeading.text = heading;
        if (relayCodeLabel != null)
            relayCodeLabel.text = code;
        SetRelayStatus(status);
        cancelHostButton?.SetEnabled(true);
        cancelHostButton?.RemoveFromClassList("hidden");
        root?.schedule.Execute(() => cancelHostButton?.Focus());
    }

    private void ResetRelayState()
    {
        if (connectionCodeHeading != null)
            connectionCodeHeading.text = "Relay code";
        if (relayCodeLabel != null)
            relayCodeLabel.text = string.Empty;
        SetRelayStatus(string.Empty);
        cancelHostButton?.AddToClassList("hidden");
        relayCodePanel?.AddToClassList("hidden");
    }

    private void ShowJoinCancel()
    {
        joinMatchButton?.AddToClassList("hidden");
        cancelJoinButton?.SetEnabled(true);
        cancelJoinButton?.RemoveFromClassList("hidden");
        root?.schedule.Execute(() => cancelJoinButton?.Focus());
    }

    private void ResetJoinControls()
    {
        cancelJoinButton?.AddToClassList("hidden");
        joinMatchButton?.RemoveFromClassList("hidden");
    }

    private void SetRelayStatus(string message)
    {
        if (relayStatusLabel != null)
            relayStatusLabel.text = message;
    }

    private void SetCreateStatus(string message, bool isError)
    {
        if (createErrorLabel == null)
            return;
        createErrorLabel.text = message;
        createErrorLabel.EnableInClassList("status-line--danger", isError);
    }

    private void SetJoinStatus(string message, bool isError)
    {
        if (joinStatusLabel == null)
            return;
        joinStatusLabel.text = message;
        joinStatusLabel.EnableInClassList("status-line--danger", isError);
    }

    private void ClearMppmDirective()
    {
#if UNITY_EDITOR
        if (DevMppmAutoJoin.IsMainEditorPlayer)
            DevMppmAutoJoin.DisableAutoJoin();
#endif
    }

    private void NavigateBack()
    {
        if (networkStartInProgress)
            return;

        ClearMppmDirective();
        RestoreDirectTransport();
        SceneManager.LoadScene("Title Screen");
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Escape)
        {
            if (IsCancelHostVisible())
            {
                CancelHost();
                evt.StopImmediatePropagation();
                return;
            }
            if (IsCancelJoinVisible())
            {
                CancelJoin();
                evt.StopImmediatePropagation();
                return;
            }
            if (networkStartInProgress)
                return;

            NavigateBack();
            evt.StopImmediatePropagation();
            return;
        }

        if (networkStartInProgress)
            return;
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            return;
        if (panelState != PanelState.Join)
            return;

        JoinMatch();
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
}
