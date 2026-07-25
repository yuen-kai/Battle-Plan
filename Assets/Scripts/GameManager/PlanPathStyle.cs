using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where one route sits across a corridor at one cell, and how many routes share it there. The
/// count matters as much as the offset: a lane change belongs against the busier end of a step,
/// which is where the route that caused it actually joins or leaves.
/// </summary>
public readonly struct LanePlacement
{
    public readonly float Offset;
    public readonly int Crowd;

    public LanePlacement(float offset, int crowd)
    {
        Offset = offset;
        Crowd = Mathf.Max(1, crowd);
    }
}

/// <summary>
/// Shared styling for planned routes: one identity colour per roster slot, and the lane geometry
/// that keeps several units' routes readable where they run through the same cells.
/// </summary>
public static class PlanPathStyle
{
    // Five separable hues indexed by roster slot, so a route on the board can be traced back to
    // its unit card without reading any labels.
    private static readonly Color[] SlotColors =
    {
        new(1f, 0.76f, 0.24f),
        new(0.32f, 0.80f, 1f),
        new(1f, 0.44f, 0.70f),
        new(0.52f, 0.93f, 0.44f),
        new(0.72f, 0.62f, 1f),
    };

    private const float LaneSpacingCells = 0.155f;
    private const float SelectedWidthCells = 0.11f;
    private const float UnselectedWidthCells = 0.07f;
    private const float UnselectedAlpha = 0.72f;

    // The end cap is a chevron at the route's last drawn point. Kept narrower than one lane so two
    // units finishing on the same cell get separate markers instead of one crossing the other's.
    private const float DestinationLengthCells = 0.115f;
    private const float DestinationHalfWidthCells = 0.065f;

    // How much board a lane change covers, how finely the S is sampled across it, and how far it
    // is held clear of a cell centre so it never lands on top of a route vertex.
    private const float LaneTransitionCells = 0.22f;
    private const int LaneTransitionSteps = 6;
    private const float LaneEdgeMargin = 0.03f;

    public const int AxisX = 0;
    public const int AxisZ = 1;

    // The move-range overlay stacks an opaque outline quad at world y=0.227 under a transparent
    // tile. Routes need real clearance above that outline, not the few millimetres that let the
    // board cut into them.
    public const float RouteHeight = 0.35f;

    /// <summary>
    /// Height for the path an ability travels. Above the target markers at 0.26 so an arrowhead
    /// reads over the cell it stops on, and below <see cref="RouteHeight"/> so a planned route
    /// crossing one still comes out on top of it.
    /// </summary>
    public const float AbilityPathHeight = 0.3f;

    /// <summary>Width at the route's start relative to its end, giving a direction-of-travel cue.</summary>
    public const float StartWidthScale = 0.72f;

    /// <summary>Draws after the transparent board overlays, which all sit at queue 3000 or below.</summary>
    public const int RouteRenderQueue = 3100;

    /// <summary>Sideways gap between two routes forced to share a cell.</summary>
    public static float LaneSpacing => LaneSpacingCells * GameLoop.cellSize;

    public static float DestinationLength => DestinationLengthCells * GameLoop.cellSize;

    public static float DestinationHalfWidth => DestinationHalfWidthCells * GameLoop.cellSize;

    public static Color GetSlotColor(int rosterSlot)
    {
        if (SlotColors.Length == 0)
            return Color.white;

        int index = ((rosterSlot % SlotColors.Length) + SlotColors.Length) % SlotColors.Length;
        return SlotColors[index];
    }

    public static Color GetRouteColor(int rosterSlot, bool selected)
    {
        Color color = GetSlotColor(rosterSlot);
        color.a = selected ? 1f : UnselectedAlpha;
        return color;
    }

    public static float GetRouteWidth(bool selected)
    {
        return (selected ? SelectedWidthCells : UnselectedWidthCells) * GameLoop.cellSize;
    }

    /// <summary>
    /// Colour for the end of a route that is resting somewhere it cannot stop. Deliberately not one
    /// of the slot hues: a destination that will not hold has to read as wrong at a glance rather
    /// than as merely belonging to a different unit.
    /// </summary>
    public static Color GetBlockedEndColor(bool selected)
    {
        return new Color(1f, 0.29f, 0.31f, selected ? 1f : UnselectedAlpha);
    }

