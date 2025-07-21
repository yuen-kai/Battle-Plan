using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;

public class GameLoop : MonoBehaviour
{
    public List<string> teams = new List<string>() { "BlueTeam", "RedTeam" };
    List<GameObject> doneMovingUnits = new List<GameObject>();
    List<GameObject> doneShootingUnits = new List<GameObject>();
    public static float cellSize = 2.7f; // Size of each cell in the grid
    [SerializeField] private TMP_Text overlayUIText;
    [SerializeField] private List<Color> planningColors;
    [SerializeField] private Color executingMoves;
    float planningTimePerUnit = 2f; //TODO: CHANGE TO 4
    [SerializeField] private GameObject unitCards;

    void Start()
    {
        StartCoroutine(GameLoopTemp());
    }

    IEnumerator GameLoopTemp()
    {
        while (teams.All(team => teamSize(team) > 0))
        {
            List<Dictionary<GameObject, List<Vector3>>> pathsList = new List<Dictionary<GameObject, List<Vector3>>>();

            System.Action<Dictionary<GameObject, List<Vector3>>> addPaths = (Dictionary<GameObject, List<Vector3>> paths) =>
            {
                pathsList.Add(new Dictionary<GameObject, List<Vector3>>(paths));
            };

            setUnitCardsInteractable(false);
            
            float timerLength = planningTimePerUnit * teams.Max(teamSize);
            for (int i = 0; i < teams.Count; i++)
            {
                string team = teams[i];
                overlayUIText.text = $"Planning: {team}";
                overlayUIText.color = planningColors[i];
                
                yield return StartCoroutine(transform.GetComponent<PlanMovement>().ChoosePaths(team, addPaths, timerLength));
            }

            setUnitCardsInteractable(true);
            overlayUIText.text = "Executing Moves";
            overlayUIText.color = executingMoves;

            foreach (var paths in pathsList)
            {
                ExecuteMoves(paths);

            }

            while (CheckStillMoving())
            {
                yield return null;
            }
            OrderStopShooting();
            while (CheckStillShooting())
            {
                yield return null;
            }
        }
        Debug.Log("Game Over");
        //yield return StartCoroutine(GameObject.FindGameObjectsWithTag("BlueTeam")[0].GetComponent<Shooting>().StartShooting());
    }

    void setUnitCardsInteractable(bool interactable)
    {
        foreach (Transform child in unitCards.transform)
        {
            child.Find("TouchArea").GetComponent<UnityEngine.UI.Button>().interactable = interactable;
        }
    }


    void ExecuteMoves(Dictionary<GameObject, List<Vector3>> paths)
    {
        foreach (var pair in paths)
        {
            GameObject unit = pair.Key;
            List<Vector3> movementPath = pair.Value;
            unit.GetComponent<Movement>().StartMovement(new List<Vector3>(movementPath)); // C# passes parameters by reference, so we need to create a new list
        }
    }

    int teamSize(string team)
    {
        return GameObject.FindGameObjectsWithTag(team).Length;
    }


    bool CheckStillMoving()
    {
        foreach (var team in teams)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                if (unit.GetComponent<Movement>().moving == true) return true;
            }
        }

        return false;
    }

    void OrderStopShooting()
    {
        foreach (var team in teams)
        {
            foreach (GameObject unit in GameObject.FindGameObjectsWithTag(team))
            {
                unit.GetComponent<Shooting>().allowShooting = false;
            }
        }
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

    void PrintPaths(Dictionary<GameObject, List<Vector3>> paths)
    {
        foreach (var pair in paths)
        {
            Debug.Log($"Unit: {pair.Key.name}, Path: {string.Join(", ", pair.Value)}");
        }
    }

    // Update is called once per frame
    void Update()
    {

    }
}
