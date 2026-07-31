using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records which roster slots run through each cell, per axis of travel, so a route sits dead
/// centre wherever it has a corridor to itself and only steps aside for units genuinely sharing
/// that corridor. Routes merely crossing a cell on the other axis are left alone.
/// </summary>
public sealed class RouteLaneMap
{
    private readonly struct LaneKey : System.IEquatable<LaneKey>
    {
        public readonly Vector2Int Cell;
        public readonly int Axis;

        public LaneKey(Vector2Int cell, int axis)
        {
            Cell = cell;
            Axis = axis;
        }

        public bool Equals(LaneKey other) => Cell == other.Cell && Axis == other.Axis;

        public override bool Equals(object obj) => obj is LaneKey other && Equals(other);

        public override int GetHashCode() => (Cell.GetHashCode() * 397) ^ Axis;
    }

    private readonly struct Occupant : System.IComparable<Occupant>
    {
        public readonly int Slot;
        public readonly int DirectionSign;

        // Biases folded into the shared frame, so ascending order always runs the same way across
        // the corridor no matter which way each unit is walking down it.
        public readonly int EntryKey;
        public readonly float EntryProgress;
        public readonly int CarriedKey;
        public readonly int ExitKey;

        /// <summary>Assigned after sorting; routes that never overlap may share one.</summary>
        public readonly int Lane;

        public Occupant(
            int slot,
            int directionSign,
            int entryBias,
            float entryProgress,
            int previousEntryBias,
            int exitBias
        )
        {
            Slot = slot;
            DirectionSign = directionSign;
            EntryKey = entryBias * directionSign;
            EntryProgress = entryProgress;
            CarriedKey = previousEntryBias * directionSign;
            ExitKey = exitBias * directionSign;
            Lane = 0;
        }

        private Occupant(Occupant source, int lane)
        {
            Slot = source.Slot;
            DirectionSign = source.DirectionSign;
            EntryKey = source.EntryKey;
            EntryProgress = source.EntryProgress;
            CarriedKey = source.CarriedKey;
            ExitKey = source.ExitKey;
            Lane = lane;
        }

        public Occupant WithLane(int lane) => new(this, lane);

        public int CompareTo(Occupant other)
        {
            // Where a route joins decides its lane first, because it must cross everyone already in
            // the corridor to reach any other one.
            int byEntry = EntryKey.CompareTo(other.EntryKey);
            if (byEntry != 0)
                return byEntry;

            // Merged in from the same side, so whoever got there first sits furthest from it. This
            // only applies to routes that actually merged: one that simply starts inside the
            // corridor crosses nobody on the way in, and should be placed by where it leaves.
            if (EntryKey != 0)
            {
                int byProgress = EntryProgress.CompareTo(other.EntryProgress);
                if (byProgress != 0)
                {
                    // "Furthest from the side we both came from" runs toward whichever end of the
                    // ordering is away from that side, so the comparison flips with it.
                    return EntryKey > 0 ? byProgress : -byProgress;
                }
            }

            // Identical approach to this corridor, so hold the order they arrived in rather than
            // letting two routes rounding the same corner swap across each other.
            int byCarried = CarriedKey.CompareTo(other.CarriedKey);
            if (byCarried != 0)
                return byCarried;

            int byExit = ExitKey.CompareTo(other.ExitKey);
            return byExit != 0 ? byExit : Slot.CompareTo(other.Slot);
        }
    }

    // The routes as handed in, kept because lanes cannot be resolved until every route is known.
    private readonly List<int> routeSlots = new();
    private readonly List<List<Vector3>> routeCells = new();
    private readonly Stack<List<Vector3>> cellPool = new();

    // Three views of who is where, narrowing as they go. Every route using a step; every route
    // touching a cell on an axis; and only those that end up holding a lane there, which is what
    // the contention checks read so a mere touch cannot pin somebody's lane.
    private readonly Dictionary<LaneKey, List<int>> edges = new();
    private readonly Dictionary<LaneKey, List<int>> cellUsers = new();
    private readonly Dictionary<LaneKey, List<int>> laneHolders = new();
    private readonly Stack<List<int>> slotListPool = new();

    // Cells a corner overruns into. They need sharing out even though no step is shared there.
    private readonly HashSet<LaneKey> forcedCells = new();

