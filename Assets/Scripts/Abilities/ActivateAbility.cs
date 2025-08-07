using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Netcode;

public class ActivateAbility : NetworkBehaviour
{
    public GameObject unit;

    public UnitData unitData;
    public int uses;

    [SerializeField] private GameObject abilityRangeOverlayPrefab;
    [SerializeField] private GameObject abilitySquareIndicatorPrefab;
    [SerializeField] private GameObject abilityAOEIndicatorPrefab;

    GameObject abilityRangeOverlay;
    LineRenderer laserLine;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            gameObject.SetActive(false);
            return;
        }
        uses = unitData.uses;
        GameLoop.setUnitCardsInteractable += setUnitCardInteractable;
        GameLoop.disableUnitCard += disableUnitCard;
    }

    public override void OnDestroy()
    {
        GameLoop.setUnitCardsInteractable -= setUnitCardInteractable;
        GameLoop.disableUnitCard -= disableUnitCard;
    }

    public void setUnitCardInteractable(bool interactable)
    {
        if (uses <= 0 || !unit.activeInHierarchy)
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

        GameLoop.Instance.setOverlayUITextClientRpc($"{unit.name}:\nSelect ability target square", unit.tag);

        Destroy(abilityRangeOverlay);
        Vector3 nearestCell = PlanMovement.GetGridCellUnderCharacter(unit);
        abilityRangeOverlay = Helper.DisplayGridRange(nearestCell, unitData.abilitySquareRange, abilityRangeOverlayPrefab);

        float timeRemaining = unitData.selectTime;
        Vector3 selectedSquare = PlanMovement.GetGridCellUnderCharacter(unit); //TODO: change default selection
        GameObject abilityIndicator = null;
        GameObject AOEindicator = null;

        if(unitData.responseDistLine)
        {
            unit.transform.position = PlanMovement.GetNearestGridCell(unit.transform.position) + Helper.heightOffset(unit.transform); //snap to nearest cell
        }

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
        Destroy(laserLine?.gameObject);

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

        if(unitData.responseDistLine)
        {
            Destroy(laserLine?.gameObject);
            Vector3 targetPosition = selectedSquare + Helper.heightOffset(unit.transform);
            CreateLaserLine(unit.transform.position, targetPosition);
        }
    }

    private void CreateLaserLine(Vector3 start, Vector3 end)
    {
        Vector3 direction = (end - start).normalized;
        Vector3 finalEnd = end + direction * 50f;

        // Stop if there is a wall in the way
        if (Physics.Raycast(start, direction, out RaycastHit hit, Mathf.Infinity, LayerMask.GetMask("Walls")))
        {
            finalEnd = hit.point;
        }

        GameObject laserObject = new GameObject("LaserLinePreview");
        laserLine = laserObject.AddComponent<LineRenderer>();
        
        Material laserMaterial = new Material(Shader.Find("Unlit/Color"));
        laserMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        laserMaterial.color = Color.red;
        laserLine.material = laserMaterial;

        laserLine.startColor = Color.red;
        laserLine.endColor = Color.red;
        laserLine.startWidth = laserLine.endWidth = 0.1f;
        laserLine.positionCount = 2;
        laserLine.SetPosition(0, start);
        laserLine.SetPosition(1, finalEnd);
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
        List<GameObject> enemiesInRange = !unitData.responseDistLine ? GetUnitsInRange(selectedSquare, enemyTeam, unitData.responseRange) : GetUnitsInRangeofLine(selectedSquare, enemyTeam, unitData.responseRange);
        foreach (GameObject enemy in enemiesInRange)
        {
            enemy.transform.Find("UnitCanvas").Find("Alert").gameObject.SetActive(true);
        }
    }

    void planEnemyResponse(Vector3 abilitySquare)
    {
        //Response
        string enemyTeam = GameLoop.GetEnemyTeam(unit.tag);
        
        List<GameObject> enemiesInRange = !unitData.responseDistLine ? GetUnitsInRange(abilitySquare, enemyTeam, unitData.responseRange) : GetUnitsInRangeofLine(abilitySquare, enemyTeam, unitData.responseRange);


        GameLoop.Instance.setOverlayUITextClientRpc($"Dodging: {enemyTeam}", enemyTeam);

        StartCoroutine(PlanMovement.Instance.ChoosePaths(enemyTeam, (PathsDict paths) =>
        {
            GameLoop.Instance.setOverlayUITextClientRpc("Executing Moves", "neutral");

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
                unit.GetComponent<Shooting>().PauseShooting();
                unit.GetComponent<Movement>().StartMovement(new List<Vector3>(movementPath), true);
            }

            //activate ability
            StartCoroutine(unit.GetComponent<Ability>().ExecuteAbility(abilitySquare, unitData.abilityRadius));
        }, unitData.timeDivePerUnit * enemiesInRange.Count, enemiesInRange, unitData.diveRange));
    }

    public static List<GameObject> GetUnitsInRange(Vector3 abilitySquare, string team, float range)
    {
        List<GameObject> unitsInRange = new List<GameObject>();
        GameObject[] allUnits = GameObject.FindGameObjectsWithTag(team);

        foreach (GameObject unit in allUnits)
        {
            float distance = Vector3.Distance(abilitySquare, unit.transform.position);
            if (distance <= range * GameLoop.cellSize)
            {
                unitsInRange.Add(unit);
            }
        }

        return unitsInRange;
    }

    public List<GameObject> GetUnitsInRangeofLine(Vector3 abilitySquare, string team, float range)
    {
        List<GameObject> unitsInRange = new List<GameObject>();
        GameObject[] allUnits = GameObject.FindGameObjectsWithTag(team);

        Vector3 start = unit.transform.position;
        Vector3 direction = (abilitySquare - start).normalized;
        Vector3 end = abilitySquare + direction * 50f;

        // Stop if there is a wall in the way
        if (Physics.Raycast(start, direction, out RaycastHit hit, Mathf.Infinity, LayerMask.GetMask("Walls")))
        {
            end = hit.point;
        }

        foreach (GameObject targetUnit in allUnits)
        {
            Vector3 unitPosition = targetUnit.transform.position;
            float distanceToLine = DistancePointToLineSegment(unitPosition, start, end);
            if (distanceToLine <= range * GameLoop.cellSize)
            {
                unitsInRange.Add(targetUnit);
            }
        }

        return unitsInRange;
    }

    private float DistancePointToLineSegment(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        // Ignore Y values for 2D calculation
        Vector2 point2D = new Vector2(point.x, point.z);
        Vector2 lineStart2D = new Vector2(lineStart.x, lineStart.z);
        Vector2 lineEnd2D = new Vector2(lineEnd.x, lineEnd.z);

        Vector2 lineDirection = lineEnd2D - lineStart2D;
        float lineLength = lineDirection.magnitude;

        if (lineDirection == Vector2.zero)
            return Vector2.Distance(point2D, lineStart2D);

        Vector2 lineDirectionNormalized = lineDirection.normalized;
        Vector2 pointToStart = point2D - lineStart2D;
        float projectionLength = Vector2.Dot(pointToStart, lineDirectionNormalized);

        if (projectionLength < 0f)
        {
            return 999;
        }
        else if (projectionLength > lineLength)
        {
            return Vector2.Distance(point2D, lineEnd2D);
        }
        else
        {
            Vector2 closestPointOnLine = lineStart2D + lineDirectionNormalized * projectionLength;
            return Vector2.Distance(point2D, closestPointOnLine);
        }
    }

}
