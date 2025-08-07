using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;
using OutlineEffect = cakeslice.OutlineEffect;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class GameLoop : NetworkBehaviour
{
    // Grid Configuration
    public static float cellSize = 2.7f;
    public static Rect gridBounds = new Rect(new Vector2(0, 0), new Vector2(8, 9) * cellSize + new Vector2(0.1f, 0.1f));

    // UI Components
    [SerializeField] private TMP_Text overlayUIText;

    // Visual Properties
    public List<Color> teamColors;
    [SerializeField] private Color executingMoves;
    public List<Material> teamMaterials;

    // Game Setup: Objects and Prefabs
    [SerializeField] private GameObject wallPrefab;
    public GameObject wallsParent;
    public List<GameObject> allUnits;
    public List<GameObject> unitCardPrefabs;

    // Team Configuration
    public static List<string> teams = new List<string>() { "BlueTeam", "RedTeam" };
    [SerializeField] private List<GameObject> unitCards;
    [SerializeField] private GameObject unitCardsContainer;

    public static List<int[]> allTeamUnits = new List<int[]>()
        {
            new int[] { 0, 1, 2 }, // Blue Team Units
            new int[] { 2, 3, 4 }  // Red Team Units
        };

    // Level Layout (col, row) from bottom left corner
    HashSet<Vector2Int> wallLayout = new HashSet<Vector2Int>()
        {
            new Vector2Int(7, 2),
            new Vector2Int(1, 3),
            new Vector2Int(0, 3),
            new Vector2Int(6, 4),
            new Vector2Int(2, 5),
            new Vector2Int(8, 6),
            new Vector2Int(7, 6),
            new Vector2Int(1, 7)
        };

    List<HashSet<Vector2Int>> spawns = new List<HashSet<Vector2Int>>()
    {
        new HashSet<Vector2Int>() //Blue Team Spawn Positions
        {
            new Vector2Int(0, 0),
            new Vector2Int(4, 0),
            new Vector2Int(8, 0)
        },
        new HashSet<Vector2Int>() //Red Team Spawn Positions
        {
            new Vector2Int(0, 9),
            new Vector2Int(4, 9),
            new Vector2Int(8, 9)
        }
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

    public static GameLoop Instance
    {
        get; private set;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        InitializeClientTeamMapping();
        Instance = this;
        StartGame();
        StartCoroutine(GameLoopTemp());
    }

    void StartGame()
    {
        //// Setup walls
        //Destroy(wallsParent);
        //wallsParent = new GameObject("Walls");
        //foreach (var pos in wallLayout)
        //{
        //    Vector3 worldPos = gridCoordToWorld(pos);
        //    GameObject wall = NetworkHelper.SpawnNetworked(wallPrefab, worldPos, Quaternion.identity);
        //    wall.transform.position += Helper.heightOffset(wall.transform);
        //    wall.transform.SetParent(wallsParent.transform);
        //}

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
            SetupUnitsAndCards(allTeamUnits[i], unitCards[0], spawns[i], Quaternion.Euler(0, 180 * i, 0), teams[i]);
        }

        //Setup outline effect for teams
        Camera.main.GetComponent<OutlineEffect>().lineColor0 = GetTeamColor(teams[0]);
        Camera.main.GetComponent<OutlineEffect>().lineColor1 = GetTeamColor(teams[1]);
    }

    void SetupUnitsAndCards(int[] teamUnits, GameObject unitCardTeamContainer, HashSet<Vector2Int> spawnPositions, Quaternion rotation, string team)
    {

        for (int i = 0; i < teamUnits.Length; i++)
        {
            GameObject unit = NetworkHelper.Spawn(allUnits[teamUnits[i]], gridCoordToWorld(spawnPositions.ElementAt(i)), rotation, GetClientForTeamIndex(teams.IndexOf(team)));
            unit.transform.position += Helper.heightOffset(unit.transform);
            NetworkHelper.Instance.SetTag(unit, team);
            SetGroupLayerClientRpc(unit.GetComponent<NetworkObject>(), LayerMask.NameToLayer(team));

            GameObject newUnitCard = NetworkHelper.Spawn(unitCardPrefabs[teamUnits[i]], unitCardTeamContainer.transform, GetClientForTeamIndex(teams.IndexOf(team)));

            newUnitCard.GetComponent<ActivateAbility>().unit = unit; //no need to sync cause activate ability should only be handled by the server
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

    private ulong GetClientForTeamIndex(int teamIndex)
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
        if (!IsServer) yield break;
        yield return null;
        SetTeamIndicators();

        while (teams.All(team => teamSize(team) > 0))
        {
            pathsList = new List<PathsDict>();

            setUnitCardsInteractable?.Invoke(false);
            OrderStillShooting?.Invoke(true);

            float timerLength = planningTimePerUnit * teams.Max(teamSize);
            double endTime = NetworkManager.Singleton.ServerTime.Time + timerLength;

            setOverlayUITextClientRpc($"Planning", teams[0]);
            StartPlanningClientRpc(endTime);


            while (pathsList.Count < teams.Count && NetworkManager.Singleton.ServerTime.Time < endTime + 1)
            {
                yield return null;
            }

            setUnitCardsInteractable?.Invoke(true);
            setOverlayUITextClientRpc("Executing Moves", "neutral");

            
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
        StartCoroutine(transform.GetComponent<PlanMovement>().StartPlanning(SendPathsToServerRpc, endTime));
    }

    [ServerRpc(RequireOwnership = false)]
    void SendPathsToServerRpc(PathsDict paths)
    {
        pathsList.Add(paths);
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

    //public void SetGroupLayer(NetworkObjectReference objRef, int layer)
    //{

    //    SetGroupLayerClientRpc(NetworkObjectReference objRef, int layer);
    //}

    [ClientRpc]
    public void SetGroupLayerClientRpc(NetworkObjectReference objRef, int layer)
    {
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

    void SetTeamIndicators()
    {
        GameObject[] teamIndicators = GameObject.FindGameObjectsWithTag("TeamIndicatorProp");
        foreach (GameObject indicator in teamIndicators)
        {
            string unitTeam = null;

            //Loop through parents to find team tag
            Transform current = indicator.transform;
            while (current != null)
            {
                if (teams.Contains(current.tag))
                {
                    unitTeam = current.tag;
                    break;
                }
                current = current.parent;
            }

            //Set material based on team tag
            if (unitTeam != null)
            {
                Renderer renderer = indicator.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.materials = new Material[] { GetTeamMaterial(unitTeam) };
                }
            }
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
                if (unit.GetComponent<Shooting>().stillShooting == true) return true;
            }
        }

        return false;
    }

    [ClientRpc]
    public void setOverlayUITextClientRpc(string message, string team = "neutral")
    {
        overlayUIText.text = message;
        overlayUIText.color = GetTeamColor(team);
    }

    public static int GetTeamIndex(string team)
    {
        if (!teams.Contains(team)) return -1;
        return teams.IndexOf(team);
    }

    public Color GetTeamColor(string team)
    {
        if (!teams.Contains(team)) return executingMoves;
        return teamColors[teams.IndexOf(team)];
    }

    public Material GetTeamMaterial(string team)
    {
        if (!teams.Contains(team)) return null;
        return teamMaterials[teams.IndexOf(team)];
    }

    public static string GetEnemyTeam(string team)
    {
        if (!teams.Contains(team)) return null;
        return teams.FirstOrDefault(t => t != team);
    }

    public static int teamSize(string team)
    {
        return GameObject.FindGameObjectsWithTag(team).Length;
    }

    public static Vector3 gridCoordToWorld(Vector2Int coords)
    {
        return new Vector3(gridBounds.xMin + coords.x * cellSize, 0, gridBounds.yMin + coords.y * cellSize);
    }
}
