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

public readonly struct BotShooter
{
    public readonly Vector2Int Cell;
    public readonly float ShotRange;
    public readonly float Weight;

    public BotShooter(Vector2Int cell, float shotRange, float weight = 1f)
    {
        Cell = cell;
        ShotRange = Mathf.Max(0f, shotRange);
        Weight = Mathf.Max(0f, weight);
    }
}

/// <summary>
/// The temperament a bot crew is dealt for a match. Rush closes to brawling range and spends its
/// ability eagerly; Skirmish fights from most of its range; Anchor holds the edge of its range,
/// advances in shorter steps, and values screens and shields over damage.
/// </summary>
public enum BotPlaystyle
{
    Rush,
    Skirmish,
    Anchor,
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
/// Server-only opponent. Planning consumes only the bot's fog observation and its own last-known
/// memory, except in Escort the President, where <see cref="IgnoresFog"/> grants full board
/// knowledge so the motorcade is hunted rather than searched for. Dodge planning consumes public
/// ability telegraphs, never the opponent's submitted movement plans.
/// <para>
/// The static helpers are deterministic unless handed a <see cref="System.Random"/>; the live bot
/// always hands them its own, so it picks among near-equal routes and ability uses instead of
/// repeating the same opening every match.
/// </para>
/// </summary>
public sealed class BotPlayer
{
    public const int RandomRouteSlack = 2;
    public const int RandomExplorationSlack = 3;
    public const float SmokeThreatStepAllowance = 1f;
    public const float MinimumSmokeScore = 1f;
    public const float SupportTrailDistance = 2.5f;
    private const float BrawlerRange = 3f;
    private const float AbilityValueNoise = 1f;
    private const float LostShotPenalty = 0.75f;
    private const float DefaultEnemyShotRange = 5f;

    private readonly GameLoop gameLoop;
    private readonly System.Random random;
    private readonly BotKnowledge knowledge = new();
    private readonly HashSet<Vector2Int> visibleEnemyCells = new();
    private readonly Dictionary<Vector2Int, GameObject> visibleEnemyUnits = new();
    private readonly Dictionary<ulong, GameObject> enemyUnitsById = new();
    private readonly HashSet<Vector2Int> observedCells = new();
    private readonly Dictionary<Vector2Int, int> lastObservedEpoch = new();
    private int observationEpoch;

    public int TeamIndex { get; }
    public BotPlaystyle Playstyle { get; }
    public BotKnowledge Knowledge => knowledge;
    public bool IgnoresFog => gameLoop.Options.IsEscort;
    public int PlanningContributionCount { get; private set; }
    public int DodgeContributionCount { get; private set; }
    public int AbilityContributionCount { get; private set; }
    public bool LastPlanUsedAbility { get; private set; }

    public BotPlayer(GameLoop gameLoop, int teamIndex, int? seed = null)
    {
        this.gameLoop = gameLoop ?? throw new ArgumentNullException(nameof(gameLoop));
        TeamIndex = teamIndex;
        random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        Playstyle = (BotPlaystyle)random.Next(Enum.GetValues(typeof(BotPlaystyle)).Length);
        Debug.Log($"[Bot] Team {teamIndex} plays {Playstyle}");
    }

    private float EngageRangeFactor =>
        Playstyle switch
        {
            BotPlaystyle.Rush => 0.55f,
            BotPlaystyle.Skirmish => 0.8f,
            _ => 1f,
        };
    private float AbilityThreshold => Playstyle == BotPlaystyle.Rush ? 1f : 1.5f;
    private float SupportBias => Playstyle == BotPlaystyle.Anchor ? 1.4f : 1f;
    private float DamageBias => Playstyle == BotPlaystyle.Rush ? 1.3f : 1f;
    private float ShortStepChance =>
        Playstyle switch
        {
            BotPlaystyle.Rush => 0f,
            BotPlaystyle.Skirmish => 0.15f,
            _ => 0.35f,
        };

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
            botUnits,
            out abilityUnit,
            out abilityTarget,
            out abilityNeedsTarget
        );
        LastPlanUsedAbility = abilityUnit != null;
        if (LastPlanUsedAbility)
            AbilityContributionCount++;

        HashSet<Vector2Int> occupiedCells = new(botUnits.Select(GetCell).Concat(visibleEnemyCells));
        List<Vector2Int> crewDestinations = new();
        Vector2Int abilityLanding = default;
        bool abilityRelocatesCaster =
            abilityUnit != null
            && abilityNeedsTarget
            && abilityUnit
                .GetComponent<Ability>()
                .TryGetCasterDestination(
                    GameLoop.gridCoordToWorld(abilityTarget),
                    abilityUnit.GetComponent<Movement>().unitData,
                    out abilityLanding
                );
        if (abilityRelocatesCaster)
        {
            occupiedCells.Add(abilityLanding);
            crewDestinations.Add(abilityLanding);
        }

