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

public class GameLoop : NetworkBehaviour
{
    public const bool TESTING = true;

    public UnitDatabase allUnits;

    // UI Components
    [SerializeField]
    private TMP_Text overlayUIText;
    public List<Color> teamColors;

    public Color executingMoves;
    public List<Material> teamMaterials;

    public GameObject unitCards;

    //Teams
    public static List<string> teamNames = new() { "BlueTeam", "RedTeam" };
    public static Dictionary<ulong, int[]> allTeamUnits = new();
    public static Dictionary<ulong, GameObject[]> allTeamUnitObjects = new();

    // Level Layout (col, row) from bottom left corner
    public static float cellSize = 2.7f;
    public static Rect gridBounds = new(
        new Vector2(0, 0),
        new Vector2(8, 9) * cellSize + new Vector2(0.1f, 0.1f)
    );

    [SerializeField]
    private GameObject wallPrefab;
    public static HashSet<Vector2Int> wallLayout = new()
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

    List<HashSet<Vector2Int>> spawns = !TESTING
        ? new List<HashSet<Vector2Int>>()
        {
            new() //Blue Team Spawn Positions
            {
                new Vector2Int(0, 0),
                new Vector2Int(4, 0),
                new Vector2Int(8, 0),
            },
            new() //Red Team Spawn Positions
            {
                new Vector2Int(8, 9),
                new Vector2Int(4, 9),
                new Vector2Int(0, 9),
            },
        }
        : new List<HashSet<Vector2Int>>()
        {
            new() //Blue Team Spawn Positions
            {
                new Vector2Int(1, 3),
                new Vector2Int(4, 5),
                new Vector2Int(7, 3),
            },
            new() //Red Team Spawn Positions
            {
                new Vector2Int(5, 6),
                new Vector2Int(4, 6),
                new Vector2Int(3, 6),
            },
        };

    // Store camera positions and rotations as a list of (Vector3 position, Quaternion rotation) tuples
    private List<(Vector3 position, Quaternion rotation)> cameraPositions = new()
    {
        (new Vector3(11.93f, 24.4f, 3.4f), new Quaternion(0.59543306f, 0f, 0f, 0.8034049f)),
        (new Vector3(11.93f, 24.4f, 21.0f), new Quaternion(0f, 0.80340505f, -0.5954329f, 0f)),
    };

    // Game Settings
    float planningTimePerUnit = TESTING ? 3f : 5f;

    // Actions
    public static System.Action<bool> OrderAllowShooting;
    public static System.Action<bool> OrderStillShooting;
    public static System.Action OrderContinueShooting;

    List<PathsDict> pathsList = new();

    public GameObject teamCameraParent;
    public Camera TeamCamera;

    //End Game UI
    public GameObject endGameUI;
    public GameObject endGameStatusText;
    public GameObject playAgainButton;
    public GameObject mainMenuButton;
    private List<ulong> playAgain = new();


    public static GameLoop Instance { get; private set; }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        TeamCamera = teamCameraParent.GetComponent<Camera>();

