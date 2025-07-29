using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ActivateAbility : MonoBehaviour
{
    public GameObject unit;

    public UnitData unitData;
    public int uses;

    [SerializeField] private GameObject abilityRangeOverlayPrefab;
    [SerializeField] private GameObject abilitySquareIndicatorPrefab;
    [SerializeField] private GameObject abilityAOEIndicatorPrefab;

    GameObject abilityRangeOverlay;

    void Start()
    {
        uses = unitData.uses;
        GameLoop.setUnitCardsInteractable += setUnitCardsInteractable;
        GameLoop.disableUnitCard += disableUnitCard;
    }

    public void setUnitCardsInteractable(bool interactable)
    {
        if (uses <= 0 || unit == null)
        {
            transform.Find("TouchArea").GetComponent<UnityEngine.UI.Button>().interactable = false;
        }
        else
        {
            transform.Find("TouchArea").GetComponent<UnityEngine.UI.Button>().interactable = interactable;
        }
    }

    public void disableUnitCard(GameObject disableUnit)
    {
        if (unit == disableUnit)
        {
            transform.Find("TouchArea").GetComponent<UnityEngine.UI.Button>().interactable = false;
            return;
        }
    }

    public void activateAbility()
    {
        uses--;

        //pause time
        Time.timeScale = 0f;
        GameLoop.setUnitCardsInteractable(false);

        StartCoroutine(selectAbilitySquareFunc());
    }

    IEnumerator selectAbilitySquareFunc()
    {
        if (!unitData.selectAbilitySquare)
        {
            planEnemyResponse(unit.transform.position);
            yield break;
        }

        GameLoop.Instance.setOverlayUIText($"{unit.name}:\nSelect ability target square", unit.tag);

        Destroy(abilityRangeOverlay);
        Vector3 nearestCell = PlanMovement.GetGridCellUnderCharacter(unit);
        abilityRangeOverlay = Helper.DisplayGridRange(nearestCell, unitData.abilitySquareRange, abilityRangeOverlayPrefab);


        float timeRemaining = unitData.selectTime;
        Vector3 selectedSquare = PlanMovement.GetGridCellUnderCharacter(unit); //TODO: change default selection
        GameObject abilityIndicator = null;
        GameObject AOEindicator = null;

        while (timeRemaining > 0f)
        {
            PlanMovement.Instance.timerTextUI.text = (Mathf.CeilToInt(timeRemaining)).ToString();

            if (Input.GetMouseButtonDown(0))
            {
                Vector3? potentialSquare = PlanMovement.GetGridCellUnderMouse();
                if (potentialSquare == null) continue;
                Vector3 mouseSquare = potentialSquare.Value;

                float horizontalDistance = Mathf.Abs(nearestCell.x - mouseSquare.x);
                float verticalDistance = Mathf.Abs(nearestCell.z - mouseSquare.z);

                if (horizontalDistance + verticalDistance <= (unitData.abilitySquareRange + 0.1f) * GameLoop.cellSize)
                {
                    selectedSquare = mouseSquare;
                    Destroy(abilityIndicator);
                    Destroy(AOEindicator);
                    abilityIndicator = Instantiate(abilitySquareIndicatorPrefab, mouseSquare, Quaternion.identity);
                    AOEindicator = Instantiate(abilityAOEIndicatorPrefab, mouseSquare, Quaternion.identity);
                    float diameter = 2 * unitData.abilityRadius * GameLoop.cellSize;
                    AOEindicator.transform.localScale = new Vector3(diameter, 0.05f, diameter);

                    string enemyTeam = GameLoop.GetEnemyTeam(unit.tag);

                    // Clear previous alerts
                    GameObject[] allEnemies = GameObject.FindGameObjectsWithTag(enemyTeam);
                    foreach (GameObject enemy in allEnemies)
                    {
                        enemy.transform.Find("UnitCanvas").Find("Alert").gameObject.SetActive(false);
                    }

                    List<GameObject> enemiesInRange = GetEnemiesInRange(selectedSquare, enemyTeam);
                    foreach (GameObject enemy in enemiesInRange)
                    {
                        enemy.transform.Find("UnitCanvas").Find("Alert").gameObject.SetActive(true);
                    }
                }
                else
                {
                    Debug.Log($"Too far! Range: {unitData.abilitySquareRange}");
                }
            }

            timeRemaining -= Time.unscaledDeltaTime;
            yield return null;
        }

        Destroy(abilityIndicator);
        Destroy(AOEindicator);
        Destroy(abilityRangeOverlay);

        PlanMovement.Instance.timerTextUI.text = "";

        planEnemyResponse(selectedSquare);
    }

    void planEnemyResponse(Vector3 abilitySquare)
    {
        //Response
        string enemyTeam = GameLoop.GetEnemyTeam(unit.tag);

        List<GameObject> enemiesInRange = GetEnemiesInRange(abilitySquare, enemyTeam);


        GameLoop.Instance.setOverlayUIText($"Dodging: {enemyTeam}", enemyTeam);

        StartCoroutine(PlanMovement.Instance.ChoosePaths(enemyTeam, (Dictionary<GameObject, List<Vector3>> paths) =>
        {
            GameLoop.Instance.setOverlayUIText("Executing Moves", "neutral");

            //continue time
            Time.timeScale = 1f;
            GameLoop.setUnitCardsInteractable(true);

            foreach (GameObject enemy in enemiesInRange)
            {
                enemy.transform.Find("UnitCanvas").Find("Alert").gameObject.SetActive(false);
            }

            //execute dive
            foreach (var pair in paths)
            {
                GameObject unit = pair.Key;
                List<Vector3> movementPath = pair.Value;
                if (movementPath.Count == 0) continue; //skip if no path
                unit.GetComponent<Shooting>().StopShooting();
                unit.GetComponent<Movement>().StartMovement(new List<Vector3>(movementPath), true);
            }

            //activate ability
            StartCoroutine(unit.GetComponent<IAbility>().ExecuteAbility(abilitySquare, unitData.abilityRadius));
        }, unitData.timeDivePerUnit * enemiesInRange.Count, enemiesInRange, unitData.diveRange));
    }

    public List<GameObject> GetEnemiesInRange(Vector3 abilitySquare, string enemyTeam)
    {
        List<GameObject> enemiesInRange = new List<GameObject>();
        GameObject[] allEnemies = GameObject.FindGameObjectsWithTag(enemyTeam);

        foreach (GameObject enemy in allEnemies)
        {
            float distance = Vector3.Distance(abilitySquare, enemy.transform.position);
            if (distance <= unitData.responseRange * GameLoop.cellSize)
            {
                enemiesInRange.Add(enemy);
            }
        }

        return enemiesInRange;
    }
}