        List<Vector2Int> targets = GetStrategicTargets(
            gameLoop.Options.gameMode,
            knowledge.GetTargetCells(),
            TeamIndex
        );
        Vector2Int? focusTarget = ChooseFocusTarget(botUnits, targets);

        ResolveEscortRoles(
            out GameObject ownPresident,
            out List<Vector2Int> presidentTargets,
            out List<Vector2Int> escortCrewTargets
        );

        // The president plans first so his crew can screen the cell he commits to; screeners plan
        // last so they can settle behind wherever the rest of the crew is heading.
        botUnits = botUnits
            .OrderByDescending(unit => unit == ownPresident)
            .ThenBy(IsScreener)
            .ToArray();

        foreach (GameObject unit in botUnits)
        {
            Vector3 startWorld = GridSystem.GetNearestGridCell(unit);
            Vector2Int start = GetCell(unit);
            if (BotFrozenUnits.IsFrozen(unit, TeamIndex))
            {
                // Frozen means it never moves and never spends the ability slot; whether it shoots
                // back is a separate question this does not answer, so nothing here touches
                // Shooting. A frozen crew still returns fire, which is what a sandbox opponent
                // standing on a square is expected to do.
                plans[unit] = (false, new List<Vector3> { startWorld });
                crewDestinations.Add(start);
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
                if (!abilityRelocatesCaster)
                    crewDestinations.Add(start);
                continue;
            }

            occupiedCells.Remove(start);
            UnitData data = unit.GetComponent<Movement>().unitData;
            List<Vector2Int> unitTargets =
                unit == ownPresident
                    ? presidentTargets
                    : escortCrewTargets
                        ?? ChooseRoleTargets(
                            unit,
                            data,
                            start,
                            targets,
                            focusTarget,
                            crewDestinations,
                            occupiedCells
                        );
            List<Vector2Int> cellPath = BuildMovementPath(
                start,
                unitTargets,
                visibleEnemyCells,
                occupiedCells,
                data.moveDist,
                observedCells,
                lastObservedEpoch,
                random
            );
            if (
                (unitTargets == null || unitTargets.Count == 0)
                && cellPath.Count > 2
                && random.NextDouble() < ShortStepChance
            )
            {
                cellPath.RemoveAt(cellPath.Count - 1);
            }
            if (unit == ownPresident && cellPath.Count > 0)
                escortCrewTargets = BuildEscortScreenTargets(cellPath[^1], TeamIndex);
            // Movement speeds vary by unit, so reserving only matching timesteps is unsafe.
            // Reserve every cell in an earlier unit's route to prevent crossings and edge swaps.
            ReservePathCells(occupiedCells, cellPath);
            crewDestinations.Add(cellPath[^1]);

            plans[unit] = (false, cellPath.Select(GameLoop.gridCoordToWorld).ToList());
        }

