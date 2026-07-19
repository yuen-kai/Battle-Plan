using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Shield : Ability
{
    private const float AbilityTime = 3f;
    private const float RushSpeedCellsPerSecond = 1.5f;
    private Transform shieldTransform;

    // NetworkVariable (not ClientRpc) so shield state survives fog NetworkHide/NetworkShow:
    // a unit revealed mid-shield renders the correct state from the resynced value.
    private NetworkVariable<bool> shieldActive = new(false);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        shieldTransform = transform.Find("Shield");
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

        float shieldTimeRemaining = AbilityTime - (Time.time - shieldStartedAt);
        if (shieldTimeRemaining > 0f)
            yield return new WaitForSeconds(shieldTimeRemaining);
        shieldActive.Value = false;
        movement.transitionToShooting();
    }

    private void OnShieldActiveChanged(bool previousValue, bool newValue)
    {
        ApplyShieldState(newValue);
    }

    private void ApplyShieldState(bool active)
    {
        if (shieldTransform == null)
            shieldTransform = transform.Find("Shield");
        if (shieldTransform != null)
            shieldTransform.gameObject.SetActive(active);
    }
}
