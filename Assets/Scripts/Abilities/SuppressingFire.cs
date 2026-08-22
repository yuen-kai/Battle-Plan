using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class SuppressingFire : Ability
{
    private const float WindUpSeconds = 0.8f;

    private const int BarrageShots = 25;
    private const float SecondsBetweenShots = 0.22f;

    private const float RecoverySeconds = 1.4f;

    private const float SpreadDegrees = 18f;
    private const float SpreadStride = 0.6180339887f;

    private const float BarrageRangeCells = 4f;

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        if (!IsServer)
            yield break;

        Movement movement = GetComponent<Movement>();
        Shooting shooting = GetComponent<Shooting>();
        UnitData data = movement != null ? movement.unitData : null;
        if (movement == null || shooting == null || data == null)
            yield break;

        Vector2Int casterCell = GridSystem.ConvertToGridCoords(
            GridSystem.GetNearestGridCell(gameObject)
        );
        Vector2Int targetCell = GridSystem.ConvertToGridCoords(abilitySquare);
        if (
            !GridSystem.TryGetAdjacentDirection(casterCell, targetCell, out Vector2Int direction)
        )
        {
            Debug.LogWarning(
                $"[SuppressingFire] {name} could not resolve a barrage direction from "
                    + $"{abilitySquare} (caster at {casterCell}); aborting. Check this unit's "
                    + "UnitData has selectAbilityDirection and abilityFixedDistance set."
            );
            yield break;
        }

        Vector3 direction3D = new(direction.x, 0f, direction.y);
        Vector2Int? ignoredWallCell = GameLoop.wallLayout.Contains(targetCell)
            ? targetCell
            : null;

        movement.PauseMovement();
        BeginInterruptibleAbilityAction();

        yield return movement.RotateToFaceTarget(
            transform.position + direction3D * GameLoop.cellSize,
            data.rotationSpeed
        );

        yield return new WaitForSeconds(WindUpSeconds);

        BarrageFxClientRpc(transform.position, direction3D);

        float spreadPhase = Random.value;

        for (int shot = 0; shot < BarrageShots; shot++)
        {
            float sample = Mathf.Repeat(spreadPhase + shot * SpreadStride, 1f);
            float spread = Mathf.Lerp(-SpreadDegrees, SpreadDegrees, sample);
            Vector3 shotDirection = Quaternion.AngleAxis(spread, Vector3.up) * direction3D;

            shooting.FireBulletInDirection(
                shotDirection,
                range: BarrageRangeCells,
                ignoredWallCell: ignoredWallCell
            );

            yield return new WaitForSeconds(SecondsBetweenShots);
        }

        yield return new WaitForSeconds(RecoverySeconds);
        CompleteInterruptibleAbilityAction();
    }

    [ClientRpc]
    private void BarrageFxClientRpc(Vector3 origin, Vector3 direction)
    {
        Color muzzle = AbilityJuice.Alarm;
        Vector3 muzzlePosition = origin + direction.normalized * (GameLoop.cellSize * 0.4f);

        ImpactCore.Spawn(muzzlePosition, muzzle, 1.1f, 1.4f);
        CameraEffects.Instance?.CameraShakeClientRpc(
            BarrageShots * SecondsBetweenShots,
            0.02f
        );
    }
}
