using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public readonly struct BotEnemySighting
{
    public readonly ulong EnemyId;
    public readonly Vector2Int Cell;

    public BotEnemySighting(ulong enemyId, Vector2Int cell)
    {
        EnemyId = enemyId;
        Cell = cell;
    }
}

public readonly struct BotDodgeThreat
{
    public readonly bool IsLine;
    public readonly Vector2Int Origin;
    public readonly Vector2Int Target;
    public readonly float Radius;

    public BotDodgeThreat(bool isLine, Vector2Int origin, Vector2Int target, float radius)
    {
        IsLine = isLine;
        Origin = origin;
        Target = target;
        Radius = Mathf.Max(0f, radius);
    }
}

public readonly struct BotSmokeAlly
{
    public readonly Vector2Int Cell;
    public readonly float ShotRange;

    public BotSmokeAlly(Vector2Int cell, float shotRange)
    {
        Cell = cell;
        ShotRange = Mathf.Max(0f, shotRange);
    }
}

/// <summary>
/// A deliberately narrow information boundary for bot decisions. Callers may submit only
/// currently visible sightings. Hidden current positions never enter this object, so targets
/// remain at their last legitimately observed cells until those cells are seen empty.
/// </summary>
public sealed class BotKnowledge
{
    private readonly Dictionary<ulong, Vector2Int> lastKnownCells = new();

    public IReadOnlyDictionary<ulong, Vector2Int> LastKnownCells => lastKnownCells;

    public void Update(
        ISet<Vector2Int> currentlyVisibleCells,
        IEnumerable<BotEnemySighting> visibleSightings
    )
    {
        HashSet<ulong> seenIds = new();
        foreach (
            BotEnemySighting sighting in visibleSightings ?? Enumerable.Empty<BotEnemySighting>()
        )
        {
            seenIds.Add(sighting.EnemyId);
            lastKnownCells[sighting.EnemyId] = sighting.Cell;
        }

        if (currentlyVisibleCells == null)
            return;

        foreach (
            ulong staleId in lastKnownCells
                .Where(entry =>
                    currentlyVisibleCells.Contains(entry.Value) && !seenIds.Contains(entry.Key)
                )
                .Select(entry => entry.Key)
                .ToList()
        )
        {
            lastKnownCells.Remove(staleId);
        }
    }

    public List<Vector2Int> GetTargetCells()
    {
        return lastKnownCells
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Value)
            .Distinct()
            .ToList();
    }
}

/// <summary>
/// DEV: opt-in "hold position" override so specific bot-controlled units — or, registered before
/// their GameObjects even exist, an entire bot team — never plan a move or an ability and never
/// queue a dodge dive, regardless of what TryChooseAbility/BuildMovementPath would otherwise choose.
/// Built for the sandbox (see <c>SandboxSession.cs</c>), where the designer plans the opposing crew
/// by hand and the bot must never choose anything for it.
///
/// Freezing only touches planning/dodge DECISIONS. Health, damage application, on-hit reactions,
/// and death all still run through the normal Health/Unit pipeline untouched — a frozen unit still
/// takes damage and can die exactly like any other unit; it simply never chooses to move, attack, or
/// evade on its own.
///
/// The team-level flag exists alongside the per-unit set because a per-GameObject freeze can only be
/// registered once a unit's GameObject exists (after it spawns), which is a frame after BotPlayer
/// plans the very first round of a freshly started match — too late to guarantee round 1 is inert.
/// Setting the team flag before the match even starts (before StartHost()) has no such race.
/// </summary>
public static class BotFrozenUnits
{
    private static readonly HashSet<GameObject> frozenUnits = new();
    private static readonly HashSet<int> frozenTeams = new();

    public static void SetFrozen(GameObject unit, bool isFrozen)
    {
        if (unit == null)
            return;
        if (isFrozen)
            frozenUnits.Add(unit);
        else
            frozenUnits.Remove(unit);
    }

    public static void SetTeamFrozen(int teamIndex, bool isFrozen)
    {
        if (isFrozen)
            frozenTeams.Add(teamIndex);
        else
            frozenTeams.Remove(teamIndex);
    }

    public static bool IsFrozen(GameObject unit, int teamIndex)
    {
        return (unit != null && frozenUnits.Contains(unit)) || frozenTeams.Contains(teamIndex);
    }

    /// <summary>Clears every override. Call between unrelated dev sessions to avoid stale state.</summary>
    public static void ClearAll()
    {
        frozenUnits.Clear();
        frozenTeams.Clear();
    }
}

