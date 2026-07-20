using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
[Category("BotExploration")]
public class BotExplorationEditModeTests
{
    private const int ProductionMoveDistance = 3;

    private static readonly Vector2Int[] ProductionBotSpawns = { new(8, 9), new(4, 9), new(0, 9) };

    [Test]
    public void ProductionSpawns_MoveWithoutEnemyKnowledge()
    {
        HashSet<Vector2Int> observed = CreateProductionBotObservation();
        HashSet<Vector2Int> occupied = new(ProductionBotSpawns);

        foreach (Vector2Int start in ProductionBotSpawns)
        {
            occupied.Remove(start);
            List<Vector2Int> path = BotPlayer.BuildMovementPath(
                start,
                Array.Empty<Vector2Int>(),
                new HashSet<Vector2Int>(),
                occupied,
                ProductionMoveDistance,
                observed
            );

            Assert.That(
                path.Count,
                Is.EqualTo(ProductionMoveDistance + 1),
                $"Expected a full exploration move from production spawn {start}."
            );
            AssertLegalPath(path, start, occupied, ProductionMoveDistance);
            BotPlayer.ReservePathCells(occupied, path);
        }
    }

    [Test]
    public void Exploration_IsDeterministicAcrossObservationAndBlockerOrdering()
    {
        Vector2Int start = new(4, 9);
        List<Vector2Int> observed = CreateProductionBotObservation()
            .OrderBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .ToList();
        Vector2Int[] blockers = { new(8, 9), new(0, 9), new(4, 8) };

        List<Vector2Int> first = BotPlayer.BuildMovementPath(
            start,
            Array.Empty<Vector2Int>(),
            new HashSet<Vector2Int>(),
            new HashSet<Vector2Int>(blockers),
            ProductionMoveDistance,
            observed
        );
        List<Vector2Int> second = BotPlayer.BuildMovementPath(
            start,
            Array.Empty<Vector2Int>(),
            new HashSet<Vector2Int>(),
            new HashSet<Vector2Int>(blockers.Reverse()),
            ProductionMoveDistance,
            observed.AsEnumerable().Reverse()
        );

        Assert.That(first.Count, Is.GreaterThan(1));
        CollectionAssert.AreEqual(first, second);
    }

    [Test]
    public void Exploration_PathStaysInBoundsAndAvoidsKnownBlockers()
    {
        Vector2Int start = new(8, 9);
        HashSet<Vector2Int> blocked = new()
        {
            new Vector2Int(8, 8),
            new Vector2Int(6, 9),
            new Vector2Int(4, 8),
            new Vector2Int(4, 7),
            new Vector2Int(2, 9),
        };
        const int maxSteps = 20;

        List<Vector2Int> path = BotPlayer.BuildMovementPath(
            start,
            Array.Empty<Vector2Int>(),
            new HashSet<Vector2Int>(),
            blocked,
            maxSteps,
            Array.Empty<Vector2Int>()
        );

        Assert.That(path.Count, Is.GreaterThan(1));
        AssertLegalPath(path, start, blocked, maxSteps);
    }

    [Test]
    public void ObservationBoundary_FiltersDifferentHiddenEnemyCoordinates()
    {
        Vector2Int visibleCell = new(4, 4);
        HashSet<Vector2Int> observableCells = new() { visibleCell };
        BotEnemySighting[] hiddenOnLeft = { new(1, visibleCell), new(2, new Vector2Int(0, 0)) };
        BotEnemySighting[] hiddenOnRight = { new(1, visibleCell), new(2, new Vector2Int(8, 9)) };

        List<BotEnemySighting> leftResult = GameLoop.FilterObservableEnemySightings(
            hiddenOnLeft,
            observableCells,
            true
        );
        List<BotEnemySighting> rightResult = GameLoop.FilterObservableEnemySightings(
            hiddenOnRight,
            observableCells,
            true
        );

        Assert.That(leftResult.Count, Is.EqualTo(1));
        Assert.That(rightResult.Count, Is.EqualTo(1));
        Assert.That(leftResult[0].EnemyId, Is.EqualTo(1));
        Assert.That(rightResult[0].EnemyId, Is.EqualTo(1));
        Assert.That(leftResult[0].Cell, Is.EqualTo(visibleCell));
        Assert.That(rightResult[0].Cell, Is.EqualTo(visibleCell));
    }

    [Test]
    public void LastKnownTarget_TakesPriorityOverExplorationFrontier()
    {
        Vector2Int start = new(4, 9);
        Vector2Int lastKnownTarget = new(8, 0);
        HashSet<Vector2Int> observed = CreateProductionBotObservation();
        HashSet<Vector2Int> knownOccupied = new() { new Vector2Int(8, 9), new Vector2Int(0, 9) };

        Assert.That(observed.Contains(lastKnownTarget), Is.False);
        List<Vector2Int> path = BotPlayer.BuildMovementPath(
            start,
            new[] { lastKnownTarget },
            new HashSet<Vector2Int>(),
            knownOccupied,
            ProductionMoveDistance,
            observed
        );

        Assert.That(path.Count, Is.EqualTo(ProductionMoveDistance + 1));
        Assert.That(path[1], Is.EqualTo(new Vector2Int(5, 9)));
        AssertLegalPath(path, start, knownOccupied, ProductionMoveDistance);
    }