    /// <summary>
    /// Fills <paramref name="chevron"/> with the three points of the arrowhead that caps a path at
    /// <paramref name="tip"/>, pointing the way it arrives. Shared so a walked route and an
    /// ability's own path finish in the same shape. False when the approach names no direction to
    /// point along, which leaves the caller nothing to draw.
    /// </summary>
    public static bool TryBuildEndChevron(Vector3 tip, Vector3 approach, Vector3[] chevron)
    {
        if (chevron == null || chevron.Length < 3)
            return false;

        approach.y = 0f;
        if (approach.sqrMagnitude <= Mathf.Epsilon)
            return false;

        approach.Normalize();
        Vector3 halfWidth = new Vector3(approach.z, 0f, -approach.x) * DestinationHalfWidth;
        Vector3 back = tip - approach * DestinationLength;

        chevron[0] = back + halfWidth;
        chevron[1] = tip;
        chevron[2] = back - halfWidth;
        return true;
    }

    /// <summary>
    /// Which board axis a step runs along. Lanes are grouped per axis, so two routes only make room
    /// for each other when they genuinely share a corridor rather than merely crossing.
    /// </summary>
    public static int GetAxis(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        return Mathf.Abs(direction.x) >= Mathf.Abs(direction.z) ? AxisX : AxisZ;
    }

    /// <summary>
    /// Whether a step runs with or against the board's reference direction. Lane order is reversed
    /// for units travelling against it, which is what keeps two units walking the same corridor in
    /// opposite directions on their own side rather than resolving both onto the same spot.
    /// </summary>
    public static int GetDirectionSign(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        return direction.x + direction.z >= 0f ? 1 : -1;
    }

    /// <summary>
    /// The side a run turns off to at its end, or zero if it simply stops. A route already sitting
    /// in that side's lane can leave without cutting across the corridor.
    /// </summary>
    public static int GetExitBias(IReadOnlyList<Vector3> cells, int stepIndex)
    {
        int axis = GetAxis(cells[stepIndex], cells[stepIndex + 1]);
        for (int step = stepIndex; step < cells.Count - 1; step++)
        {
            if (GetAxis(cells[step], cells[step + 1]) == axis)
                continue;

            // The run ends at cells[step]; report the side it turns off toward.
            return LateralSign(cells[step] - cells[step - 1], cells[step + 1] - cells[step]);
        }
        return 0;
    }

    /// <summary>
    /// The side a run was joined from, or zero if the route simply began there. This outranks the
    /// exit side when they disagree: a route joining a corridor has to cross everyone already in it
    /// to reach any other lane, whereas its exit only conflicts with whoever is still alongside by
    /// the time it leaves.
    /// </summary>
    public static int GetEntryBias(IReadOnlyList<Vector3> cells, int stepIndex)
    {
        int axis = GetAxis(cells[stepIndex], cells[stepIndex + 1]);
        for (int step = stepIndex; step > 0; step--)
        {
            if (GetAxis(cells[step - 1], cells[step]) == axis)
                continue;

            // The run begins at cells[step]; the side it came from is opposite its approach.
            return -LateralSign(cells[step + 1] - cells[step], cells[step] - cells[step - 1]);
        }
        return 0;
    }

    /// <summary>
    /// The side the run before this one was joined from. Because the perpendicular is always the
    /// right-hand side of travel, a lane's side survives a turn unchanged — left stays left. So
    /// when two routes round the same corner together and agree on everything about the new
    /// corridor, this keeps them in the order they already held, instead of letting them swap
    /// across each other mid-turn.
    /// </summary>
    public static int GetPreviousEntryBias(IReadOnlyList<Vector3> cells, int stepIndex)
    {
        int start = GetRunStartIndex(cells, stepIndex);
        return start > 0 ? GetEntryBias(cells, start - 1) : 0;
    }

    /// <summary>Index of the cell where this step's run begins.</summary>
    public static int GetRunStartIndex(IReadOnlyList<Vector3> cells, int stepIndex)
    {
        int axis = GetAxis(cells[stepIndex], cells[stepIndex + 1]);
        int start = stepIndex;
        while (start > 0 && GetAxis(cells[start - 1], cells[start]) == axis)
            start--;
        return start;
    }

