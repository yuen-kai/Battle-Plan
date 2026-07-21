using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Shield : Ability
{
    public const float AbilityDurationSeconds = 3f;
    public const int ShieldWidthIncreaseCellsPerSide = 1;
    public const float AllySpeedBoostRadiusCells = 2f;
    public const float AllySpeedBoostMultiplier = 1.5f;
    public const float AllySpeedBoostDurationSeconds = AbilityDurationSeconds;
    public const string BlueShieldLayerName = "BlueShield";
    public const string RedShieldLayerName = "RedShield";

    private const float RushSpeedCellsPerSecond = 1.5f;
    private Transform shieldTransform;
    private bool shieldFootprintExpanded;

    // NetworkVariable (not ClientRpc) so shield state survives fog NetworkHide/NetworkShow:
    // a unit revealed mid-shield renders the correct state from the resynced value.
    private NetworkVariable<bool> shieldActive = new(false);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        shieldTransform = transform.Find("Shield");
        EnsureExpandedShieldFootprint();
        shieldActive.OnValueChanged += OnShieldActiveChanged;
        ApplyShieldState(shieldActive.Value);
    }

    public override void OnNetworkDespawn()
    {
        shieldActive.OnValueChanged -= OnShieldActiveChanged;
        base.OnNetworkDespawn();
    }

    public override void ResetForRespawn()
    {
        base.ResetForRespawn();
        if (IsServer)
            shieldActive.Value = false;
        ApplyShieldState(false);
    }

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        if (!IsServer)
            yield break;

        Movement movement = GetComponent<Movement>();
        Shooting shooting = GetComponent<Shooting>();
        UnitData data = movement != null ? movement.unitData : null;
        if (movement == null || shooting == null || data == null || data.abilityFixedDistance <= 0)
        {
            yield break;
        }

        Vector3 startSquare = GridSystem.GetNearestGridCell(gameObject);
        Vector2Int startCell = GridSystem.ConvertToGridCoords(startSquare);
        Vector2Int selectedCell = GridSystem.ConvertToGridCoords(abilitySquare);
        if (!GridSystem.TryGetAdjacentDirection(startCell, selectedCell, out Vector2Int direction))
            yield break;

        Vector2Int destinationCell = GridSystem.GetDirectionalDestination(
            startCell,
            direction,
            data.abilityFixedDistance,
            GameLoop.wallLayout
        );
        Vector3 heightOffset = Helper.heightOffset(transform);
        Vector3 startPosition = GameLoop.gridCoordToWorld(startCell) + heightOffset;
        Vector3 destinationPosition = GameLoop.gridCoordToWorld(destinationCell) + heightOffset;

        movement.PauseMovement();
        shooting.PauseShooting();
        movement.moving = true;
        transform.position = startPosition;

        shieldActive.Value = true;
        float shieldStartedAt = Time.time;
        ApplyAllySpeedBoost(startCell, shieldStartedAt);

        Vector3 facingTarget =
            startPosition + new Vector3(direction.x, 0f, direction.y) * GameLoop.cellSize;
        yield return StartCoroutine(movement.RotateToFaceTarget(facingTarget, data.rotationSpeed));

        float distance = Vector3.Distance(startPosition, destinationPosition);
        float rushDuration =
            distance > 0f ? distance / (RushSpeedCellsPerSecond * GameLoop.cellSize) : 0f;
        float elapsed = 0f;
        while (elapsed < rushDuration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(
                startPosition,
                destinationPosition,
                Mathf.Clamp01(elapsed / rushDuration)
            );
            yield return null;
        }
        transform.position = destinationPosition;
        movement.PauseMovement();
        movement.moving = false;

        float shieldTimeRemaining = AbilityDurationSeconds - (Time.time - shieldStartedAt);
        if (shieldTimeRemaining > 0f)
            yield return new WaitForSeconds(shieldTimeRemaining);
        shieldActive.Value = false;
        movement.transitionToShooting();
    }

    public static bool IsEligibleAllyForSpeedBoost(
        Vector2Int casterCell,
        int casterTeamIndex,
        Vector2Int candidateCell,
        int candidateTeamIndex,
        bool candidateIsLiving,
        bool candidateIsCaster
    )
    {
        return casterTeamIndex >= 0
            && candidateTeamIndex == casterTeamIndex
            && candidateIsLiving
            && !candidateIsCaster
            && Vector2.Distance(casterCell, candidateCell) <= AllySpeedBoostRadiusCells + 0.001f;
    }

    public static bool TryExpandShieldFootprint(Transform shield)
    {
        if (shield == null)
            return false;

        BoxCollider shieldCollider = shield.GetComponent<BoxCollider>();
        float colliderLocalWidth =
            shieldCollider != null ? Mathf.Abs(shieldCollider.size.x) : 0f;
        float parentWorldScaleX =
            shield.parent != null ? Mathf.Abs(shield.parent.lossyScale.x) : 1f;
        if (colliderLocalWidth <= Mathf.Epsilon || parentWorldScaleX <= Mathf.Epsilon)
            return false;

        float addedWorldWidth =
            ShieldWidthIncreaseCellsPerSide * 2f * GameLoop.cellSize;
        float addedLocalScale = addedWorldWidth / (colliderLocalWidth * parentWorldScaleX);
        Vector3 scale = shield.localScale;
        scale.x += (scale.x < 0f ? -1f : 1f) * addedLocalScale;
        shield.localScale = scale;
        return true;
    }

    public static int GetCollisionLayerForTeam(int teamIndex)
    {
        string layerName = teamIndex switch
        {
            GameLoop.HostTeamIndex => BlueShieldLayerName,
            GameLoop.OpponentTeamIndex => RedShieldLayerName,
            _ => null,
        };
        return layerName != null ? LayerMask.NameToLayer(layerName) : -1;
    }

    public static bool TryApplyCollisionLayer(Transform shield, int teamIndex)
    {
        if (shield == null)
            return false;

        int shieldLayer = GetCollisionLayerForTeam(teamIndex);
        if (shieldLayer < 0)
            return false;

        shield.gameObject.layer = shieldLayer;
        return true;
    }

    private void ApplyAllySpeedBoost(Vector2Int casterCell, float startsAt)
    {
        Unit casterIdentity = GetComponent<Unit>();
        if (!IsServer || casterIdentity == null)
            return;

        int casterTeamIndex = casterIdentity.TeamIndex;
        foreach (GameObject candidate in GameLoop.GetTeamUnits(casterTeamIndex))
        {
            if (candidate == null)
                continue;

            Unit candidateIdentity = candidate.GetComponent<Unit>();
            Health candidateHealth = candidate.GetComponent<Health>();
            Movement candidateMovement = candidate.GetComponent<Movement>();
            if (candidateIdentity == null || candidateHealth == null || candidateMovement == null)
                continue;

            Vector2Int candidateCell = GridSystem.ConvertToGridCoords(
                GridSystem.GetNearestGridCell(candidate)
            );
            if (
                !IsEligibleAllyForSpeedBoost(
                    casterCell,
                    casterTeamIndex,
                    candidateCell,
                    candidateIdentity.TeamIndex,
                    candidate.activeInHierarchy && candidateHealth.IsAlive,
                    candidate == gameObject
                )
            )
            {
                continue;
            }

            candidateMovement.TryApplyTemporaryMoveSpeedBoost(
                AllySpeedBoostMultiplier,
                AllySpeedBoostDurationSeconds,
                startsAt
            );
        }
    }

    private void OnShieldActiveChanged(bool previousValue, bool newValue)
    {
        ApplyShieldState(newValue);
    }

    private void ApplyShieldState(bool active)
    {
        if (shieldTransform == null)
            shieldTransform = transform.Find("Shield");
        EnsureExpandedShieldFootprint();
        if (shieldTransform != null)
        {
            // GameLoop assigns the team layer recursively at spawn. Restore the dedicated shield
            // layer whenever replicated shield state is applied so targeting casts skip the shield
            // while the projectile colliders keep their existing team-shield collision rules.
            Unit identity = GetComponent<Unit>();
            TryApplyCollisionLayer(
                shieldTransform,
                identity != null ? identity.TeamIndex : -1
            );
            shieldTransform.gameObject.SetActive(active);
        }
    }

    private void EnsureExpandedShieldFootprint()
    {
        if (!shieldFootprintExpanded && TryExpandShieldFootprint(shieldTransform))
            shieldFootprintExpanded = true;
    }
}
