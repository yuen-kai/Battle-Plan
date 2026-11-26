using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Plays Elimination with the self-play network from <c>Training/</c>. Every decision is encoded
/// exactly as <c>battlesim/observe.py</c> encodes it: one step of one unit's path, or one ability
/// target, chosen from the bot's fog observation and its memory of where enemies were last seen.
/// </summary>
public sealed class TrainedBotPlayer
{
    private const int TeamSize = 5;
    private const int TypeCount = 5;
    private const float MaxMove = 5f;
    private const float MaxRounds = 40f;
    private const float MaxCooldown = 4f;
    private const int Columns = GridSystem.ColumnCount;
    private const int Rows = GridSystem.RowCount;
    private const int Cells = TrainedBotNetwork.Cells;
    private const int MoveActions = TrainedBotNetwork.MoveActions;
    private const int Stay = 0;
    private const double LineOvershootCells = 50.0 / 2.7;
    private const double WallHalf = 0.5;
    private const double WallChamfer = 0.4 / 2.7;
    private const double RayRadius = 1e-7;
    private static readonly Vector2Int[] Corners = { new(1, 1), new(-1, 1), new(-1, -1), new(1, -1) };

    private const int WallPlane = 0;
    private const int VisiblePlane = 1;
    private const int SmokePlane = 2;
    private const int OwnHpPlane = 3;
    private const int OwnCooldownPlane = 4;
    private const int EnemyHpPlane = 5;
    private const int MemoryAgePlane = 6;
    private const int ActivePathPlane = 7;
    private const int ActiveHeadPlane = TrainedBotNetwork.ActiveHeadPlane;
    private const int ClaimedPlane = 9;
    private const int EnemyThreatPlane = 10;
    private const int EnemyMarkerPlane = 11;
    private const int OwnMarkerPlane = 12;
    private const int AlertedPlane = 13;
    private const int OwnTypePlane = 14;
    private const int EnemyTypePlane = OwnTypePlane + TypeCount;
    private const int TeamPathPlane = EnemyTypePlane + TypeCount;
    private const int AllyAbilityPlane = TeamPathPlane + 1;
    private const int EscapePlane = AllyAbilityPlane + TypeCount;
    private const int FullPlanes = EscapePlane + 1;
    private const int RevisingScalar = TypeCount + 11 + TeamSize * (TypeCount + 3);
    private const int FullScalars = RevisingScalar + 1;

    private static readonly string[] AbilityTypes =
    {
        "Smoke Screen",
        "Pogo",
        "Ram Charge",
        "Area Lock",
        "Grenade",
    };
    private static readonly Vector2Int[] Steps =
    {
        Vector2Int.zero,
        Vector2Int.up,
        Vector2Int.right,
        Vector2Int.down,
        Vector2Int.left,
    };
    private static readonly int[] MoveMirror = { 0, 3, 4, 1, 2 };
    private static readonly float[] HalfHeights = { 1.7946f, 1.7946f, 1.6f, 1.5605f, 1.7946f };

    private readonly struct Sighting
    {
        public readonly Vector2Int Cell;
        public readonly int Type;
        public readonly float HpFraction;
        public readonly int Round;

        public Sighting(Vector2Int cell, int type, float hpFraction, int round)
        {
            Cell = cell;
            Type = type;
            HpFraction = hpFraction;
            Round = round;
        }
    }

    private readonly struct Telegraph
    {
        public readonly GameObject Caster;
        public readonly bool Friendly;
        public readonly Vector2Int Target;
        public readonly List<Vector2Int> Cells;

        public Telegraph(GameObject caster, bool friendly, Vector2Int target, List<Vector2Int> cells)
        {
            Caster = caster;
            Friendly = friendly;
            Target = target;
            Cells = cells;
        }
    }

    private readonly GameLoop gameLoop;
    private readonly int teamIndex;
    private readonly TrainedBotNetwork network = TrainedBotNetwork.Shared;
    private readonly Dictionary<GameObject, (Vector2Int cell, int round)> lastSeen = new();
    private readonly Dictionary<GameObject, Sighting> lastKnown = new();
    private readonly float[] planes = new float[FullPlanes * Cells];
    private readonly float[] canonicalPlanes = new float[FullPlanes * Cells];
    private readonly float[] scalars = new float[FullScalars];
    private readonly bool[] mask = new bool[TrainedBotNetwork.Actions];
    private readonly bool[] canonicalMask = new bool[TrainedBotNetwork.Actions];
    private GameObject[] enemyRoster;
    private HashSet<Vector2Int> visibleCells = new();