/// <summary>
/// Server-only deterministic opponent. Planning consumes only the bot's fog observation and
/// its own last-known memory. Dodge planning consumes public ability telegraphs, never the
/// opponent's submitted movement plans.
/// </summary>
public sealed class BotPlayer
{
    private readonly GameLoop gameLoop;
    private readonly BotKnowledge knowledge = new();
    private readonly HashSet<Vector2Int> visibleEnemyCells = new();
    private readonly HashSet<Vector2Int> observedCells = new();
    private readonly Dictionary<Vector2Int, int> lastObservedEpoch = new();
    private int observationEpoch;

    public int TeamIndex { get; }
    public BotKnowledge Knowledge => knowledge;
    public int PlanningContributionCount { get; private set; }
    public int DodgeContributionCount { get; private set; }
    public int AbilityContributionCount { get; private set; }
    public bool LastPlanUsedAbility { get; private set; }

    public BotPlayer(GameLoop gameLoop, int teamIndex)
    {
        this.gameLoop = gameLoop ?? throw new ArgumentNullException(nameof(gameLoop));
        TeamIndex = teamIndex;
    }

    public PathsDict CreatePlanningContribution(int roundNumber)
    {
        PlanningContributionCount++;
        LastPlanUsedAbility = false;
        RefreshObservation();

        GameObject[] botUnits = GameLoop
            .GetTeamUnits(TeamIndex)
            .Where(IsLiving)
            .OrderBy(GetStableUnitId)
            .ToArray();
        PathsDict plans = new();
        if (botUnits.Length == 0)
            return plans;

        GameObject abilityUnit = null;
        Vector2Int abilityTarget = default;
        bool abilityNeedsTarget = false;
        // Frozen units never volunteer for the round's single ability slot, so a sandbox target
        // dummy can never spend it out from under a unit that is actually free to act.
        GameObject[] abilityCandidateUnits = botUnits
            .Where(unit => !BotFrozenUnits.IsFrozen(unit, TeamIndex))
            .ToArray();
        TryChooseAbility(
            abilityCandidateUnits,
            roundNumber,
            out abilityUnit,
            out abilityTarget,
            out abilityNeedsTarget
        );
        LastPlanUsedAbility = abilityUnit != null;
        if (LastPlanUsedAbility)
            AbilityContributionCount++;

        HashSet<Vector2Int> occupiedCells = new(botUnits.Select(GetCell).Concat(visibleEnemyCells));
        List<Vector2Int> targets = GetStrategicTargets(
            gameLoop.Options.gameMode,
            knowledge.GetTargetCells(),
            TeamIndex
        );

        ResolveEscortRoles(
            out GameObject ownPresident,
            out List<Vector2Int> presidentTargets,
            out List<Vector2Int> escortCrewTargets
        );

        // The president plans first so his crew can screen the cell he commits to.
        if (ownPresident != null)
            botUnits = botUnits.OrderByDescending(unit => unit == ownPresident).ToArray();

        foreach (GameObject unit in botUnits)
        {
            Vector3 startWorld = GridSystem.GetNearestGridCell(unit);
            if (BotFrozenUnits.IsFrozen(unit, TeamIndex))
            {
                // Frozen means it never moves and never spends the ability slot; whether it shoots
                // back is a separate question this does not answer, so nothing here touches
                // Shooting. A frozen crew still returns fire, which is what a sandbox opponent
                // standing on a square is expected to do.
                plans[unit] = (false, new List<Vector3> { startWorld });
                continue;
            }
            if (unit == abilityUnit)
            {
                plans[unit] = abilityNeedsTarget
                    ? (
                        true,
                        new List<Vector3> { startWorld, GameLoop.gridCoordToWorld(abilityTarget) }
                    )
                    : (true, new List<Vector3> { startWorld });
                continue;
            }

            Vector2Int start = GetCell(unit);
            occupiedCells.Remove(start);
            List<Vector2Int> cellPath = BuildMovementPath(
                start,
                unit == ownPresident ? presidentTargets : escortCrewTargets ?? targets,
                visibleEnemyCells,
                occupiedCells,
                unit.GetComponent<Movement>().unitData.moveDist,
                observedCells,
                lastObservedEpoch
            );
            if (unit == ownPresident && cellPath.Count > 0)
                escortCrewTargets = BuildEscortScreenTargets(cellPath[^1], TeamIndex);
            // Movement speeds vary by unit, so reserving only matching timesteps is unsafe.
            // Reserve every cell in an earlier unit's route to prevent crossings and edge swaps.
            ReservePathCells(occupiedCells, cellPath);

            plans[unit] = (false, cellPath.Select(GameLoop.gridCoordToWorld).ToList());
        }

        return plans;
    }

    public const int EscortScreenLeadCells = 2;
    public const int EscortInterceptLeadCells = 1;
    public const int EscortRoleSpreadCells = 2;

