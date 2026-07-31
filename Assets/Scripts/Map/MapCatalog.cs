using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every board the game can play, and which one is live.
///
/// <see cref="Active"/> is a plain cached field rather than a lookup, because
/// <c>GameLoop.wallLayout</c> resolves through it inside the line-of-sight inner loop. It is
/// repointed only from <see cref="MatchOptions.SetCurrent"/>, so host and client follow the same
/// replicated match options and can never disagree about where the walls are.
///
/// The set spans one dial — how exposed the objective is — so boards can be read against each
/// other. See <c>docs/design/MapPlan.md</c>.
/// </summary>
public static class MapCatalog
{
    public static readonly MapDefinition Concourse = new(
        MapId.Concourse,
        "Concourse",
        "Open hall · long crossing lanes",
        new[]
        {
            "...............",
            "....#.....#....",
            ".......#.......",
            "..#..#...#..#..",
            "#.............#",
            "#.............#",
            "..#..#...#..#..",
            ".......#.......",
            "....#.....#....",
            "...............",
        }
    );

    /// <summary>
    /// Concourse's blockout with corner-weighted deployment, so the contested axis runs corner to
    /// corner. Two slots reach the hill in one round and two take the long way, which makes roster
    /// order a real deployment decision.
    /// </summary>
    public static readonly MapDefinition ConcourseOblique = new(
        MapId.ConcourseOblique,
        "Concourse Oblique",
        "Same hall · diagonal contest",
        new[]
        {
            "...............",
            "....#.....#....",
            ".......#.......",
            "..#..#...#..#..",
            "#.............#",
            "#.............#",
            "..#..#...#..#..",
            ".......#.......",
            "....#.....#....",
            "...............",
        },
        hostDeploymentColumns: new Vector2Int(0, 8)
    );

    /// <summary>
    /// The hill as a walled compound with four one-cell doorways at (8,7), (6,2), (5,5) and (9,4),
    /// offset so no straight line runs through the objective.
    /// </summary>
    public static readonly MapDefinition Bastion = new(
        MapId.Bastion,
        "Bastion",
        "Fortified hill · four doorways",
        new[]
        {
            "...............",
            "..#.#.....#.#..",
            ".#...###.#...#.",
            "..#..#...#..#..",
            "...#.....#.#...",
            "...#.#.....#...",
            "..#..#...#..#..",
            ".#...#.###...#.",
            "..#.#.....#.#..",
            "...............",
        }
    );

    /// <summary>Dense close quarters: no straight line anywhere exceeds six cells.</summary>
    public static readonly MapDefinition Foundry = new(
        MapId.Foundry,
        "Foundry",
        "Close quarters · short sightlines",
        new[]
        {
            "...............",
            "..#...#.#...#..",
            "#...#..#..#...#",
            ".#...#...#...#.",
            "...##.....##...",
            "...##.....##...",
            ".#...#...#...#.",
            "#...#..#..#...#",
            "..#...#.#...#..",
            "...............",
        }
    );

    /// <summary>
    /// Two spines split the board into an exposed centre court and two protected side corridors,
    /// each with a near and a far doorway, so route choice costs something.
    /// </summary>
    public static readonly MapDefinition Causeway = new(
        MapId.Causeway,
        "Causeway",
        "Spines · centre court and side corridors",
        new[]
        {
            "...#.......#...",
            "...#..#....#...",
            ".......##......",
            "...#.#...#.#...",
            "...#.......#...",
            "...#.......#...",
            "...#.#...#.#...",
            "......##.......",
            "...#....#..#...",
            "...#.......#...",
        }
    );

    public static readonly IReadOnlyList<MapDefinition> All = new[]
    {
        Concourse,
        ConcourseOblique,
        Bastion,
        Foundry,
        Causeway,
    };

    public static readonly MapDefinition Fallback = Concourse;

    private static MapDefinition active = Fallback;

    public static MapDefinition Active => active;

    public static bool IsKnown(MapId id) => Find(id) != null;

    public static MapDefinition ById(MapId id) => Find(id) ?? Fallback;

    public static void SetActive(MapId id)
    {
        active = ById(id);
    }

    /// <summary>
    /// Points the board at a definition directly, including an off-catalog
    /// <see cref="MapDefinition.Scratch"/>. Callers own restoring the previous value; the next
    /// <see cref="MatchOptions.SetCurrent"/> will reset it to the selected map regardless.
    /// </summary>
    public static void SetActive(MapDefinition map)
    {
        active = map ?? Fallback;
    }

    private static MapDefinition Find(MapId id)
    {
        for (int index = 0; index < All.Count; index++)
        {
            if (All[index].Id == id)
                return All[index];
        }
        return null;
    }
}
