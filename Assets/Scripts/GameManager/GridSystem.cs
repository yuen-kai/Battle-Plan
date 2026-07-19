using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GridSystem : MonoBehaviour
{
    [SerializeField]
    private GameObject cellPrefab; // Public variable for the GameObject prefab
    public static float CellSize => GameLoop.cellSize; // Size of each grid cell

    [SerializeField]
    private int gridWidth = 10; // Width of the grid

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

    public static GameObject DisplayGridRange(
        Vector3 currentPos,
        int dist,
        GameObject overlayPrefab
    )
    {
        GameObject overlay = new("MoveOverlay");

        for (int i = -dist; i <= dist; i++)
        {
            int horizontalDist = dist - Mathf.Abs(i);
            for (int j = -horizontalDist; j <= horizontalDist; j++)
            {
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

        return blockedCells.Contains(current + new Vector2Int(direction.x, 0))
            || blockedCells.Contains(current + new Vector2Int(0, direction.y));
    }

    // === FOG OF WAR VISION MATH (pure/static, shared by server visibility + client overlay) ===

    public const int ColumnCount = 9;
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
    /// intersected with grid LoS, clamped to the board.
    /// </summary>
    public static HashSet<Vector2Int> ComputeVisibleCells(
        IEnumerable<(Vector2Int cell, int range)> viewers
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
                    if (HasGridLineOfSight(viewerCell, target))
                        visible.Add(target);
                }
            }
        }

        return visible;
    }
}