    public TrainedBotPlayer(GameLoop gameLoop, int teamIndex)
    {
        this.gameLoop = gameLoop;
        this.teamIndex = teamIndex;
    }

    private static readonly List<(GameObject, List<Vector2Int>, Vector2Int?)> NoTeammates = new();

    private int EnemyTeamIndex => GameLoop.GetEnemyTeamIndex(teamIndex);

    public PathsDict CreatePlanningContribution(int roundNumber, out bool usedAbility)
    {
        int completedRounds = roundNumber - 1;
        enemyRoster ??= GameLoop.GetTeamUnits(EnemyTeamIndex).OrderBy(Slot).ToArray();
        visibleCells = gameLoop.GetObservableCellsForTeam(teamIndex);
        RecordSightings(completedRounds);
        UpdateMemory();

        List<GameObject> order = OwnLivingUnits();
        Dictionary<GameObject, List<Vector2Int>> routes = order.ToDictionary(unit => unit, unit => new List<Vector2Int> { Cell(unit) });
        Dictionary<GameObject, Vector2Int> abilities = new();
        HashSet<Vector2Int> claimed = new();
        for (int pass = 0; pass < (network.Revises ? 2 : 1); pass++)
        {
            bool revising = pass == 1;
            for (int i = 0; i < order.Count; i++)
            {
                GameObject unit = order[i];
                if (revising)
                {
                    routes[unit] = new List<Vector2Int> { Cell(unit) };
                    abilities.Remove(unit);
                    claimed = new HashSet<Vector2Int>(
                        order.Where(other => other != unit).Select(other => EndCell(other, routes, abilities))
                    );
                }
                List<(GameObject, List<Vector2Int>, Vector2Int?)> teammates = (revising ? order : order.Take(i))
                    .Where(other => other != unit)
                    .Select(other => (other, routes[other], abilities.TryGetValue(other, out Vector2Int aim) ? aim : (Vector2Int?)null))
                    .ToList();
                PlanUnit(unit, routes, abilities, claimed, teammates, completedRounds, revising);
            }
        }

        PathsDict plans = new();
        foreach (GameObject unit in order)
        {
            Vector2Int start = Cell(unit);
            UnitData data = unit.GetComponent<Movement>().unitData;
            plans[unit] = !abilities.TryGetValue(unit, out Vector2Int target) ? (false, routes[unit].Select(World).ToList())
                : data.selectAbilitySquare ? (true, new List<Vector3> { World(start), World(target) })
                : (true, new List<Vector3> { World(start) });
        }
        usedAbility = abilities.Count > 0;
        return plans;
    }

    private void PlanUnit(
        GameObject unit,
        Dictionary<GameObject, List<Vector2Int>> routes,
        Dictionary<GameObject, Vector2Int> abilities,
        HashSet<Vector2Int> claimed,
        List<(GameObject, List<Vector2Int>, Vector2Int?)> teammates,
        int round,
        bool revising
    )
    {
        UnitData data = unit.GetComponent<Movement>().unitData;
        Vector2Int start = Cell(unit);
        List<Vector2Int> path = routes[unit];
        List<Vector2Int> targets = AbilityTargets(unit, data, start);
        while (true)
        {
            FillPlanningMask(path, data.moveDist, claimed, targets);
            Encode(unit, path, claimed, round, null, null, 0, teammates, revising);
            int action = Decide();
            RecordDecision(unit, path, claimed, round, null, null, 0, teammates, revising, action);
            if (action >= MoveActions)
            {
                Vector2Int target = ActionCell(action);
                abilities[unit] = target;
                claimed.Add(CasterEndCell(data, start, target));
                return;
            }
            if (action != Stay)
            {
                path.Add(path[^1] + Steps[action]);
                if (path.Count - 1 < data.moveDist)
                    continue;
            }
            claimed.Add(path[^1]);
            return;
        }
    }

    private static Vector2Int EndCell(
        GameObject unit,
        Dictionary<GameObject, List<Vector2Int>> routes,
        Dictionary<GameObject, Vector2Int> abilities
    )
    {
        return abilities.TryGetValue(unit, out Vector2Int target)
            ? CasterEndCell(unit.GetComponent<Movement>().unitData, Cell(unit), target)
            : routes[unit][^1];
    }