    /// <summary>
    /// How far a run's opening corner sits along its own travel direction, relative to the cell it
    /// turns in. Negative means the turn pushes the corner backward into that cell, which is what
    /// lets two runs leaving one cell in opposite directions overlap; positive means the corner has
    /// already cleared the cell and nothing needs to give way.
    /// </summary>
    public static float GetRunStartShift(
        IReadOnlyList<Vector3> cells,
        int stepIndex,
        float incomingOffset
    )
    {
        if (stepIndex <= 0 || stepIndex >= cells.Count - 1)
            return 0f;

        Vector3 incomingPerpendicular = Perpendicular(cells[stepIndex - 1], cells[stepIndex]);
        Vector3 travel = (cells[stepIndex + 1] - cells[stepIndex]).normalized;
        return Vector3.Dot(incomingPerpendicular, travel) * incomingOffset;
    }

    /// <summary>
    /// How far a run's closing corner reaches along its own travel direction, past the cell it
    /// turns in. Positive means the turn carries the run beyond that cell, which is what lets it
    /// reach into a stretch it holds no lane for.
    /// </summary>
    public static float GetRunEndShift(
        IReadOnlyList<Vector3> cells,
        int cellIndex,
        float outgoingOffset
    )
    {
        if (cellIndex <= 0 || cellIndex >= cells.Count - 1)
            return 0f;

        Vector3 outgoingPerpendicular = Perpendicular(cells[cellIndex], cells[cellIndex + 1]);
        Vector3 travel = (cells[cellIndex] - cells[cellIndex - 1]).normalized;
        return Vector3.Dot(outgoingPerpendicular, travel) * outgoingOffset;
    }

    /// <summary>Index of the cell where this step's run ends.</summary>
    public static int GetRunEndIndex(IReadOnlyList<Vector3> cells, int stepIndex)
    {
        int axis = GetAxis(cells[stepIndex], cells[stepIndex + 1]);
        int end = stepIndex + 1;
        while (end < cells.Count - 1 && GetAxis(cells[end], cells[end + 1]) == axis)
            end++;
        return end;
    }

    /// <summary>
    /// How far along its own travel direction a run had already started by the time it reaches this
    /// step. When several routes merge into one corridor from the same side, the one that joined
    /// further upstream sorts first and takes the far lane, leaving the near lane for whoever joins
    /// later — otherwise the late joiner has to cut across everyone already in the corridor.
    /// </summary>
    public static float GetRunEntryProgress(IReadOnlyList<Vector3> cells, int stepIndex)
    {
        Vector3 travel = (cells[stepIndex + 1] - cells[stepIndex]).normalized;
        return Vector3.Dot(cells[GetRunStartIndex(cells, stepIndex)], travel);
    }

    private static int LateralSign(Vector3 runDirection, Vector3 otherDirection)
    {
        Vector3 perpendicular = new(runDirection.z, 0f, -runDirection.x);
        float side = Vector3.Dot(otherDirection, perpendicular);
        if (Mathf.Abs(side) <= Mathf.Epsilon)
            return 0;
        return side > 0f ? 1 : -1;
    }

    /// <summary>
    /// Builds the drawn polyline. Each route point carries two lane offsets: the one for the run
    /// arriving at it and the one for the run leaving it. They differ only at a turn, where the two
    /// runs are measured on perpendicular axes and the corner simply joins them.
    /// <para>
    /// <paramref name="vertexIndices"/>, when supplied, receives the index in
    /// <paramref name="results"/> of each original route point, since lane changes add points in
    /// between.
    /// </para>
    /// </summary>
    public static void BuildLanePolyline(
        IReadOnlyList<Vector3> route,
        IReadOnlyList<LanePlacement> arrival,
        IReadOnlyList<LanePlacement> departure,
        float height,
        List<Vector3> results,
        List<int> vertexIndices = null
    )
    {
        results.Clear();
        vertexIndices?.Clear();
        if (route == null || route.Count < 2)
            return;

        for (int i = 0; i < route.Count; i++)
        {
            vertexIndices?.Add(results.Count);
            results.Add(Flatten(GetVertexPoint(route, arrival, departure, i), height));

            if (i >= route.Count - 1)
                continue;

            // Both ends of a step share that step's axis, so their lanes are directly comparable.
            LanePlacement leaving = GetPlacement(departure, i);
            LanePlacement arriving = GetPlacement(arrival, i + 1);
            if (!Mathf.Approximately(leaving.Offset, arriving.Offset))
                AppendLaneChange(route[i], route[i + 1], leaving, arriving, height, results);
        }
    }

