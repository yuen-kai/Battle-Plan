using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkHandler : NetworkBehaviour
{
    private const string JoinSceneName = "JoinGame";
    private static NetworkManager approvalManager;
    private static bool acceptingConnections = true;

    private readonly NetworkVariable<MatchOptions> replicatedOptions = new(MatchOptions.Default);
    private bool sceneLoadRequested;

    public MatchOptions Options => replicatedOptions.Value.Sanitized();

    private void Awake()
    {
        InstallConnectionApproval();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        InstallConnectionApproval();
        replicatedOptions.OnValueChanged += OnOptionsChanged;

        if (!IsServer)
        {
            MatchOptions.SetCurrent(replicatedOptions.Value);
            return;
        }

        replicatedOptions.Value = MatchOptions.Current.Sanitized();
        MatchOptions.SetCurrent(replicatedOptions.Value);
        acceptingConnections = true;
        NetworkManager.OnClientConnectedCallback += OnClientConnected;
        TryAdvance();
    }

    public override void OnNetworkDespawn()
    {
        replicatedOptions.OnValueChanged -= OnOptionsChanged;
        if (NetworkManager != null)
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
        base.OnNetworkDespawn();
    }

    private void OnOptionsChanged(MatchOptions previousValue, MatchOptions newValue)
    {
        MatchOptions.SetCurrent(newValue);
    }

    private void OnClientConnected(ulong clientId)
    {
        TryAdvance();
    }

    private static void InstallConnectionApproval()
    {
        NetworkManager manager =
            NetworkManager.Singleton
            ?? Object.FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogError("[NetworkHandler] NetworkManager is required for connection approval.");
            return;
        }

        if (approvalManager != manager || !manager.IsListening)
            acceptingConnections = true;
        approvalManager = manager;
        manager.NetworkConfig.ConnectionApproval = true;
        manager.ConnectionApprovalCallback = ApproveConnection;
    }

    private static void ApproveConnection(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response
    )
    {
        NetworkManager manager = approvalManager;
        bool isServerClient = request.ClientNetworkId == NetworkManager.ServerClientId;
        string reconnectToken = ReconnectSession.DecodeToken(request.Payload);

        response.CreatePlayerObject = false;
        response.Pending = false;

        // A held seat is the one way past the lobby gate: the claimant has to present the identity
        // the seat was issued to, inside the window, while nobody else is sitting in it.
        if (!isServerClient && TryApproveRejoin(reconnectToken, request.ClientNetworkId))
        {
            response.Approved = true;
            response.Reason = string.Empty;
            return;
        }

        MatchOptions options = MatchOptions.Current.Sanitized();
        int clientLimit = RequiredClientCount(options);
        int admittedOrPending =
            manager == null
                ? int.MaxValue
                : manager
                    .ConnectedClientsIds.Concat(manager.PendingClients.Keys)
                    .Append(request.ClientNetworkId)
                    .Distinct()
                    .Count();
        bool inJoinScene = SceneManager.GetActiveScene().name == JoinSceneName;
        bool approved = CanApproveConnection(
            isServerClient,
            acceptingConnections,
            inJoinScene,
            admittedOrPending,
            clientLimit
        );

        if (approved)
            ReconnectGrace.Server.RegisterConnection(request.ClientNetworkId, reconnectToken);

        response.Approved = approved;
        response.Reason = approved ? string.Empty : "This match is full or has already started.";
    }

    /// <summary>
    /// Reattaches a returning player to the seat their identity still owns. The seat is pointed at
    /// the new client ID here, before NGO synchronises it, because per-object visibility is
    /// resolved during synchronisation and a client the match cannot place observes nothing.
    /// </summary>
    private static bool TryApproveRejoin(string reconnectToken, ulong clientId)
    {
        GameLoop gameLoop = GameLoop.Instance;
        if (gameLoop == null || !gameLoop.IsServer)
            return false;

        if (
            !ReconnectGrace.Server.TryClaimSeat(
                reconnectToken,
                clientId,
                ReconnectGrace.Now,
                out int teamIndex
            )
        )
        {
            return false;
        }

        gameLoop.ServerReattachParticipant(teamIndex, clientId);
        Debug.Log($"[NetworkHandler] Client {clientId} reclaimed team {teamIndex} after a drop.");
        return true;
    }

    public static int RequiredClientCount(MatchOptions options)
    {
        return options.Sanitized().IsBotMatch ? 1 : 2;
    }

    public static bool CanApproveConnection(
        bool isServerClient,
        bool gateOpen,
        bool inJoinScene,
        int admittedOrPending,
        int clientLimit
    )
    {
        if (isServerClient)
            return true;

        return gateOpen && inJoinScene && clientLimit > 0 && admittedOrPending <= clientLimit;
    }

    private void TryAdvance()
    {
        if (!IsServer || sceneLoadRequested || NetworkManager == null)
            return;

        MatchOptions options = replicatedOptions.Value.Sanitized();
        int requiredClients = RequiredClientCount(options);
        if (NetworkManager.ConnectedClients.Count < requiredClients)
            return;

        acceptingConnections = false;
        if (TutorialSession.IsActive)
        {
            // The tutorial picks its own crew, so it goes straight to the board rather than through
            // character selection.
            GameLoop.ResetMatchState();
            GameLoop.ConfigureTeam(
                GameLoop.HostTeamIndex,
                NetworkManager.ServerClientId,
                TutorialSession.BuildRoster()
            );
            GameLoop.ConfigureTeam(
                GameLoop.OpponentTeamIndex,
                GameLoop.BotParticipantId,
                TutorialSession.BuildRoster()
            );

            sceneLoadRequested = true;
            NetworkManager.SceneManager.LoadScene("Game", LoadSceneMode.Single);
            return;
        }

        if (SandboxSession.IsActive)
        {
            // The sandbox composes both crews itself, so it goes straight to the board rather than
            // through character selection — same as the tutorial above.
            GameLoop.ResetMatchState();
            GameLoop.ConfigureTeam(
                GameLoop.HostTeamIndex,
                NetworkManager.ServerClientId,
                SandboxSession.BuildRoster(GameLoop.HostTeamIndex)
            );
            GameLoop.ConfigureTeam(
                GameLoop.OpponentTeamIndex,
                GameLoop.BotParticipantId,
                SandboxSession.BuildRoster(GameLoop.OpponentTeamIndex)
            );

            sceneLoadRequested = true;
            NetworkManager.SceneManager.LoadScene("Game", LoadSceneMode.Single);
            return;
        }

        if (GameLoop.devMode)
        {
            GameLoop.ResetMatchState();
            GameLoop.ConfigureTeam(
                GameLoop.HostTeamIndex,
                NetworkManager.ServerClientId,
                options.IsBotMatch ? GameLoop.DevBotHostRoster : GameLoop.DevHostRoster
            );

            if (options.IsBotMatch)
            {
                GameLoop.ConfigureTeam(
                    GameLoop.OpponentTeamIndex,
                    GameLoop.BotParticipantId,
                    GameLoop.DefaultBotRoster
                );
            }
            else
            {
                ulong opponentClientId = NetworkManager
                    .ConnectedClientsIds.Where(id => id != NetworkManager.ServerClientId)
                    .OrderBy(id => id)
                    .First();
                GameLoop.ConfigureTeam(
                    GameLoop.OpponentTeamIndex,
                    opponentClientId,
                    GameLoop.DevOpponentRoster
                );
            }

            sceneLoadRequested = true;
            NetworkManager.SceneManager.LoadScene("Game", LoadSceneMode.Single);
            return;
        }

        sceneLoadRequested = true;
        NetworkManager.SceneManager.LoadScene("HomeScreen", LoadSceneMode.Single);
    }
}
