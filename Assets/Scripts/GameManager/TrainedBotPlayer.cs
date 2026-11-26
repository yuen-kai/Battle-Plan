using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    private const float LineOvershootCells = 50f / 2.7f;

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
        public readonly bool Friendly;
        public readonly Vector2Int Target;
        public readonly List<Vector2Int> Cells;

        public Telegraph(bool friendly, Vector2Int target, List<Vector2Int> cells)
        {
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
    private readonly float[] planes = new float[TrainedBotNetwork.Planes * Cells];
    private readonly float[] canonicalPlanes = new float[TrainedBotNetwork.Planes * Cells];
    private readonly float[] scalars = new float[TrainedBotNetwork.Scalars];
    private readonly bool[] mask = new bool[TrainedBotNetwork.Actions];
    private readonly bool[] canonicalMask = new bool[TrainedBotNetwork.Actions];
    private GameObject[] enemyRoster;
    private HashSet<Vector2Int> visibleCells = new();

    public TrainedBotPlayer(GameLoop gameLoop, int teamIndex)
    {
        this.gameLoop = gameLoop;
        this.teamIndex = teamIndex;
    }

    private int EnemyTeamIndex => GameLoop.GetEnemyTeamIndex(teamIndex);

    public PathsDict CreatePlanningContribution(int roundNumber, out bool usedAbility)
    {
        int completedRounds = roundNumber - 1;
        enemyRoster ??= GameLoop.GetTeamUnits(EnemyTeamIndex).OrderBy(Slot).ToArray();
        visibleCells = gameLoop.GetObservableCellsForTeam(teamIndex);
        RecordSightings(completedRounds);
        UpdateMemory();

        PathsDict plans = new();
        HashSet<Vector2Int> claimed = new();
        usedAbility = false;
        foreach (GameObject unit in OwnLivingUnits())
        {
            UnitData data = unit.GetComponent<Movement>().unitData;
            Vector2Int start = Cell(unit);
            List<Vector2Int> path = new() { start };
            List<Vector2Int> targets = AbilityTargets(unit, data, start);
            while (true)
            {
                FillPlanningMask(path, data.moveDist, claimed, targets);
                Encode(unit, path, claimed, completedRounds, null, null, 0);
                int action = Decide();
                if (action >= MoveActions)
                {
                    Vector2Int target = ActionCell(action);
                    plans[unit] = data.selectAbilitySquare
                        ? (true, new List<Vector3> { World(start), World(target) })
                        : (true, new List<Vector3> { World(start) });
                    claimed.Add(CasterEndCell(data, start, target));
                    usedAbility = true;
                    break;
                }
                if (action != Stay)
                {
                    path.Add(path[^1] + Steps[action]);
                    if (path.Count - 1 < data.moveDist)
                        continue;
                }
                claimed.Add(path[^1]);
                plans[unit] = (false, path.Select(World).ToList());
                break;
            }
        }
        return plans;
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
                Encode(unit, path, noClaims, gameLoop.RoundNumber, telegraphs, alertedCells, maxDiveRange);
                int action = Decide();
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
        List<Vector2Int> cells = data.abilityName switch
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
        return new Telegraph(GameLoop.GetTeamIndex(caster) == teamIndex, target, cells);
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

    private static List<Vector2Int> LineCells(Vector2Int start, Vector2Int target)
    {
        Vector2 origin = start;
        Vector2 delta = target - start;
        float distance = delta.magnitude;
        if (distance <= 1e-6f)
            return new List<Vector2Int>();

        Vector2 direction = delta / distance;
        float reach = distance + LineOvershootCells;
        for (float t = 0f; t < reach; t += 0.05f)
        {
            Vector2Int cell = Vector2Int.RoundToInt(origin + direction * t);
            if (GameLoop.wallLayout.Contains(cell))
            {
                reach = t;
                break;
            }
        }

        Vector2 end = origin + direction * reach;
        List<Vector2Int> cells = new();
        for (int x = 0; x < Columns; x++)
        {
            for (int y = 0; y < Rows; y++)
            {
                if (SegmentDistance(origin, end, new Vector2(x, y)) <= 0.5f)
                    cells.Add(new Vector2Int(x, y));
            }
        }
        return cells;
    }

    private static float SegmentDistance(Vector2 a, Vector2 b, Vector2 point)
    {
        Vector2 segment = b - a;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= 1e-12f)
            return float.PositiveInfinity;
        float t = Vector2.Dot(point - a, segment) / lengthSquared;
        if (t < 0f)
            return float.PositiveInfinity;
        return Vector2.Distance(point, a + Mathf.Min(1f, t) * segment);
    }

    private void Encode(
        GameObject unit,
        List<Vector2Int> path,
        HashSet<Vector2Int> claimed,
        int round,
        List<Telegraph> telegraphs,
        HashSet<Vector2Int> alertedCells,
        int maxDive
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
    }

    private int Decide()
    {
        if (teamIndex != 1)
            return network.Evaluate(planes, scalars, mask);

        for (int plane = 0; plane < TrainedBotNetwork.Planes; plane++)
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
}