    private static Vector3 GetVertexPoint(
        IReadOnlyList<Vector3> route,
        IReadOnlyList<LanePlacement> arrival,
        IReadOnlyList<LanePlacement> departure,
        int index
    )
    {
        Vector3 incoming =
            index > 0 ? Perpendicular(route[index - 1], route[index]) : Vector3.zero;
        Vector3 outgoing =
            index < route.Count - 1 ? Perpendicular(route[index], route[index + 1]) : Vector3.zero;

        if (index == 0)
            return route[index] + outgoing * GetPlacement(departure, index).Offset;
        if (index == route.Count - 1)
            return route[index] + incoming * GetPlacement(arrival, index).Offset;

        // Straight through: one lane, and any change to it is handled by the S on a neighbouring
        // step rather than here.
        if (Vector3.Dot(incoming, outgoing) > 0.99f)
            return route[index] + outgoing * GetPlacement(departure, index).Offset;

        // A turn joins two lanes held on perpendicular axes. Satisfying both offsets at once lands
        // exactly on the intersection of the two offset lines, which is the correct mitre.
        return route[index]
            + incoming * GetPlacement(arrival, index).Offset
            + outgoing * GetPlacement(departure, index).Offset;
    }

    private static void AppendLaneChange(
        Vector3 from,
        Vector3 to,
        LanePlacement fromLane,
        LanePlacement toLane,
        float height,
        List<Vector3> results
    )
    {
        Vector3 perpendicular = Perpendicular(from, to);
        float length = Vector3.Distance(from, to);
        if (perpendicular == Vector3.zero || length <= Mathf.Epsilon)
            return;

        float span = Mathf.Min(
            1f - 2f * LaneEdgeMargin,
            LaneTransitionCells * GameLoop.cellSize / length
        );

        // Hold the swap against the busier end of the step, because that is the cell where whoever
        // forced the change actually joins or leaves. Judging by offset size instead would move a
        // route into a middle lane long before the route that opened it up even arrives.
        float start;
        if (toLane.Crowd > fromLane.Crowd)
            start = 1f - LaneEdgeMargin - span;
        else if (fromLane.Crowd > toLane.Crowd)
            start = LaneEdgeMargin;
        else if (Mathf.Abs(toLane.Offset) > Mathf.Abs(fromLane.Offset) + 1e-4f)
            start = 1f - LaneEdgeMargin - span;
        else if (Mathf.Abs(fromLane.Offset) > Mathf.Abs(toLane.Offset) + 1e-4f)
            start = LaneEdgeMargin;
        else
            start = 0.5f - span * 0.5f;

        for (int step = 0; step <= LaneTransitionSteps; step++)
        {
            float blend = step / (float)LaneTransitionSteps;
            float distance = Mathf.Lerp(start, start + span, blend);
            float offset = Mathf.SmoothStep(fromLane.Offset, toLane.Offset, blend);
            results.Add(Flatten(Vector3.Lerp(from, to, distance) + perpendicular * offset, height));
        }
    }

    private static LanePlacement GetPlacement(IReadOnlyList<LanePlacement> lanes, int index)
    {
        return lanes != null && index < lanes.Count ? lanes[index] : new LanePlacement(0f, 1);
    }

    private static Vector3 Flatten(Vector3 point, float height)
    {
        point.y = height;
        return point;
    }

    /// <summary>Public view of the lane's sideways direction for a step.</summary>
    public static Vector3 GetPerpendicular(Vector3 from, Vector3 to) => Perpendicular(from, to);

    /// <summary>
    /// Sideways direction for a lane, measured against the unit's own travel. Keeping this relative
    /// to travel is what stops two routes turning the same corner from swapping which of them is on
    /// the outside; <see cref="GetDirectionSign"/> handles the head-on case instead.
    /// </summary>
    private static Vector3 Perpendicular(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.y = 0f;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
            return Vector3.zero;

        direction.Normalize();
        return new Vector3(direction.z, 0f, -direction.x);
    }
}