    public PathsDict CreateDodgeContribution(
        IReadOnlyCollection<GameObject> alertedUnits,
        IReadOnlyList<(GameObject unit, Vector3 square, UnitData data)> activations,
        int maxDiveRange
    )
    {
        PathsDict dives = new();
        visibleCells = gameLoop.GetObservableCellsForTeam(teamIndex);
        List<Telegraph> telegraphs = activations
            .Where(activation => activation.unit != null && activation.data != null)
            .Select(activation => BuildTelegraph(activation.unit, activation.square, activation.data))
            .ToList();
        List<GameObject> alerted = alertedUnits.Where(IsLiving).OrderBy(Slot).ToList();
        HashSet<Vector2Int> alertedCells = new(alerted.Select(Cell));
        HashSet<Vector2Int> noClaims = new();

        foreach (GameObject unit in alerted)
        {
            List<Vector2Int> path = new() { Cell(unit) };
            while (true)
            {
                FillStepMask(path, maxDiveRange);
                Encode(unit, path, noClaims, gameLoop.RoundNumber, telegraphs, alertedCells, maxDiveRange, NoTeammates, false);
                int action = Decide();
                RecordDecision(unit, path, noClaims, gameLoop.RoundNumber, telegraphs, alertedCells, maxDiveRange, NoTeammates, false, action);
                if (action <= Stay || action >= MoveActions)
                    break;
                path.Add(path[^1] + Steps[action]);
                if (path.Count - 1 >= maxDiveRange)
                    break;
            }
            if (path.Count > 1)
                dives[unit] = (false, path.Select(World).ToList());
        }
        return dives;
    }

    /// <summary>
    /// Records where enemies are seen while a round plays out, as the simulator does every frame,
    /// so an enemy glimpsed mid-round is remembered at the cell it was last seen in.
    /// </summary>
    public IEnumerator WatchExecution()
    {
        int round = gameLoop.RoundNumber;
        while (GameLoop.currentPhase == GameLoop.Phase.Executing)
        {
            visibleCells = gameLoop.GetObservableCellsForTeam(teamIndex);
            RecordSightings(round);
            yield return null;
        }
    }

    private void RecordSightings(int round)
    {
        foreach (GameObject enemy in GameLoop.GetTeamUnits(EnemyTeamIndex).Where(IsLiving))
        {
            Vector2Int cell = Cell(enemy);
            if (visibleCells.Contains(cell))
                lastSeen[enemy] = (cell, round);
        }
    }

    private void UpdateMemory()
    {
        List<GameObject> living = GameLoop.GetTeamUnits(EnemyTeamIndex).Where(IsLiving).ToList();
        foreach (GameObject enemy in living)
        {
            if (!lastSeen.TryGetValue(enemy, out var seen))
                continue;
            if (!lastKnown.TryGetValue(enemy, out Sighting known) || seen.round >= known.Round)
                lastKnown[enemy] = new Sighting(seen.cell, TypeIndex(enemy), HpFraction(enemy), seen.round);
        }

        HashSet<Vector2Int> occupied = new(living.Select(Cell).Where(visibleCells.Contains));
        foreach (
            GameObject stale in lastKnown
                .Where(entry =>
                    !IsLiving(entry.Key)
                    || (visibleCells.Contains(entry.Value.Cell) && !occupied.Contains(entry.Value.Cell))
                )
                .Select(entry => entry.Key)
                .ToList()
        )
        {
            lastKnown.Remove(stale);
        }
    }

    private void FillStepMask(List<Vector2Int> path, int maxSteps)
    {
        Array.Clear(mask, 0, mask.Length);
        mask[Stay] = true;
        if (path.Count - 1 >= maxSteps)
            return;
        for (int move = 1; move < MoveActions; move++)
        {
            Vector2Int next = path[^1] + Steps[move];
            mask[move] =
                GridSystem.IsCellInBounds(next)
                && !GameLoop.wallLayout.Contains(next)
                && !path.Contains(next);
        }
    }

    private void FillPlanningMask(
        List<Vector2Int> path,
        int moveDist,
        HashSet<Vector2Int> claimed,
        List<Vector2Int> targets
    )
    {
        FillStepMask(path, moveDist);
        if (claimed.Contains(path[^1]) && mask.Skip(1).Take(MoveActions - 1).Any(legal => legal))
            mask[Stay] = false;
        if (path.Count == 1)
        {
            foreach (Vector2Int target in targets)
                mask[MoveActions + target.y * Columns + target.x] = true;
        }
    }

