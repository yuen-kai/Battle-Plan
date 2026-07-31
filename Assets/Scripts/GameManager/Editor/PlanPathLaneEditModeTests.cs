using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Guards the lane geometry that keeps several units' planned routes readable on one board. Each
/// case counts real segment intersections between the drawn polylines rather than inspecting lane
/// sides, so a regression that merely relabels lanes without untangling them still fails.
/// </summary>
[TestFixture]
[Category("PlanPathLanes")]
public class PlanPathLaneEditModeTests
{
    private const float Tolerance = 0.001f;

    private static Vector3 Cell(int column, int row)
    {
        return new Vector3(column * GameLoop.cellSize, 0f, row * GameLoop.cellSize);
    }

    private static List<Vector3> Draw(RouteLaneMap laneMap, int rosterSlot, Vector3[] route)
    {
        List<LanePlacement> arrival = new();
        List<LanePlacement> departure = new();
        for (int i = 0; i < route.Length; i++)
        {
            arrival.Add(
                i > 0
                    ? laneMap.GetPlacement(
                        rosterSlot,
                        route[i],
                        PlanPathStyle.GetAxis(route[i - 1], route[i])
                    )
                    : new LanePlacement(0f, 1)
            );
            departure.Add(
                i < route.Length - 1
                    ? laneMap.GetPlacement(
                        rosterSlot,
                        route[i],
                        PlanPathStyle.GetAxis(route[i], route[i + 1])
                    )
                    : new LanePlacement(0f, 1)
            );
        }

        List<Vector3> drawn = new();
        PlanPathStyle.BuildLanePolyline(
            route,
            arrival,
            departure,
            PlanPathStyle.RouteHeight,
            drawn
        );
        return drawn;
    }

    /// <summary>
    /// Segment intersections in the XZ plane. Both parameters are treated as half-open, so a
    /// crossing that happens to land on a vertex — which is exactly where one route's turn meets
    /// another's line — is counted once instead of being missed by both adjoining segments, while
    /// two routes that merely finish on the same point still count as nothing.
    /// </summary>
    private static int CountCrossings(List<Vector3> first, List<Vector3> second)
    {
        const float edge = 1e-5f;
        int crossings = 0;
        for (int i = 0; i + 1 < first.Count; i++)
        {
            for (int j = 0; j + 1 < second.Count; j++)
            {
                Vector2 a = new(first[i].x, first[i].z);
                Vector2 b = new(first[i + 1].x, first[i + 1].z);
                Vector2 c = new(second[j].x, second[j].z);
                Vector2 d = new(second[j + 1].x, second[j + 1].z);

                Vector2 ab = b - a;
                Vector2 cd = d - c;
                float denominator = ab.x * cd.y - ab.y * cd.x;
                if (Mathf.Abs(denominator) < 1e-9f)
                    continue;

                float t = ((c.x - a.x) * cd.y - (c.y - a.y) * cd.x) / denominator;
                float u = ((c.x - a.x) * ab.y - (c.y - a.y) * ab.x) / denominator;
                if (t >= -edge && t < 1f - edge && u >= -edge && u < 1f - edge)
                    crossings++;
            }
        }
        return crossings;
    }

    /// <summary>
    /// Total length over which two routes are drawn on top of each other. A crossing count alone
    /// cannot see this — two collinear segments never register an intersection — yet a route buried
    /// under another reads worse on screen than a clean crossing.
    /// </summary>
    private static float MeasureOverlap(List<Vector3> first, List<Vector3> second)
    {
        const float collinearTolerance = 0.01f;
        float total = 0f;
        for (int i = 0; i + 1 < first.Count; i++)
        {
            for (int j = 0; j + 1 < second.Count; j++)
            {
                Vector2 a = new(first[i].x, first[i].z);
                Vector2 b = new(first[i + 1].x, first[i + 1].z);
                Vector2 c = new(second[j].x, second[j].z);
                Vector2 d = new(second[j + 1].x, second[j + 1].z);

                Vector2 ab = b - a;
                Vector2 cd = d - c;
                if (ab.magnitude < 1e-5f || cd.magnitude < 1e-5f)
                    continue;
                if (Mathf.Abs(ab.x * cd.y - ab.y * cd.x) > 1e-6f)
                    continue;

                Vector2 along = ab.normalized;
                if (Mathf.Abs((c - a).x * along.y - (c - a).y * along.x) > collinearTolerance)
                    continue;

                float end = Vector2.Dot(ab, along);
                float otherStart = Vector2.Dot(c - a, along);
                float otherEnd = Vector2.Dot(d - a, along);
                if (otherStart > otherEnd)
                    (otherStart, otherEnd) = (otherEnd, otherStart);

                float low = Mathf.Max(0f, otherStart);
                float high = Mathf.Min(end, otherEnd);
                if (high - low > 1e-3f)
                    total += high - low;
            }
        }
        return total;
    }