    private readonly Dictionary<LaneKey, List<Occupant>> occupants = new();
    private readonly Stack<List<Occupant>> occupantPool = new();
    private readonly Dictionary<LaneKey, List<Vector2>> runExtents = new();
    private readonly Stack<List<Vector2>> extentPool = new();

    private const float ExtentTouchTolerance = 1e-3f;
    private bool needsRebuild;

    public void Clear()
    {
        ReleaseOccupants();
        ReleaseCellLists();

        for (int i = 0; i < routeCells.Count; i++)
        {
            routeCells[i].Clear();
            cellPool.Push(routeCells[i]);
        }
        routeCells.Clear();
        routeSlots.Clear();
        needsRebuild = false;
    }

    /// <summary>
    /// Takes a copy of a drawn route. Lanes are resolved lazily once every route is known, because
    /// whether a route's entry or exit constrains its lane depends on who else is beside it there.
    /// Plans of fewer than two cells describe a unit holding position, which draws nothing and so
    /// must not push anyone aside.
    /// </summary>
    public void AddRoute(int rosterSlot, IReadOnlyList<Vector3> cells)
    {
        if (cells == null || cells.Count < 2)
            return;

        List<Vector3> copy = cellPool.Count > 0 ? cellPool.Pop() : new List<Vector3>();
        copy.Clear();
        for (int i = 0; i < cells.Count; i++)
            copy.Add(cells[i]);

        routeSlots.Add(rosterSlot);
        routeCells.Add(copy);
        needsRebuild = true;
    }

    /// <summary>
    /// Resolves every lane in four stages, each needing the one before it settled: record who is
    /// where, place the genuinely shared steps, find the corners that overrun into cells nobody
    /// shares, then fold routes covering disjoint stretches onto a common lane.
    /// </summary>
    private void Rebuild()
    {
        needsRebuild = false;
        ReleaseOccupants();
        ReleaseCellLists();

        // Sharing is mostly a property of the step between two cells rather than of a cell. Two
        // runs that merely meet at one cell and then both leave on the other axis cover disjoint
        // stretches of the corridor, so neither has to give way for a pass that never happens.
        for (int r = 0; r < routeCells.Count; r++)
        {
            List<Vector3> cells = routeCells[r];
            for (int i = 0; i < cells.Count - 1; i++)
            {
                int axis = PlanPathStyle.GetAxis(cells[i], cells[i + 1]);
                AddSlot(edges, GetEdgeKey(cells[i], cells[i + 1]), routeSlots[r]);
                AddSlot(cellUsers, GetCellKey(cells[i], axis), routeSlots[r]);
                AddSlot(cellUsers, GetCellKey(cells[i + 1], axis), routeSlots[r]);
            }
        }

        // Placing the shared steps on their own settles every lane a corner could be displaced
        // into, which the overrun tests below need before they can tell a real overlap from two
        // corners that clear each other.
        RegisterSteps(false);
        FindOverrunCells();

        ReleaseOccupants();
        RegisterSteps(true);

        // With every lane settled one per route, the corner overruns are known, so extents can be
        // measured and routes covering disjoint stretches folded onto a shared lane.
        CacheRunExtents();
        AssignLanes(false);
    }

    private void FindOverrunCells()
    {
        forcedCells.Clear();
        for (int r = 0; r < routeCells.Count; r++)
        {
            List<Vector3> cells = routeCells[r];
            for (int i = 0; i < cells.Count - 1; i++)
            {
                if (IsSharedStep(cells[i], cells[i + 1]))
                    continue;

                int axis = PlanPathStyle.GetAxis(cells[i], cells[i + 1]);

                // A run pushed backward into the cell it starts from, or carried past the cell it
                // ends in, reaches into ground it holds no lane for.
                if (NeedsRoomAtRunStart(cells, i, routeSlots[r]))
                    forcedCells.Add(GetCellKey(cells[i], axis));

                int runEnd = PlanPathStyle.GetRunEndIndex(cells, i);
                if (runEnd == i + 1 && OverrunsRunEnd(cells, runEnd, routeSlots[r], axis))
                    forcedCells.Add(GetCellKey(cells[runEnd], axis));
            }
        }
    }

    private void ReleaseOccupants()
    {
        foreach (KeyValuePair<LaneKey, List<Occupant>> entry in occupants)
        {
            entry.Value.Clear();
            occupantPool.Push(entry.Value);
        }
        occupants.Clear();
    }

