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
        GameLoop.setUnitCardsInteractable += setUnitCardInteractable;
        GameLoop.disableUnitCard += disableUnitCard;
    }

    public void setUnitCardInteractable(bool interactable)
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
        GameLoop.setUnitCardsInteractable?.Invoke(false);

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
                HandleMouseClick(nearestCell, ref selectedSquare, ref abilityIndicator, ref AOEindicator);
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

    private void HandleMouseClick(Vector3 nearestCell, ref Vector3 selectedSquare, ref GameObject abilityIndicator, ref GameObject AOEindicator)
    {
        Vector3? potentialSquare = PlanMovement.GetGridCellUnderMouse();
        if (!potentialSquare.HasValue) return;

        Vector3 mouseSquare = potentialSquare.Value;
        float horizontalDistance = Mathf.Abs(nearestCell.x - mouseSquare.x);
        float verticalDistance = Mathf.Abs(nearestCell.z - mouseSquare.z);

        if (horizontalDistance + verticalDistance > (unitData.abilitySquareRange + 0.1f) * GameLoop.cellSize) return;

        selectedSquare = mouseSquare;
        UpdateAbilityIndicators(mouseSquare, ref abilityIndicator, ref AOEindicator);
        UpdateEnemyAlerts(selectedSquare);
    }

    private void UpdateAbilityIndicators(Vector3 mouseSquare, ref GameObject abilityIndicator, ref GameObject AOEindicator)
    {
        Destroy(abilityIndicator);
        Destroy(AOEindicator);
        
        abilityIndicator = Instantiate(abilitySquareIndicatorPrefab, mouseSquare, Quaternion.identity);
        AOEindicator = Instantiate(abilityAOEIndicatorPrefab, mouseSquare, Quaternion.identity);
        
        float diameter = 2 * unitData.abilityRadius * GameLoop.cellSize;
        AOEindicator.transform.localScale = new Vector3(diameter, 0.05f, diameter);
    }

    private void UpdateEnemyAlerts(Vector3 selectedSquare)
    {
        string enemyTeam = GameLoop.GetEnemyTeam(unit.tag);
        GameObject[] allEnemies = GameObject.FindGameObjectsWithTag(enemyTeam);
        
        // Clear previous alerts
        foreach (GameObject enemy in allEnemies)
        {
            enemy.transform.Find("UnitCanvas").Find("Alert").gameObject.SetActive(false);
        }

        // Set alerts for enemies in range
        List<GameObject> enemiesInRange = GetEnemiesInRange(selectedSquare, enemyTeam);
        foreach (GameObject enemy in enemiesInRange)
        {
            enemy.transform.Find("UnitCanvas").Find("Alert").gameObject.SetActive(true);
        }
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
