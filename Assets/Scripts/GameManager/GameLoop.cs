using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum MessagePerspective
{
    Neutral,
    Friendly,
    Enemy,
}

[System.Serializable]
public struct TeamMappingData : INetworkSerializable
{
    public ulong clientId;
    public int teamIndex;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref clientId);
        serializer.SerializeValue(ref teamIndex);
    }
}

public class GameLoop : NetworkBehaviour
{
    // Grid Configuration
    public static float cellSize = 2.7f;
    public static Rect gridBounds = new Rect(
        new Vector2(0, 0),
        new Vector2(8, 9) * cellSize + new Vector2(0.1f, 0.1f)
    );

    // UI Components
    [SerializeField]
    private TMP_Text overlayUIText;

    // Visual Properties
    public List<Color> teamColors;

    [SerializeField]
    private Color executingMoves;
    public List<Material> teamMaterials;

    // Game Setup: Objects and Prefabs
    [SerializeField]
    private GameObject wallPrefab;
    public List<GameObject> allUnits;
    public List<GameObject> unitCardPrefabs;

    // Team Configuration
    public static List<string> teams = new List<string>() { "BlueTeam", "RedTeam" };

    [SerializeField]
    private List<GameObject> unitCards;

    public static Dictionary<ulong, int[]> allTeamUnits = new Dictionary<ulong, int[]>();

    // Level Layout (col, row) from bottom left corner
    public static HashSet<Vector2Int> wallLayout = new HashSet<Vector2Int>()
    {
        new Vector2Int(3, 2),
        new Vector2Int(5, 2),
        new Vector2Int(2, 3),
        new Vector2Int(6, 3),
        new Vector2Int(0, 4),
        new Vector2Int(8, 4),
        new Vector2Int(3, 7),
        new Vector2Int(5, 7),
        new Vector2Int(2, 6),
        new Vector2Int(6, 6),
        new Vector2Int(0, 5),
        new Vector2Int(8, 5),
    };

    List<HashSet<Vector2Int>> spawns = new List<HashSet<Vector2Int>>()
    {
        new HashSet<Vector2Int>() //Blue Team Spawn Positions
        {
            new Vector2Int(0, 0),
            new Vector2Int(4, 0),
            new Vector2Int(8, 0),
        },
        new HashSet<Vector2Int>() //Red Team Spawn Positions
        {
            new Vector2Int(8, 9),
            new Vector2Int(4, 9),
            new Vector2Int(0, 9),
        },
    };

    // Store camera positions and rotations as a list of (Vector3 position, Quaternion rotation) tuples
    private List<(Vector3 position, Quaternion rotation)> cameraPositions = new List<(
        Vector3,
        Quaternion
    )>()
    {
        (new Vector3(11.93f, 24.4f, 3.4f), new Quaternion(0.59543306f, 0f, 0f, 0.8034049f)),
        (new Vector3(11.93f, 24.4f, 21.0f), new Quaternion(0f, 0.80340505f, -0.5954329f, 0f)),
    };

    // Game Settings
    float planningTimePerUnit = 5f;

    // Game State
    List<GameObject> doneMovingUnits = new List<GameObject>();
    List<GameObject> doneShootingUnits = new List<GameObject>();
    private Dictionary<ulong, int> clientIdToTeamIndex = new Dictionary<ulong, int>();

    // Client-side team mapping synchronized from server
    private Dictionary<ulong, int> clientTeamMapping = new Dictionary<ulong, int>();

    // Actions
    public static System.Action<bool> setUnitCardsInteractable;
    public static System.Action<GameObject> disableUnitCard;
    public static System.Action<bool> OrderAllowShooting;
    public static System.Action<bool> OrderStillShooting;
    public static System.Action OrderContinueShooting;

    List<PathsDict> pathsList = new List<PathsDict>();

    public GameObject teamCameraParent;
    public Camera teamCamera => teamCameraParent.GetComponent<Camera>();

    //End Game UI
    public GameObject endGameUI;
    public GameObject endGameStatusText;
    public GameObject playAgainButton;
    public GameObject mainMenuButton;
    private bool[] playAgain = new bool[teams.Count];

    public static GameLoop Instance { get; private set; }