        return plans;
    }

    private static bool IsScreener(GameObject unit)
    {
        return unit.GetComponent<Smoke>() != null;
    }

    /// <summary>
    /// Where a unit goes in Elimination once it knows where an enemy is: a firing position at the
    /// range its role and the crew's playstyle want, rather than walking into the enemy's square.
    /// A screener trails the crew instead, keeping its smoke in reach of everyone it covers.
    /// Objective modes keep the objective as the destination.
    /// </summary>
    private List<Vector2Int> ChooseRoleTargets(
        GameObject unit,
        UnitData data,
        Vector2Int start,
        List<Vector2Int> strategicTargets,
        Vector2Int? focusTarget,
        IReadOnlyList<Vector2Int> crewDestinations,
        ISet<Vector2Int> blockedCells
    )
    {
        if (gameLoop.Options.gameMode != GameMode.Elimination)
            return strategicTargets;

        if (IsScreener(unit) && crewDestinations.Count > 0)
        {
            List<Vector2Int> trail = BuildSupportTargets(
                crewDestinations,
                strategicTargets,
                blockedCells
            );
            if (trail.Count > 0)
                return trail;
        }

        if (strategicTargets.Count == 0)
            return strategicTargets;

        Vector2Int nearest = strategicTargets
            .OrderBy(cell => GridDistance(start, cell))
            .ThenBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .First();
        Vector2Int quarry =
            focusTarget.HasValue
            && GridDistance(start, focusTarget.Value) <= GridDistance(start, nearest) + 3
                ? focusTarget.Value
                : nearest;

        List<Vector2Int> firingPositions = BuildEngagementTargets(
            quarry,
            data.targetRange,
            PreferredEngageDistance(data),
            blockedCells
        );
        if (firingPositions.Count == 0)
            return strategicTargets;
        if (firingPositions.Contains(start) && random.NextDouble() > 0.2)
            return new List<Vector2Int> { start };
        return firingPositions;
    }

    private float PreferredEngageDistance(UnitData data)
    {
        if (data.targetRange < BrawlerRange)
            return 1f;
        float factor = EngageRangeFactor;
        if (data.unitClass == UnitClass.Controller || data.unitClass == UnitClass.Support)
            factor = Mathf.Max(factor, 0.85f);
        return Mathf.Clamp(data.targetRange * factor, 1.5f, data.targetRange);
    }

    /// <summary>
    /// The crew converges on one enemy rather than each unit chasing its own: the weakest visible
    /// enemy, or failing that the remembered one nearest the crew.
    /// </summary>
    private Vector2Int? ChooseFocusTarget(
        IReadOnlyList<GameObject> crew,
        IReadOnlyList<Vector2Int> strategicTargets
    )
    {
        if (gameLoop.Options.gameMode != GameMode.Elimination || strategicTargets.Count == 0)
            return null;

        List<Vector2Int> visible = strategicTargets.Where(visibleEnemyCells.Contains).ToList();
        if (visible.Count > 0)
        {
            float weakest = visible.Min(HealthFractionAt);
            List<Vector2Int> weakestCells = visible
                .Where(cell => HealthFractionAt(cell) <= weakest + 0.1f)
                .ToList();
            return weakestCells[random.Next(weakestCells.Count)];
        }

        Vector2 centroid = Centroid(crew.Select(GetCell));
        return strategicTargets
            .OrderBy(cell => Vector2.Distance(centroid, cell))
            .ThenBy(cell => cell.x)
            .ThenBy(cell => cell.y)
            .First();
    }

    private float HealthFractionAt(Vector2Int cell)
    {
        Health health = visibleEnemyUnits.TryGetValue(cell, out GameObject enemy)
            ? enemy.GetComponent<Health>()
            : null;
        return health != null && health.MaxHealth > 0f ? health.CurrentHealth / health.MaxHealth : 1f;
    }

    /// <summary>
    /// Open cells with a line of sight to <paramref name="enemyCell"/>, at about
    /// <paramref name="preferredDistance"/> from it and never beyond <paramref name="shotRange"/>.
    /// </summary>
    public static List<Vector2Int> BuildEngagementTargets(
        Vector2Int enemyCell,
        float shotRange,
        float preferredDistance,
        ISet<Vector2Int> blockedCells
    )
    {
        float reach = Mathf.Max(1f, shotRange) + 0.0001f;
        float near = Mathf.Max(0.5f, preferredDistance - 1f);
        float far = Mathf.Min(reach, preferredDistance + 0.5f);
        List<Vector2Int> cells = new();
        for (int column = 0; column < GridSystem.ColumnCount; column++)
        {
            for (int row = 0; row < GridSystem.RowCount; row++)
            {
                Vector2Int cell = new(column, row);
                float distance = Vector2.Distance(cell, enemyCell);
                if (
                    cell == enemyCell
                    || distance < near
                    || distance > far
                    || GameLoop.wallLayout.Contains(cell)
                    || (blockedCells != null && blockedCells.Contains(cell))
                    || !GridSystem.HasGridLineOfSight(cell, enemyCell)
                )
                {
                    continue;
                }
                cells.Add(cell);
            }
        }
        return cells;
    }

    /// <summary>
    /// Open cells just behind the crew — near its centre, and further from every known enemy than
    /// the crew itself is — so a screener stays in smoke reach of the units it protects.
    /// </summary>
    public static List<Vector2Int> BuildSupportTargets(
        IEnumerable<Vector2Int> crewCells,
        IReadOnlyCollection<Vector2Int> threatCells,
        ISet<Vector2Int> blockedCells
    )
    {
        List<Vector2Int> crew = (crewCells ?? Enumerable.Empty<Vector2Int>()).ToList();
        List<Vector2Int> cells = new();
        if (crew.Count == 0)
            return cells;

        Vector2 centroid = Centroid(crew);
        List<Vector2Int> threats = (threatCells ?? Array.Empty<Vector2Int>()).ToList();
        float crewThreatDistance =
            threats.Count == 0 ? 0f : threats.Min(threat => Vector2.Distance(centroid, threat));
        for (int column = 0; column < GridSystem.ColumnCount; column++)
        {
            for (int row = 0; row < GridSystem.RowCount; row++)
            {
                Vector2Int cell = new(column, row);
                float fromCentre = Vector2.Distance(cell, centroid);
                if (
                    fromCentre < 1f
                    || fromCentre > SupportTrailDistance
                    || GameLoop.wallLayout.Contains(cell)
                    || (blockedCells != null && blockedCells.Contains(cell))
                    || (
                        threats.Count > 0
                        && threats.Min(threat => Vector2.Distance(cell, threat))
                            < crewThreatDistance + 0.5f
                    )
                )
                {
                    continue;
                }
                cells.Add(cell);
            }
        }
        return cells;
    }

    private static Vector2 Centroid(IEnumerable<Vector2Int> cells)
    {
        Vector2 sum = Vector2.zero;
        int count = 0;
        foreach (Vector2Int cell in cells)
        {
            sum += cell;
            count++;
        }
        return count == 0 ? Vector2.zero : sum / count;
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
        bool ignoreFog = IgnoresFog;
        HashSet<Vector2Int> visibleCells = gameLoop.GetObservableCellsForTeam(
            TeamIndex,
            ignoreFog
        );
        List<BotEnemySighting> sightings = gameLoop.GetVisibleEnemySightingsForTeam(
            TeamIndex,
            visibleCells,
            ignoreFog
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

        enemyUnitsById.Clear();
        foreach (GameObject enemy in GameLoop.GetTeamUnits(GameLoop.GetEnemyTeamIndex(TeamIndex)))
        {
            if (enemy != null)
                enemyUnitsById[GetStableUnitId(enemy)] = enemy;
        }
        visibleEnemyUnits.Clear();
        foreach (BotEnemySighting sighting in sightings)
        {
            if (enemyUnitsById.TryGetValue(sighting.EnemyId, out GameObject enemy))
                visibleEnemyUnits[sighting.Cell] = enemy;
        }

        knowledge.Update(visibleCells, sightings);
    }

    private List<BotShooter> GetKnownEnemyShooters()
    {
        return knowledge
            .LastKnownCells.OrderBy(entry => entry.Key)
            .Select(entry => new BotShooter(
                entry.Value,
                enemyUnitsById.TryGetValue(entry.Key, out GameObject enemy)
                    ? ShotRangeOf(enemy)
                    : DefaultEnemyShotRange
            ))
            .ToList();
    }

    private static float ShotRangeOf(GameObject unit)
    {
        UnitData data = unit.GetComponent<Movement>()?.unitData;
        return unit.GetComponent<Shooting>() != null && data != null ? data.targetRange : 0f;
    }

    /// <summary>
    /// How much keeping enemy fire off this ally is worth: more the more wounded it is, and far
    /// more for an escorted president.
    /// </summary>
    private float ScreenWeightOf(GameObject ally)
    {
        Health health = ally.GetComponent<Health>();
        float missing =
            health != null && health.MaxHealth > 0f
                ? 1f - health.CurrentHealth / health.MaxHealth
                : 0f;
        float weight = 1f + 1.5f * Mathf.Clamp01(missing);
        return ally == gameLoop.GetPresident(TeamIndex) ? weight * 3f : weight;
    }

    private static bool IsWithin(Vector2Int from, Vector2Int to, float range)
    {
        return (to - from).sqrMagnitude <= range * range + 0.0001f;
    }

    /// <summary>
    /// The round has one ability slot. Every unit that could spend it proposes its best use with
    /// a value; the slot goes to the highest after a little noise, and stays unspent when nothing
    /// clears the playstyle's bar.
    /// </summary>
    private void TryChooseAbility(
        IReadOnlyList<GameObject> casters,
        IReadOnlyList<GameObject> crew,
        out GameObject selectedUnit,
        out Vector2Int target,
        out bool needsTarget
    )
    {
        selectedUnit = null;
        target = default;
        needsTarget = false;
        float bestValue = AbilityThreshold;
        List<BotShooter> knownEnemies = GetKnownEnemyShooters();

        foreach (GameObject unit in casters)
        {
            UnitData data = unit.GetComponent<Movement>()?.unitData;
            Ability ability = unit.GetComponent<Ability>();
            if (data == null || ability == null || unit.GetComponent<Unit>()?.CanUseAbility != true)
                continue;
            if (
                !TryEvaluateAbility(
                    unit,
                    data,
                    ability,
                    crew,
                    knownEnemies,
                    out Vector2Int optionTarget,
                    out bool optionNeedsTarget,
                    out float value
                )
            )
            {
                continue;
            }

            float noisyValue = value + (float)random.NextDouble() * AbilityValueNoise;
            if (noisyValue <= bestValue)
                continue;

            bestValue = noisyValue;
            selectedUnit = unit;
            target = optionTarget;
            needsTarget = optionNeedsTarget;
        }
    }

    private bool TryEvaluateAbility(
        GameObject unit,
        UnitData data,
        Ability ability,
        IReadOnlyList<GameObject> crew,
        IReadOnlyList<BotShooter> knownEnemies,
        out Vector2Int target,
        out bool needsTarget,
        out float value
    )
    {
        target = default;
        needsTarget = false;
        value = 0f;
        Vector2Int start = GetCell(unit);

        if (ability is Smoke)
        {
            if (!data.selectAbilitySquare || data.selectAbilityDirection)
                return false;
            List<BotShooter> allies = crew.Select(ally => new BotShooter(
                    GetCell(ally),
                    ShotRangeOf(ally),
                    ScreenWeightOf(ally)
                ))
                .ToList();
            if (
                !TryChooseSmokeCenter(
                    start,
                    data.abilitySquareRange,
                    allies,
                    knownEnemies,
                    visibleEnemyCells,
                    gameLoop.ActiveSmokeCells,
                    out target,
                    out float score,
                    random
                )
            )
            {
                return false;
            }
            needsTarget = true;
            value = score * SupportBias;
            return true;
        }

        if (visibleEnemyCells.Count == 0)
            return false;

        if (ability is ShieldStance)
            return TryEvaluateShield(start, data, out target, out needsTarget, out value);
        if (ability is Pogo)
            return TryEvaluatePogo(start, data, crew, out target, out needsTarget, out value);
        if (data.selectAbilityDirection)
            return TryEvaluateDirectional(start, data, out target, out needsTarget, out value);

        AbilityLineOfFire lineOfFire = ability.LineOfFire;
        if (!data.selectAbilitySquare)
        {
            bool isRecall = ability is PresidentialRecall;
            float reach = !isRecall && data.abilityRadius > 0f ? data.abilityRadius : data.targetRange;
            int caught = visibleEnemyCells.Count(cell =>
                IsWithin(start, cell, reach) && HasUsableLineOfFire(start, cell, lineOfFire)
            );
            if (caught == 0)
                return false;
            value = isRecall ? 3f : (1.5f + 1.2f * caught) * DamageBias;
            return true;
        }

        List<Vector2Int> aims = OrderAimCandidates(start, visibleEnemyCells, lineOfFire)
            .Where(cell =>
                GridDistance(start, cell) <= data.abilitySquareRange
                && (data.responseDistLine || !GameLoop.wallLayout.Contains(cell))
            )
            .ToList();
        if (aims.Count == 0)
            return false;

        needsTarget = true;
        if (data.responseDistLine || data.abilityRadius <= 0f)
        {
            target = aims[0];
            value = 2.5f * DamageBias;
            return true;
        }

        int bestCaught = 0;
        foreach (Vector2Int aim in aims)
        {
            int caught = visibleEnemyCells.Count(cell => IsWithin(aim, cell, data.abilityRadius));
            if (caught <= bestCaught)
                continue;
            bestCaught = caught;
            target = aim;
        }
        value = (1.5f + 1.2f * bestCaught) * DamageBias;
        return true;
    }

    private bool TryEvaluateDirectional(
        Vector2Int start,
        UnitData data,
        out Vector2Int target,
        out bool needsTarget,
        out float value
    )
    {
        target = default;
        needsTarget = false;
        value = 0f;
        int shotRange = Mathf.CeilToInt(data.targetRange);
        foreach (
            Vector2Int visibleTarget in visibleEnemyCells
                .OrderBy(cell => GridDistance(start, cell))
                .ThenBy(cell => cell.x)
                .ThenBy(cell => cell.y)
        )
        {
            Vector2Int delta = visibleTarget - start;
            int directionalDistance = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            if (directionalDistance > data.abilityFixedDistance + shotRange)
                continue;

            Vector2Int direction = new(Math.Sign(delta.x), Math.Sign(delta.y));
            Vector2Int directionTarget = start + direction;
            Vector2Int destination = GridSystem.GetDirectionalDestination(
                start,
                direction,
                data.abilityFixedDistance,
                GameLoop.wallLayout
            );
            if (
                direction == Vector2Int.zero
                || !GridSystem.IsCellInBounds(directionTarget)
                || destination == start
                || WouldAbandonHill(gameLoop.Options.gameMode, start, destination)
                || GridDistance(destination, visibleTarget) > shotRange
                || !GridSystem.HasGridLineOfSight(destination, visibleTarget)
            )
            {
                continue;
            }

            target = directionTarget;
            needsTarget = true;
            value = 2.5f * DamageBias;
            return true;
        }
        return false;
    }

    /// <summary>
    /// A shield is worth raising only toward enemies close enough to be shooting at the bearer
    /// this round, and is turned to face as many of them as one direction can.
    /// </summary>
    private bool TryEvaluateShield(
        Vector2Int start,
        UnitData data,
        out Vector2Int target,
        out bool needsTarget,
        out float value
    )
    {
        target = default;
        needsTarget = false;
        value = 0f;
        List<Vector2Int> shooters = visibleEnemyCells
            .Where(cell =>
                IsWithin(
                    cell,
                    start,
                    (visibleEnemyUnits.TryGetValue(cell, out GameObject enemy)
                        ? ShotRangeOf(enemy)
                        : DefaultEnemyShotRange) + SmokeThreatStepAllowance
                )
                && GridSystem.HasGridLineOfSight(cell, start)
            )
            .ToList();
        if (shooters.Count == 0)
            return false;

        int bestCovered = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                Vector2Int direction = new(dx, dy);
                if (direction == Vector2Int.zero || !GridSystem.IsCellInBounds(start + direction))
                    continue;
                Vector2 facing = ((Vector2)direction).normalized;
                int covered = shooters.Count(cell =>
                    Vector2.Dot(((Vector2)(cell - start)).normalized, facing) > 0.7f
                );
                if (covered <= bestCovered)
                    continue;
                bestCovered = covered;
                target = start + direction;
            }
        }
        if (bestCovered == 0)
            return false;

        needsTarget = true;
        value = (1f + 1.2f * bestCovered) * SupportBias;
        return true;
    }

    /// <summary>
    /// Pogo is a reposition, not an attack: it lands on open ground in shooting range of a
    /// visible enemy, preferably behind it for the backstab, and away from the rest of its crew.
    /// </summary>
    private bool TryEvaluatePogo(
        Vector2Int start,
        UnitData data,
        IReadOnlyList<GameObject> crew,
        out Vector2Int target,
        out bool needsTarget,
        out float value
    )
    {
        target = default;
        needsTarget = false;
        value = 0f;
        HashSet<Vector2Int> occupied = new(crew.Select(GetCell).Concat(visibleEnemyCells));
        bool alreadyHasShot = visibleEnemyCells.Any(cell =>
            IsWithin(start, cell, data.targetRange) && GridSystem.HasGridLineOfSight(start, cell)
        );
        float bestScore = float.NegativeInfinity;

        foreach (KeyValuePair<Vector2Int, GameObject> quarry in visibleEnemyUnits)
        {
            Vector2 enemyFacing = new(
                quarry.Value.transform.forward.x,
                quarry.Value.transform.forward.z
            );
            for (int column = 0; column < GridSystem.ColumnCount; column++)
            {
                for (int row = 0; row < GridSystem.RowCount; row++)
                {
                    Vector2Int landing = new(column, row);
                    float distance = Vector2.Distance(landing, quarry.Key);
                    if (
                        distance < 2f
                        || !IsWithin(landing, quarry.Key, data.targetRange)
                        || GridDistance(start, landing) > data.abilitySquareRange
                        || occupied.Contains(landing)
                        || GameLoop.wallLayout.Contains(landing)
                        || WouldAbandonHill(gameLoop.Options.gameMode, start, landing)
                        || !GridSystem.HasGridLineOfSight(landing, quarry.Key)
                    )
                    {
                        continue;
                    }

                    bool behind = Vector2.Dot(enemyFacing, landing - quarry.Key) < 0f;
                    int exposure = visibleEnemyUnits.Count(other =>
                        other.Key != quarry.Key
                        && IsWithin(other.Key, landing, ShotRangeOf(other.Value))
                        && GridSystem.HasGridLineOfSight(other.Key, landing)
                    );
                    float score =
                        (behind ? 1.5f : 0f)
                        - 0.6f * exposure
                        + (1f - HealthFractionAt(quarry.Key))
                        + (float)random.NextDouble() * 0.3f;
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    target = landing;
                }
            }
        }

        if (float.IsNegativeInfinity(bestScore))
            return false;

        needsTarget = true;
        value = (alreadyHasShot ? 0.5f : 1.5f) + Mathf.Max(0f, bestScore);
        return true;
    }

    /// <summary>
    /// Visible enemy cells in the order an aimed ability should try them.
    /// <para>
    /// A cell can be visible to the team and still sit behind a wall from the caster, because the
    /// team's sight is the union of every unit's, and aiming something that stops at a wall past
    /// one wastes the round's only ability. So a required line drops those cells outright.
    /// </para>
    /// <para>
    /// A preferred line only breaks ties between cells the same distance away. It is not allowed
    /// to reach past a nearer target for a clear shot at a further one, because what is being
    /// compared are two different aims rather than two targets: this planner measures range with
    /// <c>abilitySquareRange</c>, which is how far a player may aim, and an ability's effect can
    /// travel a good deal less than that — Breach may aim across the whole board and its rocket
    /// flies eight cells. Nearest-first is what keeps an aim inside that.
    /// </para>
    /// </summary>
    public static IEnumerable<Vector2Int> OrderAimCandidates(
        Vector2Int casterCell,
        IEnumerable<Vector2Int> enemyCells,
        AbilityLineOfFire lineOfFire
    )
    {
        return enemyCells
            .Where(cell => HasUsableLineOfFire(casterCell, cell, lineOfFire))
            .OrderBy(cell => GridDistance(casterCell, cell))
            .ThenByDescending(cell =>
                lineOfFire == AbilityLineOfFire.Preferred
                && GridSystem.HasGridLineOfSight(casterCell, cell)
            )
            .ThenBy(cell => cell.x)
            .ThenBy(cell => cell.y);
    }

    /// <summary>
    /// Whether an ability can do anything at all to <paramref name="target"/> from
    /// <paramref name="casterCell"/>. A preferred line still fires through a wall — it only scores
    /// worse, which is <see cref="OrderAimCandidates"/>'s job, not this one's.
    /// </summary>
    public static bool HasUsableLineOfFire(
        Vector2Int casterCell,
        Vector2Int target,
        AbilityLineOfFire lineOfFire
    )
    {
        return lineOfFire != AbilityLineOfFire.Required
            || GridSystem.HasGridLineOfSight(casterCell, target);
    }

    /// <summary>
    /// Selects a legal 3x3 Smoke center that cuts real incoming fire. A lane counts only when the
    /// enemy at one end could actually shoot the ally at the other — within its shot range plus a
    /// step — so a crew a board away never draws a screen. Each lane is worth the ally's weight;
    /// smoke that would also blind a shot an ally has right now on a visible enemy costs some back.
    /// Enemy positions are visible or fog-bounded last-known cells only. Centers within a quarter
    /// point of the best are picked between at random when <paramref name="random"/> is given.
    /// </summary>
    public static bool TryChooseSmokeCenter(
        Vector2Int casterCell,
        int castRange,
        IEnumerable<BotShooter> allies,
        IEnumerable<BotShooter> knownEnemies,
        IEnumerable<Vector2Int> currentlyVisibleEnemyCells,
        IEnumerable<Vector2Int> activeSmokeCells,
        out Vector2Int selectedCenter,
        out float selectedScore,
        System.Random random = null
    )
    {
        List<BotShooter> allyList = (allies ?? Enumerable.Empty<BotShooter>())
            .Where(ally => GridSystem.IsCellInBounds(ally.Cell))
            .ToList();
        List<BotShooter> enemyList = (knownEnemies ?? Enumerable.Empty<BotShooter>())
            .Where(enemy => GridSystem.IsCellInBounds(enemy.Cell))
            .ToList();
        List<Vector2Int> visibleEnemies = (
            currentlyVisibleEnemyCells ?? Enumerable.Empty<Vector2Int>()
        )
            .Where(GridSystem.IsCellInBounds)
            .Distinct()
            .ToList();
        HashSet<Vector2Int> existingSmoke = new(
            (activeSmokeCells ?? Enumerable.Empty<Vector2Int>()).Where(GridSystem.IsCellInBounds)
        );

        selectedCenter = default;
        selectedScore = 0f;
        if (!GridSystem.IsCellInBounds(casterCell) || allyList.Count == 0 || enemyList.Count == 0)
            return false;

        List<(Vector2Int enemy, Vector2Int ally, float weight)> threatLanes = new();
        foreach (BotShooter enemy in enemyList)
        {
            foreach (BotShooter ally in allyList)
            {
                if (
                    IsWithin(enemy.Cell, ally.Cell, enemy.ShotRange + SmokeThreatStepAllowance)
                    && IsOpenShotLine(enemy.Cell, ally.Cell, existingSmoke)
                )
                {
                    threatLanes.Add((enemy.Cell, ally.Cell, ally.Weight));
                }
            }
        }
        if (threatLanes.Count == 0)
            return false;

        List<(Vector2Int ally, Vector2Int enemy)> ownShots = new();
        foreach (Vector2Int enemyCell in visibleEnemies)
        {
            foreach (BotShooter ally in allyList)
            {
                if (
                    IsWithin(ally.Cell, enemyCell, ally.ShotRange)
                    && IsOpenShotLine(ally.Cell, enemyCell, existingSmoke)
                )
                {
                    ownShots.Add((ally.Cell, enemyCell));
                }
            }
        }

        List<(Vector2Int center, float score)> scored = new();
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

                HashSet<Vector2Int> footprint = new(
                    GridSystem.GetSquareFootprint(center, Smoke.FootprintRadius)
                );
                float score =
                    threatLanes
                        .Where(lane =>
                            GridSystem.DoesCellSegmentCrossCells(lane.enemy, lane.ally, footprint)
                        )
                        .Sum(lane => lane.weight)
                    - LostShotPenalty
                        * ownShots.Count(shot =>
                            GridSystem.DoesCellSegmentCrossCells(shot.ally, shot.enemy, footprint)
                        );
                if (score >= MinimumSmokeScore)
                    scored.Add((center, score));
            }
        }
        if (scored.Count == 0)
            return false;

        float best = scored.Max(option => option.score);
        List<(Vector2Int center, float score)> contenders = scored
            .Where(option => option.score >= best - (random == null ? 0f : 0.25f))
            .ToList();
        (selectedCenter, selectedScore) =
            random == null ? contenders[0] : contenders[random.Next(contenders.Count)];
        return true;
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
        IReadOnlyDictionary<Vector2Int, int> observationEpochs = null,
        System.Random random = null
    )
    {
        List<(List<Vector2Int> path, bool isVisibleEnemy)> routes = new();
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
            if (path != null)
                routes.Add((path, isVisibleEnemy));
        }

        List<Vector2Int> bestPath = null;
        bool stopShortOfBestTarget = false;
        if (routes.Count > 0)
        {
            (bestPath, stopShortOfBestTarget) = PickNearBest(
                routes,
                route => route.path.Count,
                random,
                RandomRouteSlack
            );
        }
        else
        {
            bestPath = FindExplorationPath(
                start,
                observedCells,
                observationEpochs,
                blockedCells,
                random
            );
        }

        if (bestPath == null || bestPath.Count == 0)
            return new List<Vector2Int> { start };

        int availableSteps = bestPath.Count - 1;
        if (stopShortOfBestTarget)
            availableSteps = Mathf.Max(0, availableSteps - 1);

        int steps = Mathf.Min(Mathf.Max(0, maxSteps), availableSteps);
        return bestPath.GetRange(0, steps + 1);
    }

    /// <summary>
    /// The first option with the lowest cost, or — given a <paramref name="random"/> — any option
    /// within <paramref name="slack"/> of it. Options must arrive in their deterministic order.
    /// </summary>
    private static T PickNearBest<T>(
        IReadOnlyList<T> options,
        Func<T, int> cost,
        System.Random random,
        int slack
    )
    {
        int best = options.Min(cost);
        List<T> contenders = options
            .Where(option => cost(option) <= best + (random == null ? 0 : slack))
            .ToList();
        return random == null ? contenders[0] : contenders[random.Next(contenders.Count)];
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
        ISet<Vector2Int> blockedCells,
        System.Random random
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
                .OrderBy(cell => cell.x)
                .ThenBy(cell => cell.y)
                .ToList();
        }

        List<(List<Vector2Int> path, int epoch)> routes = new();
        foreach (Vector2Int candidate in candidates)
        {
            List<Vector2Int> path = GridSystem.FindPath(start, candidate, blockedCells);
            if (path == null)
                continue;
            routes.Add(
                (
                    path,
                    observationEpochs != null && observationEpochs.TryGetValue(candidate, out int epoch)
                        ? epoch
                        : int.MinValue
                )
            );
        }
        if (routes.Count == 0)
            return null;

        if (patrolObservedCells)
        {
            int stalest = routes.Min(route => route.epoch);
            routes = routes.Where(route => route.epoch == stalest).ToList();
        }

        return PickNearBest(
                routes,
                route => preferLongestPath ? -route.path.Count : route.path.Count,
                random,
                RandomExplorationSlack
            )
            .path;
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
            : (unit != null ? unit.GetEntityId().GetRawData() : 0UL);
    }
}