    private void ResolveEscortRoles(
        out GameObject ownPresident,
        out List<Vector2Int> presidentTargets,
        out List<Vector2Int> escortCrewTargets
    )
    {
        ownPresident = null;
        presidentTargets = null;
        escortCrewTargets = null;
        if (!gameLoop.Options.IsEscort)
            return;

        if (EscortSeries.IsEscortingTeam(TeamIndex))
        {
            GameObject president = gameLoop.GetPresident(TeamIndex);
            if (president == null || !IsLiving(president))
                return;

            ownPresident = president;
            presidentTargets = EscortSeries.ExtractionCellsFor(TeamIndex).ToList();
            escortCrewTargets = BuildEscortScreenTargets(GetCell(president), TeamIndex);
            return;
        }

        int escortingTeamIndex = GameLoop.GetEnemyTeamIndex(TeamIndex);
        escortCrewTargets = TryGetKnownEnemyPresidentCell(escortingTeamIndex, out Vector2Int seenAt)
            ? BuildEscortInterceptTargets(seenAt, escortingTeamIndex)
            : EscortSeries.ExtractionCellsFor(escortingTeamIndex).ToList();
    }

    private bool TryGetKnownEnemyPresidentCell(int escortingTeamIndex, out Vector2Int cell)
    {
        cell = default;
        GameObject president = gameLoop.GetPresident(escortingTeamIndex);
        return president != null
            && knowledge.LastKnownCells.TryGetValue(GetStableUnitId(president), out cell);
    }

    public static List<Vector2Int> BuildEscortScreenTargets(
        Vector2Int presidentCell,
        int escortingTeamIndex
    )
    {
        return OpenFootprint(
            StepTowardExtraction(presidentCell, escortingTeamIndex, EscortScreenLeadCells),
            EscortRoleSpreadCells
        );
    }

    public static List<Vector2Int> BuildEscortInterceptTargets(
        Vector2Int presidentCell,
        int escortingTeamIndex
    )
    {
        int presidentSteps = EscortSeries.StepsToExtraction(presidentCell, escortingTeamIndex);
        List<Vector2Int> between = OpenFootprint(
                StepTowardExtraction(presidentCell, escortingTeamIndex, EscortInterceptLeadCells),
                EscortRoleSpreadCells
            )
            .Where(cell =>
                EscortSeries.StepsToExtraction(cell, escortingTeamIndex) <= presidentSteps
            )
            .ToList();
        return between.Count > 0 ? between : new List<Vector2Int> { presidentCell };
    }

    private static Vector2Int StepTowardExtraction(
        Vector2Int from,
        int escortingTeamIndex,
        int steps
    )
    {
        Vector2Int goal = NearestExtractionCell(from, escortingTeamIndex);
        int distance = GridSystem.GetGridDistance(from, goal);
        if (distance == 0 || steps <= 0)
            return from;

        float progress = Mathf.Min(1f, steps / (float)distance);
        return new Vector2Int(
            Mathf.RoundToInt(Mathf.Lerp(from.x, goal.x, progress)),
            Mathf.RoundToInt(Mathf.Lerp(from.y, goal.y, progress))
        );
    }

    private static Vector2Int NearestExtractionCell(Vector2Int from, int escortingTeamIndex)
    {
        Vector2Int nearest = from;
        int fewest = int.MaxValue;
        foreach (
            Vector2Int cell in EscortSeries
                .ExtractionCellsFor(escortingTeamIndex)
                .OrderBy(cell => cell.x)
                .ThenBy(cell => cell.y)
        )
        {
            int steps = GridSystem.GetGridDistance(from, cell);
            if (steps >= fewest)
                continue;
            fewest = steps;
            nearest = cell;
        }
        return nearest;
    }

    private static List<Vector2Int> OpenFootprint(Vector2Int centre, int radius)
    {
        HashSet<Vector2Int> walls = GameLoop.wallLayout;
        return GridSystem
            .GetSquareFootprint(centre, radius)
            .Where(cell => GridSystem.IsCellInBounds(cell) && !walls.Contains(cell))
            .ToList();
    }

    /// <summary>
    /// Where the crew is trying to be, as opposed to who it is trying to shoot. An objective mode
    /// names cells; Elimination names whichever enemies have been seen.
    /// </summary>
    public static List<Vector2Int> GetStrategicTargets(
        GameMode gameMode,
        IEnumerable<Vector2Int> knownEnemyCells,
        int botTeamIndex = -1
    )
    {
        IEnumerable<Vector2Int> targets;
        if (gameMode == GameMode.KingOfTheHill)
            targets = GameLoop.KingOfTheHillCells;
        else if (gameMode == GameMode.EscortThePresident && botTeamIndex >= 0)
            targets = GetEscortObjectiveCells(botTeamIndex);
        else
            targets = knownEnemyCells ?? Enumerable.Empty<Vector2Int>();
        return targets.Distinct().OrderBy(cell => cell.x).ThenBy(cell => cell.y).ToList();
    }

