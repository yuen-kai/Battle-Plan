using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;
using OutlineEffect = cakeslice.OutlineEffect;
using UnityEngine.SceneManagement;


public class GameLoop : MonoBehaviour
{
    // Grid Configuration
    public static float cellSize = 2.7f; // Size of each cell in the grid
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
    [SerializeField] private List<GameObject> teamParents;
    [SerializeField] private List<GameObject> unitCards;
    public static List<int[]> teamUnits = new List<int[]>()
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
    float planningTimePerUnit = 1f;

    // Game State
    List<GameObject> doneMovingUnits = new List<GameObject>();
    List<GameObject> doneShootingUnits = new List<GameObject>();

    // Actions
    public static System.Action<bool> setUnitCardsInteractable;
    public static System.Action<GameObject> disableUnitCard;
    public static System.Action<bool> OrderAllowShooting;
    public static System.Action<bool> OrderStillShooting;
    public static System.Action OrderContinueShooting;

    public static GameLoop Instance
    {
        get; private set;
    }

    void Awake()
    {
        Instance = this;
        StartGame();
    }

    void StartGame()
    {
        //// Setup walls
        //Destroy(wallsParent);
        //wallsParent = new GameObject("Walls");
        //foreach (var pos in wallLayout)
        //{
        //    Vector3 worldPos = gridCoordToWorld(pos);
        //    GameObject wall = Instantiate(wallPrefab, worldPos, Quaternion.identity);
        //    wall.transform.position += Helper.heightOffset(wall.transform);
        //    wall.transform.SetParent(wallsParent.transform);
        //}

        //Setup teams
        for (int i = 0; i < teams.Count; i++)
        {
            Destroy(teamParents[i]);
            teamParents[i] = SetupUnitsAndCards(teamUnits[i], unitCards[i], spawns[i], Quaternion.Euler(0, 180 * i, 0), teams[i]);
        }

        //Setup outline effect for teams
        Camera.main.GetComponent<OutlineEffect>().lineColor0 = GetTeamColor(teams[0]);
        Camera.main.GetComponent<OutlineEffect>().lineColor1 = GetTeamColor(teams[1]);
    }

    GameObject SetupUnitsAndCards(int[] teamUnits, GameObject unitCardTeamContainer, HashSet<Vector2Int> spawnPositions, Quaternion rotation, string team)
    {
        GameObject teamParent = new GameObject(team);
        for (int i = 0; i < teamUnits.Length; i++)
        {
            GameObject oldUnitCard = unitCardTeamContainer.transform.GetChild(i).gameObject;
            GameObject newUnitCard = Instantiate(unitCardPrefabs[teamUnits[i]], oldUnitCard.transform.position, oldUnitCard.transform.rotation, unitCardTeamContainer.transform);
            Destroy(oldUnitCard);

            GameObject unit = Instantiate(allUnits[teamUnits[i]], gridCoordToWorld(spawnPositions.ElementAt(i)), rotation);
            unit.transform.position += Helper.heightOffset(unit.transform);
            unit.transform.SetParent(teamParent.transform);
            unit.tag = team;
            SetGroupLayer(unit, LayerMask.NameToLayer(team));

            newUnitCard.GetComponent<ActivateAbility>().unit = unit;
        }
        return teamParent;
    }

    void Start()
    {
        StartCoroutine(GameLoopTemp());
    }

    IEnumerator GameLoopTemp()
    {
        yield return null;
        SetTeamIndicators();

        while (teams.All(team => teamSize(team) > 0))
        {
            List<PathsDict> pathsList = new List<PathsDict>();

            System.Action<PathsDict> addPaths = (PathsDict paths) =>
            {
                pathsList.Add(new PathsDict(paths));
            };

            setUnitCardsInteractable?.Invoke(false);
            OrderStillShooting(true);

            float timerLength = planningTimePerUnit * teams.Max(teamSize);
            foreach (var team in teams)
            {
                setOverlayUIText($"Planning: {team}", team);

                yield return StartCoroutine(transform.GetComponent<PlanMovement>().ChoosePaths(team, addPaths, timerLength));
            }

            setUnitCardsInteractable?.Invoke(true);
            setOverlayUIText("Executing Moves", "neutral");

            foreach (var paths in pathsList)
            {
                ExecuteMoves(paths);

            }

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

    void ExecuteMoves(PathsDict paths)
    {
        foreach (var pair in paths)
        {
            GameObject unit = pair.Key;
            List<Vector3> movementPath = pair.Value;
            unit.GetComponent<Movement>().StartMovement(new List<Vector3>(movementPath)); // C# passes parameters by reference, so we need to create a new list
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


    public void setOverlayUIText(string message, string team = "neutral")
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
