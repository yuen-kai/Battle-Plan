using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GridSystem : MonoBehaviour
{
    [SerializeField]
    private GameObject cellPrefab; // Public variable for the GameObject prefab
    public static float CellSize => GameLoop.cellSize; // Size of each grid cell

    [SerializeField]
    private int gridWidth = 15; // Width of the grid

    [SerializeField]
    private int gridHeight = 10; // Height of the grid
    GameObject[,] gridArray; // 2D array to hold grid cells

    // Start is called before the first frame update
    // void Start()
    // {
    //     cellSize = cellPrefab.GetComponent<Renderer>().bounds.size.x; // Set cellSize to the width of the cellPrefab
    //     CreateGrid();
    // }

    // void CreateGrid()
    // {
    //     gridArray = new GameObject[gridWidth, gridHeight];
    //     GameObject gridParent = new("GridCells"); // Create a parent GameObject for the grid cells
    //     for (int x = 0; x < gridWidth; x++)
    //     {
    //         for (int z = 0; z < gridHeight; z++)
    //         {
    //             Vector3 position = new(x * cellSize, 0, z * cellSize);
    //             GameObject cell = Instantiate(cellPrefab, position, Quaternion.identity); // Instantiate the prefab
    //             cell.transform.parent = gridParent.transform; // Set the parent of the cell to the gridParent
    //             gridArray[x, z] = cell; // Store the cell in the grid array
    //         }
    //     }
    // }

    /// <summary>
    /// Draws the Manhattan diamond of cells within <paramref name="dist"/> of a position. Aiming an
    /// ability that only uses its square as a direction has no use for the caster's own cell, so
    /// <paramref name="includeOrigin"/> leaves that one out of the range it offers.
    /// </summary>
    public static GameObject DisplayGridRange(
        Vector3 currentPos,
        int dist,
        GameObject overlayPrefab,
        bool includeOrigin = true
    )
    {
        GameObject overlay = new("MoveOverlay");

        for (int i = -dist; i <= dist; i++)
        {
            int horizontalDist = dist - Mathf.Abs(i);
            for (int j = -horizontalDist; j <= horizontalDist; j++)
            {
                if (!includeOrigin && i == 0 && j == 0)
                    continue;

                float cellSize = GameLoop.cellSize;
                Vector2 overlayCellPos = new(
                    currentPos.x + j * cellSize,
                    currentPos.z + i * cellSize
                );
                if (GameLoop.gridBounds.Contains(overlayCellPos))
                {
                    GameObject overlayCell = Instantiate(
                        overlayPrefab,
                        new Vector3(overlayCellPos.x, 0.2f, overlayCellPos.y),
                        Quaternion.identity
                    );
                    overlayCell.transform.parent = overlay.transform;
                }
            }
        }
        return overlay;
    }

    public static GameObject DisplayGridDirections(Vector3 currentPos, GameObject overlayPrefab)
    {
        GameObject overlay = new("AbilityDirectionOverlay");
        for (int columnOffset = -1; columnOffset <= 1; columnOffset++)
        {
            for (int rowOffset = -1; rowOffset <= 1; rowOffset++)
            {
                if (columnOffset == 0 && rowOffset == 0)
                    continue;

                Vector2 overlayCellPos = new(
                    currentPos.x + columnOffset * CellSize,
                    currentPos.z + rowOffset * CellSize
                );
                if (!GameLoop.gridBounds.Contains(overlayCellPos))
                    continue;

                GameObject overlayCell = Instantiate(
                    overlayPrefab,
                    new Vector3(overlayCellPos.x, 0.2f, overlayCellPos.y),
                    Quaternion.identity
                );
                overlayCell.transform.parent = overlay.transform;
            }
        }
        return overlay;
    }

    public static Vector3 GetNearestGridCell(GameObject character)
    {
        Vector3 position = character.transform.position; // Get the position of the character
        return GetNearestGridCell(position); // Return the nearest grid cell position
    }

    public static Vector3 GetNearestGridCell(Vector3 position)
    {
        float x = Mathf.Round(position.x / CellSize) * CellSize;
        float z = Mathf.Round(position.z / CellSize) * CellSize;
        return new Vector3(x, 0, z);
    }

    public static Vector2Int ConvertToGridCoords(Vector3 position)
    {
        //Assuming grid's bottom left corner is at (0,0) and the grid is aligned with the world axes
        int x = Mathf.RoundToInt(position.x / CellSize);
        int z = Mathf.RoundToInt(position.z / CellSize);
        return new Vector2Int(x, z);
    }

    public static bool TryGetAdjacentDirection(
        Vector2Int start,
        Vector2Int target,
        out Vector2Int direction
    )
    {
        Vector2Int delta = target - start;
        bool isAdjacent =
            delta != Vector2Int.zero && Mathf.Abs(delta.x) <= 1 && Mathf.Abs(delta.y) <= 1;
        direction = isAdjacent
            ? new Vector2Int(Mathf.Clamp(delta.x, -1, 1), Mathf.Clamp(delta.y, -1, 1))
            : Vector2Int.zero;
        return isAdjacent;
    }

    public static Vector2Int GetDirectionalDestination(
        Vector2Int start,
        Vector2Int direction,
        int maxSteps,
        ISet<Vector2Int> blockedCells = null
    )
    {
        direction = new Vector2Int(
            Mathf.Clamp(direction.x, -1, 1),
            Mathf.Clamp(direction.y, -1, 1)
        );
        if (direction == Vector2Int.zero)
            return start;

        Vector2Int current = start;
        for (int step = 0; step < Mathf.Max(0, maxSteps); step++)
        {
            Vector2Int next = current + direction;
            if (
                !IsCellInBounds(next)
                || IsDirectionalStepBlocked(current, next, direction, blockedCells)
            )
                break;
            current = next;
        }
        return current;
    }

    private static bool IsDirectionalStepBlocked(
        Vector2Int current,
        Vector2Int next,
        Vector2Int direction,
        ISet<Vector2Int> blockedCells
    )
    {
        if (blockedCells == null)
            return false;
        if (blockedCells.Contains(next))
            return true;

        if (direction.x == 0 || direction.y == 0)
            return false;

        // Match the permissive corner rule used by grid line-of-sight: one open side leaves
        // enough room for a diagonal rush, while two closed sides still block the corner.
        return blockedCells.Contains(current + new Vector2Int(direction.x, 0))
            && blockedCells.Contains(current + new Vector2Int(0, direction.y));
    }

    // === GRID MATH (shared by server gameplay, bots, and client overlays) ===

    public const int ColumnCount = 15;
    public const int RowCount = 10;
    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left,
    };

    public static bool IsCellInBounds(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < ColumnCount && cell.y >= 0 && cell.y < RowCount;
    }

    /// <summary>
    /// Returns a square footprint in deterministic row-major order (bottom row to top row,
    /// left to right within each row). A negative radius has no footprint.
    /// </summary>
    public static List<Vector2Int> GetSquareFootprint(Vector2Int center, int radius)
    {
        List<Vector2Int> footprint = new();
        if (radius < 0)
            return footprint;

        for (int rowOffset = -radius; rowOffset <= radius; rowOffset++)
        {
            for (int columnOffset = -radius; columnOffset <= radius; columnOffset++)
            {
                footprint.Add(center + new Vector2Int(columnOffset, rowOffset));
            }
        }
        return footprint;
    }

    /// <summary>True only when every cell in the requested square footprint is on the board.</summary>
    public static bool IsSquareFootprintInBounds(Vector2Int center, int radius)
    {
        if (radius < 0)
            return false;

        foreach (Vector2Int cell in GetSquareFootprint(center, radius))
        {
            if (!IsCellInBounds(cell))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Tests a segment expressed in continuous cell coordinates against whole-cell blockers.
    /// Endpoint cells do not block, allowing a unit at a cloud edge to shoot out or be targeted.
    /// Cell interiors are used so merely touching a cell corner or edge is not a crossing.
    /// </summary>
    public static bool DoesCellSegmentCrossCells(Vector2 start, Vector2 end, ISet<Vector2Int> cells)
    {
        if (cells == null || cells.Count == 0 || start == end)
            return false;

        // Canonical endpoint ordering makes the calculation exactly symmetric when callers swap
        // shooter and target.
        if (start.x > end.x || (start.x == end.x && start.y > end.y))
            (start, end) = (end, start);

        Vector2Int startCell = GetContainingCell(start);
        Vector2Int endCell = GetContainingCell(end);
        foreach (Vector2Int cell in cells)
        {
            if (cell == startCell || cell == endCell)
                continue;
            if (DoesSegmentCrossCellInterior(start, end, cell))
                return true;
        }
        return false;
    }

    public static bool DoesCellSegmentCrossCells(
        Vector2Int start,
        Vector2Int end,
        ISet<Vector2Int> cells
    )
    {
        return DoesCellSegmentCrossCells(
            new Vector2(start.x, start.y),
            new Vector2(end.x, end.y),
            cells
        );
    }

    /// <summary>
    /// World-space wrapper for <see cref="DoesCellSegmentCrossCells"/> using the board origin and
    /// current cell size. Only X/Z coordinates participate in the test.
    /// </summary>
    public static bool DoesWorldSegmentCrossCells(
        Vector3 start,
        Vector3 end,
        ISet<Vector2Int> cells
    )
    {
        if (CellSize <= 0f)
            return false;

        Vector2 origin = new(GameLoop.gridBounds.xMin, GameLoop.gridBounds.yMin);
        Vector2 startCellPosition = (new Vector2(start.x, start.z) - origin) / CellSize;
        Vector2 endCellPosition = (new Vector2(end.x, end.z) - origin) / CellSize;
        return DoesCellSegmentCrossCells(startCellPosition, endCellPosition, cells);
    }

    private static Vector2Int GetContainingCell(Vector2 position)
    {
        return new Vector2Int(
            Mathf.FloorToInt(position.x + 0.5f),
            Mathf.FloorToInt(position.y + 0.5f)
        );
    }

    private static bool DoesSegmentCrossCellInterior(Vector2 start, Vector2 end, Vector2Int cell)
    {
        const float interiorInset = 0.0001f;
        const float halfCell = 0.5f - interiorInset;
        Vector2 minimum = new(cell.x - halfCell, cell.y - halfCell);
        Vector2 maximum = new(cell.x + halfCell, cell.y + halfCell);
        Vector2 delta = end - start;
        float enter = 0f;
        float exit = 1f;

        return ClipSegmentToAxis(start.x, delta.x, minimum.x, maximum.x, ref enter, ref exit)
            && ClipSegmentToAxis(start.y, delta.y, minimum.y, maximum.y, ref enter, ref exit);
    }

    private static bool ClipSegmentToAxis(
        float start,
        float delta,
        float minimum,
        float maximum,
        ref float enter,
        ref float exit
    )
    {
        if (Mathf.Abs(delta) <= Mathf.Epsilon)
            return start >= minimum && start <= maximum;

        float first = (minimum - start) / delta;
        float second = (maximum - start) / delta;
        if (first > second)
            (first, second) = (second, first);

        enter = Mathf.Max(enter, first);
        exit = Mathf.Min(exit, second);
        return enter <= exit;
    }

    /// <summary>
    /// Deterministic four-directional BFS. The returned path includes start and goal. A blocked
    /// goal is allowed so callers can path toward an occupied enemy cell and stop one step short.
    /// </summary>
    public static List<Vector2Int> FindPath(
        Vector2Int start,
        Vector2Int goal,
        ISet<Vector2Int> blockedCells = null
    )
    {
        if (
            !IsCellInBounds(start)
            || !IsCellInBounds(goal)
            || GameLoop.wallLayout.Contains(start)
            || GameLoop.wallLayout.Contains(goal)
        )
        {
            return null;
        }

        Queue<Vector2Int> frontier = new();
        Dictionary<Vector2Int, Vector2Int> cameFrom = new() { [start] = start };
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            if (current == goal)
                break;

            foreach (Vector2Int direction in CardinalDirections)
            {
                Vector2Int next = current + direction;
                if (
                    !IsCellInBounds(next)
                    || GameLoop.wallLayout.Contains(next)
                    || cameFrom.ContainsKey(next)
                    || (next != goal && blockedCells != null && blockedCells.Contains(next))
                )
                {
                    continue;
                }

                cameFrom[next] = current;
                frontier.Enqueue(next);
            }
        }

        if (!cameFrom.ContainsKey(goal))
            return null;

        List<Vector2Int> path = new() { goal };
        while (path[0] != start)
            path.Insert(0, cameFrom[path[0]]);
        return path;
    }

    /// <summary>All cells reachable in at most maxSteps, in deterministic BFS order.</summary>
    public static List<Vector2Int> GetReachableCells(
        Vector2Int start,
        int maxSteps,
        ISet<Vector2Int> blockedCells = null
    )
    {
        List<Vector2Int> reachable = new();
        if (!IsCellInBounds(start) || GameLoop.wallLayout.Contains(start))
            return reachable;

        Queue<Vector2Int> frontier = new();
        Dictionary<Vector2Int, int> distance = new() { [start] = 0 };
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            reachable.Add(current);
            if (distance[current] >= Mathf.Max(0, maxSteps))
                continue;

            foreach (Vector2Int direction in CardinalDirections)
            {
                Vector2Int next = current + direction;
                if (
                    !IsCellInBounds(next)
                    || GameLoop.wallLayout.Contains(next)
                    || distance.ContainsKey(next)
                    || (next != start && blockedCells != null && blockedCells.Contains(next))
                )
                {
                    continue;
                }

                distance[next] = distance[current] + 1;
                frontier.Enqueue(next);
            }
        }

        return reachable;
    }

    /// <summary>Four-directional step count between two cells, ignoring walls and occupancy.</summary>
    public static int GetGridDistance(Vector2Int from, Vector2Int to)
    {
        return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
    }

    /// <summary>
    /// Row-major ordering (bottom row first, left to right within a row), matching the order
    /// square footprints are enumerated in. Used wherever a tie between two cells has to resolve
    /// the same way on every peer.
    /// </summary>
    public static int CompareCellsRowMajor(Vector2Int left, Vector2Int right)
    {
        int rowComparison = left.y.CompareTo(right.y);
        return rowComparison != 0 ? rowComparison : left.x.CompareTo(right.x);
    }

    /// <summary>
    /// Picks the cell a unit is set down on when it has to give up the one it is standing on.
    /// Candidates are unclaimed cells within <paramref name="maxSteps"/> of walking around walls,
    /// so a shove can squeeze past another body — the way units already pass through each other
    /// mid-move — but never through a wall or off the board. The nearest candidate wins, pulled
    /// toward <paramref name="anchor"/> (normally the cell the unit came from, so it gives ground
    /// the way it arrived) and settled row-major when that is still a tie. False when everything
    /// in range is spoken for and there is nowhere to put the unit.
    /// </summary>
    public static bool TryFindDisplacementCell(
        Vector2Int origin,
        Vector2Int anchor,
        ISet<Vector2Int> occupiedCells,
        int maxSteps,
        out Vector2Int displacementCell
    )
    {
        displacementCell = origin;
        bool found = false;
        int nearestDistance = int.MaxValue;
        int nearestAnchorDistance = int.MaxValue;

        foreach (Vector2Int candidate in GetReachableCells(origin, maxSteps))
        {
            if (
                candidate == origin
                || (occupiedCells != null && occupiedCells.Contains(candidate))
            )
            {
                continue;
            }

            int distance = GetGridDistance(origin, candidate);
            int anchorDistance = GetGridDistance(anchor, candidate);
            bool nearer =
                !found
                || distance < nearestDistance
                || (
                    distance == nearestDistance
                    && (
                        anchorDistance < nearestAnchorDistance
                        || (
                            anchorDistance == nearestAnchorDistance
                            && CompareCellsRowMajor(candidate, displacementCell) < 0
                        )
                    )
                );
            if (!nearer)
                continue;

            displacementCell = candidate;
            nearestDistance = distance;
            nearestAnchorDistance = anchorDistance;
            found = true;
        }

        if (!found)
            displacementCell = origin;
        return found;
    }

    // === FOG OF WAR VISION MATH (pure/static, shared by server visibility + client overlay) ===

    /// <summary>
    /// Grid line-of-sight over wallLayout using a supercover line walk. Endpoints never block.
    /// When the ideal line crosses exactly through a cell corner, sight is blocked only if BOTH
    /// corner-adjacent cells are walls (permissive diagonals).
    /// </summary>
    public static bool HasGridLineOfSight(Vector2Int viewer, Vector2Int target)
    {
        int x = viewer.x;
        int y = viewer.y;
        int dx = Mathf.Abs(target.x - viewer.x);
        int dy = Mathf.Abs(target.y - viewer.y);
        int stepX = target.x >= viewer.x ? 1 : -1;
        int stepY = target.y >= viewer.y ? 1 : -1;
        int crossedX = 0;
        int crossedY = 0;

        while (crossedX < dx || crossedY < dy)
        {
            int horizontalDecision = (1 + 2 * crossedX) * dy;
            int verticalDecision = (1 + 2 * crossedY) * dx;

            if (horizontalDecision == verticalDecision)
            {
                // The ideal line crosses a corner. It is blocked only when both side cells
                // are walls; one open side permits diagonal sight around the corner.
                Vector2Int horizontalSide = new(x + stepX, y);
                Vector2Int verticalSide = new(x, y + stepY);
                if (
                    IsBlockingIntermediateCell(horizontalSide, viewer, target)
                    && IsBlockingIntermediateCell(verticalSide, viewer, target)
                )
                {
                    return false;
                }

                x += stepX;
                y += stepY;
                crossedX++;
                crossedY++;
            }
            else if (horizontalDecision < verticalDecision)
            {
                x += stepX;
                crossedX++;
            }
            else
            {
                y += stepY;
                crossedY++;
            }

            if (IsBlockingIntermediateCell(new Vector2Int(x, y), viewer, target))
                return false;
        }

        return true;
    }

    private static bool IsBlockingIntermediateCell(
        Vector2Int cell,
        Vector2Int viewer,
        Vector2Int target
    )
    {
        return cell != viewer && cell != target && GameLoop.wallLayout.Contains(cell);
    }

    /// <summary>
    /// Union of every viewer's visible cell set: Manhattan diamond of the viewer's range,
    /// intersected with grid LoS and optional transient whole-cell blockers, clamped to the
    /// board. Transient endpoint cells do not block, and edge/corner grazes remain clear.
    /// </summary>
    public static HashSet<Vector2Int> ComputeVisibleCells(
        IEnumerable<(Vector2Int cell, int range)> viewers,
        ISet<Vector2Int> transientBlockingCells = null
    )
    {
        HashSet<Vector2Int> visible = new();
        if (viewers == null)
            return visible;

        foreach ((Vector2Int viewerCell, int configuredRange) in viewers)
        {
            int range = Mathf.Max(0, configuredRange);
            for (int rowOffset = -range; rowOffset <= range; rowOffset++)
            {
                int horizontalRange = range - Mathf.Abs(rowOffset);
                for (
                    int columnOffset = -horizontalRange;
                    columnOffset <= horizontalRange;
                    columnOffset++
                )
                {
                    Vector2Int target = new(viewerCell.x + columnOffset, viewerCell.y + rowOffset);
                    if (!IsCellInBounds(target))
                        continue;
                    if (
                        HasGridLineOfSight(viewerCell, target)
                        && !DoesCellSegmentCrossCells(viewerCell, target, transientBlockingCells)
                    )
                    {
                        visible.Add(target);
                    }
                }
            }
        }

        return visible;
    }
}