    private static float MeasureOverlap(Vector3[][] routes, int[] rosterSlots)
    {
        RouteLaneMap laneMap = new();
        for (int i = 0; i < routes.Length; i++)
            laneMap.AddRoute(rosterSlots[i], routes[i]);

        List<Vector3>[] drawn = new List<Vector3>[routes.Length];
        for (int i = 0; i < routes.Length; i++)
            drawn[i] = Draw(laneMap, rosterSlots[i], routes[i]);

        float total = 0f;
        for (int i = 0; i < drawn.Length; i++)
        {
            for (int j = i + 1; j < drawn.Length; j++)
                total += MeasureOverlap(drawn[i], drawn[j]);
        }
        return total;
    }

    private static int CountCrossings(Vector3[][] routes, int[] rosterSlots)
    {
        RouteLaneMap laneMap = new();
        for (int i = 0; i < routes.Length; i++)
            laneMap.AddRoute(rosterSlots[i], routes[i]);

        List<Vector3>[] drawn = new List<Vector3>[routes.Length];
        for (int i = 0; i < routes.Length; i++)
            drawn[i] = Draw(laneMap, rosterSlots[i], routes[i]);

        int crossings = 0;
        for (int i = 0; i < drawn.Length; i++)
        {
            for (int j = i + 1; j < drawn.Length; j++)
                crossings += CountCrossings(drawn[i], drawn[j]);
        }
        return crossings;
    }

    /// <summary>
    /// Asserts the crossing count and that no two routes are drawn on top of each other, then
    /// repeats both with the roster slots reversed — lane order has to come from the geometry, not
    /// from which unit sits in which card.
    /// </summary>
    private static void AssertCrossings(int expected, Vector3[][] routes, params int[] rosterSlots)
    {
        int[] reversed = new int[rosterSlots.Length];
        for (int i = 0; i < rosterSlots.Length; i++)
            reversed[i] = rosterSlots[rosterSlots.Length - 1 - i];

        Assert.That(
            CountCrossings(routes, rosterSlots),
            Is.EqualTo(expected),
            "Unexpected number of crossings between planned routes."
        );
        Assert.That(
            MeasureOverlap(routes, rosterSlots),
            Is.EqualTo(0f).Within(Tolerance),
            "Routes must never be drawn on top of each other, even where a crossing is unavoidable."
        );

        Assert.That(
            CountCrossings(routes, reversed),
            Is.EqualTo(expected),
            "Lane assignment must not depend on roster slot order."
        );
        Assert.That(
            MeasureOverlap(routes, reversed),
            Is.EqualTo(0f).Within(Tolerance)
        );
    }

    [Test]
    public void LoneRoute_RunsThroughCellCentres()
    {
        Vector3[] route = { Cell(3, 0), Cell(3, 1), Cell(2, 1), Cell(1, 1) };
        RouteLaneMap laneMap = new();
        laneMap.AddRoute(2, route);

        foreach (Vector3 cell in route)
        {
            Assert.That(
                laneMap.GetLaneOffset(2, cell, PlanPathStyle.AxisX),
                Is.EqualTo(0f).Within(Tolerance)
            );
            Assert.That(
                laneMap.GetLaneOffset(2, cell, PlanPathStyle.AxisZ),
                Is.EqualTo(0f).Within(Tolerance)
            );
        }
    }