    void Awake()
    {
        for (int i = 0; i < playAgain.Length; i++)
        {
            playAgain[i] = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (IsServer)
        {
            InitializeClientTeamMapping();
            StartGame();
            StartCoroutine(GameLoopTemp());
        }
        else
        {
            // Client initialization - set up local team mapping
            InitializeLocalClientTeamMapping();
        }

        InitializeCameraPosition();
    }

    private void InitializeCameraPosition()
    {
        if (!IsClient)
            return;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        string localTeam = GetLocalClientTeam(localClientId);
        int teamIndex = GetTeamIndex(localTeam);

        if (teamIndex >= 0 && teamIndex < spawns.Count && teamIndex < cameraPositions.Count)
        {
            if (teamCameraParent != null)
            {
                teamCameraParent.transform.position = cameraPositions[teamIndex].position;
                teamCameraParent.transform.rotation = cameraPositions[teamIndex].rotation;
            }
        }
    }

    private void InitializeLocalClientTeamMapping()
    {
        if (IsServer)
            return; // Only clients should call this

        // Initialize with default mapping based on client order
        var connectedClients = NetworkManager.Singleton.ConnectedClientsIds;
        for (int i = 0; i < connectedClients.Count && i < teams.Count; i++)
        {
            clientTeamMapping[connectedClients[i]] = i;
        }
    }

    void StartGame()
    {
        foreach (var pos in wallLayout)
        {
            Vector3 worldPos = gridCoordToWorld(pos);
            GameObject wall = NetworkHelper.Spawn(wallPrefab, worldPos, Quaternion.identity);
            Vector3 heightOffset = Helper.heightOffset(wall.transform);
            wall.transform.position += heightOffset;

            // Sync the height-adjusted position to all clients
            NetworkHelper.SyncHeightAdjustedPositionStatic(wall, wall.transform.position);
        }

        //Destroy all units
        foreach (string team in teams)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                Destroy(unit);
            }
        }