    private void RegisterSteps(bool applyForcedCells)
    {
        foreach (KeyValuePair<LaneKey, List<int>> entry in laneHolders)
        {
            entry.Value.Clear();
            slotListPool.Push(entry.Value);
        }
        laneHolders.Clear();

        for (int r = 0; r < routeCells.Count; r++)
        {
            List<Vector3> cells = routeCells[r];
            for (int i = 0; i < cells.Count - 1; i++)
            {
                if (!ShouldRegisterStep(r, i, applyForcedCells))
                    continue;

                int axis = PlanPathStyle.GetAxis(cells[i], cells[i + 1]);
                AddSlot(laneHolders, GetCellKey(cells[i], axis), routeSlots[r]);
                AddSlot(laneHolders, GetCellKey(cells[i + 1], axis), routeSlots[r]);
            }
        }

        for (int r = 0; r < routeCells.Count; r++)
        {
            List<Vector3> cells = routeCells[r];
            int rosterSlot = routeSlots[r];
            for (int i = 0; i < cells.Count - 1; i++)
            {
                if (!ShouldRegisterStep(r, i, applyForcedCells))
                    continue;

                int axis = PlanPathStyle.GetAxis(cells[i], cells[i + 1]);
                int sign = PlanPathStyle.GetDirectionSign(cells[i], cells[i + 1]);

                // Joining or leaving only pins a lane where somebody else is actually alongside at
                // that cell. An uncontested end can cross the corridor freely, and letting it vote
                // would override a route whose preference is genuinely contested.
                int runStart = PlanPathStyle.GetRunStartIndex(cells, i);
                int runEnd = PlanPathStyle.GetRunEndIndex(cells, i);
                int entryBias = IsContested(cells[runStart], axis, rosterSlot)
                    ? PlanPathStyle.GetEntryBias(cells, i)
                    : 0;
                int exitBias = IsContested(cells[runEnd], axis, rosterSlot)
                    ? PlanPathStyle.GetExitBias(cells, i)
                    : 0;

                // All constant along a run, so every step in it agrees on one lane.
                int carried = PlanPathStyle.GetPreviousEntryBias(cells, i);
                float entry = PlanPathStyle.GetRunEntryProgress(cells, i);
                Register(cells[i], axis, rosterSlot, sign, entryBias, entry, carried, exitBias);
                Register(cells[i + 1], axis, rosterSlot, sign, entryBias, entry, carried, exitBias);
            }
        }

        AssignLanes(true);
    }

    private bool ShouldRegisterStep(int routeIndex, int stepIndex, bool applyForcedCells)
    {
        List<Vector3> cells = routeCells[routeIndex];
        if (IsSharedStep(cells[stepIndex], cells[stepIndex + 1]))
            return true;
        if (!applyForcedCells)
            return false;

        // Once a cell has to be shared out, everyone passing through it on that axis needs a lane,
        // otherwise there is nobody to order the overrunning route against.
        int axis = PlanPathStyle.GetAxis(cells[stepIndex], cells[stepIndex + 1]);
        return forcedCells.Contains(GetCellKey(cells[stepIndex], axis))
            || forcedCells.Contains(GetCellKey(cells[stepIndex + 1], axis));
    }

    private bool OverrunsRunEnd(List<Vector3> cells, int cellIndex, int rosterSlot, int axis)
    {
        if (cellIndex <= 0 || cellIndex >= cells.Count - 1)
            return false;
        if (!HasOtherUser(cells[cellIndex], axis, rosterSlot))
            return false;

        int outgoingAxis = PlanPathStyle.GetAxis(cells[cellIndex], cells[cellIndex + 1]);
        float outgoing = GetPlacementCore(rosterSlot, cells[cellIndex], outgoingAxis).Offset;
        return PlanPathStyle.GetRunEndShift(cells, cellIndex, outgoing) > 1e-4f;
    }