    public static bool WouldAbandonHill(
        GameMode gameMode,
        Vector2Int start,
        Vector2Int destination
    )
    {
        return gameMode == GameMode.KingOfTheHill
            && GameLoop.KingOfTheHillCells.Contains(start)
            && !GameLoop.KingOfTheHillCells.Contains(destination);
    }

    private static IEnumerable<Vector2Int> GetEscortObjectiveCells(int botTeamIndex)
    {
        return EscortSeries.IsEscortingTeam(botTeamIndex)
            ? EscortSeries.ExtractionCellsFor(botTeamIndex)
            : EscortSeries.ExtractionCellsFor(GameLoop.GetEnemyTeamIndex(botTeamIndex));
    }

    public PathsDict CreateDodgeContribution(
        IReadOnlyCollection<GameObject> alertedUnits,
        IReadOnlyList<(GameObject unit, Vector3 square, UnitData data)> activations,
        int maxDiveRange
    )
    {
        DodgeContributionCount++;
        PathsDict dives = new();
        if (alertedUnits == null || alertedUnits.Count == 0)
            return dives;

        List<BotDodgeThreat> threats = activations
            .Where(activation =>
                activation.unit != null && GameLoop.GetTeamIndex(activation.unit) != TeamIndex
            )
            .Select(activation => new BotDodgeThreat(
                activation.data.responseDistLine,
                activation.data.responseDistLine
                    ? GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(activation.unit))
                    : GridSystem.ConvertToGridCoords(activation.square),
                GridSystem.ConvertToGridCoords(activation.square),
                activation.data.responseDistLine
                    ? activation.data.responseRange
                    : activation.data.abilityRadius
            ))
            .ToList();

        HashSet<Vector2Int> occupied = new(
            GameLoop.GetTeamUnits(TeamIndex).Where(IsLiving).Select(GetCell)
        );
        occupied.UnionWith(visibleEnemyCells);
        List<Vector2Int> knownEnemies = knowledge.GetTargetCells();

        // Frozen units decline every dodge window too — a target dummy that dove clear of a hit
        // would defeat the point of standing still for the test unit's ability to land on it.
        foreach (
            GameObject unit in alertedUnits
                .Where(IsLiving)
                .Where(unit => !BotFrozenUnits.IsFrozen(unit, TeamIndex))
                .OrderBy(GetStableUnitId)
        )
        {
            Vector2Int start = GetCell(unit);
            occupied.Remove(start);
            List<Vector2Int> reachable = GridSystem.GetReachableCells(
                start,
                maxDiveRange,
                occupied
            );
            Vector2Int safest = ChooseSafestDodgeCell(start, reachable, threats, knownEnemies);
            List<Vector2Int> path =
                GridSystem.FindPath(start, safest, occupied) ?? new List<Vector2Int> { start };
            if (path.Count > maxDiveRange + 1)
                path = path.GetRange(0, maxDiveRange + 1);

            ReservePathCells(occupied, path);
            dives[unit] = (false, path.Select(GameLoop.gridCoordToWorld).ToList());
        }