    [Test]
    public void CrossingRoutes_OnlyMakeRoomWhenTheyShareACorridor()
    {
        Vector3[] eastward = { Cell(1, 1), Cell(2, 1), Cell(3, 1) };
        Vector3[] northward = { Cell(2, 0), Cell(2, 1), Cell(2, 2) };
        RouteLaneMap laneMap = new();
        laneMap.AddRoute(0, eastward);
        laneMap.AddRoute(3, northward);

        // They meet at (2,1) but travel on different axes, so neither is pushed off centre.
        Assert.That(
            laneMap.GetLaneOffset(0, Cell(2, 1), PlanPathStyle.AxisX),
            Is.EqualTo(0f).Within(Tolerance)
        );
        Assert.That(
            laneMap.GetLaneOffset(3, Cell(2, 1), PlanPathStyle.AxisZ),
            Is.EqualTo(0f).Within(Tolerance)
        );
    }

    [Test]
    public void RunsThatOnlyTouchAtOneCell_DoNotGiveWayToEachOther()
    {
        // Both approach cell (2,0) along row 0 from opposite ends and then both turn north, so
        // their row-0 runs cover disjoint stretches and meet at a single point. Neither should step
        // aside on the approach for a pass that never happens.
        Vector3[] first =
        {
            Cell(0, 0),
            Cell(1, 0),
            Cell(2, 0),
            Cell(2, 1),
            Cell(2, 2),
            Cell(1, 2),
            Cell(0, 2),
        };
        Vector3[] second =
        {
            Cell(4, 0),
            Cell(3, 0),
            Cell(2, 0),
            Cell(2, 1),
            Cell(2, 2),
            Cell(3, 2),
            Cell(4, 2),
        };

        RouteLaneMap laneMap = new();
        laneMap.AddRoute(2, first);
        laneMap.AddRoute(3, second);

        foreach (Vector3 cell in new[] { Cell(0, 0), Cell(1, 0), Cell(2, 0) })
        {
            Assert.That(
                laneMap.GetLaneOffset(2, cell, PlanPathStyle.AxisX),
                Is.EqualTo(0f).Within(Tolerance),
                "A run that only touches another at its final cell must hold the centre."
            );
        }
        foreach (Vector3 cell in new[] { Cell(4, 0), Cell(3, 0), Cell(2, 0) })
        {
            Assert.That(
                laneMap.GetLaneOffset(3, cell, PlanPathStyle.AxisX),
                Is.EqualTo(0f).Within(Tolerance)
            );
        }

        // The column they both climb is genuinely shared, so there they must still separate.
        float gap = Mathf.Abs(
            laneMap.GetLaneOffset(2, Cell(2, 1), PlanPathStyle.AxisZ)
                - laneMap.GetLaneOffset(3, Cell(2, 1), PlanPathStyle.AxisZ)
        );
        Assert.That(gap, Is.EqualTo(PlanPathStyle.LaneSpacing).Within(Tolerance));
    }

    [Test]
    public void PartiallyOverlappingRuns_OnlyGiveWayWhereTheyActuallyOverlap()
    {
        Vector3[] first = { Cell(0, 5), Cell(1, 5), Cell(2, 5), Cell(3, 5) };
        Vector3[] second = { Cell(2, 5), Cell(3, 5), Cell(4, 5), Cell(5, 5) };

        RouteLaneMap laneMap = new();
        laneMap.AddRoute(0, first);
        laneMap.AddRoute(3, second);

        Assert.That(
            laneMap.GetLaneOffset(0, Cell(1, 5), PlanPathStyle.AxisX),
            Is.EqualTo(0f).Within(Tolerance),
            "Cells before the overlap keep the centre."
        );
        Assert.That(
            laneMap.GetLaneOffset(3, Cell(4, 5), PlanPathStyle.AxisX),
            Is.EqualTo(0f).Within(Tolerance),
            "Cells after the overlap reclaim the centre."
        );
        float gap = Mathf.Abs(
            laneMap.GetLaneOffset(0, Cell(2, 5), PlanPathStyle.AxisX)
                - laneMap.GetLaneOffset(3, Cell(2, 5), PlanPathStyle.AxisX)
        );
        Assert.That(gap, Is.EqualTo(PlanPathStyle.LaneSpacing).Within(Tolerance));
    }