        if (IsServer)
        {
            StartGame();
            StartCoroutine(GameLoopTemp());
            InitializeCameraPosition();
        }
    }

    private void InitializeCameraPosition()
    {
        // For each client in allTeamUnits, initialize the client by calling InitializeCameraPositionClientRpc with their team index
        for (int i = 0; i < allTeamUnits.Count; i++)
        {
            ulong clientId = allTeamUnits.ElementAt(i).Key;
            InitializeCameraPositionClientRpc(i, NetworkHelper.ToClient(clientId));
        }
    }

    [ClientRpc]
    private void InitializeCameraPositionClientRpc(int teamIndex, ClientRpcParams clientRpcParams = default)
    {
        if (teamIndex >= 0 && teamIndex < spawns.Count && teamIndex < cameraPositions.Count)
        {
            if (teamCameraParent != null)
            {
                teamCameraParent.transform.position = cameraPositions[teamIndex].position;
                teamCameraParent.transform.rotation = cameraPositions[teamIndex].rotation;
            }
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
        foreach (string team in teamNames)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                Destroy(unit);
            }
        }

        //Setup teams
        for (int i = 0; i < teamNames.Count; i++)
        {
            SetupUnitsAndCards(
                allTeamUnits.ElementAt(i).Key,
                allTeamUnits.ElementAt(i).Value,
                spawns[i],
                Quaternion.Euler(0, 180 * i, 0),
                teamNames[i]
            );
        }
        // yield return new WaitForSeconds(1f);
    }

    void SetupUnitsAndCards(
        ulong clientId,
        int[] teamUnits,
        HashSet<Vector2Int> spawnPositions,
        Quaternion rotation,
        string team
    )
    {
        allTeamUnitObjects[clientId] = new GameObject[teamUnits.Length];
        for (int i = 0; i < teamUnits.Length; i++)
        {
            GameObject unit = NetworkHelper.Spawn(
                allUnits.units[teamUnits[i]].unitModel,
                gridCoordToWorld(spawnPositions.ElementAt(i)),
                rotation,
                clientId
            );
            allTeamUnitObjects[clientId][i] = unit;
            Vector3 heightOffset = Helper.heightOffset(unit.transform);
            unit.transform.position += heightOffset;
            NetworkHelper.SyncHeightAdjustedPositionStatic(unit, unit.transform.position);

            unit.tag = team;
            SetGroupLayerGlobal(unit, LayerMask.NameToLayer(team));

            SetUnitCardClientRpc(i, teamUnits[i], NetworkHelper.ToClient(clientId));
        }
    }

    [ClientRpc]
    void SetUnitCardClientRpc(int cardIndex, int unitIndex, ClientRpcParams clientRpcParams = default)
    {
        CardHandler card = unitCards.transform.GetChild(cardIndex).GetComponent<CardHandler>();
        UnitData unitData = allUnits.units[unitIndex];
        card.uses = unitData.uses;
        card.setImage(unitData.abilitySprite);
        card.setText(unitData.abilityName);
        card.setButtonListener(() => PlanMovement.Instance.SwitchToUnit(cardIndex));
    }

    IEnumerator GameLoopTemp()
    {
        if (!IsServer)
            yield break;
        yield return null;

        while (teamNames.All(team => teamSize(team) > 0))
        {
            pathsList = new List<PathsDict>();

            unitCards.GetComponent<UnitCardContainer>().SetUnitCardsInteractable(true);
            OrderStillShooting?.Invoke(true);

            float timerLength = planningTimePerUnit * teamNames.Max(teamSize);
            double endTime = NetworkManager.Singleton.ServerTime.Time + timerLength;

            CameraEffects.Instance?.FlashClientRpc(MessagePerspective.Friendly);
            setOverlayUITextClientRpc($"Planning", MessagePerspective.Friendly);
            StartPlanningClientRpc(endTime);

            while (
                pathsList.Count < teamNames.Count
                && NetworkManager.Singleton.ServerTime.Time < endTime + 1
            )
            {
                yield return null;
            }


            CameraEffects.Instance?.FlashClientRpc(MessagePerspective.Neutral);
            unitCards.GetComponent<UnitCardContainer>().SetUnitCardsInteractable(false);

            setOverlayUITextClientRpc("Executing Moves", MessagePerspective.Neutral);

            // Flatten pathsList into a single PathsDict
            PathsDict paths = new();
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
        bool hasWinner = winner.HasValue;
        ulong winnerValue = winner.GetValueOrDefault();
        EndGameClientRpc(hasWinner, winnerValue);
    }

    [ClientRpc]
    void EndGameClientRpc(bool hasWinner, ulong winnerValue)
    {
        endGameStatusText.GetComponent<TMP_Text>().text = !hasWinner
            ? "No winner"
            : (winnerValue == NetworkManager.Singleton.LocalClientId ? "You win!" : "You lose!");
        playAgainButton.GetComponent<Button>().onClick.AddListener(() => PlayAgain());
        mainMenuButton
            .GetComponent<Button>()
            .onClick.AddListener(() => ExitToMainMenu());
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
        playAgain.Add(rpcParams.Receive.SenderClientId);
        if (playAgain.Count == allTeamUnits.Count)
        {
            playAgain.Clear();
            NetworkHelper.CleanupAllNetworkObjects();
            NetworkManager.Singleton.SceneManager.LoadScene("HomeScreen", LoadSceneMode.Single);
        }
    }

    void ExitToMainMenu()
    {
        playAgainButton.GetComponent<Button>().interactable = false;
        mainMenuButton.GetComponent<Button>().interactable = false;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            ExitServerRpc(NetworkManager.Singleton.LocalClientId);
        }
        SceneManager.LoadScene("Title Screen");
    }

    [ServerRpc(RequireOwnership = false)]
    void ExitServerRpc(ulong id)
    {
        DisablePlayAgainButtonClientRpc();
        if (id == NetworkManager.ServerClientId)
        {
            NetworkHelper.CleanupAllNetworkObjects();
            NetworkManager.Singleton.Shutdown();
        }
        else
        {
            NetworkManager.Singleton.DisconnectClient(id);
        }
    }

    [ClientRpc]
    void DisablePlayAgainButtonClientRpc()
    {
        playAgainButton.GetComponent<Button>().interactable = false;
        mainMenuButton.GetComponent<Button>().interactable = true;
    }

    ulong? getWinner()
    {
        foreach (var team in teamNames)
        {
            if (teamSize(team) > 0)
            {
                return allTeamUnits.ElementAt(teamNames.IndexOf(team)).Key;
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

        PathsDict sanitized = new();

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
            List<Vector3> submitted = kvp.Value.Item2 ?? new List<Vector3>();

            // Build a sanitized path: start at current cell, then step by step adjacents, up to maxSteps
            List<Vector3> clean = new();
            Vector3 start = GridSystem.GetNearestGridCell(unit);
            clean.Add(start);

            int stepsAdded = 0;
            Vector3 last = start;
            foreach (var point in submitted)
            {
                if (stepsAdded >= maxSteps)
                    break;

                // snap to grid
                Vector3 snapped = GridSystem.GetNearestGridCell(point);

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

            sanitized[unit] = (kvp.Value.Item1, clean);
        }

        pathsList.Add(sanitized);
    }

    void ExecuteMoves(PathsDict paths)
    {
        // Get all units from all teams
        foreach (var team in teamNames)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                List<Vector3> movementPath = new();

                // Check if this unit has a path in the dictionary
                if (paths.ContainsKey(unit))
                {
                    movementPath = new List<Vector3>(paths[unit].Item2); // Create a new list copy
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
        foreach (var team in teamNames)
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
        foreach (var team in teamNames)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                if (unit.GetComponent<Shooting>().stillShooting == true)
                    return true;
            }
        }

        return false;
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

    public Color GetTeamColor(string team)
    {
        if (!teamNames.Contains(team))
        {
            Debug.LogWarning(
                $"[GameLoop] Team '{team}' not found in teams list, falling back to executingMoves color"
            );
            return executingMoves;
        }
        return teamColors[teamNames.IndexOf(team)];
    }

    public Material GetTeamMaterial(string team)
    {
        if (!teamNames.Contains(team))
        {
            Debug.LogWarning(
                $"[GameLoop] Team '{team}' not found in teams list, falling back to null material"
            );
            return null;
        }
        return teamMaterials[teamNames.IndexOf(team)];
    }

    public static string GetEnemyTeam(string team)
    {
        if (!teamNames.Contains(team))
        {
            Debug.LogWarning(
                $"[GameLoop] Team '{team}' not found in teams list, cannot determine enemy team, falling back to null"
            );
            return null;
        }
        return teamNames.FirstOrDefault(t => t != team);
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

    // [ClientRpc]
    // public void setOverlayUITextClientRpc(string message, string team = "neutral")
    // {
    //     overlayUIText.text = message;

    //     if (team == "neutral")
    //     {
    //         overlayUIText.color = executingMoves;
    //     }
    //     else
    //     {
    //         // Determine if this message is about the client's own team or enemy team
    //         ulong localClientId = NetworkManager.Singleton.LocalClientId;
    //         string localTeam = GetLocalClientTeam(localClientId);

    //         if (localTeam == team)
    //         {
    //             overlayUIText.color = teamColors[0];
    //         }
    //         else
    //         {
    //             overlayUIText.color = teamColors[1];
    //         }
    //     }
    // }

    /// <summary>
    /// Gets the perspective for a specific team relative to the local client
    /// </summary>
    // public MessagePerspective GetTeamPerspective(string team)
    // {
    //     ulong localClientId = NetworkManager.Singleton.LocalClientId;
    //     string localTeam = getTeamName(localClientId);

    //     if (team == localTeam)
    //         return MessagePerspective.Friendly;
    //     else if (team == "neutral" || team == "")
    //         return MessagePerspective.Neutral;
    //     else
    //         return MessagePerspective.Enemy;
    // }

    // public string getTeamName(ulong clientId)
    // {
    //     var entry = allTeamUnits.Keys
    //         .Select((key, idx) => new { key, idx })
    //         .FirstOrDefault(pair => pair.key == clientId);
    //     return entry != null ? teamNames[entry.idx] : null;
    // }

    // public static int GetTeamIndex(string team)
    // {
    //     if (!teamNames.Contains(team))
    //         return -1;
    //     return teamNames.IndexOf(team);
    // }
}