        return dives;
    }

    private void RefreshObservation()
    {
        observationEpoch++;
        HashSet<Vector2Int> visibleCells = gameLoop.GetObservableCellsForTeam(TeamIndex);
        List<BotEnemySighting> sightings = gameLoop.GetVisibleEnemySightingsForTeam(
            TeamIndex,
            visibleCells
        );
        if (visibleCells != null)
        {
            foreach (Vector2Int cell in visibleCells.Where(GridSystem.IsCellInBounds))
            {
                observedCells.Add(cell);
                lastObservedEpoch[cell] = observationEpoch;
            }
        }

        visibleEnemyCells.Clear();
        visibleEnemyCells.UnionWith(sightings.Select(sighting => sighting.Cell));

        knowledge.Update(visibleCells, sightings);
    }

    private void TryChooseAbility(
        IReadOnlyList<GameObject> botUnits,
        int roundNumber,
        out GameObject selectedUnit,
        out Vector2Int target,
        out bool needsTarget
    )
    {
        selectedUnit = null;
        target = default;
        needsTarget = false;

        List<(GameObject unit, int index, UnitData data)> candidates = botUnits
            .Select((unit, index) => (unit, index, data: unit.GetComponent<Movement>()?.unitData))
            .Where(candidate =>
                candidate.data != null
                && candidate.unit.GetComponent<Ability>() != null
                && candidate.unit.GetComponent<Unit>()?.CanUseAbility == true
            )
            .OrderBy(candidate =>
                candidate.data.selectAbilitySquare ? (candidate.data.abilityRadius > 0f ? 0 : 1) : 2
            )
            .ThenBy(candidate =>
                (candidate.index - roundNumber % botUnits.Count + botUnits.Count) % botUnits.Count
            )
            .ToList();

        foreach (var candidate in candidates)
        {
            Vector2Int start = GetCell(candidate.unit);
            if (candidate.unit.GetComponent<Smoke>() != null)
            {
                if (!candidate.data.selectAbilitySquare || candidate.data.selectAbilityDirection)
                    continue;

                List<BotSmokeAlly> allies = botUnits
                    .Select(unit =>
                    {
                        UnitData allyData = unit.GetComponent<Movement>()?.unitData;
                        float shotRange =
                            unit.GetComponent<Shooting>() != null && allyData != null
                                ? allyData.targetRange
                                : 0f;
                        return new BotSmokeAlly(GetCell(unit), shotRange);
                    })
                    .ToList();
                if (
                    !TryChooseSmokeCenter(
                        start,
                        candidate.data.abilitySquareRange,
                        allies,
                        knowledge.GetTargetCells(),
                        visibleEnemyCells,
                        gameLoop.ActiveSmokeCells,
                        out Vector2Int smokeTarget,
                        out _
                    )
                )
                {
                    continue;
                }

                selectedUnit = candidate.unit;
                target = smokeTarget;
                needsTarget = true;
                return;
            }

            if (visibleEnemyCells.Count == 0)
                continue;

            if (candidate.data.selectAbilityDirection)
            {
                foreach (
                    Vector2Int visibleTarget in visibleEnemyCells
                        .OrderBy(cell => GridDistance(start, cell))
                        .ThenBy(cell => cell.x)
                        .ThenBy(cell => cell.y)
                )
                {
                    Vector2Int delta = visibleTarget - start;
                    int directionalDistance = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                    int engageRange =
                        candidate.data.abilityFixedDistance
                        + Mathf.CeilToInt(candidate.data.targetRange);
                    if (directionalDistance > engageRange)
                        continue;

                    Vector2Int direction = new(Math.Sign(delta.x), Math.Sign(delta.y));
                    Vector2Int directionTarget = start + direction;
                    Vector2Int destination = GridSystem.GetDirectionalDestination(
                        start,
                        direction,
                        candidate.data.abilityFixedDistance,
                        GameLoop.wallLayout
                    );
                    if (
                        direction == Vector2Int.zero
                        || !GridSystem.IsCellInBounds(directionTarget)
                        || destination == start
                        || WouldAbandonHill(gameLoop.Options.gameMode, start, destination)
                        || GridDistance(destination, visibleTarget)
                            > Mathf.CeilToInt(candidate.data.targetRange)
                        || !GridSystem.HasGridLineOfSight(destination, visibleTarget)
                    )
                    {
                        continue;
                    }

                    selectedUnit = candidate.unit;
                    target = directionTarget;
                    needsTarget = true;
                    return;
                }
                continue;
            }

            if (!candidate.data.selectAbilitySquare)
            {
                bool enemyClose = visibleEnemyCells.Any(cell =>
                    GridDistance(start, cell) <= Mathf.CeilToInt(candidate.data.targetRange)
                );
                if (!enemyClose)
                    continue;

                selectedUnit = candidate.unit;
                needsTarget = false;
                return;
            }

            foreach (
                Vector2Int visibleTarget in visibleEnemyCells
                    .OrderBy(cell => GridDistance(start, cell))
                    .ThenBy(cell => cell.x)
                    .ThenBy(cell => cell.y)
            )
            {
                if (GridDistance(start, visibleTarget) > candidate.data.abilitySquareRange)
                    continue;
                if (!candidate.data.responseDistLine && GameLoop.wallLayout.Contains(visibleTarget))
                {
                    continue;
                }
                if (
                    candidate.unit.GetComponent<Pogo>() != null
                    && WouldAbandonHill(gameLoop.Options.gameMode, start, visibleTarget)
                )
                {
                    continue;
                }

                selectedUnit = candidate.unit;
                target = visibleTarget;
                needsTarget = true;
                return;
            }
        }
    }

    /// <summary>
    /// Selects a legal 3x3 Smoke center using only allied state, visible enemy cells, and
    /// fog-bounded last-known enemy cells. Remembered enemies contribute possible incoming lanes,
    /// but only currently visible enemies can count as confirmed friendly shot opportunities.
    /// </summary>
    public static bool TryChooseSmokeCenter(
        Vector2Int casterCell,
        int castRange,
        IEnumerable<BotSmokeAlly> allies,
        IEnumerable<Vector2Int> knownEnemyCells,
        IEnumerable<Vector2Int> currentlyVisibleEnemyCells,
        IEnumerable<Vector2Int> activeSmokeCells,
        out Vector2Int selectedCenter,
        out int selectedScore
    )
    {
        List<BotSmokeAlly> allyList = (allies ?? Enumerable.Empty<BotSmokeAlly>())
            .Where(ally => GridSystem.IsCellInBounds(ally.Cell))
            .OrderBy(ally => ally.Cell.x)
            .ThenBy(ally => ally.Cell.y)
            .ThenBy(ally => ally.ShotRange)
            .ToList();
        List<Vector2Int> knownEnemies = (
            knownEnemyCells ?? Enumerable.Empty<Vector2Int>()
        )
            .Where(GridSystem.IsCellInBounds)
            .Distinct()
            .OrderBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .ToList();
        List<Vector2Int> visibleEnemies = (
            currentlyVisibleEnemyCells ?? Enumerable.Empty<Vector2Int>()
        )
            .Where(GridSystem.IsCellInBounds)
            .Distinct()
            .OrderBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .ToList();
        HashSet<Vector2Int> existingSmoke = new(
            (activeSmokeCells ?? Enumerable.Empty<Vector2Int>())
                .Where(GridSystem.IsCellInBounds)
        );

        selectedCenter = default;
        selectedScore = 0;
        if (
            !GridSystem.IsCellInBounds(casterCell)
            || allyList.Count == 0
            || knownEnemies.Count == 0
        )
        {
            return false;
        }

        int legalCastRange = Mathf.Max(0, castRange);
        for (int column = 0; column < GridSystem.ColumnCount; column++)
        {
            for (int row = 0; row < GridSystem.RowCount; row++)
            {
                Vector2Int center = new(column, row);
                if (
                    GridDistance(casterCell, center) > legalCastRange
                    || !GridSystem.IsSquareFootprintInBounds(center, Smoke.FootprintRadius)
                    || GameLoop.wallLayout.Contains(center)
                )
                {
                    continue;
                }

                int score = ScoreSmokeCenter(
                    center,
                    allyList,
                    knownEnemies,
                    visibleEnemies,
                    existingSmoke
                );
                if (
                    score <= 0
                    || score < selectedScore
                    || (
                        score == selectedScore
                        && selectedScore > 0
                        && CompareCells(center, selectedCenter) >= 0
                    )
                )
                {
                    continue;
                }

                selectedCenter = center;
                selectedScore = score;
            }
        }

        return selectedScore > 0;
    }

    private static int ScoreSmokeCenter(
        Vector2Int center,
        IReadOnlyList<BotSmokeAlly> allies,
        IReadOnlyList<Vector2Int> knownEnemyCells,
        IReadOnlyList<Vector2Int> visibleEnemyCells,
        ISet<Vector2Int> activeSmokeCells
    )
    {
        HashSet<Vector2Int> footprint = new(
            GridSystem.GetSquareFootprint(center, Smoke.FootprintRadius)
        );
        int score = 0;

        foreach (Vector2Int enemyCell in knownEnemyCells)
        {
            foreach (BotSmokeAlly ally in allies)
            {
                if (
                    IsOpenShotLine(enemyCell, ally.Cell, activeSmokeCells)
                    && GridSystem.DoesCellSegmentCrossCells(enemyCell, ally.Cell, footprint)
                )
                {
                    score++;
                }
            }
        }

        foreach (Vector2Int enemyCell in visibleEnemyCells)
        {
            foreach (BotSmokeAlly ally in allies)
            {
                if (
                    IsWithinShotRange(ally, enemyCell)
                    && IsOpenShotLine(ally.Cell, enemyCell, activeSmokeCells)
                    && GridSystem.DoesCellSegmentCrossCells(ally.Cell, enemyCell, footprint)
                )
                {
                    score--;
                }
            }
        }

        return score;
    }

    private static bool IsOpenShotLine(
        Vector2Int start,
        Vector2Int end,
        ISet<Vector2Int> activeSmokeCells
    )
    {
        return start != end
            && GridSystem.HasGridLineOfSight(start, end)
            && !GridSystem.DoesCellSegmentCrossCells(start, end, activeSmokeCells);
    }

    private static bool IsWithinShotRange(BotSmokeAlly ally, Vector2Int target)
    {
        Vector2Int delta = target - ally.Cell;
        float squaredDistance = delta.x * delta.x + delta.y * delta.y;
        return squaredDistance <= ally.ShotRange * ally.ShotRange + 0.0001f;
    }

    /// <summary>
    /// Prioritizes visible or remembered enemy targets. When none are reachable, explores toward
    /// the nearest unobserved frontier using only observation history and public board geometry.
    /// </summary>
    public static List<Vector2Int> BuildMovementPath(
        Vector2Int start,
        IEnumerable<Vector2Int> targetCells,
        ISet<Vector2Int> currentlyVisibleEnemyCells,
        ISet<Vector2Int> blockedCells,
        int maxSteps,
        IEnumerable<Vector2Int> observedCells = null,
        IReadOnlyDictionary<Vector2Int, int> observationEpochs = null
    )
    {
        List<Vector2Int> bestPath = null;
        Vector2Int bestTarget = default;
        bool stopShortOfBestTarget = false;
        foreach (
            Vector2Int candidate in (targetCells ?? Enumerable.Empty<Vector2Int>())
                .Distinct()
                .OrderBy(cell => cell.x)
                .ThenBy(cell => cell.y)
        )
        {
            bool isVisibleEnemy =
                currentlyVisibleEnemyCells != null
                && currentlyVisibleEnemyCells.Contains(candidate);
            if (
                candidate != start
                && blockedCells != null
                && blockedCells.Contains(candidate)
                && !isVisibleEnemy
            )
            {
                continue;
            }

            List<Vector2Int> path = GridSystem.FindPath(start, candidate, blockedCells);
            if (
                path == null
                || (
                    bestPath != null
                    && (
                        path.Count > bestPath.Count
                        || (
                            path.Count == bestPath.Count && CompareCells(candidate, bestTarget) >= 0
                        )
                    )
                )
            )
            {
                continue;
            }

            bestPath = path;
            bestTarget = candidate;
            stopShortOfBestTarget = isVisibleEnemy;
        }

        if (bestPath == null)
        {
            bestPath = FindExplorationPath(start, observedCells, observationEpochs, blockedCells);
            stopShortOfBestTarget = false;
        }

        if (bestPath == null || bestPath.Count == 0)
            return new List<Vector2Int> { start };

        int availableSteps = bestPath.Count - 1;
        if (stopShortOfBestTarget)
            availableSteps = Mathf.Max(0, availableSteps - 1);

        int steps = Mathf.Min(Mathf.Max(0, maxSteps), availableSteps);
        return bestPath.GetRange(0, steps + 1);
    }

    public static void ReservePathCells(
        ISet<Vector2Int> reservedCells,
        IEnumerable<Vector2Int> path
    )
    {
        if (reservedCells == null || path == null)
            return;

        foreach (Vector2Int cell in path)
            reservedCells.Add(cell);
    }

    private static List<Vector2Int> FindExplorationPath(
        Vector2Int start,
        IEnumerable<Vector2Int> observedCells,
        IReadOnlyDictionary<Vector2Int, int> observationEpochs,
        ISet<Vector2Int> blockedCells
    )
    {
        HashSet<Vector2Int> observed = new(
            (observedCells ?? Enumerable.Empty<Vector2Int>()).Where(GridSystem.IsCellInBounds)
        );
        List<Vector2Int> unobserved = new();
        List<Vector2Int> frontier = new();

        for (int column = 0; column < GridSystem.ColumnCount; column++)
        {
            for (int row = 0; row < GridSystem.RowCount; row++)
            {
                Vector2Int candidate = new(column, row);
                if (
                    candidate == start
                    || GameLoop.wallLayout.Contains(candidate)
                    || observed.Contains(candidate)
                    || (blockedCells != null && blockedCells.Contains(candidate))
                )
                {
                    continue;
                }

                unobserved.Add(candidate);
                if (observed.Count == 0 || observed.Any(cell => GridDistance(cell, candidate) == 1))
                {
                    frontier.Add(candidate);
                }
            }
        }

        IReadOnlyList<Vector2Int> candidates;
        bool preferLongestPath = observed.Count == 0;
        bool patrolObservedCells = false;
        if (frontier.Count > 0)
        {
            candidates = frontier;
        }
        else if (unobserved.Count > 0)
        {
            candidates = unobserved;
        }
        else
        {
            // Full historical coverage must not make the bot idle forever. Patrol the least
            // recently observed reachable region so an enemy can be rediscovered after hiding.
            patrolObservedCells = true;
            candidates = observed
                .Where(cell =>
                    cell != start
                    && !GameLoop.wallLayout.Contains(cell)
                    && (blockedCells == null || !blockedCells.Contains(cell))
                )
                .OrderBy(cell =>
                    observationEpochs != null && observationEpochs.TryGetValue(cell, out int epoch)
                        ? epoch
                        : int.MinValue
                )
                .ThenBy(cell => cell.x)
                .ThenBy(cell => cell.y)
                .ToList();
        }
        List<Vector2Int> bestPath = null;
        Vector2Int bestTarget = default;
        int bestEpoch = int.MaxValue;

        foreach (Vector2Int candidate in candidates)
        {
            List<Vector2Int> path = GridSystem.FindPath(start, candidate, blockedCells);
            if (path == null)
                continue;

            int candidateEpoch =
                observationEpochs != null && observationEpochs.TryGetValue(candidate, out int epoch)
                    ? epoch
                    : int.MinValue;
            bool better =
                bestPath == null
                || (patrolObservedCells && candidateEpoch < bestEpoch)
                || (
                    (!patrolObservedCells || candidateEpoch == bestEpoch)
                    && (
                        preferLongestPath
                            ? path.Count > bestPath.Count
                            : path.Count < bestPath.Count
                    )
                )
                || (
                    (!patrolObservedCells || candidateEpoch == bestEpoch)
                    && path.Count == bestPath.Count
                    && CompareCells(candidate, bestTarget) < 0
                );
            if (!better)
                continue;

            bestPath = path;
            bestTarget = candidate;
            bestEpoch = candidateEpoch;
        }

        return bestPath;
    }

    public static Vector2Int ChooseSafestDodgeCell(
        Vector2Int start,
        IEnumerable<Vector2Int> reachableCells,
        IEnumerable<BotDodgeThreat> threats,
        IEnumerable<Vector2Int> knownEnemyCells
    )
    {
        List<BotDodgeThreat> threatList = threats?.ToList() ?? new List<BotDodgeThreat>();
        List<Vector2Int> enemies = knownEnemyCells?.ToList() ?? new List<Vector2Int>();
        Vector2Int best = start;
        float bestThreatClearance = float.NegativeInfinity;
        int bestEnemyDistance = int.MinValue;
        int bestTravelDistance = int.MaxValue;

        foreach (
            Vector2Int candidate in (reachableCells ?? new[] { start })
                .Distinct()
                .OrderBy(cell => cell.x)
                .ThenBy(cell => cell.y)
        )
        {
            float threatClearance =
                threatList.Count == 0
                    ? 0f
                    : threatList.Min(threat => ThreatClearance(candidate, threat));
            int enemyDistance =
                enemies.Count == 0 ? 0 : enemies.Min(enemy => GridDistance(candidate, enemy));
            int travelDistance = GridDistance(start, candidate);

            bool better =
                threatClearance > bestThreatClearance + 0.001f
                || (
                    Mathf.Abs(threatClearance - bestThreatClearance) <= 0.001f
                    && (
                        enemyDistance > bestEnemyDistance
                        || (
                            enemyDistance == bestEnemyDistance
                            && (
                                travelDistance < bestTravelDistance
                                || (
                                    travelDistance == bestTravelDistance
                                    && CompareCells(candidate, best) < 0
                                )
                            )
                        )
                    )
                );
            if (!better)
                continue;

            best = candidate;
            bestThreatClearance = threatClearance;
            bestEnemyDistance = enemyDistance;
            bestTravelDistance = travelDistance;
        }

        return best;
    }

    private static float ThreatClearance(Vector2Int cell, BotDodgeThreat threat)
    {
        if (!threat.IsLine)
            return Vector2.Distance(cell, threat.Target) - threat.Radius;

        Vector2 point = cell;
        Vector2 start = threat.Origin;
        Vector2 end = threat.Target;
        Vector2 line = end - start;
        if (line.sqrMagnitude <= Mathf.Epsilon)
            return Vector2.Distance(point, start) - threat.Radius;

        // Line abilities continue past their selected direction anchor until a wall.
        float projection = Mathf.Max(0f, Vector2.Dot(point - start, line) / line.sqrMagnitude);
        return Vector2.Distance(point, start + line * projection) - threat.Radius;
    }

    private static int GridDistance(Vector2Int left, Vector2Int right)
    {
        return Mathf.Abs(left.x - right.x) + Mathf.Abs(left.y - right.y);
    }

    private static int CompareCells(Vector2Int left, Vector2Int right)
    {
        int xComparison = left.x.CompareTo(right.x);
        return xComparison != 0 ? xComparison : left.y.CompareTo(right.y);
    }

    private static bool IsLiving(GameObject unit)
    {
        if (unit == null || !unit.activeInHierarchy)
            return false;
        Health health = unit.GetComponent<Health>();
        return health == null || health.IsAlive;
    }

    private static Vector2Int GetCell(GameObject unit)
    {
        return GridSystem.ConvertToGridCoords(GridSystem.GetNearestGridCell(unit));
    }

    private static ulong GetStableUnitId(GameObject unit)
    {
        NetworkObject networkObject = unit != null ? unit.GetComponent<NetworkObject>() : null;
        return networkObject != null && networkObject.IsSpawned
            ? networkObject.NetworkObjectId
            : unchecked((ulong)(uint)(unit != null ? unit.GetInstanceID() : 0));
    }
}
