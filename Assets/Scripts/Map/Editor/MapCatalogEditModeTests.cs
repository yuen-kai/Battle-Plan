using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The acceptance gate from <c>docs/design/MapPlan.md</c> §4, run against every catalog entry.
/// A new board is playable only once it passes all of this, so the checks are written to be
/// map-agnostic rather than pinned to any one layout.
/// </summary>
public class MapCatalogEditModeTests
{
    private MapId originalMapId;

    [SetUp]
    public void SetUp() => originalMapId = MatchOptions.Current.mapId;

    [TearDown]
    public void TearDown()
    {
        MatchOptions options = MatchOptions.Current;
        options.mapId = originalMapId;
        MatchOptions.SetCurrent(options);
    }

    private static IEnumerable<MapDefinition> Maps => MapCatalog.All;

    private static void Activate(MapId mapId)
    {
        MatchOptions options = MatchOptions.Current;
        options.mapId = mapId;
        MatchOptions.SetCurrent(options);
    }

    [Test]
    public void Catalog_IdsAreUniqueAndResolvable()
    {
        Assert.That(MapCatalog.All.Count, Is.GreaterThan(0));
        CollectionAssert.AllItemsAreUnique(MapCatalog.All.Select(map => map.Id).ToList());
        CollectionAssert.AllItemsAreUnique(
            MapCatalog.All.Select(map => map.DisplayName).ToList()
        );

        foreach (MapDefinition map in Maps)
        {
            Assert.That(MapCatalog.IsKnown(map.Id), Is.True, map.DisplayName);
            Assert.That(MapCatalog.ById(map.Id), Is.SameAs(map), map.DisplayName);
            Assert.That(map.DisplayName, Is.Not.Null.And.Not.Empty);
            Assert.That(map.Caption, Is.Not.Null.And.Not.Empty);
        }

        // An unknown id must degrade to a playable board rather than a null layout, because it can
        // arrive over the wire from a peer on a different build.
        Assert.That(MapCatalog.ById((MapId)200), Is.SameAs(MapCatalog.Fallback));
    }

    [Test]
    public void EveryMap_IsPointSymmetric()
    {
        foreach (MapDefinition map in Maps)
        {
            foreach (Vector2Int wall in map.Walls)
            {
                Vector2Int rotated = new(
                    GridSystem.ColumnCount - 1 - wall.x,
                    GridSystem.RowCount - 1 - wall.y
                );
                Assert.That(
                    map.Walls.Contains(rotated),
                    Is.True,
                    $"{map.DisplayName}: {wall} has no 180-degree partner at {rotated}."
                );
            }
        }
    }

    [Test]
    public void EveryMap_IsInBoundsAndFullyConnected()
    {
        foreach (MapDefinition map in Maps)
        {
            Activate(map.Id);

            foreach (Vector2Int wall in map.Walls)
            {
                Assert.That(
                    GridSystem.IsCellInBounds(wall),
                    Is.True,
                    $"{map.DisplayName}: wall {wall} is off the board."
                );
            }

            int openCells = GridSystem.ColumnCount * GridSystem.RowCount - map.Walls.Count;
            Vector2Int start = FirstOpenCell(map);
            Assert.That(
                GridSystem.GetReachableCells(start, 999).Count,
                Is.EqualTo(openCells),
                $"{map.DisplayName}: board is not one connected region."
            );
        }
    }

    [Test]
    public void EveryMap_KeepsHillAndSpawnsClear()
    {
        foreach (MapDefinition map in Maps)
        {
            Activate(map.Id);

            Assert.That(map.HillCells.Count, Is.EqualTo(12), map.DisplayName);
            foreach (Vector2Int cell in map.HillCells)
            {
                Assert.That(GridSystem.IsCellInBounds(cell), Is.True, map.DisplayName);
                Assert.That(
                    map.Walls.Contains(cell),
                    Is.False,
                    $"{map.DisplayName}: hill cell {cell} is walled."
                );
            }

            for (int teamIndex = 0; teamIndex < GameLoop.TeamCount; teamIndex++)
            {
                Vector2Int[] spawns = GameLoop.CreateSpawnPositions(
                    false,
                    teamIndex,
                    RosterRules.UnitsPerPlayer
                );
                Assert.That(spawns.Length, Is.EqualTo(RosterRules.UnitsPerPlayer), map.DisplayName);
                Assert.That(spawns.Distinct().Count(), Is.EqualTo(spawns.Length), map.DisplayName);
                foreach (Vector2Int cell in spawns)
                {
                    Assert.That(GridSystem.IsCellInBounds(cell), Is.True, map.DisplayName);
                    Assert.That(
                        map.Walls.Contains(cell),
                        Is.False,
                        $"{map.DisplayName}: spawn {cell} is inside a wall."
                    );
                    Assert.That(
                        map.HillCells.Contains(cell),
                        Is.False,
                        $"{map.DisplayName}: spawn {cell} starts on the objective."
                    );
                }
            }
        }
    }