    [Test]
    public void FullCoverage_PatrolsLeastRecentlyObservedRegion()
    {
        Vector2Int start = new(4, 9);
        Vector2Int staleTarget = new(0, 0);
        HashSet<Vector2Int> observed = AllTraversableCells();
        Dictionary<Vector2Int, int> observationEpochs = observed.ToDictionary(
            cell => cell,
            _ => 10
        );
        observationEpochs[staleTarget] = 1;

        List<Vector2Int> path = BotPlayer.BuildMovementPath(
            start,
            Array.Empty<Vector2Int>(),
            new HashSet<Vector2Int>(),
            new HashSet<Vector2Int>(),
            ProductionMoveDistance,
            observed,
            observationEpochs
        );

        Assert.That(path.Count, Is.EqualTo(ProductionMoveDistance + 1));
        Assert.That(
            GridDistance(path[^1], staleTarget),
            Is.LessThan(GridDistance(start, staleTarget))
        );
        AssertLegalPath(path, start, new HashSet<Vector2Int>(), ProductionMoveDistance);
    }

    [Test]
    public void SequentialReservations_PreventCrossingAndSwapCollisions()
    {
        Vector2Int[] starts = { new(0, 0), new(0, 1) };
        Vector2Int target = new(8, 1);
        HashSet<Vector2Int> reserved = new(starts);
        List<List<Vector2Int>> paths = new();

        foreach (Vector2Int start in starts)
        {
            reserved.Remove(start);
            List<Vector2Int> path = BotPlayer.BuildMovementPath(
                start,
                new[] { target },
                new HashSet<Vector2Int>(),
                reserved,
                ProductionMoveDistance
            );

            Assert.That(path.Count, Is.GreaterThan(1));
            AssertLegalPath(path, start, reserved, ProductionMoveDistance);
            paths.Add(path);
            BotPlayer.ReservePathCells(reserved, path);
        }

        CollectionAssert.IsEmpty(paths[0].Intersect(paths[1]).ToArray());
    }

    [Test]
    public void ObservationBoundary_HonorsFogFlagAndDropsAllHiddenSightings()
    {
        Vector2Int visibleCell = new(4, 4);
        HashSet<Vector2Int> observable = new() { visibleCell };
        BotEnemySighting[] sightings =
        {
            new(2, new Vector2Int(0, 0)),
            new(1, visibleCell),
            new(3, new Vector2Int(8, 9)),
        };

        // Fog on: only the sighting standing on an observable cell survives.
        List<BotEnemySighting> fogOn = GameLoop.FilterObservableEnemySightings(
            sightings,
            observable,
            true
        );
        Assert.That(fogOn.Count, Is.EqualTo(1));
        Assert.That(fogOn[0].EnemyId, Is.EqualTo(1));
        Assert.That(fogOn[0].Cell, Is.EqualTo(visibleCell));

        // Fog on with nothing (or no set) observable: no hidden coordinate leaks through.
        Assert.That(
            GameLoop.FilterObservableEnemySightings(sightings, new HashSet<Vector2Int>(), true),
            Is.Empty
        );
        Assert.That(GameLoop.FilterObservableEnemySightings(sightings, null, true), Is.Empty);
        Assert.That(GameLoop.FilterObservableEnemySightings(null, observable, true), Is.Empty);

        // Fog off is the mirror: every authoritative sighting is visible, ordered by enemy id.
        List<BotEnemySighting> fogOff = GameLoop.FilterObservableEnemySightings(
            sightings,
            observable,
            false
        );
        CollectionAssert.AreEqual(
            new ulong[] { 1, 2, 3 },
            fogOff.Select(sighting => sighting.EnemyId).ToArray()
        );
    }

    private static HashSet<Vector2Int> CreateProductionBotObservation()
    {
        return GridSystem.ComputeVisibleCells(
            new[]
            {
                (new Vector2Int(8, 9), 3),
                (new Vector2Int(4, 9), 8),
                (new Vector2Int(0, 9), 5),
            }
        );
    }

    private static HashSet<Vector2Int> AllTraversableCells()
    {
        HashSet<Vector2Int> cells = new();
        for (int column = 0; column < GridSystem.ColumnCount; column++)
        {
            for (int row = 0; row < GridSystem.RowCount; row++)
            {
                Vector2Int cell = new(column, row);
                if (!GameLoop.wallLayout.Contains(cell))
                    cells.Add(cell);
            }
        }
        return cells;
    }

    private static int GridDistance(Vector2Int left, Vector2Int right)
    {
        return Mathf.Abs(left.x - right.x) + Mathf.Abs(left.y - right.y);
    }

    private static void AssertLegalPath(
        IReadOnlyList<Vector2Int> path,
        Vector2Int start,
        ISet<Vector2Int> blocked,
        int maxSteps
    )
    {
        Assert.That(path, Is.Not.Null);
        Assert.That(path.Count, Is.InRange(1, maxSteps + 1));
        Assert.That(path[0], Is.EqualTo(start));

        for (int index = 0; index < path.Count; index++)
        {
            Vector2Int cell = path[index];
            Assert.That(GridSystem.IsCellInBounds(cell), Is.True, $"{cell} is out of bounds.");
            Assert.That(GameLoop.wallLayout.Contains(cell), Is.False, $"{cell} is a wall.");
            if (index > 0)
                Assert.That(blocked.Contains(cell), Is.False, $"{cell} is blocked.");

            if (index == 0)
                continue;

            int stepDistance =
                Mathf.Abs(cell.x - path[index - 1].x) + Mathf.Abs(cell.y - path[index - 1].y);
            Assert.That(stepDistance, Is.EqualTo(1), $"Step {index} is not adjacent.");
        }
    }
}