    private bool HasOtherUser(Vector3 cell, int axis, int rosterSlot)
    {
        if (!cellUsers.TryGetValue(GetCellKey(cell, axis), out List<int> slots))
            return false;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != rosterSlot)
                return true;
        }
        return false;
    }

    private static LaneKey GetCellKey(Vector3 cell, int axis)
    {
        return new LaneKey(GridSystem.ConvertToGridCoords(cell), axis);
    }

    /// <summary>Identifies a step by its lower cell along the axis, so both directions agree.</summary>
    private static LaneKey GetEdgeKey(Vector3 from, Vector3 to)
    {
        int axis = PlanPathStyle.GetAxis(from, to);
        Vector2Int a = GridSystem.ConvertToGridCoords(from);
        Vector2Int b = GridSystem.ConvertToGridCoords(to);
        Vector2Int lower =
            axis == PlanPathStyle.AxisX
                ? new Vector2Int(Mathf.Min(a.x, b.x), a.y)
                : new Vector2Int(a.x, Mathf.Min(a.y, b.y));
        return new LaneKey(lower, axis);
    }

    private void AddSlot(Dictionary<LaneKey, List<int>> map, LaneKey key, int rosterSlot)
    {
        if (!map.TryGetValue(key, out List<int> slots))
        {
            slots = slotListPool.Count > 0 ? slotListPool.Pop() : new List<int>();
            map[key] = slots;
        }
        if (!slots.Contains(rosterSlot))
            slots.Add(rosterSlot);
    }

    private bool IsSharedStep(Vector3 from, Vector3 to)
    {
        return edges.TryGetValue(GetEdgeKey(from, to), out List<int> slots) && slots.Count > 1;
    }

    /// <summary>
    /// A run that begins at a cell is pushed back into it by its own turn, because the corner sits
    /// at the incoming lane's offset. Two runs leaving one cell in opposite directions therefore do
    /// overlap around it even though they share no step, and must still make room. Runs that only
    /// converge on a cell are pulled back instead, so those are left alone.
    /// </summary>
    private bool NeedsRoomAtRunStart(List<Vector3> cells, int stepIndex, int rosterSlot)
    {
        if (stepIndex == 0 || stepIndex != PlanPathStyle.GetRunStartIndex(cells, stepIndex))
            return false;

        int axis = PlanPathStyle.GetAxis(cells[stepIndex], cells[stepIndex + 1]);
        if (!HasOtherUser(cells[stepIndex], axis, rosterSlot))
            return false;

        // Only a corner pushed backward reaches into the cell far enough to meet a run leaving the
        // other way. A corner that pulls forward has already cleared it, and forcing room there
        // would kick the route sideways for one cell and straight back again.
        int incomingAxis = PlanPathStyle.GetAxis(cells[stepIndex - 1], cells[stepIndex]);
        float incoming = GetPlacementCore(rosterSlot, cells[stepIndex], incomingAxis).Offset;
        return PlanPathStyle.GetRunStartShift(cells, stepIndex, incoming) < -1e-4f;
    }

    private bool IsContested(Vector3 cell, int axis, int rosterSlot)
    {
        if (!laneHolders.TryGetValue(GetCellKey(cell, axis), out List<int> slots))
            return false;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == rosterSlot)
                continue;

            // A route that finishes on this cell has no line carrying on past it, so moving by it
            // is a touch rather than a crossing. Letting it pin a lane here would force whoever is
            // joining to honour an entry side it does not actually need, and then cut across
            // somebody real on the way out.
            if (IsRouteEndCell(slots[i], cell))
                continue;

            return true;
        }
        return false;
    }

    private bool IsRouteEndCell(int rosterSlot, Vector3 cell)
    {
        Vector2Int target = GridSystem.ConvertToGridCoords(cell);
        for (int r = 0; r < routeSlots.Count; r++)
        {
            if (routeSlots[r] != rosterSlot)
                continue;

            List<Vector3> cells = routeCells[r];
            return GridSystem.ConvertToGridCoords(cells[cells.Count - 1]) == target;
        }
        return false;
    }

    private void ReleaseCellLists()
    {
        ReleaseSlotLists(laneHolders);
        ReleaseSlotLists(edges);
        ReleaseSlotLists(cellUsers);

        foreach (KeyValuePair<LaneKey, List<Vector2>> entry in runExtents)
        {
            entry.Value.Clear();
            extentPool.Push(entry.Value);
        }
        runExtents.Clear();
    }

    private void ReleaseSlotLists(Dictionary<LaneKey, List<int>> map)
    {
        foreach (KeyValuePair<LaneKey, List<int>> entry in map)
        {
            entry.Value.Clear();
            slotListPool.Push(entry.Value);
        }
        map.Clear();
    }

    private void Register(
        Vector3 cell,
        int axis,
        int rosterSlot,
        int directionSign,
        int entryBias,
        float entryProgress,
        int previousEntryBias,
        int exitBias
    )
    {
        LaneKey key = new(GridSystem.ConvertToGridCoords(cell), axis);
        if (!occupants.TryGetValue(key, out List<Occupant> slots))
        {
            slots = occupantPool.Count > 0 ? occupantPool.Pop() : new List<Occupant>();
            occupants[key] = slots;
        }

        Occupant occupant = new(
            rosterSlot,
            directionSign,
            entryBias,
            entryProgress,
            previousEntryBias,
            exitBias
        );
        int index = slots.BinarySearch(occupant);
        if (index < 0)
            slots.Insert(~index, occupant);
    }

    /// <summary>
    /// Lane position for a slot in one cell on one axis. Units travelling against the reference
    /// direction read the slot order backwards, so each keeps to its own side of the corridor
    /// exactly as its travel-relative lane offset expects.
    /// </summary>
    public bool TryGetLane(
        int rosterSlot,
        Vector3 cell,
        int axis,
        out int laneIndex,
        out int laneCount
    )
    {
        if (needsRebuild)
            Rebuild();

        return TryGetLaneCore(rosterSlot, cell, axis, out laneIndex, out laneCount);
    }

    private bool TryGetLaneCore(
        int rosterSlot,
        Vector3 cell,
        int axis,
        out int laneIndex,
        out int laneCount
    )
    {
        laneIndex = 0;
        laneCount = 1;
        LaneKey key = new(GridSystem.ConvertToGridCoords(cell), axis);
        if (!occupants.TryGetValue(key, out List<Occupant> slots))
            return false;

        int position = -1;
        int span = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            span = Mathf.Max(span, slots[i].Lane + 1);
            if (slots[i].Slot == rosterSlot)
                position = i;
        }
        if (position < 0)
            return false;

        laneCount = Mathf.Max(1, span);
        laneIndex =
            slots[position].DirectionSign >= 0
                ? slots[position].Lane
                : laneCount - 1 - slots[position].Lane;
        return true;
    }

    /// <summary>
    /// Hands out lane numbers within each cell. Routes only need separate lanes where they actually
    /// cover the same stretch of the corridor, so the two arms of a T junction can share one and a
    /// junction takes two lanes rather than three. The safe test is geometric — do their drawn
    /// extents along this axis overlap — because the structural shortcuts do not hold: two routes
    /// sharing no step here may still be forced together by a corner overrun, and two routes that
    /// both merely terminate here may be running the entire corridor side by side.
    /// </summary>
    private void AssignLanes(bool oneLaneEach)
    {
        foreach (KeyValuePair<LaneKey, List<Occupant>> entry in occupants)
        {
            List<Occupant> slots = entry.Value;
            List<Vector2> spans = null;
            if (!oneLaneEach)
                runExtents.TryGetValue(entry.Key, out spans);

            for (int i = 0; i < slots.Count; i++)
            {
                int lane = 0;
                bool clash = true;
                while (clash)
                {
                    clash = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (slots[j].Lane != lane)
                            continue;
                        if (!RunsAlongside(entry.Key, spans, i, j, oneLaneEach))
                            continue;

                        lane++;
                        clash = true;
                        break;
                    }
                }
                slots[i] = slots[i].WithLane(lane);
            }
        }
    }

    private bool RunsAlongside(LaneKey key, List<Vector2> spans, int first, int second, bool oneLaneEach)
    {
        if (oneLaneEach || spans == null || first >= spans.Count || second >= spans.Count)
            return true;

        // A cell only reaches this set because a corner overruns into it, which is exactly where
        // the extents cannot be trusted to tell the whole story.
        if (forcedCells.Contains(key))
            return true;

        Vector2 a = spans[first];
        Vector2 b = spans[second];
        return Mathf.Min(a.y, b.y) - Mathf.Max(a.x, b.x) > ExtentTouchTolerance;
    }

    /// <summary>
    /// Records how far along the axis each route's run actually reaches at every cell, corner
    /// overruns included, using the one-lane-each offsets so the numbers are settled before the
    /// relaxed pass reads them.
    /// </summary>
    private void CacheRunExtents()
    {
        foreach (KeyValuePair<LaneKey, List<Vector2>> entry in runExtents)
        {
            entry.Value.Clear();
            extentPool.Push(entry.Value);
        }
        runExtents.Clear();

        foreach (KeyValuePair<LaneKey, List<Occupant>> entry in occupants)
        {
            List<Vector2> spans = extentPool.Count > 0 ? extentPool.Pop() : new List<Vector2>();
            List<Occupant> slots = entry.Value;
            for (int i = 0; i < slots.Count; i++)
                spans.Add(GetRunExtent(slots[i].Slot, entry.Key));

            runExtents[entry.Key] = spans;
        }
    }

    private Vector2 GetRunExtent(int rosterSlot, LaneKey key)
    {
        for (int r = 0; r < routeSlots.Count; r++)
        {
            if (routeSlots[r] != rosterSlot)
                continue;

            List<Vector3> cells = routeCells[r];
            for (int i = 0; i < cells.Count - 1; i++)
            {
                if (PlanPathStyle.GetAxis(cells[i], cells[i + 1]) != key.Axis)
                    continue;
                if (
                    GridSystem.ConvertToGridCoords(cells[i]) != key.Cell
                    && GridSystem.ConvertToGridCoords(cells[i + 1]) != key.Cell
                )
                {
                    continue;
                }

                int start = PlanPathStyle.GetRunStartIndex(cells, i);
                int end = PlanPathStyle.GetRunEndIndex(cells, i);
                float from = GetRunEdgeCoordinate(cells, start, rosterSlot, key.Axis, true);
                float to = GetRunEdgeCoordinate(cells, end, rosterSlot, key.Axis, false);
                return new Vector2(Mathf.Min(from, to), Mathf.Max(from, to));
            }
        }
        return new Vector2(float.MinValue, float.MaxValue);
    }

    /// <summary>
    /// Where one end of a run actually sits along its own axis. A turn puts the corner at the
    /// offset of the corridor it joins, and that offset lies along this axis, so the run can reach
    /// past the cell it turns in.
    /// </summary>
    private float GetRunEdgeCoordinate(
        List<Vector3> cells,
        int cellIndex,
        int rosterSlot,
        int axis,
        bool isStart
    )
    {
        Vector3 cell = cells[cellIndex];
        float coordinate = axis == PlanPathStyle.AxisX ? cell.x : cell.z;

        int neighbour = isStart ? cellIndex - 1 : cellIndex + 1;
        if (neighbour < 0 || neighbour >= cells.Count)
            return coordinate;

        Vector3 perpendicular = isStart
            ? PlanPathStyle.GetPerpendicular(cells[neighbour], cell)
            : PlanPathStyle.GetPerpendicular(cell, cells[neighbour]);
        int turnAxis = isStart
            ? PlanPathStyle.GetAxis(cells[neighbour], cell)
            : PlanPathStyle.GetAxis(cell, cells[neighbour]);
        float offset = GetPlacementCore(rosterSlot, cell, turnAxis).Offset;

        return coordinate
            + (axis == PlanPathStyle.AxisX ? perpendicular.x : perpendicular.z) * offset;
    }

    /// <summary>Sideways shift for a slot on one axis; zero when it has that corridor to itself.</summary>
    public float GetLaneOffset(int rosterSlot, Vector3 cell, int axis)
    {
        return GetPlacement(rosterSlot, cell, axis).Offset;
    }

    /// <summary>Lane offset plus how many routes share that cell, which decides where a lane change sits.</summary>
    public LanePlacement GetPlacement(int rosterSlot, Vector3 cell, int axis)
    {
        if (needsRebuild)
            Rebuild();

        return GetPlacementCore(rosterSlot, cell, axis);
    }

    private LanePlacement GetPlacementCore(int rosterSlot, Vector3 cell, int axis)
    {
        if (
            !TryGetLaneCore(rosterSlot, cell, axis, out int laneIndex, out int laneCount)
            || laneCount <= 1
        )
        {
            return new LanePlacement(0f, 1);
        }

        return new LanePlacement(
            (laneIndex - (laneCount - 1) * 0.5f) * PlanPathStyle.LaneSpacing,
            laneCount
        );
    }
}
