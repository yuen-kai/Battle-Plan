using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A rush to a cell, same feel as Shield's old rush, but the destructive half of the kit instead
/// of the defensive one: every enemy standing anywhere on the line it sweeps gets shoved off to the
/// side and stunned, rather than the caster raising a bullet-blocking shield when it lands.
/// </summary>
public class DashRush : Ability
{
    private const float RushSpeedCellsPerSecond = 4.5f;

    // Short enough to read as a hard, telegraphed interrupt rather than a long lockout -- the
    // designer's explicit correction for how a knockback stun should feel.
    public const float KnockbackStunSeconds = 0.6f;

    /// <summary>
    /// How close Ramrod has to get before an enemy is counted as hit, as a fraction of a cell.
    /// Enemies are shoved when the charge actually reaches them rather than all at once when it
    /// launches, so the ones further down the line are still standing while the near ones fly.
    /// Below a full cell so contact registers as the cells overlap rather than only once Ramrod is
    /// dead centre on top of them, which at this speed can be skipped over between frames.
    /// </summary>
    private const float CollisionRadiusCells = 0.75f;

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

        List<GameObject> enemiesOnTheLine = FindEnemiesOnLine(startCell, destinationCell, direction);

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
            ShoveEnemiesOnContact(enemiesOnTheLine, direction);
            yield return null;
        }
        transform.position = destinationPosition;
        // The charge can cover the last stretch inside a single frame at this speed, so anyone the
        // loop never sampled close enough to is resolved here rather than being run straight through.
        ShoveEnemiesOnContact(enemiesOnTheLine, direction);
        movement.PauseMovement();
        movement.moving = false;
        movement.transitionToShooting(onlyIfWeaponsStillFree: true);
    }

    /// <summary>
    /// Every living enemy standing anywhere on the line this rush is about to sweep -- not just the
    /// cell it stops on. Resolved once, before the charge starts moving, because it reads occupancy
    /// off live transforms and a unit already being shoved has left its cell; who is standing in the
    /// way is a question about the board as the charge is declared, not about where everyone has been
    /// pushed to halfway through it.
    /// </summary>
    private List<GameObject> FindEnemiesOnLine(
        Vector2Int startCell,
        Vector2Int destinationCell,
        Vector2Int direction
    )
    {
        Unit casterIdentity = GetComponent<Unit>();
        if (casterIdentity == null)
            return new List<GameObject>();

        List<Vector2Int> sweptCells = GetSweptCells(startCell, destinationCell, direction);
        if (sweptCells.Count == 0)
            return new List<GameObject>();

        int enemyTeamIndex = GameLoop.GetEnemyTeamIndex(casterIdentity.TeamIndex);
        return FindEnemiesOnCells(sweptCells, GameLoop.GetTeamUnits(enemyTeamIndex));
    }

    /// <summary>
    /// Shoves and stuns whichever of <paramref name="enemiesOnTheLine"/> the charge has actually
    /// reached this frame, then drops them from the list so nobody is hit twice. Called every frame
    /// of the travel rather than once at launch: the hit lands when Ramrod arrives, so an enemy three
    /// cells down the line stays put until the charge gets there instead of flying off the instant
    /// the ability is declared. Each shove is fired without a yield so its own slide-and-stun
    /// coroutine (see AbilityKnockback) runs alongside the rest of the charge instead of halting it.
    /// </summary>
    private void ShoveEnemiesOnContact(List<GameObject> enemiesOnTheLine, Vector2Int direction)
    {
        float contactRange = CollisionRadiusCells * GameLoop.cellSize;
        for (int index = enemiesOnTheLine.Count - 1; index >= 0; index--)
        {
            GameObject enemy = enemiesOnTheLine[index];
            if (enemy == null || !enemy.activeInHierarchy || !IsLivingUnit(enemy))
            {
                enemiesOnTheLine.RemoveAt(index);
                continue;
            }

            // Flat distance: the charge and its target are both on the floor, and a unit's height
            // offset varies by model, so comparing full 3D distance would make taller units harder
            // to run into.
            Vector3 separation = enemy.transform.position - transform.position;
            separation.y = 0f;
            if (separation.magnitude > contactRange)
                continue;

            enemiesOnTheLine.RemoveAt(index);
            StartCoroutine(AbilityKnockback.ShoveOffPath(enemy, direction, KnockbackStunSeconds));
        }
    }

    /// <summary>
    /// Every cell the rush crosses from <paramref name="startCell"/> to
    /// <paramref name="destinationCell"/> along <paramref name="direction"/>, in order, excluding
    /// the start cell itself and including the destination. Pure grid math, kept static so it can
    /// be exercised without a scene.
    /// </summary>
    public static List<Vector2Int> GetSweptCells(
        Vector2Int startCell,
        Vector2Int destinationCell,
        Vector2Int direction
    )
    {
        List<Vector2Int> cells = new();
        direction = new Vector2Int(Mathf.Clamp(direction.x, -1, 1), Mathf.Clamp(direction.y, -1, 1));
        if (direction == Vector2Int.zero || startCell == destinationCell)
            return cells;

        Vector2Int cursor = startCell;
        int maxSteps = GridSystem.ColumnCount + GridSystem.RowCount;
        for (int step = 0; step < maxSteps && cursor != destinationCell; step++)
        {
            cursor += direction;
            cells.Add(cursor);
        }
        return cells;
    }

    /// <summary>
    /// The living, active units among <paramref name="candidateUnits"/> standing on any of
    /// <paramref name="cells"/> right now. Matches AbilityKnockback's own occupancy check
    /// (activeInHierarchy plus Health.IsAlive, cells read off live transforms) read the other way
    /// around: who is here, not just whether someone is.
    /// </summary>
    public static List<GameObject> FindEnemiesOnCells(
        IReadOnlyCollection<Vector2Int> cells,
        IEnumerable<GameObject> candidateUnits
    )
    {
        List<GameObject> found = new();
        if (candidateUnits == null || cells == null || cells.Count == 0)
            return found;

        HashSet<Vector2Int> cellSet = cells as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(cells);
        foreach (GameObject candidate in candidateUnits)
        {
            if (candidate == null || !candidate.activeInHierarchy || !IsLivingUnit(candidate))
                continue;

            Vector2Int candidateCell = GridSystem.ConvertToGridCoords(
                GridSystem.GetNearestGridCell(candidate)
            );
            if (cellSet.Contains(candidateCell))
                found.Add(candidate);
        }
        return found;
    }

    private static bool IsLivingUnit(GameObject unit)
    {
        Health health = unit.GetComponent<Health>();
        return health != null ? health.IsAlive : unit.activeSelf;
    }
}