    [Test]
    public void TJunction_UsesTwoLanesRatherThanOnePerRoute()
    {
        // Two routes arrive at the junction from opposite ends and both turn off; a third runs
        // straight through. The two arms cover disjoint stretches of the row, so they can share a
        // lane and the junction needs two lanes rather than three.
        Vector3[] fromWest = { Cell(1, 0), Cell(2, 0), Cell(3, 0), Cell(3, 1), Cell(3, 2) };
        Vector3[] fromEast = { Cell(5, 0), Cell(4, 0), Cell(3, 0), Cell(3, 1), Cell(3, 2) };
        Vector3[] straight =
        {
            Cell(7, 0),
            Cell(6, 0),
            Cell(5, 0),
            Cell(4, 0),
            Cell(3, 0),
            Cell(2, 0),
        };

        RouteLaneMap laneMap = new();
        laneMap.AddRoute(0, fromWest);
        laneMap.AddRoute(1, fromEast);
        laneMap.AddRoute(2, straight);

        Assert.That(
            laneMap.TryGetLane(0, Cell(3, 0), PlanPathStyle.AxisX, out _, out int laneCount),
            Is.True
        );
        Assert.That(
            laneCount,
            Is.EqualTo(2),
            "Routes covering disjoint stretches of a junction must share a lane."
        );

        // The column they both climb is genuinely shared, so there they still separate.
        float gap = Mathf.Abs(
            laneMap.GetLaneOffset(0, Cell(3, 1), PlanPathStyle.AxisZ)
                - laneMap.GetLaneOffset(1, Cell(3, 1), PlanPathStyle.AxisZ)
        );
        Assert.That(gap, Is.EqualTo(PlanPathStyle.LaneSpacing).Within(Tolerance));
    }

    [Test]
    public void RoutesSharingAWholeCorridor_StaySeparatedEvenAtItsEnds()
    {
        // The control for the case above. At either end of a fully shared corridor both routes
        // merely terminate, so any structural "do they both stop here" rule would fold them into
        // one lane and draw them as a single line. Their extents overlap, so they must not.
        Vector3[] first = { Cell(0, 3), Cell(1, 3), Cell(2, 3), Cell(3, 3) };
        Vector3[] second = { Cell(0, 3), Cell(1, 3), Cell(2, 3), Cell(3, 3) };

        RouteLaneMap laneMap = new();
        laneMap.AddRoute(0, first);
        laneMap.AddRoute(3, second);

        foreach (Vector3 cell in new[] { Cell(0, 3), Cell(1, 3), Cell(3, 3) })
        {
            float gap = Mathf.Abs(
                laneMap.GetLaneOffset(0, cell, PlanPathStyle.AxisX)
                    - laneMap.GetLaneOffset(3, cell, PlanPathStyle.AxisX)
            );
            Assert.That(
                gap,
                Is.EqualTo(PlanPathStyle.LaneSpacing).Within(Tolerance),
                "Routes running the whole corridor together must never share a lane."
            );
        }
    }

    [Test]
    public void SharedCorridor_SeparatesByExactlyOneLane()
    {
        Vector3[] first = { Cell(1, 1), Cell(2, 1), Cell(3, 1) };
        Vector3[] second = { Cell(1, 1), Cell(2, 1), Cell(3, 1) };
        RouteLaneMap laneMap = new();
        laneMap.AddRoute(0, first);
        laneMap.AddRoute(3, second);

        float gap = Mathf.Abs(
            laneMap.GetLaneOffset(0, Cell(2, 1), PlanPathStyle.AxisX)
                - laneMap.GetLaneOffset(3, Cell(2, 1), PlanPathStyle.AxisX)
        );
        Assert.That(gap, Is.EqualTo(PlanPathStyle.LaneSpacing).Within(Tolerance));
        Assert.That(
            PlanPathStyle.LaneSpacing,
            Is.GreaterThan(PlanPathStyle.GetRouteWidth(true)),
            "Lanes must stay further apart than a selected route is wide."
        );
    }

