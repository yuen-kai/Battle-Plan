using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// Pure coverage for the one-unit-per-cell rule. Both halves of it are expressed as static grid
// math so they can be pinned down without a scene, a NetworkManager, or Play mode: the planning
// half trims routes until each finishes somewhere unclaimed, and the execution half picks the cell
// a unit is set down on when it has to give one up. The parts that genuinely need live units — the
// client refusing to draw onto a held cell, and the slide that runs once movement and abilities
// have settled — are runtime acceptance and are intentionally not exercised here.
[TestFixture]
[Category("UnitOverlap")]
public class UnitOverlapEditModeTests
{
    // === Planning: routes are trimmed until every one of them ends somewhere different ===

    [Test]
    public void EndCellTrim_UnitHoldingItsGroundKeepsTheCellAndTheWalkerStopsShort()
    {
        // A unit staying put submits a one-cell route, so it is resolved first by the caller and
        // simply claims where it is standing.
        List<Vector2Int> holdingRoute = new() { new Vector2Int(5, 5) };
        List<Vector2Int> walkingRoute =
            new()
            {
                new Vector2Int(3, 5),
                new Vector2Int(4, 5),
                new Vector2Int(5, 5),
            };
        HashSet<Vector2Int> claimed = new();

        List<int> lengths = GameLoop.ResolveUniqueEndCellLengths(
            new List<IReadOnlyList<Vector2Int>> { holdingRoute, walkingRoute },
            claimed
        );

        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            lengths,
            "The stationary unit keeps its cell and the walker gives up its last step."
        );
        CollectionAssert.AreEquivalent(
            new[] { new Vector2Int(5, 5), new Vector2Int(4, 5) },
            claimed,
            "Both destinations are claimed, and they are different cells."
        );
    }

    [Test]
    public void EndCellTrim_EarlierRouteKeepsItsDestinationWhicheverSideIsListedFirst()
    {
        List<Vector2Int> fromTheLeft =
            new()
            {
                new Vector2Int(1, 1),
                new Vector2Int(2, 1),
                new Vector2Int(3, 1),
            };
        List<Vector2Int> fromTheRight =
            new()
            {
                new Vector2Int(5, 1),
                new Vector2Int(4, 1),
                new Vector2Int(3, 1),
            };

        CollectionAssert.AreEqual(
            new[] { 3, 2 },
            GameLoop.ResolveUniqueEndCellLengths(
                new List<IReadOnlyList<Vector2Int>> { fromTheLeft, fromTheRight }
            ),
            "The first route keeps (3,1); the second stops on (4,1)."
        );
        CollectionAssert.AreEqual(
            new[] { 3, 2 },
            GameLoop.ResolveUniqueEndCellLengths(
                new List<IReadOnlyList<Vector2Int>> { fromTheRight, fromTheLeft }
            ),
            "Swapping the order swaps who is trimmed, and nothing else."
        );
    }

    [Test]
    public void EndCellTrim_SeededClaimsRepresentUnitsThatAreNotMovingAtAll()
    {
        // Units with no orders, and ability casters, never leave their cell — the caller hands
        // those cells in up front so a route cannot be drawn to finish on top of one.
        HashSet<Vector2Int> claimed = new() { new Vector2Int(3, 1) };
        List<Vector2Int> route =
            new()
            {
                new Vector2Int(1, 1),
                new Vector2Int(2, 1),
                new Vector2Int(3, 1),
            };

        CollectionAssert.AreEqual(
            new[] { 2 },
            GameLoop.ResolveUniqueEndCellLengths(
                new List<IReadOnlyList<Vector2Int>> { route },
                claimed
            )
        );
    }

    [Test]
    public void EndCellTrim_ThreeRoutesOntoOneCellAllFinishSomewhereDifferent()
    {
        List<List<Vector2Int>> routes =
            new()
            {
                new List<Vector2Int>
                {
                    new(1, 1),
                    new(2, 1),
                    new(3, 1),
                },
                new List<Vector2Int>
                {
                    new(5, 1),
                    new(4, 1),
                    new(3, 1),
                },
                new List<Vector2Int>
                {
                    new(3, 3),
                    new(3, 2),
                    new(3, 1),
                },
            };

        List<int> lengths = GameLoop.ResolveUniqueEndCellLengths(
            routes.Select(route => (IReadOnlyList<Vector2Int>)route).ToList()
        );

        CollectionAssert.AreEqual(new[] { 3, 2, 2 }, lengths);
        List<Vector2Int> endCells = routes
            .Select((route, index) => route[lengths[index] - 1])
            .ToList();
        Assert.That(
            endCells.Distinct().Count(),
            Is.EqualTo(endCells.Count),
            "No two trimmed routes may still finish on the same cell."
        );
    }

    [Test]
    public void EndCellTrim_RouteWithNowhereLeftFallsBackToItsOwnStart()
    {
        // Every cell of this route is already spoken for. Collapsing to the start keeps the order
        // legal to execute; the execution-time overlap pass is what untangles the leftover stack.
        HashSet<Vector2Int> claimed = new() { new Vector2Int(1, 1), new Vector2Int(2, 1) };
        List<Vector2Int> route = new() { new Vector2Int(1, 1), new Vector2Int(2, 1) };

        CollectionAssert.AreEqual(
            new[] { 1 },
            GameLoop.ResolveUniqueEndCellLengths(
                new List<IReadOnlyList<Vector2Int>> { route },
                claimed
            ),
            "A route can always be cut back to the cell its unit is already standing on."
        );
    }

    [Test]
    public void EndCellTrim_MissingAndEmptyRoutesAreReportedWithoutClaimingAnything()
    {
        Assert.That(GameLoop.ResolveUniqueEndCellLengths(null), Is.Empty);

        HashSet<Vector2Int> claimed = new();
        CollectionAssert.AreEqual(
            new[] { 0, 0 },
            GameLoop.ResolveUniqueEndCellLengths(
                new List<IReadOnlyList<Vector2Int>> { null, new List<Vector2Int>() },
                claimed
            )
        );
        Assert.That(claimed, Is.Empty);
    }

    // === Planning: where a plan leaves its unit standing ===

    [Test]
    public void PlannedEndIndex_AbilityCasterHoldsItsStartCellWhileARouteHoldsItsLastStep()
    {
        Assert.That(
            PlanMovement.GetPlannedEndIndex(false, 4),
            Is.EqualTo(3),
            "A route finishes on its last step."
        );
        Assert.That(
            PlanMovement.GetPlannedEndIndex(false, 1),
            Is.EqualTo(0),
            "A one-cell route is a unit holding its ground."
        );
        Assert.That(
            PlanMovement.GetPlannedEndIndex(true, 2),
            Is.EqualTo(0),
            "An ability plan is [start, target] and its caster does not march anywhere."
        );
        Assert.That(PlanMovement.GetPlannedEndIndex(true, 1), Is.EqualTo(0));
        Assert.That(
            PlanMovement.GetPlannedEndIndex(false, 0),
            Is.EqualTo(-1),
            "An empty plan says nothing about where its unit ends up."
        );
        Assert.That(PlanMovement.GetPlannedEndIndex(true, 0), Is.EqualTo(-1));
    }

    // === Execution: which cell a displaced unit is set down on ===

    [Test]
    public void Displacement_TakesTheNearestFreeCellAndSettlesTiesRowMajor()
    {
        Vector2Int contested = new(7, 4);
        HashSet<Vector2Int> occupied = new() { contested };

        Assert.That(
            TryDisplace(contested, contested, occupied, out Vector2Int destination),
            Is.True
        );
        Assert.That(
            destination,
            Is.EqualTo(new Vector2Int(7, 3)),
            "All four neighbours are equally near, so the bottom-most, left-most one wins."
        );
    }

    [Test]
    public void Displacement_GivesGroundBackTowardWhereTheUnitCameFrom()
    {
        Vector2Int contested = new(7, 4);
        HashSet<Vector2Int> occupied = new() { contested };

        Assert.That(
            TryDisplace(contested, new Vector2Int(7, 6), occupied, out Vector2Int destination),
            Is.True
        );
        Assert.That(
            destination,
            Is.EqualTo(new Vector2Int(7, 5)),
            "A unit that walked down from (7,6) is pushed back up the way it arrived."
        );

        Assert.That(
            TryDisplace(contested, new Vector2Int(4, 4), occupied, out Vector2Int fromTheLeft),
            Is.True
        );
        Assert.That(
            fromTheLeft,
            Is.EqualTo(new Vector2Int(6, 4)),
            "Arriving from the left pushes back to the left."
        );
    }

    [Test]
    public void Displacement_WidensPastAFullRingOfBodiesToTheNextOne()
    {
        Vector2Int contested = new(7, 4);
        HashSet<Vector2Int> occupied =
            new()
            {
                contested,
                new Vector2Int(7, 5),
                new Vector2Int(8, 4),
                new Vector2Int(7, 3),
                new Vector2Int(6, 4),
            };

        Assert.That(
            TryDisplace(contested, contested, occupied, out Vector2Int destination),
            Is.True,
            "A ring of bodies is squeezed past, the way units already pass through each other."
        );
        Assert.That(destination, Is.EqualTo(new Vector2Int(6, 3)));
        Assert.That(
            GridSystem.GetGridDistance(contested, destination),
            Is.EqualTo(2),
            "The search only widens as far as it has to."
        );
    }

    [Test]
    public void Displacement_NeverLandsOnAWallEvenWhenOneIsTheNearestGap()
    {
        // (7,2) is a wall directly below the contested cell, and the three open neighbours are
        // taken, so the wall is the only unoccupied cell in the first ring.
        Vector2Int contested = new(7, 3);
        Assert.That(
            GameLoop.wallLayout,
            Does.Contain(new Vector2Int(7, 2)),
            "Fixture precondition: the cell below the contested one is a wall."
        );
        HashSet<Vector2Int> occupied =
            new()
            {
                contested,
                new Vector2Int(7, 4),
                new Vector2Int(8, 3),
                new Vector2Int(6, 3),
            };

        Assert.That(
            TryDisplace(contested, contested, occupied, out Vector2Int destination),
            Is.True
        );
        Assert.That(
            destination,
            Is.EqualTo(new Vector2Int(6, 2)),
            "The search steps around the wall instead of through it."
        );
        Assert.That(GameLoop.wallLayout.Contains(destination), Is.False);
    }

    [Test]
    public void Displacement_NeverLeavesTheBoard()
    {
        Vector2Int contested = new(0, 0);
        HashSet<Vector2Int> occupied =
            new()
            {
                contested,
                new Vector2Int(0, 1),
                new Vector2Int(1, 0),
            };

        Assert.That(
            TryDisplace(contested, contested, occupied, out Vector2Int destination),
            Is.True
        );
        Assert.That(
            destination,
            Is.EqualTo(new Vector2Int(2, 0)),
            "A corner has only two neighbours; the search widens inward rather than off the board."
        );
        Assert.That(GridSystem.IsCellInBounds(destination), Is.True);
    }

    [Test]
    public void Displacement_ReportsFailureRatherThanInventingACellWhenTheBoardIsFull()
    {
        Vector2Int contested = new(7, 4);
        HashSet<Vector2Int> everyCell = new();
        for (int column = 0; column < GridSystem.ColumnCount; column++)
        {
            for (int row = 0; row < GridSystem.RowCount; row++)
                everyCell.Add(new Vector2Int(column, row));
        }

        Assert.That(
            TryDisplace(contested, contested, everyCell, out Vector2Int destination),
            Is.False
        );
        Assert.That(
            destination,
            Is.EqualTo(contested),
            "A boxed-in unit is reported as staying put, not teleported somewhere arbitrary."
        );
    }

    [Test]
    public void Displacement_AlwaysProducesALegalCellFromAnywhereOnTheBoard()
    {
        // Sweep every playable cell with its whole first ring taken, which is the worst case the
        // round can hand the pass, and hold the result to the rules the shove must never break.
        int resolvedCount = 0;
        foreach (Vector2Int contested in PlayableCells())
        {
            HashSet<Vector2Int> occupied =
                new()
                {
                    contested,
                    contested + Vector2Int.up,
                    contested + Vector2Int.right,
                    contested + Vector2Int.down,
                    contested + Vector2Int.left,
                };
            if (!TryDisplace(contested, contested, occupied, out Vector2Int destination))
                continue;

            resolvedCount++;
            Assert.That(
                destination,
                Is.Not.EqualTo(contested),
                $"({contested.x},{contested.y}) was not actually vacated."
            );
            Assert.That(
                GridSystem.IsCellInBounds(destination),
                Is.True,
                $"({contested.x},{contested.y}) was displaced off the board."
            );
            Assert.That(
                GameLoop.wallLayout.Contains(destination),
                Is.False,
                $"({contested.x},{contested.y}) was displaced into a wall."
            );
            Assert.That(
                occupied.Contains(destination),
                Is.False,
                $"({contested.x},{contested.y}) was displaced onto a claimed cell."
            );
            Assert.That(
                GridSystem.GetGridDistance(contested, destination),
                Is.LessThanOrEqualTo(GameLoop.MaxDisplacementSteps),
                $"({contested.x},{contested.y}) was shoved further than the step budget."
            );
        }

        Assert.That(
            resolvedCount,
            Is.GreaterThan(100),
            "Almost every cell on this board has somewhere to give way to."
        );
    }

    [Test]
    public void Displacement_IsReproducibleSoEveryPeerAgrees()
    {
        foreach (Vector2Int contested in PlayableCells())
        {
            HashSet<Vector2Int> occupied = new() { contested, contested + Vector2Int.right };
            Vector2Int anchor = contested + Vector2Int.left;

            bool first = TryDisplace(contested, anchor, occupied, out Vector2Int firstCell);
            bool second = TryDisplace(contested, anchor, occupied, out Vector2Int secondCell);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(
                secondCell,
                Is.EqualTo(firstCell),
                $"({contested.x},{contested.y}) resolved differently on a repeat call."
            );
        }
    }

    [Test]
    public void DisplacementBudget_StaysShortEnoughToReadAsAShove()
    {
        Assert.That(GameLoop.MaxDisplacementSteps, Is.GreaterThan(0));
        Assert.That(
            GameLoop.MaxDisplacementSteps,
            Is.LessThanOrEqualTo(3),
            "A longer shove would move a unit further than its own orders did."
        );
    }

    private static bool TryDisplace(
        Vector2Int contested,
        Vector2Int anchor,
        ISet<Vector2Int> occupied,
        out Vector2Int destination
    )
    {
        return GridSystem.TryFindDisplacementCell(
            contested,
            anchor,
            occupied,
            GameLoop.MaxDisplacementSteps,
            out destination
        );
    }

    private static IEnumerable<Vector2Int> PlayableCells()
    {
        for (int column = 0; column < GridSystem.ColumnCount; column++)
        {
            for (int row = 0; row < GridSystem.RowCount; row++)
            {
                Vector2Int cell = new(column, row);
                if (!GameLoop.wallLayout.Contains(cell))
                    yield return cell;
            }
        }
    }
}
