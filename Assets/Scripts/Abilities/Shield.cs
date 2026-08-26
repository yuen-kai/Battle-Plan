using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class Shield : Ability
{
    public const float AbilityDurationSeconds = 4.5f;
    public const int ShieldWidthIncreaseCellsPerSide = 1;
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

    public override AbilityPathKind BuildPlannedPath(
        Vector3 targetSquare,
        UnitData data,
        List<Vector3> points
    )
    {
        points.Clear();
        if (
            !TryResolveRush(
                targetSquare,
                data,
                out Vector2Int startCell,
                out Vector2Int destinationCell,
                out _
            )
        )
        {
            return AbilityPathKind.None;
        }

        return AbilityTrajectory.BuildGroundRun(
            GameLoop.gridCoordToWorld(startCell),
            GameLoop.gridCoordToWorld(destinationCell),
            points
        )
            ? AbilityPathKind.Ground
            : AbilityPathKind.None;
    }

    public override bool TryGetCasterDestination(
        Vector3 targetSquare,
        UnitData data,
        out Vector2Int destinationCell
    )
    {
        return TryResolveRush(targetSquare, data, out _, out destinationCell, out _);
    }

    /// <summary>
    /// The cells this rush leaves from and stops on, for a direction read off
    /// <paramref name="targetSquare"/>. Shared with the path drawn while planning so the preview
    /// is cut short by exactly the wall that will cut the rush short.
    /// </summary>
    private bool TryResolveRush(
        Vector3 targetSquare,
        UnitData data,
        out Vector2Int startCell,
        out Vector2Int destinationCell,
        out Vector2Int direction
    )
    {
        startCell = GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(gameObject));
        destinationCell = startCell;
        direction = Vector2Int.zero;
        if (data == null || data.abilityFixedDistance <= 0)
            return false;

        if (
            !GridSystem.TryGetAdjacentDirection(
                startCell,
                GridSystem.ConvertToGridCoords(targetSquare),
                out direction
            )
        )
        {
            return false;
        }

        destinationCell = GridSystem.GetDirectionalDestination(
            startCell,
            direction,
            data.abilityFixedDistance,
            GameLoop.wallLayout
        );
        return true;
    }

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        if (!IsServer)
            yield break;

        Movement movement = GetComponent<Movement>();
        Shooting shooting = GetComponent<Shooting>();
        UnitData data = movement != null ? movement.unitData : null;
        if (movement == null || shooting == null)
            yield break;

        if (
            !TryResolveRush(
                abilitySquare,
                data,
                out Vector2Int startCell,
                out Vector2Int destinationCell,
                out Vector2Int direction
            )
        )
        {
            yield break;
        }

        Vector3 heightOffset = Helper.heightOffset(transform);
        Vector3 startPosition = GameLoop.gridCoordToWorld(startCell) + heightOffset;
        Vector3 destinationPosition = GameLoop.gridCoordToWorld(destinationCell) + heightOffset;

        movement.PauseMovement();
        shooting.PauseShooting();
        movement.moving = true;
        transform.position = startPosition;

        shieldActive.Value = true;
        float shieldStartedAt = Time.time;

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
        movement.transitionToShooting(onlyIfWeaponsStillFree: true);
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