    private List<Vector2Int> AbilityTargets(GameObject unit, UnitData data, Vector2Int start)
    {
        List<Vector2Int> targets = new();
        Unit identity = unit.GetComponent<Unit>();
        if (identity == null || !identity.CanUseAbility || string.IsNullOrEmpty(data.abilityName))
            return targets;
        if (!data.selectAbilitySquare)
        {
            targets.Add(start);
            return targets;
        }

        bool smoke = unit.GetComponent<Smoke>() != null;
        for (int x = 0; x < Columns; x++)
        {
            for (int y = 0; y < Rows; y++)
            {
                Vector2Int target = new(x, y);
                if (smoke && !GridSystem.IsSquareFootprintInBounds(target, Smoke.FootprintRadius))
                    continue;
                Vector2Int delta = target - start;
                bool legal = data.selectAbilityDirection
                    ? data.abilityFixedDistance > 0
                        && delta != Vector2Int.zero
                        && Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) <= 1
                    : Mathf.Abs(delta.x) + Mathf.Abs(delta.y) <= data.abilitySquareRange + 0.1f
                        && (data.responseDistLine || !GameLoop.wallLayout.Contains(target))
                        && (data.CanTargetOwnCell || target != start);
                if (legal)
                    targets.Add(target);
            }
        }
        return targets;
    }

    private static Vector2Int CasterEndCell(UnitData data, Vector2Int start, Vector2Int target)
    {
        return data.abilityName switch
        {
            "Pogo" => target,
            "Ram Charge" => GridSystem.GetDirectionalDestination(
                start,
                target - start,
                data.abilityFixedDistance,
                GameLoop.wallLayout
            ),
            _ => start,
        };
    }

    private Telegraph BuildTelegraph(GameObject caster, Vector3 square, UnitData data)
    {
        Vector2Int start = Cell(caster);
        Vector2Int target = data.selectAbilitySquare ? GridSystem.ConvertToGridCoords(square) : start;
        return new Telegraph(caster, GameLoop.GetTeamIndex(caster) == teamIndex, target, AbilityCells(data, start, target));
    }

    private static List<Vector2Int> AbilityCells(UnitData data, Vector2Int start, Vector2Int target)
    {
        return data.abilityName switch
        {
            "Grenade" => DiscCells(target, data.abilityRadius),
            "Area Lock" => LineCells(start, target),
            "Smoke Screen" => GridSystem
                .GetSquareFootprint(target, Smoke.FootprintRadius)
                .Where(GridSystem.IsCellInBounds)
                .ToList(),
            "Pogo" or "Ram Charge" => new List<Vector2Int> { CasterEndCell(data, start, target) },
            _ when data.responseDistLine => LineCells(start, target),
            _ when data.abilityRadius > 0f => DiscCells(target, data.abilityRadius),
            _ => new List<Vector2Int> { target },
        };
    }

    private HashSet<Vector2Int> EscapeCells(UnitData data, Vector2Int start, Vector2Int target)
    {
        HashSet<Vector2Int> cells = new();
        if (data.responseRange <= 0f)
            return cells;
        (double x, double y) end = LineEnd(start, target);
        float reach = 2.7f * data.responseRange;
        foreach (Sighting sighting in lastKnown.Values)
        {
            Vector2Int cell = sighting.Cell;
            float halfHeight = sighting.Type >= 0 ? HalfHeights[sighting.Type] : HalfHeights[0];
            bool alerted = data.responseDistLine
                ? SegmentDistance(start.x, start.y, end.x, end.y, cell.x, cell.y) <= data.responseRange
                : 2.7f * 2.7f * (cell - target).sqrMagnitude + halfHeight * halfHeight <= reach * reach;
            if (alerted)
                cells.UnionWith(DiveReach(cell, data.diveRange));
        }
        return cells;
    }

    private static HashSet<Vector2Int> DiveReach(Vector2Int start, int steps)
    {
        HashSet<Vector2Int> reached = new() { start };
        List<Vector2Int> frontier = new() { start };
        for (int step = 0; step < steps; step++)
        {
            List<Vector2Int> next = new();
            foreach (Vector2Int cell in frontier)
            {
                for (int move = 1; move < MoveActions; move++)
                {
                    Vector2Int neighbour = cell + Steps[move];
                    if (GridSystem.IsCellInBounds(neighbour) && !GameLoop.wallLayout.Contains(neighbour) && reached.Add(neighbour))
                        next.Add(neighbour);
                }
            }
            frontier = next;
        }
        return reached;
    }

    private static List<Vector2Int> DiscCells(Vector2Int centre, float radius)
    {
        List<Vector2Int> cells = new();
        for (int x = 0; x < Columns; x++)
        {
            for (int y = 0; y < Rows; y++)
            {
                if ((x - centre.x) * (x - centre.x) + (y - centre.y) * (y - centre.y) <= radius * radius)
                    cells.Add(new Vector2Int(x, y));
            }
        }
        return cells;
    }

    private static (double x, double y) LineEnd(Vector2Int start, Vector2Int target)
    {
        double dx = target.x - start.x;
        double dy = target.y - start.y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance <= 1e-9)
            return (start.x, start.y);
        double ux = dx / distance;
        double uy = dy / distance;
        double far = distance + LineOvershootCells;
        double length = FirstWallContact(start.x, start.y, ux, uy, far) ?? far;
        return (start.x + ux * length, start.y + uy * length);
    }

    private static List<Vector2Int> LineCells(Vector2Int start, Vector2Int target)
    {
        List<Vector2Int> cells = new();
        if (start == target)
            return cells;
        (double x, double y) end = LineEnd(start, target);
        for (int x = 0; x < Columns; x++)
        {
            for (int y = 0; y < Rows; y++)
            {
                if (SegmentDistance(start.x, start.y, end.x, end.y, x, y) <= 0.5)
                    cells.Add(new Vector2Int(x, y));
            }
        }
        return cells;
    }

    private static double SegmentDistance(double ax, double ay, double bx, double by, double px, double py)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 1e-12)
            return double.PositiveInfinity;
        double t = ((px - ax) * dx + (py - ay) * dy) / lengthSquared;
        if (t < 0)
            return double.PositiveInfinity;
        t = Math.Min(1.0, t);
        return Math.Sqrt((px - (ax + t * dx)) * (px - (ax + t * dx)) + (py - (ay + t * dy)) * (py - (ay + t * dy)));
    }

    private static double? FirstWallContact(double sx, double sy, double ux, double uy, double length)
    {
        double ex = sx + ux * length;
        double ey = sy + uy * length;
        double pad = WallHalf + RayRadius;
        double? best = null;
        foreach (Vector2Int wall in GameLoop.wallLayout)
        {
            if (wall.x < Math.Min(sx, ex) - pad || wall.x > Math.Max(sx, ex) + pad)
                continue;
            if (wall.y < Math.Min(sy, ey) - pad || wall.y > Math.Max(sy, ey) + pad)
                continue;
            List<(double x, double y)> polygon = WallPolygon(wall);
            if (PointPolygonDistance(sx, sy, polygon) <= RayRadius)
                continue;
            double? t = RayPolygonSweep(sx, sy, ux, uy, polygon);
            if (t.HasValue && t.Value <= length && (best == null || t.Value < best.Value))
                best = t;
        }
        return best;
    }

    private static List<(double x, double y)> WallPolygon(Vector2Int cell)
    {
        List<(double x, double y)> points = new();
        double inset = WallHalf - WallChamfer;
        foreach (Vector2Int corner in Corners)
        {
            if (GameLoop.wallLayout.Contains(cell + corner))
            {
                points.Add((cell.x + corner.x * WallHalf, cell.y + corner.y * WallHalf));
                continue;
            }
            if (corner.x * corner.y > 0)
            {
                points.Add((cell.x + corner.x * WallHalf, cell.y + corner.y * inset));
                points.Add((cell.x + corner.x * inset, cell.y + corner.y * WallHalf));
            }
            else
            {
                points.Add((cell.x + corner.x * inset, cell.y + corner.y * WallHalf));
                points.Add((cell.x + corner.x * WallHalf, cell.y + corner.y * inset));
            }
        }
        return points;
    }

    private static double? RayPolygonSweep(double px, double py, double ux, double uy, List<(double x, double y)> polygon)
    {
        double? best = null;
        for (int i = 0; i < polygon.Count; i++)
        {
            (double ax, double ay) = polygon[i];
            (double bx, double by) = polygon[(i + 1) % polygon.Count];
            foreach (double? t in new[] { RaySegmentOffset(px, py, ux, uy, ax, ay, bx, by), RayCircle(px, py, ux, uy, ax, ay) })
            {
                if (t.HasValue && (best == null || t.Value < best.Value))
                    best = t;
            }
        }
        return best;
    }

    private static double? RaySegmentOffset(double px, double py, double ux, double uy, double ax, double ay, double bx, double by)
    {
        double ex = bx - ax;
        double ey = by - ay;
        double length = Math.Sqrt(ex * ex + ey * ey);
        if (length <= 1e-12)
            return null;
        double nx = -ey / length;
        double ny = ex / length;
        double? best = null;
        foreach (double side in new[] { 1.0, -1.0 })
        {
            double ox = ax + nx * RayRadius * side;
            double oy = ay + ny * RayRadius * side;
            double denominator = ux * ey - uy * ex;
            if (Math.Abs(denominator) <= 1e-12)
                continue;
            double wx = ox - px;
            double wy = oy - py;
            double t = (wx * ey - wy * ex) / denominator;
            double along = (wx * uy - wy * ux) / denominator;
            if (t >= 0 && along >= 0 && along <= 1 && (best == null || t < best.Value))
                best = t;
        }
        return best;
    }

    private static double? RayCircle(double px, double py, double ux, double uy, double cx, double cy)
    {
        double fx = px - cx;
        double fy = py - cy;
        double b = fx * ux + fy * uy;
        double c = fx * fx + fy * fy - RayRadius * RayRadius;
        double discriminant = b * b - c;
        if (discriminant < 0)
            return null;
        double t = -b - Math.Sqrt(discriminant);
        return t >= 0 ? t : (double?)null;
    }

    private static double PointPolygonDistance(double px, double py, List<(double x, double y)> polygon)
    {
        bool inside = true;
        double best = double.PositiveInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            (double ax, double ay) = polygon[i];
            (double bx, double by) = polygon[(i + 1) % polygon.Count];
            double ex = bx - ax;
            double ey = by - ay;
            if (ex * (py - ay) - ey * (px - ax) < 0)
                inside = false;
            double t = Math.Max(0.0, Math.Min(1.0, ((px - ax) * ex + (py - ay) * ey) / (ex * ex + ey * ey)));
            best = Math.Min(best, Math.Sqrt((px - (ax + t * ex)) * (px - (ax + t * ex)) + (py - (ay + t * ey)) * (py - (ay + t * ey))));
        }
        return inside ? 0.0 : best;
    }

    private void Encode(
        GameObject unit,
        List<Vector2Int> path,
        HashSet<Vector2Int> claimed,
        int round,
        List<Telegraph> telegraphs,
        HashSet<Vector2Int> alertedCells,
        int maxDive,
        List<(GameObject, List<Vector2Int>, Vector2Int?)> teammates,
        bool revising
    )
    {
        Array.Clear(planes, 0, planes.Length);
        foreach (Vector2Int wall in GameLoop.wallLayout)
            Set(WallPlane, wall, 1f);
        foreach (Vector2Int cell in visibleCells)
            Set(VisiblePlane, cell, 1f);
        foreach (Vector2Int cell in gameLoop.ActiveSmokeCells)
            Set(SmokePlane, cell, 1f);

        List<GameObject> own = OwnLivingUnits();
        foreach (GameObject ally in own)
        {
            Vector2Int cell = Cell(ally);
            Set(OwnHpPlane, cell, HpFraction(ally));
            Set(OwnCooldownPlane, cell, Cooldown(ally) / MaxCooldown);
            int type = TypeIndex(ally);
            if (type >= 0)
                Set(OwnTypePlane + type, cell, 1f);
        }
        foreach (Sighting sighting in lastKnown.Values)
        {
            Set(EnemyHpPlane, sighting.Cell, sighting.HpFraction);
            if (sighting.Type >= 0)
                Set(EnemyTypePlane + sighting.Type, sighting.Cell, 1f);
            Set(MemoryAgePlane, sighting.Cell, Mathf.Min(1f, (round - sighting.Round) / 5f));
        }
        foreach (Vector2Int cell in path)
            Set(ActivePathPlane, cell, 1f);
        Set(ActiveHeadPlane, path[^1], 1f);
        foreach (Vector2Int cell in claimed)
            Set(ClaimedPlane, cell, 1f);
        foreach ((GameObject ally, List<Vector2Int> route, Vector2Int? aim) in teammates)
        {
            if (aim == null)
            {
                foreach (Vector2Int cell in route.Skip(1))
                    Set(TeamPathPlane, cell, 1f);
                continue;
            }
            UnitData allyData = ally.GetComponent<Movement>().unitData;
            int allyType = TypeIndex(ally);
            if (allyType >= 0)
            {
                foreach (Vector2Int cell in AbilityCells(allyData, Cell(ally), aim.Value))
                    Set(AllyAbilityPlane + allyType, cell, 1f);
            }
            foreach (Vector2Int cell in EscapeCells(allyData, Cell(ally), aim.Value))
                Set(EscapePlane, cell, 1f);
        }
        bool dodge = telegraphs != null;
        if (dodge)
        {
            foreach (Telegraph telegraph in telegraphs)
            {
                foreach (Vector2Int cell in telegraph.Cells)
                    Set(telegraph.Friendly ? OwnMarkerPlane : EnemyThreatPlane, cell, 1f);
                if (!telegraph.Friendly)
                    Set(EnemyMarkerPlane, telegraph.Target, 1f);
            }
            foreach (Vector2Int cell in alertedCells)
                Set(AlertedPlane, cell, 1f);
        }

        Array.Clear(scalars, 0, scalars.Length);
        UnitData data = unit.GetComponent<Movement>().unitData;
        int unitType = TypeIndex(unit);
        if (unitType >= 0)
            scalars[unitType] = 1f;
        scalars[TypeCount + 0] = (path.Count - 1) / MaxMove;
        scalars[TypeCount + 1] = data.moveDist / MaxMove;
        scalars[TypeCount + 2] = HpFraction(unit);
        scalars[TypeCount + 3] = Cooldown(unit) / MaxCooldown;
        scalars[TypeCount + 4] = round / MaxRounds;
        scalars[TypeCount + 5] = own.Count / (float)TeamSize;
        scalars[TypeCount + 6] = enemyRoster.Count(IsLiving) / (float)TeamSize;
        scalars[TypeCount + 7] = dodge ? 1f : 0f;
        scalars[TypeCount + 8] = (dodge ? maxDive : 0) / 2f;
        scalars[TypeCount + 9] = gameLoop.FogOfWarEnabled ? 1f : 0f;
        scalars[TypeCount + 10] = teamIndex;
        int offset = TypeCount + 11;
        foreach (GameObject enemy in enemyRoster.Take(TeamSize))
        {
            int type = TypeIndex(enemy);
            if (type >= 0)
                scalars[offset + type] = 1f;
            scalars[offset + TypeCount] = IsLiving(enemy) ? HpFraction(enemy) : 0f;
            scalars[offset + TypeCount + 1] = IsLiving(enemy) ? 1f : 0f;
            scalars[offset + TypeCount + 2] = Cooldown(enemy) / MaxCooldown;
            offset += TypeCount + 3;
        }
        scalars[RevisingScalar] = revising ? 1f : 0f;
    }

    private int Decide()
    {
        if (teamIndex != 1)
            return network.Evaluate(planes, scalars, mask);

        for (int plane = 0; plane < network.Planes; plane++)
        {
            int baseIndex = plane * Cells;
            for (int i = 0; i < Cells; i++)
                canonicalPlanes[baseIndex + i] = planes[baseIndex + Cells - 1 - i];
        }
        for (int move = 0; move < MoveActions; move++)
            canonicalMask[move] = mask[MoveMirror[move]];
        for (int i = 0; i < Cells; i++)
            canonicalMask[MoveActions + i] = mask[MoveActions + Cells - 1 - i];

        int action = network.Evaluate(canonicalPlanes, scalars, canonicalMask);
        return action < MoveActions ? MoveMirror[action] : MoveActions + Cells - 1 - (action - MoveActions);
    }

    private void Set(int plane, Vector2Int cell, float value)
    {
        if (GridSystem.IsCellInBounds(cell))
            planes[plane * Cells + cell.y * Columns + cell.x] = value;
    }

    private List<GameObject> OwnLivingUnits()
    {
        return GameLoop.GetTeamUnits(teamIndex).Where(IsLiving).OrderBy(Slot).ToList();
    }

    private static Vector2Int ActionCell(int action)
    {
        int index = action - MoveActions;
        return new Vector2Int(index % Columns, index / Columns);
    }

    private static int TypeIndex(GameObject unit)
    {
        UnitData data = unit != null ? unit.GetComponent<Movement>()?.unitData : null;
        return data != null ? Array.IndexOf(AbilityTypes, data.abilityName) : -1;
    }

    private static float HpFraction(GameObject unit)
    {
        Health health = unit != null ? unit.GetComponent<Health>() : null;
        return health != null && health.MaxHealth > 0f
            ? Mathf.Max(0f, health.CurrentHealth) / health.MaxHealth
            : 0f;
    }

    private static float Cooldown(GameObject unit)
    {
        Unit identity = unit != null ? unit.GetComponent<Unit>() : null;
        return identity != null ? identity.AbilityCooldownRoundsRemaining : 0f;
    }

    private static int Slot(GameObject unit)
    {
        Unit identity = unit != null ? unit.GetComponent<Unit>() : null;
        return identity != null ? identity.RosterSlot : int.MaxValue;
    }

    private static bool IsLiving(GameObject unit)
    {
        if (unit == null || !unit.activeInHierarchy)
            return false;
        Health health = unit.GetComponent<Health>();
        return health == null || health.IsAlive;
    }

    private static Vector2Int Cell(GameObject unit)
    {
        return GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));
    }

    private static Vector3 World(Vector2Int cell) => GameLoop.gridCoordToWorld(cell);

    public static Action<string> DevDecisionRecorded;

    [Conditional("UNITY_EDITOR")]
    private void RecordDecision(
        GameObject unit,
        List<Vector2Int> path,
        HashSet<Vector2Int> claimed,
        int round,
        List<Telegraph> telegraphs,
        HashSet<Vector2Int> alertedCells,
        int maxDive,
        List<(GameObject, List<Vector2Int>, Vector2Int?)> teammates,
        bool revising,
        int action
    )
    {
        if (DevDecisionRecorded == null)
            return;

        StringBuilder json = new();
        json.Append("{\"round\":").Append(round)
            .Append(",\"team\":").Append(teamIndex)
            .Append(",\"fog\":").Append(gameLoop.FogOfWarEnabled ? "true" : "false")
            .Append(",\"dodge\":").Append(telegraphs != null ? "true" : "false")
            .Append(",\"maxDive\":").Append(maxDive)
            .Append(",\"unit\":").Append(Slot(unit))
            .Append(",\"action\":").Append(action)
            .Append(",\"revising\":").Append(revising ? "true" : "false");
        AppendCells(json, "path", path);
        AppendCells(json, "claimed", claimed);
        AppendCells(json, "walls", GameLoop.wallLayout);
        AppendCells(json, "smoke", gameLoop.ActiveSmokeCells);
        AppendCells(json, "visible", visibleCells);
        AppendCells(json, "alerted", alertedCells ?? new HashSet<Vector2Int>());

        json.Append(",\"units\":[");
        IEnumerable<GameObject> everyone = GameLoop
            .GetTeamUnits(teamIndex)
            .Concat(enemyRoster)
            .Where(member => member != null);
        json.Append(string.Join(",", everyone.Select(member =>
        {
            UnitData data = member.GetComponent<Movement>().unitData;
            Vector3 position = member.transform.position / GridSystem.CellSize;
            return "{\"team\":" + GameLoop.GetTeamIndex(member)
                + ",\"slot\":" + Slot(member)
                + ",\"type\":\"" + data.name.Replace(" (Clone)", "") + "\""
                + ",\"x\":" + Number(position.x)
                + ",\"y\":" + Number(position.z)
                + ",\"hp\":" + Number(member.GetComponent<Health>().CurrentHealth)
                + ",\"alive\":" + (IsLiving(member) ? "true" : "false")
                + ",\"cooldown\":" + Cooldown(member).ToString(CultureInfo.InvariantCulture) + "}";
        })));
        json.Append(']');

        json.Append(",\"memory\":[");
        json.Append(string.Join(",", lastKnown.Values.Select(sighting =>
            "{\"x\":" + sighting.Cell.x + ",\"y\":" + sighting.Cell.y + ",\"type\":" + sighting.Type
            + ",\"hp\":" + Number(sighting.HpFraction) + ",\"round\":" + sighting.Round + "}")));
        json.Append(']');

        json.Append(",\"teammates\":[");
        json.Append(string.Join(",", teammates.Select(((GameObject ally, List<Vector2Int> route, Vector2Int? aim) mate) =>
        {
            StringBuilder entry = new();
            entry.Append("{\"slot\":").Append(Slot(mate.ally))
                .Append(",\"target\":").Append(mate.aim is Vector2Int aim ? "[" + aim.x + "," + aim.y + "]" : "null");
            AppendCells(entry, "path", mate.route);
            return entry.Append('}').ToString();
        })));
        json.Append(']');

        json.Append(",\"telegraphs\":[");
        json.Append(string.Join(",", (telegraphs ?? new List<Telegraph>()).Select(telegraph =>
        {
            StringBuilder entry = new();
            entry.Append("{\"team\":").Append(GameLoop.GetTeamIndex(telegraph.Caster))
                .Append(",\"slot\":").Append(Slot(telegraph.Caster))
                .Append(",\"tx\":").Append(telegraph.Target.x)
                .Append(",\"ty\":").Append(telegraph.Target.y);
            AppendCells(entry, "cells", telegraph.Cells);
            return entry.Append('}').ToString();
        })));
        json.Append(']');

        json.Append(",\"planes\":[");
        json.Append(string.Join(",", Enumerable.Range(0, planes.Length)
            .Where(i => planes[i] != 0f)
            .Select(i => "[" + i + "," + Number(planes[i]) + "]")));
        json.Append("],\"scalars\":[").Append(string.Join(",", scalars.Select(Number))).Append(']');
        json.Append(",\"mask\":[").Append(string.Join(",", Enumerable.Range(0, mask.Length).Where(i => mask[i]))).Append("]}");
        DevDecisionRecorded(json.ToString());
    }

    private static void AppendCells(StringBuilder json, string key, IEnumerable<Vector2Int> cells)
    {
        json.Append(",\"").Append(key).Append("\":[")
            .Append(string.Join(",", cells.Select(cell => "[" + cell.x + "," + cell.y + "]")))
            .Append(']');
    }

    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
