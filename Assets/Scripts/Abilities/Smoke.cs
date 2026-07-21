using System.Collections;
using UnityEngine;

public class Smoke : Ability
{
    public const int FootprintRadius = 1;

    public bool RegisterTargetFootprint(Vector3 abilitySquare)
    {
        if (!IsServer || GameLoop.Instance == null)
            return false;

        Vector3 snappedSquare = GridSystem.GetNearestGridCell(abilitySquare);
        Vector2Int center = GridSystem.ConvertToGridCoords(snappedSquare);
        return GameLoop.Instance.TryRegisterSmokeFootprint(center);
    }

    public override IEnumerator ExecuteAbility(Vector3 abilitySquare, float AreaRadius = 0)
    {
        // GameLoop registers the footprint synchronously after dodge cancellation and before
        // movement. Smoke has no timed work that should hold the round open.
        yield break;
    }
}
