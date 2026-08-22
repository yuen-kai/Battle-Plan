using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A knockback off a line-shaped dash: the unit in the way is pushed to whichever side of the
/// dash's path is open, or squeezed to the nearest free cell if both sides are blocked, and left
/// stunned once it lands. Server-only, like the ability code that calls it — it moves a unit by
/// Transform the same way GameLoop's own overlap shoves do, which only ever happens on the server
/// and replicates through each unit's NetworkTransform.
/// </summary>
public static class AbilityKnockback
{
    // Long enough to read as being knocked aside rather than teleporting, matching the shove the
    // overlap pass itself uses so every push on the board reads the same way.
    private const float ShoveSlideSeconds = 0.3f;

    /// <summary>
    /// Shoves <paramref name="unit"/> off the cell it is standing on, to one side of
    /// <paramref name="dashDirection"/>, then stuns it for <paramref name="stunSeconds"/>. If both
    /// perpendicular cells are blocked, falls back to the nearest free cell within the same budget
    /// the overlap pass uses. If nothing is free at all, the unit is stunned in place.
    /// </summary>
    public static IEnumerator ShoveOffPath(GameObject unit, Vector2Int dashDirection, float stunSeconds)
    {
        if (unit == null)
            yield break;

        Unit identity = unit.GetComponent<Unit>();
        identity?.ApplyStun(stunSeconds + ShoveSlideSeconds);

        if (
            GameLoop.Instance != null
            && TryResolveDestination(unit, dashDirection, out Vector2Int destinationCell)
        )
        {
            GameLoop.Instance.RegisterUnitBeingShoved(unit, destinationCell);
            yield return SlideToCell(unit, destinationCell);
            GameLoop.Instance.UnregisterUnitBeingShoved(unit);
        }

        identity?.ApplyStun(stunSeconds);
    }

    private static bool TryResolveDestination(
        GameObject unit,
        Vector2Int dashDirection,
        out Vector2Int destinationCell
    )
    {
        dashDirection = new Vector2Int(
            Mathf.Clamp(dashDirection.x, -1, 1),
            Mathf.Clamp(dashDirection.y, -1, 1)
        );
        Vector2Int currentCell = GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));
        HashSet<Vector2Int> occupiedCells = CollectOccupiedCells(unit);

        Vector2Int perpendicularA = new(-dashDirection.y, dashDirection.x);
        Vector2Int perpendicularB = -perpendicularA;

        if (IsCellFree(currentCell + perpendicularA, occupiedCells))
        {
            destinationCell = currentCell + perpendicularA;
            return true;
        }
        if (IsCellFree(currentCell + perpendicularB, occupiedCells))
        {
            destinationCell = currentCell + perpendicularB;
            return true;
        }

        return GridSystem.TryFindDisplacementCell(
            currentCell,
            currentCell,
            occupiedCells,
            GameLoop.MaxDisplacementSteps,
            out destinationCell
        );
    }

    private static bool IsCellFree(Vector2Int cell, ISet<Vector2Int> occupiedCells)
    {
        return GridSystem.IsCellInBounds(cell)
            && !GameLoop.wallLayout.Contains(cell)
            && !occupiedCells.Contains(cell);
    }

    /// <summary>
    /// Every living unit's current cell except <paramref name="excluding"/>, recomputed fresh off
    /// live transforms rather than tracked in a persistent registry — the same approach
    /// GameLoop's own overlap pass uses to decide whether a cell is taken right now.
    /// </summary>
    private static HashSet<Vector2Int> CollectOccupiedCells(GameObject excluding)
    {
        HashSet<Vector2Int> occupied = new();
        for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
        {
            foreach (GameObject candidate in GameLoop.GetTeamUnits(teamIndex))
            {
                if (
                    candidate == null
                    || candidate == excluding
                    || !candidate.activeInHierarchy
                    || !IsLivingUnit(candidate)
                )
                {
                    continue;
                }

                occupied.Add(GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(candidate)));
            }
        }
        return occupied;
    }

    private static bool IsLivingUnit(GameObject unit)
    {
        Health health = unit.GetComponent<Health>();
        return health != null ? health.IsAlive : unit.activeSelf;
    }

    /// <summary>
    /// Direct-transform lerp onto <paramref name="cell"/>, matching GameLoop's SlideUnitsToCells:
    /// the slide stays server-side and reaches clients through each unit's NetworkTransform, and
    /// Physics.SyncTransforms() afterwards is required because shooting acquires targets with
    /// casts against colliders, which would otherwise still see the unit at its pre-shove position
    /// for the rest of the frame.
    /// </summary>
    private static IEnumerator SlideToCell(GameObject unit, Vector2Int cell)
    {
        Transform unitTransform = unit.transform;
        Vector3 from = unitTransform.position;
        Vector3 to = GameLoop.gridCoordToWorld(cell) + Helper.heightOffset(unitTransform);

        float elapsed = 0f;
        while (elapsed < ShoveSlideSeconds)
        {
            elapsed += Time.deltaTime;
            if (unitTransform == null)
                yield break;
            unitTransform.position = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / ShoveSlideSeconds));
            yield return null;
        }
        if (unitTransform != null)
            unitTransform.position = to;

        Physics.SyncTransforms();
    }
}