        //Setup teams
        for (int i = 0; i < teams.Count; i++)
        {
            SetupUnitsAndCards(
                allTeamUnits[GetClient(i)],
                unitCards[i],
                spawns[i],
                Quaternion.Euler(0, 180 * i, 0),
                teams[i]
            );
        }
    }

    void SetupUnitsAndCards(
        int[] teamUnits,
        GameObject unitCardTeamContainer,
        HashSet<Vector2Int> spawnPositions,
        Quaternion rotation,
        string team
    )
    {
        for (int i = 0; i < teamUnits.Length; i++)
        {
            GameObject unit = NetworkHelper.Spawn(
                allUnits[teamUnits[i]],
                gridCoordToWorld(spawnPositions.ElementAt(i)),
                rotation,
                GetClient(team)
            );
            Vector3 heightOffset = Helper.heightOffset(unit.transform);
            unit.transform.position += heightOffset;
            unit.tag = team;
            SetGroupLayerGlobal(unit, LayerMask.NameToLayer(team));

            // Sync the height-adjusted position to all clients
            NetworkHelper.SyncHeightAdjustedPositionStatic(unit, unit.transform.position);

            GameObject newUnitCard = NetworkHelper.Spawn(
                unitCardPrefabs[teamUnits[i]],
                unitCardTeamContainer.transform,
                GetClient(team)
            );

            // Bind the unit to the card and sync to clients
            var ability = newUnitCard.GetComponent<ActivateAbility>();
            if (ability != null)
            {
                ability.SetUnitServer(unit);
            }
        }
        DisabledCardClientRpc(unitCardTeamContainer);
    }

    [ClientRpc]
    void DisabledCardClientRpc(NetworkObjectReference objRef)
    {
        if (objRef.TryGet(out NetworkObject networkObject))
        {
            networkObject.GetComponent<UnitCard>().DisableUI();
        }
    }

    private void InitializeClientTeamMapping()
    {
        // For now, map first two clients to teams 0 and 1
        // This can be expanded to support more teams/clients
        var connectedClients = NetworkManager.Singleton.ConnectedClientsIds;
        for (int i = 0; i < connectedClients.Count && i < teams.Count; i++)
        {
            clientIdToTeamIndex[connectedClients[i]] = i;
        }

        // Create team mapping data for synchronization
        var teamMappings = new TeamMappingData[clientIdToTeamIndex.Count];
        int index = 0;
        foreach (var kvp in clientIdToTeamIndex)
        {
            teamMappings[index] = new TeamMappingData { clientId = kvp.Key, teamIndex = kvp.Value };
            index++;
        }

        // Synchronize team mapping to all clients
        SyncTeamMappingToClientsClientRpc(teamMappings);
    }

    [ClientRpc]
    private void SyncTeamMappingToClientsClientRpc(TeamMappingData[] teamMappings)
    {
        // Clear existing mapping
        clientTeamMapping.Clear();

        // Apply the team mappings received from server
        foreach (var mapping in teamMappings)
        {
            clientTeamMapping[mapping.clientId] = mapping.teamIndex;
        }
    }

    private int GetTeamIndexForClient(ulong clientId)
    {
        if (clientIdToTeamIndex.ContainsKey(clientId))
        {
            return clientIdToTeamIndex[clientId];
        }

        // Fallback: assign based on client order if not in mapping
        Debug.LogWarning(
            $"[GameLoop] No team index found for client {clientId}, falling back to client order assignment"
        );
        var connectedClients = NetworkManager.Singleton.ConnectedClientsIds.ToList();
        int index = connectedClients.IndexOf(clientId);
        return index >= 0 && index < teams.Count ? index : 0;
    }

    public ulong GetClient(string team)
    {
        return GetClient(teams.IndexOf(team));
    }

    private ulong GetClient(int teamIndex)
    {
        foreach (var kvp in clientIdToTeamIndex)
        {
            if (kvp.Value == teamIndex)
            {
                return kvp.Key;
            }
        }

        // Fallback: return first connected client if no mapping found
        Debug.LogWarning(
            $"[GameLoop] No client found for team index {teamIndex}, falling back to first connected client"
        );
        var connectedClients = NetworkManager.Singleton.ConnectedClientsIds;
        return connectedClients.Count > 0 ? connectedClients.First() : 0;
    }

    IEnumerator GameLoopTemp()
    {
        if (!IsServer)
            yield break;
        yield return null;

        while (teams.All(team => teamSize(team) > 0))
        {
            pathsList = new List<PathsDict>();

            setUnitCardsInteractable?.Invoke(false);
            OrderStillShooting?.Invoke(true);

            float timerLength = planningTimePerUnit * teams.Max(teamSize);
            double endTime = NetworkManager.Singleton.ServerTime.Time + timerLength;

            setOverlayUITextClientRpc($"Planning", MessagePerspective.Friendly);
            StartPlanningClientRpc(endTime);

            while (
                pathsList.Count < teams.Count
                && NetworkManager.Singleton.ServerTime.Time < endTime + 1
            )
            {
                yield return null;
            }

            setUnitCardsInteractable?.Invoke(true);
            setOverlayUITextClientRpc("Executing Moves", MessagePerspective.Neutral);

            // Flatten pathsList into a single PathsDict
            PathsDict paths = new PathsDict();
            foreach (PathsDict teamPaths in pathsList)
            {
                foreach (var kvp in teamPaths)
                {
                    paths[kvp.Key] = kvp.Value;
                }
            }

            ExecuteMoves(paths);

            while (CheckStillShooting())
            {
                //THINKING: ability activates such that theres movement/gameplay extension
                //If moving continues during this time restart checkStillMoving
                if (CheckStillMoving())
                {
                    OrderContinueShooting();
                    while (CheckStillMoving())
                    {
                        yield return null;
                    }
                    yield return null; // Wait a bit before stopping shooting
                }
                OrderAllowShooting(false);

                yield return null;
            }
        }

        EndGame();
    }

    void EndGame()
    {
        ulong? winner = getWinner();
        // Serialize nullable ulong as two parameters: hasWinner (bool) and winnerValue (ulong)
        bool hasWinner = winner.HasValue;
        ulong winnerValue = winner.GetValueOrDefault();
        Debug.Log("Winner: " + winnerValue);
        EndGameClientRpc(hasWinner, winnerValue);
    }

    [ClientRpc]
    void EndGameClientRpc(bool hasWinner, ulong winnerValue)
    {
        Debug.Log("Ending game");
        endGameStatusText.GetComponent<TMP_Text>().text = !hasWinner
            ? "No winner"
            : (winnerValue == NetworkManager.Singleton.LocalClientId ? "You win!" : "You lose!");
        playAgainButton.GetComponent<Button>().onClick.AddListener(() => PlayAgain());
        mainMenuButton
            .GetComponent<Button>()
            .onClick.AddListener(() =>
            {
                NetworkManager.Singleton.Shutdown();
                SceneManager.LoadScene("JoinGame");
            });
        endGameUI.SetActive(true);
    }

    void PlayAgain()
    {
        playAgainButton.GetComponent<Button>().interactable = false;
        mainMenuButton.GetComponent<Button>().interactable = false;
        PlayAgainServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    void PlayAgainServerRpc(ServerRpcParams rpcParams = default)
    {
        playAgain[GetTeamIndexForClient(rpcParams.Receive.SenderClientId)] = true;
        if (playAgain.All(x => x))
        {
            NetworkManager.Singleton.SceneManager.LoadScene("HomeScreen", LoadSceneMode.Single);
        }
    }

    ulong? getWinner()
    {
        foreach (var team in teams)
        {
            if (teamSize(team) > 0)
            {
                return GetClient(team);
            }
        }
        return null;
    }

    [ClientRpc]
    void StartPlanningClientRpc(double endTime)
    {
        StartCoroutine(
            transform
                .GetComponent<PlanMovement>()
                .StartPlanning(paths => SendPathsToServerRpc(paths), endTime)
        );
    }

    [ServerRpc(RequireOwnership = false)]
    void SendPathsToServerRpc(PathsDict paths, ServerRpcParams rpcParams = default)
    {
        // Validate and sanitize client-submitted paths on the server
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        PathsDict sanitized = new PathsDict();

        foreach (var kvp in paths)
        {
            GameObject unit = kvp.Key;
            if (unit == null)
                continue;

            var netObj = unit.GetComponent<NetworkObject>();
            if (netObj == null || !netObj.IsSpawned)
                continue;

            // Ensure the sender owns this unit
            if (netObj.OwnerClientId != senderClientId)
                continue;

            var movement = unit.GetComponent<Movement>();
            if (movement == null || movement.unitData == null)
                continue;

            int maxSteps = Mathf.Max(0, movement.unitData.moveDist);
            List<Vector3> submitted = kvp.Value ?? new List<Vector3>();

            // Build a sanitized path: start at current cell, then step by step adjacents, up to maxSteps
            List<Vector3> clean = new List<Vector3>();
            Vector3 start = PlanMovement.GetGridCellUnderCharacter(unit);
            clean.Add(start);

            int stepsAdded = 0;
            Vector3 last = start;
            foreach (var point in submitted)
            {
                if (stepsAdded >= maxSteps)
                    break;

                // snap to grid
                Vector3 snapped = PlanMovement.GetNearestGridCell(point);

                // must be exactly one cell away (no diagonals) and not already in path
                float dist = Mathf.Abs(snapped.x - last.x) + Mathf.Abs(snapped.z - last.z);
                bool isAdjacent = Mathf.Abs(dist - cellSize) <= 0.1f; // Manhanttan 1 step
                if (!isAdjacent)
                    continue;
                if (clean.Contains(snapped))
                    continue;

                // optionally: ensure within grid bounds
                if (!gridBounds.Contains(new Vector2(snapped.x, snapped.z)))
                    continue;

                clean.Add(snapped);
                last = snapped;
                stepsAdded++;
            }

            sanitized[unit] = clean;
        }

        pathsList.Add(sanitized);
    }

    void ExecuteMoves(PathsDict paths)
    {
        // Get all units from all teams
        foreach (var team in teams)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                List<Vector3> movementPath = new List<Vector3>();

                // Check if this unit has a path in the dictionary
                if (paths.ContainsKey(unit))
                {
                    movementPath = new List<Vector3>(paths[unit]); // Create a new list copy
                }

                // Start movement with either the actual path or empty list
                unit.GetComponent<Movement>().StartMovement(movementPath);
            }
        }
    }

    public void SetGroupLayerGlobal(GameObject obj, int layer)
    {
        SetGroupLayer(obj, layer);
        var netObj = obj != null ? obj.GetComponent<NetworkObject>() : null;
        if (netObj != null)
        {
            NetworkObjectReference objRef = netObj;
            SetGroupLayerClientRpc(objRef, layer);
        }
    }

    [ClientRpc]
    public void SetGroupLayerClientRpc(NetworkObjectReference objRef, int layer)
    {
        if (IsServer)
            return;
        if (objRef.TryGet(out NetworkObject networkObject))
        {
            SetGroupLayer(networkObject.gameObject, layer);
        }
    }

    public static void SetGroupLayer(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetGroupLayer(child.gameObject, layer);
        }
    }

    bool CheckStillMoving()
    {
        foreach (var team in teams)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                if (unit.GetComponent<Movement>().moving == true)
                {
                    return true;
                }
            }
        }

        return false;
    }

    bool CheckStillShooting()
    {
        foreach (var team in teams)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                if (unit.GetComponent<Shooting>().stillShooting == true)
                    return true;
            }
        }

        return false;
    }

    [ClientRpc]
    public void setOverlayUITextClientRpc(string message, string team = "neutral")
    {
        overlayUIText.text = message;

        if (team == "neutral")
        {
            overlayUIText.color = executingMoves;
        }
        else
        {
            // Determine if this message is about the client's own team or enemy team
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            string localTeam = GetLocalClientTeam(localClientId);

            if (localTeam == team)
            {
                overlayUIText.color = teamColors[0];
            }
            else
            {
                overlayUIText.color = teamColors[1];
            }
        }
    }

    /// <summary>
    /// Wrapper function that accepts "friendly", "enemy", or "neutral" instead of specific team names
    /// </summary>
    [ClientRpc]
    public void setOverlayUITextClientRpc(string message, MessagePerspective perspective)
    {
        overlayUIText.text = message;

        if (perspective == MessagePerspective.Friendly)
        {
            overlayUIText.color = teamColors[0];
        }
        else if (perspective == MessagePerspective.Enemy)
        {
            overlayUIText.color = teamColors[1];
        }
        else
        {
            overlayUIText.color = executingMoves;
        }
    }

    /// <summary>
    /// Gets the team that the local client belongs to
    /// </summary>
    public string GetLocalClientTeam(ulong clientId)
    {
        // Use client-side mapping if available
        if (clientTeamMapping.ContainsKey(clientId))
        {
            return teams[clientTeamMapping[clientId]];
        }

        // Fallback: if no mapping found, assume first team
        Debug.LogWarning(
            $"[GameLoop] No team mapping found for client {clientId}, falling back to first team"
        );
        return teams.Count > 0 ? teams[0] : "BlueTeam";
    }

    /// <summary>
    /// Gets the perspective for a specific team relative to the local client
    /// </summary>
    public MessagePerspective GetTeamPerspective(string team)
    {
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        string localTeam = GetLocalClientTeam(localClientId);

        if (team == localTeam)
            return MessagePerspective.Friendly;
        else if (team == "neutral" || team == "")
            return MessagePerspective.Neutral;
        else
            return MessagePerspective.Enemy;
    }

    public static int GetTeamIndex(string team)
    {
        if (!teams.Contains(team))
            return -1;
        return teams.IndexOf(team);
    }

    public Color GetTeamColor(string team)
    {
        if (!teams.Contains(team))
        {
            Debug.LogWarning(
                $"[GameLoop] Team '{team}' not found in teams list, falling back to executingMoves color"
            );
            return executingMoves;
        }
        return teamColors[teams.IndexOf(team)];
    }

    public Material GetTeamMaterial(string team)
    {
        if (!teams.Contains(team))
        {
            Debug.LogWarning(
                $"[GameLoop] Team '{team}' not found in teams list, falling back to null material"
            );
            return null;
        }
        return teamMaterials[teams.IndexOf(team)];
    }

    public static string GetEnemyTeam(string team)
    {
        if (!teams.Contains(team))
        {
            Debug.LogWarning(
                $"[GameLoop] Team '{team}' not found in teams list, cannot determine enemy team, falling back to null"
            );
            return null;
        }
        return teams.FirstOrDefault(t => t != team);
    }

    public static int teamSize(string team)
    {
        return GameObject.FindGameObjectsWithTag(team).Length;
    }

    public static Vector3 gridCoordToWorld(Vector2Int coords)
    {
        return new Vector3(
            gridBounds.xMin + coords.x * cellSize,
            0,
            gridBounds.yMin + coords.y * cellSize
        );
    }
}
