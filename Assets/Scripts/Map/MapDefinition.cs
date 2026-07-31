using System.Collections.Generic;
using UnityEngine;

public enum MapId : byte
{
    Concourse = 0,
    ConcourseOblique = 1,
    Bastion = 2,
    Foundry = 3,
    Causeway = 4,
}

/// <summary>
/// One board: its blockout, its objective, and where crews deploy. Everything else about the
/// arena (15x10 size, cell size, camera seats) is shared, so a map is pure data and adding one
/// costs no scene or prefab work.
///
/// Layouts are authored as ASCII rather than coordinate lists because the design plan
/// (<c>docs/design/MapPlan.md</c>) carries the same pictures — a drift between plan and code is
/// visible at a glance instead of hiding in eighteen <c>Vector2Int</c> constructors.
/// </summary>
public sealed class MapDefinition
{
    /// <summary>Central 3x4 objective shared by every current board.</summary>
    public static readonly HashSet<Vector2Int> CentralHill = BuildCentralHill();

    public readonly MapId Id;
    public readonly string DisplayName;
    public readonly string Caption;
    public readonly HashSet<Vector2Int> Walls;
    public readonly HashSet<Vector2Int> HillCells;

    /// <summary>
    /// Columns the host crew spreads across on its back rank, opponent mirrored. Null keeps the
    /// historical behaviour: the widest inset band the unit count allows.
    /// </summary>
    public readonly Vector2Int? HostDeploymentColumns;

    public MapDefinition(
        MapId id,
        string displayName,
        string caption,
        string[] layout,
        Vector2Int? hostDeploymentColumns = null,
        HashSet<Vector2Int> hillCells = null
    )
    {
        Id = id;
        DisplayName = displayName;
        Caption = caption;
        Walls = ParseLayout(layout);
        HillCells = hillCells ?? CentralHill;
        HostDeploymentColumns = hostDeploymentColumns;
    }

    private MapDefinition(
        MapId id,
        string displayName,
        string caption,
        HashSet<Vector2Int> walls,
        HashSet<Vector2Int> hillCells,
        Vector2Int? hostDeploymentColumns
    )
    {
        Id = id;
        DisplayName = displayName;
        Caption = caption;
        Walls = walls;
        HillCells = hillCells ?? CentralHill;
        HostDeploymentColumns = hostDeploymentColumns;
    }

    /// <summary>
    /// An off-catalog board built from an explicit wall set, for tests and tools that need to
    /// isolate behaviour against geometry no shipped map has. It borrows the fallback's id, so it
    /// is never something a peer can select or a lobby can list.
    /// </summary>
    public static MapDefinition Scratch(
        IEnumerable<Vector2Int> walls,
        HashSet<Vector2Int> hillCells = null,
        Vector2Int? hostDeploymentColumns = null
    )
    {
        return new MapDefinition(
            MapId.Concourse,
            "Scratch board",
            "Ad-hoc geometry",
            new HashSet<Vector2Int>(walls ?? System.Array.Empty<Vector2Int>()),
            hillCells,
            hostDeploymentColumns
        );
    }

    /// <summary>
    /// Rows are given top-down so the literal reads like the board on screen: the first string is
    /// row <c>RowCount - 1</c>, the last is row 0. '#' is a wall, anything else is open.
    /// </summary>
    private static HashSet<Vector2Int> ParseLayout(string[] rowsTopDown)
    {
        if (rowsTopDown == null || rowsTopDown.Length != GridSystem.RowCount)
        {
            throw new System.ArgumentException(
                $"Layout needs exactly {GridSystem.RowCount} rows, got {rowsTopDown?.Length ?? 0}."
            );
        }

        HashSet<Vector2Int> walls = new();
        for (int index = 0; index < rowsTopDown.Length; index++)
        {
            string row = rowsTopDown[index];
            if (row == null || row.Length != GridSystem.ColumnCount)
            {
                throw new System.ArgumentException(
                    $"Layout row {index} needs exactly {GridSystem.ColumnCount} columns, got {row?.Length ?? 0}."
                );
            }

            int y = GridSystem.RowCount - 1 - index;
            for (int x = 0; x < row.Length; x++)
            {
                if (row[x] == '#')
                    walls.Add(new Vector2Int(x, y));
            }
        }
        return walls;
    }

    private static HashSet<Vector2Int> BuildCentralHill()
    {
        HashSet<Vector2Int> cells = new();
        for (int x = 6; x <= 8; x++)
        {
            for (int y = 3; y <= 6; y++)
                cells.Add(new Vector2Int(x, y));
        }
        return cells;
    }
}