    [Test]
    public void HeadOnRoutes_KeepToTheirOwnSide()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(1, 0), Cell(2, 0), Cell(3, 0) },
                new[] { Cell(3, 0), Cell(2, 0), Cell(1, 0) },
            },
            0,
            3
        );
    }

    [Test]
    public void HeadOnRoutes_StayApartWhenOnePeelsOff()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(1, 1), Cell(2, 1), Cell(3, 1), Cell(4, 1) },
                new[] { Cell(4, 1), Cell(3, 1), Cell(2, 1), Cell(2, 2) },
            },
            0,
            3
        );
    }

    [Test]
    public void StaggeredMerges_PutTheLateJoinerNearestTheSideItJoinsFrom()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(4, 0), Cell(4, 1), Cell(3, 1), Cell(2, 1), Cell(1, 1) },
                new[] { Cell(2, 0), Cell(2, 1), Cell(1, 1), Cell(0, 1) },
            },
            0,
            3
        );
    }

    [Test]
    public void LateJoiner_MayStillLeaveByTheOppositeSide()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(5, 0), Cell(5, 1), Cell(4, 1), Cell(3, 1), Cell(2, 1) },
                new[]
                {
                    Cell(3, 0),
                    Cell(3, 1),
                    Cell(2, 1),
                    Cell(1, 1),
                    Cell(0, 1),
                    Cell(0, 2),
                },
            },
            1,
            0
        );
    }

    [Test]
    public void RoutesJoiningFromOppositeSides_TakeTheSideTheyCameFrom()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(3, 0), Cell(3, 1), Cell(2, 1), Cell(1, 1) },
                new[] { Cell(3, 2), Cell(3, 1), Cell(2, 1), Cell(1, 1) },
            },
            0,
            4
        );
    }

    [Test]
    public void PeelingOffNorthOrSouth_UsesThatSidesLane()
    {
        Vector3[] straight =
        {
            Cell(0, 1),
            Cell(1, 1),
            Cell(2, 1),
            Cell(3, 1),
            Cell(4, 1),
            Cell(5, 1),
        };

        AssertCrossings(
            0,
            new[] { straight, new[] { Cell(1, 1), Cell(2, 1), Cell(3, 1), Cell(3, 2) } },
            2,
            3
        );
        AssertCrossings(
            0,
            new[] { straight, new[] { Cell(1, 1), Cell(2, 1), Cell(3, 1), Cell(3, 0) } },
            2,
            3
        );
    }

    [Test]
    public void RoutesLeavingToOppositeSides_DoNotSwapAcrossEachOther()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(1, 1), Cell(2, 1), Cell(3, 1), Cell(3, 2) },
                new[] { Cell(0, 1), Cell(1, 1), Cell(2, 1), Cell(3, 1), Cell(3, 0) },
            },
            0,
            1
        );
    }

    [Test]
    public void RoutesRoundingTheSameCorner_StayConcentric()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(1, 1), Cell(2, 1), Cell(3, 1), Cell(3, 2), Cell(3, 3) },
                new[]
                {
                    Cell(0, 1),
                    Cell(1, 1),
                    Cell(2, 1),
                    Cell(3, 1),
                    Cell(3, 2),
                    Cell(3, 3),
                },
            },
            1,
            4
        );
    }

    [Test]
    public void RoutesConvergingThenRoundingTwoCornersTogether_HoldTheirOrder()
    {
        // They approach one corner from opposite directions, then make the same two turns. Their
        // order is settled on the northward run and has to survive the second turn intact.
        AssertCrossings(
            0,
            new[]
            {
                new[]
                {
                    Cell(0, 0),
                    Cell(1, 0),
                    Cell(2, 0),
                    Cell(2, 1),
                    Cell(2, 2),
                    Cell(1, 2),
                    Cell(0, 2),
                },
                new[]
                {
                    Cell(4, 0),
                    Cell(3, 0),
                    Cell(2, 0),
                    Cell(2, 1),
                    Cell(2, 2),
                    Cell(1, 2),
                    Cell(0, 2),
                },
            },
            2,
            3
        );
    }

    [Test]
    public void RoutesConvergingThenTurningTheOtherWay_HoldTheirOrder()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[]
                {
                    Cell(0, 0),
                    Cell(1, 0),
                    Cell(2, 0),
                    Cell(2, 1),
                    Cell(2, 2),
                    Cell(3, 2),
                    Cell(4, 2),
                },
                new[]
                {
                    Cell(4, 0),
                    Cell(3, 0),
                    Cell(2, 0),
                    Cell(2, 1),
                    Cell(2, 2),
                    Cell(3, 2),
                    Cell(4, 2),
                },
            },
            2,
            3
        );
    }

    [Test]
    public void StaggeredMergesFromTheNorth_PutTheLateJoinerNearestTheNorth()
    {
        // Mirror of the merge-from-the-south case. Whoever joins first must sit furthest from the
        // side they all came from, which runs the opposite way round the ordering when that side is
        // the north, so the late joiner can drop straight into the near lane without cutting across.
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(5, 1), Cell(4, 1), Cell(3, 1), Cell(2, 1), Cell(1, 1) },
                new[] { Cell(3, 3), Cell(3, 2), Cell(3, 1), Cell(2, 1), Cell(1, 1), Cell(0, 1) },
                new[] { Cell(2, 4), Cell(2, 3), Cell(2, 2), Cell(2, 1), Cell(1, 1), Cell(0, 1) },
            },
            0,
            1,
            2
        );
    }

    [Test]
    public void TwoJoiningOneRowFromTheNorth_DoNotCross()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(2, 3), Cell(2, 2), Cell(2, 1), Cell(2, 0), Cell(1, 0), Cell(0, 0) },
                new[] { Cell(3, 2), Cell(3, 1), Cell(3, 0), Cell(2, 0), Cell(1, 0), Cell(0, 0) },
            },
            1,
            2
        );
    }

    [Test]
    public void RouteJoiningWhereAnotherFinishes_IsFreeToTakeTheLaneItLeavesBy()
    {
        // Three share one row. The first joins at the very cell where the third finishes, and later
        // turns off north. Because a finished route has no line carrying on, the joiner is free to
        // take the north lane straight away instead of being pinned south and cutting back across.
        // A brute force over every fixed lane order confirms zero is achievable here.
        AssertCrossings(
            0,
            new[]
            {
                new[]
                {
                    Cell(0, 0),
                    Cell(0, 1),
                    Cell(0, 2),
                    Cell(1, 2),
                    Cell(2, 2),
                    Cell(3, 2),
                    Cell(3, 3),
                    Cell(3, 4),
                },
                new[] { Cell(1, 0), Cell(1, 1), Cell(1, 2), Cell(2, 2), Cell(3, 2) },
                new[]
                {
                    Cell(4, 0),
                    Cell(4, 1),
                    Cell(4, 2),
                    Cell(3, 2),
                    Cell(2, 2),
                    Cell(1, 2),
                    Cell(0, 2),
                },
            },
            0,
            1,
            2
        );
    }

    [Test]
    public void RoutesJoiningAColumnAtDifferentHeights_SplitCleanlyAtTheTop()
    {
        // Both run east in separate rows, climb the same column, then leave in opposite directions.
        // The lower unit joins the column where the other is not yet present, so its uncontested
        // entry must not outrank the other's contested need for that same lane.
        AssertCrossings(
            0,
            new[]
            {
                new[]
                {
                    Cell(0, 2),
                    Cell(1, 2),
                    Cell(2, 2),
                    Cell(2, 3),
                    Cell(2, 4),
                    Cell(1, 4),
                    Cell(0, 4),
                },
                new[]
                {
                    Cell(0, 0),
                    Cell(1, 0),
                    Cell(2, 0),
                    Cell(2, 1),
                    Cell(2, 2),
                    Cell(2, 3),
                    Cell(2, 4),
                    Cell(3, 4),
                    Cell(4, 4),
                },
            },
            0,
            3
        );
    }

    [Test]
    public void RoutesConvergingOnAColumn_SplitCleanlyAtTheTop()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[]
                {
                    Cell(0, 0),
                    Cell(1, 0),
                    Cell(2, 0),
                    Cell(2, 1),
                    Cell(2, 2),
                    Cell(1, 2),
                    Cell(0, 2),
                },
                new[]
                {
                    Cell(4, 0),
                    Cell(3, 0),
                    Cell(2, 0),
                    Cell(2, 1),
                    Cell(2, 2),
                    Cell(3, 2),
                    Cell(4, 2),
                },
            },
            2,
            3
        );
    }

    [Test]
    public void RouteWhoseEntryAndExitWantOppositeLanes_CrossesExactlyOnce()
    {
        // The first route joins the column from the west with the other alongside, then leaves east
        // with it still alongside. It has to change sides while sharing, so one crossing is the
        // minimum rather than a defect.
        AssertCrossings(
            1,
            new[]
            {
                new[]
                {
                    Cell(0, 2),
                    Cell(1, 2),
                    Cell(2, 2),
                    Cell(2, 3),
                    Cell(2, 4),
                    Cell(3, 4),
                    Cell(4, 4),
                },
                new[]
                {
                    Cell(0, 0),
                    Cell(1, 0),
                    Cell(2, 0),
                    Cell(2, 1),
                    Cell(2, 2),
                    Cell(2, 3),
                    Cell(2, 4),
                    Cell(1, 4),
                    Cell(0, 4),
                },
            },
            0,
            3
        );
    }

    [Test]
    public void RouteEnteringAndLeavingOnOneSide_HugsThatSide()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(3, 0), Cell(3, 1), Cell(2, 1), Cell(1, 1), Cell(1, 0) },
                new[] { Cell(4, 1), Cell(3, 1), Cell(2, 1), Cell(1, 1), Cell(0, 1) },
            },
            0,
            3
        );
    }

    [Test]
    public void RoutesSharingADestinationCell_DoNotTangle()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(0, 2), Cell(1, 2), Cell(2, 2) },
                new[] { Cell(2, 0), Cell(2, 1), Cell(2, 2) },
            },
            1,
            2
        );
    }

    [Test]
    public void RouteStoppingMidCorridor_LeavesTheOtherUntangled()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[] { Cell(5, 0), Cell(5, 1), Cell(4, 1), Cell(3, 1), Cell(2, 1), Cell(1, 1) },
                new[] { Cell(4, 0), Cell(4, 1), Cell(3, 1) },
            },
            0,
            3
        );
    }

    [Test]
    public void WeavingRoute_CrossesAStraightRunOnlyOnce()
    {
        // The weaver joins the row from the north and leaves it to the south with the straight run
        // alongside at both ends, so it has to change sides while sharing. One crossing is the
        // minimum, and it lands exactly on a vertex — which an interior-only intersection test
        // would quietly miss.
        AssertCrossings(
            1,
            new[]
            {
                new[]
                {
                    Cell(0, 1),
                    Cell(1, 1),
                    Cell(2, 1),
                    Cell(3, 1),
                    Cell(4, 1),
                    Cell(5, 1),
                },
                new[]
                {
                    Cell(0, 0),
                    Cell(0, 1),
                    Cell(1, 1),
                    Cell(2, 1),
                    Cell(2, 2),
                    Cell(3, 2),
                    Cell(4, 2),
                },
            },
            2,
            4
        );
    }

    [Test]
    public void FullRosterSharingOneCorridor_StaysUntangled()
    {
        AssertCrossings(
            0,
            new[]
            {
                new[]
                {
                    Cell(7, 0),
                    Cell(7, 1),
                    Cell(6, 1),
                    Cell(5, 1),
                    Cell(4, 1),
                    Cell(3, 1),
                    Cell(2, 1),
                },
                new[]
                {
                    Cell(6, 0),
                    Cell(6, 1),
                    Cell(5, 1),
                    Cell(4, 1),
                    Cell(3, 1),
                    Cell(2, 1),
                },
                new[] { Cell(5, 0), Cell(5, 1), Cell(4, 1), Cell(3, 1), Cell(2, 1) },
                new[] { Cell(4, 0), Cell(4, 1), Cell(3, 1), Cell(2, 1) },
                new[] { Cell(3, 0), Cell(3, 1), Cell(2, 1) },
            },
            0,
            1,
            2,
            3,
            4
        );
    }

    [Test]
    public void RoutesThatMustSwapSides_CrossExactlyOnce()
    {
        // One enters the corridor from the south and leaves north while the other does the
        // reverse, so their order across the corridor has to invert. A single crossing is the
        // mathematical minimum here, not a defect.
        AssertCrossings(
            1,
            new[]
            {
                new[] { Cell(0, 0), Cell(0, 1), Cell(1, 1), Cell(2, 1), Cell(3, 1), Cell(3, 2) },
                new[] { Cell(0, 2), Cell(0, 1), Cell(1, 1), Cell(2, 1), Cell(3, 1), Cell(3, 0) },
            },
            0,
            4
        );
    }
}
