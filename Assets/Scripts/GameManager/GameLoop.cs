using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using OutlineEffect = cakeslice.OutlineEffect;

public enum MessagePerspective
{
    Neutral,
    Friendly,
    Enemy,
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

    public static List<int[]> allTeamUnits = new List<int[]>()
    {
        new int[] { 0, 1, 2 }, // Blue Team Units
        new int[] { 2, 3, 4 }, // Red Team Units
    };

    // Level Layout (col, row) from bottom left corner
    HashSet<Vector2Int> wallLayout = new HashSet<Vector2Int>()
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
            new Vector2Int(0, 9),
            new Vector2Int(4, 9),
            new Vector2Int(8, 9),
        },
    };

    // Game Settings
    float planningTimePerUnit = 5f;

    // Game State
    List<GameObject> doneMovingUnits = new List<GameObject>();
    List<GameObject> doneShootingUnits = new List<GameObject>();
    private Dictionary<ulong, int> clientIdToTeamIndex = new Dictionary<ulong, int>();

    // Actions
    public static System.Action<bool> setUnitCardsInteractable;
    public static System.Action<GameObject> disableUnitCard;
    public static System.Action<bool> OrderAllowShooting;
    public static System.Action<bool> OrderStillShooting;
    public static System.Action OrderContinueShooting;

    List<PathsDict> pathsList = new List<PathsDict>();

    public static GameLoop Instance { get; private set; }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;
        InitializeClientTeamMapping();
        Instance = this;
        StartGame();
        StartCoroutine(GameLoopTemp());
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
                allTeamUnits[i],
                unitCards[i],
                spawns[i],
                Quaternion.Euler(0, 180 * i, 0),
                teams[i]
            );
        }

        //Setup outline effect for teams
        Camera.main.GetComponent<OutlineEffect>().lineColor0 = GetTeamColor(teams[0]);
        Camera.main.GetComponent<OutlineEffect>().lineColor1 = GetTeamColor(teams[1]);
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
    }

    private int GetTeamIndexForClient(ulong clientId)
    {
        if (clientIdToTeamIndex.ContainsKey(clientId))
        {
            return clientIdToTeamIndex[clientId];
        }

        // Fallback: assign based on client order if not in mapping
        Debug.LogWarning("No team index found for client " + clientId);
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
        Debug.LogWarning("No client found for team Index " + teamIndex);
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

            setOverlayUITextPerspectiveClientRpc($"Planning", MessagePerspective.Friendly);
            StartPlanningClientRpc(endTime);

            while (
                pathsList.Count < teams.Count
                && NetworkManager.Singleton.ServerTime.Time < endTime + 1
            )
            {
                yield return null;
            }

            setUnitCardsInteractable?.Invoke(true);
            setOverlayUITextPerspectiveClientRpc("Executing Moves", MessagePerspective.Neutral);

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
        SceneManager.LoadScene("HomeScreen");
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
                // Friendly team - use their own team color
                overlayUIText.color = GetTeamColor(team);
            }
            else
            {
                // Enemy team - use the enemy team color (opposite team)
                string enemyTeam = GetEnemyTeam(localTeam);
                overlayUIText.color = GetTeamColor(enemyTeam);
            }
        }
    }

    /// <summary>
    /// Wrapper function that accepts "friendly", "enemy", or "neutral" instead of specific team names
    /// </summary>
    [ClientRpc]
    public void setOverlayUITextPerspectiveClientRpc(string message, MessagePerspective perspective)
    {
        overlayUIText.text = message;

        if (perspective == MessagePerspective.Neutral)
        {
            overlayUIText.color = executingMoves;
        }
        else if (perspective == MessagePerspective.Friendly)
        {
            // Show in the local client's team color
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            string localTeam = GetLocalClientTeam(localClientId);
            overlayUIText.color = GetTeamColor(localTeam);
        }
        else if (perspective == MessagePerspective.Enemy)
        {
            // Show in the enemy team's color
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            string localTeam = GetLocalClientTeam(localClientId);
            string enemyTeam = GetEnemyTeam(localTeam);
            overlayUIText.color = GetTeamColor(enemyTeam);
        }
        else
        {
            // Fallback to neutral color
            overlayUIText.color = executingMoves;
        }
    }

    /// <summary>
    /// Gets the team that the local client belongs to
    /// </summary>
    private string GetLocalClientTeam(ulong clientId)
    {
        // Find which team this client belongs to
        foreach (var kvp in clientIdToTeamIndex)
        {
            if (kvp.Key == clientId)
            {
                return teams[kvp.Value];
            }
        }

        // Fallback: if no mapping found, assume first team
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
            return executingMoves;
        return teamColors[teams.IndexOf(team)];
    }

    public Material GetTeamMaterial(string team)
    {
        if (!teams.Contains(team))
            return null;
        return teamMaterials[teams.IndexOf(team)];
    }

    public static string GetEnemyTeam(string team)
    {
        if (!teams.Contains(team))
            return null;
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
