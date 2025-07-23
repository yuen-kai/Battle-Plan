using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ActivateAbility : MonoBehaviour
{
    public GameObject unit;
    public PlanMovement PlanMovementScript;
    public GameLoop gameLoopScript;
    [SerializeField] private TMP_Text timerTextUI;

    [SerializeField] private bool selectAbilitySquare;
    [SerializeField] private GameObject abilitySquareIndicator;
    [SerializeField] private float abilitySquareRange = 3f;
    [SerializeField] private float selectTime = 4f;

    [SerializeField] private float responseRange = 5f;

    [SerializeField] private float timeDivePerUnit = 3f;
    [SerializeField] private int diveRange = 2;

    public int uses = 1; // Number of times the ability can be used

    public void activateAbility()
    {
        uses--;

        //pause time
        Time.timeScale = 0f;
        gameLoopScript.setUnitCardsInteractable(false);

        StartCoroutine(selectAbilitySquareFunc());
    }

    IEnumerator selectAbilitySquareFunc()
    {
        if (!selectAbilitySquare)
        {
            planEnemyResponse(unit.transform.position);
            yield break;
        }

        gameLoopScript.setOverlayUIText($"{unit.name}:\nSelect ability target square", unit.tag);


        float timeRemaining = selectTime;
        Vector3 selectedSquare = unit.transform.position; //TODO: change default selection
        GameObject abilityIndicator = null;

        while (timeRemaining > 0f)
        {
            timerTextUI.text = (Mathf.CeilToInt(timeRemaining)).ToString();

            if (Input.GetMouseButtonDown(0))
            {
                Vector3 mouseSquare = PlanMovement.GetGridCellUnderMouse();
                if (Vector3.Distance(unit.transform.position, mouseSquare) <= abilitySquareRange * GameLoop.cellSize)
                {
                    selectedSquare = mouseSquare;
                    if (abilityIndicator) Destroy(abilityIndicator);
                    abilityIndicator = Instantiate(abilitySquareIndicator, mouseSquare, Quaternion.identity);
                }
                else
                {
                    Debug.Log($"Too far! Range: {abilitySquareRange}");
                }
            }

            timeRemaining -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (abilityIndicator) Destroy(abilityIndicator);
        timerTextUI.text = "";

        planEnemyResponse(selectedSquare);
    }

    void planEnemyResponse(Vector3 abilitySquare)
    {
        //Response
        string enemyTeam = unit.tag == "BlueTeam" ? "RedTeam" : "BlueTeam";

        List<GameObject> enemiesInRange = GetEnemiesInRange(abilitySquare, enemyTeam);

        gameLoopScript.setOverlayUIText($"Dodging: {enemyTeam}", enemyTeam);

        StartCoroutine(PlanMovementScript.ChoosePaths(enemyTeam, (Dictionary<GameObject, List<Vector3>> paths) =>
        {
            gameLoopScript.setOverlayUIText("Executing Moves", "neutral");

            //continue time
            Time.timeScale = 1f;
            gameLoopScript.setUnitCardsInteractable(true);

            //execute dive
            foreach (var pair in paths)
            {
                GameObject unit = pair.Key;
                List<Vector3> movementPath = pair.Value;
                if(movementPath.Count == 0) continue; //skip if no path
                unit.GetComponent<Shooting>().StopShooting();
                unit.GetComponent<Movement>().StartMovement(new List<Vector3>(movementPath), true);
            }

            //activate ability
            StartCoroutine(unit.GetComponent<IAbility>().ExecuteAbility(abilitySquare));
        }, timeDivePerUnit * enemiesInRange.Count, enemiesInRange, diveRange));
    }

    public List<GameObject> GetEnemiesInRange(Vector3 abilitySquare, string enemyTeam)
    {
        List<GameObject> enemiesInRange = new List<GameObject>();
        GameObject[] allEnemies = GameObject.FindGameObjectsWithTag(enemyTeam);

        foreach (GameObject enemy in allEnemies)
        {
            float distance = Vector3.Distance(abilitySquare, enemy.transform.position);
            if (distance <= responseRange * GameLoop.cellSize)
            {
                enemiesInRange.Add(enemy);
            }
        }

        return enemiesInRange;
    }
}