    [Test]
    public void EveryMap_MirrorsDeploymentBetweenTeams()
    {
        foreach (MapDefinition map in Maps)
        {
            Activate(map.Id);

            Vector2Int[] host = GameLoop.CreateSpawnPositions(
                false,
                GameLoop.HostTeamIndex,
                RosterRules.UnitsPerPlayer
            );
            Vector2Int[] opponent = GameLoop.CreateSpawnPositions(
                false,
                GameLoop.OpponentTeamIndex,
                RosterRules.UnitsPerPlayer
            );

            for (int slot = 0; slot < host.Length; slot++)
            {
                Vector2Int rotated = new(
                    GridSystem.ColumnCount - 1 - host[slot].x,
                    GridSystem.RowCount - 1 - host[slot].y
                );
                Assert.That(
                    opponent[slot],
                    Is.EqualTo(rotated),
                    $"{map.DisplayName}: slot {slot} is not mirrored between teams."
                );
            }
        }
    }

    /// <summary>
    /// Gate item 5: every slot must be able to reach the objective, and both teams must face the
    /// identical travel profile. Round-one access is allowed but recorded per map, because it is a
    /// live balance question rather than a defect.
    /// </summary>
    [Test]
    public void EveryMap_ReachesTheHillWithinFourRounds()
    {
        const int moveDistance = 3;
        const int maximumRounds = 4;

        foreach (MapDefinition map in Maps)
        {
            Activate(map.Id);

            List<int> hostRounds = RoundsToHill(map, GameLoop.HostTeamIndex, moveDistance);
            List<int> opponentRounds = RoundsToHill(map, GameLoop.OpponentTeamIndex, moveDistance);

            CollectionAssert.AreEqual(
                hostRounds,
                opponentRounds,
                $"{map.DisplayName}: the two teams do not face the same travel profile."
            );

            foreach (int rounds in hostRounds)
            {
                Assert.That(
                    rounds,
                    Is.InRange(1, maximumRounds),
                    $"{map.DisplayName}: a slot needs {rounds} rounds to reach the hill."
                );
            }
        }
    }

    private static List<int> RoundsToHill(MapDefinition map, int teamIndex, int moveDistance)
    {
        Vector2Int[] spawns = GameLoop.CreateSpawnPositions(
            false,
            teamIndex,
            RosterRules.UnitsPerPlayer
        );

        List<int> rounds = new(spawns.Length);
        foreach (Vector2Int spawn in spawns)
        {
            int best = int.MaxValue;
            foreach (Vector2Int hill in map.HillCells)
            {
                List<Vector2Int> path = GridSystem.FindPath(spawn, hill);
                if (path == null || path.Count == 0)
                    continue;
                best = Mathf.Min(best, path.Count - 1);
            }

            Assert.That(
                best,
                Is.Not.EqualTo(int.MaxValue),
                $"{map.DisplayName}: spawn {spawn} cannot reach the hill at all."
            );
            rounds.Add(Mathf.CeilToInt(best / (float)moveDistance));
        }
        return rounds;
    }

    private static Vector2Int FirstOpenCell(MapDefinition map)
    {
        for (int x = 0; x < GridSystem.ColumnCount; x++)
        {
            for (int y = 0; y < GridSystem.RowCount; y++)
            {
                Vector2Int cell = new(x, y);
                if (!map.Walls.Contains(cell))
                    return cell;
            }
        }
        throw new AssertionException($"{map.DisplayName} has no open cells.");
    }
}
