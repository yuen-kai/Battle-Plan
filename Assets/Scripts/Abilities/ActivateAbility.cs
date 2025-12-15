using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class ActivateAbility : NetworkBehaviour
{
    public GameObject unit;

    public UnitData unitData;
    public int uses;

    [SerializeField]
    private GameObject abilityRangeOverlayPrefab;

    [SerializeField]
    private GameObject abilitySquareIndicatorPrefab;

    [SerializeField]
    private GameObject abilityAOEIndicatorPrefab;

    GameObject abilityRangeOverlay;
    LineRenderer laserLine;

    List<GameObject> enemiesInRange = new List<GameObject>();
    Vector3 selectedSquare;
    private ulong allowedDodgerClientId;
    private NetworkVariable<NetworkObjectReference> unitNetRef =
        new NetworkVariable<NetworkObjectReference>();

    // Global indicator references per-client
    private GameObject globalAbilityIndicator;
    private GameObject globalAOEIndicator;
    private LineRenderer globalAbilityLaser;

    public override void OnNetworkSpawn()
    {
        uses = unitData.uses;
        GameLoop.setUnitCardsInteractable += setUnitCardInteractable;
        GameLoop.disableUnitCard += disableUnitCard;

        // keep clients updated with the linked unit
        unitNetRef.OnValueChanged += OnUnitRefChanged;
        if (IsServer && unit != null)
        {
            var no = unit.GetComponent<NetworkObject>();
            if (no != null)
            {
                unitNetRef.Value = no;
            }
        }
    }

    public override void OnDestroy()
    {
        GameLoop.setUnitCardsInteractable -= setUnitCardInteractable;
        GameLoop.disableUnitCard -= disableUnitCard;
        unitNetRef.OnValueChanged -= OnUnitRefChanged;
    }

    private void OnUnitRefChanged(NetworkObjectReference prev, NetworkObjectReference next)
    {
        if (next.TryGet(out NetworkObject netObj))
        {
            unit = netObj.gameObject;
        }
        else
        {
            Debug.LogWarning(
                "[ActivateAbility] Failed to resolve unit NetworkObjectReference, unit reference will be null"
            );
        }
    }

    // Called by server during setup to bind this card to its unit and sync to clients
    public void SetUnitServer(GameObject targetUnit)
    {
        if (!IsServer || targetUnit == null)
            return;
        unit = targetUnit;
        var no = targetUnit.GetComponent<NetworkObject>();
        if (no != null)
        {
            unitNetRef.Value = no;
        }
    }

    public void setUnitCardInteractable(bool interactable)
    {
        if (uses <= 0 || unit == null || !unit.activeInHierarchy)
        {
            interactable = false;
        }
        setUnitCardInteractableClientRpc(interactable);
    }

    [ClientRpc]
    public void setUnitCardInteractableClientRpc(bool interactable)
    {
        if (!IsOwner && !IsServer)
            return;
        transform.Find("TouchArea").GetComponent<UnityEngine.UI.Button>().interactable =
            interactable;
    }

    public void disableUnitCard(GameObject disableUnit)
    {
        if (unit != null && unit == disableUnit)
        {
            disableUnitCardClientRpc();
        }
    }

    [ClientRpc]
    public void disableUnitCardClientRpc()
    {
        transform.Find("TouchArea").GetComponent<UnityEngine.UI.Button>().interactable = false;
    }

    // Pressed on client
    public void OnAbilityPressed()
    {
        PlanMovement.Instance.SwitchToUnit(unit);
        // activateAbilityServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    public void activateAbilityServerRpc(ServerRpcParams rpcParams = default)
    {
        if (unit == null)
            return;
        var netObj = unit.GetComponent<NetworkObject>();
        if (netObj == null || !netObj.IsSpawned)
            return;
        if (netObj.OwnerClientId != rpcParams.Receive.SenderClientId)
            return;
        if (uses <= 0)
            return;

        uses--;

        //pause time
        Time.timeScale = 0f;
        GameLoop.setUnitCardsInteractable?.Invoke(false);

        double endTime = NetworkManager.ServerTime.Time + unitData.selectTime;
        string team = unit.tag;

        CameraEffects.Instance?.FlashClientRpc(team);
        GameLoop.Instance?.setOverlayUITextClientRpc(
            $"{unit.name}:\nSelect ability target square",
            team
        );
        selectAbilitySquareFuncClientRpc(endTime, unit);
    }

    [ClientRpc]
    void selectAbilitySquareFuncClientRpc(double endTime, NetworkObjectReference unitRef)
    {
        if (IsOwner)
        {
            StartCoroutine(selectAbilitySquareFunc(endTime, unitRef));
        }
        else
        {
            StartCoroutine(CountDown(endTime));
        }
    }

    //On client
    public IEnumerator CountDown(double endTime)
    {
        float timeRemaining;
        while ((timeRemaining = (float)(endTime - NetworkManager.Singleton.ServerTime.Time)) > 0f)
        {
            PlanMovement.Instance.timerTextUI.text = (Mathf.CeilToInt(timeRemaining)).ToString();
            yield return null;
        }
        PlanMovement.Instance.timerTextUI.text = "";
    }

    //On client
    IEnumerator selectAbilitySquareFunc(double endTime, NetworkObjectReference unitRef)
    {
        if (!unitRef.TryGet(out NetworkObject unitNetObj))
        {
            yield break;
        }
        GameObject unit = unitNetObj.gameObject;
        if (!unitData.selectAbilitySquare)
        {
            planEnemyResponseServerRpc(unit.transform.position);
            yield break;
        }

        // Overlay text is broadcast by server already

        Destroy(abilityRangeOverlay);
        Vector3 nearestCell = PlanMovement.GetGridCellUnderCharacter(unit);
        abilityRangeOverlay = Helper.DisplayGridRange(
            nearestCell,
            unitData.abilitySquareRange,
            abilityRangeOverlayPrefab
        );

        Vector3 selectedSquare = PlanMovement.GetGridCellUnderCharacter(unit); // default selection

        if (unitData.responseDistLine)
        {
            unit.transform.position =
                PlanMovement.GetNearestGridCell(unit.transform.position)
                + Helper.heightOffset(unit.transform); //snap to nearest cell
        }

        float timeRemaining;
        while ((timeRemaining = (float)(endTime - NetworkManager.Singleton.ServerTime.Time)) > 0f)
        {
            PlanMovement.Instance.timerTextUI.text = Mathf.CeilToInt(timeRemaining).ToString();

            if (Input.GetMouseButtonDown(0))
            {
                HandleMouseClick(nearestCell, ref selectedSquare);
            }

            yield return null;
        }

        Destroy(abilityRangeOverlay);

        PlanMovement.Instance.timerTextUI.text = "";

        planEnemyResponseServerRpc(selectedSquare);
    }

    [ServerRpc(RequireOwnership = false)]
    void planEnemyResponseServerRpc(Vector3 abilitySquare, ServerRpcParams rpcParams = default)
    {
        if (unit == null)
            return;
        var netObj = unit.GetComponent<NetworkObject>();
        if (netObj == null || !netObj.IsSpawned)
            return;
        if (netObj.OwnerClientId != rpcParams.Receive.SenderClientId)
            return;

        selectedSquare = abilitySquare;
        //Response
        string enemyTeam = GameLoop.GetEnemyTeam(unit.tag);

        enemiesInRange = !unitData.responseDistLine
            ? GetUnitsInRange(abilitySquare, enemyTeam, unitData.responseRange)
            : GetUnitsInRangeofLine(abilitySquare, enemyTeam, unitData.responseRange);

        CameraEffects.Instance?.FlashClientRpc(enemyTeam);
        GameLoop.Instance?.setOverlayUITextClientRpc("Dodging", enemyTeam);
        double endTime =
            NetworkManager.ServerTime.Time + unitData.timeDivePerUnit * enemiesInRange.Count;

        //convert from List<GameObject> to NetworkObjectReference[]
        NetworkObjectReference[] enemiesInRangeArray = new NetworkObjectReference[
            enemiesInRange.Count
        ];
        for (int i = 0; i < enemiesInRangeArray.Length; i++)
        {
            GameObject enemy = enemiesInRange[i];
            if (enemy.TryGetComponent<NetworkObject>(out NetworkObject networkObject))
            {
                enemiesInRangeArray[i] = networkObject;
            }
        }

        allowedDodgerClientId = GameLoop.Instance.GetClient(enemyTeam);
        planEnemyResponseOptionsClientRpc(
            endTime,
            enemiesInRangeArray,
            unitData.diveRange,
            allowedDodgerClientId
        );
    }

    [ClientRpc]
    void planEnemyResponseOptionsClientRpc(
        double endTime,
        NetworkObjectReference[] enemiesInRange,
        int dashDist,
        ulong selectionTeam
    )
    {
        if (NetworkManager.Singleton.LocalClientId == selectionTeam)
        {
            planEnemyResponse(endTime, enemiesInRange, dashDist, selectionTeam);
        }
        else
        {
            StartCoroutine(CountDown(endTime));
        }
    }

    void planEnemyResponse(
        double endTime,
        NetworkObjectReference[] enemiesInRange,
        int dashDist,
        ulong selectionTeam
    )
    {
        //convert from NetworkObjectReference[] to List<GameObject>
        List<GameObject> dashUnits = new List<GameObject>();
        foreach (NetworkObjectReference objRef in enemiesInRange)
        {
            if (objRef.TryGet(out NetworkObject networkObject))
            {
                dashUnits.Add(networkObject.gameObject);
            }
        }

        StartCoroutine(
            PlanMovement.Instance.StartPlanning(
                paths => enemyResponseCallbackServerRpc(paths),
                endTime,
                dashUnits,
                unitData.diveRange
            )
        );
    }

    [ServerRpc(RequireOwnership = false)]
    void enemyResponseCallbackServerRpc(PathsDict paths, ServerRpcParams rpcParams = default)
    {
        ClearAbilityIndicators();

        // Only accept responses from the selected enemy team client
        if (rpcParams.Receive.SenderClientId != allowedDodgerClientId)
            return;

        GameLoop.Instance.setOverlayUITextClientRpc("Executing Moves", MessagePerspective.Neutral);

        //continue time
        Time.timeScale = 1f;
        GameLoop.setUnitCardsInteractable(true);

        foreach (GameObject enemy in enemiesInRange)
        {
            enemy.transform.Find("UnitCanvas").Find("Alert").gameObject.SetActive(false);
        }

        // Sanitize and execute dive moves
        foreach (var pair in paths)
        {
            GameObject unit = pair.Key;
            List<Vector3> movementPath = pair.Value;
            if (unit == null || movementPath == null || movementPath.Count == 0)
                continue;

            var unitNet = unit.GetComponent<NetworkObject>();
            if (unitNet == null || !unitNet.IsSpawned)
                continue;

            // Ensure this unit is one of the allowed dodging enemies
            if (!enemiesInRange.Contains(unit))
                continue;

            // Apply server-side sanitization similar to planning phase: limit to dive range and adjacency
            var moveComp = unit.GetComponent<Movement>();
            if (moveComp == null || moveComp.unitData == null)
                continue;
            int maxSteps = Mathf.Max(0, unitData.diveRange);

            List<Vector3> clean = new List<Vector3>();
            Vector3 start = PlanMovement.GetGridCellUnderCharacter(unit);
            clean.Add(start);

            int stepsAdded = 0;
            Vector3 last = start;
            foreach (var p in movementPath)
            {
                if (stepsAdded >= maxSteps)
                    break;
                Vector3 snapped = PlanMovement.GetNearestGridCell(p);
                float dist = Mathf.Abs(snapped.x - last.x) + Mathf.Abs(snapped.z - last.z);
                bool isAdjacent = Mathf.Abs(dist - GameLoop.cellSize) <= 0.1f;
                if (!isAdjacent)
                    continue;
                if (clean.Contains(snapped))
                    continue;
                if (!GameLoop.gridBounds.Contains(new Vector2(snapped.x, snapped.z)))
                    continue;

                clean.Add(snapped);
                last = snapped;
                stepsAdded++;
            }

            if (clean.Count <= 1)
                continue;
            unit.GetComponent<Shooting>().PauseShooting();
            unit.GetComponent<Movement>().StartMovement(new List<Vector3>(clean), true);
        }

        //activate ability
        StartCoroutine(
            unit.GetComponent<Ability>().ExecuteAbility(selectedSquare, unitData.abilityRadius)
        );
    }

    private void HandleMouseClick(Vector3 nearestCell, ref Vector3 selectedSquare)
    {
        Vector3? potentialSquare = PlanMovement.GetGridCellUnderMouse();
        if (!potentialSquare.HasValue)
        {
            Debug.LogWarning(
                "[ActivateAbility] Could not get grid cell under mouse, keeping current selection"
            );
            return;
        }

        Vector3 mouseSquare = potentialSquare.Value;
        float horizontalDistance = Mathf.Abs(nearestCell.x - mouseSquare.x);
        float verticalDistance = Mathf.Abs(nearestCell.z - mouseSquare.z);

        if (
            horizontalDistance + verticalDistance
            > (unitData.abilitySquareRange + 0.1f) * GameLoop.cellSize
        )
            return;

        selectedSquare = mouseSquare;
        // Notify server to update global indicators and alerts
        NotifySelectedSquareServerRpc(selectedSquare);
    }

    private void CreateLaserLine(Vector3 start, Vector3 end)
    {
        Vector3 direction = (end - start).normalized;
        Vector3 finalEnd = end + direction * 50f;

        // Stop if there is a wall in the way
        if (
            Physics.Raycast(
                start,
                direction,
                out RaycastHit hit,
                Mathf.Infinity,
                LayerMask.GetMask("Walls")
            )
        )
        {
            finalEnd = hit.point;
        }

        GameObject laserObject = new GameObject("AbilityLinePreview");
        globalAbilityLaser = laserObject.AddComponent<LineRenderer>();

        Material laserMaterial = new Material(Shader.Find("Unlit/Color"));
        laserMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        laserMaterial.color = Color.red;
        globalAbilityLaser.material = laserMaterial;

        globalAbilityLaser.startColor = Color.red;
        globalAbilityLaser.endColor = Color.red;
        globalAbilityLaser.startWidth = globalAbilityLaser.endWidth = 0.1f;
        globalAbilityLaser.positionCount = 2;
        globalAbilityLaser.SetPosition(0, start);
        globalAbilityLaser.SetPosition(1, finalEnd);
    }

    private void UpdateAbilityIndicatorsLocal(Vector3 mouseSquare)
    {
        if (globalAbilityIndicator != null)
            Destroy(globalAbilityIndicator);
        if (globalAOEIndicator != null)
            Destroy(globalAOEIndicator);

        globalAbilityIndicator = Instantiate(
            abilitySquareIndicatorPrefab,
            mouseSquare,
            Quaternion.identity
        );
        globalAOEIndicator = Instantiate(
            abilityAOEIndicatorPrefab,
            mouseSquare,
            Quaternion.identity
        );

        float diameter = 2 * unitData.abilityRadius * GameLoop.cellSize;
        globalAOEIndicator.transform.localScale = new Vector3(diameter, 0.05f, diameter);
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifySelectedSquareServerRpc(Vector3 square, ServerRpcParams rpcParams = default)
    {
        if (unit == null)
            return;
        var netObj = unit.GetComponent<NetworkObject>();
        if (netObj == null || !netObj.IsSpawned)
            return;
        if (netObj.OwnerClientId != rpcParams.Receive.SenderClientId)
            return;

        // Broadcast global indicators
        ShowAbilityIndicatorsClientRpc(square, unitData.abilityRadius);

        if (unitData.responseDistLine)
        {
            Vector3 start = unit.transform.position;
            Vector3 targetPosition = square + Helper.heightOffset(unit.transform);
            Vector3 direction = (targetPosition - start).normalized;
            Vector3 end = targetPosition + direction * 50f;
            if (
                Physics.Raycast(
                    start,
                    direction,
                    out RaycastHit hit,
                    Mathf.Infinity,
                    LayerMask.GetMask("Walls")
                )
            )
            {
                end = hit.point;
            }
            ShowAbilityLineClientRpc(start, end);
        }

        UpdateDodgeAlerts(square);
    }

    private void ClearAbilityIndicators()
    {
        if (unit == null)
        {
            Debug.LogWarning("[ActivateAbility] ClearAbilityIndicatorsServerRpc: unit is null.");
            return;
        }
        var netObj = unit.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogWarning(
                "[ActivateAbility] ClearAbilityIndicatorsServerRpc: NetworkObject component is missing on unit."
            );
            return;
        }
        if (!netObj.IsSpawned)
        {
            Debug.LogWarning(
                "[ActivateAbility] ClearAbilityIndicatorsServerRpc: NetworkObject is not spawned."
            );
            return;
        }
        HideAbilityIndicatorsClientRpc();
        SetDodgeAlerts(GameObject.FindGameObjectsWithTag(GameLoop.GetEnemyTeam(unit.tag)), false);
    }

    [ClientRpc]
    private void ShowAbilityIndicatorsClientRpc(Vector3 square, float radius)
    {
        UpdateAbilityIndicatorsLocal(square);
    }

    [ClientRpc]
    private void ShowAbilityLineClientRpc(Vector3 start, Vector3 end)
    {
        if (globalAbilityLaser != null)
        {
            Destroy(globalAbilityLaser.gameObject);
            globalAbilityLaser = null;
        }
        CreateLaserLine(start, end);
    }

    [ClientRpc]
    private void HideAbilityIndicatorsClientRpc()
    {
        if (globalAbilityIndicator != null)
            Destroy(globalAbilityIndicator);
        else
            Debug.LogWarning(
                "[ActivateAbility] Tried to destroy globalAbilityIndicator, but it was null."
            );

        if (globalAOEIndicator != null)
            Destroy(globalAOEIndicator);
        else
            Debug.LogWarning(
                "[ActivateAbility] Tried to destroy globalAOEIndicator, but it was null."
            );

        if (globalAbilityLaser != null)
            Destroy(globalAbilityLaser.gameObject);
        else
            Debug.LogWarning(
                "[ActivateAbility] Tried to destroy globalAbilityLaser, but it was null."
            );
        globalAbilityIndicator = null;
        globalAOEIndicator = null;
        globalAbilityLaser = null;
    }

    private void UpdateDodgeAlerts(Vector3 selectedSquare)
    {
        string enemyTeam = GameLoop.GetEnemyTeam(unit.tag);
        GameObject[] allEnemies = GameObject.FindGameObjectsWithTag(enemyTeam);

        // Clear previous alerts
        SetDodgeAlerts(allEnemies, false);

        // Set alerts for enemies in range
        List<GameObject> enemiesInRange = !unitData.responseDistLine
            ? GetUnitsInRange(selectedSquare, enemyTeam, unitData.responseRange)
            : GetUnitsInRangeofLine(selectedSquare, enemyTeam, unitData.responseRange);
        SetDodgeAlerts(enemiesInRange.ToArray(), true);
    }

    private void SetDodgeAlerts(GameObject[] unitsInRange, bool active)
    {
        foreach (GameObject unit in unitsInRange)
        {
            var netObj = unit.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                // Use NetworkHelper to set child object active/inactive across the network
                NetworkHelper.Instance.SetActiveClientRpc(netObj, "UnitCanvas/Alert", active);
            }
            else
            {
                Debug.LogWarning($"[ActivateAbility] No NetworkObject found on unit {unit.name}");
            }
        }
    }

    public static List<GameObject> GetUnitsInRange(Vector3 abilitySquare, string team, float range)
    {
        List<GameObject> unitsInRange = new List<GameObject>();
        GameObject[] allUnits = GameObject.FindGameObjectsWithTag(team);

        if (allUnits.Length == 0)
        {
            Debug.LogWarning(
                $"[ActivateAbility] No units found with tag '{team}' for range calculation"
            );
        }

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

        if (allUnits.Length == 0)
        {
            Debug.LogWarning(
                $"[ActivateAbility] No units found with tag '{team}' for line range calculation"
            );
        }

        Vector3 start = unit.transform.position;
        Vector3 direction = (abilitySquare - start).normalized;
        Vector3 end = abilitySquare + direction * 50f;

        // Stop if there is a wall in the way
        if (
            Physics.Raycast(
                start,
                direction,
                out RaycastHit hit,
                Mathf.Infinity,
                LayerMask.GetMask("Walls")
            )
        )
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
